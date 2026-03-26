using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.ConnectRvtLookup;

namespace RevitMCPCommandSet.Commands.ConnectRvtLookup
{
    public sealed class ExpandMembersCommand : ExternalEventCommandBase
    {
        public override string CommandName => ConnectRvtLookupCommandNames.ExpandMembers;

        public ExpandMembersCommand(UIApplication uiApp)
            : base(new ConnectRvtLookupPlaceholderEventHandler(ConnectRvtLookupCommandNames.ExpandMembers), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            var request = parameters?.ToObject<ExpandMembersRequest>() ?? new ExpandMembersRequest();
            if (!request.Validate(out var errorMessage))
            {
                return QueryCommandResults.InvalidArgument<ExpandMembersResponse>(
                    errorMessage,
                    "Provide a non-empty 'objectHandle' and at least one member with both 'declaringTypeName' and 'memberName'.");
            }

            return QueryCommandResults.NotImplemented<ExpandMembersResponse>(CommandName);
        }
    }
}
