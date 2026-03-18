using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Commands.ExecuteDynamicCode
{
    /// <summary>
    /// 处理代码执行的外部事件处理器
    /// </summary>
    public class ExecuteCodeEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private static readonly string[] AliasInjectionOrder = { "doc", "uidoc", "app", "uiapp" };
        private static readonly HashSet<string> AliasSymbols = new HashSet<string>(AliasInjectionOrder, StringComparer.Ordinal);
        private static readonly Dictionary<string, string> AliasMappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["doc"] = "var doc = document;",
            ["uidoc"] = "var uidoc = uiApp?.ActiveUIDocument;",
            ["app"] = "var app = uiApp;",
            ["uiapp"] = "var uiapp = uiApp;"
        };
        private static readonly Regex QuotedSymbolRegex = new Regex(
            "['‘’“\"](?<symbol>[A-Za-z_][A-Za-z0-9_]*)['‘’“\"]",
            RegexOptions.Compiled
        );
        private const string MethodSuffix = @"
        }
    }
}";

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
                ResultInfo = new ExecutionResultInfo();

                if (app?.ActiveUIDocument?.Document == null)
                {
                    throw new ExecutionFailureException(
                        "No active Revit document is available. Open a project before calling exec.",
                        new ExecutionErrorInfo
                        {
                            Type = "runtime",
                            ErrorCode = "ERR_NO_ACTIVE_DOCUMENT",
                            RetrySuggested = false,
                            SuggestedFix = "Open a Revit model so ActiveUIDocument is available."
                        }
                    );
                }

                var doc = app.ActiveUIDocument.Document;

                if (IsReadOnlyMode())
                {
                    var result = CompileAndExecuteCode(
                        code: _generatedCode,
                        doc: doc,
                        uiApp: app,
                        parameters: _executionParameters
                    );

                    SetSuccessResult(result);
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

                    SetSuccessResult(result);
                }
            }
            catch (ExecutionFailureException ex)
            {
                ResultInfo = BuildFailureResult(ex.Message, ex.Error);
            }
            catch (Exception ex)
            {
                ResultInfo = BuildFailureResult(
                    ex.Message,
                    new ExecutionErrorInfo
                    {
                        Type = "runtime",
                        ErrorCode = "ERR_RUNTIME_EXCEPTION",
                        RetrySuggested = false,
                        SuggestedFix = "Review the runtime exception message and update the snippet accordingly."
                    }
                );
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

            if (IsReadOnlyMode() && ContainsMutationIndicators(preparedCode.RawBody))
            {
                throw new ExecutionFailureException(
                    "read_only mode does not allow transaction or mutation-oriented Revit API calls. Ask the user to confirm model changes and retry with mode='modify'.",
                    new ExecutionErrorInfo
                    {
                        Type = "policy",
                        ErrorCode = "ERR_READONLY_MUTATION_BLOCKED",
                        RetrySuggested = true,
                        SuggestedFix = "Keep the snippet read-only, or ask for user confirmation and retry with mode='modify'."
                    }
                );
            }

            var firstCompilation = CompileSnippet(preparedCode, Array.Empty<string>());
            CompilationAttempt finalCompilation = firstCompilation;

            if (!firstCompilation.Success)
            {
                var declaredIdentifiers = FindDeclaredIdentifiers(preparedCode.RawBody);
                var aliasFallback = BuildAliasFallback(firstCompilation.Diagnostics, declaredIdentifiers);
                if (aliasFallback.Count > 0)
                {
                    finalCompilation = CompileSnippet(preparedCode, aliasFallback);
                }
            }

            if (!finalCompilation.Success)
            {
                throw CreateCompileFailure(finalCompilation.Diagnostics);
            }

            var assembly = Assembly.Load(finalCompilation.AssemblyBytes);
            var executorType = assembly.GetType("AIGeneratedCode.CodeExecutor");
            var executeMethod = executorType?.GetMethod("Execute");
            if (executeMethod == null)
            {
                throw new ExecutionFailureException(
                    "代码执行入口不存在。",
                    new ExecutionErrorInfo
                    {
                        Type = "runtime",
                        ErrorCode = "ERR_RUNTIME_EXCEPTION",
                        RetrySuggested = false,
                        SuggestedFix = "Retry with a simple snippet to verify bridge integrity."
                    }
                );
            }

            return executeMethod.Invoke(null, new object[] { doc, uiApp, parameters });
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
                RawBody = body,
                IndentedBody = IndentBody(body),
                Usings = extractedUsings
            };
        }

        private CompilationAttempt CompileSnippet(PreparedUserCode preparedCode, IReadOnlyCollection<string> aliasLines)
        {
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

            var methodPrefix = BuildMethodPrefix(usingBlock, aliasLines);
            var wrappedCode = $"{methodPrefix}{preparedCode.IndentedBody}{MethodSuffix}";
            var wrapperPrefixLineCount = methodPrefix.Split('\n').Length - 1;
            var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode);

            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
                .Cast<MetadataReference>()
                .ToList();

            var compilation = CSharpCompilation.Create(
                "AIGeneratedCode",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            using (var memoryStream = new MemoryStream())
            {
                var emitResult = compilation.Emit(memoryStream);
                var errorDiagnostics = emitResult.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => ToExecutionDiagnostic(diagnostic, wrapperPrefixLineCount))
                    .ToList();

                if (!emitResult.Success)
                {
                    return new CompilationAttempt
                    {
                        Success = false,
                        Diagnostics = errorDiagnostics
                    };
                }

                return new CompilationAttempt
                {
                    Success = true,
                    AssemblyBytes = memoryStream.ToArray(),
                    Diagnostics = errorDiagnostics
                };
            }
        }

        private static string BuildMethodPrefix(string usingBlock, IReadOnlyCollection<string> aliasLines)
        {
            var aliasBlock = string.Empty;
            if (aliasLines != null && aliasLines.Count > 0)
            {
                aliasBlock = "            // auto-injected aliases for compatibility fallback" + Environment.NewLine;
                aliasBlock += string.Join(
                    Environment.NewLine,
                    aliasLines.Select(alias => $"            {alias}")
                );
                aliasBlock += Environment.NewLine + Environment.NewLine;
            }

            return $@"{usingBlock}

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

{aliasBlock}            // 用户代码入口
";
        }

        private static ExecutionFailureException CreateCompileFailure(IReadOnlyCollection<ExecutionDiagnosticInfo> diagnostics)
        {
            var errors = diagnostics.Count > 0
                ? string.Join("\n", diagnostics.Select(FormatDiagnostic))
                : "Line 1: Unknown compile error";
            var compatibilityHints = BuildCompatibilityHints(diagnostics);
            if (!string.IsNullOrWhiteSpace(compatibilityHints))
            {
                errors = $"{errors}\n\nHints:\n{compatibilityHints}";
            }

            var errorCode = DetermineCompileErrorCode(diagnostics);
            return new ExecutionFailureException(
                $"代码编译错误:\n{errors}",
                new ExecutionErrorInfo
                {
                    Type = "compile",
                    ErrorCode = errorCode,
                    Diagnostics = diagnostics.ToList(),
                    RetrySuggested = true,
                    SuggestedFix = BuildCompileSuggestedFix(errorCode)
                }
            );
        }

        private static string DetermineCompileErrorCode(IReadOnlyCollection<ExecutionDiagnosticInfo> diagnostics)
        {
            if (diagnostics.Any(diagnostic => string.Equals(diagnostic.Id, "CS0103", StringComparison.OrdinalIgnoreCase)))
            {
                return "ERR_SYMBOL_NOT_FOUND";
            }

            if (diagnostics.Any(diagnostic =>
                    string.Equals(diagnostic.Id, "CS0128", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(diagnostic.Id, "CS0136", StringComparison.OrdinalIgnoreCase)))
            {
                return "ERR_DUPLICATE_LOCAL";
            }

            if (diagnostics.Any(IsElementIdIntegerValueUnavailable))
            {
                return "ERR_ELEMENTID_MEMBER_UNAVAILABLE";
            }

            return "ERR_COMPILE_FAILED";
        }

        private static string BuildCompileSuggestedFix(string errorCode)
        {
            switch (errorCode)
            {
                case "ERR_SYMBOL_NOT_FOUND":
                    return "Use `document` and `uiApp`, or declare aliases such as `var doc = document;`.";
                case "ERR_DUPLICATE_LOCAL":
                    return "Remove duplicate local alias declarations and keep only one definition.";
                case "ERR_ELEMENTID_MEMBER_UNAVAILABLE":
                    return "Avoid `ElementId.IntegerValue`; prefer `Id.ToString()`, reflection-based access, or `Value` on newer versions.";
                default:
                    return "Review compile diagnostics and retry with a minimal snippet.";
            }
        }

        private static bool IsElementIdIntegerValueUnavailable(ExecutionDiagnosticInfo diagnostic)
        {
            if (diagnostic == null || string.IsNullOrWhiteSpace(diagnostic.Message))
            {
                return false;
            }

            return diagnostic.Message.IndexOf("ElementId", StringComparison.OrdinalIgnoreCase) >= 0
                   && diagnostic.Message.IndexOf("IntegerValue", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ExecutionDiagnosticInfo ToExecutionDiagnostic(Diagnostic diagnostic, int wrapperPrefixLineCount)
        {
            var lineSpan = diagnostic.Location.GetLineSpan();
            var originalLine = Math.Max(1, lineSpan.StartLinePosition.Line - wrapperPrefixLineCount + 1);
            var originalColumn = Math.Max(1, lineSpan.StartLinePosition.Character + 1);

            return new ExecutionDiagnosticInfo
            {
                Id = diagnostic.Id,
                Line = originalLine,
                Column = originalColumn,
                Symbol = ExtractSymbolFromMessage(diagnostic.GetMessage()),
                Message = diagnostic.GetMessage()
            };
        }

        private static string ExtractSymbolFromMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var match = QuotedSymbolRegex.Match(message);
            if (!match.Success)
            {
                return null;
            }

            return match.Groups["symbol"].Value;
        }

        private static List<string> BuildAliasFallback(
            IReadOnlyCollection<ExecutionDiagnosticInfo> diagnostics,
            ISet<string> declaredIdentifiers)
        {
            var missingAliasSymbols = diagnostics
                .Where(diagnostic => string.Equals(diagnostic.Id, "CS0103", StringComparison.OrdinalIgnoreCase))
                .Select(diagnostic => diagnostic.Symbol)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Distinct(StringComparer.Ordinal)
                .Where(symbol => AliasSymbols.Contains(symbol))
                .Where(symbol => !declaredIdentifiers.Contains(symbol))
                .ToHashSet(StringComparer.Ordinal);

            var aliases = new List<string>();
            foreach (var symbol in AliasInjectionOrder)
            {
                if (!missingAliasSymbols.Contains(symbol))
                {
                    continue;
                }

                aliases.Add(AliasMappings[symbol]);
            }

            return aliases;
        }

        private static ISet<string> FindDeclaredIdentifiers(string codeBody)
        {
            var wrapped = $@"class AliasScan
{{
    void Scan()
    {{
{codeBody}
    }}
}}";

            var syntaxTree = CSharpSyntaxTree.ParseText(wrapped);
            var root = syntaxTree.GetRoot();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);

            foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                identifiers.Add(declarator.Identifier.ValueText);
            }

            foreach (var parameter in root.DescendantNodes().OfType<ParameterSyntax>())
            {
                identifiers.Add(parameter.Identifier.ValueText);
            }

            foreach (var forEach in root.DescendantNodes().OfType<ForEachStatementSyntax>())
            {
                identifiers.Add(forEach.Identifier.ValueText);
            }

            foreach (var catchDeclaration in root.DescendantNodes().OfType<CatchDeclarationSyntax>())
            {
                if (catchDeclaration.Identifier != default)
                {
                    identifiers.Add(catchDeclaration.Identifier.ValueText);
                }
            }

            return identifiers;
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

        private static string FormatDiagnostic(ExecutionDiagnosticInfo diagnostic)
        {
            return $"Line {diagnostic.Line}: {diagnostic.Message}";
        }

        private static string BuildCompatibilityHints(IEnumerable<ExecutionDiagnosticInfo> diagnostics)
        {
            var hints = new List<string>();
            var messages = diagnostics
                .Select(d => d.Message ?? string.Empty)
                .ToList();

            if (messages.Any(message => message.IndexOf("Transaction", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                hints.Add("Use mode='modify' for code that creates transactions or changes the model.");
            }

            if (messages.Any(message => message.IndexOf("doc", StringComparison.OrdinalIgnoreCase) >= 0) ||
                messages.Any(message => message.IndexOf("uidoc", StringComparison.OrdinalIgnoreCase) >= 0) ||
                messages.Any(message => message.IndexOf("uiapp", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                hints.Add("Use the Execute method parameters directly, for example `document` and `uiApp`, or define your own local aliases.");
            }

            if (messages.Any(message => message.IndexOf("IntegerValue", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                hints.Add("ElementId numeric APIs vary by Revit version. Prefer `Id.ToString()` or check for `Value`/`IntegerValue` via reflection.");
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

        private static string DetermineFailureNextAction(ExecutionErrorInfo error)
        {
            if (error == null)
            {
                return "retry_execute";
            }

            if (string.Equals(error.Type, "policy", StringComparison.OrdinalIgnoreCase))
            {
                return "respond_to_user";
            }

            if (string.Equals(error.ErrorCode, "ERR_NO_ACTIVE_DOCUMENT", StringComparison.OrdinalIgnoreCase))
            {
                return "respond_to_user";
            }

            if (string.Equals(error.ErrorCode, "ERR_ELEMENTID_MEMBER_UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
            {
                return "call_search";
            }

            return "retry_execute";
        }

        private void SetSuccessResult(object result)
        {
            ResultInfo.Success = true;
            ResultInfo.Result = JsonConvert.SerializeObject(result);
            ResultInfo.ErrorMessage = null;
            ResultInfo.Error = null;
            ResultInfo.CompletionHint = "answer_ready";
            ResultInfo.NextBestAction = "respond_to_user";
            ResultInfo.RetryRecommended = false;
        }

        private static ExecutionResultInfo BuildFailureResult(string message, ExecutionErrorInfo error)
        {
            return new ExecutionResultInfo
            {
                Success = false,
                Result = null,
                ErrorMessage = $"执行失败: {message}",
                Error = error,
                CompletionHint = "partial",
                NextBestAction = DetermineFailureNextAction(error),
                RetryRecommended = error?.RetrySuggested ?? false
            };
        }

        public string GetName()
        {
            return "Execute Code Event Handler";
        }

        private sealed class PreparedUserCode
        {
            public string RawBody { get; set; }
            public string IndentedBody { get; set; }
            public List<string> Usings { get; set; }
        }

        private sealed class CompilationAttempt
        {
            public bool Success { get; set; }
            public byte[] AssemblyBytes { get; set; }
            public List<ExecutionDiagnosticInfo> Diagnostics { get; set; } = new List<ExecutionDiagnosticInfo>();
        }
    }

    public class ExecutionResultInfo
    {
        public bool Success { get; set; }
        public string Result { get; set; }
        public string ErrorMessage { get; set; }
        public ExecutionErrorInfo Error { get; set; }
        public string CompletionHint { get; set; }
        public string NextBestAction { get; set; }
        public bool RetryRecommended { get; set; }
    }

    public sealed class ExecutionErrorInfo
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("errorCode")]
        public string ErrorCode { get; set; }

        [JsonProperty("diagnostics")]
        public List<ExecutionDiagnosticInfo> Diagnostics { get; set; } = new List<ExecutionDiagnosticInfo>();

        [JsonProperty("retrySuggested")]
        public bool RetrySuggested { get; set; }

        [JsonProperty("suggestedFix")]
        public string SuggestedFix { get; set; }
    }

    public sealed class ExecutionDiagnosticInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("line")]
        public int Line { get; set; }

        [JsonProperty("column")]
        public int Column { get; set; }

        [JsonProperty("symbol", NullValueHandling = NullValueHandling.Ignore)]
        public string Symbol { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public sealed class ExecutionFailureException : Exception
    {
        public ExecutionErrorInfo Error { get; }

        public ExecutionFailureException(string message, ExecutionErrorInfo error)
            : base(message)
        {
            Error = error ?? new ExecutionErrorInfo
            {
                Type = "runtime",
                ErrorCode = "ERR_RUNTIME_EXCEPTION",
                RetrySuggested = false,
                SuggestedFix = "Review the error and retry."
            };
        }
    }
}
