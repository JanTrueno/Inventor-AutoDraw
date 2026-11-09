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

        /// <summary>Called before each sheet: sheets done, total, and the sheet's document and kind.</summary>
        public Action<int, int, string> OnSheet;
        /// <summary>Called for the steps within a sheet (views, parts list, dimensions).</summary>
        public Action<string> OnDetail;

        private void Detail(string text) { try { OnDetail?.Invoke(text); } catch { } }

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
            // Background updates leave new views as raster previews for a while, and Inventor refuses dimensions on a
            // raster view: switch them off while the drawing is made.
            bool? background = null;
            try { background = _app.DrawingOptions.EnableBackgroundUpdates; _app.DrawingOptions.EnableBackgroundUpdates = false; }
            catch (Exception ex) { Log.Write("Switching off background updates failed: " + ex.Message); }
            var drawing = (DrawingDocument)_app.Documents.Add(DocumentTypeEnum.kDrawingDocumentObject, template, false);
            bool done = false;
            try
            {
                // the title block shows the drawing's own order number
                if (!string.IsNullOrWhiteSpace(_settings.DrawingOrderProperty))
                {
                    try { Props.Set((Document)drawing, _settings.DrawingOrderProperty.Trim(), order.Trim()); }
                    catch (Exception ex) { Warnings.Add("Drawing iProperty " + _settings.DrawingOrderProperty + ": " + ex.Message); }
                }
                PrepareSheets(drawing, jobs.Count);
                for (int i = 0; i < jobs.Count; i++)
                {
                    if (cancelled()) return null;
                    SheetJob job = jobs[i];
                    Sheet sheet = drawing.Sheets[i + 1];
                    progress("Sheet " + job.SheetNo + " of " + jobs.Count + ": " + job.Node.Name);
                    try { OnSheet?.Invoke(i, jobs.Count, job.Node.Name + "  (" + DocKinds.Label(job.Node.Kind) + (job.Parts > 1 ? ", sheet " + (job.Part + 1) + " of " + job.Parts : "") + ")"); } catch { }
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

                try { OnSheet?.Invoke(jobs.Count, jobs.Count, ""); } catch { }
                progress("Saving…");
                string folder = Path.GetDirectoryName(((Document)main).FullFileName) ?? "";
                string path = Naming.DrawingPath(folder, order, root.Name, DateTime.Now);
                bool silent = _app.SilentOperation;
                _app.SilentOperation = true;
                try { drawing.SaveAs(path, false); }
                finally { _app.SilentOperation = silent; }
                drawing.Views.Add();   // show it
                done = true;
                RunRules((Document)drawing, progress);
                return path;
            }
            finally
            {
                if (background != null) { try { _app.DrawingOptions.EnableBackgroundUpdates = background.Value; } catch { } }
                if (!done) { try { drawing.Close(true); } catch { } }
            }
        }

        // ----- iLogic -----------------------------------------------------------------------------------------------

        private const string ILogicId = "{3BDD8D79-2179-4B11-8A5A-257B1C0263AC}";

        /// <summary>Runs the configured external iLogic rules on the new (saved, open) drawing, then saves it again.</summary>
        private void RunRules(Document drawing, Action<string> progress)
        {
            List<string> rules = _settings.DrawingRules.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList();
            if (rules.Count == 0) return;
            object auto;
            try { auto = _app.ApplicationAddIns.ItemById[ILogicId].Automation; }
            catch (Exception ex) { Warnings.Add("iLogic is not available: " + ex.Message); return; }
            foreach (string rule in rules)
            {
                progress("iLogic: " + rule + "…");
                try { auto.GetType().InvokeMember("RunExternalRule", System.Reflection.BindingFlags.InvokeMethod, null, auto, new object[] { drawing, rule }); }
                catch (Exception ex)
                {
                    Exception inner = ex.InnerException ?? ex;
                    Log.Write("iLogic rule " + rule + " failed: " + inner);
                    Warnings.Add("iLogic rule " + rule + ": " + inner.Message);
                }
            }
            bool silent = _app.SilentOperation;
            _app.SilentOperation = true;
            try { drawing.Save(); }
            catch (Exception ex) { Warnings.Add("Saving after the iLogic rules failed: " + ex.Message); }
            finally { _app.SilentOperation = silent; }
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

            // sheet metal parts get a flat pattern when they have none, seen from the side where most bends go up
            foreach (SheetJob j in jobs.Where(j => j.Part == 0 && (j.Node.Kind == DocKind.SheetMetalFolded || j.Node.Kind == DocKind.SheetMetalFlat)))
            {
                try
                {
                    if (j.Node.Doc is PartDocument p && p.ComponentDefinition is SheetMetalComponentDefinition sm && OrientFlatPattern(p, sm))
                        changed.Add(j.Node.Doc);
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

        /// <summary>
        /// As the company's Create_Flatpattern_andflip macro, for every sheet metal part: a missing flat pattern is made
        /// (unfold) and flipped; a flat pattern whose bends mostly go down is flipped (FlipBaseFace), so the bends read
        /// UP. Unfolding or flipping a part that is not open in a window can fail; it is then opened in a window, which is
        /// closed again afterwards. Returns true when the part changed.
        /// </summary>
        private bool OrientFlatPattern(PartDocument part, SheetMetalComponentDefinition sm)
        {
            View opened = null;
            void InWindow(Action a)
            {
                try { a(); }
                catch
                {
                    if (opened == null)
                    {
                        _app.Documents.Open(((Document)part).FullFileName, true);
                        try { opened = _app.ActiveView; } catch { }
                    }
                    a();
                }
            }
            try
            {
                bool created = false, flipped = false;
                if (!sm.HasFlatPattern)
                {
                    InWindow(() => sm.Unfold());
                    created = true;
                }
                (int up, int down) = BendDirections(sm.FlatPattern);
                // a new flat pattern is flipped as the macro does; an existing one when most of its bends go down
                bool flip = up + down == 0 ? created : down > up;
                if (flip)
                {
                    InWindow(() =>
                    {
                        if (!created) { try { sm.FlatPattern.Edit(); } catch { } }
                        sm.FlatPattern.FlipBaseFace();
                    });
                    flipped = true;
                    (up, down) = BendDirections(sm.FlatPattern);
                    if (down > up && created)   // the flip made it worse: back again
                    {
                        sm.FlatPattern.FlipBaseFace();
                    }
                }
                if (created || flipped) { try { sm.FlatPattern.ExitEdit(); } catch { } }
                return created || flipped;
            }
            finally
            {
                if (opened != null) { try { opened.Close(); } catch { } }
            }
        }

        private static (int Up, int Down) BendDirections(FlatPattern fp)
        {
            int up = 0, down = 0;
            try { foreach (FlatBendResult r in fp.FlatBendResults) { if (r.IsDirectionUp) up++; else down++; } }
            catch (Exception ex) { Log.Write("Reading the bend directions failed: " + ex.Message); }
            return (up, down);
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
            ViewGroup ig = MeasureSingle(iso, Corner.BottomRight, 3.0, true);
            ig.Area = AbovePartsList(f, list);
            ViewGroup og = Measure(v, Corner.TopLeft);
            og.Area = new Region(f.Usable.X0, f.Usable.Y0, f.TitleBlock.X0 - 0.5, f.Usable.Y1);   // left of the title block column
            og.FillY = true;   // front and side view low, the top view high (beside the header)
            Arrange(f, WithPartsList(f, list), (ig, Views.Of(iso)), (og, v));
            AssemblyNotes.AddBalloons(_app, sheet, iso, asm);
            Detail("Dimensioning…");
            var d = new Dimensioner(_app, sheet);
            d.Overall(v.Base, true, true);
            if (v.Below != null) d.Overall(v.Below, false, true);
        }

        private void AssemblyOverview(Sheet sheet, SheetFrame f, AssemblyDocument asm)
        {
            DrawingView iso = CreateIso(sheet, f, (Document)asm);
            PartsList list = AssemblyNotes.AddPartsList(_app, sheet, iso, f);
            ViewGroup ig = MeasureSingle(iso, Corner.BottomRight, 3.0, false);
            ig.Area = AbovePartsList(f, list);
            Arrange(f, WithPartsList(f, list), (ig, Views.Of(iso)));
            AssemblyNotes.AddBalloons(_app, sheet, iso, asm);
        }

        private void AssemblyViews(Sheet sheet, SheetFrame f, Document asm)
        {
            Views v = CreateOrtho(sheet, f, asm, Choose(DocKind.Assembly, asm), true, true);
            ViewGroup og = Measure(v, Corner.TopLeft);
            og.FillY = true;
            Arrange(f, f.Obstacles, (og, v));
            Detail("Dimensioning…");
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
            TangentEdges(v.Base, v.Below, v.Side, iso);
            var groups = new List<(ViewGroup, Views)>();
            // the same on every sheet metal sheet: the flat pattern bottom left, the iso view bottom right (above the
            // title block), the folded views top right - top view at the top, front view down to the iso view, side
            // view left of the front view
            ViewGroup fg = flat != null ? MeasureFlat(flat) : null;
            ViewGroup og = Measure(v, Corner.TopRight);
            og.FillY = true; og.AlignRight = true; og.MaxExtraX = 3;
            ViewGroup ig = MeasureSingle(iso, Corner.BottomRight, 0.5, true);
            if (fg != null) groups.Add((fg, flat));
            groups.Add((og, v));
            groups.Add((ig, Views.Of(iso)));
            SheetMetalZones(f, fg, og, ig);
            // tall parts: when the folded views get a larger scale beside the flat pattern than above it, they go there
            Layout.LargerOrthoArea(groups.Select(x => x.Item1).ToList(), f.Usable, f.Obstacles, og);
            Arrange(f, f.Obstacles, groups.ToArray());

            // the views that show the bends as arcs are profiles: leg lengths and all angles there (90° included);
            // outside sizes on every folded view
            Detail("Dimensioning…");
            var d = new Dimensioner(_app, sheet);
            DrawingView[] folded = new[] { v.Base, v.Below, v.Side }.Where(x => x != null).ToArray();
            var arcs = folded.ToDictionary(x => x, Dimensioner.ArcCount);
            var profiles = new HashSet<DrawingView>(folded.Where(x => x != v.Base && arcs[x] > 0));
            if (profiles.Count == 0) profiles.Add(folded.OrderByDescending(x => arcs[x]).First());
            d.Overall(v.Base, true, true, profiles.Contains(v.Base) ? 1 : 0);
            if (v.Below != null) d.Overall(v.Below, false, true, profiles.Contains(v.Below) ? 1 : 0);
            if (v.Side != null) d.Overall(v.Side, true, false, profiles.Contains(v.Side) ? 1 : 0);
            foreach (DrawingView view in profiles) d.Legs(view);
            foreach (DrawingView view in folded) d.Angles(view, profiles.Contains(view));
            if (flat != null) FlatDimensions(d, flat);
        }

        private void Flat(Sheet sheet, SheetFrame f, Document doc)
        {
            Views flat = CreateFlat(sheet, f, doc) ?? throw new InvalidOperationException("No flat view.");
            TangentEdges(flat.Base, flat.Side);
            Arrange(f, f.Obstacles, (MeasureFlat(flat), flat));   // bottom left, as on the folded sheets
            Detail("Dimensioning…");
            FlatDimensions(new Dimensioner(_app, sheet), flat);
        }

        private void FlatDimensions(Dimensioner d, Views flat)
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
            (ViewGroup og, ViewGroup ig) = PartZones(f, v, iso);
            Arrange(f, f.Obstacles, (og, v), (ig, Views.Of(iso)));
            Detail("Dimensioning…");
            var d = new Dimensioner(_app, sheet);
            d.Overall(v.Base, true, true);
            if (v.Below != null) d.Overall(v.Below, false, true);
            d.HoleNotes(v.Base);
        }

        private void Lathe(Sheet sheet, SheetFrame f, Document doc)
        {
            Views v = CreateOrtho(sheet, f, doc, Choose(DocKind.Lathe, doc), false, true);
            DrawingView iso = CreateIso(sheet, f, doc);
            (ViewGroup og, ViewGroup ig) = PartZones(f, v, iso);
            Arrange(f, f.Obstacles, (og, v), (ig, Views.Of(iso)));
            Detail("Dimensioning…");
            var d = new Dimensioner(_app, sheet);
            d.Overall(v.Base, true, false);
            foreach (LinearGeneralDimension dia in d.Overall(v.Base, false, true)) Dimensioner.Diameter(dia);
        }

        private void Tube(Sheet sheet, SheetFrame f, Document doc)
        {
            Orient orient = Choose(DocKind.Tube, doc);
            Views v = CreateOrtho(sheet, f, doc, orient, false, true);
            DrawingView iso = CreateIso(sheet, f, doc);
            (ViewGroup og, ViewGroup ig) = PartZones(f, v, iso);
            Arrange(f, f.Obstacles, (og, v), (ig, Views.Of(iso)));
            Detail("Dimensioning…");
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
            public bool TopAbove = true;   // the top view (Below) sits above the base view
            public static Views Of(DrawingView single) => new Views { Base = single };
        }

        /// <summary>The model size of the views (view size divided by scale), as one group for the layout.</summary>
        private static ViewGroup Measure(Views v, Corner prefer)
        {
            double s = v.Base.Scale;
            var g = new ViewGroup { BW = v.Base.Width / s, BH = v.Base.Height / s, Prefer = prefer, TopAbove = v.TopAbove };
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
            // the edge view goes right of the flat pattern (not left, like the side view of the folded views)
            double s = flat.Base.Scale;
            var g = new ViewGroup { BW = flat.Base.Width / s, BH = flat.Base.Height / s, Prefer = prefer, Fixed = prefer == Corner.BottomLeft, SideLeft = false };
            g.EdgeW = flat.Side != null ? flat.Side.Width / s : 0;
            g.MarginT = 3.2; g.MarginL = 3.2; g.MarginB = 2.5; g.MarginR = 0.5; g.Gap = 4.0;
            return g;
        }

        // ----- zones: the same arrangement on every sheet of a kind -------------------------------------------------

        /// <summary>General, lathe and tube parts: the views in the left zone, the iso view right, above the title block.</summary>
        private static (ViewGroup, ViewGroup) PartZones(SheetFrame f, Views v, DrawingView iso)
        {
            ViewGroup og = Measure(v, Corner.TopLeft), ig = MeasureSingle(iso, Corner.TopRight, 0.5, true);
            Layout.PartZones(f.Usable, f.TitleBlock.X0, null, og, ig);
            return (og, ig);
        }

        private static void SheetMetalZones(SheetFrame f, ViewGroup fg, ViewGroup og, ViewGroup ig) =>
            Layout.PartZones(f.Usable, f.TitleBlock.X0, fg, og, ig);

        /// <summary>Above the parts list, from its left edge to the right of the usable area.</summary>
        private static Region? AbovePartsList(SheetFrame f, PartsList list)
        {
            try
            {
                Box2d rb = list.RangeBox;
                var r = new Region(Math.Max(f.Usable.X0, Math.Min(f.TitleBlock.X0, rb.MinPoint.X - 1.0)), rb.MaxPoint.Y + 0.5, f.Usable.X1, f.Usable.Y1);
                return r.H > 5 && r.W > 5 ? r : (Region?)null;
            }
            catch { return null; }
        }

        /// <summary>Shows tangent edges (bend edges of sheet metal), as the company drawings do.</summary>
        private static void TangentEdges(params DrawingView[] views)
        {
            foreach (DrawingView v in views)
            {
                if (v == null) continue;
                try { v.DisplayTangentEdges = true; } catch { }
            }
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
            Detail("Fitting the views on the sheet…");
            if (!Layout.Pack(items.Select(i => i.G).ToList(), f.Usable, obstacles))
                Log.Write("Views do not fit at the smallest scale.");
            foreach ((ViewGroup g, Views v) in items)
            {
                foreach (DrawingView view in new[] { v.Base, v.Below, v.Side })
                {
                    if (view == null) continue;
                    try { if (Math.Abs(view.Scale - g.Scale) > 1e-9 && !view.ScaleFromBase) view.Scale = g.Scale; }
                    catch (Exception ex) { Log.Write("Setting the scale failed: " + ex.Message); }
                }
                try { v.Base.Center = P(g.BaseCX, g.BaseCY); } catch (Exception ex) { Log.Write("Moving a view failed: " + ex.Message); }
                if (v.Below != null) { try { v.Below.Center = P(g.BaseCX, g.BelowCY); } catch { } }
                if (v.Side != null) { try { v.Side.Center = P(g.EdgeW > 0 ? g.EdgeCX : g.SideCX, g.BaseCY); } catch { } }
            }
        }

        // ----- views ----------------------------------------------------------------------------------------------------

        /// <summary>How the base view looks at the model: orientation, a 90° turn, and the model size across, up and into the view.</summary>
        private struct Orient
        {
            public ViewDir Dir;
            public bool Rotate;
            public double W, H, D;
            public bool Round;
        }

        /// <summary>A view direction: where the eye is (seen from the model centre), which way is up, and the model axes across, up and into the view.</summary>
        private sealed class ViewDir
        {
            public double[] Eye, Up;
            public int HAxis, VAxis, DAxis;   // 0 = X, 1 = Y, 2 = Z
        }

        /// <summary>
        /// Front, right, top and iso for the model's up axis (setting "UpAxis": Z for models built standing on XY, Y for
        /// Inventor's default). Front looks at the model from the front (-Y when Z is up), right from +X, top from above.
        /// </summary>
        private ViewDir Front => _settings.ZUp ? new ViewDir { Eye = new[] { 0.0, -1, 0 }, Up = new[] { 0.0, 0, 1 }, HAxis = 0, VAxis = 2, DAxis = 1 }
                                               : new ViewDir { Eye = new[] { 0.0, 0, 1 }, Up = new[] { 0.0, 1, 0 }, HAxis = 0, VAxis = 1, DAxis = 2 };
        private ViewDir Right => _settings.ZUp ? new ViewDir { Eye = new[] { 1.0, 0, 0 }, Up = new[] { 0.0, 0, 1 }, HAxis = 1, VAxis = 2, DAxis = 0 }
                                               : new ViewDir { Eye = new[] { 1.0, 0, 0 }, Up = new[] { 0.0, 1, 0 }, HAxis = 2, VAxis = 1, DAxis = 0 };
        private ViewDir Top => _settings.ZUp ? new ViewDir { Eye = new[] { 0.0, 0, 1 }, Up = new[] { 0.0, 1, 0 }, HAxis = 0, VAxis = 1, DAxis = 2 }
                                             : new ViewDir { Eye = new[] { 0.0, 1, 0 }, Up = new[] { 0.0, 0, -1 }, HAxis = 0, VAxis = 2, DAxis = 1 };
        private ViewDir IsoDir => _settings.ZUp ? new ViewDir { Eye = new[] { 1.0, -1, 1 }, Up = new[] { 0.0, 0, 1 } }
                                                : new ViewDir { Eye = new[] { 1.0, 1, 1 }, Up = new[] { 0.0, 1, 0 } };

        /// <summary>A camera looking at the model's centre from <paramref name="dir"/>, for an arbitrary-orientation base view.</summary>
        private Camera CameraFor(Document doc, ViewDir dir)
        {
            Box b = RangeOf(doc);
            double cx = (b.MinPoint.X + b.MaxPoint.X) / 2, cy = (b.MinPoint.Y + b.MaxPoint.Y) / 2, cz = (b.MinPoint.Z + b.MaxPoint.Z) / 2;
            double r = Math.Max(10, b.MinPoint.DistanceTo(b.MaxPoint)) * 3;
            double n = Math.Sqrt(dir.Eye[0] * dir.Eye[0] + dir.Eye[1] * dir.Eye[1] + dir.Eye[2] * dir.Eye[2]);
            Camera cam = _app.TransientObjects.CreateCamera();
            cam.Target = _tg.CreatePoint(cx, cy, cz);
            cam.Eye = _tg.CreatePoint(cx + dir.Eye[0] / n * r, cy + dir.Eye[1] / n * r, cz + dir.Eye[2] / n * r);
            cam.UpVector = _tg.CreateUnitVector(dir.Up[0], dir.Up[1], dir.Up[2]);
            try { cam.Perspective = false; } catch { }
            return cam;
        }

        private DrawingView AddView(Sheet sheet, Document doc, Point2d at, double scale, ViewDir dir) =>
            sheet.DrawingViews.AddBaseView((_Document)doc, at, scale, ViewOrientationTypeEnum.kArbitraryViewOrientation, Lines, "",
                CameraFor(doc, dir), Type.Missing);

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
        private Orient Choose(DocKind kind, Document doc)
        {
            Box box = RangeOf(doc);
            double[] e = { box.MaxPoint.X - box.MinPoint.X, box.MaxPoint.Y - box.MinPoint.Y, box.MaxPoint.Z - box.MinPoint.Z };
            ViewDir front = Front, right = Right, top = Top;
            var o = new Orient { Dir = e[right.HAxis] > e[front.HAxis] * 1.05 ? right : front };

            if ((kind == DocKind.Lathe || kind == DocKind.Tube) && doc is PartDocument part)
            {
                KindDetector.Shape s = KindDetector.Analyse(part.ComponentDefinition);
                o.Round = s.Rotational;
                ModelAxis axis = kind == DocKind.Lathe && s.RotationAxis != ModelAxis.None ? s.RotationAxis : s.LongAxis;
                int a = axis == ModelAxis.X ? 0 : axis == ModelAxis.Y ? 1 : 2;
                // the length or axis horizontal; of the views that do that, the one looking along the thinner other side
                ViewDir best = null;
                foreach (ViewDir d in new[] { front, right, top })
                    if (d.HAxis == a && (best == null || e[d.DAxis] < e[best.DAxis])) best = d;
                if (best != null) o.Dir = best;
                else
                {
                    foreach (ViewDir d in new[] { front, right, top })
                        if (d.VAxis == a && (best == null || e[d.DAxis] < e[best.DAxis])) best = d;
                    if (best != null) { o.Dir = best; o.Rotate = true; }
                }
            }
            o.W = e[o.Dir.HAxis]; o.H = e[o.Dir.VAxis]; o.D = e[o.Dir.DAxis];
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
        private Views CreateOrtho(Sheet sheet, SheetFrame f, Document doc, Orient o, bool top, bool side)
        {
            Detail("Placing views…");
            double s = Guess(f, o.W + (side ? o.D : 0), o.H + (top ? o.D : 0));
            double cx = f.Usable.X0 + f.Usable.W * 0.45, cy = f.Usable.Y0 + f.Usable.H * 0.45;
            double off = (Math.Max(o.W, o.H) + o.D) * s;

            // the base view; a tube or lathe part lying upright is turned by looking at it with its length as "right"
            double[] e = o.Dir.Eye, u = o.Dir.Up;
            if (o.Rotate) u = Cross(u, e);

            var v = new Views { TopAbove = !FirstAngle(sheet) };
            v.Base = AddView(sheet, doc, P(cx, cy), s, new ViewDir { Eye = e, Up = u });
            Tidy(v.Base);
            // projected (linked) views: the top view (third angle: projected upwards; first angle: downwards) and the
            // side view projected to the left
            if (top)
            {
                try
                {
                    v.Below = sheet.DrawingViews.AddProjectedView(v.Base, P(cx, v.TopAbove ? cy + off : cy - off), DrawingViewStyleEnum.kFromBaseDrawingViewStyle, Type.Missing);
                    Tidy(v.Below);
                }
                catch (Exception ex) { Log.Write("Top view failed: " + ex.Message); }
            }
            if (side)
            {
                try
                {
                    v.Side = sheet.DrawingViews.AddProjectedView(v.Base, P(cx - off, cy), DrawingViewStyleEnum.kFromBaseDrawingViewStyle, Type.Missing);
                    Tidy(v.Side);
                }
                catch (Exception ex) { Log.Write("Side view failed: " + ex.Message); }
            }
            return v;
        }

        /// <summary>Does the drawing standard of the template use first-angle projection?</summary>
        private static bool FirstAngle(Sheet sheet)
        {
            try { return ((DrawingDocument)sheet.Parent).StylesManager.ActiveStandardStyle.FirstAngleProjection; }
            catch { return false; }
        }

        private static double[] Cross(double[] a, double[] b) =>
            new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

        private static double[] Neg(double[] a) => new[] { -a[0], -a[1], -a[2] };

        /// <summary>An isometric view (top right), hidden lines removed.</summary>
        private DrawingView CreateIso(Sheet sheet, SheetFrame f, Document doc)
        {
            Detail("Placing the iso view…");
            Box box = RangeOf(doc);
            double ex = box.MaxPoint.X - box.MinPoint.X, ey = box.MaxPoint.Y - box.MinPoint.Y, ez = box.MaxPoint.Z - box.MinPoint.Z;
            double s = Guess(f, 0.707 * (ex + ez), 0.816 * ey + 0.408 * (ex + ez));
            DrawingView v = AddView(sheet, doc, P(f.Usable.X1 - f.Usable.W * 0.2, f.Usable.Y1 - f.Usable.H * 0.2), s, IsoDir);
            Tidy(v);
            return v;
        }

        /// <summary>
        /// The flat pattern (or, for a solid plate or sheet metal that cannot be unfolded, a view along its thinnest
        /// side) with the edge view to its right showing the thickness.
        /// </summary>
        private Views CreateFlat(Sheet sheet, SheetFrame f, Document doc)
        {
            Detail("Placing the flat pattern…");
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
