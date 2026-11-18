using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoDraw.UI
{
    /// <summary>The ribbon icon, drawn: a drawing sheet with a part outline and a dimension line.</summary>
    internal static class Icons
    {
        private sealed class PictureConverter : AxHost
        {
            private PictureConverter() : base(string.Empty) { }
            public static object ToPictureDisp(Image image) => GetIPictureDispFromPicture(image);
            public static Image FromPictureDisp(object picture) => GetPictureFromIPicture(picture);
        }

        public static Image FromPicture(object picture)
        {
            try { return picture == null ? null : PictureConverter.FromPictureDisp(picture); } catch { return null; }
        }

        /// <summary>
        /// The image drawn into a square bitmap of <paramref name="size"/> pixels, as large as fits and centred. Inventor's
        /// thumbnails are often metafiles, which a PictureBox does not scale reliably.
        /// </summary>
        public static Image Fit(Image src, int size)
        {
            if (src == null || size < 8) return src;
            try
            {
                GraphicsUnit unit = GraphicsUnit.Pixel;
                RectangleF b = src.GetBounds(ref unit);
                if (b.Width <= 0 || b.Height <= 0) return src;
                float s = Math.Min(size / b.Width, size / b.Height);
                float w = b.Width * s, h = b.Height * s;
                var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new RectangleF((size - w) / 2, (size - h) / 2, w, h), b, unit);
                }
                src.Dispose();
                return bmp;
            }
            catch { return src; }
        }

        /// <summary>
        /// A faint, drawn cube (a part in isometric view) for the preview without a thumbnail, in <paramref name="tint"/>
        /// (the theme's text colour, so light on dark and dark on light) at <paramref name="opacity"/>, centred.
        /// </summary>
        public static Image Ghost(int size, Color tint, float opacity, float scale = 0.5f)
        {
            if (size < 8) return null;
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var pen = new Pen(Color.FromArgb((int)(255 * opacity), tint), Math.Max(1.5f, size / 60f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float r = size * scale / 2, cx = size / 2f, cy = size / 2f;
                float dx = r * 0.866f, dy = r * 0.5f;   // cos/sin 30°
                var top = new PointF(cx, cy - r);
                var upperRight = new PointF(cx + dx, cy - dy);
                var lowerRight = new PointF(cx + dx, cy + dy);
                var bottom = new PointF(cx, cy + r);
                var lowerLeft = new PointF(cx - dx, cy + dy);
                var upperLeft = new PointF(cx - dx, cy - dy);
                var centre = new PointF(cx, cy);
                g.DrawPolygon(pen, new[] { top, upperRight, lowerRight, bottom, lowerLeft, upperLeft });
                g.DrawLine(pen, centre, upperLeft);
                g.DrawLine(pen, centre, upperRight);
                g.DrawLine(pen, centre, bottom);
            }
            return bmp;
        }

        public static object Create(int size)
        {
            float scale = 1f;
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, g.DpiX / 96f); } catch { }
            int px = (int)Math.Round(size * scale);
            var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float u = px / 32f;
                var sheet = new RectangleF(3 * u, 4 * u, 26 * u, 24 * u);
                using (var paper = new SolidBrush(Color.FromArgb(250, 250, 250)))
                using (var edge = new Pen(Color.FromArgb(90, 96, 106), Math.Max(1f, 1.4f * u)))
                {
                    g.FillRectangle(paper, sheet);
                    g.DrawRectangle(edge, sheet.X, sheet.Y, sheet.Width, sheet.Height);
                    // title block
                    g.DrawRectangle(edge, 17 * u, 22 * u, 12 * u, 6 * u);
                }
                using (var part = new Pen(Color.FromArgb(6, 120, 200), Math.Max(1f, 2f * u)))
                {
                    // an L-shaped part
                    g.DrawLines(part, new[] { new PointF(7 * u, 11 * u), new PointF(19 * u, 11 * u), new PointF(19 * u, 15 * u),
                        new PointF(11 * u, 15 * u), new PointF(11 * u, 20 * u), new PointF(7 * u, 20 * u), new PointF(7 * u, 11 * u) });
                }
                using (var dim = new Pen(Color.FromArgb(220, 60, 40), Math.Max(1f, 1.3f * u)))
                {
                    g.DrawLine(dim, 7 * u, 7.5f * u, 19 * u, 7.5f * u);
                    g.DrawLine(dim, 7 * u, 6 * u, 7 * u, 9 * u);
                    g.DrawLine(dim, 19 * u, 6 * u, 19 * u, 9 * u);
                    g.DrawLine(dim, 23 * u, 11 * u, 23 * u, 20 * u);
                    g.DrawLine(dim, 21.5f * u, 11 * u, 24.5f * u, 11 * u);
                    g.DrawLine(dim, 21.5f * u, 20 * u, 24.5f * u, 20 * u);
                }
            }
            return PictureConverter.ToPictureDisp(bmp);
        }
    }
}
