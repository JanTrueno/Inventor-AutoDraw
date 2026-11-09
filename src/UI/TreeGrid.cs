using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using AutoDraw.Core;

namespace AutoDraw.UI
{
    /// <summary>
    /// The assembly tree as a grid: the first column holds the caret, tick box and name (indented by level), the other
    /// columns the kind (a drop-down), Aantal, the sheet count of assemblies, the sheet number, material, the chosen
    /// iProperty columns and the status. Edited values are kept on the nodes and written when the drawing is made.
    /// </summary>
    internal sealed class TreeGrid : DataGridView
    {
        private const int Step = 16;
        public const string ColName = "name", ColKind = "kind", ColQty = "qty", ColSheets = "sheets", ColSheetNo = "sheetno",
            ColMaterial = "material", ColStatus = "status", ExtraPrefix = "prop:";

        private readonly Theme _theme;
        private readonly Dictionary<(DocNode, string), string> _propCache = new Dictionary<(DocNode, string), string>();
        private DocNode _root;
        private List<string> _extra = new List<string>();
        private bool _filling;

        /// <summary>A tick, a kind, a quantity or a sheet count changed: the sheet numbers must be redone.</summary>
        public event Action PlanChanged;
        /// <summary>The selected row changed.</summary>
        public event Action<DocNode> NodeSelected;

        public Func<int> LargeLimit = () => 15;

        public TreeGrid(Theme theme)
        {
            _theme = theme;
            DoubleBuffered = true;
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false;
            AllowUserToOrderColumns = false;
            RowHeadersVisible = false;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            MultiSelect = false;
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            BorderStyle = BorderStyle.None;
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            EnableHeadersVisualStyles = false;
            BackgroundColor = theme.Field;
            GridColor = theme.Outline;
            RowTemplate.Height = 22;
            ColumnHeadersHeight = 26;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            ColumnHeadersDefaultCellStyle.BackColor = theme.Header;
            ColumnHeadersDefaultCellStyle.ForeColor = theme.Text;
            ColumnHeadersDefaultCellStyle.SelectionBackColor = theme.Header;
            ColumnHeadersDefaultCellStyle.SelectionForeColor = theme.Text;
            DefaultCellStyle.BackColor = theme.Field;
            DefaultCellStyle.ForeColor = theme.Text;
            DefaultCellStyle.SelectionBackColor = theme.AccentFill;
            DefaultCellStyle.SelectionForeColor = theme.Text;
            Font = new Font("Segoe UI", 9f);
            BuildColumns(new List<string>());
        }

        public void BuildColumns(List<string> extra)
        {
            _extra = extra.ToList();
            Columns.Clear();
            Columns.Add(new DataGridViewTextBoxColumn { Name = ColName, HeaderText = "Name", Width = 320, ReadOnly = true, Frozen = true });
            var kind = new DataGridViewComboBoxColumn
            {
                Name = ColKind, HeaderText = "Type", Width = 140, FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing
            };
            foreach (DocKind k in DocKinds.PartKinds) kind.Items.Add(DocKinds.Label(k));
            kind.DefaultCellStyle.BackColor = _theme.Field;
            Columns.Add(kind);
            Columns.Add(new DataGridViewTextBoxColumn { Name = ColQty, HeaderText = "Aantal", Width = 60 });
            Columns.Add(new DataGridViewTextBoxColumn { Name = ColSheets, HeaderText = "Sheets", Width = 55 });
            Columns.Add(new DataGridViewTextBoxColumn { Name = ColSheetNo, HeaderText = "Sheet no.", Width = 70, ReadOnly = true });
            Columns.Add(new DataGridViewTextBoxColumn { Name = ColMaterial, HeaderText = "Material", Width = 130, ReadOnly = true });
            foreach (string p in _extra)
                Columns.Add(new DataGridViewTextBoxColumn { Name = ExtraPrefix + p, HeaderText = p, Width = 110 });
            Columns.Add(new DataGridViewTextBoxColumn { Name = ColStatus, HeaderText = "Status", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 120 });
            foreach (DataGridViewColumn c in Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (_root != null) Rebuild();
        }

        public void SetRoot(DocNode root)
        {
            _root = root;
            _propCache.Clear();
            Rebuild();
        }

        public DocNode SelectedNode => CurrentRow?.Tag as DocNode;

        /// <summary>Fills the rows from the tree (children of closed rows are left out), keeping the selection.</summary>
        public void Rebuild()
        {
            DocNode selected = SelectedNode;
            int scroll = FirstDisplayedScrollingRowIndex;
            _filling = true;
            try
            {
                Rows.Clear();
                if (_root == null) return;
                var rows = new List<DataGridViewRow>();
                void Add(DocNode n)
                {
                    rows.Add(MakeRow(n));
                    if (n.Expanded) foreach (DocNode c in n.Children) Add(c);
                }
                Add(_root);
                Rows.AddRange(rows.ToArray());
                foreach (DataGridViewRow r in Rows) Fill(r);
            }
            finally { _filling = false; }

            if (scroll >= 0 && scroll < Rows.Count) { try { FirstDisplayedScrollingRowIndex = scroll; } catch { } }
            DataGridViewRow sel = Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag == selected) ?? (Rows.Count > 0 ? Rows[0] : null);
            if (sel != null) { try { CurrentCell = sel.Cells[ColName]; } catch { } }
        }

        private DataGridViewRow MakeRow(DocNode n)
        {
            var row = new DataGridViewRow { Tag = n };
            row.CreateCells(this);
            int kindIndex = Columns[ColKind].Index;
            if (n.IsAssembly || n.IsGroup)
            {
                row.Cells[kindIndex] = new DataGridViewTextBoxCell();
                row.Cells[kindIndex].ReadOnly = true;
            }
            if (n.IsGroup)
                foreach (DataGridViewCell c in row.Cells) c.ReadOnly = true;
            if (!n.IsAssembly) row.Cells[Columns[ColSheets].Index].ReadOnly = true;
            if (n.IsGroup) row.DefaultCellStyle.ForeColor = _theme.Dim;
            return row;
        }

        /// <summary>Writes the node's values into the row's cells.</summary>
        private void Fill(DataGridViewRow r)
        {
            if (!(r.Tag is DocNode n)) return;
            bool was = _filling;
            _filling = true;
            try
            {
                r.Cells[ColName].Value = n.Name;
                if (n.IsGroup) return;
                r.Cells[ColKind].Value = DocKinds.Label(n.Kind);
                r.Cells[ColQty].Value = n.QuantityValue;
                r.Cells[ColSheets].Value = n.IsAssembly ? n.SheetCount(LargeLimit()).ToString() : "";
                r.Cells[ColSheetNo].Value = n.SheetNo > 0 ? Naming.SheetText(n.SheetNo) : "";
                r.Cells[ColMaterial].Value = n.Material;
                foreach (string p in _extra) r.Cells[ExtraPrefix + p].Value = PropValue(n, p);
                r.Cells[ColStatus].Value = n.Status;
                r.Cells[ColStatus].Style.ForeColor = n.StatusBad ? Color.FromArgb(230, 90, 80) : _theme.Dim;
            }
            finally { _filling = was; }
        }

        public void RefreshValues()
        {
            foreach (DataGridViewRow r in Rows) Fill(r);
            Invalidate();
        }

        /// <summary>Reads the iProperty columns from the files again (after they were written elsewhere).</summary>
        public void ForgetValues()
        {
            _propCache.Clear();
            RefreshValues();
        }

        private string PropValue(DocNode n, string name)
        {
            if (n.Edited.TryGetValue(name, out string v)) return v;
            if (!_propCache.TryGetValue((n, name), out v)) _propCache[(n, name)] = v = Props.Get(n.Doc, name);
            return v;
        }

        // ----- editing --------------------------------------------------------------------------------------------------

        protected override void OnCurrentCellDirtyStateChanged(EventArgs e)
        {
            base.OnCurrentCellDirtyStateChanged(e);
            if (IsCurrentCellDirty && CurrentCell is DataGridViewComboBoxCell) CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        protected override void OnDataError(bool displayErrorDialogIfNoHandler, DataGridViewDataErrorEventArgs e) => e.ThrowException = false;

        protected override void OnCellClick(DataGridViewCellEventArgs e)
        {
            base.OnCellClick(e);
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            DataGridViewCell cell = Rows[e.RowIndex].Cells[e.ColumnIndex];
            if (cell.ReadOnly || e.ColumnIndex == Columns[ColName].Index) return;
            BeginEdit(true);
            if (EditingControl is ComboBox combo) combo.DroppedDown = true;
        }

        protected override void OnCellValueChanged(DataGridViewCellEventArgs e)
        {
            base.OnCellValueChanged(e);
            if (_filling || e.RowIndex < 0) return;
            DataGridViewRow r = Rows[e.RowIndex];
            if (!(r.Tag is DocNode n)) return;
            string col = Columns[e.ColumnIndex].Name;
            string text = Convert.ToString(r.Cells[e.ColumnIndex].Value)?.Trim() ?? "";

            if (col == ColKind)
            {
                DocKind? k = DocKinds.FromLabel(text);
                if (k == null || n.IsAssembly) return;
                n.KindOverride = k == n.Detected ? (DocKind?)null : k;
                BeginInvoke((Action)(() => { if (n.Parent != null) StructureReader.SortChildren(n.Parent); Rebuild(); PlanChanged?.Invoke(); }));
            }
            else if (col == ColQty)
            {
                n.QuantityText = text.Length == 0 || text == n.Quantity.ToString() ? null : text;
                BeginInvoke((Action)(() => Fill(r)));
            }
            else if (col == ColSheets)
            {
                if (int.TryParse(text, out int s) && s >= 1 && s <= 20) n.SheetsOverride = s;
                else if (text.Length == 0) n.SheetsOverride = null;
                BeginInvoke((Action)(() => PlanChanged?.Invoke()));
            }
            else if (col.StartsWith(ExtraPrefix))
            {
                string prop = col.Substring(ExtraPrefix.Length);
                string current;
                _propCache.TryGetValue((n, prop), out current);
                if (text == (current ?? "")) n.Edited.Remove(prop); else n.Edited[prop] = text;
            }
        }

        // ----- the tree column: caret, tick box, name ---------------------------------------------------------------------

        private Rectangle CaretRect(DocNode n, Rectangle cell) => new Rectangle(cell.X + 4 + n.Level * Step, cell.Y + (cell.Height - 12) / 2, 12, 12);
        private Rectangle BoxRect(DocNode n, Rectangle cell) => new Rectangle(cell.X + 20 + n.Level * Step, cell.Y + (cell.Height - 13) / 2, 13, 13);

        protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
        {
            base.OnCellPainting(e);
            if (e.RowIndex < 0 || e.ColumnIndex != Columns[ColName].Index) return;
            if (!(Rows[e.RowIndex].Tag is DocNode n)) return;
            e.Handled = true;
            Graphics g = e.Graphics;
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            using (var fill = new SolidBrush(selected ? _theme.AccentFill : _theme.Field)) g.FillRectangle(fill, e.CellBounds);
            using (var line = new Pen(_theme.Outline)) g.DrawLine(line, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (n.Children.Count > 0)
            {
                Rectangle c = CaretRect(n, e.CellBounds);
                if (!WeaveIcons.Draw(g, c, n.Expanded ? "caret-down-xs" : "caret-right-xs"))
                    using (var tri = new SolidBrush(_theme.Dim))
                        g.FillPolygon(tri, n.Expanded
                            ? new[] { new PointF(c.X + 2, c.Y + 4), new PointF(c.Right - 2, c.Y + 4), new PointF(c.X + 6, c.Bottom - 3) }
                            : new[] { new PointF(c.X + 4, c.Y + 2), new PointF(c.X + 4, c.Bottom - 2), new PointF(c.Right - 3, c.Y + 6) });
            }
            Rectangle box = BoxRect(n, e.CellBounds);
            DrawBox(g, box, n.IsGroup ? n.Children.Count > 0 && n.Children.All(c => c.Checked) : n.Checked);

            int x = box.Right + 6;
            Color ink = n.Checked || n.IsGroup ? _theme.Text : _theme.Dim;
            Font font = n.IsMain || n.IsGroup ? new Font(Font, FontStyle.Bold) : Font;
            string text = n.IsGroup ? n.Name + " (" + n.Children.Count + ")" : n.Name;
            TextRenderer.DrawText(g, text, font, new Rectangle(x, e.CellBounds.Y, Math.Max(10, e.CellBounds.Right - x - 4), e.CellBounds.Height), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (font != Font) font.Dispose();
        }

        private void DrawBox(Graphics g, Rectangle r, bool on)
        {
            if (on)
            {
                using (var fill = new SolidBrush(_theme.Accent)) g.FillRectangle(fill, r);
                using (var pen = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(r.X + 3, r.Y + r.Height * 0.52f), new PointF(r.X + r.Width * 0.42f, r.Bottom - 3.5f), new PointF(r.Right - 3, r.Y + 3.5f) });
            }
            else
                using (var pen = new Pen(_theme.Dim)) g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        protected override void OnCellMouseDown(DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == Columns[ColName].Index && e.Button == MouseButtons.Left && Rows[e.RowIndex].Tag is DocNode n)
            {
                Rectangle cell = GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                var p = new Point(cell.X + e.X, cell.Y + e.Y);
                Rectangle caret = CaretRect(n, cell), box = BoxRect(n, cell);
                caret.Inflate(3, 4);
                box.Inflate(3, 4);
                if (n.Children.Count > 0 && caret.Contains(p)) { n.Expanded = !n.Expanded; Rebuild(); return; }
                if (box.Contains(p)) { Toggle(n); return; }
            }
            base.OnCellMouseDown(e);
        }

        protected override void OnCellDoubleClick(DataGridViewCellEventArgs e)
        {
            base.OnCellDoubleClick(e);
            if (e.RowIndex >= 0 && e.ColumnIndex == Columns[ColName].Index && Rows[e.RowIndex].Tag is DocNode n && n.Children.Count > 0)
            {
                n.Expanded = !n.Expanded;
                Rebuild();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && !IsCurrentCellInEditMode && SelectedNode != null)
            {
                Toggle(SelectedNode);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        /// <summary>Ticks or unticks a row; for an assembly or the Content Center group, everything under it too.</summary>
        private void Toggle(DocNode n)
        {
            bool on = n.IsGroup ? !n.Children.All(c => c.Checked) : !n.Checked;
            if (!n.IsGroup) n.Checked = on;
            foreach (DocNode d in n.Descendants()) if (!d.IsGroup) d.Checked = on;
            Invalidate();
            PlanChanged?.Invoke();
        }

        protected override void OnSelectionChanged(EventArgs e)
        {
            base.OnSelectionChanged(e);
            if (!_filling) NodeSelected?.Invoke(SelectedNode);
        }
    }
}
