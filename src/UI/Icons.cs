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
