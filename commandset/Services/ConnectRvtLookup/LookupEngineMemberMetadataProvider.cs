using Autodesk.Revit.DB;
using System.Collections;
using System.Reflection;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public sealed class LookupMemberMetadata
{
    public string Name { get; set; }
    public string DeclaringTypeName { get; set; }
    public int Depth { get; set; }
    public string MemberAttributes { get; set; }
}

public interface ILookupEngineMemberMetadataProvider
{
    bool IsAvailable { get; }
    bool TryGetMembers(object instance, Document document, out List<LookupMemberMetadata> members, out string errorMessage);
}

public sealed class RuntimeLookupEngineMemberMetadataProvider : ILookupEngineMemberMetadataProvider
{
    private readonly Type _decomposeOptionsType;
    private readonly MethodInfo _decomposeMembersMethod;
    private readonly MethodInfo _findDescriptorMethod;
    private readonly PropertyInfo _typeResolverProperty;
    private readonly PropertyInfo _contextProperty;

    public bool IsAvailable => _decomposeMembersMethod != null && _decomposeOptionsType != null;

    private RuntimeLookupEngineMemberMetadataProvider(
        Type decomposeOptionsType,
        MethodInfo decomposeMembersMethod,
        MethodInfo findDescriptorMethod,
        PropertyInfo typeResolverProperty,
        PropertyInfo contextProperty)
    {
        _decomposeOptionsType = decomposeOptionsType;
        _decomposeMembersMethod = decomposeMembersMethod;
        _findDescriptorMethod = findDescriptorMethod;
        _typeResolverProperty = typeResolverProperty;
        _contextProperty = contextProperty;
    }

    public static ILookupEngineMemberMetadataProvider Create()
    {
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        var revitLookupAssembly = FindAssemblyBySimpleName(loadedAssemblies, "RevitLookup");
        var lookupEngineAssembly = FindAssemblyBySimpleName(loadedAssemblies, "LookupEngine");
        if (revitLookupAssembly == null || lookupEngineAssembly == null)
        {
            return new RuntimeLookupEngineMemberMetadataProvider(null, null, null, null, null);
        }

        var lookupComposerType = lookupEngineAssembly.GetType("LookupEngine.LookupComposer", false);
        var decomposeOptionsType = lookupEngineAssembly.GetType("LookupEngine.Options.DecomposeOptions`1", false)?.MakeGenericType(typeof(Document));
        if (lookupComposerType == null || decomposeOptionsType == null)
        {
            return new RuntimeLookupEngineMemberMetadataProvider(null, null, null, null, null);
        }

        var decomposeMembersMethod = lookupComposerType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "DecomposeMembers", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!method.IsGenericMethodDefinition)
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(object);
            })?
            .MakeGenericMethod(typeof(Document));

        var descriptorsMapType = revitLookupAssembly.GetType("RevitLookup.Core.Decomposition.DescriptorsMap", false);
        var findDescriptorMethod = descriptorsMapType?.GetMethod(
            "FindDescriptor",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(object), typeof(Type) },
            null);

        return new RuntimeLookupEngineMemberMetadataProvider(
            decomposeOptionsType,
            decomposeMembersMethod,
            findDescriptorMethod,
            decomposeOptionsType.GetProperty("TypeResolver", BindingFlags.Public | BindingFlags.Instance),
            decomposeOptionsType.GetProperty("Context", BindingFlags.Public | BindingFlags.Instance));
    }

    public bool TryGetMembers(object instance, Document document, out List<LookupMemberMetadata> members, out string errorMessage)
    {
        members = new List<LookupMemberMetadata>();
        errorMessage = null;

        if (!IsAvailable)
        {
            errorMessage = "LookupEngine runtime is unavailable";
            return false;
        }

        if (document == null)
        {
            errorMessage = "LookupEngine context document is unavailable";
            return false;
        }

        try
        {
            var options = Activator.CreateInstance(_decomposeOptionsType);
            ConfigureOptions(options, document);

            var rawMembers = _decomposeMembersMethod.Invoke(null, new object[] { instance, options }) as IEnumerable;
            if (rawMembers == null)
            {
                errorMessage = "LookupEngine returned non-enumerable member payload.";
                return false;
            }

            foreach (var rawMember in rawMembers)
            {
                if (rawMember == null)
                {
                    continue;
                }

                members.Add(new LookupMemberMetadata
                {
                    Name = rawMember.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(rawMember)?.ToString(),
                    DeclaringTypeName = rawMember.GetType().GetProperty("DeclaringTypeName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(rawMember)?.ToString(),
                    Depth = ConvertToInt(rawMember.GetType().GetProperty("Depth", BindingFlags.Public | BindingFlags.Instance)?.GetValue(rawMember)),
                    MemberAttributes = rawMember.GetType().GetProperty("MemberAttributes", BindingFlags.Public | BindingFlags.Instance)?.GetValue(rawMember)?.ToString()
                });
            }

            members = members
                .Where(member => !string.IsNullOrWhiteSpace(member.Name) && !string.IsNullOrWhiteSpace(member.DeclaringTypeName))
                .ToList();

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = Unwrap(ex).Message;
            return false;
        }
    }

    private void ConfigureOptions(object options, Document document)
    {
        SetBool(options, "IncludeRoot", false);
        SetBool(options, "IncludeFields", true);
        SetBool(options, "IncludeEvents", false);
        SetBool(options, "IncludeUnsupported", true);
        SetBool(options, "IncludePrivateMembers", false);
        SetBool(options, "IncludeStaticMembers", true);
        SetBool(options, "EnableExtensions", true);
        SetBool(options, "EnableRedirection", true);

        if (_contextProperty?.CanWrite == true)
        {
            _contextProperty.SetValue(options, document);
        }

        if (_findDescriptorMethod != null && _typeResolverProperty?.CanWrite == true)
        {
            var resolver = Delegate.CreateDelegate(_typeResolverProperty.PropertyType, _findDescriptorMethod);
            _typeResolverProperty.SetValue(options, resolver);
        }
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

    private static int ConvertToInt(object value)
    {
        if (value == null)
        {
            return 0;
        }

        return value is int intValue ? intValue : Convert.ToInt32(value);
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
