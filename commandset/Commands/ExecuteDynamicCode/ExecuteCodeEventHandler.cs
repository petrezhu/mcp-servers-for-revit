using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Commands.ExecuteDynamicCode
{
    /// <summary>
    /// 处理代码执行的外部事件处理器
    /// </summary>
    public class ExecuteCodeEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // 代码执行参数
        private string _generatedCode;
        private object[] _executionParameters;
        private string _executionMode = "read_only";

        // 执行结果信息
        public ExecutionResultInfo ResultInfo { get; private set; }

        // 状态同步对象
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // 设置要执行的代码和参数
        public void SetExecutionParameters(string code, object[] parameters = null, string mode = "read_only")
        {
            _generatedCode = code;
            _executionParameters = parameters ?? Array.Empty<object>();
            _executionMode = NormalizeMode(mode);
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        // 等待执行完成 - IWaitableExternalEventHandler接口实现
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                if (app?.ActiveUIDocument?.Document == null)
                {
                    throw new InvalidOperationException("No active Revit document is available. Open a project before calling exec.");
                }

                var doc = app.ActiveUIDocument.Document;
                ResultInfo = new ExecutionResultInfo();

                if (IsReadOnlyMode())
                {
                    var result = CompileAndExecuteCode(
                        code: _generatedCode,
                        doc: doc,
                        uiApp: app,
                        parameters: _executionParameters
                    );

                    ResultInfo.Success = true;
                    ResultInfo.Result = JsonConvert.SerializeObject(result);
                    return;
                }

                using (var transaction = new Transaction(doc, "执行AI代码"))
                {
                    transaction.Start();

                    var result = CompileAndExecuteCode(
                        code: _generatedCode,
                        doc: doc,
                        uiApp: app,
                        parameters: _executionParameters
                    );

                    transaction.Commit();

                    ResultInfo.Success = true;
                    ResultInfo.Result = JsonConvert.SerializeObject(result);
                }
            }
            catch (Exception ex)
            {
                ResultInfo.Success = false;
                ResultInfo.ErrorMessage = $"执行失败: {ex.Message}";
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private object CompileAndExecuteCode(string code, Document doc, UIApplication uiApp, object[] parameters)
        {
            var preparedCode = PrepareUserCode(code);

            if (IsReadOnlyMode() && ContainsMutationIndicators(preparedCode.Body))
            {
                throw new InvalidOperationException(
                    "read_only mode does not allow transaction or mutation-oriented Revit API calls. Ask the user to confirm model changes and retry with mode='modify'."
                );
            }

            var defaultUsings = new[]
            {
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "Autodesk.Revit.DB",
                "Autodesk.Revit.UI"
            };

            var usingBlock = string.Join(
                Environment.NewLine,
                defaultUsings
                    .Concat(preparedCode.Usings)
                    .Distinct(StringComparer.Ordinal)
                    .Select(@namespace => $"using {@namespace};")
            );

            var methodPrefix = $@"{usingBlock}

namespace AIGeneratedCode
{{
    public static class CodeExecutor
    {{
        public static object Execute(Document document, UIApplication uiApp, object[] parameters)
        {{
            // document: Autodesk.Revit.DB.Document
            // uiApp: Autodesk.Revit.UI.UIApplication
            // parameters: object[]
            // You may create local aliases such as:
            // var doc = document;
            // var uidoc = uiApp?.ActiveUIDocument;
            // var app = uiApp;
            // var uiapp = uiApp;
            // var application = document?.Application;

            // 用户代码入口
";

            var methodSuffix = @"
        }
    }
}";

            var wrappedCode = $"{methodPrefix}{preparedCode.Body}{methodSuffix}";
            var wrapperPrefixLineCount = methodPrefix.Split('\n').Length - 1;

            var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode);

            // 添加必要的程序集引用（引用所有已加载的程序集）
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            // 编译代码
            var compilation = CSharpCompilation.Create(
                "AIGeneratedCode",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);

                // 处理编译结果
                if (!result.Success)
                {
                    var errors = string.Join(
                        "\n",
                        result.Diagnostics
                            .Where(d => d.Severity == DiagnosticSeverity.Error)
                            .Select(d => FormatDiagnostic(d, wrapperPrefixLineCount))
                    );

                    var compatibilityHints = BuildCompatibilityHints(result.Diagnostics);
                    if (!string.IsNullOrWhiteSpace(compatibilityHints))
                    {
                        errors = $"{errors}\n\nHints:\n{compatibilityHints}";
                    }

                    throw new Exception($"代码编译错误:\n{errors}");
                }

                // 反射调用执行方法
                ms.Seek(0, SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());
                var executorType = assembly.GetType("AIGeneratedCode.CodeExecutor");
                var executeMethod = executorType.GetMethod("Execute");

                return executeMethod.Invoke(null, new object[] { doc, uiApp, parameters });
            }
        }

        private PreparedUserCode PrepareUserCode(string code)
        {
            var normalizedCode = StripCodeFence(code).Trim();
            var codeLines = normalizedCode
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .ToList();

            var extractedUsings = new List<string>();
            for (var index = 0; index < codeLines.Count;)
            {
                var trimmedLine = codeLines[index].Trim();
                if (trimmedLine.StartsWith("using ", StringComparison.Ordinal) &&
                    trimmedLine.EndsWith(";", StringComparison.Ordinal))
                {
                    var namespaceName = trimmedLine
                        .Substring("using ".Length)
                        .TrimEnd(';')
                        .Trim();
                    if (!string.IsNullOrWhiteSpace(namespaceName))
                    {
                        extractedUsings.Add(namespaceName);
                    }

                    codeLines.RemoveAt(index);
                    continue;
                }

                index++;
            }

            var body = string.Join(Environment.NewLine, codeLines).Trim();
            if (!ContainsReturnStatement(body))
            {
                if (!string.IsNullOrWhiteSpace(body))
                {
                    body += Environment.NewLine;
                }

                body += "return null;";
            }

            return new PreparedUserCode
            {
                Body = IndentBody(body),
                Usings = extractedUsings
            };
        }

        private static string StripCodeFence(string code)
        {
            var trimmed = code.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                return code;
            }

            var withoutOpeningFence = Regex.Replace(trimmed, "^```[a-zA-Z0-9_-]*\\s*", string.Empty);
            var withoutClosingFence = Regex.Replace(withoutOpeningFence, "\\s*```$", string.Empty);
            return withoutClosingFence.Trim();
        }

        private static bool ContainsReturnStatement(string body)
        {
            return Regex.IsMatch(body, @"\breturn\b");
        }

        private static string IndentBody(string body)
        {
            var lines = body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            return string.Join(Environment.NewLine, lines.Select(line => $"            {line}"));
        }

        private static string FormatDiagnostic(Diagnostic diagnostic, int wrapperPrefixLineCount)
        {
            var lineSpan = diagnostic.Location.GetLineSpan();
            var originalLine = Math.Max(1, lineSpan.StartLinePosition.Line - wrapperPrefixLineCount + 1);
            return $"Line {originalLine}: {diagnostic.GetMessage()}";
        }

        private static string BuildCompatibilityHints(IEnumerable<Diagnostic> diagnostics)
        {
            var hints = new List<string>();
            var messages = diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage())
                .ToList();

            if (messages.Any(message => message.IndexOf("Transaction", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                hints.Add("Use mode='modify' for code that creates transactions or changes the model.");
            }

            if (messages.Any(message => message.IndexOf("doc", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                hints.Add("Use the Execute method parameters directly, for example `document` and `uiApp`, or define your own local aliases.");
            }

            return string.Join("\n", hints.Distinct(StringComparer.Ordinal));
        }

        private bool IsReadOnlyMode()
        {
            return string.Equals(_executionMode, "read_only", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeMode(string mode)
        {
            return string.Equals(mode, "modify", StringComparison.OrdinalIgnoreCase) ? "modify" : "read_only";
        }

        private static bool ContainsMutationIndicators(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            string[] blockedTokens =
            {
                "new Transaction",
                "Transaction(",
                ".Start()",
                ".Commit()",
                ".Delete(",
                ".Create.",
                ".Set(",
                "Parameter.Set(",
                "ElementTransformUtils.",
                "FamilyInstance.New",
                "Wall.Create(",
                "Floor.Create(",
                "Document.Create"
            };

            return blockedTokens.Any(token => code.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public string GetName()
        {
            return "Execute Code Event Handler";
        }

        private sealed class PreparedUserCode
        {
            public string Body { get; set; }
            public List<string> Usings { get; set; }
        }
    }

    public class ExecutionResultInfo
    {
        public bool Success { get; set; }
        public string Result { get; set; }
        public string ErrorMessage { get; set; }
    }
}
