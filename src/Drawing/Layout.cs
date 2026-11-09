using System;
using System.Collections.Generic;
using System.Linq;
using AutoDraw.Core;

namespace AutoDraw.Drawing
{
    internal enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>
    /// Views that stay together on the sheet: a base view with optionally its top view above it and its side view to
    /// its left (or, for a flat pattern, an edge view to its right), or a single view. Sizes are model sizes (cm at 1:1);
    /// margins and gaps are sheet centimetres kept free for dimensions. Each view has its own box
    /// (<see cref="Parts"/>): the empty corner above the side view may hold something else, such as the header text.
    /// </summary>
    internal sealed class ViewGroup
    {
        public double BW, BH;          // base view
        public double BelowH;          // height of the top view (its width is BW); 0 = none
        public double SideW;           // width of the side view (its height is BH); 0 = none
        public double MarginL = 2.5, MarginR = 0.5, MarginT = 2.5, MarginB = 0.5, Gap = 3.0;
        public double ExtraX, ExtraY;  // extra room between the views, added when spreading out
        private double GapX => Gap + ExtraX;
        private double GapY => Gap + ExtraY;

        public Corner Prefer = Corner.TopLeft;
        public bool Flexible;          // may get a smaller scale than the other groups (iso views)
        public bool TopAbove = true;   // the top view above the base view (third angle), else below it (first angle)
        public bool SideLeft = true;   // the side view left of the base view, else right of it
        public double EdgeW;           // width of an edge view right of the base view (flat pattern thickness); 0 = none
        public bool Fixed;             // against the left edge, as low as possible (the flat pattern); not spread out
        public Region? Area;           // keep inside this area instead of the whole usable area

        // spreading out
        public bool FillY;             // base and side view as low as they go, top view as high as it goes
        public bool AlignTop;          // the group against the top of its free room
        public bool AlignRight;        // against the right instead of centred
        public double MaxExtraX = 10, MaxExtraY = 10;

        public double Scale;           // result
        public double X0, Y1;          // result: top-left corner of the outer box

        public bool HasBelow => BelowH > 1e-6;
        public bool HasSide => SideW > 1e-6;
        private bool TopPart => HasBelow && TopAbove;
        private bool SidePart => HasSide && SideLeft;

        public (double W, double H) Size(double s) => (
            MarginL + BW * s + (HasSide ? GapX + SideW * s : 0) + (EdgeW > 1e-6 ? Gap + EdgeW * s : 0) + MarginR,
            MarginT + BH * s + (HasBelow ? GapY + BelowH * s : 0) + MarginB);

        private double BaseLeft => X0 + MarginL + (SidePart ? SideW * Scale + GapX : 0);
        private double BaseTop => Y1 - MarginT - (TopPart ? BelowH * Scale + GapY : 0);
        public double BaseCX => BaseLeft + BW * Scale / 2;
        public double BaseCY => BaseTop - BH * Scale / 2;
        public double BelowCY => TopAbove ? Y1 - MarginT - BelowH * Scale / 2 : BaseTop - BH * Scale - GapY - BelowH * Scale / 2;
        public double SideCX => SideLeft ? X0 + MarginL + SideW * Scale / 2 : BaseLeft + BW * Scale + GapX + SideW * Scale / 2;
        public double EdgeCX => BaseLeft + BW * Scale + Gap + EdgeW * Scale / 2;

        public Region Box => new Region(X0, Y1 - Size(Scale).H, X0 + Size(Scale).W, Y1);

        /// <summary>The boxes of the views themselves, with room for their dimensions: 'B' base, 'T' top view, 'S' side view.</summary>
        public List<(Region R, char Role)> Parts()
        {
            Region box = Box;
            if (!TopPart && !SidePart) return new List<(Region, char)> { (box, 'B') };
            double lowerTop = TopPart ? BaseTop + Math.Min(GapY, MarginT) : Y1;
            double baseRight = BaseLeft + BW * Scale + MarginR + (EdgeW > 1e-6 ? Gap + EdgeW * Scale : 0) + (HasSide && !SideLeft ? GapX + SideW * Scale : 0);
            var parts = new List<(Region, char)> { (new Region(BaseLeft - MarginL, box.Y0, baseRight, lowerTop), 'B') };
            if (TopPart) parts.Add((new Region(BaseLeft - MarginL, BaseTop + GapY - 0.3, BaseLeft + BW * Scale + MarginR, Y1), 'T'));
            if (SidePart) parts.Add((new Region(X0, box.Y0, X0 + MarginL + SideW * Scale + 0.3, lowerTop), 'S'));
            return parts;
        }
    }

    /// <summary>
    /// Finds the largest standard scale at which all view groups fit in their areas (outside the title block, the
    /// corner texts and the parts list), places them, and then spreads them out into the room that is left.
    /// </summary>
    internal static class Layout
    {
        private const double Spacing = 1.0;
        private const double AboveShare = 1.0 / 3;

        public static bool Pack(IList<ViewGroup> groups, Region usable, IList<Region> obstacles, double maxScale = 10)
        {
            foreach (ViewGroup g in groups) { g.ExtraX = 0; g.ExtraY = 0; }   // a fresh start when packed again
            foreach (double s in Scales.Standard.Where(x => x <= maxScale + 1e-9))
                if (TryAt(groups, s, usable, obstacles)) { Spread(groups, usable, obstacles); return true; }

            // nothing fits: the smallest scale, stacked from the top left
            double min = Scales.Standard[Scales.Standard.Length - 1];
            double y = usable.Y1;
            foreach (ViewGroup g in groups) { g.Scale = min; g.X0 = usable.X0; g.Y1 = y; y -= g.Size(min).H; }
            return false;
        }

        /// <summary>
        /// Parts, the same on every sheet: the flat pattern (sheet metal) bottom left and the views above it, top view
        /// against the top, both in the zone left of the title block column; the iso view in the right-hand column
        /// above the title block. <paramref name="columnX"/> is the left edge of the title block column.
        /// </summary>
        public static void PartZones(Region u, double columnX, ViewGroup fg, ViewGroup og, ViewGroup ig)
        {
            var left = new Region(u.X0, u.Y0, columnX - 1.0, u.Y1);
            if (fg != null) { fg.Area = left; fg.Prefer = Corner.BottomLeft; fg.Fixed = true; }
            og.Area = left;
            og.Prefer = Corner.TopLeft;
            og.AlignTop = true; og.FillY = false; og.AlignRight = false;
            og.MaxExtraX = 6; og.MaxExtraY = 3;
            ig.Area = new Region(columnX, u.Y0, u.X1, u.Y1);
            ig.Prefer = Corner.TopRight;
        }

        /// <summary>
        /// Keeps the views in their zone, unless they get a larger scale anywhere on the sheet (a tall folded part next
        /// to its flat pattern): then their area is the whole sheet.
        /// </summary>
        public static void LargerOrthoArea(IList<ViewGroup> groups, Region usable, IList<Region> obstacles, ViewGroup og)
        {
            Region? zone = og.Area;
            if (zone == null) return;
            double inZone = Pack(groups, usable, obstacles) ? og.Scale : 0;
            og.Area = null;
            double anywhere = Pack(groups, usable, obstacles) ? og.Scale : 0;
            if (anywhere <= inZone + 1e-9) og.Area = zone;
        }

        // ----- placing ----------------------------------------------------------------------------------------------------

        private static bool TryAt(IList<ViewGroup> groups, double s, Region usable, IList<Region> obstacles)
        {
            var placed = new List<ViewGroup>();
            foreach (ViewGroup g in groups)
            {
                bool ok = false;
                // an iso view may be up to five standard scales smaller than the other views
                IEnumerable<double> scales = g.Flexible ? Scales.Standard.Where(x => x <= s + 1e-12).Take(6) : new[] { s };
                Region area = g.Area ?? usable;
                List<Region> blocked = obstacles.Concat(placed.SelectMany(p => p.Parts().Select(q => q.R))).ToList();
                foreach (double gs in scales)
                {
                    (double w, double h) = g.Size(gs);
                    if (w > area.W + 1e-6 || h > area.H + 1e-6) continue;
                    IEnumerable<(double X0, double Y1)> spots = Candidates(area, blocked, w, h, g.Prefer);
                    if (g.Fixed) spots = spots.Where(p => Math.Abs(p.X0 - area.X0) < 1e-6);   // against the left edge, as low as it fits
                    foreach ((double x0, double y1) in spots)
                    {
                        g.Scale = gs; g.X0 = x0; g.Y1 = y1;
                        if (g.Parts().Any(p => blocked.Any(o => p.R.Overlaps(o)))) continue;
                        ok = true;
                        break;
                    }
                    if (ok) break;
                }
                if (!ok) return false;
                placed.Add(g);
            }
            return true;
        }

        /// <summary>
        /// Top-left corners to try for a w × h box: every combination of a horizontal position (area edges, just right
        /// or left of each obstacle or placed view) and a vertical one (area edges, just below or above each of them),
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

        // ----- spreading out --------------------------------------------------------------------------------------------

        /// <summary>
        /// Uses the room left over, group by group (not the flat pattern): more space between the views and a better
        /// place in the free room around them. Up and down first (that can lift a group clear of low texts beside it),
        /// then across, then up and down again with the extra space between top view and base view.
        /// </summary>
        public static void Spread(IList<ViewGroup> groups, Region usable, IList<Region> obstacles)
        {
            foreach (ViewGroup g in groups)
            {
                if (g.Fixed) continue;
                SpreadY(g, groups, usable, obstacles, false);
                SpreadX(g, groups, usable, obstacles);
                SpreadY(g, groups, usable, obstacles, true);
            }
        }

        private enum Dir { Left, Right, Down, Up }

        /// <summary>How far the given views of <paramref name="g"/> can move before they touch anything (≥ 0).</summary>
        private static double Room(ViewGroup g, IEnumerable<Region> parts, Dir dir, IList<ViewGroup> groups, Region usable, IList<Region> obstacles)
        {
            Region area = g.Area ?? usable;
            List<Region> others = obstacles.Concat(groups.Where(o => o != g).SelectMany(o => o.Parts().Select(q => q.R))).ToList();
            double room = double.MaxValue;
            foreach (Region p in parts)
            {
                switch (dir)
                {
                    case Dir.Left: room = Math.Min(room, p.X0 - area.X0); break;
                    case Dir.Right: room = Math.Min(room, area.X1 - p.X1); break;
                    case Dir.Down: room = Math.Min(room, p.Y0 - area.Y0); break;
                    default: room = Math.Min(room, area.Y1 - p.Y1); break;
                }
                foreach (Region o in others)
                {
                    bool acrossOverlap = o.Y0 < p.Y1 && p.Y0 < o.Y1, upDownOverlap = o.X0 < p.X1 && p.X0 < o.X1;
                    if (dir == Dir.Left && acrossOverlap && o.X1 <= p.X0 + 1e-6) room = Math.Min(room, p.X0 - o.X1 - Spacing);
                    if (dir == Dir.Right && acrossOverlap && o.X0 >= p.X1 - 1e-6) room = Math.Min(room, o.X0 - p.X1 - Spacing);
                    if (dir == Dir.Down && upDownOverlap && o.Y1 <= p.Y0 + 1e-6) room = Math.Min(room, p.Y0 - o.Y1 - Spacing);
                    if (dir == Dir.Up && upDownOverlap && o.Y0 >= p.Y1 - 1e-6) room = Math.Min(room, o.Y0 - p.Y1 - Spacing);
                }
            }
            return room == double.MaxValue ? 0 : Math.Max(0, room);
        }

        private static IEnumerable<Region> All(ViewGroup g) => g.Parts().Select(p => p.R);

        private static IEnumerable<Region> Only(ViewGroup g, Func<char, bool> role) => g.Parts().Where(p => role(p.Role)).Select(p => p.R);

        /// <summary>Across: more room between side view and base view, then centred (or against the right).</summary>
        private static void SpreadX(ViewGroup g, IList<ViewGroup> groups, Region usable, IList<Region> obstacles)
        {
            double left = Room(g, All(g), Dir.Left, groups, usable, obstacles), right = Room(g, All(g), Dir.Right, groups, usable, obstacles);
            if (g.HasSide && g.SideLeft)
            {
                // the base and top view move right, the side view stays
                double mainRight = Room(g, Only(g, r => r != 'S'), Dir.Right, groups, usable, obstacles);
                double add = Math.Min(Math.Min((left + right) * 0.5, g.MaxExtraX), mainRight);
                if (add > 0.01) g.ExtraX += add;
                left = Room(g, All(g), Dir.Left, groups, usable, obstacles);
                right = Room(g, All(g), Dir.Right, groups, usable, obstacles);
            }
            g.X0 += g.AlignRight ? right : (right - left) / 2;
        }

        /// <summary>Up and down: placed in the free room; with <paramref name="extra"/> the top view also moves up from the base view.</summary>
        private static void SpreadY(ViewGroup g, IList<ViewGroup> groups, Region usable, IList<Region> obstacles, bool extra)
        {
            bool top = g.HasBelow && g.TopAbove;
            if (extra && top && g.FillY)
            {
                // base and side view all the way down, the top view all the way up
                g.Y1 -= Room(g, All(g), Dir.Down, groups, usable, obstacles);
                double upTop = Room(g, Only(g, r => r == 'T'), Dir.Up, groups, usable, obstacles);
                g.ExtraY += upTop;
                g.Y1 += upTop;
                return;
            }
            double up = Room(g, All(g), Dir.Up, groups, usable, obstacles), down = Room(g, All(g), Dir.Down, groups, usable, obstacles);
            g.Y1 += g.AlignTop ? up : up - (up + down) * AboveShare;   // a third of the room above, two thirds below
            if (extra && top)
            {
                double upTop = Room(g, Only(g, r => r == 'T'), Dir.Up, groups, usable, obstacles);
                double add = Math.Min(g.MaxExtraY, g.AlignTop ? upTop : upTop * 0.5);
                if (add > 0.01) { g.ExtraY += add; g.Y1 += add; }
            }
        }
    }
}
