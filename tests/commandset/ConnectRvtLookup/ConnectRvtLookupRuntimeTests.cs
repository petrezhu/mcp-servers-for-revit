using RevitMCPCommandSet.Models.ConnectRvtLookup;
using RevitMCPCommandSet.Services.ConnectRvtLookup;

namespace RevitMCPCommandSet.Tests.ConnectRvtLookup;

public class ConnectRvtLookupRuntimeTests
{
    private readonly List<string> _logs = [];

    [Before(Test)]
    public void ResetRuntimeState()
    {
        ConnectRvtLookupRuntime.HandleStore.InvalidateAll();
        ConnectRvtLookupRuntime.ClearQueryCaches();
        ConnectRvtLookupDiagnostics.SetSinkForTesting(message => _logs.Add(message));
    }

    [After(Test)]
    public void ResetDiagnostics()
    {
        ConnectRvtLookupDiagnostics.ResetSinkForTesting();
    }

    [Test]
    public async Task CreateMemberGroups_OnlyIncludesSupportedMembers()
    {
        var groups = ConnectRvtLookupRuntime.CreateMemberGroups(new SampleNode());
        var sampleGroup = groups.Single(group => group.DeclaringTypeName == nameof(SampleNode));

        await Assert.That(sampleGroup.TopMembers.Contains(nameof(SampleNode.Name))).IsTrue();
        await Assert.That(sampleGroup.TopMembers.Contains(nameof(SampleNode.Child))).IsTrue();
        await Assert.That(sampleGroup.TopMembers.Contains(nameof(SampleNode.Items))).IsTrue();
        await Assert.That(sampleGroup.TopMembers.Contains(nameof(SampleNode.Echo))).IsTrue();
        await Assert.That(sampleGroup.TopMembers.Contains("Item")).IsFalse();
        await Assert.That(sampleGroup.TopMembers.Contains(nameof(SampleNode.NeedsArg))).IsFalse();
        await Assert.That(sampleGroup.TopMembers.IndexOf(nameof(SampleNode.Child)) < sampleGroup.TopMembers.IndexOf(nameof(SampleNode.Echo))).IsTrue();
        await Assert.That(sampleGroup.MemberCount).IsEqualTo(sampleGroup.TopMembers.Count);
        await Assert.That(sampleGroup.Depth).IsGreaterThan(0);
    }

    [Test]
    public async Task ExpandMember_ScalarObjectAndCollection_ReturnExpectedKindsAndHandles()
    {
        ConnectRvtLookupRuntime.HandleStore.InvalidateAll();
        var instance = new SampleNode();

        var scalar = ConnectRvtLookupRuntime.ExpandMember(
            instance,
            "doc-a",
            "ctx-a",
            new RequestedMember
            {
                DeclaringTypeName = nameof(SampleNode),
                MemberName = nameof(SampleNode.Name)
            });

        var complex = ConnectRvtLookupRuntime.ExpandMember(
            instance,
            "doc-a",
            "ctx-a",
            new RequestedMember
            {
                DeclaringTypeName = nameof(SampleNode),
                MemberName = nameof(SampleNode.Child)
            });

        var collection = ConnectRvtLookupRuntime.ExpandMember(
            instance,
            "doc-a",
            "ctx-a",
            new RequestedMember
            {
                DeclaringTypeName = nameof(SampleNode),
                MemberName = nameof(SampleNode.Items)
            });

        await Assert.That(scalar.ValueKind).IsEqualTo("scalar");
        await Assert.That(scalar.DisplayValue).IsEqualTo("alpha");
        await Assert.That(scalar.CanNavigate).IsFalse();

        await Assert.That(complex.ValueKind).IsEqualTo("object_summary");
        await Assert.That(complex.CanNavigate).IsTrue();
        await Assert.That(!string.IsNullOrWhiteSpace(complex.ValueHandle) && complex.ValueHandle.StartsWith("val:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(ConnectRvtLookupRuntime.HandleStore.TryResolve(complex.ValueHandle, out var complexEntry)).IsTrue();
        await Assert.That(complexEntry.HandleType).IsEqualTo(QueryHandleTypes.Value);
        await Assert.That(complexEntry.Value is SampleChild).IsTrue();

        await Assert.That(collection.ValueKind).IsEqualTo("collection_summary");
        await Assert.That(collection.CanNavigate).IsTrue();
        await Assert.That(collection.DisplayValue).Contains("List");
    }

    [Test]
    public async Task ExpandMember_UnsupportedOrMissingMember_ReturnsError()
    {
        var instance = new SampleNode();

        var unsupported = ConnectRvtLookupRuntime.ExpandMember(
            instance,
            "doc-a",
            "ctx-a",
            new RequestedMember
            {
                DeclaringTypeName = nameof(SampleNode),
                MemberName = nameof(SampleNode.NeedsArg)
            });

        var missing = ConnectRvtLookupRuntime.ExpandMember(
            instance,
            "doc-a",
            "ctx-a",
            new RequestedMember
            {
                DeclaringTypeName = nameof(SampleNode),
                MemberName = "MissingMember"
            });

        await Assert.That(unsupported.ValueKind).IsEqualTo("error");
        await Assert.That(unsupported.ErrorMessage).Contains("不支持展开");
        await Assert.That(missing.ValueKind).IsEqualTo("error");
        await Assert.That(missing.ErrorMessage).Contains("未找到成员");
    }

    [Test]
    public async Task ExpandMember_DescriptorProviderSuccess_PrefersDescriptorSummary()
    {
        var originalProvider = ConnectRvtLookupRuntime.DescriptorSummaryProvider;
        ConnectRvtLookupRuntime.DescriptorSummaryProvider = new FakeDescriptorSummaryProvider(
            shouldResolve: true,
            resolvedValue: new DescriptorResolvedValue
            {
                Value = new List<string> { "model", "view" },
                Description = "Descriptor summary"
            });

        try
        {
            var result = ConnectRvtLookupRuntime.ExpandMember(
                new DescriptorTarget(),
                "doc-a",
                "ctx-a",
                new RequestedMember
                {
                    DeclaringTypeName = nameof(DescriptorTarget),
                    MemberName = nameof(DescriptorTarget.BoundingBox)
                });

            await Assert.That(result.ValueKind).IsEqualTo("collection_summary");
            await Assert.That(result.DisplayValue).IsEqualTo("Descriptor summary");
            await Assert.That(result.CanNavigate).IsTrue();
            await Assert.That(result.UsedFallback).IsFalse();
        }
        finally
        {
            ConnectRvtLookupRuntime.DescriptorSummaryProvider = originalProvider;
        }
    }

    [Test]
    public async Task ExpandMember_DescriptorProviderFailure_FallsBackAndMarksFallback()
    {
        var originalProvider = ConnectRvtLookupRuntime.DescriptorSummaryProvider;
        ConnectRvtLookupRuntime.DescriptorSummaryProvider = new FakeDescriptorSummaryProvider(
            shouldResolve: false,
            resolvedValue: null);

        try
        {
            var result = ConnectRvtLookupRuntime.ExpandMember(
                new DescriptorTarget(),
                "doc-a",
                "ctx-a",
                new RequestedMember
                {
                    DeclaringTypeName = nameof(DescriptorTarget),
                    MemberName = nameof(DescriptorTarget.BoundingBox)
                });

            await Assert.That(result.ValueKind).IsEqualTo("scalar");
            await Assert.That(result.DisplayValue).IsEqualTo("fallback-bounding-box");
            await Assert.That(result.UsedFallback).IsTrue();
        }
        finally
        {
            ConnectRvtLookupRuntime.DescriptorSummaryProvider = originalProvider;
        }
    }

    [Test]
    public async Task ExpandMember_GetMaterialIds_DescriptorFailure_UsesSpecialFallback()
    {
        var originalProvider = ConnectRvtLookupRuntime.DescriptorSummaryProvider;
        ConnectRvtLookupRuntime.DescriptorSummaryProvider = new FakeDescriptorSummaryProvider(
            shouldResolve: false,
            resolvedValue: null);

        try
        {
            var result = ConnectRvtLookupRuntime.ExpandMember(
                new DescriptorMethodTarget(),
                "doc-a",
                "ctx-a",
                new RequestedMember
                {
                    DeclaringTypeName = nameof(DescriptorMethodTarget),
                    MemberName = nameof(DescriptorMethodTarget.GetMaterialIds)
                });

            await Assert.That(result.ValueKind).IsEqualTo("collection_summary");
            await Assert.That(result.CanNavigate).IsTrue();
            await Assert.That(result.UsedFallback).IsTrue();
            await Assert.That(result.ErrorMessage).IsNull();
        }
        finally
        {
            ConnectRvtLookupRuntime.DescriptorSummaryProvider = originalProvider;
        }
    }

    [Test]
    public async Task GetOrCreateObjectMemberGroupsResponse_ReusesCachedDirectory()
    {
        var instance = new SampleNode();
        const string objectHandle = "obj:sample";

        var first = ConnectRvtLookupRuntime.GetOrCreateObjectMemberGroupsResponse(objectHandle, instance, null);
        var second = ConnectRvtLookupRuntime.GetOrCreateObjectMemberGroupsResponse(objectHandle, instance, null);

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(second.Groups.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task GetOrCreateExpandMembersResponse_ReusesCachedExpansion()
    {
        var instance = new SampleNode();
        var members = new[]
        {
            new RequestedMember
            {
                DeclaringTypeName = nameof(SampleNode),
                MemberName = nameof(SampleNode.Child)
            }
        };

        var first = ConnectRvtLookupRuntime.GetOrCreateExpandMembersResponse("obj:sample", instance, "doc-a", "ctx-a", members);
        var second = ConnectRvtLookupRuntime.GetOrCreateExpandMembersResponse("obj:sample", instance, "doc-a", "ctx-a", members);

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(second.Expanded.Single().CanNavigate).IsTrue();
    }

    [Test]
    public async Task NavigateObject_DescriptorValue_ProducesObjectPageAndReusesObjectHandle()
    {
        var originalProvider = ConnectRvtLookupRuntime.DescriptorSummaryProvider;
        ConnectRvtLookupRuntime.DescriptorSummaryProvider = new FakeDescriptorSummaryProvider(
            shouldResolve: true,
            resolvedValue: new DescriptorResolvedValue
            {
                Value = new SampleChildWithMembers(),
                Description = "Descriptor child"
            });

        try
        {
            const string documentKey = "doc-a";
            var root = new DescriptorTargetWithNestedValue();
            var objectHandle = ConnectRvtLookupRuntime.RegisterObjectHandle(documentKey, root, "ctx-a");
            var expanded = ConnectRvtLookupRuntime.GetOrCreateExpandMembersResponse(
                objectHandle,
                root,
                documentKey,
                "ctx-a",
                new[]
                {
                    new RequestedMember
                    {
                        DeclaringTypeName = nameof(DescriptorTargetWithNestedValue),
                        MemberName = nameof(DescriptorTargetWithNestedValue.BoundingBox)
                    }
                });

            var valueHandle = expanded.Expanded.Single().ValueHandle;
            var navigated = ConnectRvtLookupRuntime.TryCreateNavigateObjectResponse(
                documentKey,
                null,
                new NavigateObjectRequest { ValueHandle = valueHandle },
                out var response,
                out var error);

            var repeated = ConnectRvtLookupRuntime.TryCreateNavigateObjectResponse(
                documentKey,
                null,
                new NavigateObjectRequest { ValueHandle = valueHandle },
                out var repeatedResponse,
                out var repeatedError);

            await Assert.That(navigated).IsTrue();
            await Assert.That(error).IsNull();
            await Assert.That(response.ValueHandle).IsEqualTo(valueHandle);
            await Assert.That(response.ObjectHandle).StartsWith("obj:");
            await Assert.That(response.Groups.Any(group => group.TopMembers.Contains(nameof(SampleChildWithMembers.Label)))).IsTrue();
            await Assert.That(ConnectRvtLookupRuntime.HandleStore.TryResolve(response.ObjectHandle, out var navigatedEntry)).IsTrue();
            var directory = ConnectRvtLookupRuntime.GetOrCreateObjectMemberGroupsResponse(response.ObjectHandle, navigatedEntry.Value, null);
            await Assert.That(response.Groups.Count).IsEqualTo(directory.Groups.Count);
            await Assert.That(string.Join("|", response.Groups.Select(group => group.DeclaringTypeName))).IsEqualTo(
                string.Join("|", directory.Groups.Select(group => group.DeclaringTypeName)));
            await Assert.That(repeated).IsTrue();
            await Assert.That(repeatedError).IsNull();
            await Assert.That(repeatedResponse.ObjectHandle).IsEqualTo(response.ObjectHandle);
        }
        finally
        {
            ConnectRvtLookupRuntime.DescriptorSummaryProvider = originalProvider;
        }
    }

    [Test]
    public async Task NavigateObject_NonNavigableValue_ReturnsFailure()
    {
        const string documentKey = "doc-a";
        var valueHandle = ConnectRvtLookupRuntime.RegisterValueHandle(documentKey, 42, "ctx-a");

        var success = ConnectRvtLookupRuntime.TryCreateNavigateObjectResponse(
            documentKey,
            null,
            new NavigateObjectRequest { ValueHandle = valueHandle },
            out var response,
            out var error);

        await Assert.That(success).IsFalse();
        await Assert.That(response).IsNull();
        await Assert.That(error).IsNotNull();
        await Assert.That(error.Error?.ErrorCode).IsEqualTo(ConnectRvtLookupErrorCodes.InvalidHandle);
        await Assert.That(error.ErrorMessage).Contains("值不可导航");
        await Assert.That(_logs.Any(log => log.Contains("connect-rvtLookup") && log.Contains("handle=" + valueHandle))).IsTrue();
    }

    [Test]
    public async Task TimeoutFailure_ReturnsStructuredTimeoutError_AndWritesLog()
    {
        var result = ConnectRvtLookupRuntime.TimeoutFailure<NavigateObjectResponse>(ConnectRvtLookupCommandNames.NavigateObject);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Error?.ErrorCode).IsEqualTo(ConnectRvtLookupErrorCodes.QueryTimeout);
        await Assert.That(result.RetryRecommended).IsTrue();
        await Assert.That(_logs.Any(log => log.Contains("navigate_object 执行超时") && log.Contains("command=navigate_object"))).IsTrue();
    }

    [Test]
    public async Task CreateMemberGroups_WhenLookupBridgeUnavailable_FallsBackAndLogsWarning()
    {
        var originalProvider = ConnectRvtLookupRuntime.LookupEngineMemberMetadataProvider;
        ConnectRvtLookupRuntime.LookupEngineMemberMetadataProvider = new FakeLookupEngineMemberMetadataProvider(isAvailable: false);

        try
        {
            var groups = ConnectRvtLookupRuntime.CreateMemberGroups(new SampleNode());

            await Assert.That(groups.Any(group => group.DeclaringTypeName == nameof(SampleNode))).IsTrue();
            await Assert.That(_logs.Any(log => log.Contains("LookupEngine 成员桥接不可用") && log.Contains("instanceType=SampleNode"))).IsTrue();
        }
        finally
        {
            ConnectRvtLookupRuntime.LookupEngineMemberMetadataProvider = originalProvider;
        }
    }

    [Test]
    public async Task LookupBridgeUnavailableFailure_UsesDedicatedErrorCode_AndLogsContext()
    {
        var result = ConnectRvtLookupDiagnostics.LookupBridgeUnavailableFailure<ExpandMembersResponse>(
            "ExpandMembers",
            "RevitLookup 运行时桥接不可用",
            "Ensure RevitLookup assemblies are loaded before retrying.",
            ConnectRvtLookupDiagnostics.Context("handle", "obj:1"));

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Error?.ErrorCode).IsEqualTo(ConnectRvtLookupErrorCodes.LookupBridgeUnavailable);
        await Assert.That(_logs.Any(log => log.Contains("RevitLookup 运行时桥接不可用") && log.Contains("handle=obj:1"))).IsTrue();
    }

    private sealed class SampleNode
    {
        public string Name { get; } = "alpha";
        public SampleChild Child { get; } = new();
        public List<int> Items { get; } = [1, 2, 3, 4];
        public int this[int index] => index;

        public string Echo()
        {
            return "echo";
        }

        public string NeedsArg(int value)
        {
            return value.ToString();
        }
    }

    private sealed class SampleChild
    {
        public override string ToString()
        {
            return "child-node";
        }
    }

    private sealed class SampleChildWithMembers
    {
        public string Label { get; } = "deep-child";
    }

    private sealed class DescriptorTarget
    {
        public string BoundingBox { get; } = "fallback-bounding-box";
    }

    private sealed class DescriptorTargetWithNestedValue
    {
        public string BoundingBox { get; } = "ignored-fallback";
    }

    private sealed class DescriptorMethodTarget
    {
        public IReadOnlyList<int> GetMaterialIds(bool returnPaintMaterials)
        {
            return returnPaintMaterials ? new[] { 10, 20 } : new[] { 30, 40 };
        }
    }

    private sealed class FakeDescriptorSummaryProvider : IDescriptorSummaryProvider
    {
        private readonly bool _shouldResolve;
        private readonly DescriptorResolvedValue _resolvedValue;

        public bool IsAvailable => true;

        public FakeDescriptorSummaryProvider(bool shouldResolve, DescriptorResolvedValue resolvedValue)
        {
            _shouldResolve = shouldResolve;
            _resolvedValue = resolvedValue;
        }

        public bool TryResolveMemberValue(object instance, System.Reflection.MemberInfo memberInfo, out DescriptorResolvedValue resolvedValue, out string errorMessage)
        {
            resolvedValue = _resolvedValue;
            errorMessage = _shouldResolve ? null : "descriptor unavailable";
            return _shouldResolve;
        }
    }

    private sealed class FakeLookupEngineMemberMetadataProvider : ILookupEngineMemberMetadataProvider
    {
        public bool IsAvailable { get; }

        public FakeLookupEngineMemberMetadataProvider(bool isAvailable)
        {
            IsAvailable = isAvailable;
        }

        public bool TryGetMembers(object instance, Autodesk.Revit.DB.Document document, out List<LookupMemberMetadata> members, out string errorMessage)
        {
            members = new List<LookupMemberMetadata>();
            errorMessage = "lookup unavailable";
            return false;
        }
    }
}
