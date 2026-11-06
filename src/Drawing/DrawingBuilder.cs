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

        private void AssemblySmall(Sheet sheet, SheetFrame f, AssemblyDocument asm)
        {
            Ortho o = PlaceOrtho(sheet, (Document)asm, Choose(DocKind.Assembly, (Document)asm),
                f.Part(0, 0.45, 0.42, 1), f.Part(0, 0, 0.42, 0.45), f.Part(0.42, 0.45, 0.58, 1));
            PartsList list = AssemblyNotes.AddPartsList(_app, sheet, o.Base, f);
            Region iso = f.Part(0.58, 0.55, 1, 1);
            double top = AssemblyNotes.Top(list, f) + 0.8;
            if (top > iso.Y0) iso = new Region(iso.X0, Math.Min(top, iso.Y1 - 4), iso.X1, iso.Y1);
            DrawingView isoView = Iso(sheet, (Document)asm, iso);
            AssemblyNotes.AddBalloons(_app, sheet, isoView, asm);
            var d = new Dimensioner(_app, sheet);
            d.Overall(o.Base, true, true);
            if (o.Below != null) d.Overall(o.Below, false, true);
        }

        private void AssemblyOverview(Sheet sheet, SheetFrame f, AssemblyDocument asm)
        {
            DrawingView isoView = Iso(sheet, (Document)asm, f.Part(0, 0, 0.6, 1));
            AssemblyNotes.AddPartsList(_app, sheet, isoView, f);
            AssemblyNotes.AddBalloons(_app, sheet, isoView, asm);
        }

        private void AssemblyViews(Sheet sheet, SheetFrame f, Document asm)
        {
            Ortho o = PlaceOrtho(sheet, asm, Choose(DocKind.Assembly, asm), f.Part(0, 0.45, 0.6, 1), f.Part(0, 0, 0.6, 0.45), f.Part(0.6, 0.45, 1, 1));
            var d = new Dimensioner(_app, sheet);
            d.Overall(o.Base, true, true);
            if (o.Below != null) d.Overall(o.Below, false, true);
        }

        private void AssemblyExtra(Sheet sheet, SheetFrame f, Document asm) =>
            PlaceOrtho(sheet, asm, Choose(DocKind.Assembly, asm), f.Part(0, 0, 1, 1), null, null);

        private void Folded(Sheet sheet, SheetFrame f, Document doc)
        {
            Ortho o = PlaceOrtho(sheet, doc, Choose(DocKind.SheetMetalFolded, doc),
                f.Part(0.28, 0.62, 0.62, 1), f.Part(0.28, 0.42, 0.62, 0.62), f.Part(0, 0.62, 0.28, 1));
            Iso(sheet, doc, f.Part(0.64, 0.55, 1, 1));
            (DrawingView flat, DrawingView edge) = FlatViews(sheet, doc, f.Part(0, 0, 0.46, 0.42), f.Part(0.46, 0, 0.52, 0.42));

            var d = new Dimensioner(_app, sheet);
            d.Overall(o.Base, true, true);
            if (o.Below != null) d.Overall(o.Below, false, true);
            if (flat != null)
            {
                d.Overall(flat, true, true, 1);
                d.FlatPattern(flat);
                d.HoleNotes(flat);
            }
            if (edge != null) d.Overall(edge, true, false);
        }

        private void Flat(Sheet sheet, SheetFrame f, Document doc)
        {
            (DrawingView flat, DrawingView edge) = FlatViews(sheet, doc, f.Part(0, 0.25, 0.7, 1), f.Part(0.72, 0.25, 0.86, 1));
            var d = new Dimensioner(_app, sheet);
            if (flat != null)
            {
                d.Overall(flat, true, true);
                d.HoleNotes(flat);
            }
            if (edge != null) d.Overall(edge, true, false);
        }

        private void General(Sheet sheet, SheetFrame f, Document doc)
        {
            Ortho o = PlaceOrtho(sheet, doc, Choose(DocKind.General, doc),
                f.Part(0, 0.45, 0.55, 1), f.Part(0, 0, 0.55, 0.45), f.Part(0.55, 0.45, 0.72, 1));
            Iso(sheet, doc, f.Part(0.72, 0.55, 1, 1));
            var d = new Dimensioner(_app, sheet);
            d.Overall(o.Base, true, true);
            if (o.Below != null) d.Overall(o.Below, false, true);
            d.HoleNotes(o.Base);
        }

        private void Lathe(Sheet sheet, SheetFrame f, Document doc)
        {
            Ortho o = PlaceOrtho(sheet, doc, Choose(DocKind.Lathe, doc),
                f.Part(0, 0.3, 0.62, 1), null, f.Part(0.62, 0.45, 0.82, 1));
            Iso(sheet, doc, f.Part(0.82, 0.55, 1, 1));
            var d = new Dimensioner(_app, sheet);
            d.Overall(o.Base, true, false);
            foreach (LinearGeneralDimension dia in d.Overall(o.Base, false, true)) Dimensioner.Diameter(dia);
        }

        private void Tube(Sheet sheet, SheetFrame f, Document doc)
        {
            Orient orient = Choose(DocKind.Tube, doc);
            Ortho o = PlaceOrtho(sheet, doc, orient, f.Part(0, 0.5, 0.8, 1), null, f.Part(0.8, 0.5, 1, 1));
            Iso(sheet, doc, f.Part(0, 0, 0.5, 0.45));
            var d = new Dimensioner(_app, sheet);
            d.Overall(o.Base, true, false);
            List<LinearGeneralDimension> h = d.Overall(o.Base, false, true);
            if (orient.Round) foreach (LinearGeneralDimension dia in h) Dimensioner.Diameter(dia);
            else if (o.Side != null) d.Overall(o.Side, true, true, 0, true);
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

        private sealed class Ortho
        {
            public DrawingView Base, Below, Side;
        }

        private static Box RangeOf(Document doc)
        {
            if (doc is PartDocument p) return p.ComponentDefinition.RangeBox;
            return ((AssemblyDocument)doc).ComponentDefinition.RangeBox;
        }

        /// <summary>
        /// Front view for assemblies, sheet metal and general parts. Tubes and lathe parts are looked at from the side,
        /// with their length or axis horizontal: the view direction is the shortest of the other two model axes.
        /// </summary>
        private static Orient Choose(DocKind kind, Document doc)
        {
            Box box = RangeOf(doc);
            double ex = box.MaxPoint.X - box.MinPoint.X, ey = box.MaxPoint.Y - box.MinPoint.Y, ez = box.MaxPoint.Z - box.MinPoint.Z;
            var o = new Orient { O = ViewOrientationTypeEnum.kFrontViewOrientation };
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

        /// <summary>
        /// The base view in <paramref name="baseR"/>, a projected view below it (first angle: the top view) and one to the
        /// right (the left-side view), all at one standard scale that fits their regions.
        /// </summary>
        private Ortho PlaceOrtho(Sheet sheet, Document doc, Orient o, Region baseR, Region? belowR, Region? sideR)
        {
            double fit = Scales.Fit(o.W, o.H, baseR.W, baseR.H);
            if (belowR.HasValue) fit = Math.Min(fit, Scales.Fit(o.W, o.D, baseR.W, belowR.Value.H));
            if (sideR.HasValue) fit = Math.Min(fit, Scales.Fit(o.D, o.H, sideR.Value.W, baseR.H));
            double scale = Scales.Snap(fit);

            var r = new Ortho();
            r.Base = sheet.DrawingViews.AddBaseView((_Document)doc, P(baseR.CX, baseR.CY), scale, o.O, Lines, "", Type.Missing, Type.Missing);
            Tidy(r.Base);
            if (o.Rotate) { try { r.Base.Rotation = Math.PI / 2; } catch (Exception ex) { Log.Write("Rotating the view failed: " + ex.Message); } }
            if (belowR.HasValue)
            {
                try { r.Below = sheet.DrawingViews.AddProjectedView(r.Base, P(baseR.CX, belowR.Value.CY), DrawingViewStyleEnum.kFromBaseDrawingViewStyle, Type.Missing); Tidy(r.Below); }
                catch (Exception ex) { Log.Write("Projected view below failed: " + ex.Message); }
            }
            if (sideR.HasValue)
            {
                try { r.Side = sheet.DrawingViews.AddProjectedView(r.Base, P(sideR.Value.CX, baseR.CY), DrawingViewStyleEnum.kFromBaseDrawingViewStyle, Type.Missing); Tidy(r.Side); }
                catch (Exception ex) { Log.Write("Projected side view failed: " + ex.Message); }
            }

            // the real view sizes decide: one standard scale smaller as long as something sticks out of its region
            for (int i = 0; i < 6; i++)
            {
                double over = Over(r.Base, baseR);
                if (r.Below != null) over = Math.Max(over, Over(r.Below, new Region(baseR.X0, belowR.Value.Y0, baseR.X1, belowR.Value.Y1)));
                if (r.Side != null) over = Math.Max(over, Over(r.Side, new Region(sideR.Value.X0, baseR.Y0, sideR.Value.X1, baseR.Y1)));
                if (over <= 1) break;
                double next = Math.Min(Scales.Below(r.Base.Scale), Scales.Snap(r.Base.Scale / over));
                if (next >= r.Base.Scale) break;
                r.Base.Scale = next;
            }

            try { r.Base.Center = P(baseR.CX, baseR.CY); } catch { }
            if (r.Below != null) { try { r.Below.Center = P(r.Base.Center.X, belowR.Value.CY); } catch { } }
            if (r.Side != null) { try { r.Side.Center = P(sideR.Value.CX, r.Base.Center.Y); } catch { } }
            return r;
        }

        /// <summary>How far a view is too big for its region (1 = just fits, with room for dimensions).</summary>
        private static double Over(DrawingView v, Region r, double fill = 0.85) =>
            Math.Max(v.Width / (r.W * fill), v.Height / (r.H * fill));

        private static void Tidy(DrawingView v)
        {
            try { v.ShowLabel = false; } catch { }
        }

        /// <summary>An isometric view (top right) at its own standard scale, filling <paramref name="r"/>.</summary>
        private DrawingView Iso(Sheet sheet, Document doc, Region r)
        {
            Box box = RangeOf(doc);
            double ex = box.MaxPoint.X - box.MinPoint.X, ey = box.MaxPoint.Y - box.MinPoint.Y, ez = box.MaxPoint.Z - box.MinPoint.Z;
            double w = 0.707 * (ex + ez), h = 0.816 * ey + 0.408 * (ex + ez);
            double scale = Scales.Snap(Scales.Fit(w, h, r.W, r.H, 0.9));
            DrawingView v = sheet.DrawingViews.AddBaseView((_Document)doc, P(r.CX, r.CY), scale,
                ViewOrientationTypeEnum.kIsoTopRightViewOrientation, Lines, "", Type.Missing, Type.Missing);
            Tidy(v);
            for (int i = 0; i < 6 && Over(v, r, 0.95) > 1; i++)
            {
                double next = Math.Min(Scales.Below(v.Scale), Scales.Snap(v.Scale / Over(v, r, 0.95)));
                if (next >= v.Scale) break;
                v.Scale = next;
            }
            try { v.Center = P(r.CX, r.CY); } catch { }
            return v;
        }

        /// <summary>The flat pattern view in <paramref name="flatR"/> and its edge view (showing the thickness) to the right.</summary>
        private (DrawingView, DrawingView) FlatViews(Sheet sheet, Document doc, Region flatR, Region edgeR)
        {
            if (!(doc is PartDocument p)) throw new InvalidOperationException("Not a part.");
            DrawingView flat;
            if (p.ComponentDefinition is SheetMetalComponentDefinition sm && sm.HasFlatPattern)
            {
                double len = sm.FlatPattern.Length, wid = sm.FlatPattern.Width;
                double scale = Scales.Snap(Math.Min(Scales.Fit(Math.Max(len, wid), Math.Min(len, wid), flatR.W, flatR.H),
                                                    Scales.Fit(len, wid, flatR.W, flatR.H)));
                NameValueMap options = _app.TransientObjects.CreateNameValueMap();
                options.Add("SheetMetalFoldedModel", false);
                flat = sheet.DrawingViews.AddBaseView((_Document)doc, P(flatR.CX, flatR.CY), scale,
                    ViewOrientationTypeEnum.kDefaultViewOrientation, Lines, "", Type.Missing, options);
            }
            else
            {
                // a solid plate (or sheet metal that cannot be unfolded): look straight at it along its thinnest side
                Box box = p.ComponentDefinition.RangeBox;
                ModelAxis thin = KindDetector.ShortestAxis(box, out _, out _);
                double ex = box.MaxPoint.X - box.MinPoint.X, ey = box.MaxPoint.Y - box.MinPoint.Y, ez = box.MaxPoint.Z - box.MinPoint.Z;
                ViewOrientationTypeEnum o = thin == ModelAxis.Z ? ViewOrientationTypeEnum.kFrontViewOrientation
                    : thin == ModelAxis.Y ? ViewOrientationTypeEnum.kTopViewOrientation : ViewOrientationTypeEnum.kRightViewOrientation;
                (double w, double h) = thin == ModelAxis.Z ? (ex, ey) : thin == ModelAxis.Y ? (ex, ez) : (ez, ey);
                double scale = Scales.Snap(Scales.Fit(w, h, flatR.W, flatR.H));
                flat = sheet.DrawingViews.AddBaseView((_Document)doc, P(flatR.CX, flatR.CY), scale, o, Lines, "", Type.Missing, Type.Missing);
            }
            Tidy(flat);

            DrawingView edge = null;
            try
            {
                edge = sheet.DrawingViews.AddProjectedView(flat, P(edgeR.CX, flatR.CY), DrawingViewStyleEnum.kFromBaseDrawingViewStyle, Type.Missing);
                Tidy(edge);
            }
            catch (Exception ex) { Log.Write("Edge view of the flat pattern failed: " + ex.Message); }

            for (int i = 0; i < 6 && Over(flat, flatR) > 1; i++)
            {
                double next = Math.Min(Scales.Below(flat.Scale), Scales.Snap(flat.Scale / Over(flat, flatR)));
                if (next >= flat.Scale) break;
                flat.Scale = next;
            }
            try { flat.Center = P(flatR.CX, flatR.CY); } catch { }
            if (edge != null)
            {
                double x = Math.Max(edgeR.CX, flat.Center.X + flat.Width / 2 + 2.5);
                try { edge.Center = P(x, flat.Center.Y); } catch { }
            }
            return (flat, edge);
        }
    }
}
