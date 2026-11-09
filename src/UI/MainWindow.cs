using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AutoDraw.Core;
using AutoDraw.Drawing;
using Inventor;
using Path = System.IO.Path;
using Application = System.Windows.Forms.Application;
using Environment = System.Environment;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;

namespace AutoDraw.UI
{
    /// <summary>
    /// The AutoDraw window: a separate, non-modal window (it can go on a second screen) with the order number and
    /// template on the left and the assembly tree on the right. "Generate drawing" makes the production drawing.
    /// </summary>
    internal sealed class MainWindow : Form
    {
        private static MainWindow _active;

        private readonly Inventor.Application _app;
        private readonly AppSettings _settings = AppSettings.Load();
        private readonly Theme _theme;

        private AssemblyDocument _main;
        private StructureReader _reader;
        private List<DocNode> _all = new List<DocNode>();

        private readonly TreeGrid _grid;
        private readonly ProgressPanel _progress;
        private readonly Label _title = new Label();
        private readonly FieldBox _order;
        private readonly FieldBox _template;
        private readonly FieldBox _large;
        private readonly FieldBox _drawingOrder;
        private readonly FieldBox _rules;
        private readonly ThemedCombo _up;
        private readonly PictureBox _thumb = new PictureBox();
        private readonly Label _info = new Label();
        private readonly Label _status = new Label();
        private readonly InventorButton _generate, _close, _reload, _browse, _columns, _numbering;
        private ApplicationEvents _appEvents;
        private bool _busy, _cancel;

        public static void CloseActive() { try { _active?.Close(); } catch { } }

        /// <summary>Opens the window for the active assembly (or brings it forward); returns an error, or null.</summary>
        public static string Start(Inventor.Application app)
        {
            if (!(app.ActiveDocument is AssemblyDocument asm)) return "Open an assembly first.";
            if (string.IsNullOrEmpty(((Document)asm).FullFileName)) return "Save the assembly first.";
            if (_active != null && !_active.IsDisposed)
            {
                if (_active._busy) { _active.Activate(); return null; }
                if (_active._main != asm) _active.LoadAssembly(asm);
                if (_active.WindowState == FormWindowState.Minimized) _active.WindowState = FormWindowState.Normal;
                _active.Activate();
                return null;
            }
            var w = new MainWindow(app);
            _active = w;
            w.Show(new InventorWindow(app.MainFrameHWND));
            w.LoadAssembly(asm);
            return null;
        }

        private MainWindow(Inventor.Application app)
        {
            _app = app;
            _theme = Theme.Detect(app);
            Text = "AutoDraw";
            Font = new Font("Segoe UI", 9f);
            BackColor = _theme.Back;
            ForeColor = _theme.Text;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(900, 500);
            KeyPreview = true;
            RestoreBounds_();

            _order = new FieldBox(_theme) { Dock = DockStyle.Top, Hint = "e.g. 160260496" };
            _template = new FieldBox(_theme) { Dock = DockStyle.Fill, Hint = "Choose an .idw template" };
            _large = new FieldBox(_theme) { Dock = DockStyle.Top };
            _drawingOrder = new FieldBox(_theme) { Hint = "e.g. ordernummer (empty: none)" };
            _rules = new FieldBox(_theme) { Hint = "external rule names, separated by ;" };
            _up = new ThemedCombo(_theme);
            _generate = new InventorButton(_theme, "Generate drawing", true) { Size = new Size(140, 28) };
            _close = new InventorButton(_theme, "Close", false) { Size = new Size(90, 28) };
            _reload = new InventorButton(_theme, "Reload", false) { Size = new Size(80, 26) };
            _browse = new InventorButton(_theme, "…", false) { Size = new Size(30, 22), Dock = DockStyle.Right };
            _columns = new InventorButton(_theme, "Columns…", false) { Size = new Size(80, 26) };
            _numbering = new InventorButton(_theme, "Numbering…", false) { Size = new Size(96, 26) };

            _grid = new TreeGrid(_theme) { Dock = DockStyle.Fill };
            _grid.LargeLimit = () => _settings.LargeAssemblyRows;
            _grid.BuildColumns(_settings.ExtraColumns);
            _grid.PlanChanged += Renumber;
            _grid.NodeSelected += ShowNode;

            _progress = new ProgressPanel(_theme);
            Controls.Add(_grid);
            Controls.Add(_progress);
            Controls.Add(BuildBottom());
            Controls.Add(BuildLeft());

            _order.TextChanged += (s, e) => CheckOrder();
            _template.Text = _settings.TemplatePath;
            _template.TextChanged += (s, e) => { _settings.TemplatePath = _template.Text.Trim(); _settings.Save(); };
            _large.Text = _settings.LargeAssemblyRows.ToString();
            _large.TextChanged += (s, e) =>
            {
                if (int.TryParse(_large.Text.Trim(), out int v) && v >= 1) { _settings.LargeAssemblyRows = v; _settings.Save(); Renumber(); }
            };
            _drawingOrder.Text = _settings.DrawingOrderProperty;
            _drawingOrder.TextChanged += (s, e) => { _settings.DrawingOrderProperty = _drawingOrder.Text.Trim(); _settings.Save(); };
            _rules.Text = string.Join("; ", _settings.DrawingRules);
            _rules.TextChanged += (s, e) =>
            {
                _settings.DrawingRules = _rules.Text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
                _settings.Save();
            };
            _up.SetItems(new[] { "Z up (models standing on XY)", "Y up (Inventor default)" }, _settings.ZUp ? "Z up (models standing on XY)" : "Y up (Inventor default)", null);
            _up.SelectedIndexChanged += (s, e) => { _settings.UpAxis = (_up.Value ?? "").StartsWith("Y") ? "Y" : "Z"; _settings.Save(); };
            _browse.Click += (s, e) => BrowseTemplate();
            _reload.Click += (s, e) => { if (!_busy && _main != null) LoadAssembly(_main); };
            _columns.Click += (s, e) => ColumnsMenu();
            _numbering.Click += (s, e) => Numbering();
            _generate.Click += (s, e) => { if (_busy) { _cancel = true; _generate.Text = "Stopping…"; } else Generate(); };
            _close.Click += (s, e) => Close();

            _appEvents = app.ApplicationEvents;
            _appEvents.OnCloseDocument += OnCloseDocument;
        }

        // ----- layout -----------------------------------------------------------------------------------------------------

        private Control BuildLeft()
        {
            var left = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = _theme.Back, Padding = new Padding(10, 10, 10, 10) };
            var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = _theme.Back };
            left.Controls.Add(stack);
            int w = 276;

            _title.AutoSize = false; _title.Size = new Size(w, 40); _title.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            _title.ForeColor = _theme.Text; _title.TextAlign = ContentAlignment.MiddleLeft; _title.AutoEllipsis = true;
            stack.Controls.Add(_title);

            stack.Controls.Add(Caption("Order number (ordernummer)", w));
            _order.Width = w; _order.Dock = DockStyle.None;
            stack.Controls.Add(_order);

            stack.Controls.Add(Caption("Drawing template", w));
            var tpl = new Panel { Size = new Size(w, 22), BackColor = _theme.Back };
            tpl.Controls.Add(_template);
            tpl.Controls.Add(_browse);
            stack.Controls.Add(tpl);

            stack.Controls.Add(Caption("Two sheets for assemblies with more rows than", w));
            _large.Width = 60; _large.Dock = DockStyle.None;
            stack.Controls.Add(_large);

            stack.Controls.Add(Caption("Model orientation", w));
            _up.Width = w;
            stack.Controls.Add(_up);

            stack.Controls.Add(Caption("Order number also into drawing iProperty", w));
            _drawingOrder.Size = new Size(w, 22);
            stack.Controls.Add(_drawingOrder);

            stack.Controls.Add(Caption("iLogic rules to run on the new drawing", w));
            _rules.Size = new Size(w, 22);
            stack.Controls.Add(_rules);

            var buttons = new FlowLayoutPanel { Size = new Size(w, 34), BackColor = _theme.Back, Margin = new Padding(0, 14, 0, 0) };
            buttons.Controls.Add(_reload);
            buttons.Controls.Add(_columns);
            buttons.Controls.Add(_numbering);
            stack.Controls.Add(buttons);

            _thumb.Size = new Size(w, 190); _thumb.SizeMode = PictureBoxSizeMode.Zoom; _thumb.BackColor = _theme.Field;
            _thumb.Margin = new Padding(0, 14, 0, 6);
            stack.Controls.Add(_thumb);
            _info.AutoSize = false; _info.Size = new Size(w, 120); _info.ForeColor = _theme.Text;
            stack.Controls.Add(_info);

            var help = Caption("Tick the documents that get a sheet. Change the type, Aantal, sheet count or an iProperty in the grid; " +
                "they are written to the files when the drawing is made.", w);
            help.Size = new Size(w, 70);
            stack.Controls.Add(help);
            return left;
        }

        private Label Caption(string text, int w) => new Label
        {
            Text = text, AutoSize = false, Size = new Size(w, 22), ForeColor = _theme.Dim, TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 8, 0, 2)
        };

        private Control BuildBottom()
        {
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = _theme.Header, Padding = new Padding(10, 8, 10, 8) };
            _close.Dock = DockStyle.Right;
            _generate.Dock = DockStyle.Right;
            var spacer = new Panel { Dock = DockStyle.Right, Width = 8, BackColor = _theme.Header };
            _status.Dock = DockStyle.Fill; _status.TextAlign = ContentAlignment.MiddleLeft; _status.ForeColor = _theme.Text; _status.AutoEllipsis = true;
            bottom.Controls.Add(_status);
            bottom.Controls.Add(_generate);
            bottom.Controls.Add(spacer);
            bottom.Controls.Add(_close);
            return bottom;
        }

        // ----- loading ----------------------------------------------------------------------------------------------------

        private void LoadAssembly(AssemblyDocument asm)
        {
            _main = asm;
            Cursor = Cursors.WaitCursor;
            SetStatus("Reading the assembly…", false);
            Application.DoEvents();
            try
            {
                _reader = new StructureReader(s => { SetStatus(s, false); Application.DoEvents(); });
                _reader.Read(asm);
                _all = new List<DocNode> { _reader.Root };
                _all.AddRange(_reader.AllDocs.Where(n => !n.IsMain));
                _title.Text = _reader.Root.Name;
                Text = "AutoDraw – " + _reader.Root.Name;
                _order.Text = Props.Get((Document)asm, _settings.OrderProperty);
                ReadPartNumbers();
                SheetPlan.Number(_all, _settings.LargeAssemblyRows);
                _grid.SetRoot(_reader.Root);
                int parts = _all.Count(n => !n.IsAssembly), asms = _all.Count(n => n.IsAssembly);
                SetStatus(asms + " assemblies, " + parts + " parts (" + _all.Count(n => n.IsContentCenter) + " Content Center). " + CheckOrder(), false);
            }
            catch (Exception ex)
            {
                Log.Write("Reading the assembly failed: " + ex);
                SetStatus("Reading the assembly failed: " + ex.Message, true);
            }
            finally { Cursor = Cursors.Default; }
        }

        private string CheckOrder()
        {
            string problem = Naming.OrderProblem(_order.Text, _settings);
            _order.Invalid = problem != null;
            return problem ?? "";
        }

        private readonly Dictionary<DocNode, string> _filePartNumbers = new Dictionary<DocNode, string>();

        private string PartNumberProperty => _settings.NumberProperties.FirstOrDefault() ?? "Part Number";

        /// <summary>Reads every document's part number (it orders sheets and rows within a kind) and sorts the tree again.</summary>
        private void ReadPartNumbers()
        {
            string prop = PartNumberProperty;
            _filePartNumbers.Clear();
            foreach (DocNode n in _all.Where(n => n.Doc != null))
            {
                _filePartNumbers[n] = Props.Get(n.Doc, prop);
                n.PartNumber = n.Edited.TryGetValue(prop, out string v) ? v : _filePartNumbers[n];
            }
            StructureReader.SortChildren(_reader.Root);
        }

        private void Renumber()
        {
            string prop = PartNumberProperty;
            bool moved = false;
            foreach (DocNode n in _all)
            {
                string v = n.Edited.TryGetValue(prop, out string e) ? e : _filePartNumbers.TryGetValue(n, out string f) ? f : n.PartNumber;
                if (v != n.PartNumber) { n.PartNumber = v; moved = true; }
            }
            if (moved) { StructureReader.SortChildren(_reader.Root); _grid.Rebuild(); }
            SheetPlan.Number(_all, _settings.LargeAssemblyRows);
            _grid.RefreshValues();
            int sheets = SheetPlan.Make(_all, _settings.LargeAssemblyRows).Count;
            SetStatus(sheets + " sheets.", false);
        }

        private void ShowNode(DocNode n)
        {
            Image old = _thumb.Image;
            _thumb.Image = null;
            old?.Dispose();
            if (n == null || n.IsGroup || n.Doc == null) { _info.Text = ""; return; }
            try { _thumb.Image = Icons.FromPicture(n.Doc.GetType().InvokeMember("Thumbnail", System.Reflection.BindingFlags.GetProperty, null, n.Doc, null)); } catch { }
            string size = "";
            try
            {
                Box b = n.Doc is PartDocument p ? p.ComponentDefinition.RangeBox : ((AssemblyDocument)n.Doc).ComponentDefinition.RangeBox;
                size = string.Format("{0:0} × {1:0} × {2:0} mm", (b.MaxPoint.X - b.MinPoint.X) * 10, (b.MaxPoint.Y - b.MinPoint.Y) * 10, (b.MaxPoint.Z - b.MinPoint.Z) * 10);
            }
            catch { }
            _info.Text = n.Name + Environment.NewLine +
                DocKinds.Label(n.Kind) + (n.KindOverride != null ? " (detected: " + DocKinds.Label(n.Detected) + ")" : "") + Environment.NewLine +
                "Total quantity: " + n.Quantity + (n.IsAssembly ? ", parts list rows: " + n.OwnRows : "") + Environment.NewLine +
                size + Environment.NewLine + Path.GetFileName(n.Doc.FullFileName ?? "");
        }

        private void SetStatus(string text, bool bad)
        {
            _status.Text = text;
            _status.ForeColor = bad ? Color.FromArgb(230, 90, 80) : _theme.Text;
        }

        // ----- template and columns ---------------------------------------------------------------------------------------

        private void BrowseTemplate()
        {
            using (var dlg = new OpenFileDialog { Title = "Drawing template", Filter = "Inventor drawing (*.idw)|*.idw" })
            {
                try
                {
                    string cur = _template.Text.Trim();
                    if (System.IO.File.Exists(cur)) dlg.InitialDirectory = Path.GetDirectoryName(cur);
                    else dlg.InitialDirectory = Path.GetDirectoryName(_app.FileManager.GetTemplateFile(DocumentTypeEnum.kDrawingDocumentObject,
                        SystemOfMeasureEnum.kDefaultSystemOfMeasure, DraftingStandardEnum.kDefault_DraftingStandard, Type.Missing));
                }
                catch { }
                if (dlg.ShowDialog(this) == DialogResult.OK) _template.Text = dlg.FileName;
            }
        }

        private void ColumnsMenu()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "Part Number", "Description", "Stock Number", "Revision Number" };
            foreach (DocNode n in _all.Where(n => n.Doc != null && !n.IsContentCenter)) foreach (string p in Props.CustomNames(n.Doc)) names.Add(p);
            names.RemoveWhere(p => string.Equals(p, _settings.QuantityProperty, StringComparison.OrdinalIgnoreCase));
            foreach (string c in _settings.ExtraColumns) names.Add(c);

            var menu = new ThemedMenu(_theme) { ShowCheckMargin = true };
            foreach (string name in names)
            {
                string n = name;
                var item = new ToolStripMenuItem(n) { Checked = _settings.ExtraColumns.Contains(n, StringComparer.OrdinalIgnoreCase), CheckOnClick = false };
                item.Click += (s, e) =>
                {
                    if (_settings.ExtraColumns.RemoveAll(c => string.Equals(c, n, StringComparison.OrdinalIgnoreCase)) == 0) _settings.ExtraColumns.Add(n);
                    _settings.Save();
                    _grid.BuildColumns(_settings.ExtraColumns);
                };
                menu.Items.Add(item);
            }
            menu.Show(_columns, new Point(0, _columns.Height));
        }

        private void Numbering()
        {
            if (_busy || _reader == null) return;
            _grid.EndEdit();
            using (var w = new NumberingWindow(_app, _theme, _settings, _reader.Root, _order.Text))
            {
                w.ShowDialog(this);
                if (!w.Applied) return;
            }
            ReadPartNumbers();
            _grid.Rebuild();
            Renumber();
            _grid.ForgetValues();
            if (_settings.NumberProperties.Count > 0 && !_settings.ExtraColumns.Contains(_settings.NumberProperties[0], StringComparer.OrdinalIgnoreCase))
                SetStatus("Part numbers written to " + string.Join(", ", _settings.NumberProperties) + ".", false);
            else SetStatus("Part numbers written.", false);
        }

        // ----- generating -------------------------------------------------------------------------------------------------

        private void Generate()
        {
            if (_main == null || _reader == null) return;
            _grid.EndEdit();
            string problem = Naming.OrderProblem(_order.Text, _settings);
            if (problem != null) { SetStatus(problem, true); _order.FocusField(); return; }
            if (!System.IO.File.Exists(_template.Text.Trim())) { SetStatus("Choose a drawing template first.", true); return; }

            List<SheetJob> jobs = SheetPlan.Number(_all, _settings.LargeAssemblyRows);
            if (jobs.Count == 0) { SetStatus("Tick at least one document.", true); return; }
            foreach (DocNode n in _all) { n.Status = ""; n.StatusBad = false; }

            _busy = true;
            _cancel = false;
            _grid.Enabled = false;
            _generate.Text = "Cancel";
            _close.Enabled = _reload.Enabled = _numbering.Enabled = false;
            var builder = new DrawingBuilder(_app, _settings);
            ProgressPanel progress = _progress;
            progress.Begin(jobs.Count);
            builder.OnSheet = (done, total, what) =>
            {
                progress.SetProgress(done, total);
                progress.SetStep(done < total ? "Sheet " + (done + 1) + " of " + total : "Saving the drawing…");
                progress.SetDetail(what);
            };
            builder.OnDetail = d => { progress.SetDetail(d); Application.DoEvents(); };
            string path = null;
            try
            {
                path = builder.Run(_main, _reader.Root, jobs, _all, _order.Text, s =>
                {
                    SetStatus(s, false);
                    if (!s.StartsWith("Sheet ")) progress.SetStep(s);
                    try { _app.StatusBarText = "AutoDraw: " + s; } catch { }
                    Application.DoEvents();
                }, () => { Application.DoEvents(); return _cancel; });
            }
            catch (Exception ex)
            {
                Log.Write("Generating failed: " + ex);
                SetStatus("Generating failed: " + ex.Message, true);
            }
            finally
            {
                _busy = false;
                _grid.Enabled = true;
                _generate.Text = "Generate drawing";
                _close.Enabled = _reload.Enabled = _numbering.Enabled = true;
                try { _app.StatusBarText = ""; } catch { }
                progress.End();
                _grid.RefreshValues();
            }

            if (path == null)
            {
                if (_cancel) SetStatus("Cancelled; no drawing saved.", false);
                return;
            }
            string msg = "Saved " + Path.GetFileName(path) + " (" + jobs.Count + " sheets).";
            if (builder.Warnings.Count > 0)
            {
                msg += " " + builder.Warnings.Count + " warning(s): " + string.Join("; ", builder.Warnings.Take(3)) + (builder.Warnings.Count > 3 ? " …" : "");
                Log.Write("Warnings: " + string.Join(" | ", builder.Warnings));
            }
            SetStatus(msg, builder.Warnings.Count > 0);
        }

        // ----- window -----------------------------------------------------------------------------------------------------

        private void OnCloseDocument(_Document doc, string fullName, EventTimingEnum timing, NameValueMap context, out HandlingCodeEnum handling)
        {
            handling = HandlingCodeEnum.kEventNotHandled;
            try
            {
                if (timing == EventTimingEnum.kBefore && _main != null && !_busy && ReferenceEquals(doc, _main)) BeginInvoke((Action)Close);
            }
            catch { }
        }

        private void RestoreBounds_()
        {
            var r = new Rectangle(_settings.WindowX, _settings.WindowY, Math.Max(900, _settings.WindowW), Math.Max(500, _settings.WindowH));
            bool visible = Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r)) && r.X != -1;
            if (!visible)
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                r = new Rectangle(wa.X + (wa.Width - r.Width) / 2, wa.Y + (wa.Height - r.Height) / 2, Math.Min(r.Width, wa.Width), Math.Min(r.Height, wa.Height));
            }
            Bounds = r;
            if (_settings.WindowMaximized) WindowState = FormWindowState.Maximized;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy) { _cancel = true; e.Cancel = true; return; }
            Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _settings.WindowX = b.X; _settings.WindowY = b.Y; _settings.WindowW = b.Width; _settings.WindowH = b.Height;
            _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
            _settings.Save();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _appEvents.OnCloseDocument -= OnCloseDocument; } catch { }
            _appEvents = null;
            if (_active == this) _active = null;
            base.OnFormClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && !_grid.IsCurrentCellInEditMode) { Close(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }
    }
}
