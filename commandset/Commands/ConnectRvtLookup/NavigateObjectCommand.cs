using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.ConnectRvtLookup;
using RevitMCPCommandSet.Services.ConnectRvtLookup;

namespace RevitMCPCommandSet.Commands.ConnectRvtLookup
{
    public sealed class NavigateObjectCommand : ExternalEventCommandBase
    {
        private static readonly object ExecutionLock = new();
        private NavigateObjectEventHandler NavigateObjectHandler => (NavigateObjectEventHandler) Handler;

        public override string CommandName => ConnectRvtLookupCommandNames.NavigateObject;

        public NavigateObjectCommand(UIApplication uiApp)
            : base(new NavigateObjectEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (ExecutionLock)
            {
                var request = parameters?.ToObject<NavigateObjectRequest>() ?? new NavigateObjectRequest();
                if (!request.Validate(out var errorMessage))
                {
                    return QueryCommandResults.InvalidArgument<NavigateObjectResponse>(errorMessage, "Provide a non-empty 'valueHandle'.");
                }

                NavigateObjectHandler.SetRequest(request);

                if (RaiseAndWaitForCompletion(30000))
                {
                    return NavigateObjectHandler.Result ?? ConnectRvtLookupDiagnostics.TimeoutFailure<NavigateObjectResponse>(
                        nameof(Execute),
                        CommandName);
                }

                return ConnectRvtLookupRuntime.TimeoutFailure<NavigateObjectResponse>(CommandName);
            }
        }
    }
}
