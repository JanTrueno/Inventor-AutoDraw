using System;
using System.IO;
using Inventor;
using File = System.IO.File;
using Path = System.IO.Path;

namespace AutoDraw.Core
{
    /// <summary>
    /// Opens a model for AutoDraw: an .ipt or .iam as it is; a STEP as the .iam or .ipt of the same name next to it
    /// when there is one (so work done on it is kept), else imported and saved there (an assembly's parts go into a
    /// folder of the STEP's name, as Inventor proposes).
    /// </summary>
    internal static class ModelImport
    {
        public static bool IsStep(string path)
        {
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            return ext == ".stp" || ext == ".step";
        }

        public static Document Open(Inventor.Application app, string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("File not found: " + path);
            if (!IsStep(path)) return app.Documents.Open(path, false);

            foreach (string ext in new[] { ".iam", ".ipt" })
            {
                string existing = Path.ChangeExtension(path, ext);
                if (File.Exists(existing)) return app.Documents.Open(existing, false);
            }

            bool silent = app.SilentOperation;
            app.SilentOperation = true;
            try
            {
                Document d = app.Documents.Open(path, false);
                string target = Path.ChangeExtension(path, d is AssemblyDocument ? ".iam" : ".ipt");
                Release(app, target);
                d.SaveAs(target, false);
                Log.Write("Imported " + path + " as " + target);
                // the imported document's object does not survive the Save As (every call then fails with E_FAIL):
                // fetch the saved document by its new name
                return app.Documents.Open(target, false);
            }
            finally { app.SilentOperation = silent; }
        }

        /// <summary>
        /// The file was deleted (the STEP is new) but Inventor can still hold a document of that name in memory, and then
        /// Save As to it fails (E_INVALIDARG). A hidden, unchanged one is closed; one the user has open is not touched.
        /// </summary>
        private static void Release(Inventor.Application app, string target)
        {
            foreach (Document open in app.Documents)
            {
                string name;
                try { name = open.FullFileName; } catch { continue; }
                if (!string.Equals(name, target, StringComparison.OrdinalIgnoreCase)) continue;
                if (open.Views.Count == 0 && !open.Dirty)
                {
                    try { open.Close(true); Log.Write("Closed the old " + target + " (hidden in Inventor) before the import"); return; }
                    catch (Exception ex) { Log.Write("Closing the old " + target + " failed: " + ex.Message); }
                }
                throw new InvalidOperationException(Path.GetFileName(target) + " is still open in Inventor. Close it and try again.");
            }
        }
    }
}
