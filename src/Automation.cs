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

        /// <summary>
        /// As <see cref="Open"/>, then fills in fields and switches ticks: "key=value; key=value" or a JSON object, e.g.
        /// "order=160261777; template=C:\t\A3.idw; Tube=off; General=off". Keys: order, orderProperty, template,
        /// twoSheetsAbove, up (Z/Y), drawingProperty, sheetName, rules, askCheckOut (on/off), and a type
        /// (Assembly, Bent, Flat, Lathe, Tube, General) = on/off.
        /// </summary>
        string OpenWith(object paths, string options);
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

        public string Open(object paths) => OpenWith(paths, null);

        public string OpenWith(object paths, string options)
        {
            Control ui = Ui;
            if (ui != null && ui.IsHandleCreated && ui.InvokeRequired)
                return (string)ui.Invoke(new Func<string>(() => OpenHere(paths, options)));
            return OpenHere(paths, options);
        }

        private string OpenHere(object paths, string options)
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
                if (WantsSheetMetal(options)) problems.AddRange(MakeSheetMetal(models));
                string error = MainWindow.OpenModels(_app, models, options);
                return string.Join("\n", (error != null ? new[] { error } : new string[0]).Concat(problems));
            }
            catch (Exception ex)
            {
                Log.Write("Automation: Open failed: " + ex);
                return ex.Message;
            }
        }

        /// <summary>Option sheetMetal=on: the models are sheet metal (e.g. sent from CADMAN), plain solids are converted.</summary>
        private static bool WantsSheetMetal(string options)
        {
            try { return WindowOptions.Parse(options).Any(kv => kv.Key == "sheetmetal" && WindowOptions.Bool(kv.Value) == true); }
            catch { return false; }
        }

        /// <summary>Converts the thin plain solid parts among the models (and in their assemblies) to sheet metal and saves them.</summary>
        private IEnumerable<string> MakeSheetMetal(List<Document> models)
        {
            var parts = new List<PartDocument>();
            foreach (Document m in models)
            {
                if (m is PartDocument p) parts.Add(p);
                else try { foreach (Document d in m.AllReferencedDocuments) if (d is PartDocument rp) parts.Add(rp); } catch { }
            }
            var problems = new List<string>();
            bool silent = _app.SilentOperation;
            _app.SilentOperation = true;
            try
            {
                foreach (PartDocument p in parts.Distinct())
                {
                    if (Drawing.SheetMetalConvert.IsSheetMetal(p) || Drawing.SheetMetalConvert.SheetThickness(p) <= 0) continue;
                    if (!Drawing.SheetMetalConvert.Convert(p, out string why)) { problems.Add(((Document)p).DisplayName + ": not converted to sheet metal (" + why + ")"); continue; }
                    try { ((Document)p).Save(); }
                    catch (Exception ex) { problems.Add(((Document)p).DisplayName + ": converted, not saved (" + ex.Message + ")"); }
                }
            }
            finally { _app.SilentOperation = silent; }
            return problems;
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
