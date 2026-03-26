using RevitMCPCommandSet.Services.ConnectRvtLookup;

namespace RevitMCPCommandSet.Tests.ConnectRvtLookup;

public class MemberGroupProjectorTests
{
    [Test]
    public async Task Project_OrdersGroupsFromBaseTypeToDerivedType()
    {
        var groups = MemberGroupProjector.Project(new DerivedNode());

        await Assert.That(groups.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(groups[0].DeclaringTypeName).IsEqualTo(nameof(BaseNode));
        await Assert.That(groups[1].DeclaringTypeName).IsEqualTo(nameof(DerivedNode));
        await Assert.That(groups[0].Depth).IsEqualTo(1);
        await Assert.That(groups[1].Depth).IsEqualTo(2);
    }

    [Test]
    public async Task Project_OrdersPropertiesBeforeMethodsWithinGroup()
    {
        var groups = MemberGroupProjector.Project(new DerivedNode());
        var derivedGroup = groups.Single(group => group.DeclaringTypeName == nameof(DerivedNode));

        var propertyIndex = derivedGroup.TopMembers.IndexOf(nameof(DerivedNode.Alpha));
        var methodIndex = derivedGroup.TopMembers.IndexOf(nameof(DerivedNode.Zeta));

        await Assert.That(propertyIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(methodIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(propertyIndex < methodIndex).IsTrue();
        await Assert.That(derivedGroup.HasMoreMembers).IsFalse();
    }

    [Test]
    public async Task Project_IncludesDescriptorBackedSpecialMethods()
    {
        var groups = MemberGroupProjector.Project(new DescriptorBackedNode());
        var group = groups.Single(item => item.DeclaringTypeName == nameof(DescriptorBackedNode));

        await Assert.That(group.TopMembers.Contains(nameof(DescriptorBackedNode.GetMaterialIds))).IsTrue();
    }

    [Test]
    public async Task Project_UsesLookupEngineMetadataProvider_WhenAvailable()
    {
        var originalProvider = ConnectRvtLookupRuntime.LookupEngineMemberMetadataProvider;
        ConnectRvtLookupRuntime.LookupEngineMemberMetadataProvider = new FakeLookupEngineMemberMetadataProvider();

        try
        {
            var groups = MemberGroupProjector.Project(new DerivedNode(), document: null);

            await Assert.That(groups.Count).IsEqualTo(2);
            await Assert.That(groups[0].DeclaringTypeName).IsEqualTo("Element");
            await Assert.That(groups[0].TopMembers.Single()).IsEqualTo("BoundingBox");
            await Assert.That(groups[1].DeclaringTypeName).IsEqualTo("Wall");
            await Assert.That(groups[1].TopMembers[0]).IsEqualTo("Width");
            await Assert.That(groups[1].TopMembers[1]).IsEqualTo("GetAnalyticalModel");
        }
        finally
        {
            ConnectRvtLookupRuntime.LookupEngineMemberMetadataProvider = originalProvider;
        }
    }

    private sealed class BaseNode
    {
        public string BaseName { get; } = "base";
    }

    private sealed class DerivedNode : BaseNode
    {
        public string Alpha { get; } = "alpha";

        public string Zeta()
        {
            return "zeta";
        }
    }

    private sealed class DescriptorBackedNode
    {
        public IReadOnlyList<int> GetMaterialIds(bool returnPaintMaterials)
        {
            return returnPaintMaterials ? new[] { 2, 4 } : new[] { 1, 3 };
        }
    }

    private sealed class FakeLookupEngineMemberMetadataProvider : ILookupEngineMemberMetadataProvider
    {
        public bool IsAvailable => true;

        public bool TryGetMembers(object instance, Autodesk.Revit.DB.Document document, out List<LookupMemberMetadata> members, out string errorMessage)
        {
            members =
            [
                new LookupMemberMetadata { DeclaringTypeName = "Wall", Depth = 2, Name = "GetAnalyticalModel", MemberAttributes = "Method" },
                new LookupMemberMetadata { DeclaringTypeName = "Wall", Depth = 2, Name = "Width", MemberAttributes = "Property" },
                new LookupMemberMetadata { DeclaringTypeName = "Element", Depth = 1, Name = "BoundingBox", MemberAttributes = "Property" }
            ];
            errorMessage = null;
            return true;
        }
    }
}
