using System;
using System.Collections.Generic;
using System.Linq;
using AutoDraw.Core;
using Inventor;
using Path = System.IO.Path;

namespace AutoDraw.Drawing
{
    /// <summary>One sheet of an existing drawing and the document it shows.</summary>
    internal sealed class SheetOwner
    {
        public Sheet Sheet;
        public string Key;       // full document name of the document its views show; null for a sheet without model views
        public string Name;      // the sheet's name as the browser shows it
    }

    /// <summary>
    /// Reads an existing drawing back: which document each sheet shows (the document of its first base view) and
    /// which assembly the drawing was made for (the first sheet that shows an assembly).
    /// </summary>
    internal static class SheetMap
    {
        public static List<SheetOwner> Read(DrawingDocument drawing)
        {
            var list = new List<SheetOwner>();
            foreach (Sheet s in drawing.Sheets)
            {
                string name = "";
                try { name = s.Name; } catch { }
                Document d = OwnerDoc(s);
                list.Add(new SheetOwner { Sheet = s, Key = d?.FullDocumentName, Name = name });
            }
            return list;
        }

        public static Document OwnerDoc(Sheet sheet)
        {
            Document any = null;
            try
            {
                foreach (DrawingView v in sheet.DrawingViews)
                {
                    Document d = null;
                    try { d = v.ReferencedDocumentDescriptor?.ReferencedDocument as Document; } catch { }
                    if (d == null) continue;
                    bool baseView = true;
                    try { baseView = v.ParentView == null; } catch { }
                    if (baseView) return d;
                    any = any ?? d;
                }
            }
            catch { }
            return any;
        }

        /// <summary>Custom iProperty of the drawing: the full file names of its assemblies, separated by |.</summary>
        public const string AssembliesProperty = "AutoDraw Assemblies";

        /// <summary>Stores which assemblies the drawing was made for, so it can be opened for all of them again.</summary>
        public static void Remember(DrawingDocument drawing, IList<AssemblyDocument> assemblies)
        {
            string value = string.Join("|", assemblies.Select(a => ((Document)a).FullFileName ?? ""));
            try { Props.Set((Document)drawing, AssembliesProperty, value); }
            catch (Exception ex) { Log.Write("Storing the assemblies in the drawing failed: " + ex.Message); }
        }

        /// <summary>
        /// The assemblies of a drawing made by AutoDraw: the ones stored in the drawing (looked up among the drawing's
        /// documents, by path and else by file name, else opened), else the assembly on its first assembly sheet.
        /// </summary>
        public static List<AssemblyDocument> FindAssemblies(Inventor.Application app, DrawingDocument drawing)
        {
            var list = new List<AssemblyDocument>();
            string stored = Props.Get((Document)drawing, AssembliesProperty);
            if (stored.Length > 0)
            {
                var refs = new List<Document>();
                try { foreach (Document d in ((Document)drawing).AllReferencedDocuments) refs.Add(d); } catch { }
                foreach (string path in stored.Split('|').Select(p => p.Trim()).Where(p => p.Length > 0))
                {
                    Document d = refs.FirstOrDefault(r => string.Equals(r.FullFileName, path, StringComparison.OrdinalIgnoreCase))
                        ?? refs.FirstOrDefault(r => string.Equals(Path.GetFileName(r.FullFileName ?? ""), Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
                    if (d == null && System.IO.File.Exists(path))
                    {
                        try { d = app.Documents.Open(path, false); }
                        catch (Exception ex) { Log.Write("Opening " + path + " failed: " + ex.Message); }
                    }
                    if (d is AssemblyDocument a && !list.Contains(a)) list.Add(a);
                    else if (d == null) Log.Write("Assembly of the drawing not found: " + path);
                }
            }
            if (list.Count == 0)
            {
                AssemblyDocument a = FindAssembly(drawing);
                if (a != null) list.Add(a);
            }
            return list;
        }

        /// <summary>The main assembly of a drawing made by AutoDraw: the assembly shown on the first sheet that shows one.</summary>
        public static AssemblyDocument FindAssembly(DrawingDocument drawing)
        {
            foreach (Sheet s in drawing.Sheets)
                if (OwnerDoc(s) is AssemblyDocument asm) return asm;
            foreach (Sheet s in drawing.Sheets)
                foreach (DrawingView v in s.DrawingViews)
                {
                    try { if (v.ReferencedDocumentDescriptor?.ReferencedDocument is AssemblyDocument asm) return asm; } catch { }
                }
            return null;
        }

        public static Dictionary<string, int> Counts(IEnumerable<SheetOwner> owners)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (SheetOwner o in owners)
                if (o.Key != null) counts[o.Key] = counts.TryGetValue(o.Key, out int c) ? c + 1 : 1;
            return counts;
        }
    }
}
