using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Windows.Forms;
using Inventor;
using Path = System.IO.Path;
using File = System.IO.File;
using Environment = System.Environment;

namespace AutoDrawDev
{
    /// <summary>
    /// Temporary development add-in: an "AutoDraw dev" button that runs the latest AutoDraw build. On each click it
    /// checks the build's AutoDraw.dll; when it is newer, the open AutoDraw window is closed, the build is copied to a
    /// temp folder (so the build output is never locked) and loaded into a fresh, unloadable load context.
    /// </summary>
    [ComVisible(true)]
    [Guid(ClientId)]
    [ProgId("AutoDrawDev.DevServer")]
    public class DevServer : ApplicationAddInServer
    {
        public const string ClientId = "5d2b8f0e-7c41-4e8a-9b6a-2f3c1d9e7a10";

        private Inventor.Application _app;
        private ButtonDefinition _button;
        private UserInterfaceEvents _uiEvents;
        private DevContext _context;
        private Assembly _autoDraw;
        private DateTime _loaded;
        /// <summary>Made on Inventor's main thread: calls from other programs arrive on a COM worker thread and are handed over.</summary>
        private Control _ui;

        private DevAutomation _automation;

        /// <summary>The same Open(paths) as AutoDraw's own Automation, run by the latest build.</summary>
        public object Automation => _automation ?? (_automation = new DevAutomation(this));

        private static void Log(string text)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoDrawDev");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "log.txt"), DateTime.Now.ToString("HH:mm:ss ") + text + Environment.NewLine);
            }
            catch { }
        }

        public void Activate(ApplicationAddInSite site, bool firstTime)
        {
            Log("Activate");
            try { Start(site); }
            catch (Exception ex) { Log(ex.ToString()); throw; }
        }

        private void Start(ApplicationAddInSite site)
        {
            _app = site.Application;
            _ui = new Control();
            IntPtr uiHandle = _ui.Handle;
            _button = _app.CommandManager.ControlDefinitions.AddButtonDefinition("AutoDraw dev", "AutoDraw.Dev.Start",
                CommandTypesEnum.kQueryOnlyCmdType, "{" + ClientId + "}", "Runs the latest AutoDraw build (reloaded when rebuilt)",
                "AutoDraw dev", Type.Missing, Type.Missing, ButtonDisplayEnum.kAlwaysDisplayText);
            _button.OnExecute += OnExecute;
            _uiEvents = _app.UserInterfaceManager.UserInterfaceEvents;
            _uiEvents.OnResetRibbonInterface += c => AddToRibbon();
            AddToRibbon();
        }

        public void Deactivate()
        {
            Unload();
            try { _button?.Delete(); } catch { }
            _button = null;
            _app = null;
        }

        public void ExecuteCommand(int commandID) { }

        private void AddToRibbon()
        {
            foreach (var (ribbon, tab) in new[] { ("Assembly", "id_TabAssemble"), ("Drawing", "id_TabPlaceViews"), ("Part", "id_TabModel"), ("Part", "id_TabSheetMetal") })
            {
                try
                {
                    RibbonTab t = _app.UserInterfaceManager.Ribbons[ribbon].RibbonTabs[tab];
                    RibbonPanel panel;
                    try { panel = t.RibbonPanels["AutoDraw.DevPanel"]; }
                    catch { panel = t.RibbonPanels.Add("AutoDraw dev", "AutoDraw.DevPanel", "{" + ClientId + "}", "", false); }
                    try { panel.CommandControls.AddButton(_button, false, true, "", false); } catch { }
                }
                catch { }
            }
        }

        /// <summary>The build folder, written by install-dev.ps1 next to this DLL.</summary>
        private static string BuildDir()
        {
            string here = Path.GetDirectoryName(typeof(DevServer).Assembly.Location) ?? "";
            string file = Path.Combine(here, "build.txt");
            return File.Exists(file) ? File.ReadAllText(file).Trim() : here;
        }

        private void OnExecute(NameValueMap context)
        {
            var owner = new Win(_app.MainFrameHWND);
            try
            {
                EnsureLatest();
                object error = Call("AutoDraw.UI.MainWindow", "Start", _app);
                if (error is string e) MessageBox.Show(owner, e, "AutoDraw dev");
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException t && t.InnerException != null ? t.InnerException : ex;
                MessageBox.Show(owner, inner.ToString(), "AutoDraw dev");
            }
        }

        /// <summary>Loads the build when it is newer than the one loaded.</summary>
        private void EnsureLatest()
        {
            string dll = Path.Combine(BuildDir(), "AutoDraw.dll");
            if (!File.Exists(dll)) throw new FileNotFoundException("No build at " + dll);
            DateTime stamp = File.GetLastWriteTimeUtc(dll);
            if (_autoDraw == null || stamp != _loaded) Load(dll, stamp);
        }

        /// <summary>Automation call from another program: forwarded to the latest build's AutoDrawAutomation.</summary>
        internal string Open(object paths)
        {
            if (_ui != null && _ui.IsHandleCreated && _ui.InvokeRequired)
                return (string)_ui.Invoke(new Func<string>(() => Open(paths)));
            try
            {
                EnsureLatest();
                object auto = Activator.CreateInstance(_autoDraw.GetType("AutoDraw.AutoDrawAutomation", true), _app);
                return auto.GetType().GetMethod("Open").Invoke(auto, new[] { paths }) as string ?? "";
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException t && t.InnerException != null ? t.InnerException : ex;
                Log("Open: " + inner);
                return inner.Message;
            }
        }

        private void Load(string dll, DateTime stamp)
        {
            Unload();
            string dir = Path.Combine(Path.GetTempPath(), "AutoDrawDev", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(dir);
            foreach (string f in new[] { "AutoDraw.dll", "AutoDraw.pdb", "AutoDraw.deps.json" })
            {
                string src = Path.Combine(Path.GetDirectoryName(dll), f);
                if (File.Exists(src)) File.Copy(src, Path.Combine(dir, f), true);
            }
            _context = new DevContext(dir);
            _autoDraw = _context.LoadFromAssemblyPath(Path.Combine(dir, "AutoDraw.dll"));
            _loaded = stamp;
            try { Call("AutoDraw.UI.InventorCommandIcons", "Init", _app); } catch { }
            try { _app.StatusBarText = "AutoDraw dev: loaded build of " + stamp.ToLocalTime().ToString("HH:mm:ss"); } catch { }
        }

        private void Unload()
        {
            if (_autoDraw == null) return;
            try { Call("AutoDraw.UI.MainWindow", "CloseActive"); } catch { }
            _autoDraw = null;
            try { _context?.Unload(); } catch { }
            _context = null;
            GC.Collect();
        }

        private object Call(string type, string method, params object[] args)
        {
            Type t = _autoDraw.GetType(type, true);
            MethodInfo m = t.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return m.Invoke(null, args);
        }

        private sealed class Win : IWin32Window
        {
            public Win(int h) { Handle = new IntPtr(h); }
            public IntPtr Handle { get; }
        }
    }

    [ComVisible(true)]
    [Guid("3a9c5e21-7d84-4f6b-b2e0-8c1d4a7f9e53")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface IDevAutomation
    {
        string Open(object paths);
    }

    [ComVisible(true)]
    [Guid("e27b4d90-1c6a-4e38-9f5d-6b0a3c8e2f14")]
    [ClassInterface(ClassInterfaceType.None)]
    public class DevAutomation : IDevAutomation
    {
        private readonly DevServer _server;
        internal DevAutomation(DevServer server) { _server = server; }
        public string Open(object paths) => _server.Open(paths);
    }

    /// <summary>Loads AutoDraw from the copy; Inventor's interop comes from this loader, so the COM types are the same.</summary>
    internal sealed class DevContext : AssemblyLoadContext
    {
        private readonly string _dir;
        public DevContext(string dir) : base("AutoDrawDev", true) { _dir = dir; }

        protected override Assembly Load(AssemblyName name)
        {
            if (name.Name == "Autodesk.Inventor.Interop") return typeof(Inventor.Application).Assembly;
            string p = Path.Combine(_dir, name.Name + ".dll");
            return File.Exists(p) ? LoadFromAssemblyPath(p) : null;
        }
    }
}
