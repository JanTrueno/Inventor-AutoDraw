using System;
using System.Collections.Generic;
using System.Linq;
using Inventor;

namespace AutoDraw.Drawing
{
    /// <summary>A rectangle on the sheet, in centimetres.</summary>
    internal struct Region
    {
        public double X0, Y0, X1, Y1;
        public Region(double x0, double y0, double x1, double y1) { X0 = x0; Y0 = y0; X1 = Math.Max(x0, x1); Y1 = Math.Max(y0, y1); }
        public double W => X1 - X0;
        public double H => Y1 - Y0;
        public double CX => (X0 + X1) / 2;
        public double CY => (Y0 + Y1) / 2;
        public bool Overlaps(Region o) => X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1;
    }

    /// <summary>
    /// The free area of a sheet: inside the border, below the header band (part name, material and sheet number at the
    /// top) and outside the title block. Layouts are given as fractions of that area.
    /// </summary>
    internal sealed class SheetFrame
    {
        public readonly double W, H;
        public readonly Region Usable;
        public readonly Region TitleBlock;

        public SheetFrame(Sheet sheet)
        {
            W = sheet.Width;
            H = sheet.Height;
            double m = Math.Max(1.0, 0.025 * W);
            double x0 = m, y0 = m, x1 = W - m, y1 = H - m;
            double borderRight = W - m * 0.4, borderBottom = m * 0.4;
            try
            {
                Box2d b = sheet.Border?.RangeBox;
                if (b != null && b.MaxPoint.X - b.MinPoint.X > W * 0.5)
                {
                    borderRight = b.MaxPoint.X; borderBottom = b.MinPoint.Y;
                    x0 = b.MinPoint.X + 0.8; y0 = b.MinPoint.Y + 0.8; x1 = b.MaxPoint.X - 0.8; y1 = b.MaxPoint.Y - 0.8;
                }
            }
            catch { }
            // the whole sheet inside the border; the texts in the corners are obstacles, not a band across the top
            Usable = new Region(x0, y0, x1, y1);
            double sheetTop = y1 + 0.8;

            // The company title block's range box also holds texts far from the block itself (it covers nearly the
            // whole sheet), so it is only trusted when it looks like a corner block; otherwise the block is taken to be
            // the bottom-right 38 % × 21 % of the sheet, as on the J. Blom Opmeer A2 sheets.
            double right = borderRight, bottom = borderBottom;
            double top = TitleBlockTop(sheet, borderBottom) ?? H * 0.195;
            TitleBlock = new Region(W * 0.62, bottom, right, top);
            Obstacles = new List<Region>
            {
                new Region(W * 0.62, bottom, right, top + 0.3),
                new Region(W * 0.62, bottom, W * 0.655, H * 0.27),     // the Opmerking / Datum / Check / Rev column left of it
                new Region(W * 0.42, bottom, W * 0.62, H * 0.065)      // "Ordernummer: …" at the bottom
            };
            Obstacles.AddRange(CornerTexts(sheetTop, borderRight));
            try
            {
                Box2d tb = sheet.TitleBlock?.RangeBox;
                if (tb != null)
                {
                    double w = tb.MaxPoint.X - tb.MinPoint.X, h = tb.MaxPoint.Y - tb.MinPoint.Y;
                    if (w > 1 && h > 1 && w < 0.6 * W && h < 0.35 * H)
                    {
                        TitleBlock = new Region(tb.MinPoint.X, tb.MinPoint.Y, tb.MaxPoint.X, tb.MaxPoint.Y);
                        Obstacles = new List<Region> { TitleBlock };
                        Obstacles.AddRange(CornerTexts(sheetTop, borderRight));
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// The texts in the top corners of the company border: "4x part / material / bewerking" top left (to a quarter of
        /// the width and 22 % down), the sheet number top right and the vertical "Datum gecontroleerd" along the right edge.
        /// </summary>
        private List<Region> CornerTexts(double sheetTop, double right) => new List<Region>
        {
            new Region(0, H * 0.78, W * 0.25, sheetTop),
            new Region(W * 0.92, H * 0.88, right, sheetTop),
            new Region(W * 0.955, H * 0.74, right, sheetTop)
        };

        /// <summary>
        /// The top of the title block at its right-hand edge (where the parts list stands on it), from the lines of the
        /// title block definition: the block sits on the bottom border line, so its height is added to the border bottom.
        /// Null when the lines cannot be read.
        /// </summary>
        private double? TitleBlockTop(Sheet sheet, double borderBottom)
        {
            try
            {
                TitleBlock tb = sheet.TitleBlock;
                if (tb == null) return null;
                var lines = new List<(double X1, double Y1, double X2, double Y2)>();
                foreach (SketchLine l in tb.Definition.Sketch.SketchLines)
                {
                    Point2d a = l.StartSketchPoint.Geometry, b = l.EndSketchPoint.Geometry;
                    lines.Add((a.X, a.Y, b.X, b.Y));
                }
                if (lines.Count < 2) return null;
                double maxX = lines.Max(l => Math.Max(l.X1, l.X2)), minY = lines.Min(l => Math.Min(l.Y1, l.Y2));
                // the highest horizontal line that reaches the right-hand edge is the top of the block there
                double topRight = double.MinValue;
                foreach (var l in lines)
                    if (Math.Abs(l.Y1 - l.Y2) < 1e-4 && Math.Abs(Math.Max(l.X1, l.X2) - maxX) < 0.05 && l.Y1 > topRight) topRight = l.Y1;
                if (topRight == double.MinValue) return null;
                double top = borderBottom + (topRight - minY);
                Core.Log.Write(string.Format("Title block top at the right edge: {0:0.00} cm (block height {1:0.00} cm)", top, topRight - minY));
                return top > 0.05 * H && top < 0.4 * H ? top : (double?)null;
            }
            catch (Exception ex)
            {
                Core.Log.Write("Reading the title block lines failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>Areas no view may overlap: the title block and the texts next to it.</summary>
        public List<Region> Obstacles;

        /// <summary>
        /// A region from fractions of the usable area; raised above the title block when it would overlap it.
        /// </summary>
        public Region Part(double fx0, double fy0, double fx1, double fy1)
        {
            var r = new Region(Usable.X0 + fx0 * Usable.W, Usable.Y0 + fy0 * Usable.H, Usable.X0 + fx1 * Usable.W, Usable.Y0 + fy1 * Usable.H);
            if (r.Overlaps(TitleBlock))
            {
                // the title block sits in a corner: keep the region beside it when that leaves most of it, else above it
                double besideW = TitleBlock.X0 - 0.5 - r.X0;
                if (besideW > r.W * 0.7) r = new Region(r.X0, r.Y0, TitleBlock.X0 - 0.5, r.Y1);
                else r = new Region(r.X0, Math.Min(r.Y1 - 1, TitleBlock.Y1 + 0.5), r.X1, r.Y1);
            }
            return r;
        }
    }
}
