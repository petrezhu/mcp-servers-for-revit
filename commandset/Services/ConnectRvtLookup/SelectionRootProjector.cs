using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.ConnectRvtLookup;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public static class SelectionRootProjector
{
    public static RootItemResult Project(string documentKey, string contextKey, Element element)
    {
        if (string.IsNullOrWhiteSpace(documentKey)) throw new ArgumentException("documentKey is required", nameof(documentKey));
        if (element == null) throw new ArgumentNullException(nameof(element));

        return new RootItemResult
        {
            ObjectHandle = ConnectRvtLookupRuntime.RegisterObjectHandle(documentKey, element, contextKey),
            ElementId = ConnectRvtLookupRuntime.GetElementIdValue(element.Id),
            UniqueId = string.IsNullOrWhiteSpace(element.UniqueId) ? null : element.UniqueId,
            Title = ConnectRvtLookupRuntime.CreateElementTitle(element),
            TypeName = ConnectRvtLookupRuntime.CreateElementTypeName(element),
            Category = ConnectRvtLookupRuntime.CreateElementCategoryName(element)
        };
    }
}
