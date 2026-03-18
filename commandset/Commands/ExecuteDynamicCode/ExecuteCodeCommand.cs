using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.ExecuteDynamicCode
{
    /// <summary>
    /// 处理代码执行的命令类
    /// </summary>
    public class ExecuteCodeCommand : ExternalEventCommandBase
    {
        private ExecuteCodeEventHandler _handler => (ExecuteCodeEventHandler)Handler;

        public override string CommandName => "execute";

        public ExecuteCodeCommand(UIApplication uiApp)
            : base(new ExecuteCodeEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // 参数验证
                if (parameters == null || !parameters.ContainsKey("code"))
                {
                    return BuildFailureResult(
                        "Missing required parameter: 'code'",
                        "policy",
                        "ERR_INVALID_ARGUMENT",
                        false,
                        "Provide a non-empty 'code' parameter."
                    );
                }

                // 解析代码和参数
                string code = parameters["code"].Value<string>();
                JArray parametersArray = parameters["parameters"] as JArray;
                object[] executionParameters = parametersArray?.ToObject<object[]>() ?? Array.Empty<object>();
                string mode = parameters["mode"]?.Value<string>() ?? "read_only";

                // 设置执行参数
                _handler.SetExecutionParameters(code, executionParameters, mode);

                // 触发外部事件并等待完成
                if (RaiseAndWaitForCompletion(60000)) // 1分钟超时
                {
                    return _handler.ResultInfo ?? BuildFailureResult(
                        "代码执行失败: 未获取到执行结果",
                        "runtime",
                        "ERR_RUNTIME_EXCEPTION",
                        true,
                        "Retry execute once with the same snippet."
                    );
                }

                return BuildFailureResult(
                    "代码执行超时",
                    "runtime",
                    "ERR_EXECUTION_TIMEOUT",
                    true,
                    "Simplify the snippet, reduce traversal scope, then retry."
                );
            }
            catch (Exception ex)
            {
                return BuildFailureResult(
                    $"执行代码失败: {ex.Message}",
                    "runtime",
                    "ERR_RUNTIME_EXCEPTION",
                    true,
                    "Review the error and retry with a smaller reproducible snippet."
                );
            }
        }

        private static ExecutionResultInfo BuildFailureResult(
            string message,
            string type,
            string errorCode,
            bool retrySuggested,
            string suggestedFix)
        {
            return new ExecutionResultInfo
            {
                Success = false,
                Result = null,
                ErrorMessage = $"执行失败: {message}",
                Error = new ExecutionErrorInfo
                {
                    Type = type,
                    ErrorCode = errorCode,
                    RetrySuggested = retrySuggested,
                    SuggestedFix = suggestedFix
                },
                CompletionHint = "partial",
                NextBestAction = string.Equals(type, "policy", StringComparison.OrdinalIgnoreCase)
                    ? "respond_to_user"
                    : "retry_execute",
                RetryRecommended = retrySuggested
            };
        }
    }
}
