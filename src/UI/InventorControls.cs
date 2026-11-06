using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoDraw.UI
{
    /// <summary>Draws an icon into the bounds in the given ink and accent colours.</summary>
    internal delegate void IconDrawer(Graphics g, Rectangle bounds, Color ink, Color accent);

    /// <summary>Section bar with a chevron, like "Input Geometry" in Inventor's property panels; click to fold.</summary>
    internal sealed class SectionHeader : Control
    {
        private readonly Theme _theme;
        private bool _open;

        public event Action<bool> Toggled;

        public SectionHeader(Theme theme, string title, bool open)
        {
            _theme = theme;
            Text = title;
            _open = open;
            Height = 20;
            Dock = DockStyle.Top;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public bool Open { get => _open; set { _open = value; Invalidate(); } }

        protected override void OnClick(EventArgs e)
        {
            _open = !_open;
            Invalidate();
            Toggled?.Invoke(_open);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_theme.Header);
            // Autodesk Weave caret, or a drawn triangle if the icon cannot be loaded
            if (!WeaveIcons.Draw(g, new Rectangle(4, 4, 12, 12), _open ? "caret-down-xs" : "caret-right-xs"))
            {
                using (var tri = new SolidBrush(_theme.Dim))
                {
                    PointF[] pts = _open
                        ? new[] { new PointF(7, 8), new PointF(14, 8), new PointF(10.5f, 13) }
                        : new[] { new PointF(8.5f, 5.5f), new PointF(8.5f, 12.5f), new PointF(13.5f, 9) };
                    g.FillPolygon(tri, pts);
                }
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(22, 0, Width - 22, Height), _theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Flat dark edit field with Inventor's focus outline.</summary>
    internal sealed class FieldBox : Panel
    {
        private readonly Theme _theme;
        private readonly TabTextBox _text = new TabTextBox { BorderStyle = BorderStyle.None };
        private bool _focused;

        /// <summary>
        /// A text box that receives Tab itself instead of leaving it to dialog navigation (WinForms' and Inventor's
        /// frame), so the field can save before moving on.
        /// </summary>
        private sealed class TabTextBox : TextBox
        {
            private const int WM_GETDLGCODE = 0x87, DLGC_WANTTAB = 0x2;

            protected override bool IsInputKey(Keys keyData) =>
                (keyData & Keys.KeyCode) == Keys.Tab || base.IsInputKey(keyData);

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == WM_GETDLGCODE) m.Result = new IntPtr(m.Result.ToInt64() | DLGC_WANTTAB);
            }
        }

        public new event EventHandler TextChanged;

        public FieldBox(Theme theme)
        {
            _theme = theme;
            Height = 22;
            BackColor = theme.Field;
            Padding = new Padding(5, 4, 5, 0);
            _text.Dock = DockStyle.Fill;
            _text.BackColor = theme.Field;
            _text.ForeColor = theme.Text;
            Controls.Add(_text);
            _text.TextChanged += (s, e) => { Invalidate(); TextChanged?.Invoke(this, e); };
            _text.Enter += (s, e) => { _focused = true; Invalidate(); };
            _text.Leave += (s, e) => { _focused = false; Invalidate(); };
            _text.KeyDown += OnKeyDown;
        }

        public override string Text { get => _text.Text; set => _text.Text = value; }

        public bool ReadOnly
        {
            get => _text.ReadOnly;
            set { _text.ReadOnly = value; _text.TabStop = !value; _text.ForeColor = value ? _theme.Dim : _theme.Text; }
        }

        public bool Invalid
        {
            set { BackColor = _text.BackColor = value ? _theme.FieldInvalid : _theme.Field; }
        }

        /// <summary>Dimmed text shown while the field is empty, e.g. "(multiple values)".</summary>
        public string Hint
        {
            get => _text.PlaceholderText;
            set => _text.PlaceholderText = value ?? "";
        }

        /// <summary>Raised when Enter is pressed in the field.</summary>
        public event Action Committed;

        /// <summary>Raised when Tab is pressed in the field; true for Shift+Tab (back).</summary>
        public event Action<bool> Tabbed;

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { Committed?.Invoke(); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Tab) { Tabbed?.Invoke(e.Shift); e.Handled = e.SuppressKeyPress = true; }
        }

        /// <summary>Puts the cursor in the field with its text selected, as tabbing into a field does.</summary>
        public void FocusField()
        {
            _text.Focus();
            _text.SelectAll();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // A theme-coloured underline while the field is being edited, the normal text colour once it holds a value,
            // nothing when it is empty.
            bool hasValue = _text.Text.Length > 0;
            if (!_focused && !hasValue) return;
            using (var pen = new Pen(_focused ? _theme.Accent : _theme.Text, 2f))
                e.Graphics.DrawLine(pen, 0, Height - 1.5f, Width, Height - 1.5f);
        }
    }

    /// <summary>
    /// OK / Cancel button drawn in Inventor's property-panel colours: dark body, light outline for Cancel, a blue outline for OK
    /// (dimmed while it is disabled). Windows' own button would fill a disabled button with its grey.
    /// </summary>
    internal sealed class InventorButton : Control
    {
        private readonly Theme _theme;
        private readonly bool _primary;
        private bool _hover, _down;

        public InventorButton(Theme theme, string text, bool primary)
        {
            _theme = theme;
            _primary = primary;
            Text = text;
            Size = new Size(74, 24);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }

        private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_theme.Back);

            Color fill = _theme.Back, border, text = Enabled ? _theme.Text : _theme.Dim;
            if (_primary)
            {
                border = Enabled ? _theme.OkBorder : Mix(_theme.Back, _theme.Accent, 0.22);
                if (Enabled) fill = Mix(_theme.Back, _theme.Accent, _down ? 0.30 : _hover ? 0.22 : 0.12);
            }
            else
            {
                border = Enabled ? _theme.Dim : _theme.Outline;
                if (Enabled) fill = _down ? _theme.Header : _hover ? _theme.Hover : _theme.Back;
            }

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedRectangle(r, 2))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, r, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static GraphicsPath RoundedRectangle(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>A small flat icon button (Edit, Delete...) with a tooltip, hover and disabled states.</summary>
    internal sealed class IconButton : Control
    {
        private readonly Theme _theme;
        private readonly IconDrawer _draw;
        private readonly ToolTip _tip = new ToolTip();
        private bool _hover;

        public IconButton(Theme theme, string tip, IconDrawer draw)
        {
            _theme = theme;
            _draw = draw;
            Size = new Size(30, 26);
            Cursor = Cursors.Hand;
            _tip.SetToolTip(this, tip);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = theme.Back;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_hover && Enabled)
                using (var fill = new SolidBrush(_theme.Hover)) g.FillRectangle(fill, r);
            _draw(g, r, Enabled ? _theme.Text : _theme.Dim, Enabled ? _theme.Accent : _theme.Dim);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
