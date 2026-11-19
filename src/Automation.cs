using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AutoDraw.Core;
using AutoDraw.UI;
using Inventor;

namespace AutoDraw
{
    /// <summary>
    /// What other programs can call (through COM, as the add-in's Automation object):
    /// <code>
    /// inv = GetActiveObject("Inventor.Application")
    /// ad  = inv.ApplicationAddIns.ItemById("{0091494E-410A-4CC7-BFFC-1248D26C64BD}").Automation
    /// ad.Open(r"C:\x\a.stp;C:\x\b.ipt")     # or a list of paths
    /// </code>
    /// </summary>
    [ComVisible(true)]
    [Guid("6f1e2c3a-9b47-4d1e-8a52-3c7d9e0b4a61")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface IAutoDrawAutomation
    {
        /// <summary>
        /// Opens the AutoDraw window for these models: .iam, .ipt or STEP (imported to an .ipt/.iam next to it, or the
        /// existing one of that name). Paths separated by ; or | , or an array. Returns "" when the window is open, else why not.
        /// </summary>
        string Open(object paths);
    }

    [ComVisible(true)]
    [Guid("b84d2f17-5e3c-4a90-9d6e-1f0a7c2e5b38")]
    [ClassInterface(ClassInterfaceType.None)]
    public class AutoDrawAutomation : IAutoDrawAutomation
    {
        private readonly Inventor.Application _app;

        /// <summary>
        /// A control made on Inventor's main thread (by the add-in). Calls from other programs arrive on a COM worker
        /// thread; a window made there gets no messages and hangs, so the work is handed to the main thread.
        /// </summary>
        internal static Control Ui;

        public AutoDrawAutomation(Inventor.Application app) { _app = app; }

        public string Open(object paths)
        {
            Control ui = Ui;
            if (ui != null && ui.IsHandleCreated && ui.InvokeRequired)
                return (string)ui.Invoke(new Func<string>(() => OpenHere(paths)));
            return OpenHere(paths);
        }

        private string OpenHere(object paths)
        {
            try
            {
                List<string> list = Paths(paths);
                if (list.Count == 0) return "No files given.";
                var models = new List<Document>();
                var problems = new List<string>();
                foreach (string p in list)
                {
                    try
                    {
                        Document d = ModelImport.Open(_app, p);
                        if ((d is AssemblyDocument || d is PartDocument) && !models.Contains(d)) models.Add(d);
                        else if (!(d is AssemblyDocument || d is PartDocument)) problems.Add(p + ": not a part or assembly");
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Automation: opening " + p + " failed: " + ex);
                        problems.Add(p + ": " + ex.Message);
                    }
                }
                if (models.Count == 0) return string.Join("\n", problems);
                string error = MainWindow.OpenModels(_app, models);
                return string.Join("\n", (error != null ? new[] { error } : new string[0]).Concat(problems));
            }
            catch (Exception ex)
            {
                Log.Write("Automation: Open failed: " + ex);
                return ex.Message;
            }
        }

        private static List<string> Paths(object paths)
        {
            var list = new List<string>();
            if (paths is string s) list.AddRange(s.Split(new[] { ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));
            else if (paths is IEnumerable e) foreach (object o in e) if (o != null) list.Add(o.ToString());
            return list.Select(p => p.Trim().Trim('"')).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
