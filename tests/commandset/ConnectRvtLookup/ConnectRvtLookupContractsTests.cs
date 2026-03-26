using Newtonsoft.Json;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Commands.ConnectRvtLookup;
using RevitMCPCommandSet.Models.ConnectRvtLookup;

namespace RevitMCPCommandSet.Tests.ConnectRvtLookup;

public class ConnectRvtLookupContractsTests
{
    [Test]
    public async Task CommandSkeletons_ExposeExpectedNames_AndInheritExternalEventCommandBase()
    {
        await Assert.That(typeof(SelectionRootsCommand).IsSubclassOf(typeof(ExternalEventCommandBase))).IsTrue();
        await Assert.That(typeof(ObjectMemberGroupsCommand).IsSubclassOf(typeof(ExternalEventCommandBase))).IsTrue();
        await Assert.That(typeof(ExpandMembersCommand).IsSubclassOf(typeof(ExternalEventCommandBase))).IsTrue();
        await Assert.That(typeof(NavigateObjectCommand).IsSubclassOf(typeof(ExternalEventCommandBase))).IsTrue();

        await Assert.That(ConnectRvtLookupCommandNames.SelectionRoots).IsEqualTo("selection_roots");
        await Assert.That(ConnectRvtLookupCommandNames.ObjectMemberGroups).IsEqualTo("object_member_groups");
        await Assert.That(ConnectRvtLookupCommandNames.ExpandMembers).IsEqualTo("expand_members");
        await Assert.That(ConnectRvtLookupCommandNames.NavigateObject).IsEqualTo("navigate_object");
    }

    [Test]
    public async Task ExpandMembersRequest_DeserializeAndValidate_Succeeds()
    {
        const string json = """
        {
          "objectHandle": "obj:1",
          "members": [
            { "declaringTypeName": "Wall", "memberName": "Width" },
            { "declaringTypeName": "Element", "memberName": "BoundingBox" }
          ],
          "tokenBudgetHint": 2000
        }
        """;

        var request = JsonConvert.DeserializeObject<ExpandMembersRequest>(json);
        var isValid = request.Validate(out var errorMessage);

        await Assert.That(isValid).IsTrue();
        await Assert.That(errorMessage).IsNull();
        await Assert.That(request.ObjectHandle).IsEqualTo("obj:1");
        await Assert.That(request.Members.Count).IsEqualTo(2);
        await Assert.That(request.Members[1].MemberName).IsEqualTo("BoundingBox");
    }

    [Test]
    public async Task QueryCommandResult_SerializesExpectedContract()
    {
        var result = QueryCommandResults.NotImplemented<SelectionRootsResponse>(ConnectRvtLookupCommandNames.SelectionRoots);
        var json = JsonConvert.SerializeObject(result);

        await Assert.That(json).Contains("\"success\":false");
        await Assert.That(json).Contains("\"errorCode\":\"ERR_QUERY_NOT_IMPLEMENTED\"");
        await Assert.That(json).Contains("\"nextBestAction\":\"implement_query_handler\"");
    }

    [Test]
    public async Task SelectionRootsResponse_SerializesGroupedRootItems()
    {
        var response = new SelectionRootsResponse
        {
            Source = "selection_or_active_view",
            TotalRootCount = 1,
            Groups =
            [
                new RootGroupResult
                {
                    GroupKey = "Wall",
                    Count = 1,
                    Items =
                    [
                        new RootItemResult
                        {
                            ObjectHandle = "obj:1",
                            ElementId = 12345,
                            Title = "Basic Wall, ID12345",
                            TypeName = "Wall",
                            Category = "Walls"
                        }
                    ]
                }
            ]
        };

        var json = JsonConvert.SerializeObject(response);

        await Assert.That(json).Contains("\"groupKey\":\"Wall\"");
        await Assert.That(json).Contains("\"objectHandle\":\"obj:1\"");
        await Assert.That(json).Contains("\"elementId\":12345");
        await Assert.That(json).Contains("\"typeName\":\"Wall\"");
    }
}
