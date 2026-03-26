using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.ConnectRvtLookup;

public static class ConnectRvtLookupErrorCodes
{
    public const string InvalidArgument = "ERR_INVALID_ARGUMENT";
    public const string QueryNotImplemented = "ERR_QUERY_NOT_IMPLEMENTED";
    public const string InvalidHandle = "ERR_INVALID_HANDLE";
    public const string NoActiveDocument = "ERR_NO_ACTIVE_DOCUMENT";
    public const string QueryTimeout = "ERR_QUERY_TIMEOUT";
    public const string MemberExpansionFailed = "ERR_MEMBER_EXPANSION_FAILED";
}

public sealed class QueryErrorInfo
{
    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("errorCode")]
    public string ErrorCode { get; set; }

    [JsonProperty("suggestedFix")]
    public string SuggestedFix { get; set; }

    [JsonProperty("retrySuggested")]
    public bool RetrySuggested { get; set; }
}
