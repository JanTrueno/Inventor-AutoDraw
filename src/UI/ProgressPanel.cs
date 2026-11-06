using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoDraw.UI
{
    /// <summary>
    /// A strip at the bottom of the AutoDraw window, shown while the drawing is made: what is being done, a bar with
    /// sheets done of total, and elapsed and estimated remaining time. The window's own button is Cancel meanwhile.
    /// </summary>
    internal sealed class ProgressPanel : Panel
    {
        private readonly Theme _theme;
        private readonly Label _step = new Label();
        private readonly Label _detail = new Label();
        private readonly Label _time = new Label();
        private readonly Bar _bar;
        private Stopwatch _watch = Stopwatch.StartNew();
        private readonly Timer _tick = new Timer { Interval = 500 };
        private int _done, _total;

        public ProgressPanel(Theme theme)
        {
            _theme = theme;
            Dock = DockStyle.Bottom;
            Height = 78;
            BackColor = theme.Back;
            Padding = new Padding(12, 8, 12, 8);
            Visible = false;

            _step.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            _step.ForeColor = theme.Text;
            _step.AutoEllipsis = true;
            _detail.ForeColor = theme.Dim;
            _detail.AutoEllipsis = true;
            _time.ForeColor = theme.Dim;
            _time.TextAlign = ContentAlignment.MiddleRight;
            _bar = new Bar(theme);
            Controls.AddRange(new Control[] { _step, _detail, _bar, _time });
            Resize += (s, e) => LayoutParts();
            LayoutParts();
            _tick.Tick += (s, e) => UpdateTime();
        }

        private void LayoutParts()
        {
            int w = Math.Max(100, ClientSize.Width - 24);
            _step.SetBounds(12, 6, w - 300, 22);
            _time.SetBounds(12 + w - 300, 6, 300, 22);
            _detail.SetBounds(12, 28, w, 20);
            _bar.SetBounds(12, 52, w, 14);
        }

        /// <summary>Shows the strip and starts the clock.</summary>
        public void Begin(int total)
        {
            _watch = Stopwatch.StartNew();
            _step.Text = "Starting…";
            _detail.Text = "";
            SetProgress(0, total);
            Visible = true;
            _tick.Start();
        }

        /// <summary>Hides the strip again.</summary>
        public void End()
        {
            _tick.Stop();
            Visible = false;
        }

        /// <summary>What is being done now (the bold line).</summary>
        public void SetStep(string text) { _step.Text = text; Refresh(); }

        /// <summary>The line under it, e.g. the document of the sheet.</summary>
        public void SetDetail(string text) { _detail.Text = text; Refresh(); }

        public void SetProgress(int done, int total)
        {
            _done = done;
            _total = Math.Max(1, total);
            _bar.Value = (double)done / _total;
            UpdateTime();
        }

        private void UpdateTime()
        {
            TimeSpan used = _watch.Elapsed;
            string text = "Elapsed " + Format(used);
            if (_done > 0 && _done < _total)
            {
                var left = TimeSpan.FromTicks(used.Ticks / _done * (_total - _done));
                text += "  ·  about " + Format(left) + " left";
            }
            if (_total > 0) text = _done + " of " + _total + " sheets  ·  " + text;
            _time.Text = text;
        }

        private static string Format(TimeSpan t) => t.TotalMinutes >= 1 ? (int)t.TotalMinutes + " min " + t.Seconds + " s" : t.Seconds + " s";

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _tick.Dispose(); }
            base.Dispose(disposing);
        }

        /// <summary>A flat progress bar in the theme's accent colour.</summary>
        private sealed class Bar : Control
        {
            private readonly Theme _theme;
            private double _value;

            public Bar(Theme theme)
            {
                _theme = theme;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            public double Value { get => _value; set { _value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.None;
                using (var back = new SolidBrush(_theme.Field)) g.FillRectangle(back, ClientRectangle);
                int w = (int)Math.Round((Width - 2) * _value);
                if (w > 0) using (var fill = new SolidBrush(_theme.Accent)) g.FillRectangle(fill, 1, 1, w, Height - 2);
                using (var pen = new Pen(_theme.Outline)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
