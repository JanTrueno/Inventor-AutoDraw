using System;
using System.Collections.Generic;
using System.Linq;
using AutoDraw.Core;

namespace AutoDraw.Drawing
{
    internal enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>
    /// Views that stay together on the sheet, as one box: a base view with optionally its top view above it and its
    /// right view to its right (the regular layout), or a single view. Sizes are model sizes (cm at 1:1); margins and gaps
    /// are sheet centimetres kept free for dimensions.
    /// </summary>
    internal sealed class ViewGroup
    {
        public double BW, BH;          // base view
        public double BelowH;          // height of the top view, placed above the base view (its width is BW); 0 = none
        public double SideW;           // width of the right view, placed right of the base view (its height is BH); 0 = none
        public double MarginL = 2.5, MarginR = 0.5, MarginT = 2.5, MarginB = 0.5, Gap = 3.0;
        public double ExtraX, ExtraY;
        public bool FillY;             // spreading: the top view goes to the top and the base view as low as it can
        public bool AlignRight;        // spreading: against the right instead of centred
        public double MaxExtraX = 10;  // spreading: at most this much extra between side and base view  // extra room between the views, added when spreading out (side view / top view)
        private double GapX => Gap + ExtraX;
        private double GapY => Gap + ExtraY;
        public Corner Prefer = Corner.TopLeft;
        public bool Flexible;          // may get a smaller scale than the other groups (iso views)
        public bool TopAbove = true;   // the top view above the base view (third angle), else below it (first angle)
        public bool SideLeft = true;   // the side view left of the base view, else right of it
        public double EdgeW;           // width of an edge view placed RIGHT of the base view (flat pattern thickness); 0 = none
        public bool Fixed;             // against the left edge, as low as possible (the flat pattern: bottom left); not spread out
        public Region? Area;           // keep inside this area instead of the whole usable area (iso above the parts list)

        public double Scale;           // result
        public double X0, Y1;          // result: top-left corner of the box

        public bool HasBelow => BelowH > 1e-6;
        public bool HasSide => SideW > 1e-6;

        public (double W, double H) Size(double s) => (
            MarginL + BW * s + (HasSide ? GapX + SideW * s : 0) + (EdgeW > 1e-6 ? Gap + EdgeW * s : 0) + MarginR,
            MarginT + BH * s + (HasBelow ? GapY + BelowH * s : 0) + MarginB);

        private double BaseLeft => X0 + MarginL + (SideLeft && HasSide ? SideW * Scale + GapX : 0);
        private double BaseTop => Y1 - MarginT - (TopAbove && HasBelow ? BelowH * Scale + GapY : 0);
        public double BaseCX => BaseLeft + BW * Scale / 2;
        public double BaseCY => BaseTop - BH * Scale / 2;
        public double BelowCY => TopAbove ? Y1 - MarginT - BelowH * Scale / 2 : BaseTop - BH * Scale - GapY - BelowH * Scale / 2;
        public double SideCX => SideLeft ? X0 + MarginL + SideW * Scale / 2 : BaseLeft + BW * Scale + GapX + SideW * Scale / 2;
        public double EdgeCX => BaseLeft + BW * Scale + Gap + EdgeW * Scale / 2;   // the edge view, right of the base view

        public Region Box => new Region(X0, Y1 - Size(Scale).H, X0 + Size(Scale).W, Y1);
    }

    /// <summary>
    /// Finds the largest standard scale at which all view groups fit in the free area of the sheet (inside the usable
    /// area, outside the title block and parts list), and where each goes. Each group is tried at the corners of the
    /// free area and next to the groups already placed, closest to its preferred corner first.
    /// </summary>
    internal static class Layout
    {
        private const double Spacing = 1.0;
        private const double AboveShare = 1.0 / 3;

        public static bool Pack(IList<ViewGroup> groups, Region usable, IList<Region> obstacles, double maxScale = 10)
        {
            foreach (double s in Scales.Standard.Where(x => x <= maxScale + 1e-9))
                if (TryAt(groups, s, usable, obstacles)) { Spread(groups, usable, obstacles); return true; }

            // nothing fits: the smallest scale, stacked from the top left
            double min = Scales.Standard[Scales.Standard.Length - 1];
            double y = usable.Y1;
            foreach (ViewGroup g in groups) { g.Scale = min; g.X0 = usable.X0; g.Y1 = y; y -= g.Size(min).H; }
            return false;
        }

        /// <summary>
        /// Uses the room left over: each group (not the flat pattern) gets more space between its views, up to half the
        /// free room and at most 10 cm; across it is centred in the free span around it, up and down it sits a bit above
        /// the middle (a third of the free room above it).
        /// </summary>
        public static void Spread(IList<ViewGroup> groups, Region usable, IList<Region> obstacles)
        {
            foreach (ViewGroup g in groups)
            {
                if (g.Fixed) continue;
                // centre up and down first: that can lift the group clear of low texts (order number) beside it, which
                // then no longer limit how far it may spread across
                SpreadY(g, groups, usable, obstacles, false);
                SpreadX(g, groups, usable, obstacles);
                SpreadY(g, groups, usable, obstacles, true);
            }
        }

        private static List<Region> Others(ViewGroup g, IList<ViewGroup> groups, IList<Region> obstacles) =>
            obstacles.Concat(groups.Where(o => o != g).Select(o => o.Box)).ToList();

        /// <summary>Across: from the nearest thing on the left to the nearest on the right, within the group's height.</summary>
        private static void SpreadX(ViewGroup g, IList<ViewGroup> groups, Region usable, IList<Region> obstacles)
        {
            Region area = g.Area ?? usable, b = g.Box;
            double left = area.X0, right = area.X1;
            foreach (Region o in Others(g, groups, obstacles).Where(o => o.Y0 < b.Y1 && b.Y0 < o.Y1))
            {
                if (o.X1 <= b.X0 + 1e-6) left = Math.Max(left, o.X1 + Spacing);
                else if (o.X0 >= b.X1 - 1e-6) right = Math.Min(right, o.X0 - Spacing);
            }
            double free = right - left - b.W;
            if (free <= 0.01) return;
            if (g.HasSide) { double add = Math.Min(free * 0.5, g.MaxExtraX); g.ExtraX += add; free -= add; }
            g.X0 = g.AlignRight ? left + free : left + free / 2;
        }

        /// <summary>Up and down: from the nearest thing below to the nearest above, within the group's width.</summary>
        private static void SpreadY(ViewGroup g, IList<ViewGroup> groups, Region usable, IList<Region> obstacles, bool extra)
        {
            Region area = g.Area ?? usable, b = g.Box;
            double bottom = area.Y0, top = area.Y1;
            foreach (Region o in Others(g, groups, obstacles).Where(o => o.X0 < b.X1 && b.X0 < o.X1))
            {
                if (o.Y1 <= b.Y0 + 1e-6) bottom = Math.Max(bottom, o.Y1 + Spacing);
                else if (o.Y0 >= b.Y1 - 1e-6) top = Math.Min(top, o.Y0 - Spacing);
            }
            double free = top - bottom - b.H;
            if (free <= 0.01) return;
            if (extra && g.HasBelow) { double add = g.FillY ? free : Math.Min(free * 0.5, 10); g.ExtraY += add; free -= add; }
            g.Y1 = top - free * (g.FillY ? 0 : AboveShare);   // a bit above the middle: a third of the room above, two thirds below
        }

        private static bool TryAt(IList<ViewGroup> groups, double s, Region usable, IList<Region> obstacles)
        {
            var placed = new List<Region>();
            foreach (ViewGroup g in groups)
            {
                bool ok = false;
                IEnumerable<double> scales = g.Flexible
                    ? new[] { s, Scales.Below(s), Scales.Below(Scales.Below(s)) }
                    : new[] { s };
                Region area = g.Area ?? usable;
                foreach (double gs in scales)
                {
                    (double w, double h) = g.Size(gs);
                    if (w > area.W || h > area.H) continue;
                    IEnumerable<(double X0, double Y1)> spots = Candidates(area, placed.Concat(obstacles).ToList(), w, h, g.Prefer);
                    if (g.Fixed) spots = spots.Where(p => Math.Abs(p.X0 - area.X0) < 1e-6);   // against the left edge, as low as it fits
                    foreach ((double x0, double y1) in spots)
                    {
                        var r = new Region(x0, y1 - h, x0 + w, y1);
                        if (r.X0 < area.X0 - 1e-6 || r.X1 > area.X1 + 1e-6 || r.Y0 < area.Y0 - 1e-6 || r.Y1 > area.Y1 + 1e-6) continue;
                        if (obstacles.Any(o => r.Overlaps(o)) || placed.Any(p => r.Overlaps(p))) continue;
                        g.Scale = gs; g.X0 = x0; g.Y1 = y1;
                        placed.Add(r);
                        ok = true;
                        break;
                    }
                    if (ok) break;
                }
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// Top-left corners to try for a w × h box: every combination of a horizontal position (area edges, just right
        /// or left of each obstacle or placed box) and a vertical one (area edges, just below or above each of them),
        /// kept inside the area, nearest to the preferred corner first.
        /// </summary>
        private static IEnumerable<(double X0, double Y1)> Candidates(Region u, List<Region> rects, double w, double h, Corner prefer)
        {
            var xs = new List<double> { u.X0, u.X1 - w };
            var ys = new List<double> { u.Y1, u.Y0 + h };
            foreach (Region p in rects)
            {
                xs.Add(p.X1 + Spacing); xs.Add(p.X0 - Spacing - w);
                ys.Add(p.Y0 - Spacing); ys.Add(p.Y1 + Spacing + h);
            }
            double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
            var c = new HashSet<(double, double)>();
            foreach (double x in xs)
                foreach (double y in ys)
                    c.Add((Math.Round(Clamp(x, u.X0, u.X1 - w), 4), Math.Round(Clamp(y, u.Y0 + h, u.Y1), 4)));

            double tx = prefer == Corner.TopLeft || prefer == Corner.BottomLeft ? u.X0 : u.X1;
            double ty = prefer == Corner.TopLeft || prefer == Corner.TopRight ? u.Y1 : u.Y0;
            return c.Select(p => (X0: p.Item1, Y1: p.Item2)).OrderBy(p =>
            {
                double cx = prefer == Corner.TopLeft || prefer == Corner.BottomLeft ? p.X0 : p.X0 + w;
                double cy = prefer == Corner.TopLeft || prefer == Corner.TopRight ? p.Y1 : p.Y1 - h;
                return Math.Abs(cx - tx) + Math.Abs(cy - ty);
            });
        }
    }
}
