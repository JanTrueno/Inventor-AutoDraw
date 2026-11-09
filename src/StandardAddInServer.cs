using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AutoDraw.UI;
using Inventor;

namespace AutoDraw
{
    /// <summary>
    /// Inventor add-in entry point: one "AutoDraw" button on the Assemble tab of the assembly ribbon and on the Place
    /// Views tab of the drawing ribbon. It opens the AutoDraw window for the open assembly, or for the open drawing.
    /// </summary>
    [ComVisible(true)]
    [Guid(ClientId)]
    [ProgId("AutoDraw.StandardAddInServer")]
    public class StandardAddInServer : ApplicationAddInServer
    {
        public const string ClientId = "0091494e-410a-4cc7-bffc-1248d26c64bd";
        private const string PanelName = "AutoDraw.Panel";

        private Inventor.Application _app;
        private ButtonDefinition _button;
        private UserInterfaceEvents _uiEvents;

        public object Automation => null;

        public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
        {
            _app = addInSiteObject.Application;
            string clientGuid = "{" + ClientId + "}";
            InventorCommandIcons.Init(_app);

            // Inventor's own "Create Drawing View" icon, read from the running Inventor; Base View, then a drawn icon, if not found
            object small = InventorCommandIcons.Picture("UCxCreateDrawingViewCmd", false) ?? InventorCommandIcons.Picture("DrawingBaseViewCmd", false) ?? Icons.Create(16);
            object large = InventorCommandIcons.Picture("UCxCreateDrawingViewCmd", true) ?? InventorCommandIcons.Picture("DrawingBaseViewCmd", true) ?? Icons.Create(32);

            _button = _app.CommandManager.ControlDefinitions.AddButtonDefinition(
                "AutoDraw", "AutoDraw.Start", CommandTypesEnum.kQueryOnlyCmdType, clientGuid,
                "Generate the production drawing of this assembly, or update a drawing made by AutoDraw",
                "AutoDraw\nOpens a window with the parts and sub-assemblies of this assembly and makes one drawing with a sheet for each of them: views, parts lists, balloons and dimensions, saved next to the assembly. Started from such a drawing, it adds, redoes, deletes and re-sorts its sheets.",
                small, large, ButtonDisplayEnum.kDisplayTextInLearningMode);
            _button.OnExecute += OnExecute;

            _uiEvents = _app.UserInterfaceManager.UserInterfaceEvents;
            _uiEvents.OnResetRibbonInterface += OnResetRibbon;
            AddToRibbon();
        }

        public void Deactivate()
        {
            try { _uiEvents.OnResetRibbonInterface -= OnResetRibbon; } catch { }
            try { _button.OnExecute -= OnExecute; } catch { }
            try { MainWindow.CloseActive(); } catch { }
            try { _button?.Delete(); } catch { }
            _button = null;
            _uiEvents = null;
            _app = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public void ExecuteCommand(int commandID) { }

        private void OnResetRibbon(NameValueMap context) => AddToRibbon();

        private void AddToRibbon()
        {
            AddTo("Assembly", "id_TabAssemble");     // make the drawing
            AddTo("Drawing", "id_TabPlaceViews");    // update a drawing made by AutoDraw
        }

        private void AddTo(string ribbon, string tabId)
        {
            try
            {
                RibbonTab tab = _app.UserInterfaceManager.Ribbons[ribbon].RibbonTabs[tabId];
                RibbonPanel panel;
                try { panel = tab.RibbonPanels[PanelName]; }
                catch { panel = tab.RibbonPanels.Add("AutoDraw", PanelName, "{" + ClientId + "}", "", false); }
                try { panel.CommandControls.AddButton(_button, true, true, "", false); } catch { /* already added */ }
            }
            catch (Exception ex) { Core.Log.Write("Adding the ribbon button to " + ribbon + " failed: " + ex.Message); }
        }

        private void OnExecute(NameValueMap context)
        {
            string error = MainWindow.Start(_app);
            if (error != null)
                MessageBox.Show(new InventorWindow(_app.MainFrameHWND), error, "AutoDraw", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
