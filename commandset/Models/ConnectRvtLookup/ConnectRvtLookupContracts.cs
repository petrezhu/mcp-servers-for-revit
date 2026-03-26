using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.ConnectRvtLookup;

public static class SelectionRootsSources
{
    public const string SelectionOrActiveView = "selection_or_active_view";
    public const string Selection = "selection";
    public const string ActiveView = "active_view";
}

public sealed class SelectionRootsRequest
{
    [JsonProperty("source")]
    public string Source { get; set; } = SelectionRootsSources.SelectionOrActiveView;

    [JsonProperty("limitGroups")]
    public int LimitGroups { get; set; } = 20;

    [JsonProperty("limitItemsPerGroup")]
    public int LimitItemsPerGroup { get; set; } = 20;

    [JsonProperty("tokenBudgetHint", NullValueHandling = NullValueHandling.Ignore)]
    public int? TokenBudgetHint { get; set; }

    public bool Validate(out string errorMessage)
    {
        if (!string.Equals(Source, SelectionRootsSources.SelectionOrActiveView, StringComparison.Ordinal) &&
            !string.Equals(Source, SelectionRootsSources.Selection, StringComparison.Ordinal) &&
            !string.Equals(Source, SelectionRootsSources.ActiveView, StringComparison.Ordinal))
        {
            errorMessage = $"Unsupported source: '{Source}'";
            return false;
        }

        errorMessage = null;
        return true;
    }
}

public sealed class ObjectMemberGroupsRequest
{
    [JsonProperty("objectHandle")]
    public string ObjectHandle { get; set; }

    [JsonProperty("limitGroups")]
    public int LimitGroups { get; set; } = 10;

    [JsonProperty("limitMembersPerGroup")]
    public int LimitMembersPerGroup { get; set; } = 12;

    [JsonProperty("tokenBudgetHint", NullValueHandling = NullValueHandling.Ignore)]
    public int? TokenBudgetHint { get; set; }

    public bool Validate(out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(ObjectHandle))
        {
            errorMessage = "Missing required parameter: 'objectHandle'";
            return false;
        }

        errorMessage = null;
        return true;
    }
}

public sealed class RequestedMember
{
    [JsonProperty("declaringTypeName")]
    public string DeclaringTypeName { get; set; }

    [JsonProperty("memberName")]
    public string MemberName { get; set; }
}

public sealed class ExpandMembersRequest
{
    [JsonProperty("objectHandle")]
    public string ObjectHandle { get; set; }

    [JsonProperty("members")]
    public List<RequestedMember> Members { get; set; } = new List<RequestedMember>();

    [JsonProperty("tokenBudgetHint", NullValueHandling = NullValueHandling.Ignore)]
    public int? TokenBudgetHint { get; set; }

    public bool Validate(out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(ObjectHandle))
        {
            errorMessage = "Missing required parameter: 'objectHandle'";
            return false;
        }

        if (Members == null || Members.Count == 0)
        {
            errorMessage = "Missing required parameter: 'members'";
            return false;
        }

        if (Members.Any(member =>
                string.IsNullOrWhiteSpace(member?.DeclaringTypeName) ||
                string.IsNullOrWhiteSpace(member.MemberName)))
        {
            errorMessage = "Each member requires both 'declaringTypeName' and 'memberName'";
            return false;
        }

        errorMessage = null;
        return true;
    }
}

public sealed class NavigateObjectRequest
{
    [JsonProperty("valueHandle")]
    public string ValueHandle { get; set; }

    [JsonProperty("limitGroups")]
    public int LimitGroups { get; set; } = 10;

    [JsonProperty("limitMembersPerGroup")]
    public int LimitMembersPerGroup { get; set; } = 12;

    [JsonProperty("tokenBudgetHint", NullValueHandling = NullValueHandling.Ignore)]
    public int? TokenBudgetHint { get; set; }

    public bool Validate(out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(ValueHandle))
        {
            errorMessage = "Missing required parameter: 'valueHandle'";
            return false;
        }

        errorMessage = null;
        return true;
    }
}

public sealed class QueryBudgetMetadata
{
    [JsonProperty("truncated")]
    public bool Truncated { get; set; }

    [JsonProperty("nextSuggestedAction", NullValueHandling = NullValueHandling.Ignore)]
    public string NextSuggestedAction { get; set; }

    [JsonProperty("truncationStage", NullValueHandling = NullValueHandling.Ignore)]
    public string TruncationStage { get; set; }
}

public sealed class RootItemResult
{
    [JsonProperty("objectHandle")]
    public string ObjectHandle { get; set; }

    [JsonProperty("elementId", NullValueHandling = NullValueHandling.Ignore)]
    public long? ElementId { get; set; }

    [JsonProperty("uniqueId", NullValueHandling = NullValueHandling.Ignore)]
    public string UniqueId { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("typeName")]
    public string TypeName { get; set; }

    [JsonProperty("category", NullValueHandling = NullValueHandling.Ignore)]
    public string Category { get; set; }
}

public sealed class RootGroupResult
{
    [JsonProperty("groupKey")]
    public string GroupKey { get; set; }

    [JsonProperty("count")]
    public int Count { get; set; }

    [JsonProperty("items")]
    public List<RootItemResult> Items { get; set; } = new List<RootItemResult>();
}

public sealed class SelectionRootsResponse
{
    [JsonProperty("source")]
    public string Source { get; set; }

    [JsonProperty("totalRootCount")]
    public int TotalRootCount { get; set; }

    [JsonProperty("truncated")]
    public bool Truncated { get; set; }

    [JsonProperty("groups")]
    public List<RootGroupResult> Groups { get; set; } = new List<RootGroupResult>();

    [JsonProperty("budget", NullValueHandling = NullValueHandling.Ignore)]
    public QueryBudgetMetadata Budget { get; set; }
}

public sealed class MemberGroupResult
{
    [JsonProperty("declaringTypeName")]
    public string DeclaringTypeName { get; set; }

    [JsonProperty("depth")]
    public int Depth { get; set; }

    [JsonProperty("memberCount")]
    public int MemberCount { get; set; }

    [JsonProperty("topMembers")]
    public List<string> TopMembers { get; set; } = new List<string>();

    [JsonProperty("hasMoreMembers")]
    public bool HasMoreMembers { get; set; }
}

public sealed class ObjectMemberGroupsResponse
{
    [JsonProperty("objectHandle")]
    public string ObjectHandle { get; set; }

    [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
    public string Title { get; set; }

    [JsonProperty("truncated")]
    public bool Truncated { get; set; }

    [JsonProperty("groups")]
    public List<MemberGroupResult> Groups { get; set; } = new List<MemberGroupResult>();

    [JsonProperty("budget", NullValueHandling = NullValueHandling.Ignore)]
    public QueryBudgetMetadata Budget { get; set; }
}

public sealed class ExpandedMemberResult
{
    [JsonProperty("declaringTypeName")]
    public string DeclaringTypeName { get; set; }

    [JsonProperty("memberName")]
    public string MemberName { get; set; }

    [JsonProperty("valueKind")]
    public string ValueKind { get; set; }

    [JsonProperty("displayValue", NullValueHandling = NullValueHandling.Ignore)]
    public string DisplayValue { get; set; }

    [JsonProperty("canNavigate")]
    public bool CanNavigate { get; set; }

    [JsonProperty("valueHandle", NullValueHandling = NullValueHandling.Ignore)]
    public string ValueHandle { get; set; }

    [JsonProperty("errorMessage", NullValueHandling = NullValueHandling.Ignore)]
    public string ErrorMessage { get; set; }

    [JsonProperty("usedFallback")]
    public bool UsedFallback { get; set; }
}

public sealed class ExpandMembersResponse
{
    [JsonProperty("objectHandle")]
    public string ObjectHandle { get; set; }

    [JsonProperty("expanded")]
    public List<ExpandedMemberResult> Expanded { get; set; } = new List<ExpandedMemberResult>();

    [JsonProperty("budget", NullValueHandling = NullValueHandling.Ignore)]
    public QueryBudgetMetadata Budget { get; set; }
}

public sealed class NavigateObjectResponse
{
    [JsonProperty("valueHandle")]
    public string ValueHandle { get; set; }

    [JsonProperty("objectHandle", NullValueHandling = NullValueHandling.Ignore)]
    public string ObjectHandle { get; set; }

    [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
    public string Title { get; set; }

    [JsonProperty("truncated")]
    public bool Truncated { get; set; }

    [JsonProperty("groups")]
    public List<MemberGroupResult> Groups { get; set; } = new List<MemberGroupResult>();

    [JsonProperty("budget", NullValueHandling = NullValueHandling.Ignore)]
    public QueryBudgetMetadata Budget { get; set; }
}

public sealed class QueryCommandResult<T>
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
    public T Data { get; set; }

    [JsonProperty("errorMessage", NullValueHandling = NullValueHandling.Ignore)]
    public string ErrorMessage { get; set; }

    [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
    public QueryErrorInfo Error { get; set; }

    [JsonProperty("completionHint")]
    public string CompletionHint { get; set; }

    [JsonProperty("nextBestAction")]
    public string NextBestAction { get; set; }

    [JsonProperty("retryRecommended")]
    public bool RetryRecommended { get; set; }
}

public static class QueryCommandResults
{
    public static QueryCommandResult<T> InvalidArgument<T>(string message, string suggestedFix)
    {
        return new QueryCommandResult<T>
        {
            Success = false,
            ErrorMessage = $"执行失败: {message}",
            Error = new QueryErrorInfo
            {
                Type = "policy",
                ErrorCode = ConnectRvtLookupErrorCodes.InvalidArgument,
                SuggestedFix = suggestedFix,
                RetrySuggested = false
            },
            CompletionHint = "partial",
            NextBestAction = "respond_to_user",
            RetryRecommended = false
        };
    }

    public static QueryCommandResult<T> NotImplemented<T>(string commandName)
    {
        return new QueryCommandResult<T>
        {
            Success = false,
            ErrorMessage = $"执行失败: 命令 {commandName} 的骨架已建立，但具体查询逻辑尚未实现",
            Error = new QueryErrorInfo
            {
                Type = "runtime",
                ErrorCode = ConnectRvtLookupErrorCodes.QueryNotImplemented,
                SuggestedFix = "Continue with the next connect-rvtLookup task to implement the command handler.",
                RetrySuggested = false
            },
            CompletionHint = "partial",
            NextBestAction = "implement_query_handler",
            RetryRecommended = false
        };
    }

    public static QueryCommandResult<T> RuntimeFailure<T>(string message, string errorCode, string suggestedFix, bool retrySuggested = false)
    {
        return new QueryCommandResult<T>
        {
            Success = false,
            ErrorMessage = $"执行失败: {message}",
            Error = new QueryErrorInfo
            {
                Type = "runtime",
                ErrorCode = errorCode,
                SuggestedFix = suggestedFix,
                RetrySuggested = retrySuggested
            },
            CompletionHint = "partial",
            NextBestAction = retrySuggested ? "retry_execute" : "respond_to_user",
            RetryRecommended = retrySuggested
        };
    }
}
