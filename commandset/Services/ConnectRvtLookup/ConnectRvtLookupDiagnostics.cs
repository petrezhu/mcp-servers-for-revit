using System.Diagnostics;
using System.Globalization;
using RevitMCPCommandSet.Models.ConnectRvtLookup;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public static class ConnectRvtLookupDiagnostics
{
    private const string ModuleName = "connect-rvtLookup";
    private static readonly object SyncRoot = new();
    private static Action<string> _sink = WriteToTrace;

    public static string Context(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key) || value == null)
        {
            return null;
        }

        return $"{key}={Convert.ToString(value, CultureInfo.InvariantCulture)}";
    }

    public static void SetSinkForTesting(Action<string> sink)
    {
        lock (SyncRoot)
        {
            _sink = sink ?? WriteToTrace;
        }
    }

    public static void ResetSinkForTesting()
    {
        lock (SyncRoot)
        {
            _sink = WriteToTrace;
        }
    }

    public static void Info(string methodName, string message, params string[] contextParts)
    {
        Log("Info", methodName, message, null, contextParts);
    }

    public static void Warning(string methodName, string message, params string[] contextParts)
    {
        Log("Warning", methodName, message, null, contextParts);
    }

    public static void Error(string methodName, string message, Exception exception = null, params string[] contextParts)
    {
        Log("Error", methodName, message, exception, contextParts);
    }

    public static QueryCommandResult<T> NoActiveDocumentFailure<T>(string methodName, string commandName)
    {
        return RuntimeFailure<T>(
            methodName,
            "当前没有可用的活动文档",
            ConnectRvtLookupErrorCodes.NoActiveDocument,
            "Open a Revit model so ActiveUIDocument is available.",
            false,
            null,
            Context("command", commandName));
    }

    public static QueryCommandResult<T> InvalidHandleFailure<T>(string methodName, string handle, string message, string suggestedFix)
    {
        return RuntimeFailure<T>(
            methodName,
            message,
            ConnectRvtLookupErrorCodes.InvalidHandle,
            suggestedFix,
            false,
            null,
            Context("handle", handle));
    }

    public static QueryCommandResult<T> LookupBridgeUnavailableFailure<T>(string methodName, string message, string suggestedFix, params string[] contextParts)
    {
        return RuntimeFailure<T>(
            methodName,
            message,
            ConnectRvtLookupErrorCodes.LookupBridgeUnavailable,
            suggestedFix,
            false,
            null,
            contextParts);
    }

    public static QueryCommandResult<T> TimeoutFailure<T>(string methodName, string commandName)
    {
        return RuntimeFailure<T>(
            methodName,
            $"{commandName} 执行超时",
            ConnectRvtLookupErrorCodes.QueryTimeout,
            "Reduce traversal scope or retry once after Revit becomes idle.",
            true,
            null,
            Context("command", commandName));
    }

    public static QueryCommandResult<T> RuntimeFailure<T>(
        string methodName,
        string message,
        string errorCode,
        string suggestedFix,
        bool retrySuggested = false,
        Exception exception = null,
        params string[] contextParts)
    {
        Error(methodName, message, exception, contextParts);
        return QueryCommandResults.RuntimeFailure<T>(message, errorCode, suggestedFix, retrySuggested);
    }

    private static void Log(string level, string methodName, string message, Exception exception, params string[] contextParts)
    {
        var parts = contextParts?
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList() ?? new List<string>();

        if (exception != null)
        {
            parts.Add($"exception={exception.GetType().Name}: {exception.Message}");
        }

        var suffix = parts.Count == 0 ? string.Empty : $"，{string.Join("，", parts)}";
        var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level}/{ModuleName}: [{methodName}] {message}{suffix}";

        lock (SyncRoot)
        {
            _sink(logLine);
        }
    }

    private static void WriteToTrace(string message)
    {
        Debug.WriteLine(message);
        Trace.WriteLine(message);
    }
}
