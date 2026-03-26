using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.ConnectRvtLookup;
using RevitMCPCommandSet.Services.ConnectRvtLookup;

namespace RevitMCPCommandSet.Commands.ConnectRvtLookup
{
    public sealed class SelectionRootsCommand : ExternalEventCommandBase
    {
        private static readonly object ExecutionLock = new();
        private SelectionRootsEventHandler SelectionRootsHandler => (SelectionRootsEventHandler) Handler;

        public override string CommandName => ConnectRvtLookupCommandNames.SelectionRoots;

        public SelectionRootsCommand(UIApplication uiApp)
            : base(new SelectionRootsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (ExecutionLock)
            {
                var request = parameters?.ToObject<SelectionRootsRequest>() ?? new SelectionRootsRequest();
                if (!request.Validate(out var errorMessage))
                {
                    return QueryCommandResults.InvalidArgument<SelectionRootsResponse>(
                        errorMessage,
                        "Use 'selection_or_active_view', 'selection', or 'active_view' for 'source'.");
                }

                SelectionRootsHandler.SetRequest(request);

                if (RaiseAndWaitForCompletion(30000))
                {
                    return SelectionRootsHandler.Result ?? ConnectRvtLookupDiagnostics.TimeoutFailure<SelectionRootsResponse>(
                        nameof(Execute),
                        CommandName);
                }

                return ConnectRvtLookupRuntime.TimeoutFailure<SelectionRootsResponse>(CommandName);
            }
        }
    }
}
