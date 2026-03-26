using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.ConnectRvtLookup;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace RevitMCPCommandSet.Services.ConnectRvtLookup;

public static class ConnectRvtLookupRuntime
{
    private static readonly ConcurrentDictionary<string, ObjectMemberGroupsResponse> ObjectMemberGroupsCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ExpandMembersResponse> ExpandMembersCache = new(StringComparer.Ordinal);

    public static QueryHandleStore HandleStore { get; } = new();
    public static QueryBudgetService BudgetService { get; } = new();
    public static IDescriptorSummaryProvider DescriptorSummaryProvider { get; set; } = RuntimeDescriptorSummaryProvider.Create();
    public static ILookupEngineMemberMetadataProvider LookupEngineMemberMetadataProvider { get; set; } = RuntimeLookupEngineMemberMetadataProvider.Create();

    public static string CreateDocumentKey(Document document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        if (!string.IsNullOrWhiteSpace(document.PathName))
        {
            return document.PathName;
        }

        return $"{document.Title}:{document.GetHashCode()}";
    }

    public static string CreateSelectionContextKey(Document document, ICollection<ElementId> selectedIds)
    {
        var documentKey = CreateDocumentKey(document);
        if (selectedIds == null || selectedIds.Count == 0)
        {
            return $"{documentKey}|active-view";
        }

        var ids = selectedIds
            .Select(GetElementIdValue)
            .OrderBy(value => value)
            .ToArray();

        return $"{documentKey}|selection|{string.Join(",", ids)}";
    }

    public static string CreateElementTitle(Element element)
    {
        if (element == null) throw new ArgumentNullException(nameof(element));

        return CreateElementTitle(element.Name, GetElementIdValue(element.Id));
    }

    public static string CreateElementTitle(string elementName, long elementId)
    {
        var idText = elementId.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(elementName)
            ? $"ID{idText}"
            : $"{elementName}, ID{idText}";
    }

    public static string CreateElementTypeName(Element element)
    {
        if (element == null) throw new ArgumentNullException(nameof(element));

        return element.GetType().Name;
    }

    public static string CreateElementCategoryName(Element element)
    {
        if (element == null) throw new ArgumentNullException(nameof(element));

        return string.IsNullOrWhiteSpace(element.Category?.Name)
            ? null
            : element.Category.Name;
    }

    public static long GetElementIdValue(ElementId elementId)
    {
#if REVIT2024_OR_GREATER
        return elementId?.Value ?? -1;
#else
        return elementId?.IntegerValue ?? -1;
#endif
    }

    public static QueryCommandResult<T> TimeoutFailure<T>(string commandName)
    {
        return ConnectRvtLookupDiagnostics.TimeoutFailure<T>(nameof(ConnectRvtLookupRuntime), commandName);
    }

    public static List<MemberGroupResult> CreateMemberGroups(object instance)
    {
        if (instance == null)
        {
            return new List<MemberGroupResult>();
        }

        return MemberGroupProjector.Project(instance);
    }

    public static string CreateObjectTitle(object instance)
    {
        if (instance == null)
        {
            return null;
        }

        if (instance is Element element)
        {
            return CreateElementTitle(element);
        }

        return instance.GetType().Name;
    }

    public static ExpandedMemberResult ExpandMember(
        object instance,
        string documentKey,
        string contextKey,
        RequestedMember requestedMember)
    {
        if (requestedMember == null)
        {
            return new ExpandedMemberResult
            {
                ValueKind = "error",
                ErrorMessage = "成员请求不能为空"
            };
        }

        var result = new ExpandedMemberResult
        {
            DeclaringTypeName = requestedMember.DeclaringTypeName,
            MemberName = requestedMember.MemberName
        };

        if (instance == null)
        {
            result.ValueKind = "error";
            result.ErrorMessage = "当前对象为空，无法展开成员";
            return result;
        }

        if (!TryResolveSupportedMember(instance.GetType(), requestedMember, out var memberInfo, out var resolvedDeclaringType, out var resolveError))
        {
            result.ValueKind = "error";
            result.ErrorMessage = resolveError;
            return result;
        }

        result.DeclaringTypeName = resolvedDeclaringType.Name;

        if (TryEvaluateDescriptorResolvedValue(instance, memberInfo, out var descriptorValue, out var descriptorError))
        {
            return ClassifyExpandedValue(result, descriptorValue.Value, documentKey, contextKey, descriptorValue.Description, false);
        }

        if (!TryEvaluateMemberValue(instance, memberInfo, out var value, out var evaluationError))
        {
            result.ValueKind = "error";
            result.ErrorMessage = evaluationError ?? descriptorError;
            return result;
        }

        result.UsedFallback = ShouldMarkDescriptorFallback(memberInfo);
        return ClassifyExpandedValue(result, value, documentKey, contextKey, null, result.UsedFallback);
    }

    public static string RegisterObjectHandle(string documentKey, object value, string contextKey)
    {
        return HandleStore.RegisterObjectHandle(documentKey, value, contextKey);
    }

    public static string RegisterValueHandle(string documentKey, object value, string contextKey)
    {
        return HandleStore.RegisterValueHandle(documentKey, value, contextKey);
    }

    public static void ClearQueryCaches()
    {
        ObjectMemberGroupsCache.Clear();
        ExpandMembersCache.Clear();
    }

    public static ObjectMemberGroupsResponse GetOrCreateObjectMemberGroupsResponse(
        string objectHandle,
        object instance,
        Document document)
    {
        if (string.IsNullOrWhiteSpace(objectHandle)) throw new ArgumentException("objectHandle is required", nameof(objectHandle));

        return ObjectMemberGroupsCache.GetOrAdd(
            objectHandle,
            _ => new ObjectMemberGroupsResponse
            {
                ObjectHandle = objectHandle,
                Title = CreateObjectTitle(instance),
                Truncated = false,
                Groups = MemberGroupProjector.Project(instance, document)
            });
    }

    public static ExpandMembersResponse GetOrCreateExpandMembersResponse(
        string objectHandle,
        object instance,
        string documentKey,
        string contextKey,
        IReadOnlyList<RequestedMember> members)
    {
        if (string.IsNullOrWhiteSpace(objectHandle)) throw new ArgumentException("objectHandle is required", nameof(objectHandle));
        if (members == null) throw new ArgumentNullException(nameof(members));

        var cacheKey = CreateExpandMembersCacheKey(objectHandle, members);
        return ExpandMembersCache.GetOrAdd(
            cacheKey,
            _ => new ExpandMembersResponse
            {
                ObjectHandle = objectHandle,
                Expanded = members
                    .Select(member => ExpandMember(instance, documentKey, contextKey, member))
                    .ToList()
            });
    }

    public static bool TryCreateNavigateObjectResponse(
        string activeDocumentKey,
        Document document,
        NavigateObjectRequest request,
        out NavigateObjectResponse response,
        out QueryCommandResult<NavigateObjectResponse> errorResult)
    {
        response = null;
        errorResult = null;

        if (request == null || string.IsNullOrWhiteSpace(request.ValueHandle))
        {
            errorResult = QueryCommandResults.InvalidArgument<NavigateObjectResponse>(
                "Missing required parameter: 'valueHandle'",
                "Provide a non-empty 'valueHandle'.");
            return false;
        }

        if (!HandleStore.TryResolve(request.ValueHandle, out var entry))
        {
            errorResult = ConnectRvtLookupDiagnostics.InvalidHandleFailure<NavigateObjectResponse>(
                nameof(TryCreateNavigateObjectResponse),
                request.ValueHandle,
                $"未找到值句柄: {request.ValueHandle}",
                "Expand a member again and request a new valueHandle.");
            return false;
        }

        if (!string.Equals(entry.DocumentKey, activeDocumentKey, StringComparison.Ordinal))
        {
            errorResult = ConnectRvtLookupDiagnostics.InvalidHandleFailure<NavigateObjectResponse>(
                nameof(TryCreateNavigateObjectResponse),
                request.ValueHandle,
                $"值句柄已不属于当前文档: {request.ValueHandle}",
                "Expand the member again in the active Revit document and request a new valueHandle.");
            return false;
        }

        if (!string.Equals(entry.HandleType, QueryHandleTypes.Value, StringComparison.Ordinal))
        {
            errorResult = ConnectRvtLookupDiagnostics.InvalidHandleFailure<NavigateObjectResponse>(
                nameof(TryCreateNavigateObjectResponse),
                request.ValueHandle,
                $"句柄类型不匹配: {request.ValueHandle}",
                "Use a valueHandle returned by expand_members.");
            return false;
        }

        if (!CanNavigateValue(entry.Value))
        {
            errorResult = ConnectRvtLookupDiagnostics.InvalidHandleFailure<NavigateObjectResponse>(
                nameof(TryCreateNavigateObjectResponse),
                request.ValueHandle,
                $"值不可导航: {request.ValueHandle}",
                "Choose a member where canNavigate is true and reuse its valueHandle.");
            return false;
        }

        var objectHandle = RegisterObjectHandle(activeDocumentKey, entry.Value, entry.ContextKey);
        var directory = GetOrCreateObjectMemberGroupsResponse(objectHandle, entry.Value, document);

        response = new NavigateObjectResponse
        {
            ValueHandle = request.ValueHandle,
            ObjectHandle = objectHandle,
            Title = directory.Title,
            Truncated = false,
            Groups = directory.Groups
        };
        return true;
    }

    private static ExpandedMemberResult ClassifyExpandedValue(
        ExpandedMemberResult result,
        object value,
        string documentKey,
        string contextKey,
        string descriptorDescription,
        bool usedFallback)
    {
        result.UsedFallback = usedFallback;

        if (value == null)
        {
            result.ValueKind = "null";
            result.DisplayValue = string.IsNullOrWhiteSpace(descriptorDescription) ? "null" : descriptorDescription;
            result.CanNavigate = false;
            return result;
        }

        var valueType = value.GetType();
        if (valueType.IsEnum)
        {
            result.ValueKind = "enum";
            result.DisplayValue = string.IsNullOrWhiteSpace(descriptorDescription) ? value.ToString() : descriptorDescription;
            result.CanNavigate = false;
            return result;
        }

        if (IsScalarValue(valueType))
        {
            result.ValueKind = "scalar";
            result.DisplayValue = string.IsNullOrWhiteSpace(descriptorDescription) ? FormatScalarValue(value) : descriptorDescription;
            result.CanNavigate = false;
            return result;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            result.ValueKind = "collection_summary";
            result.DisplayValue = string.IsNullOrWhiteSpace(descriptorDescription) ? CreateCollectionSummary(enumerable) : descriptorDescription;
            result.CanNavigate = true;
            result.ValueHandle = RegisterValueHandle(documentKey, value, contextKey);
            return result;
        }

        result.ValueKind = "object_summary";
        result.DisplayValue = string.IsNullOrWhiteSpace(descriptorDescription) ? CreateObjectSummary(value) : descriptorDescription;
        result.CanNavigate = true;
        result.ValueHandle = RegisterValueHandle(documentKey, value, contextKey);
        return result;
    }

    private static bool TryEvaluateDescriptorResolvedValue(
        object instance,
        MemberInfo memberInfo,
        out DescriptorResolvedValue resolvedValue,
        out string errorMessage)
    {
        resolvedValue = null;
        errorMessage = null;

        if (!ShouldTryDescriptorFirst(memberInfo))
        {
            return false;
        }

        var provider = DescriptorSummaryProvider;
        if (provider == null)
        {
            ConnectRvtLookupDiagnostics.Warning(
                nameof(TryEvaluateDescriptorResolvedValue),
                "RevitLookup descriptor 摘要桥接不可用，已回退到反射路径。",
                ConnectRvtLookupDiagnostics.Context("member", memberInfo.Name),
                ConnectRvtLookupDiagnostics.Context("instanceType", instance?.GetType().Name));
            errorMessage = "Descriptor summary provider is not configured";
            return false;
        }

        var resolved = provider.TryResolveMemberValue(instance, memberInfo, out resolvedValue, out errorMessage);
        if (!resolved)
        {
            ConnectRvtLookupDiagnostics.Warning(
                nameof(TryEvaluateDescriptorResolvedValue),
                "descriptor 解析失败，已回退到反射或最小摘要输出。",
                ConnectRvtLookupDiagnostics.Context("member", memberInfo.Name),
                ConnectRvtLookupDiagnostics.Context("instanceType", instance?.GetType().Name),
                ConnectRvtLookupDiagnostics.Context("reason", errorMessage));
        }

        return resolved;
    }

    private static bool ShouldTryDescriptorFirst(MemberInfo memberInfo)
    {
        return memberInfo != null &&
               (string.Equals(memberInfo.Name, "BoundingBox", StringComparison.Ordinal) ||
                string.Equals(memberInfo.Name, "Geometry", StringComparison.Ordinal) ||
                string.Equals(memberInfo.Name, "GetMaterialIds", StringComparison.Ordinal));
    }

    private static bool ShouldMarkDescriptorFallback(MemberInfo memberInfo)
    {
        return ShouldTryDescriptorFirst(memberInfo);
    }

    private static List<Type> GetTypeHierarchy(Type type)
    {
        var stack = new Stack<Type>();
        var current = type;

        while (current != null)
        {
            stack.Push(current);
            current = current.BaseType;
        }

        return stack.ToList();
    }

    internal static List<Type> GetTypeHierarchyForProjection(Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));

        return GetTypeHierarchy(type);
    }

    internal static IEnumerable<MemberInfo> GetSupportedMembersForProjection(Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));

        return type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(IsSupportedProjectionMember);
    }

    internal static int GetSupportedMemberPriorityForProjection(MemberInfo memberInfo)
    {
        return GetSupportedMemberPriority(memberInfo);
    }

    private static bool TryResolveSupportedMember(
        Type runtimeType,
        RequestedMember requestedMember,
        out MemberInfo memberInfo,
        out Type declaringType,
        out string errorMessage)
    {
        memberInfo = null;
        declaringType = null;

        var typeHierarchy = GetTypeHierarchy(runtimeType);
        declaringType = typeHierarchy.FirstOrDefault(type =>
            string.Equals(type.Name, requestedMember.DeclaringTypeName, StringComparison.Ordinal) ||
            string.Equals(type.FullName, requestedMember.DeclaringTypeName, StringComparison.Ordinal));

        if (declaringType == null)
        {
            errorMessage = $"未找到声明类型: {requestedMember.DeclaringTypeName}";
            return false;
        }

        var members = declaringType
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => string.Equals(member.Name, requestedMember.MemberName, StringComparison.Ordinal))
            .ToList();

        memberInfo = members
            .Where(IsResolvableMember)
            .OrderBy(GetSupportedMemberPriority)
            .FirstOrDefault();

        if (memberInfo != null)
        {
            errorMessage = null;
            return true;
        }

        errorMessage = members.Count == 0
            ? $"未找到成员: {requestedMember.DeclaringTypeName}.{requestedMember.MemberName}"
            : $"成员不支持展开: {requestedMember.DeclaringTypeName}.{requestedMember.MemberName}";
        return false;
    }

    private static bool TryEvaluateMemberValue(
        object instance,
        MemberInfo memberInfo,
        out object value,
        out string errorMessage)
    {
        value = null;

        try
        {
            switch (memberInfo)
            {
                case PropertyInfo propertyInfo:
                    value = propertyInfo.GetValue(propertyInfo.GetMethod.IsStatic ? null : instance, null);
                    errorMessage = null;
                    return true;
                case FieldInfo fieldInfo:
                    value = fieldInfo.GetValue(fieldInfo.IsStatic ? null : instance);
                    errorMessage = null;
                    return true;
                case MethodInfo methodInfo:
                    if (IsDescriptorSpecialMethod(methodInfo) &&
                        TryEvaluateSpecialMethodFallback(instance, methodInfo, out value))
                    {
                        errorMessage = null;
                        return true;
                    }

                    value = methodInfo.Invoke(methodInfo.IsStatic ? null : instance, null);
                    errorMessage = null;
                    return true;
                default:
                    errorMessage = $"暂不支持成员类型: {memberInfo.MemberType}";
                    return false;
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            errorMessage = ex.InnerException.Message;
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static bool IsSupportedMember(MemberInfo memberInfo)
    {
        switch (memberInfo)
        {
            case PropertyInfo propertyInfo:
                return propertyInfo.GetMethod != null &&
                       propertyInfo.GetMethod.IsPublic &&
                       propertyInfo.GetIndexParameters().Length == 0;
            case FieldInfo:
                return true;
            case MethodInfo methodInfo:
                return methodInfo.IsPublic &&
                       !methodInfo.IsSpecialName &&
                       !methodInfo.ContainsGenericParameters &&
                       methodInfo.GetParameters().Length == 0;
            default:
                return false;
        }
    }

    private static bool IsResolvableMember(MemberInfo memberInfo)
    {
        return IsSupportedMember(memberInfo) || IsDescriptorSpecialMethod(memberInfo);
    }

    private static bool IsSupportedProjectionMember(MemberInfo memberInfo)
    {
        return IsSupportedMember(memberInfo) || IsDescriptorSpecialMethod(memberInfo);
    }

    private static int GetSupportedMemberPriority(MemberInfo memberInfo)
    {
        if (memberInfo is PropertyInfo)
        {
            return 0;
        }

        if (memberInfo is FieldInfo)
        {
            return 1;
        }

        if (memberInfo is MethodInfo)
        {
            return 2;
        }

        return 9;
    }

    private static bool IsDescriptorSpecialMethod(MemberInfo memberInfo)
    {
        if (memberInfo is not MethodInfo methodInfo)
        {
            return false;
        }

        return methodInfo.IsPublic &&
               string.Equals(methodInfo.Name, "GetMaterialIds", StringComparison.Ordinal) &&
               methodInfo.GetParameters().Length == 1 &&
               methodInfo.GetParameters()[0].ParameterType == typeof(bool);
    }

    private static bool TryEvaluateSpecialMethodFallback(object instance, MethodInfo methodInfo, out object value)
    {
        value = null;

        if (!string.Equals(methodInfo.Name, "GetMaterialIds", StringComparison.Ordinal))
        {
            return false;
        }

        var geometryMaterials = methodInfo.Invoke(methodInfo.IsStatic ? null : instance, new object[] { false });
        var paintMaterials = methodInfo.Invoke(methodInfo.IsStatic ? null : instance, new object[] { true });
        value = new DescriptorFallbackCollectionGroup[]
        {
            new("Geometry and compound structure materials", geometryMaterials),
            new("Paint materials", paintMaterials)
        };
        return true;
    }

    private sealed class DescriptorFallbackCollectionGroup
    {
        public DescriptorFallbackCollectionGroup(string name, object value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }
        public object Value { get; }

        public override string ToString()
        {
            return Name;
        }
    }

    private static bool IsScalarValue(Type type)
    {
        return type.IsPrimitive ||
               type == typeof(string) ||
               type == typeof(decimal) ||
               type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) ||
               type == typeof(TimeSpan) ||
               type == typeof(Guid) ||
               type == typeof(ElementId);
    }

    private static bool CanNavigateValue(object value)
    {
        if (value == null)
        {
            return false;
        }

        var type = value.GetType();
        if (type.IsEnum || IsScalarValue(type))
        {
            return false;
        }

        return value is not string;
    }

    private static string CreateExpandMembersCacheKey(string objectHandle, IReadOnlyList<RequestedMember> members)
    {
        return $"{objectHandle}|{string.Join(";", members.Select(member => $"{member.DeclaringTypeName}.{member.MemberName}"))}";
    }

    private static string FormatScalarValue(object value)
    {
        switch (value)
        {
            case null:
                return "null";
            case ElementId elementId:
                return GetElementIdValue(elementId).ToString(CultureInfo.InvariantCulture);
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            default:
                return value.ToString();
        }
    }

    private static string CreateCollectionSummary(IEnumerable enumerable)
    {
        var valueType = enumerable.GetType();
        var samples = new List<string>();
        var count = 0;
        var hasMore = false;

        foreach (var item in enumerable)
        {
            count++;
            if (samples.Count < 3)
            {
                samples.Add(CreateSampleText(item));
            }
            else
            {
                hasMore = true;
                break;
            }
        }

        var summary = $"{valueType.Name} count={(hasMore ? $"{count}+" : count.ToString(CultureInfo.InvariantCulture))}";
        if (samples.Count > 0)
        {
            summary += $" [{string.Join(", ", samples)}]";
        }

        return summary;
    }

    private static string CreateSampleText(object item)
    {
        if (item == null)
        {
            return "null";
        }

        var itemType = item.GetType();
        if (itemType.IsEnum || IsScalarValue(itemType))
        {
            return FormatScalarValue(item);
        }

        return itemType.Name;
    }

    private static string CreateObjectSummary(object value)
    {
        if (value is Element element)
        {
            return CreateElementTitle(element);
        }

        var text = value.ToString();
        if (!string.IsNullOrWhiteSpace(text) &&
            !string.Equals(text, value.GetType().FullName, StringComparison.Ordinal) &&
            !string.Equals(text, value.GetType().Name, StringComparison.Ordinal))
        {
            return $"{value.GetType().Name}: {text}";
        }

        return value.GetType().Name;
    }
}
