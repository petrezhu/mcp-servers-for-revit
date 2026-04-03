using System.Reflection;
using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.ConnectRvtLookup;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public static class MemberGroupProjector
{
    public static List<MemberGroupResult> Project(object instance)
    {
        return Project(instance, TryResolveDocument(instance));
    }

    public static List<MemberGroupResult> Project(object instance, Document document)
    {
        if (instance == null)
        {
            return new List<MemberGroupResult>();
        }

        if (TryProjectWithLookupEngine(instance, document, out var lookupGroups))
        {
            return lookupGroups;
        }

        return ProjectWithReflection(instance);
    }

    private static List<MemberGroupResult> ProjectWithReflection(object instance)
    {
        var hierarchy = ConnectRvtLookupRuntime.GetTypeHierarchyForProjection(instance.GetType());
        var groups = new List<MemberGroupResult>(hierarchy.Count);
        var depth = 1;

        foreach (var type in hierarchy)
        {
            var members = ConnectRvtLookupRuntime.GetSupportedMembersForProjection(type)
                .GroupBy(member => member.Name, StringComparer.Ordinal)
                .Select(group => group.OrderBy(ConnectRvtLookupRuntime.GetSupportedMemberPriorityForProjection)
                    .ThenBy(GetMemberSignature, StringComparer.Ordinal)
                    .First())
                .OrderBy(ConnectRvtLookupRuntime.GetSupportedMemberPriorityForProjection)
                .ThenBy(member => member.Name, StringComparer.Ordinal)
                .ThenBy(GetMemberSignature, StringComparer.Ordinal)
                .ToList();

            groups.Add(new MemberGroupResult
            {
                DeclaringTypeName = type.Name,
                Depth = depth++,
                MemberCount = members.Count,
                TopMembers = members.Select(member => member.Name).ToList(),
                HasMoreMembers = false
            });
        }

        return groups;
    }

    private static bool TryProjectWithLookupEngine(object instance, Document document, out List<MemberGroupResult> groups)
    {
        groups = null;

        var provider = ConnectRvtLookupRuntime.LookupEngineMemberMetadataProvider;
        if (provider == null || (!provider.IsAvailable && provider is RuntimeLookupEngineMemberMetadataProvider))
        {
            var refreshedProvider = RuntimeLookupEngineMemberMetadataProvider.Create();
            if (refreshedProvider.IsAvailable)
            {
                ConnectRvtLookupRuntime.LookupEngineMemberMetadataProvider = refreshedProvider;
                provider = refreshedProvider;
                ConnectRvtLookupDiagnostics.Info(
                    nameof(TryProjectWithLookupEngine),
                    "LookupEngine 成员桥接已自动刷新并恢复可用。",
                    ConnectRvtLookupDiagnostics.Context("instanceType", instance?.GetType().Name));
            }
        }

        if (provider == null || !provider.IsAvailable)
        {
            ConnectRvtLookupDiagnostics.Warning(
                nameof(TryProjectWithLookupEngine),
                "LookupEngine 成员桥接不可用，已回退到反射成员投影。",
                ConnectRvtLookupDiagnostics.Context("instanceType", instance?.GetType().Name));
            return false;
        }

        if (!provider.TryGetMembers(instance, document, out var members, out _))
        {
            ConnectRvtLookupDiagnostics.Warning(
                nameof(TryProjectWithLookupEngine),
                "LookupEngine 成员桥接返回失败，已回退到反射成员投影。",
                ConnectRvtLookupDiagnostics.Context("instanceType", instance?.GetType().Name));
            return false;
        }

        groups = members
            .GroupBy(member => new LookupMemberGroupKey(member.DeclaringTypeName, member.Depth))
            .OrderBy(group => group.Key.Depth)
            .ThenBy(group => group.Key.DeclaringTypeName, StringComparer.Ordinal)
            .Select(group =>
            {
                var orderedMembers = group
                    .GroupBy(member => member.Name, StringComparer.Ordinal)
                    .Select(item => item.OrderBy(GetLookupMemberPriority)
                        .ThenBy(member => member.Name, StringComparer.Ordinal)
                        .First())
                    .OrderBy(GetLookupMemberPriority)
                    .ThenBy(member => member.Name, StringComparer.Ordinal)
                    .ToList();

                return new MemberGroupResult
                {
                    DeclaringTypeName = group.Key.DeclaringTypeName,
                    Depth = group.Key.Depth,
                    MemberCount = orderedMembers.Count,
                    TopMembers = orderedMembers.Select(member => member.Name).ToList(),
                    HasMoreMembers = false
                };
            })
            .ToList();

        return groups.Count > 0;
    }

    private static int GetLookupMemberPriority(LookupMemberMetadata member)
    {
        var attributes = member?.MemberAttributes ?? string.Empty;
        if (attributes.IndexOf("Property", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 0;
        }

        if (attributes.IndexOf("Field", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 1;
        }

        if (attributes.IndexOf("Method", StringComparison.OrdinalIgnoreCase) >= 0 ||
            attributes.IndexOf("Extension", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 2;
        }

        return 9;
    }

    private static string GetMemberSignature(MemberInfo memberInfo)
    {
        if (memberInfo is MethodInfo methodInfo)
        {
            var parameterTypes = methodInfo.GetParameters()
                .Select(parameter => parameter.ParameterType.Name);
            return $"{methodInfo.Name}({string.Join(",", parameterTypes)})";
        }

        return memberInfo.Name;
    }

    private static Document TryResolveDocument(object instance)
    {
        return instance switch
        {
            Element element => element.Document,
            Parameter { Element: not null } parameter => parameter.Element.Document,
            Document document => document,
            _ => null
        };
    }

    private readonly record struct LookupMemberGroupKey(string DeclaringTypeName, int Depth);
}
