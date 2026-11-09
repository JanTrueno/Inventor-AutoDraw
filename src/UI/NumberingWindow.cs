using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AutoDraw.Core;
using Inventor;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;
using Path = System.IO.Path;
using Application = System.Windows.Forms.Application;

namespace AutoDraw.UI
{
    /// <summary>
    /// Bulk part numbers: a preview of the new number of every document in the tree (pattern, order digits, type
    /// letters and target iProperties set at the top), written to the files with Apply.
    /// </summary>
    internal sealed class NumberingWindow : Form
    {
        private static readonly string[] Standard =
        {
            "Part Number", "Stock Number", "Description", "Title", "Subject", "Project", "Revision Number", "Comments",
            "Keywords", "Authority", "Cost Center", "Vendor", "Designer", "Engineer"
        };

        private readonly Inventor.Application _app;
        private readonly Theme _theme;
        private readonly AppSettings _settings;
        private readonly DocNode _root;
        private readonly string _order;
        private readonly List<DocNode> _nodes = new List<DocNode>();
        private readonly HashSet<DocNode> _excluded = new HashSet<DocNode>();
        private readonly HashSet<DocNode> _readOnly = new HashSet<DocNode>();
        private readonly Dictionary<DocNode, string> _current = new Dictionary<DocNode, string>();
        private Dictionary<DocNode, NumberRow> _rows = new Dictionary<DocNode, NumberRow>();
        /// <summary>Unticked documents that get their file name back as number.</summary>
        private readonly Dictionary<DocNode, string> _resets = new Dictionary<DocNode, string>();
        private bool _ok;

        private readonly FieldBox _pattern, _digits, _props, _levels;
        private readonly FieldBox[] _letters = new FieldBox[DocKinds.All.Length];
        private readonly CheckBox _keep = new CheckBox(), _reset = new CheckBox();
        private readonly Label _example = new Label();
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _status = new Label();
        private readonly InventorButton _apply, _close, _propsMenu, _refresh;

        /// <summary>True when numbers were written; the main window then reads the iProperties again.</summary>
        public bool Applied { get; private set; }

        public NumberingWindow(Inventor.Application app, Theme theme, AppSettings settings, DocNode root, string order)
        {
            _app = app;
            _theme = theme;
            _settings = settings;
            _root = root;
            _order = order ?? "";
            Text = "AutoDraw – Part numbers";
            Font = new Font("Segoe UI", 9f);
            BackColor = theme.Back;
            ForeColor = theme.Text;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 460);
            Size = new Size(Math.Max(760, settings.NumberWindowW), Math.Max(460, settings.NumberWindowH));
            ShowInTaskbar = false;
            KeyPreview = true;

            _pattern = new FieldBox(theme) { Text = settings.NumberPattern };
            _digits = new FieldBox(theme) { Text = settings.NumberOrderDigits.ToString() };
            _levels = new FieldBox(theme) { Text = PartNumbering.Levels(settings).ToString() };
            _props = new FieldBox(theme) { Text = string.Join("; ", settings.NumberProperties), Hint = "e.g. Part Number; artikelnummer" };
            _propsMenu = new InventorButton(theme, "…", false) { Size = new Size(30, 22) };
            _apply = new InventorButton(theme, "Apply", true) { Size = new Size(110, 28), Dock = DockStyle.Right };
            _close = new InventorButton(theme, "Close", false) { Size = new Size(90, 28), Dock = DockStyle.Right };
            _refresh = new InventorButton(theme, "Refresh", false) { Size = new Size(90, 28), Dock = DockStyle.Left };

            Controls.Add(BuildGrid());
            Controls.Add(BuildBottom());
            Controls.Add(BuildTop());

            Collect(root);
            Refill();
        }

        // ----- layout -----------------------------------------------------------------------------------------------------

        private Label Caption(string text, int x, int y, int w) => new Label
        {
            Text = text, Location = new Point(x, y), Size = new Size(w, 22), ForeColor = _theme.Dim, TextAlign = ContentAlignment.MiddleLeft
        };

        private Control BuildTop()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 196, BackColor = _theme.Back };
            int lx = 12, fx = 140;

            top.Controls.Add(Caption("Pattern", lx, 12, 120));
            _pattern.Location = new Point(fx, 12); _pattern.Width = 260;
            top.Controls.Add(_pattern);
            top.Controls.Add(Caption("{order} {asm} {seq} {type} {name}", fx + 272, 12, 220));
            top.Controls.Add(Caption("Assembly levels", fx + 500, 12, 100));
            _levels.Location = new Point(fx + 604, 12); _levels.Width = 40;
            top.Controls.Add(_levels);
            top.Controls.Add(Caption("1–6", fx + 650, 12, 40));

            top.Controls.Add(Caption("Order number digits", lx, 42, 128));
            _digits.Location = new Point(fx, 42); _digits.Width = 50;
            top.Controls.Add(_digits);
            top.Controls.Add(Caption("last digits of " + Naming.Digits(_order) + " (0 = all)", fx + 62, 42, 220));
            _example.Location = new Point(fx + 290, 42); _example.Size = new Size(320, 22); _example.ForeColor = _theme.Text;
            _example.TextAlign = ContentAlignment.MiddleLeft;
            top.Controls.Add(_example);

            top.Controls.Add(Caption("Write to iProperties", lx, 72, 128));
            _props.Location = new Point(fx, 72); _props.Width = 260;
            top.Controls.Add(_props);
            _propsMenu.Location = new Point(fx + 264, 72);
            top.Controls.Add(_propsMenu);
            top.Controls.Add(Caption("built-in or custom, separated by ;", fx + 302, 72, 260));

            top.Controls.Add(Caption("Type letters", lx, 102, 128));
            int x = fx;
            for (int i = 0; i < DocKinds.All.Length; i++)
            {
                DocKind k = DocKinds.All[i];
                string label = k == DocKind.SheetMetalFolded ? "Folded" : k == DocKind.SheetMetalFlat ? "Flat" : k == DocKind.Tube ? "Tube" : DocKinds.Label(k);
                Label l = Caption(label, x, 102, TextRenderer.MeasureText(label, Font).Width + 4);
                top.Controls.Add(l);
                x += l.Width + 2;
                _letters[i] = new FieldBox(_theme) { Location = new Point(x, 102), Width = 34, Text = PartNumbering.Letter(_settings, k) };
                top.Controls.Add(_letters[i]);
                x += 34 + 14;
                _letters[i].TextChanged += (s, e) => { SaveSettings(); Refill(); };
            }

            _keep.Text = "Keep numbers that already fit the pattern (new documents get the free numbers)";
            _keep.Checked = _settings.NumberKeepExisting;
            _keep.Location = new Point(fx, 132); _keep.Size = new Size(560, 24);
            _keep.ForeColor = _theme.Text; _keep.BackColor = _theme.Back; _keep.FlatStyle = FlatStyle.Flat;
            _keep.FlatAppearance.BorderColor = _theme.Dim;
            top.Controls.Add(_keep);

            _reset.Text = "Unticked rows that carry a number of this order get their file name back as number";
            _reset.Checked = _settings.NumberResetSkipped;
            _reset.Location = new Point(fx, 158); _reset.Size = new Size(620, 24);
            _reset.ForeColor = _theme.Text; _reset.BackColor = _theme.Back; _reset.FlatStyle = FlatStyle.Flat;
            _reset.FlatAppearance.BorderColor = _theme.Dim;
            top.Controls.Add(_reset);
            _reset.CheckedChanged += (s, e) => { SaveSettings(); Refill(); };

            _pattern.TextChanged += (s, e) => { SaveSettings(); Refill(); };
            _digits.TextChanged += (s, e) => { SaveSettings(); Refill(); };
            _levels.TextChanged += (s, e) => { SaveSettings(); Refill(); };
            _props.TextChanged += (s, e) => { SaveSettings(); _current.Clear(); Refill(); };
            _keep.CheckedChanged += (s, e) => { SaveSettings(); Refill(); };
            _propsMenu.Click += (s, e) => PropsMenu();
            return top;
        }

        private Control BuildBottom()
        {
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = _theme.Header, Padding = new Padding(10, 8, 10, 8) };
            _status.Dock = DockStyle.Fill; _status.TextAlign = ContentAlignment.MiddleLeft; _status.AutoEllipsis = true; _status.ForeColor = _theme.Text;
            bottom.Controls.Add(_status);
            bottom.Controls.Add(_apply);
            bottom.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8, BackColor = _theme.Header });
            bottom.Controls.Add(_close);
            bottom.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 12, BackColor = _theme.Header });
            bottom.Controls.Add(_refresh);
            _refresh.Click += (s, e) => { _current.Clear(); Refill(); };
            _apply.Click += (s, e) => Apply();
            _close.Click += (s, e) => Close();
            return bottom;
        }

        private Control BuildGrid()
        {
            DataGridView g = _grid;
            g.Dock = DockStyle.Fill;
            g.AllowUserToAddRows = g.AllowUserToDeleteRows = g.AllowUserToResizeRows = g.AllowUserToOrderColumns = false;
            g.RowHeadersVisible = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false;
            g.BorderStyle = BorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            g.EnableHeadersVisualStyles = false;
            g.BackgroundColor = _theme.Field;
            g.GridColor = _theme.Outline;
            g.RowTemplate.Height = 22;
            g.ColumnHeadersHeight = 26;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersDefaultCellStyle.BackColor = _theme.Header;
            g.ColumnHeadersDefaultCellStyle.ForeColor = _theme.Text;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = _theme.Header;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = _theme.Text;
            g.DefaultCellStyle.BackColor = _theme.Field;
            g.DefaultCellStyle.ForeColor = _theme.Text;
            g.DefaultCellStyle.SelectionBackColor = _theme.AccentFill;
            g.DefaultCellStyle.SelectionForeColor = _theme.Text;
            g.Font = new Font("Segoe UI", 9f);

            // read-only: the tick is toggled here (not by the grid's own editing), so the rows can be rebuilt safely
            g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "use", HeaderText = "", Width = 30, FlatStyle = FlatStyle.Flat, ReadOnly = true });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Name", Width = 300, ReadOnly = true });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "Type", Width = 130, ReadOnly = true });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "current", HeaderText = "Current", Width = 160, ReadOnly = true });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "new", HeaderText = "New", Width = 160, ReadOnly = true });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "note", HeaderText = "", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 80 });
            foreach (DataGridViewColumn c in g.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

            g.CellMouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex < 0 || g.Columns[e.ColumnIndex].Name != "use") return;
                if (g.Rows[e.RowIndex].Tag is DocNode n) Toggle(n);
            };
            g.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Space || !(g.CurrentRow?.Tag is DocNode n)) return;
                Toggle(n);
                e.Handled = e.SuppressKeyPress = true;
            };
            g.DataError += (s, e) => e.ThrowException = false;
            return g;
        }

        // ----- content ----------------------------------------------------------------------------------------------------

        /// <summary>Ticks or unticks a row; the numbers are worked out again after the click has finished.</summary>
        private void Toggle(DocNode n)
        {
            if (!_excluded.Remove(n)) _excluded.Add(n);
            BeginInvoke((Action)Refill);
        }

        private void Collect(DocNode n)
        {
            if (n.IsGroup || n.IsContentCenter) return;
            if (n.IsVirtual) { foreach (DocNode c in n.Children) Collect(c); return; }
            _nodes.Add(n);
            if (!n.Checked && !n.IsMain) _excluded.Add(n);   // unticked in the main tree: unticked here too
            try
            {
                string file = n.Doc?.FullFileName;
                if (!string.IsNullOrEmpty(file) && System.IO.File.Exists(file) &&
                    (System.IO.File.GetAttributes(file) & System.IO.FileAttributes.ReadOnly) != 0)
                {
                    _readOnly.Add(n);
                    _excluded.Add(n);
                }
            }
            catch { }
            foreach (DocNode c in n.Children) Collect(c);
        }

        private string Current(DocNode n)
        {
            if (_current.TryGetValue(n, out string v)) return v;
            string prop = _settings.NumberProperties.FirstOrDefault() ?? "";
            v = n.Edited.TryGetValue(prop, out string e) ? e : Props.Get(n.Doc, prop);
            return _current[n] = v ?? "";
        }

        private void SaveSettings()
        {
            _settings.NumberPattern = _pattern.Text.Trim();
            _settings.NumberOrderDigits = int.TryParse(_digits.Text.Trim(), out int d) && d >= 0 ? d : _settings.NumberOrderDigits;
            _settings.NumberProperties = _props.Text.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _settings.NumberKeepExisting = _keep.Checked;
            if (int.TryParse(_levels.Text.Trim(), out int lv) && lv >= 1 && lv <= 6) _settings.NumberLevels = lv;
            _settings.NumberResetSkipped = _reset.Checked;
            _settings.TypeLetters = new Dictionary<string, string>();
            for (int i = 0; i < DocKinds.All.Length; i++)
                if (_letters[i] != null) _settings.TypeLetters[DocKinds.All[i].ToString()] = _letters[i].Text.Trim();
            _settings.Save();
        }

        /// <summary>Works the numbers out again and fills the grid.</summary>
        private void Refill()
        {
            if (_letters.Any(l => l == null)) return;   // still building
            string problem = Naming.OrderProblem(_order, _settings) ?? PartNumbering.PatternProblem(_settings.NumberPattern)
                ?? (_settings.NumberProperties.Count == 0 ? "Name at least one iProperty to write the number to." : null);
            _pattern.Invalid = PartNumbering.PatternProblem(_settings.NumberPattern) != null;
            _props.Invalid = _settings.NumberProperties.Count == 0;

            var warnings = new List<string>();
            _rows = problem != null ? new Dictionary<DocNode, NumberRow>()
                : PartNumbering.Plan(_root, _order, _settings, Current, n => !_excluded.Contains(n), warnings).ToDictionary(r => r.Node);
            _resets.Clear();
            if (problem == null && _settings.NumberResetSkipped)
                foreach (DocNode n in _excluded.Where(x => !_readOnly.Contains(x) && x.Doc != null))
                    if (PartNumbering.IsOurs(Current(n), _order, _settings)) _resets[n] = n.Name;
            bool leftovers = problem == null && !_settings.NumberResetSkipped &&
                _excluded.Any(n => PartNumbering.IsOurs(Current(n), _order, _settings));
            _example.Text = problem == null
                ? "e.g. " + PartNumbering.Format(_settings.NumberPattern, PartNumbering.OrderPart(_order, _settings.NumberOrderDigits),
                    PartNumbering.Levels(_settings) >= 2 ? 11 * (int)Math.Pow(10, PartNumbering.Levels(_settings) - 2) : 1, 2,
                    PartNumbering.Letter(_settings, DocKind.Lathe), "shaft", PartNumbering.Levels(_settings))
                : "";

            int scroll = _grid.FirstDisplayedScrollingRowIndex;
            DocNode selected = _grid.CurrentRow?.Tag as DocNode;
            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (DocNode n in _nodes)
            {
                int i = _grid.Rows.Add();
                DataGridViewRow r = _grid.Rows[i];
                r.Tag = n;
                _rows.TryGetValue(n, out NumberRow row);
                r.Cells["use"].Value = !_excluded.Contains(n);
                r.Cells["name"].Value = n.Name;
                r.Cells["name"].Style.Padding = new Padding(n.Level * 16, 0, 0, 0);
                if (n.IsMain) r.Cells["name"].Style.Font = new Font(_grid.Font, FontStyle.Bold);
                r.Cells["kind"].Value = DocKinds.Label(n.Kind);
                r.Cells["current"].Value = Current(n);
                bool reset = _resets.TryGetValue(n, out string back);
                r.Cells["new"].Value = row?.Value ?? (reset ? back : "");
                string note = _readOnly.Contains(n) ? "read-only file" : reset ? "skipped, number removed"
                    : row == null ? (_excluded.Contains(n) ? "skipped" : "")
                    : !row.Changed ? "unchanged" : row.Kept ? "kept, type changed" : "";
                r.Cells["note"].Value = note;
                r.Cells["note"].Style.ForeColor = _theme.Dim;
                r.Cells["new"].Style.ForeColor = (row != null && row.Changed) || reset ? _theme.Link : _theme.Dim;
                if (row == null) r.DefaultCellStyle.ForeColor = _theme.Dim;
            }
            _grid.ResumeLayout();
            if (scroll >= 0 && scroll < _grid.Rows.Count) { try { _grid.FirstDisplayedScrollingRowIndex = scroll; } catch { } }
            DataGridViewRow sel = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => x.Tag == selected);
            if (sel != null) { try { _grid.CurrentCell = sel.Cells["name"]; } catch { } }

            int changes = _rows.Values.Count(r => r.Changed) + _resets.Count;
            _ok = problem == null && changes > 0;
            _apply.Enabled = _ok;
            if (problem != null) SetStatus(problem, true);
            else if (leftovers) SetStatus(changes + " of " + _rows.Count + " numbers change.  Unticked rows still carry a number of this order: " +
                "tick \"file name back\" to remove them" + (_settings.NumberKeepExisting ? " and untick \"Keep numbers\" to close the gaps." : "."), true);
            else SetStatus(changes + " of " + _rows.Count + " numbers change." +
                (warnings.Count > 0 ? "  " + string.Join("  ", warnings.Take(2)) + (warnings.Count > 2 ? " …" : "") : ""), warnings.Count > 0);
        }

        private void SetStatus(string text, bool bad)
        {
            _status.Text = text;
            _status.ForeColor = bad ? Color.FromArgb(230, 90, 80) : _theme.Text;
        }

        private void PropsMenu()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DocNode n in _nodes.Where(n => n.Doc != null)) foreach (string p in Props.CustomNames(n.Doc)) names.Add(p);
            var menu = new ThemedMenu(_theme) { ShowCheckMargin = true };
            void Item(string name)
            {
                var item = new ToolStripMenuItem(name) { Checked = _settings.NumberProperties.Contains(name, StringComparer.OrdinalIgnoreCase) };
                item.Click += (s, e) =>
                {
                    var list = _settings.NumberProperties.ToList();
                    if (list.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)) == 0) list.Add(name);
                    _props.Text = string.Join("; ", list);
                };
                menu.Items.Add(item);
            }
            foreach (string s in Standard) Item(s);
            names.ExceptWith(Standard);
            if (names.Count > 0) menu.Items.Add(new ToolStripSeparator());
            foreach (string s in names) Item(s);
            menu.Show(_propsMenu, new Point(0, _propsMenu.Height));
        }

        // ----- writing ----------------------------------------------------------------------------------------------------

        private void Apply()
        {
            if (!_ok) return;
            List<NumberRow> todo = _rows.Values.Where(r => r.Changed && r.Node.Doc != null)
                .Concat(_resets.Select(kv => new NumberRow { Node = kv.Key, Value = kv.Value, Current = Current(kv.Key) })).ToList();
            Cursor = Cursors.WaitCursor;
            _apply.Enabled = _close.Enabled = false;
            var changed = new HashSet<Document>();
            var failed = new List<string>();
            try
            {
                int i = 0;
                foreach (NumberRow r in todo)
                {
                    if (++i % 10 == 0) { SetStatus("Writing " + i + " of " + todo.Count + "…", false); Application.DoEvents(); }
                    foreach (string prop in _settings.NumberProperties)
                    {
                        try
                        {
                            if (Props.Set(r.Node.Doc, prop, r.Value)) changed.Add(r.Node.Doc);
                            r.Node.Edited.Remove(prop);   // an older value typed in the main tree would overwrite the number
                        }
                        catch (Exception ex)
                        {
                            Log.Write("Setting " + prop + " of " + r.Node.Name + " failed: " + ex.Message);
                            failed.Add(r.Node.Name);
                        }
                    }
                }

                SetStatus("Saving " + changed.Count + " files…", false);
                Application.DoEvents();
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
                            failed.Add(Path.GetFileName(doc.FullFileName) + " (not saved)");
                        }
                    }
                }
                finally { _app.SilentOperation = silent; }
            }
            finally
            {
                Cursor = Cursors.Default;
                _close.Enabled = true;
            }

            Applied = true;
            _current.Clear();
            Refill();
            failed = failed.Distinct().ToList();
            if (failed.Count == 0) SetStatus("Numbered " + todo.Count + " documents and saved " + changed.Count + " files.", false);
            else SetStatus("Numbered " + todo.Count + " documents; " + failed.Count + " problem(s): " + string.Join(", ", failed.Take(3)) +
                (failed.Count > 3 ? " …" : "") + " (checked in or read-only?)", true);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_close.Enabled) { e.Cancel = true; return; }
            if (WindowState == FormWindowState.Normal) { _settings.NumberWindowW = Width; _settings.NumberWindowH = Height; _settings.Save(); }
            base.OnFormClosing(e);
        }
    }
}
