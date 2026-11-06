using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoDraw.Core;
using Inventor;
using Path = System.IO.Path;

namespace AutoDraw.Drawing
{
    /// <summary>
    /// Makes the production drawing of a main assembly: writes Aantal and the sheet number (bewerking, bewerkingBlad)
    /// into the documents, creates one .idw from the chosen template with a sheet per document in sheet-plan order, puts
    /// the views, parts lists, balloons and dimensions on each sheet, and saves it next to the main assembly.
    /// </summary>
    internal sealed class DrawingBuilder
    {
        private const DrawingViewStyleEnum Lines = DrawingViewStyleEnum.kHiddenLineRemovedDrawingViewStyle;

        private readonly Inventor.Application _app;
        private readonly AppSettings _settings;
        private readonly TransientGeometry _tg;

        public readonly List<string> Warnings = new List<string>();

        public DrawingBuilder(Inventor.Application app, AppSettings settings)
        {
            _app = app;
            _settings = settings;
            _tg = app.TransientGeometry;
        }

        /// <summary>Makes and saves the drawing; returns its path, or null when cancelled.</summary>
        public string Run(AssemblyDocument main, DocNode root, List<SheetJob> jobs, IEnumerable<DocNode> all, string order,
            Action<string> progress, Func<bool> cancelled)
        {
            string template = _settings.TemplatePath;
            if (string.IsNullOrWhiteSpace(template) || !System.IO.File.Exists(template))
                throw new InvalidOperationException("Choose a drawing template first.");
            if (jobs.Count == 0) throw new InvalidOperationException("Tick at least one document.");

            progress("Writing iProperties…");
            WriteProperties(main, jobs, all, order);
            if (cancelled()) return null;

            progress("Creating the drawing…");
            var drawing = (DrawingDocument)_app.Documents.Add(DocumentTypeEnum.kDrawingDocumentObject, template, false);
            bool done = false;
            try
            {
                PrepareSheets(drawing, jobs.Count);
                for (int i = 0; i < jobs.Count; i++)
                {
                    if (cancelled()) return null;
                    SheetJob job = jobs[i];
                    Sheet sheet = drawing.Sheets[i + 1];
                    progress("Sheet " + job.SheetNo + " of " + jobs.Count + ": " + job.Node.Name);
                    try { sheet.Name = job.Node.Name; } catch { }
                    try
                    {
                        Build(sheet, job);
                        if (job.Part == 0) { job.Node.Status = "Sheet " + job.SheetNo; job.Node.StatusBad = false; }
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Sheet " + job.SheetNo + " (" + job.Node.Name + ") failed: " + ex);
                        job.Node.Status = "Sheet " + job.SheetNo + ": " + ex.Message;
                        job.Node.StatusBad = true;
                        Warnings.Add(job.Node.Name + ": " + ex.Message);
                    }
                }
                try { drawing.Sheets[1].Activate(); } catch { }

                progress("Saving…");
                string folder = Path.GetDirectoryName(((Document)main).FullFileName) ?? "";
                string path = Naming.DrawingPath(folder, order, root.Name, DateTime.Now);
                bool silent = _app.SilentOperation;
                _app.SilentOperation = true;
                try { drawing.SaveAs(path, false); }
                finally { _app.SilentOperation = silent; }
                drawing.Views.Add();   // show it
                done = true;
                return path;
            }
            finally
            {
                if (!done) { try { drawing.Close(true); } catch { } }
            }
        }

        // ----- iProperties ------------------------------------------------------------------------------------------

        private void WriteProperties(AssemblyDocument main, List<SheetJob> jobs, IEnumerable<DocNode> all, string order)
        {
            var changed = new HashSet<Document>();
            void Set(Document doc, string name, string value, DocNode node)
            {
                if (doc == null || string.IsNullOrWhiteSpace(name)) return;
                try { if (Props.Set(doc, name, value)) changed.Add(doc); }
                catch (Exception ex)
                {
                    Log.Write("Setting " + name + " of " + doc.DisplayName + " failed: " + ex.Message);
                    if (node != null) { node.Status = "Cannot write " + name; node.StatusBad = true; }
                }
            }

            Set((Document)main, _settings.OrderProperty, order.Trim(), null);
            foreach (SheetJob j in jobs.Where(j => j.Part == 0))
            {
                Set(j.Node.Doc, _settings.QuantityProperty, j.Node.QuantityValue, j.Node);
                foreach (string p in _settings.SheetNumberProperties) Set(j.Node.Doc, p, Naming.SheetText(j.SheetNo), j.Node);
            }
            foreach (DocNode n in all)
                foreach (KeyValuePair<string, string> kv in n.Edited) Set(n.Doc, kv.Key, kv.Value, n);

            // sheet metal parts without a flat pattern get one, so the flat pattern view can be placed
            foreach (SheetJob j in jobs.Where(j => j.Node.Kind == DocKind.SheetMetalFolded || j.Node.Kind == DocKind.SheetMetalFlat))
            {
                try
                {
                    if (j.Node.Doc is PartDocument p && p.ComponentDefinition is SheetMetalComponentDefinition sm && !sm.HasFlatPattern)
                    {
                        sm.Unfold();
                        try { sm.FlatPattern.ExitEdit(); } catch { }
                        changed.Add(j.Node.Doc);
                    }
                }
                catch (Exception ex) { Warnings.Add(j.Node.Name + ": no flat pattern (" + ex.Message + ")"); }
            }

            // save parts before the assemblies that use them
            bool silent = _app.SilentOperation;
            _app.SilentOperation = true;
            try
            {
                foreach (Document doc in changed.OrderBy(d => d is AssemblyDocument ? 1 : 0))
                {
                    try { doc.Save(); }
                    catch (Exception ex)
                    {
                        Log.Write("Saving " + doc.FullFileName + " failed: " + ex.Message);
                        Warnings.Add("Could not save " + Path.GetFileName(doc.FullFileName) + " (checked in or read-only?)");
                    }
                }
            }
            finally { _app.SilentOperation = silent; }
        }

        // ----- sheets -----------------------------------------------------------------------------------------------

        /// <summary>Keeps the template's first sheet (border, title block) and copies it until there are <paramref name="count"/>.</summary>
        private static void PrepareSheets(DrawingDocument drawing, int count)
        {
            for (int i = drawing.Sheets.Count; i > 1; i--) drawing.Sheets[i].Delete(false);
            Sheet first = drawing.Sheets[1];
            for (int i = first.DrawingViews.Count; i >= 1; i--) first.DrawingViews[i].Delete();
            TitleBlockDefinition tbDef = null;
            BorderDefinition borderDef = null;
            try { tbDef = first.TitleBlock?.Definition; } catch { }
            try { borderDef = first.Border?.Definition; } catch { }
            for (int i = 1; i < count; i++)
            {
                Sheet copy = first.CopyTo((_DrawingDocument)drawing);
                // a copy should keep border and title block; add them when it did not
                try { if (borderDef != null && copy.Border == null) copy.AddBorder(borderDef, Type.Missing); }
                catch (Exception ex) { Log.Write("Adding the border failed: " + ex.Message); }
                try { if (tbDef != null && copy.TitleBlock == null) copy.AddTitleBlock(tbDef, Type.Missing, Type.Missing); }
                catch (Exception ex) { Log.Write("Adding the title block failed: " + ex.Message); }
            }
        }

        private void Build(Sheet sheet, SheetJob job)
        {
            var frame = new SheetFrame(sheet);
            Document doc = job.Node.Doc;
            switch (job.Node.Kind)
            {
                case DocKind.Assembly:
                    if (job.Parts == 1) AssemblySmall(sheet, frame, (AssemblyDocument)doc);
                    else if (job.Part == 0) AssemblyOverview(sheet, frame, (AssemblyDocument)doc);
                    else if (job.Part == 1) AssemblyViews(sheet, frame, doc);
                    else AssemblyExtra(sheet, frame, doc);
                    break;
                case DocKind.SheetMetalFolded: Folded(sheet, frame, doc); break;
                case DocKind.SheetMetalFlat: Flat(sheet, frame, doc); break;
                case DocKind.Lathe: Lathe(sheet, frame, doc); break;
                case DocKind.Tube: Tube(sheet, frame, doc); break;
                default: General(sheet, frame, doc); break;
            }
        }


        // ----- layouts per kind ---------------------------------------------------------------------------------------
        //
        // Every layout first creates its views, then measures them (the real view boxes, not the model range box) and
        // lets Layout.Pack find the largest standard scale at which all of them fit around the title block and parts
        // list. Dimensions and balloons are added once the views are in their final place.

        private void AssemblySmall(Sheet sheet, SheetFrame f, AssemblyDocument asm)
        {
            var doc = (Document)asm;
            Views v = CreateOrtho(sheet, f, doc, Choose(DocKind.Assembly, doc), true, true);
            DrawingView iso = CreateIso(sheet, f, doc);
            PartsList list = AssemblyNotes.AddPartsList(_app, sheet, v.Base, f);
            ViewGroup og = Measure(v, Corner.TopLeft), ig = MeasureSingle(iso, Corner.TopRight, 3.0, true);
            Arrange(f, WithPartsList(f, list), (og, v), (ig, Views.Of(iso)));
            AssemblyNotes.AddBalloons(_app, sheet, iso, asm);
            var d = new Dimensioner(_app, sheet);
            d.Overall(v.Base, true, true);
            if (v.Below != null) d.Overall(v.Below, false, true);
        }

        private void AssemblyOverview(Sheet sheet, SheetFrame f, AssemblyDocument asm)
        {
            DrawingView iso = CreateIso(sheet, f, (Document)asm);
            PartsList list = AssemblyNotes.AddPartsList(_app, sheet, iso, f);
            Arrange(f, WithPartsList(f, list), (MeasureSingle(iso, Corner.TopLeft, 3.0, false), Views.Of(iso)));
            AssemblyNotes.AddBalloons(_app, sheet, iso, asm);
        }

        private void AssemblyViews(Sheet sheet, SheetFrame f, Document asm)
        {
            Views v = CreateOrtho(sheet, f, asm, Choose(DocKind.Assembly, asm), true, true);
            Arrange(f, f.Obstacles, (Measure(v, Corner.TopLeft), v));
            var d = new Dimensioner(_app, sheet);
            d.Overall(v.Base, true, true);
            if (v.Below != null) d.Overall(v.Below, false, true);
        }

        private void AssemblyExtra(Sheet sheet, SheetFrame f, Document asm)
        {
            Views v = CreateOrtho(sheet, f, asm, Choose(DocKind.Assembly, asm), false, false);
            Arrange(f, f.Obstacles, (Measure(v, Corner.TopLeft), v));
        }

        private void Folded(Sheet sheet, SheetFrame f, Document doc)
        {
            Views v = CreateOrtho(sheet, f, doc, Choose(DocKind.SheetMetalFolded, doc), true, true);
            DrawingView iso = CreateIso(sheet, f, doc);
            Views flat = CreateFlat(sheet, f, doc);
            var groups = new List<(ViewGroup, Views)> { (Measure(v, Corner.TopLeft), v) };
            if (flat != null) groups.Add((MeasureFlat(flat), flat));
            groups.Add((MeasureSingle(iso, Corner.TopRight, 0.5, true), Views.Of(iso)));
            Arrange(f, f.Obstacles, groups.ToArray());

            var d = new Dimensioner(_app, sheet);
            d.Overall(v.Base, true, true);
            if (v.Below != null) d.Overall(v.Below, false, true);
            if (flat != null) FlatDimensions(d, flat);
        }

        private void Flat(Sheet sheet, SheetFrame f, Document doc)
        {
            Views flat = CreateFlat(sheet, f, doc) ?? throw new InvalidOperationException("No flat view.");
            Arrange(f, f.Obstacles, (MeasureFlat(flat, Corner.TopLeft), flat));
            FlatDimensions(new Dimensioner(_app, sheet), flat);
        }

        private static void FlatDimensions(Dimensioner d, Views flat)
        {
            d.Overall(flat.Base, true, true, 1);
            d.FlatPattern(flat.Base);
            d.HoleNotes(flat.Base);
            if (flat.Side != null) d.Overall(flat.Side, true, false);
        }

        private void General(Sheet sheet, SheetFrame f, Document doc)
        {
            Views v = CreateOrtho(sheet, f, doc, Choose(DocKind.General, doc), true, true);
            DrawingView iso = CreateIso(sheet, f, doc);
            Arrange(f, f.Obstacles, (Measure(v, Corner.TopLeft), v), (MeasureSingle(iso, Corner.TopRight, 0.5, true), Views.Of(iso)));
            var d = new Dimensioner(_app, sheet);
            d.Overall(v.Base, true, true);
            if (v.Below != null) d.Overall(v.Below, false, true);
            d.HoleNotes(v.Base);
        }

        private void Lathe(Sheet sheet, SheetFrame f, Document doc)
        {
            Views v = CreateOrtho(sheet, f, doc, Choose(DocKind.Lathe, doc), false, true);
            DrawingView iso = CreateIso(sheet, f, doc);
            Arrange(f, f.Obstacles, (Measure(v, Corner.TopLeft), v), (MeasureSingle(iso, Corner.TopRight, 0.5, true), Views.Of(iso)));
            var d = new Dimensioner(_app, sheet);
            d.Overall(v.Base, true, false);
            foreach (LinearGeneralDimension dia in d.Overall(v.Base, false, true)) Dimensioner.Diameter(dia);
        }

        private void Tube(Sheet sheet, SheetFrame f, Document doc)
        {
            Orient orient = Choose(DocKind.Tube, doc);
            Views v = CreateOrtho(sheet, f, doc, orient, false, true);
            DrawingView iso = CreateIso(sheet, f, doc);
            Arrange(f, f.Obstacles, (Measure(v, Corner.TopLeft), v), (MeasureSingle(iso, Corner.BottomLeft, 0.5, true), Views.Of(iso)));
            var d = new Dimensioner(_app, sheet);
            d.Overall(v.Base, true, false);
            List<LinearGeneralDimension> h = d.Overall(v.Base, false, true);
            if (orient.Round) foreach (LinearGeneralDimension dia in h) Dimensioner.Diameter(dia);
            else if (v.Side != null) d.Overall(v.Side, true, true, 0, true);
        }

        // ----- box layout -------------------------------------------------------------------------------------------------

        /// <summary>A base view with the projected view below it and the one to its right (either may be missing).</summary>
        private sealed class Views
        {
            public DrawingView Base, Below, Side;
            public static Views Of(DrawingView single) => new Views { Base = single };
        }

        /// <summary>The model size of the views (view size divided by scale), as one group for the layout.</summary>
        private static ViewGroup Measure(Views v, Corner prefer)
        {
            double s = v.Base.Scale;
            var g = new ViewGroup { BW = v.Base.Width / s, BH = v.Base.Height / s, Prefer = prefer };
            if (v.Below != null) g.BelowH = v.Below.Height / s;
            if (v.Side != null) g.SideW = v.Side.Width / s;
            return g;
        }

        private static ViewGroup MeasureSingle(DrawingView view, Corner prefer, double margin, bool flexible) => new ViewGroup
        {
            BW = view.Width / view.Scale, BH = view.Height / view.Scale, Prefer = prefer, Flexible = flexible,
            MarginL = margin, MarginR = margin, MarginT = margin, MarginB = margin
        };

        /// <summary>The flat pattern with its edge view: room around it for the overall and bend chain dimensions.</summary>
        private static ViewGroup MeasureFlat(Views flat, Corner prefer = Corner.BottomLeft)
        {
            ViewGroup g = Measure(flat, prefer);
            g.MarginT = 3.2; g.MarginL = 3.2; g.MarginB = 2.5; g.MarginR = 0.5; g.Gap = 4.0;
            return g;
        }

        /// <summary>The title block, plus the parts list (from its left edge to the right side of the sheet).</summary>
        private static List<Region> WithPartsList(SheetFrame f, PartsList list)
        {
            var obstacles = new List<Region>(f.Obstacles);
            try
            {
                Box2d rb = list.RangeBox;
                obstacles.Add(new Region(rb.MinPoint.X - 0.5, f.TitleBlock.Y0, f.W, rb.MaxPoint.Y + 0.5));
            }
            catch { }
            return obstacles;
        }

        /// <summary>Packs the groups and moves and scales their views to the places found.</summary>
        private void Arrange(SheetFrame f, IList<Region> obstacles, params (ViewGroup G, Views V)[] items)
        {
            if (!Layout.Pack(items.Select(i => i.G).ToList(), f.Usable, obstacles))
                Log.Write("Views do not fit at the smallest scale.");
            foreach ((ViewGroup g, Views v) in items)
            {
                try { if (Math.Abs(v.Base.Scale - g.Scale) > 1e-9) v.Base.Scale = g.Scale; }
                catch (Exception ex) { Log.Write("Setting the scale failed: " + ex.Message); }
                try { v.Base.Center = P(g.BaseCX, g.BaseCY); } catch (Exception ex) { Log.Write("Moving a view failed: " + ex.Message); }
                if (v.Below != null) { try { v.Below.Center = P(g.BaseCX, g.BelowCY); } catch { } }
                if (v.Side != null) { try { v.Side.Center = P(g.SideCX, g.BaseCY); } catch { } }
            }
        }

        // ----- views ----------------------------------------------------------------------------------------------------

        /// <summary>How the base view looks at the model: orientation, a 90° turn, and the model size across, up and into the view.</summary>
        private struct Orient
        {
            public ViewOrientationTypeEnum O;
            public bool Rotate;
            public double W, H, D;
            public bool Round;
        }

        private static Box RangeOf(Document doc)
        {
            if (doc is PartDocument p) return p.ComponentDefinition.RangeBox;
            return ((AssemblyDocument)doc).ComponentDefinition.RangeBox;
        }

        /// <summary>
        /// Assemblies, sheet metal and general parts: the front view, or the right view when the model is deeper than
        /// wide (so the base view shows the long side; up stays up). Tubes and lathe parts are looked at from the side
        /// with their length or axis horizontal: the view direction is the shortest of the other two model axes.
        /// </summary>
        private static Orient Choose(DocKind kind, Document doc)
        {
            Box box = RangeOf(doc);
            double ex = box.MaxPoint.X - box.MinPoint.X, ey = box.MaxPoint.Y - box.MinPoint.Y, ez = box.MaxPoint.Z - box.MinPoint.Z;
            var o = new Orient { O = ez > ex * 1.05 ? ViewOrientationTypeEnum.kRightViewOrientation : ViewOrientationTypeEnum.kFrontViewOrientation };
            ModelAxis axis = ModelAxis.None;
            if ((kind == DocKind.Lathe || kind == DocKind.Tube) && doc is PartDocument part)
            {
                KindDetector.Shape s = KindDetector.Analyse(part.ComponentDefinition);
                o.Round = s.Rotational;
                axis = kind == DocKind.Lathe && s.RotationAxis != ModelAxis.None ? s.RotationAxis : s.LongAxis;
            }
            switch (axis)
            {
                case ModelAxis.X: o.O = ez <= ey ? ViewOrientationTypeEnum.kFrontViewOrientation : ViewOrientationTypeEnum.kTopViewOrientation; break;
                case ModelAxis.Y: o.O = ez <= ex ? ViewOrientationTypeEnum.kFrontViewOrientation : ViewOrientationTypeEnum.kRightViewOrientation; o.Rotate = true; break;
                case ModelAxis.Z:
                    if (ex <= ey) o.O = ViewOrientationTypeEnum.kRightViewOrientation;
                    else { o.O = ViewOrientationTypeEnum.kTopViewOrientation; o.Rotate = true; }
                    break;
            }
            switch (o.O)
            {
                case ViewOrientationTypeEnum.kTopViewOrientation: o.W = ex; o.H = ez; o.D = ey; break;
                case ViewOrientationTypeEnum.kRightViewOrientation: o.W = ez; o.H = ey; o.D = ex; break;
                default: o.W = ex; o.H = ey; o.D = ez; break;
            }
            if (o.Rotate) (o.W, o.H) = (o.H, o.W);
            return o;
        }

        private Point2d P(double x, double y) => _tg.CreatePoint2d(x, y);

        private static void Tidy(DrawingView v)
        {
            try { v.ShowLabel = false; } catch { }
        }

        /// <summary>A first guess of the scale from the model size, so the views are created at about the right size.</summary>
        private static double Guess(SheetFrame f, double w, double h) => Scales.Snap(Scales.Fit(w, h, f.Usable.W * 0.6, f.Usable.H * 0.6));

        /// <summary>The base view with the projected view below it (first angle: the top view) and/or to its right (the left-side view).</summary>
        private Views CreateOrtho(Sheet sheet, SheetFrame f, Document doc, Orient o, bool below, bool side)
        {
            double s = Guess(f, o.W + (side ? o.D : 0), o.H + (below ? o.D : 0));
            double cx = f.Usable.X0 + f.Usable.W * 0.3, cy = f.Usable.Y0 + f.Usable.H * 0.6;
            var v = new Views();
            v.Base = sheet.DrawingViews.AddBaseView((_Document)doc, P(cx, cy), s, o.O, Lines, "", Type.Missing, Type.Missing);
            Tidy(v.Base);
            if (o.Rotate) { try { v.Base.Rotation = Math.PI / 2; } catch (Exception ex) { Log.Write("Rotating the view failed: " + ex.Message); } }
            double off = (Math.Max(o.W, o.H) + o.D) * s;
            if (below)
            {
                try { v.Below = sheet.DrawingViews.AddProjectedView(v.Base, P(cx, cy - off), DrawingViewStyleEnum.kFromBaseDrawingViewStyle, Type.Missing); Tidy(v.Below); }
                catch (Exception ex) { Log.Write("Projected view below failed: " + ex.Message); }
            }
            if (side)
            {
                try { v.Side = sheet.DrawingViews.AddProjectedView(v.Base, P(cx + off, cy), DrawingViewStyleEnum.kFromBaseDrawingViewStyle, Type.Missing); Tidy(v.Side); }
                catch (Exception ex) { Log.Write("Projected side view failed: " + ex.Message); }
            }
            return v;
        }

        /// <summary>An isometric view (top right), hidden lines removed.</summary>
        private DrawingView CreateIso(Sheet sheet, SheetFrame f, Document doc)
        {
            Box box = RangeOf(doc);
            double ex = box.MaxPoint.X - box.MinPoint.X, ey = box.MaxPoint.Y - box.MinPoint.Y, ez = box.MaxPoint.Z - box.MinPoint.Z;
            double s = Guess(f, 0.707 * (ex + ez), 0.816 * ey + 0.408 * (ex + ez));
            DrawingView v = sheet.DrawingViews.AddBaseView((_Document)doc, P(f.Usable.X1 - f.Usable.W * 0.2, f.Usable.Y1 - f.Usable.H * 0.2), s,
                ViewOrientationTypeEnum.kIsoTopRightViewOrientation, Lines, "", Type.Missing, Type.Missing);
            Tidy(v);
            return v;
        }

        /// <summary>
        /// The flat pattern (or, for a solid plate or sheet metal that cannot be unfolded, a view along its thinnest
        /// side) with the edge view to its right showing the thickness.
        /// </summary>
        private Views CreateFlat(Sheet sheet, SheetFrame f, Document doc)
        {
            if (!(doc is PartDocument p)) return null;
            Point2d at = P(f.Usable.X0 + f.Usable.W * 0.3, f.Usable.Y0 + f.Usable.H * 0.3);
            var v = new Views();
            double w, h;
            if (p.ComponentDefinition is SheetMetalComponentDefinition sm && sm.HasFlatPattern)
            {
                w = Math.Max(sm.FlatPattern.Length, sm.FlatPattern.Width); h = Math.Min(sm.FlatPattern.Length, sm.FlatPattern.Width);
                NameValueMap options = _app.TransientObjects.CreateNameValueMap();
                options.Add("SheetMetalFoldedModel", false);
                v.Base = sheet.DrawingViews.AddBaseView((_Document)doc, at, Guess(f, w, w), ViewOrientationTypeEnum.kDefaultViewOrientation,
                    Lines, "", Type.Missing, options);
            }
            else
            {
                Box box = p.ComponentDefinition.RangeBox;
                ModelAxis thin = KindDetector.ShortestAxis(box, out _, out _);
                double ex = box.MaxPoint.X - box.MinPoint.X, ey = box.MaxPoint.Y - box.MinPoint.Y, ez = box.MaxPoint.Z - box.MinPoint.Z;
                ViewOrientationTypeEnum o = thin == ModelAxis.Z ? ViewOrientationTypeEnum.kFrontViewOrientation
                    : thin == ModelAxis.Y ? ViewOrientationTypeEnum.kTopViewOrientation : ViewOrientationTypeEnum.kRightViewOrientation;
                (w, h) = thin == ModelAxis.Z ? (ex, ey) : thin == ModelAxis.Y ? (ex, ez) : (ez, ey);
                v.Base = sheet.DrawingViews.AddBaseView((_Document)doc, at, Guess(f, w, h), o, Lines, "", Type.Missing, Type.Missing);
            }
            Tidy(v.Base);
            try
            {
                v.Side = sheet.DrawingViews.AddProjectedView(v.Base, P(at.X + w * v.Base.Scale + 5, at.Y), DrawingViewStyleEnum.kFromBaseDrawingViewStyle, Type.Missing);
                Tidy(v.Side);
            }
            catch (Exception ex) { Log.Write("Edge view failed: " + ex.Message); }
            return v;
        }
    }
}
