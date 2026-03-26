using System.Reflection;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public sealed class DescriptorResolvedValue
{
    public object Value { get; set; }
    public string Description { get; set; }
}

public interface IDescriptorSummaryProvider
{
    bool IsAvailable { get; }
    bool TryResolveMemberValue(object instance, MemberInfo memberInfo, out DescriptorResolvedValue resolvedValue, out string errorMessage);
}

public sealed class RuntimeDescriptorSummaryProvider : IDescriptorSummaryProvider
{
    private readonly MethodInfo _findDescriptorMethod;
    private readonly Type _descriptorResolverType;

    public bool IsAvailable => _findDescriptorMethod != null && _descriptorResolverType != null;

    private RuntimeDescriptorSummaryProvider(MethodInfo findDescriptorMethod, Type descriptorResolverType)
    {
        _findDescriptorMethod = findDescriptorMethod;
        _descriptorResolverType = descriptorResolverType;
    }

    public static IDescriptorSummaryProvider Create()
    {
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        var revitLookupAssembly = FindAssemblyBySimpleName(loadedAssemblies, "RevitLookup");
        var lookupEngineAbstractionsAssembly = FindAssemblyBySimpleName(loadedAssemblies, "LookupEngine.Abstractions");

        if (revitLookupAssembly == null || lookupEngineAbstractionsAssembly == null)
        {
            return new RuntimeDescriptorSummaryProvider(null, null);
        }

        var descriptorsMapType = revitLookupAssembly.GetType("RevitLookup.Core.Decomposition.DescriptorsMap", false);
        var descriptorResolverType = lookupEngineAbstractionsAssembly.GetType("LookupEngine.Abstractions.Configuration.IDescriptorResolver", false);
        var findDescriptorMethod = descriptorsMapType?.GetMethod(
            "FindDescriptor",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(object), typeof(Type) },
            null);

        return new RuntimeDescriptorSummaryProvider(findDescriptorMethod, descriptorResolverType);
    }

    public bool TryResolveMemberValue(object instance, MemberInfo memberInfo, out DescriptorResolvedValue resolvedValue, out string errorMessage)
    {
        resolvedValue = null;
        errorMessage = null;

        if (!IsAvailable)
        {
            errorMessage = "Descriptor runtime is unavailable";
            return false;
        }

        if (instance == null)
        {
            errorMessage = "Descriptor instance is null";
            return false;
        }

        if (memberInfo == null)
        {
            errorMessage = "Descriptor memberInfo is null";
            return false;
        }

        try
        {
            var descriptor = ResolveDescriptor(instance);
            if (descriptor == null || !_descriptorResolverType.IsInstanceOfType(descriptor))
            {
                errorMessage = "Descriptor resolver is unavailable for current instance";
                return false;
            }

            var resolveMethod = _descriptorResolverType.GetMethod("Resolve", new[] { typeof(string), typeof(ParameterInfo[]) });
            if (resolveMethod == null)
            {
                errorMessage = "Descriptor resolver method is unavailable";
                return false;
            }

            var resolverResult = resolveMethod.Invoke(descriptor, new object[] { memberInfo.Name, Array.Empty<ParameterInfo>() }) as Delegate;
            if (resolverResult == null)
            {
                errorMessage = $"Descriptor does not override member: {memberInfo.Name}";
                return false;
            }

            var variant = resolverResult.DynamicInvoke();
            if (variant == null)
            {
                resolvedValue = new DescriptorResolvedValue();
                return true;
            }

            var variantType = variant.GetType();
            resolvedValue = new DescriptorResolvedValue
            {
                Value = variantType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.GetValue(variant),
                Description = variantType.GetProperty("Description", BindingFlags.Public | BindingFlags.Instance)?.GetValue(variant)?.ToString()
            };

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = Unwrap(ex).Message;
            return false;
        }
    }

    private object ResolveDescriptor(object instance)
    {
        var descriptor = _findDescriptorMethod.Invoke(null, new object[] { instance, instance.GetType() });
        if (descriptor != null && _descriptorResolverType.IsInstanceOfType(descriptor))
        {
            return descriptor;
        }

        return _findDescriptorMethod.Invoke(null, new object[] { instance, null });
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
