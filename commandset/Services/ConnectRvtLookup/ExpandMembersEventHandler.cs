using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.ConnectRvtLookup;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public sealed class ExpandMembersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
{
    private readonly ManualResetEvent _resetEvent = new(false);
    private ExpandMembersRequest _request = new();

    public QueryCommandResult<ExpandMembersResponse> Result { get; private set; }

    public void SetRequest(ExpandMembersRequest request)
    {
        _request = request ?? new ExpandMembersRequest();
        _resetEvent.Reset();
    }

    public void Execute(UIApplication app)
    {
        try
        {
            var uiDoc = app?.ActiveUIDocument;
            var document = uiDoc?.Document;
            if (document == null)
            {
                Result = ConnectRvtLookupDiagnostics.NoActiveDocumentFailure<ExpandMembersResponse>(
                    nameof(Execute),
                    ConnectRvtLookupCommandNames.ExpandMembers);
                return;
            }

            if (!ConnectRvtLookupRuntime.HandleStore.TryResolve(_request.ObjectHandle, out var entry))
            {
                Result = ConnectRvtLookupDiagnostics.InvalidHandleFailure<ExpandMembersResponse>(
                    nameof(Execute),
                    _request.ObjectHandle,
                    $"未找到对象句柄: {_request.ObjectHandle}",
                    "Refresh roots and request a new objectHandle.");
                return;
            }

            var documentKey = ConnectRvtLookupRuntime.CreateDocumentKey(document);
            if (!string.Equals(entry.DocumentKey, documentKey, StringComparison.Ordinal))
            {
                Result = ConnectRvtLookupDiagnostics.InvalidHandleFailure<ExpandMembersResponse>(
                    nameof(Execute),
                    _request.ObjectHandle,
                    $"对象句柄已不属于当前文档: {_request.ObjectHandle}",
                    "Refresh roots in the active Revit document and request a new objectHandle.");
                return;
            }

            if (!string.Equals(entry.HandleType, QueryHandleTypes.Object, StringComparison.Ordinal))
            {
                Result = ConnectRvtLookupDiagnostics.InvalidHandleFailure<ExpandMembersResponse>(
                    nameof(Execute),
                    _request.ObjectHandle,
                    $"句柄类型不匹配: {_request.ObjectHandle}",
                    "Use an objectHandle returned by selection_roots or navigate_object.");
                return;
            }

            var response = ConnectRvtLookupRuntime.GetOrCreateExpandMembersResponse(
                _request.ObjectHandle,
                entry.Value,
                documentKey,
                entry.ContextKey,
                _request.Members);

            var budgeted = ConnectRvtLookupRuntime.BudgetService.ApplyExpandMembersBudget(response, _request.TokenBudgetHint);
            Result = new QueryCommandResult<ExpandMembersResponse>
            {
                Success = true,
                Data = budgeted,
                CompletionHint = "answer_ready",
                NextBestAction = budgeted.Expanded.Any(item => item.CanNavigate) ? "navigate_object" : "respond_to_user",
                RetryRecommended = false
            };
        }
        catch (Exception ex)
        {
            Result = ConnectRvtLookupDiagnostics.RuntimeFailure<ExpandMembersResponse>(
                nameof(Execute),
                $"expand_members 执行失败: {ex.Message}",
                ConnectRvtLookupErrorCodes.MemberExpansionFailed,
                "Review the exception and retry with fewer members.",
                false,
                ex,
                ConnectRvtLookupDiagnostics.Context("handle", _request.ObjectHandle));
        }
        finally
        {
            _resetEvent.Set();
        }
    }

    public bool WaitForCompletion(int timeoutMilliseconds = 10000)
    {
        return _resetEvent.WaitOne(timeoutMilliseconds);
    }

    public string GetName()
    {
        return "Connect RevitLookup Expand Members";
    }
}
