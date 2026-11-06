using System;
using System.Runtime.InteropServices;
using Inventor;

// Read-only inspection of a drawing or part in the running Inventor (development aid).
internal static class Probe
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = (Inventor.Application)Marshal.GetActiveObject("Inventor.Application");
        if (args[0] == "idw") return Idw(app, args[1], args.Length > 2 ? int.Parse(args[2]) : 99);
        if (args[0] == "part") return Part(app, args[1]);
        return 1;
    }

    private static string B(Box b) => string.Format("({0:0.0},{1:0.0},{2:0.0})-({3:0.0},{4:0.0},{5:0.0})", b.MinPoint.X, b.MinPoint.Y, b.MinPoint.Z, b.MaxPoint.X, b.MaxPoint.Y, b.MaxPoint.Z);
    private static string B2(Box2d b) => b == null ? "-" : string.Format("({0:0.0},{1:0.0})-({2:0.0},{3:0.0})", b.MinPoint.X, b.MinPoint.Y, b.MaxPoint.X, b.MaxPoint.Y);

    private static int Idw(Inventor.Application app, string path, int maxSheets)
    {
        var d = (DrawingDocument)app.Documents.Open(path, false);
        try
        {
            int i = 0;
            foreach (Sheet s in d.Sheets)
            {
                if (++i > maxSheets) break;
                Box2d tb = null, bd = null;
                try { tb = s.TitleBlock.RangeBox; } catch { }
                try { bd = s.Border.RangeBox; } catch { }
                string tbInfo = "none";
                try { if (s.TitleBlock != null) tbInfo = s.TitleBlock.Definition.Name; } catch (Exception ex) { tbInfo = "err " + ex.Message; }
                Console.WriteLine("   titleblock=" + tbInfo + " format=" + (s.TitleBlock == null ? "" : ""));
                try { foreach (Sketch sk in new object[0]) { } } catch { }
                Console.WriteLine($"SHEET {s.Name} {s.Width:0.0}x{s.Height:0.0} tb={B2(tb)} border={B2(bd)} dims={s.DrawingDimensions.GeneralDimensions.Count} pl={s.PartsLists.Count}");
                foreach (DrawingView v in s.DrawingViews)
                {
                    string doc = "";
                    string rb = "";
                    try { var m = (Document)v.ReferencedDocumentDescriptor.ReferencedDocument; doc = m.DisplayName;
                          rb = m is PartDocument p ? B(p.ComponentDefinition.RangeBox) : B(((AssemblyDocument)m).ComponentDefinition.RangeBox); } catch { }
                    Console.WriteLine($"   view {v.Name} {v.ViewType} scale={v.Scale:0.####} W={v.Width:0.0} H={v.Height:0.0} C=({v.Center.X:0.0},{v.Center.Y:0.0}) rot={v.Rotation:0.00} {doc} model={rb}");
                }
            }
        }
        finally { d.Close(true); }
        return 0;
    }

    private static int Part(Inventor.Application app, string path)
    {
        var d = (PartDocument)app.Documents.Open(path, false);
        Console.WriteLine("subtype " + d.SubType + " range " + B(d.ComponentDefinition.RangeBox));
        foreach (SurfaceBody b in d.ComponentDefinition.SurfaceBodies)
            foreach (Face f in b.Faces)
            {
                string g = f.SurfaceType.ToString();
                try {
                    if (f.Geometry is Plane pl) g += $" n=({pl.Normal.X:0.00},{pl.Normal.Y:0.00},{pl.Normal.Z:0.00})";
                    if (f.Geometry is Cylinder c) g += $" ax=({c.AxisVector.X:0.00},{c.AxisVector.Y:0.00},{c.AxisVector.Z:0.00}) r={c.Radius:0.000}";
                } catch { }
                Console.WriteLine($"  {g} area={f.Evaluator.Area:0.00}");
            }
        foreach (AttributeSet a in ((Document)d).AttributeSets) Console.WriteLine("  attrset " + a.Name);
        return 0;
    }
}
