using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.ConnectRvtLookup;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public sealed class ObjectMemberGroupsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
{
    private readonly ManualResetEvent _resetEvent = new(false);
    private ObjectMemberGroupsRequest _request = new();

    public QueryCommandResult<ObjectMemberGroupsResponse> Result { get; private set; }

    public void SetRequest(ObjectMemberGroupsRequest request)
    {
        _request = request ?? new ObjectMemberGroupsRequest();
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
                Result = ConnectRvtLookupDiagnostics.NoActiveDocumentFailure<ObjectMemberGroupsResponse>(
                    nameof(Execute),
                    ConnectRvtLookupCommandNames.ObjectMemberGroups);
                return;
            }

            if (!ConnectRvtLookupRuntime.HandleStore.TryResolve(_request.ObjectHandle, out var entry))
            {
                Result = ConnectRvtLookupDiagnostics.InvalidHandleFailure<ObjectMemberGroupsResponse>(
                    nameof(Execute),
                    _request.ObjectHandle,
                    $"未找到对象句柄: {_request.ObjectHandle}",
                    "Refresh roots and request a new objectHandle.");
                return;
            }

            var activeDocumentKey = ConnectRvtLookupRuntime.CreateDocumentKey(document);
            if (!string.Equals(entry.DocumentKey, activeDocumentKey, StringComparison.Ordinal))
            {
                Result = ConnectRvtLookupDiagnostics.InvalidHandleFailure<ObjectMemberGroupsResponse>(
                    nameof(Execute),
                    _request.ObjectHandle,
                    $"对象句柄已不属于当前文档: {_request.ObjectHandle}",
                    "Refresh roots in the active Revit document and request a new objectHandle.");
                return;
            }

            if (!string.Equals(entry.HandleType, QueryHandleTypes.Object, StringComparison.Ordinal))
            {
                Result = ConnectRvtLookupDiagnostics.InvalidHandleFailure<ObjectMemberGroupsResponse>(
                    nameof(Execute),
                    _request.ObjectHandle,
                    $"句柄类型不匹配: {_request.ObjectHandle}",
                    "Use an objectHandle returned by selection_roots or navigate_object.");
                return;
            }

            var response = ConnectRvtLookupRuntime.GetOrCreateObjectMemberGroupsResponse(_request.ObjectHandle, entry.Value, document);
            var budgeted = ConnectRvtLookupRuntime.BudgetService.ApplyObjectMemberGroupsBudget(response, _request);
            Result = new QueryCommandResult<ObjectMemberGroupsResponse>
            {
                Success = true,
                Data = budgeted,
                CompletionHint = "answer_ready",
                NextBestAction = "expand_members",
                RetryRecommended = false
            };
        }
        catch (Exception ex)
        {
            Result = ConnectRvtLookupDiagnostics.RuntimeFailure<ObjectMemberGroupsResponse>(
                nameof(Execute),
                $"object_member_groups 执行失败: {ex.Message}",
                ConnectRvtLookupErrorCodes.MemberExpansionFailed,
                "Review the exception and retry with a smaller object scope.",
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
        return "Connect RevitLookup Object Member Groups";
    }
}
