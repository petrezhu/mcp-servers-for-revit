using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    public async Task SelectionRootsRequest_Validate_RejectsUnsupportedSource()
    {
        var request = new SelectionRootsRequest
        {
            Source = "all_documents"
        };

        var isValid = request.Validate(out var errorMessage);

        await Assert.That(isValid).IsFalse();
        await Assert.That(errorMessage).Contains("Unsupported source");
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
    public async Task SelectionRootsResult_UsesObjectMemberGroupsAsNextBestAction()
    {
        var result = new QueryCommandResult<SelectionRootsResponse>
        {
            Success = true,
            Data = new SelectionRootsResponse(),
            CompletionHint = "answer_ready",
            NextBestAction = ConnectRvtLookupCommandNames.ObjectMemberGroups,
            RetryRecommended = false
        };

        var json = JsonConvert.SerializeObject(result);

        await Assert.That(json).Contains("\"nextBestAction\":\"object_member_groups\"");
    }

    [Test]
    public async Task SelectionRootsResponse_SerializesGroupedRootItems()
    {
        var response = new SelectionRootsResponse
        {
            Source = SelectionRootsSources.Selection,
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
        await Assert.That(json.Contains("memberCount")).IsFalse();
        await Assert.That(json.Contains("topMembers")).IsFalse();
    }

    [Test]
    public async Task CommandManifest_RegistersConnectRvtLookupCommandsAlongsideExecute()
    {
        var manifestPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "command.json"));
        var manifest = JObject.Parse(await File.ReadAllTextAsync(manifestPath));
        var commandNames = manifest["commands"]?
            .Select(token => token["commandName"]?.Value<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        await Assert.That(commandNames).Contains("execute");
        await Assert.That(commandNames).Contains("selection_roots");
        await Assert.That(commandNames).Contains("object_member_groups");
        await Assert.That(commandNames).Contains("expand_members");
        await Assert.That(commandNames).Contains("navigate_object");
    }
}
