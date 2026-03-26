using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.ConnectRvtLookup;

namespace RevitMCPCommandSet.Commands.ConnectRvtLookup
{
    public sealed class NavigateObjectCommand : ExternalEventCommandBase
    {
        public override string CommandName => ConnectRvtLookupCommandNames.NavigateObject;

        public NavigateObjectCommand(UIApplication uiApp)
            : base(new ConnectRvtLookupPlaceholderEventHandler(ConnectRvtLookupCommandNames.NavigateObject), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            var request = parameters?.ToObject<NavigateObjectRequest>() ?? new NavigateObjectRequest();
            if (!request.Validate(out var errorMessage))
            {
                return QueryCommandResults.InvalidArgument<NavigateObjectResponse>(errorMessage, "Provide a non-empty 'valueHandle'.");
            }

            return QueryCommandResults.NotImplemented<NavigateObjectResponse>(CommandName);
        }
    }
}
