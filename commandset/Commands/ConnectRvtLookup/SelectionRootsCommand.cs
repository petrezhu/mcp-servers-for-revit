using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.ConnectRvtLookup;

namespace RevitMCPCommandSet.Commands.ConnectRvtLookup
{
    public sealed class SelectionRootsCommand : ExternalEventCommandBase
    {
        public override string CommandName => ConnectRvtLookupCommandNames.SelectionRoots;

        public SelectionRootsCommand(UIApplication uiApp)
            : base(new ConnectRvtLookupPlaceholderEventHandler(ConnectRvtLookupCommandNames.SelectionRoots), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            parameters?.ToObject<SelectionRootsRequest>() ?? new SelectionRootsRequest();
            return QueryCommandResults.NotImplemented<SelectionRootsResponse>(CommandName);
        }
    }
}
