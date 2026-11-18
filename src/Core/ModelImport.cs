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
                d.SaveAs(target, false);
                Log.Write("Imported " + path + " as " + target);
                return d;
            }
            finally { app.SilentOperation = silent; }
        }
    }
}
