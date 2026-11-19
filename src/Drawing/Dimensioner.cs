using System;
using System.Collections.Generic;
using System.Linq;
using AutoDraw.Core;
using Inventor;

namespace AutoDraw.Drawing
{
    /// <summary>
    /// Automatic dimensions on a view: overall width and height from the outermost geometry, the chain of bend lines on
    /// a flat pattern (edge → bend → bend → edge), bend notes and hole notes. Each dimension that cannot be made is
    /// skipped; the drawing is still made.
    /// </summary>
    internal sealed class Dimensioner
    {
        private const double Gap = 1.0;     // cm between the view and the first dimension line
        private const double Step = 0.8;    // cm between stacked dimension lines

        private readonly Sheet _sheet;
        private readonly TransientGeometry _tg;
        private readonly TransientObjects _to;
        private readonly GeneralDimensions _dims;
        private bool _updated;

        public Dimensioner(Inventor.Application app, Sheet sheet)
        {
            _sheet = sheet;
            _tg = app.TransientGeometry;
            _to = app.TransientObjects;
            _dims = sheet.DrawingDimensions.GeneralDimensions;
            // the views were just placed: make them precise (not raster previews) and current before dimensioning
            try
            {
                bool raster = false;
                foreach (DrawingView v in sheet.DrawingViews) { try { if (v.IsRasterView) { raster = true; break; } } catch { } }
                if (raster) ((DrawingDocument)sheet.Parent).MakeAllViewsPrecise();
            }
            catch (Exception ex) { Log.Write("Making the views precise failed: " + ex.Message); }
            try { sheet.Update(); } catch { }
        }

        private sealed class Curve
        {
            public DrawingCurve C;
            public Curve2dTypeEnum Type;
            public DrawingEdgeTypeEnum Edge;
            public bool Visible = true;      // at least one segment drawn as a visible line (not hidden, not behind another edge)
            public double MinX, MinY, MaxX, MaxY;
            public double SX, SY, EX, EY;
            public bool IsLine => Type == Curve2dTypeEnum.kLineSegmentCurve2d || Type == Curve2dTypeEnum.kLineCurve2d;
            public bool IsRound => Type == Curve2dTypeEnum.kCircleCurve2d || Type == Curve2dTypeEnum.kCircularArcCurve2d;
            public bool IsBend => Edge == DrawingEdgeTypeEnum.kBendUpEdge || Edge == DrawingEdgeTypeEnum.kBendDownEdge;
        }

        private static List<Curve> Read(DrawingView view)
        {
            var list = new List<Curve>();
            DrawingCurvesEnumerator curves;
            try { curves = view.get_DrawingCurves(Type.Missing); } catch { return list; }
            foreach (DrawingCurve c in curves)
            {
                try
                {
                    var k = new Curve { C = c, Type = c.ProjectedCurveType };
                    try { k.Edge = c.EdgeType; } catch { k.Edge = DrawingEdgeTypeEnum.kUnknownEdge; }
                    try
                    {
                        bool any = false;
                        foreach (DrawingCurveSegment seg in c.Segments) if (seg.Visible && !seg.HiddenLine) { any = true; break; }
                        k.Visible = any;
                    }
                    catch { }
                    Box2d rb = c.Evaluator2D.RangeBox;
                    k.MinX = rb.MinPoint.X; k.MinY = rb.MinPoint.Y; k.MaxX = rb.MaxPoint.X; k.MaxY = rb.MaxPoint.Y;
                    if (k.IsLine || k.Type == Curve2dTypeEnum.kCircularArcCurve2d)
                    {
                        Point2d s = c.StartPoint, e = c.EndPoint;
                        k.SX = s.X; k.SY = s.Y; k.EX = e.X; k.EY = e.Y;
                    }
                    list.Add(k);
                }
                catch { }
            }
            return list;
        }

        private static bool IsOutline(Curve k) =>
            k.Visible && !k.IsBend && k.Edge != DrawingEdgeTypeEnum.kBendExtentEdge && k.Edge != DrawingEdgeTypeEnum.kThreadEdge &&
            k.Edge != DrawingEdgeTypeEnum.kTangentEdge;

        private enum Side { Left, Right, Bottom, Top }

        /// <summary>The geometry at the far <paramref name="side"/> of the view, as a dimension anchor.</summary>
        private GeometryIntent Extreme(List<Curve> curves, Side side, out double at, bool pointsOnly = false)
        {
            at = 0;
            List<Curve> outline = curves.Where(IsOutline).ToList();
            if (outline.Count == 0) return null;
            double eps = 1e-4;
            switch (side)
            {
                case Side.Left: at = outline.Min(k => k.MinX); break;
                case Side.Right: at = outline.Max(k => k.MaxX); break;
                case Side.Bottom: at = outline.Min(k => k.MinY); break;
                default: at = outline.Max(k => k.MaxY); break;
            }
            double v = at;
            bool horizontalSide = side == Side.Left || side == Side.Right;   // the anchor is found along X

            // 1. a straight edge lying on that side (vertical for left/right, horizontal for bottom/top): dimension to the edge
            foreach (Curve k in outline.Where(k => k.IsLine && !pointsOnly))
            {
                bool on = horizontalSide
                    ? Math.Abs(k.SX - v) < eps && Math.Abs(k.EX - v) < eps
                    : Math.Abs(k.SY - v) < eps && Math.Abs(k.EY - v) < eps;
                if (on) { try { return _sheet.CreateGeometryIntent(k.C, Type.Missing); } catch { } }
            }
            // 2. an end point of a line or arc on that side
            foreach (Curve k in outline.Where(k => k.IsLine || k.Type == Curve2dTypeEnum.kCircularArcCurve2d))
            {
                double s = horizontalSide ? k.SX : k.SY, e = horizontalSide ? k.EX : k.EY;
                if (Math.Abs(s - v) < eps) { try { return _sheet.CreateGeometryIntent(k.C, PointIntentEnum.kStartPointIntent); } catch { } }
                if (Math.Abs(e - v) < eps) { try { return _sheet.CreateGeometryIntent(k.C, PointIntentEnum.kEndPointIntent); } catch { } }
            }
            // 3. the quadrant point of a circle or arc
            PointIntentEnum q = side == Side.Left ? PointIntentEnum.kCircularLeftPointIntent
                : side == Side.Right ? PointIntentEnum.kCircularRightPointIntent
                : side == Side.Bottom ? PointIntentEnum.kCircularBottomPointIntent : PointIntentEnum.kCircularTopPointIntent;
            foreach (Curve k in outline.Where(k => k.IsRound))
            {
                double edge = side == Side.Left ? k.MinX : side == Side.Right ? k.MaxX : side == Side.Bottom ? k.MinY : k.MaxY;
                if (Math.Abs(edge - v) < eps) { try { return _sheet.CreateGeometryIntent(k.C, q); } catch { } }
            }
            return null;
        }

        private static (double MinX, double MinY, double MaxX, double MaxY) Box(DrawingView view) =>
            (view.Center.X - view.Width / 2, view.Center.Y - view.Height / 2, view.Center.X + view.Width / 2, view.Center.Y + view.Height / 2);

        private LinearGeneralDimension Linear(Point2d text, GeometryIntent a, GeometryIntent b, DimensionTypeEnum type)
        {
            if (a == null || b == null) return null;
            try { return _dims.AddLinear(text, a, b, type, true, Type.Missing, Type.Missing); }
            catch when (!_updated)
            {
                // views that were just moved or rescaled are not recalculated yet: update the sheet once and try again
                _updated = true;
                try { _sheet.Update(); } catch { }
                return Linear(text, a, b, type);
            }
            catch (Exception ex)
            {
                Log.Write("Dimension failed (" + type + ", " + Describe(a) + " → " + Describe(b) + ", text at " +
                    text.X.ToString("0.0") + "," + text.Y.ToString("0.0") + "): " + ex.Message);
                return null;
            }
        }

        private static string Describe(GeometryIntent i)
        {
            try
            {
                string what = i.Geometry is DrawingCurve c ? c.ProjectedCurveType + "/" + c.EdgeType : i.Geometry?.GetType().Name ?? "?";
                string at = i.Intent is PointIntentEnum p ? p.ToString() : i.Intent == null ? "curve" : i.Intent.ToString();
                return what + " " + at;
            }
            catch { return "?"; }
        }

        /// <summary>
        /// Angle dimensions between flanges that are not square to each other (as 135° in a profile view): for each
        /// pair of edge directions that meet at an angle other than 90°, the two nearest edges get an angle dimension,
        /// its text inside the angle between them.
        /// </summary>
        public void Angles(DrawingView view, bool square = false, int max = 8)
        {
            List<Curve> lines = Read(view).Where(k => k.IsLine && IsOutline(k)).ToList();
            HashSet<Curve> facets = Facets(lines);
            lines = lines.Where(k => !facets.Contains(k)).ToList();
            double diag = Math.Sqrt(view.Width * view.Width + view.Height * view.Height);
            double minLen = Math.Max(0.3, diag * 0.03);
            var edges = lines.Select(k => (K: k, Len: Math.Sqrt((k.EX - k.SX) * (k.EX - k.SX) + (k.EY - k.SY) * (k.EY - k.SY))))
                .Where(e => e.Len >= minLen)
                .Select(e => (e.K, e.Len, Dir: Norm(Math.Atan2(e.K.EY - e.K.SY, e.K.EX - e.K.SX))))
                .ToList();

            // directions, 0.5° apart
            var dirs = new List<double>();
            foreach (var e in edges.OrderByDescending(e => e.Len))
                if (!dirs.Any(d => AngleBetween(d, e.Dir) < 0.5 * Math.PI / 180)) dirs.Add(e.Dir);

            int made = 0;
            for (int i = 0; i < dirs.Count && made < max; i++)
                for (int j = i + 1; j < dirs.Count && made < max; j++)
                {
                    double between = AngleBetween(dirs[i], dirs[j]) * 180 / Math.PI;   // 0..90
                    if (between < 1 || (!square && Math.Abs(between - 90) < 1)) continue;
                    var a = edges.Where(e => AngleBetween(e.Dir, dirs[i]) < 0.01).ToList();
                    var b = edges.Where(e => AngleBetween(e.Dir, dirs[j]) < 0.01).ToList();
                    double best = double.MaxValue;
                    (Curve A, Curve B) pair = (null, null);
                    foreach (var ea in a)
                        foreach (var eb in b)
                        {
                            double d = EndGap(ea.K, eb.K);
                            if (d < best) { best = d; pair = (ea.K, eb.K); }
                        }
                    if (pair.A == null || best > Math.Max(1.0, diag * 0.08)) continue;
                    if (AddAngle(pair.A, pair.B)) made++;
                }
        }

        private bool AddAngle(Curve a, Curve b)
        {
            // where the two edge lines cross, and from there towards the far end of each edge
            double dax = a.EX - a.SX, day = a.EY - a.SY, dbx = b.EX - b.SX, dby = b.EY - b.SY;
            double den = dax * dby - day * dbx;
            if (Math.Abs(den) < 1e-9) return false;
            double t = ((b.SX - a.SX) * dby - (b.SY - a.SY) * dbx) / den;
            double px = a.SX + t * dax, py = a.SY + t * day;
            (double ux, double uy) = Away(a, px, py);
            (double vx, double vy) = Away(b, px, py);
            double bx = ux + vx, by = uy + vy, bl = Math.Sqrt(bx * bx + by * by);
            if (bl < 1e-9) return false;
            Point2d text = _tg.CreatePoint2d(px + bx / bl * 1.4, py + by / bl * 1.4);
            try
            {
                _dims.AddAngular(text, _sheet.CreateGeometryIntent(a.C, Type.Missing), _sheet.CreateGeometryIntent(b.C, Type.Missing),
                    Type.Missing, false, true, false, Type.Missing, Type.Missing);
                return true;
            }
            catch (Exception ex) { Log.Write("Angle dimension failed: " + ex.Message); return false; }
        }

        /// <summary>The unit direction from (px, py) towards the end of the edge farthest from it.</summary>
        private static (double, double) Away(Curve k, double px, double py)
        {
            double ds = (k.SX - px) * (k.SX - px) + (k.SY - py) * (k.SY - py), de = (k.EX - px) * (k.EX - px) + (k.EY - py) * (k.EY - py);
            double x = (de > ds ? k.EX : k.SX) - px, y = (de > ds ? k.EY : k.SY) - py, l = Math.Sqrt(x * x + y * y);
            return l < 1e-9 ? (0, 0) : (x / l, y / l);
        }

        private static double EndGap(Curve a, Curve b)
        {
            double best = double.MaxValue;
            foreach (var p in new[] { (a.SX, a.SY), (a.EX, a.EY) })
                foreach (var q in new[] { (b.SX, b.SY), (b.EX, b.EY) })
                    best = Math.Min(best, Math.Sqrt((p.Item1 - q.Item1) * (p.Item1 - q.Item1) + (p.Item2 - q.Item2) * (p.Item2 - q.Item2)));
            return best;
        }

        /// <summary>A line direction folded into [0, π).</summary>
        private static double Norm(double a)
        {
            while (a < 0) a += Math.PI;
            while (a >= Math.PI) a -= Math.PI;
            return a;
        }

        /// <summary>The angle between two line directions, 0..π/2.</summary>
        private static double AngleBetween(double a, double b)
        {
            double d = Math.Abs(a - b) % Math.PI;
            return Math.Min(d, Math.PI - d);
        }

        /// <summary>Overall width (above the view) and/or height (left of the view). Returns the dimensions made.</summary>
        public List<LinearGeneralDimension> Overall(DrawingView view, bool width, bool height, int stack = 0, bool heightRight = false)
        {
            var made = new List<LinearGeneralDimension>();
            List<Curve> curves = Read(view);
            if (curves.Count == 0) return made;
            var box = Box(view);
            if (width)
            {
                LinearGeneralDimension d = Between(curves, Side.Left, Side.Right, DimensionTypeEnum.kHorizontalDimensionType,
                    (a, b) => _tg.CreatePoint2d((a + b) / 2, box.MaxY + Gap + stack * Step));
                if (d != null) made.Add(d);
            }
            if (height)
            {
                double x = heightRight ? box.MaxX + Gap + stack * Step : box.MinX - Gap - stack * Step;
                LinearGeneralDimension d = Between(curves, Side.Bottom, Side.Top, DimensionTypeEnum.kVerticalDimensionType,
                    (a, b) => _tg.CreatePoint2d(x, (a + b) / 2));
                if (d != null) made.Add(d);
            }
            return made;
        }

        /// <summary>A dimension between the two far sides: to whole edges first, to end points when that fails.</summary>
        private LinearGeneralDimension Between(List<Curve> curves, Side from, Side to, DimensionTypeEnum type, Func<double, double, Point2d> text)
        {
            foreach (bool pointsOnly in new[] { false, true })
            {
                GeometryIntent a = Extreme(curves, from, out double av, pointsOnly), b = Extreme(curves, to, out double bv, pointsOnly);
                if (bv - av <= 1e-3) return null;
                LinearGeneralDimension d = Linear(text(av, bv), a, b, type);
                if (d != null) return d;
            }
            return null;
        }

        /// <summary>The number of visible arcs in a view: the folded view showing the bends as arcs is the profile view.</summary>
        public static int ArcCount(DrawingView view) => Read(view).Count(k => k.Visible && k.Type == Curve2dTypeEnum.kCircularArcCurve2d);

        /// <summary>
        /// Leg lengths in the profile view of a folded part: each outside edge of a leg gets an aligned dimension, measured
        /// to the virtual sharp (where it meets the outside edge of the next leg) or to its free end, placed outside.
        /// </summary>
        public void Legs(DrawingView view, int max = 12)
        {
            List<Curve> all = Read(view).Where(k => k.IsLine && IsOutline(k)).ToList();
            HashSet<Curve> facets = Facets(all);
            double diag = Math.Sqrt(view.Width * view.Width + view.Height * view.Height);
            double minLen = Math.Max(0.3, diag * 0.04);
            var lines = all.Where(k => Len(k) >= minLen && !facets.Contains(k)).ToList();
            if (lines.Count < 2) return;

            // the middle of the profile, weighted by edge length
            double tw = lines.Sum(Len), cx = lines.Sum(k => (k.SX + k.EX) / 2 * Len(k)) / tw, cy = lines.Sum(k => (k.SY + k.EY) / 2 * Len(k)) / tw;

            // of each inside/outside pair (parallel, a sheet thickness apart, side by side) keep the outside one
            var inner = new HashSet<Curve>();
            foreach (Curve a in lines)
                foreach (Curve b in lines)
                {
                    if (a == b || inner.Contains(a) || inner.Contains(b)) continue;
                    if (AngleBetween(Dir(a), Dir(b)) > 0.5 * Math.PI / 180) continue;
                    double gap = LineDistance(a, (b.SX + b.EX) / 2, (b.SY + b.EY) / 2);
                    if (gap < 0.003 || gap > Math.Max(0.6, diag * 0.03) || !Overlap(a, b)) continue;
                    double da = LineDistance(a, cx, cy), db = LineDistance(b, cx, cy);
                    inner.Add(da >= db ? b : a);
                }
            List<Curve> outer = lines.Where(k => !inner.Contains(k)).OrderByDescending(Len).Take(max).ToList();
            double reach = Math.Max(1.5, diag * 0.1);

            // a straight leg as long as the whole view is wide or high already has its overall dimension
            List<Curve> outline = Read(view).Where(IsOutline).ToList();
            double spanX = outline.Count > 0 ? outline.Max(k => k.MaxX) - outline.Min(k => k.MinX) : 0;
            double spanY = outline.Count > 0 ? outline.Max(k => k.MaxY) - outline.Min(k => k.MinY) : 0;

            foreach (Curve o in outer)
            {
                bool flat = Math.Abs(o.SY - o.EY) < 1e-4, upright = Math.Abs(o.SX - o.EX) < 1e-4;
                double reachEnds = Len(o) + 2 * Math.Max(0.1, diag * 0.02);   // up to the virtual sharps (bend radius)
                if ((flat && spanX <= reachEnds && spanX >= Len(o) - 0.05) || (upright && spanY <= reachEnds && spanY >= Len(o) - 0.05)) continue;
                GeometryIntent a = End(o, true, outer, reach), b = End(o, false, outer, reach);
                // outside: from the middle of the edge, away from the middle of the profile
                double mx = (o.SX + o.EX) / 2, my = (o.SY + o.EY) / 2, l = Len(o);
                double nx = -(o.EY - o.SY) / l, ny = (o.EX - o.SX) / l;
                if ((mx - cx) * nx + (my - cy) * ny < 0) { nx = -nx; ny = -ny; }
                Point2d text = _tg.CreatePoint2d(mx + nx * 1.0, my + ny * 1.0);
                DimensionTypeEnum type = Math.Abs(o.SY - o.EY) < 1e-4 ? DimensionTypeEnum.kHorizontalDimensionType
                    : Math.Abs(o.SX - o.EX) < 1e-4 ? DimensionTypeEnum.kVerticalDimensionType : DimensionTypeEnum.kAlignedDimensionType;
                Linear(text, a, b, type);
            }
        }

        /// <summary>
        /// The straight pieces of a curve made of many small bends (the round bottom of a trough): edges that meet the
        /// next edge at both ends with a small turn (under 25°). They get no leg length and no angle; dimensioning each
        /// piece only fills the view with overlapping texts.
        /// </summary>
        private static HashSet<Curve> Facets(List<Curve> lines)
        {
            var facets = new HashSet<Curve>();
            foreach (Curve o in lines)
            {
                double reach = Math.Max(0.05, Math.Min(0.6, 0.5 * Len(o)));
                double turnS = Turn(o, o.SX, o.SY, lines, reach), turnE = Turn(o, o.EX, o.EY, lines, reach);
                if (turnS > 0 && turnS < 25 && turnE > 0 && turnE < 25) facets.Add(o);
            }
            return facets;
        }

        /// <summary>The turn in degrees to the nearest non-parallel edge whose end is within reach of (px, py); -1 when none.</summary>
        private static double Turn(Curve o, double px, double py, List<Curve> lines, double reach)
        {
            double best = reach, turn = -1;
            foreach (Curve n in lines)
            {
                if (n == o) continue;
                double a = AngleBetween(Dir(n), Dir(o)) * 180 / Math.PI;
                if (a < 1) continue;   // parallel: the other side of the sheet, or a continuation
                double d = Math.Min(Dist(px, py, n.SX, n.SY), Dist(px, py, n.EX, n.EY));
                if (d < best) { best = d; turn = a; }
            }
            return turn;
        }

        /// <summary>The end of a leg: the virtual sharp with the leg next to it, else the end point itself.</summary>
        private GeometryIntent End(Curve o, bool start, List<Curve> outer, double reach)
        {
            double px = start ? o.SX : o.EX, py = start ? o.SY : o.EY;
            Curve next = null;
            double best = reach;
            foreach (Curve n in outer)
            {
                if (n == o || AngleBetween(Dir(n), Dir(o)) < 1 * Math.PI / 180) continue;
                double d = Math.Min(Dist(px, py, n.SX, n.SY), Dist(px, py, n.EX, n.EY));
                if (d < best) { best = d; next = n; }
            }
            if (next != null)
            {
                try { return _sheet.CreateGeometryIntent(o.C, next.C); }   // the intersection of the two edges
                catch (Exception ex) { Log.Write("Virtual sharp failed: " + ex.Message); }
            }
            return _sheet.CreateGeometryIntent(o.C, start ? PointIntentEnum.kStartPointIntent : PointIntentEnum.kEndPointIntent);
        }

        private static double Len(Curve k) => Dist(k.SX, k.SY, k.EX, k.EY);
        private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        private static double Dir(Curve k) => Norm(Math.Atan2(k.EY - k.SY, k.EX - k.SX));

        /// <summary>Distance from a point to the (endless) line through an edge.</summary>
        private static double LineDistance(Curve k, double px, double py)
        {
            double l = Len(k);
            return l < 1e-9 ? Dist(k.SX, k.SY, px, py) : Math.Abs((k.EX - k.SX) * (k.SY - py) - (k.SX - px) * (k.EY - k.SY)) / l;
        }

        /// <summary>Do two parallel edges lie side by side (their projections on the direction overlap)?</summary>
        private static bool Overlap(Curve a, Curve b)
        {
            double l = Len(a);
            if (l < 1e-9) return false;
            double ux = (a.EX - a.SX) / l, uy = (a.EY - a.SY) / l;
            double t1 = (b.SX - a.SX) * ux + (b.SY - a.SY) * uy, t2 = (b.EX - a.SX) * ux + (b.EY - a.SY) * uy;
            return Math.Max(t1, t2) > 0 && Math.Min(t1, t2) < l;
        }

        /// <summary>Puts a Ø in front of the dimension value (diameters of lathe parts and round tubes).</summary>
        public static void Diameter(LinearGeneralDimension d)
        {
            try { d.Text.FormattedText = "Ø<DimensionValue/>"; } catch (Exception ex) { Log.Write("Ø prefix failed: " + ex.Message); }
        }

        /// <summary>
        /// Flat pattern: a chain of dimensions from the outer edge to each bend line to the opposite edge (below the
        /// view for vertical bend lines, right of the view for horizontal ones), and a bend note on every bend line.
        /// </summary>
        public void FlatPattern(DrawingView view)
        {
            List<Curve> curves = Read(view);
            List<Curve> bends = curves.Where(k => k.IsBend && k.IsLine && k.Visible).ToList();
            var box = Box(view);
            const double same = 0.005;

            // vertical bend lines → horizontal chain below the view
            List<Curve> vertical = Unique(bends.Where(k => Math.Abs(k.SX - k.EX) < same).OrderBy(k => k.SX), k => k.SX, same);
            if (vertical.Count > 0)
            {
                var anchors = new List<(GeometryIntent I, double At)>();
                GeometryIntent l = Extreme(curves, Side.Left, out double lx);
                if (l != null) anchors.Add((l, lx));
                foreach (Curve k in vertical) anchors.Add((_sheet.CreateGeometryIntent(k.C, Type.Missing), k.SX));
                GeometryIntent r = Extreme(curves, Side.Right, out double rx);
                if (r != null) anchors.Add((r, rx));
                if (Crowded(anchors)) Ordinate(anchors, true, box.MinY - Gap, -1);
                else Chain(anchors, true, box.MinY - Gap, -1);
            }

            // horizontal bend lines → vertical chain right of the view
            List<Curve> horizontal = Unique(bends.Where(k => Math.Abs(k.SY - k.EY) < same).OrderBy(k => k.SY), k => k.SY, same);
            if (horizontal.Count > 0)
            {
                var anchors = new List<(GeometryIntent I, double At)>();
                GeometryIntent b = Extreme(curves, Side.Bottom, out double by);
                if (b != null) anchors.Add((b, by));
                foreach (Curve k in horizontal) anchors.Add((_sheet.CreateGeometryIntent(k.C, Type.Missing), k.SY));
                GeometryIntent t = Extreme(curves, Side.Top, out double ty);
                if (t != null) anchors.Add((t, ty));
                if (Crowded(anchors)) Ordinate(anchors, false, box.MaxX + Gap, 1);
                else Chain(anchors, false, box.MaxX + Gap, 1);
            }

            // bend notes: UP/DOWN, angle and radius, in the bend note style of the template
            var noted = new HashSet<DrawingCurve>();
            var notes = new List<(BendNote N, Curve K)>();
            foreach (Curve k in bends)
            {
                if (!noted.Add(k.C)) continue;
                try { notes.Add((_sheet.DrawingNotes.BendNotes.Add(k.C, Type.Missing), k)); }
                catch (Exception ex) { Log.Write("Bend note failed: " + ex.Message); }
            }
            Stagger(notes.Where(n => Math.Abs(n.K.SY - n.K.EY) < same).ToList(), true);
            Stagger(notes.Where(n => Math.Abs(n.K.SX - n.K.EX) < same).ToList(), false);
        }

        private const double TextPitch = 0.55;   // cm on the sheet between two dimension or note texts side by side

        /// <summary>Bend lines so close together that the chain's texts would overlap.</summary>
        private static bool Crowded(List<(GeometryIntent I, double At)> anchors)
        {
            var at = anchors.Select(a => a.At).OrderBy(x => x).ToList();
            int close = 0;
            for (int i = 0; i + 1 < at.Count; i++) if (at[i + 1] - at[i] < TextPitch * 1.5) close++;
            return close >= 3;
        }

        /// <summary>
        /// Ordinate dimensions (0 at the first edge) instead of a chain, for bend lines close together: the texts are
        /// spread out along the line with a jog to their bend line, so they never overlap.
        /// </summary>
        private void Ordinate(List<(GeometryIntent I, double At)> anchors, bool horizontal, double line, double outward)
        {
            if (anchors.Count < 2) return;
            OrdinateDimensionSet set;
            try
            {
                ObjectCollection intents = _to.CreateObjectCollection();
                foreach (var a in anchors) intents.Add(a.I);
                double mid = (anchors.Min(a => a.At) + anchors.Max(a => a.At)) / 2;
                Point2d place = horizontal ? _tg.CreatePoint2d(mid, line) : _tg.CreatePoint2d(line, mid);
                set = _sheet.DrawingDimensions.OrdinateDimensionSets.Add(intents, place,
                    horizontal ? DimensionTypeEnum.kHorizontalDimensionType : DimensionTypeEnum.kVerticalDimensionType, Type.Missing, Type.Missing);
            }
            catch (Exception ex)
            {
                Log.Write("Ordinate dimensions failed (" + ex.Message + "); a chain instead");
                Chain(anchors, horizontal, line, outward);
                return;
            }

            // where each text is now (at its bend line), and where it goes: pushed apart to at least TextPitch
            var members = new List<(OrdinateDimension D, double At)>();
            foreach (OrdinateDimension m in set.Members)
            {
                try { Point2d o = m.Text.Origin; members.Add((m, horizontal ? o.X : o.Y)); } catch { }
            }
            members = members.OrderBy(m => m.At).ToList();
            double[] spread = Spread(members.Select(m => m.At).ToArray(), TextPitch);
            double jogFrom = line - outward * 0.2, jogTo = line + outward * 0.35, textAt = line + outward * 0.45;
            for (int i = 0; i < members.Count; i++)
            {
                if (Math.Abs(spread[i] - members[i].At) < 1e-4) continue;
                OrdinateDimension d = members[i].D;
                try
                {
                    d.JogPointOne = horizontal ? _tg.CreatePoint2d(members[i].At, jogFrom) : _tg.CreatePoint2d(jogFrom, members[i].At);
                    d.JogPointTwo = horizontal ? _tg.CreatePoint2d(spread[i], jogTo) : _tg.CreatePoint2d(jogTo, spread[i]);
                    d.Text.Origin = horizontal ? _tg.CreatePoint2d(spread[i], textAt) : _tg.CreatePoint2d(textAt, spread[i]);
                }
                catch (Exception ex) { Log.Write("Moving an ordinate text failed: " + ex.Message); }
            }
        }

        /// <summary>Positions at least <paramref name="pitch"/> apart, each group of pushed texts centred on its originals.</summary>
        internal static double[] Spread(double[] at, double pitch)
        {
            int n = at.Length;
            var groups = new List<(int From, int To, double Start)>();
            for (int i = 0; i < n; i++)
            {
                groups.Add((i, i, at[i]));
                // merge with the group before while they overlap; a merged group is centred on its members
                while (groups.Count > 1)
                {
                    var b = groups[groups.Count - 1];
                    var a = groups[groups.Count - 2];
                    if (a.Start + (a.To - a.From + 1) * pitch <= b.Start + 1e-9) break;
                    int from = a.From, to = b.To;
                    double mean = 0;
                    for (int k = from; k <= to; k++) mean += at[k] - (k - from) * pitch;
                    groups.RemoveRange(groups.Count - 2, 2);
                    groups.Add((from, to, mean / (to - from + 1)));
                }
            }
            var result = new double[n];
            foreach (var g in groups) for (int k = g.From; k <= g.To; k++) result[k] = g.Start + (k - g.From) * pitch;
            return result;
        }

        /// <summary>
        /// Bend notes of bend lines close together, which Inventor puts at the middle of each line, on top of each other:
        /// moved along their lines into columns, so neighbouring lines have their notes side by side.
        /// </summary>
        private static void Stagger(List<(BendNote N, Curve K)> notes, bool horizontalLines)
        {
            if (notes.Count < 2) return;
            notes = notes.OrderBy(n => horizontalLines ? n.K.SY : n.K.SX).ToList();
            double across(Curve k) => horizontalLines ? k.SY : k.SX;
            double minGap = double.MaxValue;
            for (int i = 0; i + 1 < notes.Count; i++) minGap = Math.Min(minGap, Math.Abs(across(notes[i + 1].K) - across(notes[i].K)));
            double noteW = 0, noteH = 0;
            foreach (var n in notes)
            {
                try
                {
                    Box2d rb = n.N.RangeBox;
                    double w = rb.MaxPoint.X - rb.MinPoint.X, h = rb.MaxPoint.Y - rb.MinPoint.Y;
                    noteW = Math.Max(noteW, horizontalLines ? w : h);
                    noteH = Math.Max(noteH, horizontalLines ? h : w);
                }
                catch { }
            }
            if (noteW <= 0 || minGap >= noteH * 2.0) return;   // they fit as they are

            // lines that lie within a note's height of each other need different columns
            int need = (int)Math.Ceiling(noteH * 2.0 / Math.Max(minGap, 1e-3)) + 1;
            double from = notes.Max(n => horizontalLines ? Math.Min(n.K.SX, n.K.EX) : Math.Min(n.K.SY, n.K.EY));
            double to = notes.Min(n => horizontalLines ? Math.Max(n.K.SX, n.K.EX) : Math.Max(n.K.SY, n.K.EY));
            double length = to - from;
            int cols = Math.Max(2, Math.Min(need, (int)Math.Floor(length / (noteW * 1.15))));
            double colW = length / cols;
            // each note takes the next column that no note within a note's height of it has taken
            var col = new int[notes.Count];
            for (int i = 0; i < notes.Count; i++)
            {
                var taken = new HashSet<int>();
                for (int j = i - 1; j >= 0 && Math.Abs(across(notes[i].K) - across(notes[j].K)) < noteH * 2.0; j--) taken.Add(col[j]);
                int c = i == 0 ? 0 : (col[i - 1] + 1) % cols;
                for (int t = 0; t < cols && taken.Contains(c); t++) c = (c + 1) % cols;
                col[i] = c;
            }
            for (int i = 0; i < notes.Count; i++)
            {
                double along = from + colW * (col[i] + 0.5);
                try
                {
                    Point2d p = notes[i].N.Position;
                    if (horizontalLines) p.X = along; else p.Y = along;
                    notes[i].N.Position = p;
                }
                catch (Exception ex) { Log.Write("Moving a bend note failed: " + ex.Message); }
            }
        }

        /// <summary>
        /// A chain of dimensions on one line; a short piece (its text would not fit between the arrows) that follows
        /// another short piece goes one step further out, so the texts stay apart.
        /// </summary>
        private void Chain(List<(GeometryIntent I, double At)> anchors, bool horizontal, double line, double outward)
        {
            const double shortPiece = 1.6;   // cm on the sheet
            bool lastShortOut = false, lastShort = false;
            for (int i = 0; i + 1 < anchors.Count; i++)
            {
                double len = Math.Abs(anchors[i + 1].At - anchors[i].At);
                if (len < 1e-3) continue;
                bool isShort = len < shortPiece;
                bool outer = isShort && lastShort && !lastShortOut;
                double at = line + (outer ? outward * Step : 0);
                double mid = (anchors[i].At + anchors[i + 1].At) / 2;
                Point2d p = horizontal ? _tg.CreatePoint2d(mid, at) : _tg.CreatePoint2d(at, mid);
                Linear(p, anchors[i].I, anchors[i + 1].I,
                    horizontal ? DimensionTypeEnum.kHorizontalDimensionType : DimensionTypeEnum.kVerticalDimensionType);
                lastShortOut = outer;
                lastShort = isShort;
            }
        }

        private static List<Curve> Unique(IEnumerable<Curve> sorted, Func<Curve, double> at, double tol)
        {
            var list = new List<Curve>();
            foreach (Curve k in sorted)
                if (list.Count == 0 || Math.Abs(at(list[list.Count - 1]) - at(k)) > tol) list.Add(k);
            return list;
        }

        /// <summary>One hole/thread note per hole size (the note style shows the count, such as 3x M16x2).</summary>
        public void HoleNotes(DrawingView view, int max = 8)
        {
            List<Curve> circles = Read(view).Where(k => k.Type == Curve2dTypeEnum.kCircleCurve2d && IsOutline(k)).ToList();
            var box = Box(view);
            const double away = 3.2;   // outside the overall and chain dimensions
            var used = new Dictionary<int, List<double>>();   // side → positions along it already taken
            var sizes = new HashSet<long>();
            foreach (Curve k in circles)
            {
                if (sizes.Count >= max) break;
                long size = (long)Math.Round((k.MaxX - k.MinX) * 1000);
                if (!sizes.Add(size)) continue;
                double cx = (k.MinX + k.MaxX) / 2, cy = (k.MinY + k.MaxY) / 2;
                // outside the part, on the side the hole is nearest to: 0 left, 1 right, 2 bottom, 3 top
                double[] dist = { cx - box.MinX, box.MaxX - cx, cy - box.MinY, box.MaxY - cy };
                int side = Array.IndexOf(dist, dist.Min());
                double along = side < 2 ? cy : cx;
                if (!used.TryGetValue(side, out List<double> taken)) used[side] = taken = new List<double>();
                while (taken.Any(t => Math.Abs(t - along) < 1.0)) along += side < 2 ? -1.0 : 1.0;
                taken.Add(along);
                Point2d p = side == 0 ? _tg.CreatePoint2d(box.MinX - away, along)
                    : side == 1 ? _tg.CreatePoint2d(box.MaxX + away, along)
                    : side == 2 ? _tg.CreatePoint2d(along, box.MinY - away)
                    : _tg.CreatePoint2d(along, box.MaxY + away);
                try { _sheet.DrawingNotes.HoleThreadNotes.Add(p, _sheet.CreateGeometryIntent(k.C, Type.Missing), false, Type.Missing); }
                catch { /* not a hole feature: no note */ }
            }
        }
    }
}
