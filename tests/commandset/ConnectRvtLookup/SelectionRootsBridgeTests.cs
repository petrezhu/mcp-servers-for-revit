using RevitMCPCommandSet.Models.ConnectRvtLookup;
using RevitMCPCommandSet.Services.ConnectRvtLookup;

namespace RevitMCPCommandSet.Tests.ConnectRvtLookup;

public class SelectionRootsBridgeTests
{
    [Test]
    public async Task ResolveActualSource_SelectionOrActiveView_WithSelection_UsesSelection()
    {
        var source = SelectionRootsBridge.ResolveActualSource(SelectionRootsSources.SelectionOrActiveView, true);

        await Assert.That(source).IsEqualTo(SelectionRootsSources.Selection);
    }

    [Test]
    public async Task ResolveActualSource_SelectionOrActiveView_WithoutSelection_UsesActiveView()
    {
        var source = SelectionRootsBridge.ResolveActualSource(SelectionRootsSources.SelectionOrActiveView, false);

        await Assert.That(source).IsEqualTo(SelectionRootsSources.ActiveView);
    }

    [Test]
    public async Task ResolveActualSource_ExplicitSelection_PreservesSelection()
    {
        var source = SelectionRootsBridge.ResolveActualSource(SelectionRootsSources.Selection, false);

        await Assert.That(source).IsEqualTo(SelectionRootsSources.Selection);
    }

    [Test]
    public async Task ResolveActualSource_ExplicitActiveView_PreservesActiveView()
    {
        var source = SelectionRootsBridge.ResolveActualSource(SelectionRootsSources.ActiveView, true);

        await Assert.That(source).IsEqualTo(SelectionRootsSources.ActiveView);
    }
}
