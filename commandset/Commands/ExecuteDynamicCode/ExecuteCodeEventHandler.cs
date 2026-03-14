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
            var doc = document;
            var uidoc = uiApp?.ActiveUIDocument;
            var app = uiApp;
            var uiapp = uiApp;
            var application = document?.Application;

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

            var lines = trimmed.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            if (lines.Count >= 2 && lines[0].StartsWith("```", StringComparison.Ordinal) &&
                lines[^1].Trim().Equals("```", StringComparison.Ordinal))
            {
                lines.RemoveAt(lines.Count - 1);
                lines.RemoveAt(0);
                return string.Join(Environment.NewLine, lines);
            }

            return code;
        }

        private static bool ContainsReturnStatement(string code)
        {
            return Regex.IsMatch(code, @"\breturn\b");
        }

        private bool IsReadOnlyMode()
        {
            return string.Equals(_executionMode, "read_only", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeMode(string mode)
        {
            return string.Equals(mode, "modify", StringComparison.OrdinalIgnoreCase)
                ? "modify"
                : "read_only";
        }

        private static bool ContainsMutationIndicators(string code)
        {
            var mutationPatterns = new[]
            {
                @"\bnew\s+Transaction\b",
                @"\bnew\s+SubTransaction\b",
                @"\bnew\s+TransactionGroup\b",
                @"\bTransactionManager\b",
                @"\.(Commit|RollBack|Assimilate)\s*\(",
                @"\bElementTransformUtils\b",
                @"\bdoc\s*\.\s*(Delete|Regenerate)\s*\(",
                @"\bdocument\s*\.\s*(Delete|Regenerate)\s*\(",
                @"\bParameter\s*\.\s*Set\s*\(",
                @"\.\s*Set\s*\("
            };

            return mutationPatterns.Any(pattern => Regex.IsMatch(code, pattern, RegexOptions.IgnoreCase));
        }

        private static string IndentBody(string code)
        {
            var lines = code.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            return string.Join(
                Environment.NewLine,
                lines.Select(line => string.IsNullOrWhiteSpace(line) ? string.Empty : $"            {line}")
            );
        }

        private static string FormatDiagnostic(Diagnostic diagnostic, int wrapperPrefixLineCount)
        {
            var lineSpan = diagnostic.Location.GetLineSpan();
            var rawLine = lineSpan.StartLinePosition.Line + 1;
            var userLine = rawLine - wrapperPrefixLineCount;
            var lineLabel = userLine > 0 ? userLine : rawLine;
            return $"Line {lineLabel}: {diagnostic.GetMessage()}";
        }

        private static string BuildCompatibilityHints(IEnumerable<Diagnostic> diagnostics)
        {
            var hints = new List<string>();
            var messages = diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage())
                .ToList();

            if (messages.Any(message => message.Contains("Not all code paths return a value", StringComparison.OrdinalIgnoreCase)))
            {
                hints.Add("End the snippet with `return null;` or return a serializable value.");
            }

            if (messages.Any(message => message.Contains("No overload for method 'Show' takes 1 arguments", StringComparison.OrdinalIgnoreCase)))
            {
                hints.Add("Use `TaskDialog.Show(\"Title\", \"Message\")` or create a `TaskDialog` instance and call `Show()` with no arguments.");
            }

            if (messages.Any(message => message.Contains("The name 'doc' does not exist", StringComparison.OrdinalIgnoreCase)))
            {
                hints.Add("Use `document` for the active Revit document. A compatibility alias `doc` is also injected by the latest bridge build.");
            }

            if (messages.Any(message => message.Contains("FilteredElementcollector", StringComparison.OrdinalIgnoreCase)))
            {
                hints.Add("The correct type name is `FilteredElementCollector`.");
            }

            if (messages.Any(message => message.Contains("Unexpected character", StringComparison.OrdinalIgnoreCase) ||
                                        message.Contains("Invalid token", StringComparison.OrdinalIgnoreCase)))
            {
                hints.Add("Submit only the method body. Do not include unmatched braces, partial class declarations, or malformed code fences.");
            }

            return string.Join("\n", hints.Distinct(StringComparer.Ordinal));
        }

        public string GetName()
        {
            return "执行AI代码";
        }
    }

    // 执行结果数据结构
    public class ExecutionResultInfo
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("result")]
        public string Result { get; set; }

        [JsonProperty("errorMessage")]
        public string ErrorMessage { get; set; } = string.Empty;
    }

    internal class PreparedUserCode
    {
        public string Body { get; set; } = string.Empty;

        public IReadOnlyList<string> Usings { get; set; } = Array.Empty<string>();
    }
}
