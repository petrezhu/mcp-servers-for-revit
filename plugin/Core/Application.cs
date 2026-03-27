using System;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.UI;
using System.Reflection;
using System.Windows.Media.Imaging;
using RevitMCPSDK.API.Interfaces;
using revit_mcp_plugin.Utils;

namespace revit_mcp_plugin.Core
{
    public class Application : IExternalApplication
    {
        private const int MaxAutoStartAttempts = 10;

        private bool _autoStartPending;
        private int _autoStartAttempts;
        private ILogger _logger;

        public Result OnStartup(UIControlledApplication application)
        {
            _logger = new Logger();
            RibbonPanel mcpPanel = application.CreateRibbonPanel("Revit MCP Plugin");

            PushButtonData pushButtonData = new PushButtonData("ID_EXCMD_TOGGLE_REVIT_MCP", "Revit MCP\r\n Switch",
                Assembly.GetExecutingAssembly().Location, "revit_mcp_plugin.Core.MCPServiceConnection");
            pushButtonData.ToolTip = "Open / Close mcp server";
            pushButtonData.Image = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/icon-16.png", UriKind.RelativeOrAbsolute));
            pushButtonData.LargeImage = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/icon-32.png", UriKind.RelativeOrAbsolute));
            mcpPanel.AddItem(pushButtonData);

            PushButtonData mcp_settings_pushButtonData = new PushButtonData("ID_EXCMD_MCP_SETTINGS", "Settings",
                Assembly.GetExecutingAssembly().Location, "revit_mcp_plugin.Core.Settings");
            mcp_settings_pushButtonData.ToolTip = "MCP Settings";
            mcp_settings_pushButtonData.Image = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/settings-16.png", UriKind.RelativeOrAbsolute));
            mcp_settings_pushButtonData.LargeImage = new BitmapImage(new Uri("/RevitMCPPlugin;component/Core/Ressources/settings-32.png", UriKind.RelativeOrAbsolute));
            mcpPanel.AddItem(mcp_settings_pushButtonData);

            application.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;
            application.Idling += OnIdling;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            application.ControlledApplication.ApplicationInitialized -= OnApplicationInitialized;
            application.Idling -= OnIdling;

            try
            {
                if (SocketService.Instance.IsRunning)
                {
                    SocketService.Instance.Stop();
                }
            }
            catch { }

            return Result.Succeeded;
        }

        private void OnApplicationInitialized(object sender, ApplicationInitializedEventArgs e)
        {
            _autoStartPending = true;
            _autoStartAttempts = 0;
            _logger?.Info("Revit application initialized. MCP auto-start is pending.");
        }

        private void OnIdling(object sender, IdlingEventArgs e)
        {
            if (!_autoStartPending || SocketService.Instance.IsRunning)
            {
                return;
            }

            if (_autoStartAttempts >= MaxAutoStartAttempts)
            {
                _autoStartPending = false;
                _logger?.Warning("MCP auto-start stopped after {0} unsuccessful attempts.", MaxAutoStartAttempts);
                return;
            }

            _autoStartAttempts++;

            try
            {
                UIApplication uiApplication = sender as UIApplication;
                if (uiApplication == null && sender is Autodesk.Revit.ApplicationServices.Application revitApplication)
                {
                    uiApplication = new UIApplication(revitApplication);
                }

                if (uiApplication == null)
                {
                    _logger?.Warning("MCP auto-start attempt {0} skipped because no UIApplication was available.", _autoStartAttempts);
                    return;
                }

                SocketService.Instance.Initialize(uiApplication);
                SocketService.Instance.Start();

                if (SocketService.Instance.IsRunning)
                {
                    _autoStartPending = false;
                    _logger?.Info("MCP server auto-started successfully on idle attempt {0}.", _autoStartAttempts);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error("MCP auto-start attempt {0} failed: {1}", _autoStartAttempts, ex.Message);
            }
        }
    }
}
