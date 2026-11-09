using System;
using System.Collections.Generic;
using Inventor;

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
