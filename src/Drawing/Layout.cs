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
        public Corner Prefer = Corner.TopLeft;
        public bool Flexible;          // may get a smaller scale than the other groups (iso views)
        public double EdgeW;           // width of an edge view placed RIGHT of the base view (flat pattern thickness); 0 = none
        public bool Fixed;             // only at its preferred corner (the flat pattern: always bottom left)
        public Region? Area;           // keep inside this area instead of the whole usable area (iso above the parts list)

        public double Scale;           // result
        public double X0, Y1;          // result: top-left corner of the box

        public bool HasBelow => BelowH > 1e-6;
        public bool HasSide => SideW > 1e-6;

        public (double W, double H) Size(double s) => (
            MarginL + BW * s + (HasSide ? Gap + SideW * s : 0) + (EdgeW > 1e-6 ? Gap + EdgeW * s : 0) + MarginR,
            MarginT + BH * s + (HasBelow ? Gap + BelowH * s : 0) + MarginB);

        private double BaseLeft => X0 + MarginL;
        private double BaseTop => Y1 - MarginT - (HasBelow ? BelowH * Scale + Gap : 0);
        public double BaseCX => BaseLeft + BW * Scale / 2;
        public double BaseCY => BaseTop - BH * Scale / 2;
        public double BelowCY => Y1 - MarginT - BelowH * Scale / 2;     // the top view, above the base view
        public double SideCX => BaseLeft + BW * Scale + Gap + SideW * Scale / 2;   // the right view, right of the base view
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

        public static bool Pack(IList<ViewGroup> groups, Region usable, IList<Region> obstacles, double maxScale = 10)
        {
            foreach (double s in Scales.Standard.Where(x => x <= maxScale + 1e-9))
                if (TryAt(groups, s, usable, obstacles)) return true;

            // nothing fits: the smallest scale, stacked from the top left
            double min = Scales.Standard[Scales.Standard.Length - 1];
            double y = usable.Y1;
            foreach (ViewGroup g in groups) { g.Scale = min; g.X0 = usable.X0; g.Y1 = y; y -= g.Size(min).H; }
            return false;
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
                    if (g.Fixed) spots = spots.Take(1);
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

        /// <summary>Top-left corners to try for a w × h box, nearest to the preferred corner first.</summary>
        private static IEnumerable<(double X0, double Y1)> Candidates(Region u, List<Region> placed, double w, double h, Corner prefer)
        {
            var c = new List<(double X0, double Y1)>
            {
                (u.X0, u.Y1), (u.X1 - w, u.Y1), (u.X0, u.Y0 + h), (u.X1 - w, u.Y0 + h)
            };
            foreach (Region p in placed)
            {
                c.Add((p.X1 + Spacing, p.Y1));          // right of it, tops level
                c.Add((p.X1 + Spacing, p.Y0 + h));      // right of it, bottoms level
                c.Add((p.X0, p.Y0 - Spacing));          // below it, left sides level
                c.Add((p.X1 - w, p.Y0 - Spacing));      // below it, right sides level
                c.Add((u.X1 - w, p.Y0 - Spacing));      // below it, against the right edge
                c.Add((u.X0, p.Y0 - Spacing));          // below it, against the left edge
            }
            double tx = prefer == Corner.TopLeft || prefer == Corner.BottomLeft ? u.X0 : u.X1;
            double ty = prefer == Corner.TopLeft || prefer == Corner.TopRight ? u.Y1 : u.Y0;
            return c.OrderBy(p =>
            {
                double cx = prefer == Corner.TopLeft || prefer == Corner.BottomLeft ? p.X0 : p.X0 + w;
                double cy = prefer == Corner.TopLeft || prefer == Corner.TopRight ? p.Y1 : p.Y1 - h;
                return Math.Abs(cx - tx) + Math.Abs(cy - ty);
            });
        }
    }
}
