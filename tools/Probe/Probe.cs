using System;
using System.Runtime.InteropServices;
using Inventor;

// Read-only inspection of a drawing or part in the running Inventor (development aid).
internal static class Probe
{
    [STAThread]
    private static int Main(string[] args)
    {
        Inventor.Application app;
        bool own = false;
        try { app = (Inventor.Application)Marshal.GetActiveObject("Inventor.Application"); }
        catch
        {
            Console.WriteLine("starting a hidden Inventor…");
            app = (Inventor.Application)Activator.CreateInstance(Type.GetTypeFromProgID("Inventor.Application"));
            app.Visible = false;
            own = true;
        }
        try { return Run(app, args); }
        finally { if (own) { try { app.Quit(); } catch { } } }
    }

    private static int Run(Inventor.Application app, string[] args)
    {
        if (args[0] == "idw") return Idw(app, args[1], args.Length > 2 ? int.Parse(args[2]) : 99);
        if (args[0] == "part") return Part(app, args[1]);
        if (args[0] == "dims") return Dims(app, args[1], args[2]);
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

    private static string P(Point2d p) => string.Format("({0:0.00},{1:0.00})", p.X, p.Y);

    private static int Dims(Inventor.Application app, string path, string sheetName)
    {
        var d = (DrawingDocument)app.Documents.Open(path, false);
        try
        {
            Sheet sh = null;
            foreach (Sheet s in d.Sheets) if (s.Name.StartsWith(sheetName)) sh = s;
            Console.WriteLine("sheet " + sh.Name);
            var tg = app.TransientGeometry;
            foreach (DrawingView v in sh.DrawingViews)
            {
                Console.WriteLine($"VIEW {v.Name} {v.ViewType} style={v.ViewStyle} scale={v.Scale:0.###} C={P(v.Center)} W={v.Width:0.00} H={v.Height:0.00} flat={v.IsFlatPatternView} raster={TryRaster(v)}");
                DrawingCurve left = null, right = null; double lx = 1e9, rx = -1e9;
                int n = 0;
                foreach (DrawingCurve c in v.get_DrawingCurves(Type.Missing))
                {
                    string vis = "";
                    try { int vs = 0, hid = 0; foreach (DrawingCurveSegment sg in c.Segments) { if (sg.Visible) vs++; if (sg.HiddenLine) hid++; } vis = $"seg vis={vs} hidden={hid}"; } catch (Exception ex) { vis = "seg err " + ex.Message; }
                    string geo = "";
                    try { geo = P(c.StartPoint) + "-" + P(c.EndPoint); } catch { }
                    if (n++ < 12) Console.WriteLine($"   {c.ProjectedCurveType} {c.EdgeType} {vis} {geo}");
                    if (c.ProjectedCurveType == Curve2dTypeEnum.kLineSegmentCurve2d)
                    {
                        double x = c.StartPoint.X;
                        if (Math.Abs(c.EndPoint.X - x) < 1e-4) { if (x < lx) { lx = x; left = c; } if (x > rx) { rx = x; right = c; } }
                    }
                }
                Console.WriteLine($"   curves={n}");
                if (left == null || right == null || left == right) continue;
                var gd = sh.DrawingDimensions.GeneralDimensions;
                var pt = tg.CreatePoint2d((lx + rx) / 2, v.Center.Y + v.Height / 2 + 1);
                Try("whole lines, horizontal", () => gd.AddLinear(pt, sh.CreateGeometryIntent(left, Type.Missing), sh.CreateGeometryIntent(right, Type.Missing), DimensionTypeEnum.kHorizontalDimensionType, true, Type.Missing, Type.Missing));
                Try("whole lines, no type", () => gd.AddLinear(pt, sh.CreateGeometryIntent(left, Type.Missing), sh.CreateGeometryIntent(right, Type.Missing), DimensionTypeEnum.kAlignedDimensionType, true, Type.Missing, Type.Missing));
                Try("start points, horizontal", () => gd.AddLinear(pt, sh.CreateGeometryIntent(left, PointIntentEnum.kStartPointIntent), sh.CreateGeometryIntent(right, PointIntentEnum.kStartPointIntent), DimensionTypeEnum.kHorizontalDimensionType, true, Type.Missing, Type.Missing));
                Try("mid points, horizontal", () => gd.AddLinear(pt, sh.CreateGeometryIntent(left, PointIntentEnum.kMidPointIntent), sh.CreateGeometryIntent(right, PointIntentEnum.kMidPointIntent), DimensionTypeEnum.kHorizontalDimensionType, true, Type.Missing, Type.Missing));
                Try("whole lines, horizontal, arrange off", () => gd.AddLinear(pt, sh.CreateGeometryIntent(left, Type.Missing), sh.CreateGeometryIntent(right, Type.Missing), DimensionTypeEnum.kHorizontalDimensionType, false, Type.Missing, Type.Missing));
            }
        }
        finally { d.Close(true); }
        return 0;
    }

    private static string TryRaster(DrawingView v)
    {
        try { return Convert.ToString(v.GetType().InvokeMember("IsRasterView", System.Reflection.BindingFlags.GetProperty, null, v, null)); } catch { return "?"; }
    }

    private static void Try(string what, Func<object> f)
    {
        try { f(); Console.WriteLine("   OK    " + what); }
        catch (Exception ex) { Console.WriteLine("   FAIL  " + what + ": " + ex.Message); }
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
