using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.ConnectRvtLookup;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public sealed class NavigateObjectEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
{
    private readonly ManualResetEvent _resetEvent = new(false);
    private NavigateObjectRequest _request = new();

    public QueryCommandResult<NavigateObjectResponse> Result { get; private set; }

    public void SetRequest(NavigateObjectRequest request)
    {
        _request = request ?? new NavigateObjectRequest();
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
                Result = ConnectRvtLookupDiagnostics.NoActiveDocumentFailure<NavigateObjectResponse>(
                    nameof(Execute),
                    ConnectRvtLookupCommandNames.NavigateObject);
                return;
            }

            var documentKey = ConnectRvtLookupRuntime.CreateDocumentKey(document);
            if (!ConnectRvtLookupRuntime.TryCreateNavigateObjectResponse(documentKey, document, _request, out var response, out var errorResult))
            {
                Result = errorResult;
                return;
            }

            var budgeted = ConnectRvtLookupRuntime.BudgetService.ApplyNavigateObjectBudget(response, _request);
            Result = new QueryCommandResult<NavigateObjectResponse>
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
            Result = ConnectRvtLookupDiagnostics.RuntimeFailure<NavigateObjectResponse>(
                nameof(Execute),
                $"navigate_object 执行失败: {ex.Message}",
                ConnectRvtLookupErrorCodes.MemberExpansionFailed,
                "Review the exception and retry with a simpler value.",
                false,
                ex,
                ConnectRvtLookupDiagnostics.Context("handle", _request.ValueHandle));
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
        return "Connect RevitLookup Navigate Object";
    }
}
