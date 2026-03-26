using RevitMCPCommandSet.Models.ConnectRvtLookup;
using RevitMCPCommandSet.Services.ConnectRvtLookup;

namespace RevitMCPCommandSet.Tests.ConnectRvtLookup;

public class QueryBudgetServiceTests
{
    [Test]
    public async Task SelectionRootsBudget_TightBudget_TruncatesItemsAndRemovesUniqueId()
    {
        var service = new QueryBudgetService();
        var request = new SelectionRootsRequest
        {
            LimitGroups = 20,
            LimitItemsPerGroup = 20,
            TokenBudgetHint = 500
        };

        var response = new SelectionRootsResponse
        {
            Source = "selection_or_active_view",
            TotalRootCount = 20,
            Groups =
            [
                new RootGroupResult
                {
                    GroupKey = "Wall",
                    Count = 10,
                    Items = Enumerable.Range(1, 10).Select(index => new RootItemResult
                    {
                        ObjectHandle = $"obj:{index}",
                        ElementId = index,
                        UniqueId = $"uid-{index}",
                        Title = $"Wall {index}",
                        TypeName = "Wall",
                        Category = "Walls"
                    }).ToList()
                }
            ]
        };

        var result = service.ApplySelectionRootsBudget(response, request);

        await Assert.That(result.Truncated).IsTrue();
        await Assert.That(result.Groups[0].Items.Count).IsEqualTo(4);
        await Assert.That(result.Groups[0].Items.All(item => item.UniqueId == null)).IsTrue();
        await Assert.That(result.Budget.TruncationStage).IsEqualTo(QueryBudgetTruncationStages.NonEssentialFields);
        await Assert.That(response.Groups[0].Items.Count).IsEqualTo(10);
    }

    [Test]
    public async Task SelectionRootsBudget_MinimalBudget_ReducesToCountsAndSamples()
    {
        var service = new QueryBudgetService();
        var request = new SelectionRootsRequest
        {
            LimitGroups = 20,
            LimitItemsPerGroup = 20,
            TokenBudgetHint = 200
        };

        var response = new SelectionRootsResponse
        {
            Groups =
            [
                new RootGroupResult
                {
                    GroupKey = "Wall",
                    Count = 10,
                    Items = Enumerable.Range(1, 3).Select(index => new RootItemResult
                    {
                        ObjectHandle = $"obj:{index}",
                        ElementId = index,
                        UniqueId = $"uid-{index}",
                        Title = $"Wall {index}",
                        TypeName = "Wall",
                        Category = "Walls"
                    }).ToList()
                },
                new RootGroupResult
                {
                    GroupKey = "Pipe",
                    Count = 10,
                    Items = Enumerable.Range(10, 3).Select(index => new RootItemResult
                    {
                        ObjectHandle = $"obj:{index}",
                        ElementId = index,
                        UniqueId = $"uid-{index}",
                        Title = $"Pipe {index}",
                        TypeName = "Pipe",
                        Category = "Pipes"
                    }).ToList()
                },
                new RootGroupResult
                {
                    GroupKey = "Door",
                    Count = 10,
                    Items = Enumerable.Range(20, 3).Select(index => new RootItemResult
                    {
                        ObjectHandle = $"obj:{index}",
                        ElementId = index,
                        UniqueId = $"uid-{index}",
                        Title = $"Door {index}",
                        TypeName = "Door",
                        Category = "Doors"
                    }).ToList()
                },
                new RootGroupResult
                {
                    GroupKey = "Window",
                    Count = 10,
                    Items = Enumerable.Range(30, 3).Select(index => new RootItemResult
                    {
                        ObjectHandle = $"obj:{index}",
                        ElementId = index,
                        UniqueId = $"uid-{index}",
                        Title = $"Window {index}",
                        TypeName = "Window",
                        Category = "Windows"
                    }).ToList()
                }
            ]
        };

        var result = service.ApplySelectionRootsBudget(response, request);

        await Assert.That(result.Groups.Count).IsEqualTo(3);
        await Assert.That(result.Groups.All(group => group.Items.Count == 1)).IsTrue();
        await Assert.That(result.Budget.TruncationStage).IsEqualTo(QueryBudgetTruncationStages.CountsAndSamples);
    }

    [Test]
    public async Task ObjectMemberGroupsBudget_TightBudget_TruncatesPreviewsAndDropsTitle()
    {
        var service = new QueryBudgetService();
        var request = new ObjectMemberGroupsRequest
        {
            ObjectHandle = "obj:1",
            LimitGroups = 10,
            LimitMembersPerGroup = 12,
            TokenBudgetHint = 600
        };

        var response = new ObjectMemberGroupsResponse
        {
            ObjectHandle = "obj:1",
            Title = "Basic Wall, ID12345",
            Groups =
            [
                new MemberGroupResult
                {
                    DeclaringTypeName = "Element",
                    Depth = 1,
                    MemberCount = 10,
                    TopMembers = Enumerable.Range(1, 10).Select(index => $"Member{index}").ToList()
                }
            ]
        };

        var result = service.ApplyObjectMemberGroupsBudget(response, request);

        await Assert.That(result.Truncated).IsTrue();
        await Assert.That(result.Title).IsNull();
        await Assert.That(result.Groups[0].TopMembers.Count).IsEqualTo(4);
        await Assert.That(result.Groups[0].HasMoreMembers).IsTrue();
        await Assert.That(result.Budget.NextSuggestedAction).IsEqualTo("expand_members");
    }

    [Test]
    public async Task NavigateObjectBudget_MinimalBudget_DropsNonEssentialFieldsAndKeepsSamples()
    {
        var service = new QueryBudgetService();
        var request = new NavigateObjectRequest
        {
            ValueHandle = "val:1",
            LimitGroups = 10,
            LimitMembersPerGroup = 12,
            TokenBudgetHint = 250
        };

        var response = new NavigateObjectResponse
        {
            ValueHandle = "val:1",
            ObjectHandle = "obj:99",
            Title = "Geometry Variant",
            Groups =
            [
                new MemberGroupResult
                {
                    DeclaringTypeName = "GeometryObject",
                    Depth = 1,
                    MemberCount = 8,
                    TopMembers = Enumerable.Range(1, 8).Select(index => $"Member{index}").ToList()
                },
                new MemberGroupResult
                {
                    DeclaringTypeName = "Object",
                    Depth = 2,
                    MemberCount = 8,
                    TopMembers = Enumerable.Range(1, 8).Select(index => $"Base{index}").ToList()
                },
                new MemberGroupResult
                {
                    DeclaringTypeName = "Extra",
                    Depth = 3,
                    MemberCount = 8,
                    TopMembers = Enumerable.Range(1, 8).Select(index => $"Extra{index}").ToList()
                },
                new MemberGroupResult
                {
                    DeclaringTypeName = "Skipped",
                    Depth = 4,
                    MemberCount = 8,
                    TopMembers = Enumerable.Range(1, 8).Select(index => $"Skipped{index}").ToList()
                }
            ]
        };

        var result = service.ApplyNavigateObjectBudget(response, request);

        await Assert.That(result.Groups.Count).IsEqualTo(3);
        await Assert.That(result.Groups.All(group => group.TopMembers.Count == 1)).IsTrue();
        await Assert.That(result.Title).IsNull();
        await Assert.That(result.ObjectHandle).IsNull();
        await Assert.That(result.Budget.TruncationStage).IsEqualTo(QueryBudgetTruncationStages.CountsAndSamples);
    }

    [Test]
    public async Task ExpandMembersBudget_TightBudget_LimitsResultsAndClearsComplexDisplayValues()
    {
        var service = new QueryBudgetService();
        var response = new ExpandMembersResponse
        {
            ObjectHandle = "obj:1",
            Expanded = Enumerable.Range(1, 10).Select(index => new ExpandedMemberResult
            {
                DeclaringTypeName = "Element",
                MemberName = $"Member{index}",
                ValueKind = index == 1 ? "scalar" : "object_summary",
                DisplayValue = $"Value{index}",
                CanNavigate = index != 1,
                ValueHandle = index != 1 ? $"val:{index}" : null
            }).ToList()
        };

        var result = service.ApplyExpandMembersBudget(response, 500);

        await Assert.That(result.Expanded.Count).IsEqualTo(6);
        await Assert.That(result.Expanded[0].DisplayValue).IsEqualTo("Value1");
        await Assert.That(result.Expanded.Skip(1).All(member => member.DisplayValue == null)).IsTrue();
        await Assert.That(result.Budget.TruncationStage).IsEqualTo(QueryBudgetTruncationStages.CountsAndSamples);
    }

    [Test]
    public async Task FullBudget_PreservesResponseShapeWithoutTruncation()
    {
        var service = new QueryBudgetService();
        var request = new ObjectMemberGroupsRequest
        {
            ObjectHandle = "obj:1",
            LimitGroups = 10,
            LimitMembersPerGroup = 12,
            TokenBudgetHint = 2200
        };

        var response = new ObjectMemberGroupsResponse
        {
            ObjectHandle = "obj:1",
            Title = "Basic Wall, ID12345",
            Groups =
            [
                new MemberGroupResult
                {
                    DeclaringTypeName = "Element",
                    Depth = 1,
                    MemberCount = 3,
                    TopMembers = new List<string> { "Id", "Name", "Category" }
                }
            ]
        };

        var result = service.ApplyObjectMemberGroupsBudget(response, request);

        await Assert.That(result.Truncated).IsFalse();
        await Assert.That(result.Budget.Truncated).IsFalse();
        await Assert.That(result.Title).IsEqualTo("Basic Wall, ID12345");
        await Assert.That(result.Groups[0].TopMembers.Count).IsEqualTo(3);
    }
}
