using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Settings;
using FieldCodes.Standards;
using Autodesk.AutoCAD.DatabaseServices;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Layers: every layer FieldToFinish draws on, what goes on it, where the name is
    /// configured and how it stands in this drawing -- with a "New Layer" column to send any of
    /// them to the project's layer, and the layer's standard (colour, linetype, lineweight, plot,
    /// transparency, description) beside what the drawing's layer has now.
    ///
    /// The new layer is a project mapping (<see cref="GeneralSettings.LayerMappings"/>) and the
    /// properties are layer standards (<see cref="FtfSettings.LayerStandards"/>), both saved to the
    /// settings level chosen at the bottom of the window. Every command applies the mapping
    /// (<see cref="ProjectLayers"/>); a layer FTF creates takes its standard
    /// (<see cref="CadUtil.EnsureLayerExact(Database, Transaction, string, CadUtil.LayerLook, LayerStandardsSettings)"/>);
    /// a layer already in the drawing changes only through "Update Drawing Layers...", which
    /// previews first. Standards are kept per layer FTF draws on, after the mapping.
    /// </summary>
    internal sealed class LayersPage : SetupPage
    {
        private const int ColFeature = 0, ColType = 1, ColCurrent = 2, ColNew = 3, ColColor = 4, ColLinetype = 5, ColWeight = 6,
                          ColPlot = 7, ColDescription = 8, ColDrawing = 9, ColStatus = 10;

        private static readonly Color Differs = Color.FromArgb(255, 240, 200);
        private static readonly Color Amber = Color.FromArgb(160, 90, 0);
        private static readonly Color ReadOnlyBack = Color.FromArgb(246, 246, 248);

        private sealed class Row
        {
            public LayerUse Use;
        }

        private DataGridView _grid;
        private TextBox _search;
        private ComboBox _family;
        private ComboBox _filter;
        private CheckBox _modifiers;
        private Label _summary;

        // The selected layers' properties.
        private Label _selection;
        private ComboBox _color;
        private ComboBox _linetype;
        private ComboBox _weight;
        private CheckBox _plot;
        private TextBox _transparency;
        private TextBox _description;
        private readonly Dictionary<LayerProperty, Label> _now = new Dictionary<LayerProperty, Label>();
        private readonly HashSet<LayerProperty> _touched = new HashSet<LayerProperty>();
        private CheckBox _applyToNew;
        private bool _loadingEditors;

        private readonly List<Row> _rows = new List<Row>();
        private readonly Dictionary<string, string> _pending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private List<LayerMapping> _loaded = new List<LayerMapping>();
        private LayerStandardsSettings _standards = new LayerStandardsSettings();
        private List<string> _drawingLayers = new List<string>();
        private Dictionary<string, LayerProperties> _drawingProps = new Dictionary<string, LayerProperties>(StringComparer.OrdinalIgnoreCase);
        private bool _loading;
        private bool _filling;
        private bool _settingsWritten;
        private static readonly Font Italic = new Font(SystemFonts.DefaultFont, FontStyle.Italic);

        public override string Title { get { return "Layers"; } }
        public override string AffectedCommands
        {
            get { return "Every command that draws: labels, driplines, tags, schedule, spots, contours, line labels, production tools; FTFLAYERUPDATE; FTFCHECKSTANDARDS"; }
        }

        protected override void BuildBody() { }

        protected override Control BuildFilled()
        {
            var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(new Label
            {
                Text = "New Layer sends a layer to the project's own (typed, or picked from the drawing); it applies to every feature on " +
                       "that layer. Below the table, set what the selected layers look like: a layer FTF creates takes it, and " +
                       "\"Update Drawing Layers...\" brings layers already in the drawing to it after a preview. Saved to the level chosen below.",
                AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 6)
            }, 0, 0);

            var filters = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 2) };
            filters.Controls.Add(new Label { Text = "Search", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            _search = new TextBox { Width = 180, Tag = Ui.SettingsPanel.ViewOnly };
            _search.TextChanged += (s, e) => Fill();
            filters.Controls.Add(_search);
            _family = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, Margin = new Padding(10, 3, 0, 0), Tag = Ui.SettingsPanel.ViewOnly };
            _family.Items.Add("All kinds of objects");
            foreach (LayerFamily f in Enum.GetValues(typeof(LayerFamily))) _family.Items.Add(LayerCatalog.FamilyName(f));
            _family.SelectedIndex = 0;
            _family.SelectedIndexChanged += (s, e) => Fill();
            Tip(_family, "Show one kind of object: point labels, symbols, line labels, trees, dips, leaders, spots, contours, pavement markings, control, tables and legends, easement and boundary, other.");
            filters.Controls.Add(_family);
            _filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Margin = new Padding(6, 3, 0, 0), Tag = Ui.SettingsPanel.ViewOnly };
            _filter.Items.AddRange(new object[]
            {
                "All layers", "Changed (New Layer set)", "Missing from the drawing", "Differs from its standard",
                "Has a layer standard", "No layer standard", "From rules.json", "From settings"
            });
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += (s, e) => Fill();
            filters.Controls.Add(_filter);
            _modifiers = new CheckBox { Text = "Modifier layers (-DEAD, -RMV, -PROT)", AutoSize = true, Margin = new Padding(10, 5, 0, 0), Tag = Ui.SettingsPanel.ViewOnly };
            _modifiers.CheckedChanged += (s, e) => Fill();
            Tip(_modifiers, "A modifier in a point's description (DEAD, REMOVE, PROTECTED) adds a suffix to its label, dripline and symbol layers. Show those layers too.");
            filters.Controls.Add(_modifiers);
            root.Controls.Add(filters, 0, 1);

            var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
            bar.Controls.Add(Button("Pick from Drawing...", "Sends the selected rows to a layer the drawing has.", PickFromDrawing));
            bar.Controls.Add(Button("Keep Current", "Clears New Layer on the selected rows: FTF uses the current layer.", () => SetSelected(null)));
            bar.Controls.Add(Button("Create Missing", "Creates the selected layers that are not in the drawing yet, each with its layer standard " +
                                                     "(or the colour and lineweight of its family when it has none). Nothing else in the drawing changes.", CreateMissing));
            bar.Controls.Add(Button("Edit Feature Layer", "Opens the page that sets this feature's own layer -- for a change to one feature only.", EditFeature));
            bar.Controls.Add(Button("Update Drawing Layers...", "Previews, then applies, the layer standards to the layers FTF uses that are already in this " +
                                                                "drawing. Layer properties only: no object moves or changes, and other layers are not touched.", UpdateDrawingLayers));
            bar.Controls.Add(Button("Check Standards...", "Checks every layer, linetype, style and block the settings and rules name against this drawing, " +
                                                          "and offers a fix for each problem. Nothing changes unless you pick a fix.", CheckStandards));
            bar.Controls.Add(Button("All Standards...", "Every layer standard as a list, including patterns (V-*-TEXT-E) and layers not in this table.", EditAllStandards));
            root.Controls.Add(bar, 0, 2);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                ShowCellToolTips = true
            };
            AddColumn("Feature", 20, true);
            AddColumn("Object Type", 13, true);
            AddColumn("Current Layer", 15, true);
            AddColumn("New Layer", 14, false);
            AddColumn("Color", 7, true);
            AddColumn("Linetype", 9, true);
            AddColumn("Lineweight", 6, true);
            AddColumn("Plot", 5, true);
            AddColumn("Description", 9, true);
            AddColumn("In Drawing Now", 14, true);
            AddColumn("Status", 11, true);
            _grid.CellEndEdit += (s, e) => OnEdited(e.RowIndex);
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == ColNew) _grid.BeginEdit(true); };
            _grid.SelectionChanged += (s, e) => LoadEditors();
            _grid.EditingControlShowing += (s, e) =>
            {
                var box = e.Control as TextBox;
                if (box == null) return;
                box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                box.AutoCompleteSource = AutoCompleteSource.CustomSource;
                var source = new AutoCompleteStringCollection();
                source.AddRange(_drawingLayers.ToArray());
                box.AutoCompleteCustomSource = source;
            };
            root.Controls.Add(_grid, 0, 3);

            root.Controls.Add(BuildEditors(), 0, 4);

            _summary = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0), MaximumSize = new Size(900, 0) };
            root.Controls.Add(_summary, 0, 5);
            return root;
        }

        private static Button Button(string text, string tip, Action click)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 2, 6, 0) };
            b.Click += (s, e) => click();
            Tip(b, tip);
            return b;
        }

        private void AddColumn(string header, float weight, bool readOnly)
        {
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            if (readOnly) column.DefaultCellStyle.BackColor = ReadOnlyBack;
            else column.DefaultCellStyle.Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            _grid.Columns.Add(column);
        }

        /// <summary>The selected layers' standard, each property beside what the drawing has now.</summary>
        private Control BuildEditors()
        {
            var box = new GroupBox { Text = "Layer standard for the selected layer(s)", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 4, 8, 6) };
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 6, RowCount = 5 };
            for (var i = 0; i < 2; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            }

            _selection = new Label { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(0, 2, 0, 4) };
            table.Controls.Add(_selection, 0, 0);
            table.SetColumnSpan(_selection, 6);

            _color = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 130, Tag = Ui.SettingsPanel.ViewOnly };
            _color.Items.Add(string.Empty);
            for (short i = 1; i <= 9; i++) _color.Items.Add(LayerColor.FromAci(i).Describe());
            for (short i = 250; i <= 255; i++) _color.Items.Add(LayerColor.FromAci(i).Describe());
            Watch(_color, LayerProperty.Color);
            Tip(_color, "An AutoCAD colour 1-255, a colour name (red, yellow, green, cyan, blue, magenta, white), or a true colour r,g,b. Blank: not set.");

            _linetype = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown, Width = 160, Tag = Ui.SettingsPanel.ViewOnly,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.CustomSource
            };
            Watch(_linetype, LayerProperty.Linetype);
            Tip(_linetype, "A linetype the drawing has, or type one: FTF loads it from acad.lin when it applies the standard, and says so if it cannot. Blank: not set.");

            _weight = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Tag = Ui.SettingsPanel.ViewOnly };
            _weight.Items.Add("(not set)");
            foreach (var w in LayerStandards.LineWeightChoices()) _weight.Items.Add(w);
            Watch(_weight, LayerProperty.LineWeight);

            _plot = new CheckBox { Text = "Plot", ThreeState = true, AutoSize = true, CheckState = CheckState.Indeterminate, Tag = Ui.SettingsPanel.ViewOnly };
            _plot.CheckStateChanged += (s, e) => Touch(LayerProperty.Plot);
            Tip(_plot, "Ticked: the layer plots. Clear: it does not (QC and construction layers). Filled: not set.");

            _transparency = new TextBox { Width = 60, Tag = Ui.SettingsPanel.ViewOnly };
            _transparency.TextChanged += (s, e) => Touch(LayerProperty.Transparency);
            Tip(_transparency, "0 (opaque) to 90 percent. Blank: not set.");

            _description = new TextBox { Width = 220, Tag = Ui.SettingsPanel.ViewOnly };
            _description.TextChanged += (s, e) => Touch(LayerProperty.Description);

            AddEditor(table, 1, 0, "Color", _color, LayerProperty.Color);
            AddEditor(table, 2, 0, "Linetype", _linetype, LayerProperty.Linetype);
            AddEditor(table, 3, 0, "Lineweight", _weight, LayerProperty.LineWeight);
            AddEditor(table, 1, 3, "Plot", _plot, LayerProperty.Plot);
            AddEditor(table, 2, 3, "Transparency", _transparency, LayerProperty.Transparency);
            AddEditor(table, 3, 3, "Description", _description, LayerProperty.Description);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
            buttons.Controls.Add(Button("Apply to Selected", "Writes the properties you changed above to the standard of every selected layer.", ApplyEditors));
            buttons.Controls.Add(Button("Copy from Drawing", "Makes each selected layer's standard what its layer in this drawing has now.", CopyFromDrawing));
            buttons.Controls.Add(Button("Clear Standard", "Removes the selected layers' own standards (a pattern standard that matches them still applies).", ClearStandard));
            _applyToNew = new CheckBox { Text = "Layers FTF creates take their standard", AutoSize = true, Margin = new Padding(16, 6, 0, 0) };
            _applyToNew.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                _standards.ApplyToNewLayers = _applyToNew.Checked;
                MarkChanged();
            };
            Tip(_applyToNew, "On: a layer FTF has to create gets its standard's colour, linetype, lineweight, plot, transparency and description. " +
                             "Off: it takes the look of its family, as before. Layers already in the drawing change only through Update Drawing Layers.");
            buttons.Controls.Add(_applyToNew);
            table.Controls.Add(buttons, 0, 4);
            table.SetColumnSpan(buttons, 6);

            box.Controls.Add(table);
            return box;
        }

        private void AddEditor(TableLayoutPanel table, int row, int column, string caption, Control editor, LayerProperty property)
        {
            table.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(column == 0 ? 0 : 18, 6, 6, 0) }, column, row);
            editor.Margin = new Padding(0, 2, 6, 2);
            table.Controls.Add(editor, column + 1, row);
            var now = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0) };
            _now[property] = now;
            table.Controls.Add(now, column + 2, row);
        }

        private void Watch(ComboBox combo, LayerProperty property)
        {
            combo.TextChanged += (s, e) => Touch(property);
            combo.SelectedIndexChanged += (s, e) => Touch(property);
        }

        private void Touch(LayerProperty property)
        {
            if (!_loadingEditors) _touched.Add(property);
        }

        // ------------------------------------------------------------ data

        public override void LoadFrom(FtfSettings s)
        {
            _loading = true;
            try
            {
                _loaded = new List<LayerMapping>(s.General.LayerMappings ?? new List<LayerMapping>());
                _pending.Clear();
                foreach (var m in _loaded.Where(m => m != null && !string.IsNullOrWhiteSpace(m.From)))
                    if (!_pending.ContainsKey(m.From.Trim())) _pending[m.From.Trim()] = (m.To ?? string.Empty).Trim();

                _standards = (s.LayerStandards ?? new LayerStandardsSettings()).Clone();
                if (_applyToNew != null) _applyToNew.Checked = _standards.ApplyToNewLayers;

                _drawingLayers = (Context.Drawing != null ? Context.Drawing.Layers : new List<string>())
                                 .Where(l => !string.IsNullOrWhiteSpace(l)).OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
                ReadDrawing();
                _rows.Clear();
                foreach (var use in LayerCatalog.Build(Context.Rules, s, _drawingLayers)
                                                .OrderBy(u => u.Family).ThenBy(u => u.Feature, StringComparer.OrdinalIgnoreCase))
                    _rows.Add(new Row { Use = use });
                FillLinetypes();
                Fill();
            }
            finally { _loading = false; }
        }

        /// <summary>What the drawing's layers look like now, read fresh (after a fix or an update too).</summary>
        private void ReadDrawing()
        {
            var props = new Dictionary<string, LayerProperties>(StringComparer.OrdinalIgnoreCase);
            try
            {
                LayerStandardsCad.InActiveDrawing((db, tr) =>
                {
                    foreach (var layer in LayerStandardsCad.ReadLayers(db, tr)) props[layer.Name] = layer;
                });
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // The table still works from the names the window read when it opened.
            }
            _drawingProps = props;
            foreach (var name in props.Keys)
                if (!_drawingLayers.Contains(name, StringComparer.OrdinalIgnoreCase)) _drawingLayers.Add(name);
            _drawingLayers.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private void FillLinetypes()
        {
            if (_linetype == null) return;
            var drawing = (Context.Drawing != null ? Context.Drawing.Linetypes : new List<string>())
                          .Where(l => !string.Equals(l, "ByLayer", StringComparison.OrdinalIgnoreCase) && !string.Equals(l, "ByBlock", StringComparison.OrdinalIgnoreCase))
                          .ToList();
            _loadingEditors = true;
            _linetype.Items.Clear();
            _linetype.Items.Add(string.Empty);
            foreach (var l in drawing) _linetype.Items.Add(l);
            var source = new AutoCompleteStringCollection();
            source.AddRange(drawing.ToArray());
            IList<string> lin = null;
            try { lin = LayerStandardsCad.AcadLinLinetypes(null); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { lin = null; }
            if (lin != null) source.AddRange(lin.Where(l => !drawing.Contains(l, StringComparer.OrdinalIgnoreCase)).ToArray());
            _linetype.AutoCompleteCustomSource = source;
            _loadingEditors = false;
        }

        /// <summary>The layer FTF draws on for this row: its New Layer, or the current one.</summary>
        private string EffectiveOf(Row row)
        {
            var mapped = NewFor(row.Use.Layer);
            return string.IsNullOrEmpty(mapped) ? row.Use.Layer : mapped;
        }

        private void Fill()
        {
            if (_grid == null) return;
            // The selection and scroll position survive a refill (after an edit, a fix or a filter change).
            var keep = new HashSet<Row>(Selected());
            var scroll = _grid.FirstDisplayedScrollingRowIndex;
            _filling = true;
            try { FillRows(keep); }
            finally { _filling = false; }
            if (scroll >= 0 && scroll < _grid.Rows.Count)
            {
                try { _grid.FirstDisplayedScrollingRowIndex = scroll; }
                catch (InvalidOperationException) { }
            }
            LoadEditors();
        }

        private void FillRows(HashSet<Row> keep)
        {
            var text = (_search.Text ?? string.Empty).Trim();
            var family = _family.SelectedIndex - 1;
            var resolved = new Dictionary<string, ResolvedLayerStandard>(StringComparer.OrdinalIgnoreCase);
            Func<string, ResolvedLayerStandard> standardOf = layer =>
            {
                ResolvedLayerStandard r;
                if (!resolved.TryGetValue(layer, out r)) resolved[layer] = r = _standards.For(layer);
                return r;
            };

            _grid.Rows.Clear();
            foreach (var row in _rows)
            {
                var u = row.Use;
                if (u.Modifier != null && !_modifiers.Checked) continue;
                if (family >= 0 && (int)u.Family != family) continue;
                var mapped = NewFor(u.Layer);
                var effective = EffectiveOf(row);
                var status = Status(u.Layer);
                var standard = standardOf(effective);
                LayerProperties now;
                _drawingProps.TryGetValue(effective, out now);
                var differences = now == null ? new List<LayerPropertyChange>() : LayerStandards.Differences(now, standard).ToList();

                if (text.Length > 0 && (u.Feature + " " + u.ObjectType + " " + u.Layer + " " + mapped + " " + LayerCatalog.FamilyName(u.Family))
                        .IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                switch (_filter.SelectedIndex)
                {
                    case 1: if (string.IsNullOrEmpty(mapped)) continue; break;
                    case 2: if (status == LayerStatus.Exists || status == LayerStatus.MappedExists) continue; break;
                    case 3: if (differences.Count == 0) continue; break;
                    case 4: if (standard.IsEmpty) continue; break;
                    case 5: if (!standard.IsEmpty) continue; break;
                    case 6: if (u.Source != LayerSource.Rules) continue; break;
                    case 7: if (u.Source != LayerSource.Settings) continue; break;
                }

                var v = standard.Values;
                var statusText = u.OnlyIfPresent && (status != LayerStatus.Exists && status != LayerStatus.MappedExists)
                    ? "Not in drawing; default label layer used"
                    : LayerCatalog.Describe(status);
                var i = _grid.Rows.Add(u.Feature, u.ObjectType, u.Layer, mapped,
                                       LayerStandards.Display(v, LayerProperty.Color), v.Text(LayerProperty.Linetype),
                                       LayerStandards.Display(v, LayerProperty.LineWeight), v.Text(LayerProperty.Plot), v.Text(LayerProperty.Description),
                                       now == null ? "(not in drawing)" : now.Summary(), statusText);
                var gridRow = _grid.Rows[i];
                gridRow.Tag = row;
                gridRow.Cells[ColFeature].ToolTipText = LayerCatalog.FamilyName(u.Family) + "\nSet in: " + u.EditedIn;
                gridRow.Cells[ColType].ToolTipText = u.ObjectType;
                var shared = _rows.Count(r => string.Equals(r.Use.Layer, u.Layer, StringComparison.OrdinalIgnoreCase)) - 1;
                if (shared > 0)
                    gridRow.Cells[ColCurrent].ToolTipText = "Also used by " + shared + " other row(s): a New Layer or a standard moves them together.";
                if (status == LayerStatus.WillBeCreated || status == LayerStatus.SimilarExists)
                    gridRow.Cells[ColStatus].Style.ForeColor = Amber;
                Decorate(gridRow, ColColor, LayerProperty.Color, standard, now, differences);
                Decorate(gridRow, ColLinetype, LayerProperty.Linetype, standard, now, differences);
                Decorate(gridRow, ColWeight, LayerProperty.LineWeight, standard, now, differences);
                Decorate(gridRow, ColPlot, LayerProperty.Plot, standard, now, differences);
                Decorate(gridRow, ColDescription, LayerProperty.Description, standard, now, differences);
                if (differences.Count > 0)
                    gridRow.Cells[ColDrawing].ToolTipText = "Differs from its standard: " +
                        string.Join("; ", differences.Select(d => LayerStandards.PropertyName(d.Property) + " " + d.From + " -> " + d.To).ToArray()) +
                        "\nUpdate Drawing Layers... previews and applies it.";
                else if (now != null) gridRow.Cells[ColDrawing].ToolTipText = effective + " in this drawing.";
                if (keep.Count > 0) gridRow.Selected = keep.Contains(row);
            }

            var changed = _pending.Count(p => !string.IsNullOrEmpty(p.Value));
            var distinct = _rows.Select(EffectiveOf).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var missing = _rows.Where(r => !r.Use.OnlyIfPresent).Select(r => r.Use.Layer).Distinct(StringComparer.OrdinalIgnoreCase)
                               .Count(l => Status(l) == LayerStatus.WillBeCreated || Status(l) == LayerStatus.SimilarExists);
            var differing = distinct.Count(l =>
            {
                LayerProperties now;
                return _drawingProps.TryGetValue(l, out now) && LayerStandards.Differences(now, standardOf(l)).Count > 0;
            });
            var withStandard = distinct.Count(l => !standardOf(l).IsEmpty);
            _summary.Text = string.Format(
                "{0} row(s), {1} distinct layer(s); {2} sent to a project layer; {3} not in this drawing yet; {4} with a layer standard; " +
                "{5} in this drawing differ from their standard{6}.",
                _rows.Count, distinct.Count, changed, missing, withStandard, differing,
                differing > 0 ? " (Update Drawing Layers... previews the change)" : string.Empty);
        }

        /// <summary>A property cell: italic when a pattern sets it, amber when the drawing's layer differs.</summary>
        private static void Decorate(DataGridViewRow row, int column, LayerProperty property, ResolvedLayerStandard standard,
                                     LayerProperties now, IList<LayerPropertyChange> differences)
        {
            var cell = row.Cells[column];
            var source = standard.Source(property);
            var tip = source == null ? "No standard: the drawing's layer is left as it is."
                    : standard.FromPattern(property) ? "From the pattern standard " + source + "." : "This layer's own standard.";
            if (now != null) tip += "\nIn the drawing now: " + now.Text(property);
            cell.ToolTipText = tip;
            if (source != null && standard.FromPattern(property))
                cell.Style.Font = Italic;
            if (differences.Any(d => d.Property == property))
            {
                cell.Style.BackColor = Differs;
                cell.Style.ForeColor = Amber;
            }
        }

        private string NewFor(string layer)
        {
            string to;
            return _pending.TryGetValue(layer ?? string.Empty, out to) ? to : string.Empty;
        }

        private IEnumerable<LayerMapping> Mappings()
        {
            return _pending.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => new LayerMapping { From = p.Key, To = p.Value });
        }

        private LayerStatus Status(string layer)
        {
            return LayerCatalog.StatusOf(layer, _drawingLayers, Mappings());
        }

        private void OnEdited(int rowIndex)
        {
            if (_loading || rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
            var row = _grid.Rows[rowIndex].Tag as Row;
            if (row == null) return;
            var value = Convert.ToString(_grid.Rows[rowIndex].Cells[ColNew].Value ?? string.Empty).Trim();
            Set(row.Use.Layer, value);
            BeginInvokeFill();
        }

        private void BeginInvokeFill()
        {
            // Refilling inside the edit event would re-enter the grid.
            if (_grid.IsHandleCreated) _grid.BeginInvoke((Action)Fill);
            else Fill();
        }

        private void Set(string layer, string to)
        {
            if (string.IsNullOrWhiteSpace(layer)) return;
            if (string.IsNullOrWhiteSpace(to) || string.Equals(to, layer, StringComparison.OrdinalIgnoreCase)) _pending.Remove(layer);
            else _pending[layer] = to.Trim();
            MarkChanged();
        }

        private IEnumerable<Row> Selected()
        {
            return _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as Row).Where(r => r != null);
        }

        /// <summary>The layers the selected rows draw on, after New Layer, once each.</summary>
        private List<string> SelectedLayers()
        {
            return Selected().Select(EffectiveOf).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void SetSelected(string to)
        {
            foreach (var row in Selected().ToList()) Set(row.Use.Layer, to);
            Fill();
        }

        // ------------------------------------------------------------ the standard editors

        /// <summary>Shows the selected layers' own standards in the editors (blank where they differ),
        /// with what the drawing has beside each.</summary>
        private void LoadEditors()
        {
            if (_color == null || _filling) return;
            _loadingEditors = true;
            try
            {
                _touched.Clear();
                var layers = SelectedLayers();
                var enabled = layers.Count > 0;
                foreach (var c in new Control[] { _color, _linetype, _weight, _plot, _transparency, _description }) c.Enabled = enabled;
                if (!enabled)
                {
                    _selection.Text = "Select one or more rows to set what their layers look like.";
                    SetEditors(null);
                    foreach (var l in _now.Values) l.Text = string.Empty;
                    return;
                }

                var standards = layers.Select(l => _standards.Exact(l) ?? new LayerStandard { Name = l }).ToList();
                var first = standards[0];
                var common = new LayerStandard { Name = first.Name };
                foreach (var p in LayerStandards.Properties)
                    if (standards.All(s => s.Text(p) == first.Text(p))) common.CopyFrom(first, p);
                SetEditors(common);

                if (layers.Count == 1)
                {
                    var resolved = _standards.For(layers[0]);
                    var patterns = LayerStandards.Properties.Where(resolved.FromPattern).Select(p => LayerStandards.PropertyName(p) + " from " + resolved.Source(p)).ToList();
                    _selection.Text = layers[0] + (_standards.Exact(layers[0]) != null ? " - its own standard" : " - no standard of its own") +
                                      (patterns.Count > 0 ? "; pattern: " + string.Join(", ", patterns.ToArray()) : string.Empty);
                }
                else _selection.Text = layers.Count + " layers selected - a blank box means they differ; only what you change is applied to all.";

                foreach (var p in LayerStandards.Properties)
                {
                    var values = layers.Select(l =>
                    {
                        LayerProperties now;
                        return _drawingProps.TryGetValue(l, out now) ? now.Text(p) : "(not in drawing)";
                    }).Distinct().ToList();
                    _now[p].Text = "In drawing: " + (values.Count == 1 ? values[0] : "(varies)");
                }
            }
            finally { _loadingEditors = false; }
        }

        private void SetEditors(LayerStandard s)
        {
            _color.Text = s == null ? string.Empty : ColorText(s.Color);
            _linetype.Text = s == null ? string.Empty : s.Text(LayerProperty.Linetype);
            int weight;
            string why;
            _weight.SelectedIndex = s != null && LayerStandards.TryParseLineWeight(s.LineWeight, out weight, out why)
                ? Math.Max(0, _weight.Items.IndexOf(LayerStandards.FormatLineWeight(weight)))
                : 0;
            _plot.CheckState = s == null || !s.Plot.HasValue ? CheckState.Indeterminate : s.Plot.Value ? CheckState.Checked : CheckState.Unchecked;
            _transparency.Text = s == null ? string.Empty : s.Text(LayerProperty.Transparency);
            _description.Text = s == null ? string.Empty : s.Text(LayerProperty.Description);
        }

        private static string ColorText(string standard)
        {
            LayerColor colour;
            string why;
            return LayerStandards.TryParseColor(standard, out colour, out why) ? colour.Describe() : (standard ?? string.Empty);
        }

        /// <summary>"3 (green)" from the dropdown is "3".</summary>
        private static string ColorValue(string text)
        {
            var t = (text ?? string.Empty).Trim();
            var paren = t.IndexOf(" (", StringComparison.Ordinal);
            return paren > 0 && t.EndsWith(")", StringComparison.Ordinal) ? t.Substring(0, paren).Trim() : t;
        }

        /// <summary>Reads the editors the user changed into a standard; false (with the reason) when one is not valid.</summary>
        private bool ReadEditors(LayerStandard into, out string problem)
        {
            problem = null;
            string why;
            if (_touched.Contains(LayerProperty.Color))
            {
                var colour = ColorValue(_color.Text);
                LayerColor parsed;
                if (colour.Length > 0 && !LayerStandards.TryParseColor(colour, out parsed, out why)) { problem = why; return false; }
                into.Color = colour.Length == 0 ? null : colour;
            }
            if (_touched.Contains(LayerProperty.Linetype))
            {
                var linetype = (_linetype.Text ?? string.Empty).Trim();
                if (linetype.Length > 0 && !LayerStandards.IsValidLinetypeName(linetype, out why)) { problem = why; return false; }
                into.Linetype = linetype.Length == 0 ? null : linetype;
            }
            if (_touched.Contains(LayerProperty.LineWeight))
                into.LineWeight = _weight.SelectedIndex <= 0 ? null : _weight.SelectedItem.ToString();
            if (_touched.Contains(LayerProperty.Plot))
                into.Plot = _plot.CheckState == CheckState.Indeterminate ? (bool?)null : _plot.CheckState == CheckState.Checked;
            if (_touched.Contains(LayerProperty.Transparency))
            {
                var t = (_transparency.Text ?? string.Empty).Trim();
                int percent;
                if (t.Length > 0 && (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out percent) || percent < 0 || percent > 90))
                { problem = "transparency must be a whole number from 0 to 90"; return false; }
                into.Transparency = t.Length == 0 ? (int?)null : int.Parse(t, CultureInfo.InvariantCulture);
            }
            if (_touched.Contains(LayerProperty.Description))
            {
                var d = (_description.Text ?? string.Empty).Trim();
                into.Description = d.Length == 0 ? null : d;
            }
            return true;
        }

        private void ApplyEditors()
        {
            var layers = SelectedLayers();
            if (layers.Count == 0) { MessageBox.Show(_grid, "Select one or more rows first.", "Layers"); return; }
            if (_touched.Count == 0) { MessageBox.Show(_grid, "Change a property above first; only what you change is applied.", "Layers"); return; }
            var probe = new LayerStandard();
            string problem;
            if (!ReadEditors(probe, out problem)) { MessageBox.Show(_grid, Capital(problem) + ".", "Layers", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            foreach (var layer in layers)
            {
                var standard = _standards.ExactOrNew(layer);
                foreach (var p in _touched) standard.CopyFrom(probe, p);
            }
            _standards.RemoveEmpty();
            MarkChanged();
            Fill();
        }

        private void CopyFromDrawing()
        {
            var layers = SelectedLayers().Where(l => _drawingProps.ContainsKey(l)).ToList();
            if (layers.Count == 0) { MessageBox.Show(_grid, "None of the selected layers is in this drawing.", "Copy from Drawing"); return; }
            if (MessageBox.Show(_grid, "Make the standard of " + layers.Count + " layer(s) what the drawing has now?\n\n" +
                                       string.Join("\n", layers.Take(15).ToArray()), "Copy from Drawing",
                                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            foreach (var layer in layers)
            {
                var copy = LayerStandards.FromDrawing(_drawingProps[layer]);
                var standard = _standards.ExactOrNew(layer);
                foreach (var p in LayerStandards.Properties) standard.CopyFrom(copy, p);
            }
            MarkChanged();
            Fill();
        }

        private void ClearStandard()
        {
            var layers = SelectedLayers().Where(l => _standards.Exact(l) != null).ToList();
            if (layers.Count == 0) { MessageBox.Show(_grid, "None of the selected layers has a standard of its own.", "Clear Standard"); return; }
            foreach (var layer in layers) _standards.RemoveExact(layer);
            MarkChanged();
            Fill();
        }

        private static string Capital(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        // ------------------------------------------------------------ actions on the drawing

        private void PickFromDrawing()
        {
            if (!Selected().Any()) { MessageBox.Show(_grid, "Select one or more rows first.", "Layers"); return; }
            using (var form = new Form
            {
                Text = "Layers in this drawing", Width = 380, Height = 460, StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.SizableToolWindow, MinimizeBox = false, MaximizeBox = false
            })
            {
                var filter = new TextBox { Dock = DockStyle.Top };
                var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
                Action refill = () =>
                {
                    list.BeginUpdate();
                    list.Items.Clear();
                    foreach (var l in _drawingLayers.Where(l => l.IndexOf(filter.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0))
                        list.Items.Add(l);
                    list.EndUpdate();
                };
                filter.TextChanged += (s, e) => refill();
                var ok = new Button { Text = "Use Layer", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                list.DoubleClick += (s, e) => { if (list.SelectedItem != null) form.DialogResult = DialogResult.OK; };
                form.Controls.Add(list);
                form.Controls.Add(filter);
                form.Controls.Add(ok);
                form.AcceptButton = ok;
                refill();
                if (form.ShowDialog(_grid) == DialogResult.OK && list.SelectedItem != null)
                    SetSelected(list.SelectedItem.ToString());
            }
        }

        /// <summary>Creates the selected layers that are not in the drawing, under the name FTF
        /// will use (the New Layer when set), with their standard (from this page, saved or not)
        /// or their family's look. Nothing else changes.</summary>
        private void CreateMissing()
        {
            var names = Selected().Where(r => !r.Use.OnlyIfPresent).Select(EffectiveOf)
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .Where(n => !_drawingLayers.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            if (names.Count == 0) { MessageBox.Show(_grid, "The selected layers are already in the drawing.", "Layers"); return; }
            string problem;
            var bad = names.FirstOrDefault(n => !LayerMap.IsValidName(n, out problem));
            if (bad != null) { LayerMap.IsValidName(bad, out problem); MessageBox.Show(_grid, "\"" + bad + "\": " + problem + ".", "Layers"); return; }
            if (MessageBox.Show(_grid, "Create " + names.Count + " layer(s) in this drawing, each with its layer standard?\n\n" + string.Join("\n", names.Take(20).ToArray()),
                                "Create Missing Layers", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            var failures = new List<string>();
            foreach (var n in names)
            {
                // Straight to the table entry, not through the project mapping: this is the layer the user asked for.
                var outcome = LayerStandardsCad.CreateLayerNow(n, _standards);
                if (!outcome.Done) failures.Add(outcome.Message);
            }
            ReadDrawing();
            Fill();
            if (failures.Count > 0) MessageBox.Show(_grid, string.Join("\n", failures.ToArray()), "Create Missing Layers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void EditFeature()
        {
            var row = Selected().FirstOrDefault();
            if (row == null) return;
            if (Context.Navigate != null && !string.IsNullOrEmpty(row.Use.SettingsSection))
                Context.Navigate(row.Use.SettingsSection);
            else
                MessageBox.Show(_grid, "This layer is set in " + row.Use.EditedIn + ".", "Edit Feature Layer");
        }

        /// <summary>Previews, then applies on confirmation, this page's standards to the layers FTF uses
        /// that are in the drawing. Layer properties only.</summary>
        private void UpdateDrawingLayers()
        {
            ReadDrawing();
            var settings = Context.Resolution != null ? Context.Resolution.Settings : new FtfSettings();
            var plan = LayerStandardsCad.Plan(Context.Rules, settings, _drawingProps.Values.ToList(), _standards, Mappings());
            if (plan.Changes.Count == 0)
            {
                MessageBox.Show(_grid, "Every layer FTF uses in this drawing already matches its standard" +
                                       (plan.LayersWithoutStandard > 0 ? " (" + plan.LayersWithoutStandard + " have no standard)" : string.Empty) + ". Nothing to change." +
                                       (plan.Problems.Count > 0 ? "\n\nNot applied: " + string.Join("; ", plan.Problems.Take(6).ToArray()) : string.Empty),
                                "Update Drawing Layers");
                Fill();
                return;
            }

            IList<LayerPropertyChange> chosen;
            using (var form = new LayerUpdatePreviewForm(plan, "this page's standards (saved or not)"))
            {
                if (form.ShowDialog(_grid) != DialogResult.OK) return;
                chosen = form.Chosen;
            }
            if (chosen.Count == 0) return;

            LayerStandardsCad.UpdateResult result = null;
            try
            {
                LayerStandardsCad.InActiveDrawing((db, tr) => result = LayerStandardsCad.ApplyChanges(db, tr, chosen, _standards));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                MessageBox.Show(_grid, "The layers were not changed: " + ex.Message, "Update Drawing Layers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ReadDrawing();
            Fill();
            if (result != null)
            {
                LayerStandardsCad.Say("FTF Settings > Layers: " + LayerStandardsCad.Describe(result).Replace("\n", "\n  "));
                MessageBox.Show(_grid, LayerStandardsCad.Describe(result), "Update Drawing Layers");
            }
        }

        /// <summary>Check Standards on the settings as they are in this window (this page's layer
        /// mappings and standards included, saved or not).</summary>
        private void CheckStandards()
        {
            var settings = Context.Resolution != null ? Context.Resolution.Settings : null;
            if (settings == null) return;
            Push(settings);
            DrawingNames names = null;
            try
            {
                if (!LayerStandardsCad.InActiveDrawing((db, tr) => names = LayerStandardsCad.ReadNames(db, tr)))
                {
                    MessageBox.Show(_grid, "No drawing is open.", "Check Standards");
                    return;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                MessageBox.Show(_grid, "Could not read the drawing: " + ex.Message, "Check Standards");
                return;
            }

            var issues = StandardsCheck.Run(Context.Rules, settings, names);
            _settingsWritten = false;
            using (var form = new StandardsCheckForm(issues, new PageFixer(this, settings),
                                                     "the settings in this window (saved or not)" + (Context.Rules == null ? "; rules.json could not be read" : string.Empty)))
                form.ShowDialog(_grid);

            if (_settingsWritten)
            {
                // A style or linetype was written to the settings, which already hold this page's
                // mappings and standards: every page shows it, and it is saved with Save.
                if (Context.Reload != null) Context.Reload();
                else LoadFrom(settings);
                MarkChanged();
            }
            else
            {
                ReadDrawing();
                Fill();
            }
        }

        /// <summary>This page's mappings and standards into a settings object (no validation messages).</summary>
        private void Push(FtfSettings s)
        {
            SaveTo(s, new List<string>());
        }

        /// <summary>Every standard as one list, patterns included.</summary>
        private void EditAllStandards()
        {
            using (var form = new AllStandardsForm(_standards, Context.Drawing != null ? Context.Drawing.Linetypes : new List<string>()))
            {
                if (form.ShowDialog(_grid) != DialogResult.OK) return;
                _standards.Layers = form.Result;
                MarkChanged();
                Fill();
            }
        }

        /// <summary>Fixes from the Check Standards window, into this window's settings.</summary>
        private sealed class PageFixer : IStandardsFixer
        {
            private readonly LayersPage _page;
            private readonly FtfSettings _settings;
            private bool _warned;

            public PageFixer(LayersPage page, FtfSettings settings)
            {
                _page = page;
                _settings = settings;
            }

            public FixOutcome CreateLayer(StandardsIssue issue)
            {
                var outcome = LayerStandardsCad.CreateLayerNow(issue.Name, _page._standards);
                if (outcome.Done && !_page._drawingLayers.Contains(issue.Name, StringComparer.OrdinalIgnoreCase)) _page._drawingLayers.Add(issue.Name);
                return outcome;
            }

            public FixOutcome LoadLinetype(StandardsIssue issue)
            {
                return LayerStandardsCad.LoadLinetypeNow(issue.Name);
            }

            public FixOutcome MapLayer(StandardsIssue issue, string target)
            {
                var from = issue.ConfiguredLayer ?? issue.Name;
                _page.Set(from, target);
                LayerMap.Set(_settings.General.LayerMappings, from, target);
                return FixOutcome.Ok("New Layer for " + from + " is " + target + " (saved with Save).");
            }

            public FixOutcome UseExisting(StandardsIssue issue, string name)
            {
                if (issue.Setting == null) return FixOutcome.Failed("This one is not a setting FTF can write.");
                if (!_warned)
                {
                    if (MessageBox.Show(_page._grid,
                            "This writes \"" + name + "\" to " + string.Join("; ", issue.Setting.Fields.ToArray()) + " in the settings being edited " +
                            "(saved when you click Save).\n\nWhen this window closes, the settings pages are refreshed to show it: anything typed on " +
                            "another page and not yet saved is reset. Continue?",
                            "Check Standards", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                        return FixOutcome.Failed("Not changed.");
                    _warned = true;
                }
                issue.Setting.Write(name);
                _page._settingsWritten = true;
                return FixOutcome.Ok(string.Join("; ", issue.Setting.Fields.ToArray()) + " = " + name + " (saved with Save).");
            }
        }

        // ------------------------------------------------------------ saving

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            foreach (var p in _pending.Where(p => !string.IsNullOrEmpty(p.Value)))
            {
                string why;
                if (!LayerMap.IsValidName(p.Value, out why)) problems.Add("Layers: \"" + p.Value + "\" for " + p.Key + ": " + why + ".");
            }
            // Mappings for layers not in the table (an older profile's, a production layer
            // FTF no longer lists) are kept as they were.
            var listed = new HashSet<string>(_rows.Select(r => r.Use.Layer), StringComparer.OrdinalIgnoreCase);
            var result = _loaded.Where(m => m != null && !string.IsNullOrWhiteSpace(m.From) && !listed.Contains(m.From.Trim())
                                            && !_pending.ContainsKey(m.From.Trim())).ToList();
            foreach (var p in _pending)
                LayerMap.Set(result, p.Key, p.Value);
            s.General.LayerMappings = result;

            // The standards are validated with the rest of the settings when they are saved.
            _standards.RemoveEmpty();
            s.LayerStandards = _standards.Clone();
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.General.LayerMappings = new List<LayerMapping>();
            s.LayerStandards = new LayerStandardsSettings();
            LoadFrom(s);
            MarkChanged();
        }
    }

    /// <summary>
    /// Every layer standard as an editable list: exact layer names and patterns ("V-*-TEXT-E",
    /// "FTF-QC-*"), including layers the Layers table does not list. An exact name beats a
    /// pattern, property by property; among patterns the more specific wins.
    /// </summary>
    internal sealed class AllStandardsForm : Form
    {
        private readonly DataGridView _grid;

        public AllStandardsForm(LayerStandardsSettings standards, IList<string> linetypes)
        {
            Text = "Layer Standards";
            Width = 960;
            Height = 520;
            MinimumSize = new Size(640, 320);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var note = new Label
            {
                Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 6, 8, 0),
                Text = "One row per layer, or per pattern with * and ? (V-*-TEXT-E covers every text layer). A layer's own row beats a pattern, " +
                       "property by property. Blank cells are not set: the drawing's layer is left as it is. Colour: 1-255, a name or r,g,b; " +
                       "lineweight: Default or 0.00-2.11; plot: Yes, No or blank; transparency: 0-90."
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Layer or pattern", FillWeight = 26 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Color", FillWeight = 10 });
            // Typed, with the drawing's linetypes suggested: one the drawing lacks is loaded from acad.lin when applied.
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Linetype", FillWeight = 14 });
            var known = (linetypes ?? new List<string>()).Where(l => !string.Equals(l, "ByLayer", StringComparison.OrdinalIgnoreCase) &&
                                                                     !string.Equals(l, "ByBlock", StringComparison.OrdinalIgnoreCase)).ToArray();
            _grid.EditingControlShowing += (s, e) =>
            {
                var box = e.Control as TextBox;
                if (box == null) return;
                if (_grid.CurrentCell == null || _grid.CurrentCell.ColumnIndex != 2)
                {
                    box.AutoCompleteMode = AutoCompleteMode.None;
                    return;
                }
                var source = new AutoCompleteStringCollection();
                source.AddRange(known);
                box.AutoCompleteCustomSource = source;
                box.AutoCompleteSource = AutoCompleteSource.CustomSource;
                box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            };
            var weight = new DataGridViewComboBoxColumn { HeaderText = "Lineweight", FillWeight = 10, FlatStyle = FlatStyle.Flat };
            weight.Items.Add(string.Empty);
            foreach (var w in LayerStandards.LineWeightChoices()) weight.Items.Add(w);
            _grid.Columns.Add(weight);
            var plot = new DataGridViewComboBoxColumn { HeaderText = "Plot", FillWeight = 7, FlatStyle = FlatStyle.Flat };
            plot.Items.AddRange(string.Empty, "Yes", "No");
            _grid.Columns.Add(plot);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Transparency", FillWeight = 9 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Description", FillWeight = 24 });
            _grid.DataError += (s, e) => { e.ThrowException = false; };

            foreach (var s in (standards.Layers ?? new List<LayerStandard>()).Where(x => x != null)
                                                                              .OrderBy(x => x.IsPattern ? 1 : 0).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                int w;
                string why;
                var lw = LayerStandards.TryParseLineWeight(s.LineWeight, out w, out why) ? LayerStandards.FormatLineWeight(w) : string.Empty;
                _grid.Rows.Add(s.Name, s.Text(LayerProperty.Color), s.Text(LayerProperty.Linetype), lw,
                               s.Plot.HasValue ? (s.Plot.Value ? "Yes" : "No") : string.Empty, s.Text(LayerProperty.Transparency), s.Text(LayerProperty.Description));
            }

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, Height = 40, Padding = new Padding(6) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var ok = new Button { Text = "OK", AutoSize = true };
            ok.Click += (s, e) =>
            {
                string problem;
                if (!TryRead(out problem)) { MessageBox.Show(this, problem, "Layer Standards", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                DialogResult = DialogResult.OK;
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            CancelButton = cancel;

            Controls.Add(_grid);
            Controls.Add(buttons);
            Controls.Add(note);
        }

        /// <summary>The standards as edited, once OK has checked them.</summary>
        public List<LayerStandard> Result { get; private set; }

        private bool TryRead(out string problem)
        {
            problem = null;
            var list = new List<LayerStandard>();
            var settings = new LayerStandardsSettings { Layers = list };
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                Func<int, string> cell = i => Convert.ToString(row.Cells[i].Value ?? string.Empty).Trim();
                var name = cell(0);
                var transparency = cell(5);
                int percent;
                if (transparency.Length > 0 && !int.TryParse(transparency, NumberStyles.Integer, CultureInfo.InvariantCulture, out percent))
                {
                    problem = (name.Length == 0 ? "A row" : name) + ": transparency must be a whole number from 0 to 90.";
                    return false;
                }
                var s = new LayerStandard
                {
                    Name = name,
                    Color = cell(1).Length == 0 ? null : cell(1),
                    Linetype = cell(2).Length == 0 ? null : cell(2),
                    LineWeight = cell(3).Length == 0 ? null : cell(3),
                    Plot = cell(4) == "Yes" ? true : cell(4) == "No" ? (bool?)false : null,
                    Transparency = transparency.Length == 0 ? (int?)null : int.Parse(transparency, CultureInfo.InvariantCulture),
                    Description = cell(6).Length == 0 ? null : cell(6)
                };
                if (name.Length == 0 && s.IsEmpty) continue;
                list.Add(s);
            }
            var problems = new List<string>();
            settings.Validate(problems);
            if (problems.Count > 0) { problem = string.Join("\n", problems.Take(12).ToArray()); return false; }
            settings.RemoveEmpty();
            Result = list;
            return true;
        }
    }
}
