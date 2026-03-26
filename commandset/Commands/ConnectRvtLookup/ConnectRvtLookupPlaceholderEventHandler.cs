using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Commands.ConnectRvtLookup;

internal sealed class ConnectRvtLookupPlaceholderEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
{
    private readonly string _name;

    public ConnectRvtLookupPlaceholderEventHandler(string name)
    {
        _name = name;
    }

    public void Execute(UIApplication app)
    {
    }

    public string GetName()
    {
        return _name;
    }

    public bool WaitForCompletion(int timeoutMilliseconds = 10000)
    {
        return true;
    }
}
