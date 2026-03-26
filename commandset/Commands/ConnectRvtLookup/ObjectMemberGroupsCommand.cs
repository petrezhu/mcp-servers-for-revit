using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.ConnectRvtLookup;

namespace RevitMCPCommandSet.Commands.ConnectRvtLookup
{
    public sealed class ObjectMemberGroupsCommand : ExternalEventCommandBase
    {
        public override string CommandName => ConnectRvtLookupCommandNames.ObjectMemberGroups;

        public ObjectMemberGroupsCommand(UIApplication uiApp)
            : base(new ConnectRvtLookupPlaceholderEventHandler(ConnectRvtLookupCommandNames.ObjectMemberGroups), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            var request = parameters?.ToObject<ObjectMemberGroupsRequest>() ?? new ObjectMemberGroupsRequest();
            if (!request.Validate(out var errorMessage))
            {
                return QueryCommandResults.InvalidArgument<ObjectMemberGroupsResponse>(errorMessage, "Provide a non-empty 'objectHandle'.");
            }

            return QueryCommandResults.NotImplemented<ObjectMemberGroupsResponse>(CommandName);
        }
    }
}
