using RevitMCPCommandSet.Services.ConnectRvtLookup;

namespace RevitMCPCommandSet.Tests.ConnectRvtLookup;

public class SelectionRootProjectorTests
{
    [Test]
    public async Task CreateElementTitle_WithName_MatchesDescriptorStyle()
    {
        var title = ConnectRvtLookupRuntime.CreateElementTitle("Basic Wall", 12345);

        await Assert.That(title).IsEqualTo("Basic Wall, ID12345");
    }

    [Test]
    public async Task CreateElementTitle_WithoutName_FallsBackToIdOnly()
    {
        var titleFromNull = ConnectRvtLookupRuntime.CreateElementTitle(null, 42);
        var titleFromWhitespace = ConnectRvtLookupRuntime.CreateElementTitle("   ", 43);

        await Assert.That(titleFromNull).IsEqualTo("ID42");
        await Assert.That(titleFromWhitespace).IsEqualTo("ID43");
    }
}
