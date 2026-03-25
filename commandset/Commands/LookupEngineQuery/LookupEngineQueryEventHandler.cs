using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Commands.LookupEngineQuery
{
    public class LookupEngineQueryEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private const int MaxMemberCount = 12;

        private string _query = string.Empty;
        private int _limit = 5;
        private bool _includeMembers = true;

        public LookupEngineQueryResult ResultInfo { get; private set; }

        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetQueryParameters(string query, int limit, bool includeMembers)
        {
            _query = query ?? string.Empty;
            _limit = Math.Min(10, Math.Max(1, limit));
            _includeMembers = includeMembers;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                string normalized = _query.Trim().ToLowerInvariant();
                var queryTokens = TokenizeQuery(normalized);

                var dbAssembly = typeof(Element).Assembly;
                var uiAssembly = typeof(UIApplication).Assembly;

                var allTypes = GetPublicTypesSafe(dbAssembly)
                    .Concat(GetPublicTypesSafe(uiAssembly))
                    .Where(type => type != null)
                    .GroupBy(type => type.FullName ?? type.Name, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList();

                var matched = allTypes
                    .Select(type => new
                    {
                        Type = type,
                        FullName = type.FullName ?? type.Name,
                        Name = type.Name,
                        NamespaceName = type.Namespace ?? string.Empty,
                        FullNameLower = (type.FullName ?? type.Name).ToLowerInvariant(),
                        NameLower = type.Name.ToLowerInvariant()
                    })
                    .Select(item => new
                    {
                        item.Type,
                        item.FullName,
                        item.Name,
                        item.NamespaceName,
                        PhraseMatched =
                            string.IsNullOrWhiteSpace(normalized) ||
                            item.FullNameLower.Contains(normalized) ||
                            item.NameLower.Contains(normalized),
                        TokenMatchCount = queryTokens.Count(token =>
                            item.FullNameLower.Contains(token) || item.NameLower.Contains(token))
                    })
                    .Where(item => item.PhraseMatched || item.TokenMatchCount > 0)
                    .OrderByDescending(item => item.PhraseMatched)
                    .ThenByDescending(item => item.TokenMatchCount)
                    .ThenBy(item => item.FullName.Length)
                    .ThenBy(item => item.FullName, StringComparer.Ordinal)
                    .Take(_limit)
                    .ToList();

                if (!LookupQueryProviderFactory.TryCreate(out var lookupProvider, out var initializationError))
                {
                    ResultInfo = new LookupEngineQueryResult
                    {
                        Query = _query,
                        MatchedCount = 0,
                        RuntimeSource = "lookup_engine",
                        ErrorMessage = initializationError,
                        AssemblyVersions = new LookupAssemblyVersions
                        {
                            RevitDb = dbAssembly.GetName().Version?.ToString(),
                            RevitUi = uiAssembly.GetName().Version?.ToString()
                        },
                        Engine = new LookupEngineRuntimeStatus
                        {
                            Requested = true,
                            Available = false,
                            Used = false,
                            UsesDescriptorsMap = false,
                            Diagnostics = new List<string> { initializationError }
                        },
                        Results = new List<LookupTypeResult>()
                    };
                    return;
                }

                bool engineAvailable = true;
                int engineMemberHitCount = 0;
                var providerDiagnostics = lookupProvider.Diagnostics.ToList();

                var results = new List<LookupTypeResult>(matched.Count);
                foreach (var item in matched)
                {
                    var members = new List<string>();
                    string memberSource = "lookup_engine";
                    string memberError = null;

                    if (_includeMembers)
                    {
                        if (lookupProvider.TryGetMembers(item.Type, MaxMemberCount, out var engineMembers, out var engineError))
                        {
                            members = engineMembers;
                            engineMemberHitCount++;
                        }
                        else
                        {
                            memberSource = "lookup_engine_error";
                            memberError = engineError;
                            providerDiagnostics.Add($"{item.FullName}: {engineError}");
                        }
                    }

                    results.Add(new LookupTypeResult
                    {
                        FullName = item.FullName,
                        Name = item.Name,
                        NamespaceName = item.NamespaceName,
                        Kind = ResolveKind(item.Type),
                        Members = members,
                        MemberSource = memberSource,
                        MemberError = memberError
                    });
                }

                bool engineUsed = engineMemberHitCount > 0;

                ResultInfo = new LookupEngineQueryResult
                {
                    Query = _query,
                    MatchedCount = results.Count,
                    RuntimeSource = "lookup_engine",
                    AssemblyVersions = new LookupAssemblyVersions
                    {
                        RevitDb = dbAssembly.GetName().Version?.ToString(),
                        RevitUi = uiAssembly.GetName().Version?.ToString(),
                        LookupEngine = lookupProvider.LookupEngineVersion,
                        RevitLookup = lookupProvider.RevitLookupVersion
                    },
                    Engine = new LookupEngineRuntimeStatus
                    {
                        Requested = true,
                        Available = engineAvailable,
                        Used = engineUsed,
                        UsesDescriptorsMap = lookupProvider.UsesDescriptorsMap,
                        Diagnostics = providerDiagnostics
                    },
                    Results = results
                };
            }
            catch (Exception ex)
            {
                ResultInfo = new LookupEngineQueryResult
                {
                    Query = _query,
                    MatchedCount = 0,
                    RuntimeSource = "lookup_engine",
                    ErrorMessage = ex.Message,
                    AssemblyVersions = new LookupAssemblyVersions(),
                    Engine = new LookupEngineRuntimeStatus
                    {
                        Requested = true,
                        Available = false,
                        Used = false,
                        UsesDescriptorsMap = false,
                        Diagnostics = new List<string> { ex.Message }
                    },
                    Results = new List<LookupTypeResult>()
                };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Lookup Engine Query Event Handler";
        }

        private static List<string> TokenizeQuery(string normalizedQuery)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return new List<string>();
            }

            return normalizedQuery
                .Split(new[] { ' ', '\t', '\r', '\n', ',', ';', '|', '/', '\\', '-', '_', '+', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .Where(token => token.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static IEnumerable<Type> GetPublicTypesSafe(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes().Where(type => type != null && type.IsPublic);
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null && type.IsPublic);
            }
        }

        private static string ResolveKind(Type type)
        {
            if (type.IsEnum)
            {
                return "enum";
            }

            if (type.IsInterface)
            {
                return "interface";
            }

            if (type.IsValueType)
            {
                return "struct";
            }

            return "class";
        }
    }

    public class LookupEngineQueryResult
    {
        [JsonProperty("query")]
        public string Query { get; set; }

        [JsonProperty("matchedCount")]
        public int MatchedCount { get; set; }

        [JsonProperty("runtimeSource")]
        public string RuntimeSource { get; set; }

        [JsonProperty("assemblyVersions")]
        public LookupAssemblyVersions AssemblyVersions { get; set; } = new LookupAssemblyVersions();

        [JsonProperty("engine")]
        public LookupEngineRuntimeStatus Engine { get; set; } = new LookupEngineRuntimeStatus();

        [JsonProperty("results")]
        public List<LookupTypeResult> Results { get; set; } = new List<LookupTypeResult>();

        [JsonProperty("errorMessage", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }
    }

    public class LookupAssemblyVersions
    {
        [JsonProperty("revitDb")]
        public string RevitDb { get; set; }

        [JsonProperty("revitUi")]
        public string RevitUi { get; set; }

        [JsonProperty("lookupEngine", NullValueHandling = NullValueHandling.Ignore)]
        public string LookupEngine { get; set; }

        [JsonProperty("revitLookup", NullValueHandling = NullValueHandling.Ignore)]
        public string RevitLookup { get; set; }
    }

    public class LookupEngineRuntimeStatus
    {
        [JsonProperty("requested")]
        public bool Requested { get; set; }

        [JsonProperty("available")]
        public bool Available { get; set; }

        [JsonProperty("used")]
        public bool Used { get; set; }

        [JsonProperty("usesDescriptorsMap")]
        public bool UsesDescriptorsMap { get; set; }

        [JsonProperty("diagnostics")]
        public List<string> Diagnostics { get; set; } = new List<string>();
    }

    public class LookupTypeResult
    {
        [JsonProperty("fullName")]
        public string FullName { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("namespaceName")]
        public string NamespaceName { get; set; }

        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("members")]
        public List<string> Members { get; set; } = new List<string>();

        [JsonProperty("memberSource")]
        public string MemberSource { get; set; }

        [JsonProperty("memberError", NullValueHandling = NullValueHandling.Ignore)]
        public string MemberError { get; set; }
    }
}
