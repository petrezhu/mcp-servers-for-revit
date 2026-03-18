using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.LookupEngineQuery
{
    public class LookupEngineQueryCommand : ExternalEventCommandBase
    {
        private LookupEngineQueryEventHandler _handler => (LookupEngineQueryEventHandler)Handler;

        public override string CommandName => "lookup_engine_query";

        public LookupEngineQueryCommand(UIApplication uiApp)
            : base(new LookupEngineQueryEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                string query = parameters?["query"]?.Value<string>() ?? string.Empty;
                int limit = parameters?["limit"]?.Value<int>() ?? 5;
                bool includeMembers = parameters?["includeMembers"]?.Value<bool>() ?? true;

                _handler.SetQueryParameters(query, limit, includeMembers);

                if (RaiseAndWaitForCompletion(60000))
                {
                    return _handler.ResultInfo;
                }

                throw new TimeoutException("lookup_engine_query execution timeout");
            }
            catch (Exception ex)
            {
                throw new Exception($"lookup_engine_query failed: {ex.Message}");
            }
        }
    }
}
