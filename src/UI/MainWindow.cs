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

        /// <summary>The models of the drawing: one main assembly, or several separate assemblies and parts.</summary>
        private List<Document> _models = new List<Document>();
        /// <summary>The drawing the window was opened from (it then updates that drawing), or null.</summary>
        private DrawingDocument _drawing;
        private StructureReader _reader;
        private List<DocNode> _all = new List<DocNode>();

        private readonly TreeGrid _grid;
        private readonly ProgressPanel _progress;
        private readonly Label _title = new Label();
        private readonly FieldBox _order;
        private readonly FieldBox _template;
        private readonly FieldBox _large;
        private readonly FieldBox _drawingOrder;
        private readonly FieldBox _orderProp;
        private CheckBox _askCheckOut;
        private Label _orderCaption;
        private readonly FieldBox _sheetName;
        private readonly FieldBox _rules;
        private readonly ThemedCombo _up;
        private readonly PictureBox _thumb = new PictureBox();
        private readonly Label _info = new Label();
        private readonly Label _status = new Label();
        private readonly InventorButton _generate, _close, _reload, _browse, _columns, _numbering, _modelsButton;
        /// <summary>One tick per document type, always shown: a type switched off gets no sheets.</summary>
        private readonly Dictionary<DocKind, CheckBox> _typeBoxes = new Dictionary<DocKind, CheckBox>();
        private FlowLayoutPanel _typePanel;
        private bool _typesFilling;
        private ApplicationEvents _appEvents;
        private bool _busy, _cancel;

        public static void CloseActive() { try { _active?.Close(); } catch { } }

        /// <summary>
        /// Opened by another program (<see cref="AutoDrawAutomation"/>): the window for exactly these models, in place of
        /// what it showed. Returns an error, or null.
        /// </summary>
        public static string OpenModels(Inventor.Application app, List<Document> models, string options = null)
        {
            MainWindow w = _active;
            if (w != null && !w.IsDisposed)
            {
                if (w._busy) return "AutoDraw is busy making a drawing.";
                if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
            }
            else
            {
                w = new MainWindow(app);
                _active = w;
                w.Show(new InventorWindow(app.MainFrameHWND));
            }
            w.LoadModels(models, null);
            string problems = w.ApplyOptions(options);
            w.Activate();
            return problems;
        }

        /// <summary>
        /// Fills in fields and switches ticks as another program asked (see <see cref="WindowOptions"/>): order, orderProperty,
        /// template, twoSheetsAbove, up (Z/Y), drawingProperty, sheetName, rules, askCheckOut, and a document type
        /// (Assembly, Bent, Flat, Lathe, Tube, General) = on/off. Like typing them in, they are kept as settings, except the
        /// order number. Returns the options that were not understood, or null.
        /// </summary>
        private string ApplyOptions(string options)
        {
            List<KeyValuePair<string, string>> list;
            try { list = WindowOptions.Parse(options); }
            catch (Exception ex) { return "Options not understood: " + ex.Message; }
            if (list.Count == 0) return null;
            var problems = new List<string>();
            string order = null;
            // the order number's iProperty first: changing it reads the order number again
            foreach (KeyValuePair<string, string> kv in list.OrderBy(kv => kv.Key == "orderproperty" ? 0 : 1))
            {
                string v = kv.Value ?? "";
                switch (kv.Key)
                {
                    case "order": case "ordernumber": case "ordernummer": order = v; break;
                    case "orderproperty": if (v.Length > 0) _orderProp.Text = v; break;
                    case "template": _template.Text = v; break;
                    case "twosheetsabove": case "largeassemblyrows": _large.Text = v; break;
                    case "up": case "orientation": case "upaxis":
                        _settings.UpAxis = v.Trim().ToUpperInvariant().StartsWith("Y") ? "Y" : "Z";
                        _settings.Save();
                        _up.SetItems(new[] { "Z up (models standing on XY)", "Y up (Inventor default)" }, _settings.ZUp ? "Z up (models standing on XY)" : "Y up (Inventor default)", null);
                        break;
                    case "drawingproperty": case "drawingorderproperty": _drawingOrder.Text = v; break;
                    case "sheetname": case "sheetnameproperty": _sheetName.Text = v; break;
                    case "rules": case "ilogicrules": _rules.Text = v.Replace(',', ';'); break;
                    case "askcheckout":
                        if (WindowOptions.Bool(v) is bool ask) _askCheckOut.Checked = ask; else problems.Add(kv.Key + "=" + v);
                        break;
                    default:
                        DocKind? kind = WindowOptions.Kind(kv.Key);
                        if (kind != null && WindowOptions.Bool(v) is bool on && _typeBoxes.TryGetValue(kind.Value, out CheckBox box))
                        {
                            if (box.Checked != on) box.Checked = on;   // ticks or unticks the documents of that type
                        }
                        else problems.Add(kv.Key + "=" + v);
                        break;
                }
            }
            if (order != null) _order.Text = order;
            CheckOrder();
            return problems.Count > 0 ? "Options not understood: " + string.Join(", ", problems) : null;
        }

        /// <summary>
        /// Opens the window for the active assembly, or for the assembly of the active AutoDraw drawing (then the window
        /// updates that drawing), or brings it forward; returns an error, or null.
        /// </summary>
        public static string Start(Inventor.Application app)
        {
            Document active = app.ActiveDocument;
            var dwg = active as DrawingDocument;
            Document asm = active is AssemblyDocument || active is PartDocument ? active : null;
            List<Document> asms;
            if (dwg != null)
            {
                if (string.IsNullOrEmpty(active.FullFileName)) return "Save the drawing first.";
                asms = SheetMap.FindModels(app, dwg);
                if (asms.Count == 0) return "This drawing shows no model.";
            }
            else if (asm == null) return "Open an assembly or a part, or a drawing made by AutoDraw.";
            else if (string.IsNullOrEmpty(active.FullFileName)) return "Save the " + (active is PartDocument ? "part" : "assembly") + " first.";
            else asms = new List<Document> { asm };

            MainWindow w = _active;
            if (w != null && !w.IsDisposed)
            {
                if (w._busy) { w.Activate(); return null; }
                if (w.WindowState == FormWindowState.Minimized) w.WindowState = FormWindowState.Normal;
                if (dwg != null) { if (w._drawing != dwg) w.LoadModels(asms, dwg); }
                else if (w._drawing != null || !w._models.Contains(asm))
                {
                    bool add = false;
                    if (w._drawing == null && w._models.Count > 0)
                    {
                        w.Activate();
                        DialogResult r = MessageBox.Show(w,
                            "Add " + DocName(asm) + " to the drawing of " + (w._reader?.Root.Name ?? "") + "?" + Environment.NewLine + Environment.NewLine +
                            "Yes: one drawing of several models. No: start again with " + DocName(asm) + " only.",
                            "AutoDraw", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                        if (r == DialogResult.Cancel) return null;
                        add = r == DialogResult.Yes;
                    }
                    if (add) w.ChangeModels(w._models.Concat(new[] { asm }).ToList());
                    else w.LoadModels(asms, null);
                }
                w.Activate();
                return null;
            }
            w = new MainWindow(app);
            _active = w;
            w.Show(new InventorWindow(app.MainFrameHWND));
            w.LoadModels(asms, dwg);
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
            _orderProp = new FieldBox(_theme) { Hint = "e.g. ordernummer" };
            _sheetName = new FieldBox(_theme) { Hint = "e.g. Part Number (empty: file name)" };
            _rules = new FieldBox(_theme) { Hint = "external rule names, separated by ;" };
            _up = new ThemedCombo(_theme);
            _generate = new InventorButton(_theme, "Generate drawing", true) { Size = new Size(140, 28) };
            _close = new InventorButton(_theme, "Close", false) { Size = new Size(90, 28) };
            _reload = new InventorButton(_theme, "Reload", false) { Size = new Size(80, 26) };
            _browse = new InventorButton(_theme, "…", false) { Size = new Size(30, 22), Dock = DockStyle.Right };
            _columns = new InventorButton(_theme, "Columns…", false) { Size = new Size(80, 26) };
            _numbering = new InventorButton(_theme, "Numbering…", false) { Size = new Size(96, 26) };
            _modelsButton = new InventorButton(_theme, "Models…", false) { Size = new Size(80, 26) };

            _grid = new TreeGrid(_theme) { Dock = DockStyle.Fill };
            _grid.LargeLimit = () => _settings.LargeAssemblyRows;
            _grid.ShowLocation = () => _settings.ShowLocation;
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
            _orderProp.Text = _settings.OrderProperty;
            _orderProp.TextChanged += (s, e) =>
            {
                if (_orderProp.Text.Trim().Length == 0) return;
                _settings.OrderProperty = _orderProp.Text.Trim();
                _settings.Save();
                ReadOrder();
            };
            _sheetName.Text = _settings.SheetNameProperty;
            _sheetName.TextChanged += (s, e) => { _settings.SheetNameProperty = _sheetName.Text.Trim(); _settings.Save(); };
            _rules.Text = string.Join("; ", _settings.DrawingRules);
            _rules.TextChanged += (s, e) =>
            {
                _settings.DrawingRules = _rules.Text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
                _settings.Save();
            };
            _up.SetItems(new[] { "Z up (models standing on XY)", "Y up (Inventor default)" }, _settings.ZUp ? "Z up (models standing on XY)" : "Y up (Inventor default)", null);
            _up.SelectedIndexChanged += (s, e) => { _settings.UpAxis = (_up.Value ?? "").StartsWith("Y") ? "Y" : "Z"; _settings.Save(); };
            _browse.Click += (s, e) => BrowseTemplate();
            _reload.Click += (s, e) => { if (!_busy && _models.Count > 0) LoadModels(_models, _drawing); };
            _modelsButton.Click += (s, e) => ModelsMenu();
            _columns.Click += (s, e) => ColumnsMenu();
            _numbering.Click += (s, e) => Numbering();
            _generate.Click += (s, e) => { if (_busy) { _cancel = true; _generate.Text = "Stopping…"; } else if (_drawing != null) UpdateDrawing(); else Generate(); };
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
            int w = 280 - SystemInformation.VerticalScrollBarWidth - 2;   // room for the scroll bar when Settings is open

            _title.AutoSize = false; _title.Size = new Size(w, 40); _title.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            _title.ForeColor = _theme.Text; _title.TextAlign = ContentAlignment.MiddleLeft; _title.AutoEllipsis = true;
            stack.Controls.Add(_title);

            _orderCaption = Caption("Order number (" + _settings.OrderProperty + ")", w);
            stack.Controls.Add(_orderCaption);
            _order.Width = w; _order.Dock = DockStyle.None;
            stack.Controls.Add(_order);

            stack.Controls.Add(Caption("Drawing template", w));
            var tpl = new Panel { Size = new Size(w, 22), BackColor = _theme.Back };
            tpl.Controls.Add(_template);
            tpl.Controls.Add(_browse);
            stack.Controls.Add(tpl);

            var buttons = new FlowLayoutPanel { Size = new Size(w, 34), BackColor = _theme.Back, Margin = new Padding(0, 14, 0, 0) };
            buttons.Controls.Add(_reload);
            buttons.Controls.Add(_numbering);
            buttons.Controls.Add(_modelsButton);
            stack.Controls.Add(buttons);

            stack.Controls.Add(Caption("Sheets for", w));
            _typePanel = new FlowLayoutPanel { Size = new Size(w, 3 * 24), BackColor = _theme.Back, Margin = new Padding(0) };
            foreach (DocKind kind in DocKinds.All)
            {
                DocKind k = kind;
                var box = new CheckBox
                {
                    Text = ShortLabel(k), Size = new Size(w / 2 - 2, 22), Margin = new Padding(0, 0, 2, 2),
                    ForeColor = _theme.Text, BackColor = _theme.Back, FlatStyle = FlatStyle.Flat, Checked = !_settings.Skips(k)
                };
                box.CheckedChanged += (s, e) => { if (!_typesFilling) SwitchType(k, box.Checked); };
                _typeBoxes[k] = box;
                _typePanel.Controls.Add(box);
            }
            stack.Controls.Add(_typePanel);

            // the settings that are set once: folded away under a Settings header
            var header = new Label
            {
                AutoSize = false, Size = new Size(w, 26), ForeColor = _theme.Text, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(0, 10, 0, 0)
            };
            var more = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = _theme.Back, Margin = new Padding(0)
            };
            void Fold() { more.Visible = _settings.SettingsOpen; header.Text = (_settings.SettingsOpen ? "▾  " : "▸  ") + "Settings"; }
            header.Click += (s, e) =>
            {
                _settings.SettingsOpen = !_settings.SettingsOpen;
                _settings.Save();
                Fold();
                if (more.Visible && more.Controls.Count > 0) stack.ScrollControlIntoView(more.Controls[more.Controls.Count - 1]);
            };

            var setButtons = new FlowLayoutPanel { Size = new Size(w, 32), BackColor = _theme.Back, Margin = new Padding(0, 4, 0, 0) };
            setButtons.Controls.Add(_columns);
            more.Controls.Add(setButtons);

            more.Controls.Add(Caption("Two sheets above this many parts list rows", w));
            _large.Width = 60; _large.Dock = DockStyle.None;
            more.Controls.Add(_large);

            more.Controls.Add(Caption("Model orientation", w));
            _up.Width = w;
            more.Controls.Add(_up);

            more.Controls.Add(Caption("Order number from iProperty", w));
            more.Controls.Add(PropField(_orderProp, w, "ordernummer (default)", "ordernummer"));

            more.Controls.Add(Caption("Order number also into drawing iProperty", w));
            more.Controls.Add(PropField(_drawingOrder, w, "(none)"));

            more.Controls.Add(Caption("Name sheets after iProperty", w));
            more.Controls.Add(PropField(_sheetName, w, "(file name)"));

            more.Controls.Add(Caption("iLogic rules to run on the new drawing", w));
            _rules.Size = new Size(w, 22);
            more.Controls.Add(_rules);

            var askCheckOut = _askCheckOut = new CheckBox
            {
                Text = "Ask to check out checked-in files (Vault)", Size = new Size(w, 24), Margin = new Padding(0, 10, 0, 0),
                ForeColor = _theme.Text, BackColor = _theme.Back, FlatStyle = FlatStyle.Flat, Checked = _settings.AskCheckOut
            };
            askCheckOut.CheckedChanged += (s, e) => { _settings.AskCheckOut = askCheckOut.Checked; _settings.Save(); };
            more.Controls.Add(askCheckOut);
            Fold();

            _thumb.Size = new Size(w, w);   // square, like the thumbnails _thumb.SizeMode = PictureBoxSizeMode.Zoom; _thumb.BackColor = _theme.Field;
            _thumb.Margin = new Padding(0, 14, 0, 6);
            stack.Controls.Add(_thumb);
            _info.AutoSize = false; _info.Size = new Size(w, 120); _info.ForeColor = _theme.Text;
            stack.Controls.Add(_info);

            var help = Caption("Tick the documents that get a sheet. Change the type, Aantal, sheet count or an iProperty in the grid; " +
                "they are written to the files when the drawing is made. Models… adds more assemblies or loose parts to the drawing.", w);
            help.Size = new Size(w, 104);
            stack.Controls.Add(help);

            // Settings at the bottom
            stack.Controls.Add(header);
            stack.Controls.Add(more);
            return left;
        }

        /// <summary>An iProperty field with a … button that lists the standard iProperties and the custom ones in use.</summary>
        private Control PropField(FieldBox field, int w, string noneText, string noneValue = "")
        {
            var panel = new Panel { Size = new Size(w, 22), BackColor = _theme.Back };
            field.Dock = DockStyle.Fill;
            var pick = new InventorButton(_theme, "…", false) { Size = new Size(30, 22), Dock = DockStyle.Right };
            pick.Click += (s, e) => PropMenu(field, pick, noneText, noneValue);
            panel.Controls.Add(field);
            panel.Controls.Add(pick);
            return panel;
        }

        private void PropMenu(FieldBox field, Control at, string noneText, string noneValue)
        {
            var custom = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DocNode n in _all.Where(n => n.Doc != null && !n.IsContentCenter)) foreach (string p in Props.CustomNames(n.Doc)) custom.Add(p);
            try { if (_drawing != null) foreach (string p in Props.CustomNames((Document)_drawing)) custom.Add(p); } catch { }
            custom.ExceptWith(NumberingWindow.Standard);
            custom.Remove(SheetMap.AssembliesProperty);
            string cur = field.Text.Trim();
            var menu = new ThemedMenu(_theme) { ShowCheckMargin = true };
            void Item(string text, string value)
            {
                var item = new ToolStripMenuItem(text) { Checked = string.Equals(cur, value, StringComparison.OrdinalIgnoreCase) };
                item.Click += (s, e) => field.Text = value;
                menu.Items.Add(item);
            }
            void Header(string text) => menu.Items.Add(new ToolStripMenuItem(text) { Enabled = false });
            Item(noneText, noneValue);
            menu.Items.Add(new ToolStripSeparator());
            Header("Standard");
            foreach (string p in NumberingWindow.Standard) Item(p, p);
            if (custom.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                Header("Custom");
                foreach (string p in custom) Item(p, p);
            }
            menu.Show(at, new Point(0, at.Height));
        }

        private Label Caption(string text, int w) => new Label
        {
            Text = text, AutoSize = false, Size = new Size(w, 22), ForeColor = _theme.Dim, TextAlign = ContentAlignment.BottomLeft, AutoEllipsis = true,
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

        private static string DocName(Document a) => Path.GetFileNameWithoutExtension(a.FullFileName ?? "?");

        /// <summary>
        /// Reads the assemblies into the tree. <paramref name="previous"/> (the tree before the list of assemblies
        /// changed) keeps the ticks, types and typed values of the documents that were there already.
        /// </summary>
        private void LoadModels(List<Document> asms, DrawingDocument drawing, Dictionary<string, DocNode> previous = null)
        {
            string typedOrder = _order.Text;
            _models = asms.ToList();
            _drawing = drawing;
            _grid.DrawingMode = drawing != null;
            _grid.BuildColumns(_settings.ExtraColumns);
            _generate.Text = drawing != null ? "Update drawing" : "Generate drawing";
            Cursor = Cursors.WaitCursor;
            SetStatus("Reading the model…", false);
            Application.DoEvents();
            try
            {
                _reader = new StructureReader(s => { SetStatus(s, false); Application.DoEvents(); });
                _reader.Read(_models);
                _all = new List<DocNode> { _reader.Root };
                _all.AddRange(_reader.AllDocs.Where(n => n != _reader.Root));
                _title.Text = _reader.Root.Name;
                Text = "AutoDraw – " + (drawing != null ? Path.GetFileName(((Document)drawing).FullFileName) : _reader.Root.Name);
                _order.Text = previous != null && typedOrder.Trim().Length > 0 ? typedOrder
                    : _models.Select(a => Props.Get(a, _settings.OrderProperty)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
                // types switched off start unticked (opened from a drawing, the drawing's sheets decide)
                if (drawing == null && previous == null)
                    foreach (DocKind k in DocKinds.All.Where(_settings.Skips)) SheetPlan.TickKind(_all, k, false);
                string drawingText = drawing != null ? ApplyDrawing(false) : null;
                if (previous != null) Restore(previous, drawing != null);
                Quantities.Compute(_all);
                ReadTopQuantities(previous);
                Quantities.Compute(_all);
                ReadPartNumbers();
                SheetPlan.Number(_all, _settings.LargeAssemblyRows);
                _grid.SetRoot(_reader.Root);
                ShowTypeCounts();
                // the preview shows the first document straight away (the first model when there are several)
                DocNode first = _reader.Root.Doc != null ? _reader.Root : _reader.Root.Children.FirstOrDefault(c => c.Doc != null);
                if (first != null) _grid.Select(first);
                ShowNode(first);
                int parts = _all.Count(n => !n.IsAssembly), subs = _all.Count(n => n.IsAssembly && !n.IsVirtual);
                SetStatus(drawingText ?? (_models.Count > 1 ? _models.Count + " separate models; " : "") +
                    subs + " assemblies, " + parts + " parts (" + _all.Count(n => n.IsContentCenter) + " Content Center). " + CheckOrder(), false);
            }
            catch (Exception ex)
            {
                Log.Write("Reading the assembly failed: " + ex);
                SetStatus("Reading the assembly failed: " + ex.Message, true);
            }
            finally { Cursor = Cursors.Default; }
        }

        /// <summary>
        /// The quantity of the main assembly (of each assembly, when there are several) as AutoDraw wrote it last time:
        /// a main assembly made 3 times keeps 3 when the window is opened again, so its parts stay ×3.
        /// </summary>
        private void ReadTopQuantities(Dictionary<string, DocNode> previous)
        {
            DocNode root = _reader.Root;
            IEnumerable<DocNode> tops = root.IsVirtual ? root.Children.Where(c => !c.IsGroup) : new[] { root };
            foreach (DocNode t in tops)
            {
                if (t.Doc == null || t.QuantityText != null || (previous != null && previous.ContainsKey(t.Key))) continue;
                if (int.TryParse(Props.Get(t.Doc, _settings.QuantityProperty).Trim(), out int v) && v >= 1 && v != t.Quantity)
                    t.QuantityText = v.ToString();
            }
        }

        private Dictionary<string, DocNode> Snapshot()
        {
            var map = new Dictionary<string, DocNode>(StringComparer.OrdinalIgnoreCase);
            foreach (DocNode n in _all.Where(n => !n.IsGroup && !n.IsVirtual && n.Key.Length > 0)) if (!map.ContainsKey(n.Key)) map[n.Key] = n;
            return map;
        }

        /// <summary>Gives the documents their ticks, types and typed values back; new documents are ticked (unless their type is off).</summary>
        private void Restore(Dictionary<string, DocNode> previous, bool drawingMode)
        {
            foreach (DocNode n in _all.Where(n => !n.IsGroup && !n.IsVirtual))
            {
                if (previous.TryGetValue(n.Key, out DocNode p))
                {
                    n.Checked = p.Checked;
                    n.KindOverride = p.KindOverride;
                    n.QuantityText = p.QuantityText;
                    n.SheetsOverride = p.SheetsOverride;
                    n.Redo = p.Redo;
                    n.Expanded = p.Expanded;
                    foreach (KeyValuePair<string, string> kv in p.Edited) n.Edited[kv.Key] = kv.Value;
                }
                else n.Checked = !n.IsContentCenter && !n.IsEmpty && !_settings.Skips(n.Kind);
            }
        }

        /// <summary>Reads the tree again for another list of assemblies, keeping what was set for the documents.</summary>
        private void ChangeModels(List<Document> asms)
        {
            if (_busy || asms.Count == 0 || asms.SequenceEqual(_models)) return;
            _grid.EndEdit();
            LoadModels(asms, _drawing, Snapshot());
        }

        /// <summary>
        /// A type switched off: all its documents are unticked, and stay unticked the next time (each one can still be
        /// ticked by hand); switched on: they are ticked again.
        /// </summary>
        private void SwitchType(DocKind k, bool on)
        {
            if (_busy) return;
            _settings.SkippedKinds.RemoveAll(x => string.Equals(x, k.ToString(), StringComparison.OrdinalIgnoreCase));
            if (!on) _settings.SkippedKinds.Add(k.ToString());
            _settings.Save();
            _grid.EndEdit();
            int changed = SheetPlan.TickKind(_all, k, on);
            _grid.Invalidate();
            Renumber();
            SetStatus(DocKinds.Label(k) + (on ? " on: " : " off: ") + changed + " document(s) " + (on ? "ticked" : "unticked") + ". " + _status.Text, false);
        }

        /// <summary>The type names for the ticks.</summary>
        private static string ShortLabel(DocKind k) => k == DocKind.Assembly ? "Assemblies" : DocKinds.Label(k);

        /// <summary>The type ticks with the number of documents of each type.</summary>
        private void ShowTypeCounts()
        {
            _typesFilling = true;
            try
            {
                foreach (KeyValuePair<DocKind, CheckBox> kv in _typeBoxes)
                {
                    int count = _all.Count(n => !n.IsGroup && !n.IsVirtual && !n.IsContentCenter && n.Kind == kv.Key);
                    kv.Value.Text = ShortLabel(kv.Key) + " (" + count + ")";
                    kv.Value.Checked = !_settings.Skips(kv.Key);
                }
            }
            finally { _typesFilling = false; }
        }

        /// <summary>The models in the drawing (click one to take it out), the other open assemblies and parts, and Add from file.</summary>
        private void ModelsMenu()
        {
            if (_busy) return;
            var menu = new ThemedMenu(_theme) { ShowCheckMargin = true };
            foreach (Document a in _models)
            {
                Document asm = a;
                var item = new ToolStripMenuItem(DocName(asm)) { Checked = true, Enabled = _models.Count > 1, ToolTipText = "Click to take it out of the drawing" };
                item.Click += (s, e) => ChangeModels(_models.Where(x => x != asm).ToList());
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());

            var inTree = new HashSet<string>(_all.Select(n => n.Key), StringComparer.OrdinalIgnoreCase);
            var open = new List<Document>();
            try
            {
                foreach (Document d in _app.Documents.VisibleDocuments)
                    if ((d is AssemblyDocument || d is PartDocument) && !string.IsNullOrEmpty(d.FullFileName) && !inTree.Contains(d.FullDocumentName ?? "") && !open.Contains(d)) open.Add(d);
            }
            catch (Exception ex) { Log.Write("Listing the open assemblies failed: " + ex.Message); }
            foreach (Document o in open.OrderBy(d => d is PartDocument ? 1 : 0).Take(20))
            {
                Document asm = o;
                var item = new ToolStripMenuItem("Add " + DocName(asm) + (asm is PartDocument ? "  (part)" : ""));
                item.Click += (s, e) => ChangeModels(_models.Concat(new[] { asm }).ToList());
                menu.Items.Add(item);
            }
            var file = new ToolStripMenuItem("Add from file…");
            file.Click += (s, e) => AddFromFile();
            menu.Items.Add(file);
            menu.Show(_modelsButton, new Point(0, _modelsButton.Height));
        }

        private void AddFromFile()
        {
            using (var dlg = new OpenFileDialog { Title = "Add assemblies, parts or STEP files", Filter = "Models (*.iam;*.ipt;*.stp;*.step)|*.iam;*.ipt;*.stp;*.step|Assemblies (*.iam)|*.iam|Parts (*.ipt)|*.ipt|STEP (*.stp;*.step)|*.stp;*.step", Multiselect = true })
            {
                try { if (_models.Count > 0) dlg.InitialDirectory = Path.GetDirectoryName(_models[0].FullFileName); } catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var list = _models.ToList();
                Cursor = Cursors.WaitCursor;
                try
                {
                    foreach (string f in dlg.FileNames)
                    {
                        SetStatus("Opening " + Path.GetFileName(f) + "…", false);
                        Application.DoEvents();
                        try
                        {
                            Document a = ModelImport.Open(_app, f);   // a STEP: its .ipt/.iam next to it, imported when there is none
                            if ((a is AssemblyDocument || a is PartDocument) && !list.Contains(a)) list.Add(a);
                        }
                        catch (Exception ex) { Log.Write("Opening " + f + " failed: " + ex.Message); SetStatus("Could not open " + Path.GetFileName(f) + ": " + ex.Message, true); return; }
                    }
                }
                finally { Cursor = Cursors.Default; }
                ChangeModels(list);
            }
        }

        /// <summary>
        /// Opened from a drawing: ticks the documents that have a sheet there (the others are unticked: tick them to add
        /// a sheet), keeps the sheet count of assemblies as drawn, and describes the drawing.
        /// </summary>
        private string ApplyDrawing(bool keepErrors)
        {
            List<SheetOwner> owners = SheetMap.Read(_drawing);
            Dictionary<string, int> counts = SheetMap.Counts(owners);
            foreach (DocNode n in _all.Where(n => !n.IsGroup && !n.IsVirtual))
            {
                counts.TryGetValue(n.Key, out int c);
                n.SheetsInDrawing = c;
                n.Checked = c > 0;
                n.Redo = false;
                if (n.IsAssembly && c > 0)
                {
                    n.SheetsOverride = null;
                    if (c != n.SheetCount(_settings.LargeAssemblyRows)) n.SheetsOverride = c;
                }
                if (keepErrors && n.StatusBad) continue;
                n.Status = n.IsEmpty ? n.EmptyText : c > 0 || n.IsContentCenter ? "" : "no sheet yet";
                n.StatusBad = false;
            }
            if (_reader?.ContentGroup != null) _reader.ContentGroup.Expanded = _reader.ContentGroup.Children.Any(c => c.Checked);
            var keys = new HashSet<string>(_all.Select(n => n.Key), StringComparer.OrdinalIgnoreCase);
            int foreign = owners.Count(o => o.Key != null && !keys.Contains(o.Key));
            int missing = _all.Count(n => !n.IsGroup && !n.IsVirtual && !n.IsContentCenter && n.SheetsInDrawing == 0);
            return "Drawing with " + owners.Count + " sheets." +
                (missing > 0 ? " " + missing + " document(s) have no sheet yet: tick them to add one." : "") +
                (foreign > 0 ? " " + foreign + " sheet(s) show documents that are no longer in the assembly." : "") +
                " Tick Redo to make a sheet again.";
        }

        /// <summary>The order number again from the chosen iProperty of the models (the first that has one).</summary>
        private void ReadOrder()
        {
            _orderCaption.Text = "Order number (" + _settings.OrderProperty + ")";
            if (_orderProp.Text.Trim() != _settings.OrderProperty) _orderProp.Text = _settings.OrderProperty;
            string v = _models.Select(a => Props.Get(a, _settings.OrderProperty)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (v != null) _order.Text = v;
            CheckOrder();
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
            ShowTypeCounts();
            Quantities.Compute(_all);
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
            try
            {
                Image picture = Icons.FromPicture(n.Doc.GetType().InvokeMember("Thumbnail", System.Reflection.BindingFlags.GetProperty, null, n.Doc, null));
                _thumb.Image = Icons.Fit(picture, Math.Min(_thumb.ClientSize.Width, _thumb.ClientSize.Height));
            }
            catch { }
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
            var location = new ToolStripMenuItem("File location") { Checked = _settings.ShowLocation };
            location.Click += (s, e) =>
            {
                _settings.ShowLocation = !_settings.ShowLocation;
                _settings.Save();
                _grid.BuildColumns(_settings.ExtraColumns);
            };
            menu.Items.Add(location);
            menu.Items.Add(new ToolStripSeparator());
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
            if (!CheckOut(_all.Where(n => n.Checked || n.Edited.Count > 0))) return;
            string orderProp = _settings.OrderProperty;
            using (var w = new NumberingWindow(_app, _theme, _settings, _reader.Root, _order.Text))
            {
                w.ShowDialog(this);
                if (!string.Equals(orderProp, _settings.OrderProperty, StringComparison.OrdinalIgnoreCase)) ReadOrder();
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

        /// <summary>Asks to check out the read-only (checked in) files among these documents, the models and the drawing.</summary>
        private bool CheckOut(IEnumerable<DocNode> nodes)
        {
            if (!_settings.AskCheckOut) return true;   // switched off: checked-in files are simply not saved (a warning says so)
            var docs = nodes.Where(n => n.Doc != null && !n.IsGroup && !n.IsContentCenter).Select(n => n.Doc).Concat(_models).ToList();
            var tops = new List<Document>();
            if (_drawing != null) { docs.Add((Document)_drawing); tops.Add((Document)_drawing); }
            tops.AddRange(_models);
            return VaultCheckout.Ensure(_app, this, docs, tops);
        }

        private void Generate()
        {
            if (_models.Count == 0 || _reader == null) return;
            _grid.EndEdit();
            string problem = Naming.OrderProblem(_order.Text, _settings);
            if (problem != null) { SetStatus(problem, true); _order.FocusField(); return; }
            if (!System.IO.File.Exists(_template.Text.Trim())) { SetStatus("Choose a drawing template first.", true); return; }

            List<SheetJob> jobs = SheetPlan.Number(_all, _settings.LargeAssemblyRows);
            if (jobs.Count == 0) { SetStatus("Tick at least one document.", true); return; }
            if (!CheckOut(jobs.Select(j => j.Node).Concat(_all.Where(n => n.Edited.Count > 0)))) { SetStatus("Cancelled.", false); return; }
            foreach (DocNode n in _all) { n.Status = n.IsEmpty ? n.EmptyText : ""; n.StatusBad = false; }

            string path = null;
            DrawingBuilder builder = RunBuilder(jobs.Count, "Generating", (b, status, cancelled) =>
                path = b.Run(_models, _reader.Root, jobs, _all, _order.Text, status, cancelled));
            if (builder == null) return;
            if (path == null)
            {
                if (_cancel) SetStatus("Cancelled; no drawing saved.", false);
                return;
            }
            Report(builder, "Saved " + Path.GetFileName(path) + " (" + jobs.Count + " sheets).");
        }

        /// <summary>
        /// Opened from a drawing: adds sheets for newly ticked documents, makes the Redo ones again, deletes (after
        /// asking) the sheets of unticked documents and of documents no longer in the assembly, and re-sorts and renumbers.
        /// </summary>
        private void UpdateDrawing()
        {
            if (_models.Count == 0 || _reader == null || _drawing == null) return;
            _grid.EndEdit();
            string problem = Naming.OrderProblem(_order.Text, _settings);
            if (problem != null) { SetStatus(problem, true); _order.FocusField(); return; }
            List<SheetJob> jobs = SheetPlan.Number(_all, _settings.LargeAssemblyRows);
            if (jobs.Count == 0) { SetStatus("Tick at least one document.", true); return; }
            if (!CheckOut(jobs.Select(j => j.Node).Concat(_all.Where(n => n.Edited.Count > 0)))) { SetStatus("Cancelled.", false); return; }

            List<SheetOwner> owners;
            try { owners = SheetMap.Read(_drawing); }
            catch (Exception ex) { SetStatus("Reading the drawing failed: " + ex.Message, true); return; }
            var planned = new HashSet<string>(jobs.Select(j => j.Node.Key), StringComparer.OrdinalIgnoreCase);
            List<string> going = owners.Where(o => o.Key != null && !planned.Contains(o.Key)).Select(o => o.Name).ToList();
            bool deleteOthers = false;
            if (going.Count > 0)
            {
                DialogResult r = MessageBox.Show(this,
                    "These sheets show documents that are unticked or no longer in the assembly:" + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, going.Take(20)) + (going.Count > 20 ? Environment.NewLine + "… and " + (going.Count - 20) + " more" : "") +
                    Environment.NewLine + Environment.NewLine + "Delete them? No keeps them at the end of the drawing.",
                    "AutoDraw", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                deleteOthers = r == DialogResult.Yes;
            }
            foreach (DocNode n in _all) { n.Status = n.IsEmpty ? n.EmptyText : ""; n.StatusBad = false; }

            int toMake = jobs.Count(j => j.Node.Redo || j.Node.SheetsInDrawing != j.Parts);
            bool ran = false;
            DrawingBuilder builder = RunBuilder(Math.Max(1, toMake), "Updating", (b, status, cancelled) =>
            {
                b.Update(_drawing, _models, jobs, _all, _order.Text, deleteOthers, status, cancelled);
                ran = true;
            });
            if (builder == null) return;
            try { ApplyDrawing(true); } catch (Exception ex) { Log.Write("Reading the drawing back failed: " + ex.Message); }
            SheetPlan.Number(_all, _settings.LargeAssemblyRows);
            _grid.Rebuild();
            if (_cancel) { SetStatus("Stopped; the drawing is partly updated and not saved.", true); return; }
            if (ran) Report(builder, "Drawing updated: " + jobs.Count + " sheets, " + toMake + " made" + (deleteOthers ? ", " + going.Count + " deleted" : "") + ".");
        }

        /// <summary>Runs a drawing job with the progress strip, the Cancel button and the rest of the window locked; null when it failed.</summary>
        private DrawingBuilder RunBuilder(int total, string what, Action<DrawingBuilder, Action<string>, Func<bool>> work)
        {
            _busy = true;
            _cancel = false;
            _grid.Enabled = false;
            _generate.Text = "Cancel";
            _close.Enabled = _reload.Enabled = _numbering.Enabled = _modelsButton.Enabled = _typePanel.Enabled = false;
            var builder = new DrawingBuilder(_app, _settings);
            ProgressPanel progress = _progress;
            progress.Begin(total);
            builder.OnSheet = (done, all, detail) =>
            {
                progress.SetProgress(done, all);
                progress.SetStep(done < all ? "Sheet " + (done + 1) + " of " + all : "Saving the drawing…");
                progress.SetDetail(detail);
            };
            builder.OnDetail = d => { progress.SetDetail(d); Application.DoEvents(); };
            try
            {
                work(builder, s =>
                {
                    SetStatus(s, false);
                    if (!s.StartsWith("Sheet ")) progress.SetStep(s);
                    try { _app.StatusBarText = "AutoDraw: " + s; } catch { }
                    Application.DoEvents();
                }, () => { Application.DoEvents(); return _cancel; });
                return builder;
            }
            catch (Exception ex)
            {
                Log.Write(what + " failed: " + ex);
                SetStatus(what + " failed: " + ex.Message, true);
                return null;
            }
            finally
            {
                _busy = false;
                _grid.Enabled = true;
                _generate.Text = _drawing != null ? "Update drawing" : "Generate drawing";
                _close.Enabled = _reload.Enabled = _numbering.Enabled = _modelsButton.Enabled = _typePanel.Enabled = true;
                try { _app.StatusBarText = ""; } catch { }
                progress.End();
                _grid.RefreshValues();
            }
        }

        private void Report(DrawingBuilder builder, string msg)
        {
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
                if (timing == EventTimingEnum.kBefore && !_busy && (_models.Any(a => ReferenceEquals(doc, a)) || (_drawing != null && ReferenceEquals(doc, _drawing)))) BeginInvoke((Action)Close);
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
