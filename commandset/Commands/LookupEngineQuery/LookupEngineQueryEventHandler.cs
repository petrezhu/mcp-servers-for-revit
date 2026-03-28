using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using RevitMCPCommandSet.Services.ConnectRvtLookup;
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

                LookupEngineAssemblyLoader.EnsureLoaded();

                string bridgeInitializationError;
                var engineBridge = LookupEngineBridge.TryCreate(out bridgeInitializationError);
                bool engineAvailable = engineBridge != null;
                int engineMemberHitCount = 0;
                var bridgeWarnings = new List<string>();

                if (!string.IsNullOrWhiteSpace(bridgeInitializationError))
                {
                    bridgeWarnings.Add(bridgeInitializationError);
                }

                var results = new List<LookupTypeResult>(matched.Count);
                foreach (var item in matched)
                {
                    var members = new List<string>();
                    string memberSource = "none";
                    string memberError = null;

                    if (_includeMembers)
                    {
                        string engineError = null;
                        if (engineBridge != null &&
                            engineBridge.TryGetMembers(item.Type, MaxMemberCount, out var engineMembers, out engineError))
                        {
                            members = engineMembers;
                            memberSource = "lookup_engine";
                            engineMemberHitCount++;
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(engineError))
                            {
                                memberError = engineError;
                                bridgeWarnings.Add($"{item.FullName}: {engineError}");
                            }

                            members = GetReflectionMembers(item.Type, MaxMemberCount);
                            memberSource = "reflection_fallback";
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
                    RuntimeSource = engineUsed ? "lookup_engine" : "revit-runtime-reflection-fallback",
                    AssemblyVersions = new LookupAssemblyVersions
                    {
                        RevitDb = dbAssembly.GetName().Version?.ToString(),
                        RevitUi = uiAssembly.GetName().Version?.ToString(),
                        LookupEngine = engineBridge?.LookupEngineVersion,
                        RevitLookup = engineBridge?.RevitLookupVersion
                    },
                    Engine = new LookupEngineRuntimeStatus
                    {
                        Requested = true,
                        Available = engineAvailable,
                        Used = engineUsed,
                        UsesDescriptorsMap = engineBridge?.UsesDescriptorsMap ?? false,
                        Diagnostics = bridgeWarnings
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
                    RuntimeSource = "revit-runtime-reflection-fallback",
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

        private static List<string> GetReflectionMembers(Type type, int maxMembers)
        {
            return type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(member =>
                    member.MemberType == MemberTypes.Property ||
                    member.MemberType == MemberTypes.Method ||
                    member.MemberType == MemberTypes.Field)
                .Select(member => $"{member.MemberType}:{member.Name}")
                .Distinct(StringComparer.Ordinal)
                .Take(maxMembers)
                .ToList();
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

        private sealed class LookupEngineBridge
        {
            private readonly Type _decomposeOptionsType;
            private readonly MethodInfo _decomposeMembersMethod;
            private readonly MethodInfo _findDescriptorMethod;
            private readonly PropertyInfo _typeResolverProperty;

            public string LookupEngineVersion { get; }
            public string RevitLookupVersion { get; }
            public bool UsesDescriptorsMap => _findDescriptorMethod != null && _typeResolverProperty != null;

            private LookupEngineBridge(
                Type decomposeOptionsType,
                MethodInfo decomposeMembersMethod,
                MethodInfo findDescriptorMethod,
                PropertyInfo typeResolverProperty,
                string lookupEngineVersion,
                string revitLookupVersion)
            {
                _decomposeOptionsType = decomposeOptionsType;
                _decomposeMembersMethod = decomposeMembersMethod;
                _findDescriptorMethod = findDescriptorMethod;
                _typeResolverProperty = typeResolverProperty;
                LookupEngineVersion = lookupEngineVersion;
                RevitLookupVersion = revitLookupVersion;
            }

            public static LookupEngineBridge TryCreate(out string errorMessage)
            {
                errorMessage = null;

                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                var revitLookupAssembly = FindAssemblyBySimpleName(loadedAssemblies, "RevitLookup");
                if (revitLookupAssembly == null)
                {
                    errorMessage = "RevitLookup assembly is not loaded. Start RevitLookup first, then retry lookup_engine_query.";
                    return null;
                }

                var lookupEngineAssembly = FindAssemblyBySimpleName(loadedAssemblies, "LookupEngine");
                if (lookupEngineAssembly == null)
                {
                    errorMessage = "LookupEngine assembly is not loaded. Start RevitLookup first to load LookupEngine.";
                    return null;
                }

                var lookupComposerType = lookupEngineAssembly.GetType("LookupEngine.LookupComposer", false);
                var decomposeOptionsType = lookupEngineAssembly.GetType("LookupEngine.DecomposeOptions", false);

                if (lookupComposerType == null || decomposeOptionsType == null)
                {
                    errorMessage = "LookupEngine types were not found in loaded assembly.";
                    return null;
                }

                var decomposeMembersMethod = lookupComposerType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method =>
                    {
                        if (!string.Equals(method.Name, "DecomposeMembers", StringComparison.Ordinal))
                        {
                            return false;
                        }

                        var parameters = method.GetParameters();
                        return parameters.Length == 2 &&
                               parameters[0].ParameterType == typeof(object) &&
                               parameters[1].ParameterType == decomposeOptionsType;
                    });

                if (decomposeMembersMethod == null)
                {
                    errorMessage = "LookupComposer.DecomposeMembers(object, DecomposeOptions) is unavailable.";
                    return null;
                }

                Type descriptorsMapType = revitLookupAssembly.GetType("RevitLookup.Core.Decomposition.DescriptorsMap", false);
                MethodInfo findDescriptorMethod = descriptorsMapType?.GetMethod(
                    "FindDescriptor",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(object), typeof(Type) },
                    null
                );

                PropertyInfo typeResolverProperty = decomposeOptionsType.GetProperty("TypeResolver", BindingFlags.Public | BindingFlags.Instance);

                return new LookupEngineBridge(
                    decomposeOptionsType,
                    decomposeMembersMethod,
                    findDescriptorMethod,
                    typeResolverProperty,
                    lookupEngineAssembly.GetName().Version?.ToString(),
                    revitLookupAssembly?.GetName().Version?.ToString()
                );
            }

            public bool TryGetMembers(Type type, int maxMembers, out List<string> members, out string errorMessage)
            {
                members = new List<string>();
                errorMessage = null;

                try
                {
                    object options = Activator.CreateInstance(_decomposeOptionsType);
                    ConfigureOptions(options);

                    var rawMembers = _decomposeMembersMethod.Invoke(null, new object[] { type, options }) as IEnumerable;
                    if (rawMembers == null)
                    {
                        errorMessage = "LookupEngine returned non-enumerable member payload.";
                        return false;
                    }

                    foreach (var member in rawMembers)
                    {
                        string signature = FormatMemberSignature(member);
                        if (!string.IsNullOrWhiteSpace(signature))
                        {
                            members.Add(signature);
                        }
                    }

                    members = members
                        .Distinct(StringComparer.Ordinal)
                        .Take(maxMembers)
                        .ToList();

                    return true;
                }
                catch (Exception ex)
                {
                    errorMessage = Unwrap(ex).Message;
                    return false;
                }
            }

            private void ConfigureOptions(object options)
            {
                SetBool(options, "IncludeRoot", false);
                SetBool(options, "IncludeFields", true);
                SetBool(options, "IncludeEvents", false);
                SetBool(options, "IncludeUnsupported", true);
                SetBool(options, "IncludePrivateMembers", false);
                SetBool(options, "IncludeStaticMembers", true);
                SetBool(options, "EnableExtensions", true);
                SetBool(options, "EnableRedirection", true);

                if (_findDescriptorMethod == null || _typeResolverProperty == null)
                {
                    return;
                }

                if (!_typeResolverProperty.CanWrite)
                {
                    return;
                }

                var resolver = Delegate.CreateDelegate(_typeResolverProperty.PropertyType, _findDescriptorMethod);
                _typeResolverProperty.SetValue(options, resolver);
            }

            private static void SetBool(object target, string propertyName, bool value)
            {
                var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanWrite || property.PropertyType != typeof(bool))
                {
                    return;
                }

                property.SetValue(target, value);
            }

            private static string FormatMemberSignature(object member)
            {
                if (member == null)
                {
                    return null;
                }

                var memberType = member.GetType();
                string name = memberType.GetProperty("Name")?.GetValue(member)?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                string kind = ResolveMemberKind(memberType.GetProperty("MemberAttributes")?.GetValue(member));
                string valueType = GetNestedPropertyAsString(member, "Value", "TypeName");

                if (!string.IsNullOrWhiteSpace(valueType))
                {
                    return $"{kind}:{name}->{valueType}";
                }

                return $"{kind}:{name}";
            }

            private static string ResolveMemberKind(object attributes)
            {
                string text = attributes?.ToString() ?? string.Empty;
                if (text.IndexOf("Property", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Property";
                }

                if (text.IndexOf("Method", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Method";
                }

                if (text.IndexOf("Field", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Field";
                }

                if (text.IndexOf("Event", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Event";
                }

                if (text.IndexOf("Extension", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Extension";
                }

                return "Member";
            }

            private static string GetNestedPropertyAsString(object root, string firstPropertyName, string secondPropertyName)
            {
                var first = root.GetType().GetProperty(firstPropertyName, BindingFlags.Public | BindingFlags.Instance);
                if (first == null)
                {
                    return null;
                }

                var firstValue = first.GetValue(root);
                if (firstValue == null)
                {
                    return null;
                }

                var second = firstValue.GetType().GetProperty(secondPropertyName, BindingFlags.Public | BindingFlags.Instance);
                if (second == null)
                {
                    return null;
                }

                return second.GetValue(firstValue)?.ToString();
            }

            private static Exception Unwrap(Exception ex)
            {
                while (ex is TargetInvocationException tie && tie.InnerException != null)
                {
                    ex = tie.InnerException;
                }

                return ex;
            }

            private static Assembly FindAssemblyBySimpleName(IEnumerable<Assembly> assemblies, string simpleName)
            {
                return assemblies.FirstOrDefault(assembly =>
                    string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
            }
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
