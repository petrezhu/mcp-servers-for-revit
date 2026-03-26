using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.ConnectRvtLookup;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public sealed class SelectionRootsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
{
    private readonly ManualResetEvent _resetEvent = new(false);
    private SelectionRootsRequest _request = new();

    public QueryCommandResult<SelectionRootsResponse> Result { get; private set; }

    public void SetRequest(SelectionRootsRequest request)
    {
        _request = request ?? new SelectionRootsRequest();
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
                Result = ConnectRvtLookupDiagnostics.NoActiveDocumentFailure<SelectionRootsResponse>(
                    nameof(Execute),
                    ConnectRvtLookupCommandNames.SelectionRoots);
                return;
            }

            var selectedIds = uiDoc.Selection.GetElementIds();
            var bridgeResult = SelectionRootsBridge.Collect(document, document.ActiveView, selectedIds, _request.Source);
            var roots = bridgeResult.Roots;
            var documentKey = ConnectRvtLookupRuntime.CreateDocumentKey(document);
            var contextKey = ConnectRvtLookupRuntime.CreateSelectionContextKey(document, selectedIds);

            ConnectRvtLookupRuntime.HandleStore.InvalidateContext(contextKey);

            var grouped = roots
                .GroupBy(element => element.GetType().Name, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new RootGroupResult
                {
                    GroupKey = group.Key,
                    Count = group.Count(),
                    Items = group
                        .OrderBy(element => ConnectRvtLookupRuntime.GetElementIdValue(element.Id))
                        .Select(element => SelectionRootProjector.Project(documentKey, contextKey, element))
                        .ToList()
                })
                .ToList();

            var response = new SelectionRootsResponse
            {
                Source = bridgeResult.ActualSource,
                TotalRootCount = roots.Count,
                Truncated = false,
                Groups = grouped
            };

            var budgeted = ConnectRvtLookupRuntime.BudgetService.ApplySelectionRootsBudget(response, _request);
            Result = new QueryCommandResult<SelectionRootsResponse>
            {
                Success = true,
                Data = budgeted,
                CompletionHint = "answer_ready",
                NextBestAction = "object_member_groups",
                RetryRecommended = false
            };
        }
        catch (Exception ex)
        {
            Result = ConnectRvtLookupDiagnostics.RuntimeFailure<SelectionRootsResponse>(
                nameof(Execute),
                $"selection_roots 执行失败: {ex.Message}",
                ConnectRvtLookupErrorCodes.MemberExpansionFailed,
                "Review the exception and retry with a smaller selection.",
                false,
                ex);
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
        return "Connect RevitLookup Selection Roots";
    }
}
