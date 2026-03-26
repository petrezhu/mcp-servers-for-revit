using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.ConnectRvtLookup;
using RevitMCPCommandSet.Services.ConnectRvtLookup;

namespace RevitMCPCommandSet.Commands.ConnectRvtLookup
{
    public sealed class ExpandMembersCommand : ExternalEventCommandBase
    {
        private static readonly object ExecutionLock = new();
        private ExpandMembersEventHandler ExpandMembersHandler => (ExpandMembersEventHandler) Handler;

        public override string CommandName => ConnectRvtLookupCommandNames.ExpandMembers;

        public ExpandMembersCommand(UIApplication uiApp)
            : base(new ExpandMembersEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (ExecutionLock)
            {
                var request = parameters?.ToObject<ExpandMembersRequest>() ?? new ExpandMembersRequest();
                if (!request.Validate(out var errorMessage))
                {
                    return QueryCommandResults.InvalidArgument<ExpandMembersResponse>(
                        errorMessage,
                        "Provide a non-empty 'objectHandle' and at least one member with both 'declaringTypeName' and 'memberName'.");
                }

                ExpandMembersHandler.SetRequest(request);

                if (RaiseAndWaitForCompletion(30000))
                {
                    return ExpandMembersHandler.Result ?? ConnectRvtLookupDiagnostics.TimeoutFailure<ExpandMembersResponse>(
                        nameof(Execute),
                        CommandName);
                }

                return ConnectRvtLookupRuntime.TimeoutFailure<ExpandMembersResponse>(CommandName);
            }
        }
    }
}
