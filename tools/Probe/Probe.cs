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
        if (args[0] == "sheets") return Sheets(app, args[1]);
        if (args[0] == "panes") return Panes(app, args[1]);
        if (args[0] == "newsheet") return NewSheet(app, args[1], args[2]);
        if (args[0] == "commands") return Commands(app, args[1]);
        if (args[0] == "find") return Find(app, args[1]);
        if (args[0] == "import") return Import(app, args[1]);
        if (args[0] == "tosheet") return ToSheet(app, args[1]);
        if (args[0] == "redim") return Redim(app, args[1], args[2]);
        if (args[0] == "convert")
        {
            // imports a STEP to a copy .ipt next to it, converts it as AutoDraw does and prints the detected type
            app.SilentOperation = true;
            var d = app.Documents.Open(args[1], false);
            string ipt = System.IO.Path.ChangeExtension(args[1], ".ipt");
            if (System.IO.File.Exists(ipt)) System.IO.File.Delete(ipt);
            d.SaveAs(ipt, false);
            var p = (PartDocument)app.Documents.Open(ipt, false);
            Console.WriteLine("imported: " + AutoDraw.Drawing.SheetMetalConvert.IsImported(p) + ", thickness " + AutoDraw.Drawing.SheetMetalConvert.SheetThickness(p) * 10 + " mm");
            Console.WriteLine("convert: " + AutoDraw.Drawing.SheetMetalConvert.Convert(p, out string why) + " " + why);
            Console.WriteLine("type: " + AutoDraw.Core.KindDetector.Detect((Document)p));
            p.Close(true);
            return 0;
        }
        if (args[0] == "thick")
        {
            var p = (PartDocument)app.Documents.Open(args[1], false);
            foreach (SurfaceBody b in p.ComponentDefinition.SurfaceBodies)
            {
                double area = 0; foreach (Face f in b.Faces) area += f.Evaluator.Area;
                Console.WriteLine($"faces: {AutoDraw.Drawing.Thickness.OfFaces(b) * 10:0.00} mm   2V/A: {20 * b.Volume[0.001] / area:0.000} mm");
            }
            if (p.ComponentDefinition is SheetMetalComponentDefinition sm) Console.WriteLine($"sheet metal {sm.Thickness.Expression}, bends {sm.Bends.Count}, flat {sm.HasFlatPattern}");
            p.Close(true);
            return 0;
        }
        if (args[0] == "activate") { var ai = app.ApplicationAddIns.ItemById[args[1]]; try { ai.Activate(); Console.WriteLine("active=" + ai.Activated); } catch (Exception ex) { Console.WriteLine(ex); } return 0; }
        return 1;
    }

    // control definitions whose internal or display name contains the text (e.g. Vault), and the add-ins that are loaded
    private static int Commands(Inventor.Application app, string text)
    {
        foreach (ControlDefinition c in app.CommandManager.ControlDefinitions)
        {
            string id = "", name = "";
            try { id = c.InternalName; name = c.DisplayName; } catch { }
            if (id.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                Console.WriteLine(id + "  |  " + name);
        }
        foreach (ApplicationAddIn a in app.ApplicationAddIns)
        {
            try { if (a.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) Console.WriteLine("add-in: " + a.DisplayName + " " + a.ClientId + " active=" + a.Activated); } catch { }
        }
        return 0;
    }

    // which document each sheet shows (first base view), and a test move of the last sheet to the front in the
    // browser; the drawing is closed without saving
    private static int Sheets(Inventor.Application app, string path)
    {
        var d = (DrawingDocument)app.Documents.Open(path, false);
        try
        {
            foreach (Sheet s in d.Sheets)
            {
                string owner = "-";
                foreach (DrawingView v in s.DrawingViews)
                {
                    Document m = null;
                    try { m = (Document)v.ReferencedDocumentDescriptor.ReferencedDocument; } catch { }
                    if (m == null) continue;
                    bool baseView = true;
                    try { baseView = v.ParentView == null; } catch { }
                    if (baseView) { owner = (m is AssemblyDocument ? "ASM " : "") + System.IO.Path.GetFileName(m.FullFileName); break; }
                }
                Console.WriteLine(s.Name + "  ->  " + owner);
            }
            BrowserPane pane = null;
            foreach (BrowserPane p in ((Document)d).BrowserPanes)
            {
                try { if (p.GetBrowserNodeFromObject(d.Sheets[1]) != null) { pane = p; break; } } catch { }
            }
            Console.WriteLine("pane: " + (pane == null ? "none" : pane.Name));
            if (pane != null)
            {
                Sheet last = d.Sheets[d.Sheets.Count];
                string name = last.Name;
                last.Activate();
                try
                {
                    pane.Reorder(pane.GetBrowserNodeFromObject(d.Sheets[1]), true, pane.GetBrowserNodeFromObject(last), Type.Missing);
                    Console.WriteLine("active sheet moved: OK");
                }
                catch (Exception ex) { Console.WriteLine("active sheet moved: FAILED " + ex.Message); }
                Sheet target = d.Sheets[1];
                if (target.Name == last.Name) target = d.Sheets[2];
                target.Activate();
                try
                {
                    pane.Reorder(pane.GetBrowserNodeFromObject(target), true, pane.GetBrowserNodeFromObject(last), Type.Missing);
                    Console.WriteLine("target active, moved: OK");
                }
                catch (Exception ex) { Console.WriteLine("target active, moved: FAILED " + ex.Message); }
                Console.WriteLine("moved " + name + "; now first: " + d.Sheets[1].Name + ", second: " + d.Sheets[2].Name);
            }
        }
        finally { ((Document)d).Close(true); }
        return 0;
    }

    // opens the drawing visibly and tries a sheet move in every browser pane; closed without saving
    private static int Panes(Inventor.Application app, string path)
    {
        var d = (DrawingDocument)app.Documents.Open(path, true);
        try
        {
            foreach (BrowserPane p in ((Document)d).BrowserPanes)
            {
                string info = p.Name + " / " + p.InternalName + " visible=" + p.Visible;
                try
                {
                    BrowserNode n1 = p.GetBrowserNodeFromObject(d.Sheets[1]);
                    BrowserNode n3 = p.GetBrowserNodeFromObject(d.Sheets[3]);
                    info += " nodes=" + (n1 != null) + "," + (n3 != null);
                    p.Reorder(n1, true, n3, Type.Missing);
                    info += " move OK -> first " + d.Sheets[1].Name;
                }
                catch (Exception ex) { info += " FAILED " + ex.Message; }
                Console.WriteLine(info);
            }
        }
        finally { ((Document)d).Close(true); }
        return 0;
    }

    // as AutoDraw's update: copies the template's first sheet into the drawing (opened visibly), then moves it to
    // place 3, first as it is (the new sheet active), then with the target activated; closed without saving
    private static int NewSheet(Inventor.Application app, string path, string template)
    {
        var d = (DrawingDocument)app.Documents.Open(path, true);
        var t = (DrawingDocument)app.Documents.Open(template, false);
        try
        {
            int k = 0;
            foreach (Sheet sh in d.Sheets) { k++; try { sh.Name = "0448.100." + k.ToString("000") + "-Z"; } catch (Exception ex) { Console.WriteLine("rename failed " + ex.Message); } }
            Console.WriteLine("renamed: " + d.Sheets[1].Name + ", " + d.Sheets[2].Name);
            Sheet copy = t.Sheets[1].CopyTo((_DrawingDocument)d);
            ((Document)t).Close(true);
            t = null;
            copy.Activate();
            Console.WriteLine("new sheet " + copy.Name + " at " + d.Sheets.Count);
            BrowserPane pane = ((Document)d).BrowserPanes["DlHierarchy"];
            BrowserNode nc = null;
            try { nc = pane.GetBrowserNodeFromObject(copy); Console.WriteLine("node of the new sheet: " + (nc != null)); }
            catch (Exception ex) { Console.WriteLine("node of the new sheet FAILED " + ex.Message); }
            try
            {
                pane.Reorder(pane.GetBrowserNodeFromObject(d.Sheets[3]), true, pane.GetBrowserNodeFromObject(copy), Type.Missing);
                Console.WriteLine("move (new sheet active): OK -> 3rd " + d.Sheets[3].Name);
            }
            catch (Exception ex) { Console.WriteLine("move (new sheet active): FAILED " + ex.Message); }
            Sheet target = d.Sheets[5];
            target.Activate();
            try
            {
                pane.Reorder(pane.GetBrowserNodeFromObject(target), true, pane.GetBrowserNodeFromObject(copy), Type.Missing);
                Console.WriteLine("move (target active): OK -> 5th " + d.Sheets[5].Name);
            }
            catch (Exception ex) { Console.WriteLine("move (target active): FAILED " + ex.Message); }
            try
            {
                pane.Reorder(pane.GetBrowserNodeFromObject(d.Sheets[2]), false, pane.GetBrowserNodeFromObject(copy), Type.Missing);
                Console.WriteLine("move after (Before=false): OK -> 3rd " + d.Sheets[3].Name);
            }
            catch (Exception ex) { Console.WriteLine("move after: FAILED " + ex.Message); }
        }
        finally
        {
            if (t != null) ((Document)t).Close(true);
            ((Document)d).Close(true);
        }
        return 0;
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

    // documents open in Inventor (visible or not) whose name contains the text: type, bodies, sheet metal data, size
    private static int Find(Inventor.Application app, string text)
    {
        foreach (Document d in app.Documents)
        {
            if ((d.DisplayName ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 && (d.FullFileName ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine(d.FullFileName + "  type=" + d.DocumentType + "  subtype=" + d.SubType);
            if (d is PartDocument p)
            {
                var def = p.ComponentDefinition;
                int bodies = 0, faces = 0;
                foreach (SurfaceBody b in def.SurfaceBodies) { bodies++; faces += b.Faces.Count; Console.WriteLine("  body " + b.Name + " solid=" + b.IsSolid + " faces=" + b.Faces.Count); }
                Console.WriteLine("  bodies=" + bodies + " faces=" + faces + " features=" + def.Features.Count + " range=" + B(def.RangeBox));
                if (def is SheetMetalComponentDefinition sm)
                {
                    try { Console.WriteLine("  bends=" + sm.Bends.Count + " hasFlat=" + sm.HasFlatPattern + " thickness=" + sm.Thickness.Value); } catch (Exception ex) { Console.WriteLine("  sm " + ex.Message); }
                    try { foreach (Bend bd in sm.Bends) Console.WriteLine("   bend front faces " + bd.FrontFaces.Count + " back faces " + bd.BackFaces.Count); } catch (Exception ex) { Console.WriteLine("   bends " + ex.Message); }
                    try { foreach (PartFeature f in sm.Features) Console.WriteLine("   feature " + f.Name + " " + f.Type + " suppressed=" + f.Suppressed); } catch { }
                }
            }
        }
        return 0;
    }

    // imports a STEP (give a copy in a temp folder): what Inventor makes of it, then Save As next to it, then closes
    private static int Import(Inventor.Application app, string path)
    {
        bool silent = app.SilentOperation;
        app.SilentOperation = true;
        try
        {
            Document d = app.Documents.Open(path, false);
            Console.WriteLine("type=" + d.DocumentType + " full='" + d.FullFileName + "' dirty=" + d.Dirty + " refs=" + d.AllReferencedDocuments.Count);
            foreach (Document r in d.AllReferencedDocuments) Console.WriteLine("  ref " + r.DocumentType + " '" + r.FullFileName + "' display=" + r.DisplayName);
            string target = System.IO.Path.ChangeExtension(path, d is AssemblyDocument ? ".iam" : ".ipt");
            try { d.SaveAs(target, false); Console.WriteLine("saved as " + target + " full now '" + d.FullFileName + "'"); }
            catch (Exception ex) { Console.WriteLine("SaveAs failed: " + ex.Message); }
            foreach (Document r in d.AllReferencedDocuments) Console.WriteLine("  after ref '" + r.FullFileName + "'");
            d.Close(true);
        }
        finally { app.SilentOperation = silent; }
        foreach (string f in System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(path), "*", System.IO.SearchOption.AllDirectories)) Console.WriteLine("file " + f);
        return 0;
    }

    // imports a STEP (a copy!) and tries to make sheet metal of it: thickness from volume and area, convert, unfold.
    // Nothing is saved.
    // dimensions the first sheet of a copy of a drawing again with the current Dimensioner (all dimensions and notes
    // removed first) and exports it as PDF; the original is not touched
    private static int Redim(Inventor.Application app, string idw, string pdf)
    {
        string copy = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ad_redim_" + System.IO.Path.GetFileName(idw));
        System.IO.File.Copy(idw, copy, true);
        bool silent = app.SilentOperation;
        app.SilentOperation = true;
        var d = (DrawingDocument)app.Documents.Open(copy, false);
        try
        {
            Sheet sheet = d.Sheets[1];
            for (int i = sheet.DrawingDimensions.GeneralDimensions.Count; i >= 1; i--) { try { sheet.DrawingDimensions.GeneralDimensions[i].Delete(); } catch { } }
            for (int i = sheet.DrawingDimensions.OrdinateDimensionSets.Count; i >= 1; i--) { try { sheet.DrawingDimensions.OrdinateDimensionSets[i].Delete(); } catch { } }
            for (int i = sheet.DrawingNotes.BendNotes.Count; i >= 1; i--) { try { sheet.DrawingNotes.BendNotes[i].Delete(); } catch { } }
            var dim = new AutoDraw.Drawing.Dimensioner(app, sheet);
            DrawingView flat = null, profile = null;
            int arcs = -1;
            foreach (DrawingView v in sheet.DrawingViews)
            {
                if (v.IsFlatPatternView) { if (flat == null || v.Width > flat.Width) flat = v; continue; }
                if (v.Camera.ViewOrientationType.ToString().Contains("Iso")) continue;
                int a = AutoDraw.Drawing.Dimensioner.ArcCount(v);
                Console.WriteLine($"view {v.Name} arcs={a} {v.Width:0.0}x{v.Height:0.0}");
                if (a > arcs) { arcs = a; profile = v; }
            }
            foreach (DrawingView v in sheet.DrawingViews)
                if (v != profile && v != flat && Math.Min(v.Width, v.Height) >= 0.5 && !v.Camera.ViewOrientationType.ToString().Contains("Iso")) dim.Overall(v, true, true);
            if (profile != null) { dim.Overall(profile, true, true, 1); dim.Legs(profile); dim.Angles(profile, true); }
            if (flat != null)
            {
                dim.Overall(flat, true, true, 1);
                dim.FlatPattern(flat);
                // the edge view: the thin view right of the flat pattern
                DrawingView edge = null;
                foreach (DrawingView v in sheet.DrawingViews)
                    if (v != flat && v.Center.X > flat.Center.X + flat.Width / 2 && Math.Min(v.Width, v.Height) < 0.5 && Math.Abs(v.Center.Y - flat.Center.Y) < 1) edge = v;
                Console.WriteLine("edge view: " + (edge?.Name ?? "none"));
                dim.ClearRight(edge);
                if (edge != null) dim.Overall(edge, true, false);
            }
            Console.WriteLine($"general={sheet.DrawingDimensions.GeneralDimensions.Count} ordinate sets={sheet.DrawingDimensions.OrdinateDimensionSets.Count} bend notes={sheet.DrawingNotes.BendNotes.Count}");
            d.SaveAs(pdf, true);
            Console.WriteLine("pdf: " + pdf);
        }
        finally { d.Close(true); app.SilentOperation = silent; }
        return 0;
    }

    private static int ToSheet(Inventor.Application app, string path)
    {
        bool silent = app.SilentOperation;
        app.SilentOperation = true;
        PartDocument d = null;
        try
        {
            d = (PartDocument)app.Documents.Open(path, false);
            var def = d.ComponentDefinition;
            double vol = 0, area = 0; int faces = 0, cyl = 0, planes = 0;
            foreach (SurfaceBody b in def.SurfaceBodies)
            {
                vol += b.Volume[0.001];
                foreach (Face f in b.Faces) { area += f.Evaluator.Area; faces++; if (f.SurfaceType == SurfaceTypeEnum.kCylinderSurface) cyl++; if (f.SurfaceType == SurfaceTypeEnum.kPlaneSurface) planes++; }
            }
            double t = 2 * vol / area;
            Console.WriteLine($"bodies={def.SurfaceBodies.Count} faces={faces} planes={planes} cylinders={cyl} volume={vol:0.000} cm3 area={area:0.00} cm2 t~{t * 10:0.000} mm range={B(def.RangeBox)}");
            d.SubType = "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}";
            var sm = (SheetMetalComponentDefinition)d.ComponentDefinition;
            try { sm.UseSheetMetalStyleThickness = false; } catch (Exception ex) { Console.WriteLine("style thickness: " + ex.Message); }
            string tx = (Math.Round(t * 100) / 10.0).ToString(System.Globalization.CultureInfo.InvariantCulture) + " mm";
            try { d.Update2(true); } catch { }
            try { var par = sm.Parameters["Thickness"]; Console.WriteLine("param Thickness = " + par.Expression); par.Expression = tx; Console.WriteLine("param set to " + tx); }
            catch (Exception ex) { Console.WriteLine("param: " + ex.Message); }
            if (Math.Abs(Convert.ToDouble(sm.Thickness.Value) * 10 - Math.Round(t * 100) / 10.0) > 0.01)
            {
                try
                {
                    SheetMetalStyle st = sm.ActiveSheetMetalStyle;
                    Console.WriteLine("style " + st.Name + " thickness " + st.Thickness);
                    SheetMetalStyle copy = (SheetMetalStyle)st.Copy("AutoDraw " + tx);
                    copy.Thickness = tx;
                    copy.Activate();
                    sm.UseSheetMetalStyleThickness = true;
                    Console.WriteLine("style copy active");
                }
                catch (Exception ex) { Console.WriteLine("style: " + ex.Message); }
            }
            try { d.Update2(true); } catch { }
            Console.WriteLine("converted; thickness now " + Convert.ToDouble(sm.Thickness.Value) * 10 + " mm");
            try { sm.Unfold(); Console.WriteLine("unfold ok; bends=" + sm.Bends.Count + " flat=" + sm.HasFlatPattern); }
            catch (Exception ex) { Console.WriteLine("Unfold() failed: " + ex.Message); }
            if (!sm.HasFlatPattern)
            {
                // largest planar face as base face
                Face best = null; double ba = 0;
                foreach (SurfaceBody b in sm.SurfaceBodies) foreach (Face f in b.Faces) if (f.SurfaceType == SurfaceTypeEnum.kPlaneSurface && f.Evaluator.Area > ba) { ba = f.Evaluator.Area; best = f; }
                try { sm.Unfold2(best); Console.WriteLine("Unfold2(largest face) ok; bends=" + sm.Bends.Count + " flat=" + sm.HasFlatPattern); }
                catch (Exception ex) { Console.WriteLine("Unfold2 failed: " + ex.Message); }
            }
            if (sm.HasFlatPattern) { var r = sm.FlatPattern.RangeBox; Console.WriteLine("flat pattern " + B(r)); try { sm.FlatPattern.ExitEdit(); } catch { } }
        }
        catch (Exception ex) { Console.WriteLine("failed: " + ex); }
        finally
        {
            try { d?.Close(true); } catch { }
            app.SilentOperation = silent;
        }
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
