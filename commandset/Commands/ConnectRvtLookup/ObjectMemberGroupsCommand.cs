using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.ConnectRvtLookup;
using RevitMCPCommandSet.Services.ConnectRvtLookup;

namespace RevitMCPCommandSet.Commands.ConnectRvtLookup
{
    public sealed class ObjectMemberGroupsCommand : ExternalEventCommandBase
    {
        private static readonly object ExecutionLock = new();
        private ObjectMemberGroupsEventHandler ObjectMemberGroupsHandler => (ObjectMemberGroupsEventHandler) Handler;

        public override string CommandName => ConnectRvtLookupCommandNames.ObjectMemberGroups;

        public ObjectMemberGroupsCommand(UIApplication uiApp)
            : base(new ObjectMemberGroupsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (ExecutionLock)
            {
                var request = parameters?.ToObject<ObjectMemberGroupsRequest>() ?? new ObjectMemberGroupsRequest();
                if (!request.Validate(out var errorMessage))
                {
                    return QueryCommandResults.InvalidArgument<ObjectMemberGroupsResponse>(errorMessage, "Provide a non-empty 'objectHandle'.");
                }

                ObjectMemberGroupsHandler.SetRequest(request);

                if (RaiseAndWaitForCompletion(30000))
                {
                    return ObjectMemberGroupsHandler.Result ?? ConnectRvtLookupDiagnostics.TimeoutFailure<ObjectMemberGroupsResponse>(
                        nameof(Execute),
                        CommandName);
                }

                return ConnectRvtLookupRuntime.TimeoutFailure<ObjectMemberGroupsResponse>(CommandName);
            }
        }
    }
}
