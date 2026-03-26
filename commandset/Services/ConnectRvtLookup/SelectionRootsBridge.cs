using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.ConnectRvtLookup;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public sealed class SelectionRootsBridgeResult
{
    public string ActualSource { get; set; }
    public List<Element> Roots { get; set; } = new();
}

public static class SelectionRootsBridge
{
    public static string ResolveActualSource(string requestedSource, bool hasSelection)
    {
        if (string.Equals(requestedSource, SelectionRootsSources.Selection, StringComparison.Ordinal))
        {
            return SelectionRootsSources.Selection;
        }

        if (string.Equals(requestedSource, SelectionRootsSources.ActiveView, StringComparison.Ordinal))
        {
            return SelectionRootsSources.ActiveView;
        }

        return hasSelection
            ? SelectionRootsSources.Selection
            : SelectionRootsSources.ActiveView;
    }

    public static SelectionRootsBridgeResult Collect(
        Document document,
        View activeView,
        ICollection<ElementId> selectedIds,
        string requestedSource)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        var normalizedSelectedIds = selectedIds?
            .Where(id => id != null && ConnectRvtLookupRuntime.GetElementIdValue(id) > 0)
            .GroupBy(ConnectRvtLookupRuntime.GetElementIdValue)
            .Select(group => group.First())
            .ToList() ?? new List<ElementId>();

        var actualSource = ResolveActualSource(requestedSource, normalizedSelectedIds.Count > 0);
        var roots = string.Equals(actualSource, SelectionRootsSources.Selection, StringComparison.Ordinal)
            ? CollectSelectedElements(document, normalizedSelectedIds)
            : CollectActiveViewElements(document, activeView);

        return new SelectionRootsBridgeResult
        {
            ActualSource = actualSource,
            Roots = roots
        };
    }

    private static List<Element> CollectSelectedElements(Document document, IEnumerable<ElementId> selectedIds)
    {
        return selectedIds
            .Select(document.GetElement)
            .Where(element => element != null)
            .OrderBy(element => ConnectRvtLookupRuntime.GetElementIdValue(element.Id))
            .ToList();
    }

    private static List<Element> CollectActiveViewElements(Document document, View activeView)
    {
        if (activeView == null)
        {
            return new List<Element>();
        }

        return new FilteredElementCollector(document, activeView.Id)
            .WhereElementIsNotElementType()
            .ToElements()
            .OrderBy(element => ConnectRvtLookupRuntime.GetElementIdValue(element.Id))
            .ToList();
    }
}
