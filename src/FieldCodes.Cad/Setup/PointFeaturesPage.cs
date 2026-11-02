using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Annotation;
using FieldCodes.Editing;
using FieldCodes.Settings;
using FieldCodes.Styles;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// The Point Features section, two panels side by side: the feature list (searchable by
    /// code or name, filtered by family, sortable) stays in view while the selected rule is
    /// edited on the right, with a live preview of the label it makes.
    ///
    /// The editor works on a RuleDocument -- the rules file as a JSON DOM -- so saving
    /// preserves every comment, unknown property and unknown rule. Saves go where the
    /// configuration hierarchy says: the office configuration (created on first save), or a
    /// drawing override when one is active -- never the factory file. It edits finishing only;
    /// it cannot create blocks, symbols or Civil 3D geometry of any kind.
    ///
    /// Edits are staged as they are typed (validated, previewed, marked unsaved); Save Rules
    /// writes them. What comes from Civil 3D (the symbol) or from the rules file's advanced
    /// parts (match pattern, rotation, modifiers) is shown greyed and read only.
    ///
    /// UNTESTED as UI; the document, validation, save and preview behaviour are unit tested.
    /// </summary>
    internal sealed class PointFeaturesPage : SetupPage
    {
        private const string Automatic = "(automatic)";

        private ListView _grid;
        private TextBox _search;
        private ComboBox _familyFilter;
        private RuleDocument _document;
        private RulesConfig _candidate;
        private RulesResolution _resolution;
        private Label _sourceLine;
        private Button _restoreFactory;
        private Label _unsaved;
        private bool _dirty;
        private bool _loading;
        private int _sortColumn = -1;
        private bool _sortDescending;
        private readonly Timer _stage = new Timer { Interval = 350 };

        // editor
        private Label _identity;
        private TextBox _description;
        private CheckBox _enabled;
        private ComboBox _family;
        private TextBox _labelFormat;
        private ComboBox _labelLayer;
        private ComboBox _leader;
        private TextBox _tagPrefix;
        private Label _dripCaption;
        private ComboBox _dripLayer;
        private Label _symbolRow;
        private Label _rotationRow;
        private Label _problems;

        // preview
        private TextBox _testInput;
        private TextBox _preview;

        // Appearance: this feature's style assignments (Settings > Style Assignments, rule rows).
        private const string FromFamily = "(family / default)";
        private FtfSettings _settings;
        private StyleWorkspace _workspace;
        private CheckBox _labelOn;
        private ComboBox _labelText;
        private ComboBox _labelLeaderStyle;
        private TextBox _labelOffset;
        private ComboBox _tagText;
        private Label _appearance;
        private StylePreview _stylePreview;

        public override string Title { get { return "Point Features"; } }
        public override string AffectedCommands { get { return "FTFPOINTS, FTFLABELS, FTFTAGS, FTFLABELALL"; } }

        // The rules file has its own save path with its own buttons; the settings
        // footer must not imply it can restore this page.
        public override bool IsReadOnly { get { return true; } }

        protected override void BuildBody() { }

        protected override Control BuildFilled()
        {
            var split = new SplitContainer
            {
                Orientation = Orientation.Vertical,
                // Sized before the split is set: WinForms refuses a splitter position (or a
                // minimum) outside the control's current width. Dock fills it afterwards.
                Size = new Size(1000, 600),
                FixedPanel = FixedPanel.Panel1,
                // The settings window's own unsaved tracking is for settings; rules have
                // their own Save below.
                Tag = Ui.SettingsPanel.ViewOnly
            };
            split.Panel1MinSize = 240;
            split.SplitterDistance = 330;
            split.Panel1.Controls.Add(BuildList());
            split.Panel2.Controls.Add(BuildEditor());

            _stage.Tick += (s, e) => { _stage.Stop(); StageEdits(); };
            ReloadClicked(false);
            return split;
        }

        // ------------------------------------------------------------------ left: list

        private Control BuildList()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _sourceLine = new Label { AutoSize = true, MaximumSize = new Size(320, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 4) };
            panel.Controls.Add(_sourceLine, 0, 0);

            var filters = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            _search = new TextBox { Width = 150, Margin = new Padding(0, 0, 6, 4) };
            Tip(_search, "Search by field code or feature name.");
            _search.TextChanged += (s, e) => PopulateGrid(SelectedId());
            _familyFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(0, 0, 0, 4) };
            _familyFilter.Items.Add("All families");
            foreach (var c in AnnotationPolicy.All().Where(c => c != AnnotationCategory.Lines)) _familyFilter.Items.Add(new FamilyChoice(c));
            _familyFilter.Items.Add("Disabled only");
            _familyFilter.SelectedIndex = 0;
            _familyFilter.SelectedIndexChanged += (s, e) => PopulateGrid(SelectedId());
            filters.Controls.Add(_search);
            filters.Controls.Add(_familyFilter);
            panel.Controls.Add(filters, 0, 1);

            _grid = new ListView
            {
                View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
                Dock = DockStyle.Fill, GridLines = true
            };
            _grid.Columns.Add("Code", 80);
            _grid.Columns.Add("Feature", 110);
            _grid.Columns.Add("Family", 80);
            _grid.Columns.Add("On", 34);
            _grid.SelectedIndexChanged += (s, e) => { if (!_loading) LoadSelected(); };
            _grid.ColumnClick += (s, e) =>
            {
                _sortDescending = e.Column == _sortColumn && !_sortDescending;
                _sortColumn = e.Column;
                _grid.ListViewItemSorter = new ColumnSorter(_sortColumn, _sortDescending);
            };
            panel.Controls.Add(_grid, 0, 2);

            _unsaved = new Label { AutoSize = true, ForeColor = Color.FromArgb(160, 90, 0), Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(0, 4, 0, 0) };
            panel.Controls.Add(_unsaved, 0, 3);
            return panel;
        }

        private sealed class FamilyChoice
        {
            public readonly AnnotationCategory Category;
            public FamilyChoice(AnnotationCategory c) { Category = c; }
            public override string ToString() { return AnnotationPolicy.Name(Category); }
        }

        private sealed class ColumnSorter : IComparer
        {
            private readonly int _column;
            private readonly bool _descending;
            public ColumnSorter(int column, bool descending) { _column = column; _descending = descending; }
            public int Compare(object x, object y)
            {
                var a = ((ListViewItem)x).SubItems[_column].Text;
                var b = ((ListViewItem)y).SubItems[_column].Text;
                var r = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                return _descending ? -r : r;
            }
        }

        // ------------------------------------------------------------------ right: editor

        private Control BuildEditor()
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 0, 4, 0) };
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            var row = 0;
            Action<Control> full = c => { grid.Controls.Add(c, 0, row); grid.SetColumnSpan(c, 2); row++; };
            Func<string, Control, string, Control> labelled = (caption, c, hint) =>
            {
                var l = new Label { Text = Escape(caption), AutoSize = true, Margin = new Padding(0, 6, 10, 3) };
                c.Margin = new Padding(0, 3, 0, 3);
                grid.Controls.Add(l, 0, row);
                grid.Controls.Add(c, 1, row);
                row++;
                if (!string.IsNullOrEmpty(hint)) { Tip(c, hint); Tip(l, hint); }
                return c;
            };
            Func<string, Label> heading = t =>
            {
                var l = new Label { Text = t, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 10, 0, 2) };
                full(l);
                return l;
            };
            Func<Label> readOnly = () => new Label { AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText };

            _identity = new Label { Text = "(select a feature)", Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold), AutoSize = true };
            full(_identity);

            heading("Feature (editable)");
            _description = (TextBox)labelled("Feature name", new TextBox { Width = 240 }, "Shown in lists and previews.");
            _enabled = new CheckBox { Text = "Enabled", AutoSize = true };
            Tip(_enabled, "A disabled feature stops matching; its codes report as not configured again.");
            full(_enabled);
            _family = (ComboBox)labelled("Label All family", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 },
                "Which family Label All and Process Drawing put this feature in (Settings > Label All decides which families run).");

            heading("Label");
            _labelFormat = (TextBox)labelled("Label text", new TextBox { Width = 300, Height = 58, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical },
                "One label line per row. {placeholders} come from the pattern and the note groups ({owner}, {attach}). " +
                "Text in [square brackets] shows only when a placeholder inside it has a value; an empty row is dropped.");
            _labelLayer = (ComboBox)labelled("Label layer", new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 240, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems },
                "Pick a layer in the drawing or type a new one (created on first use). Settings > Layers can send it to a project layer.");
            foreach (var l in Context.Drawing.Layers) _labelLayer.Items.Add(l);
            _leader = (ComboBox)labelled("Leader", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 },
                "Auto draws a leader only when the label cannot sit next to the point.");
            _leader.Items.AddRange(new object[] { "Auto", "Always", "Never" });

            heading("Tag");
            _tagPrefix = (TextBox)labelled("Tag prefix", new TextBox { Width = 60 },
                "Blank: never tagged. Letters only. Trees are tagged only when Settings > Trees is Tag only.");

            heading("Appearance (saved with the window's Save, not Save Rules)");
            _labelOn = new CheckBox { Text = "Label this feature", AutoSize = true, Checked = true };
            Tip(_labelOn, "Off: FTFLABELS, Label All and Process Drawing leave this feature unlabelled, whatever its family.");
            full(_labelOn);
            _labelText = (ComboBox)labelled("Label text style", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 },
                "A named style from Settings > Text Styles: text style, height, width, colour, mask, prefix, suffix, rounding. " +
                FromFamily + ": the family's assignment, else Point Labels.");
            _labelLeaderStyle = (ComboBox)labelled("Label leader style", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 },
                "A named style from Settings > Leader Styles: multileader style, arrowhead and size, straight or spline, landing, attachment.");
            _labelOffset = (TextBox)labelled("Label offset (plotted, in)", new TextBox { Width = 70 },
                "How far from the point the label's first position is. Blank: the family's, else Point Labels' base offset.");
            _tagText = (ComboBox)labelled("Tag text style", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 },
                "The style of this feature's numbered tag. " + FromFamily + ": the family's, else Settings > Tags.");
            _appearance = readOnly();
            _appearance.MaximumSize = new Size(420, 0);
            full(_appearance);
            _stylePreview = new StylePreview { Width = 400, Height = 150, Margin = new Padding(0, 4, 0, 4) };
            full(_stylePreview);
            var styleLinks = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            var editText = new LinkLabel { Text = "Edit text styles", AutoSize = true, Margin = new Padding(0, 2, 12, 2) };
            var editLeaders = new LinkLabel { Text = "Edit leader styles", AutoSize = true, Margin = new Padding(0, 2, 12, 2) };
            var editAssign = new LinkLabel { Text = "All style assignments", AutoSize = true, Margin = new Padding(0, 2, 12, 2) };
            editText.LinkClicked += (s, e) => { if (Context.Navigate != null) Context.Navigate("Text Styles"); };
            editLeaders.LinkClicked += (s, e) => { if (Context.Navigate != null) Context.Navigate("Leader Styles"); };
            editAssign.LinkClicked += (s, e) => { if (Context.Navigate != null) Context.Navigate("Style Assignments"); };
            styleLinks.Controls.AddRange(new Control[] { editText, editLeaders, editAssign });
            full(styleLinks);
            _labelOn.CheckedChanged += (s, e) => AppearanceEdited();
            foreach (var c in new[] { _labelText, _labelLeaderStyle, _tagText }) c.SelectedIndexChanged += (s, e) => AppearanceEdited();
            _labelOffset.TextChanged += (s, e) => AppearanceEdited();

            _dripCaption = heading("Dripline");
            _dripLayer = (ComboBox)labelled("Dripline layer", new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 240 }, null);
            foreach (var l in Context.Drawing.Layers) _dripLayer.Items.Add(l);

            heading("From Civil 3D and the rules file (read only)");
            _symbolRow = readOnly();
            labelled("Symbol", _symbolRow, "Civil 3D's description key draws the symbol; FTF never draws a second one.");
            _rotationRow = readOnly();
            labelled("Rotation", _rotationRow, "The field azimuth is turned against the symbol's own zero (Settings > Symbols and Rotations).");
            var modifiers = readOnly();
            modifiers.Text = ModifierSummary();
            labelled("Modifiers", modifiers, null);

            heading("Preview");
            _testInput = (TextBox)labelled("Description", new TextBox { Width = 260 },
                "A description as the crew would type it; parsed with your staged edits. Nothing in the drawing changes.");
            _testInput.Tag = Ui.SettingsPanel.ViewOnly;
            _preview = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 400, Height = 110,
                BackColor = SystemColors.Window, Font = new Font("Consolas", 9f)
            };
            full(_preview);
            _testInput.TextChanged += (s, e) => RunPreview();

            _problems = new Label { AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.FromArgb(170, 0, 0), Margin = new Padding(0, 6, 0, 4) };
            full(_problems);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            var save = new Button { Text = "Save Rules", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
            var discard = new Button { Text = "Discard Edits", AutoSize = true };
            _restoreFactory = new Button { Text = "Restore Factory Defaults", AutoSize = true };
            save.Click += (s, e) => { StageEdits(); SaveClicked(); };
            discard.Click += (s, e) => ReloadClicked(true);
            _restoreFactory.Click += (s, e) => RestoreFactoryClicked();
            Tip(save, "Writes the active rules configuration (atomic, previous version kept as .bak).");
            Tip(_restoreFactory, "Replaces the office configuration with the shipped rules -- a separate, deliberate act.");
            buttons.Controls.Add(save);
            buttons.Controls.Add(discard);
            buttons.Controls.Add(_restoreFactory);
            full(buttons);

            foreach (var c in new Control[] { _description, _labelFormat, _tagPrefix }) c.TextChanged += (s, e) => EditMade();
            _enabled.CheckedChanged += (s, e) => EditMade();
            foreach (var c in new[] { _family, _leader }) c.SelectedIndexChanged += (s, e) => EditMade();
            foreach (var c in new[] { _labelLayer, _dripLayer }) { c.TextChanged += (s, e) => EditMade(); c.SelectedIndexChanged += (s, e) => EditMade(); }

            scroll.Controls.Add(grid);
            return scroll;
        }

        private string ModifierSummary()
        {
            if (Context.Rules == null || Context.Rules.Modifiers.Count == 0)
                return "No modifiers configured.";
            var parts = Context.Rules.Modifiers.OrderBy(m => m.Priority)
                .Select(m => m.Id.ToUpperInvariant() + (string.IsNullOrEmpty(m.LayerSuffix) ? "" : " (" + m.LayerSuffix + ")"))
                .ToArray();
            return "Apply to every feature: " + string.Join(", ", parts) + ". Defined in the rules file.";
        }

        // ------------------------------------------------------------------ loading

        /// <summary>Discards staged edits and re-reads the ACTIVE configuration. Never
        /// restores factory defaults -- that is a separate, explicit button.</summary>
        private void ReloadClicked(bool announce)
        {
            _resolution = Context.RulesResolution == null
                ? null
                : RulesStore.Resolve(Context.RulesResolution.DrawingDirectory, Context.RulesResolution.PluginDirectory);
            var active = _resolution != null ? _resolution.ActivePath : Context.RulesPath;
            try
            {
                _document = string.IsNullOrEmpty(active) ? null : RuleDocument.Load(active);
            }
            catch (ConfigException ex)
            {
                _problems.Text = ex.Message;
                _document = null;
            }
            SetDirty(false);
            ShowSource();
            RefreshCandidate();
            PopulateGrid(SelectedId());
            if (announce) _problems.Text = "Reloaded from " + active + " (staged edits discarded).";
        }

        private void ShowSource()
        {
            if (_restoreFactory != null)
                _restoreFactory.Enabled = _resolution != null && !string.IsNullOrEmpty(_resolution.FactoryPath);
            if (_resolution == null) { _sourceLine.Text = string.Empty; return; }
            switch (_resolution.Source)
            {
                case RulesSource.Drawing:
                    _sourceLine.Text = "Editing the DRAWING OVERRIDE (this job only): " + _resolution.ActivePath;
                    break;
                case RulesSource.Office:
                    _sourceLine.Text = "Editing the OFFICE STANDARD: " + _resolution.ActivePath;
                    break;
                default:
                    _sourceLine.Text = "Showing the FACTORY rules. Your first save creates the office standard at " +
                                       _resolution.OfficePath + "; the shipped file is never edited.";
                    break;
            }
        }

        private void RefreshCandidate()
        {
            if (_document == null) { _candidate = Context.Rules; return; }
            try { _candidate = _document.CompileCandidate(); }
            catch (ConfigException ex) { _problems.Text = ex.Message; }
        }

        private AnnotationCategory FamilyOf(CodeRule rule)
        {
            AnnotationCategory own;
            if (!string.IsNullOrWhiteSpace(rule.Category) && Enum.TryParse(rule.Category.Trim(), true, out own)) return own;
            return AnnotationPolicy.CategoryOf(new ParsedPoint { RuleId = rule.Id, Code = RuleDocument.LiteralCodes(rule).FirstOrDefault() }, null);
        }

        private void PopulateGrid(string selectId)
        {
            if (_grid == null) return;
            _loading = true;
            _grid.BeginUpdate();
            _grid.Items.Clear();
            var text = (_search.Text ?? string.Empty).Trim();
            var filter = _familyFilter.SelectedItem as FamilyChoice;
            var disabledOnly = Equals(_familyFilter.SelectedItem, "Disabled only");
            if (_candidate != null)
            {
                foreach (var rule in _candidate.Codes)
                {
                    var codes = CodesOf(rule);
                    var name = string.IsNullOrEmpty(rule.Description) ? rule.Id : rule.Description;
                    var family = FamilyOf(rule);
                    if (text.Length > 0 && (codes + " " + name + " " + rule.Id).IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (filter != null && filter.Category != family) continue;
                    if (disabledOnly && rule.Enabled) continue;

                    var item = new ListViewItem(codes) { Tag = rule.Id };
                    item.SubItems.Add(name);
                    item.SubItems.Add(AnnotationPolicy.Name(family));
                    item.SubItems.Add(rule.Enabled ? "Yes" : "no");
                    if (!rule.Enabled) item.ForeColor = SystemColors.GrayText;
                    _grid.Items.Add(item);
                    if (selectId != null && string.Equals(rule.Id, selectId, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
                }
            }
            _grid.EndUpdate();
            if (_grid.SelectedItems.Count == 0 && _grid.Items.Count > 0) _grid.Items[0].Selected = true;
            _loading = false;
            LoadSelected();
        }

        private string _editingId;

        private string SelectedId()
        {
            return _grid == null || _grid.SelectedItems.Count == 0 ? _editingId : (string)_grid.SelectedItems[0].Tag;
        }

        /// <summary>Fills the editor from the staged document.</summary>
        private void LoadSelected()
        {
            var id = SelectedId();
            if (id != _editingId) StageEdits();       // keep what was typed for the last rule
            _editingId = id;
            var rule = id != null && _document != null ? _document.GetEditable(id) : null;
            var have = rule != null;
            _loading = true;
            try
            {
                foreach (var c in new Control[] { _enabled, _description, _family, _labelFormat, _labelLayer, _leader, _tagPrefix, _dripLayer })
                    c.Enabled = have;
                var drip = have && rule.HasDripline;
                _dripCaption.Visible = drip;
                _dripLayer.Visible = drip;
                if (!have) { _identity.Text = "(select a feature)"; return; }

                _identity.Text = (string.IsNullOrEmpty(rule.Description) ? rule.Id : rule.Description) +
                                 "  --  code " + CodesOfSelected(id) + "  (rule '" + rule.Id + "')";
                _symbolRow.Text = rule.SymbolSummary;
                _rotationRow.Text = rule.RotationSummary == "None" ? "None"
                    : "Field azimuth turns the existing Civil 3D marker (" + rule.RotationSummary + ")";
                _description.Text = rule.Description;
                _enabled.Checked = rule.Enabled;

                _family.Items.Clear();
                var compiled = _candidate != null ? _candidate.Codes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) : null;
                var auto = compiled != null ? AnnotationPolicy.Name(AnnotationPolicy.CategoryOf(new ParsedPoint { RuleId = id, Code = RuleDocument.LiteralCodes(compiled).FirstOrDefault() }, null)) : "-";
                _family.Items.Add(Automatic + " " + auto);
                foreach (var c in AnnotationPolicy.All().Where(c => c != AnnotationCategory.Lines)) _family.Items.Add(new FamilyChoice(c));
                AnnotationCategory own;
                _family.SelectedIndex = !string.IsNullOrWhiteSpace(rule.Category) && Enum.TryParse(rule.Category, true, out own)
                    ? AnnotationPolicy.All().Where(c => c != AnnotationCategory.Lines).ToList().IndexOf(own) + 1 : 0;

                _labelFormat.Text = (rule.LabelFormat ?? string.Empty).Replace("\\P", Environment.NewLine);
                _labelLayer.Text = rule.LabelLayer ?? string.Empty;
                _leader.SelectedItem = rule.Leader;
                if (_leader.SelectedIndex < 0) _leader.SelectedIndex = 0;
                _tagPrefix.Text = rule.TagPrefix;
                if (drip) _dripLayer.Text = rule.DripLayer ?? string.Empty;

                // A description to try, from the rule's own code.
                var code = compiled != null ? RuleDocument.LiteralCodes(compiled).FirstOrDefault() : null;
                if (compiled != null && compiled.Species != null && compiled.Species.Count > 0) code = compiled.Species.Keys.First() + " 18 . 20";
                else if (code != null) code = code + " 1234";
                _testInput.Text = code ?? string.Empty;
                LoadAppearance(id);
            }
            finally { _loading = false; }
            RunPreview();
        }

        // ------------------------------------------------------------------ appearance

        private void FillStyleChoices()
        {
            if (_labelText == null || _workspace == null) return;
            var wasLoading = _loading;
            _loading = true;
            try
            {
                foreach (var pair in new[] { Tuple.Create(_labelText, true), Tuple.Create(_tagText, true), Tuple.Create(_labelLeaderStyle, false) })
                {
                    var current = pair.Item1.SelectedItem as string;
                    pair.Item1.Items.Clear();
                    pair.Item1.Items.Add(FromFamily);
                    foreach (var n in pair.Item2 ? _workspace.Styles.Text.Select(t => t.Name) : _workspace.Styles.Leaders.Select(t => t.Name))
                        if (!string.IsNullOrWhiteSpace(n)) pair.Item1.Items.Add(n);
                    pair.Item1.SelectedItem = current;
                    if (pair.Item1.SelectedIndex < 0) pair.Item1.SelectedIndex = 0;
                }
            }
            finally { _loading = wasLoading; }
        }

        private static void Choose(ComboBox c, string name)
        {
            c.SelectedIndex = 0;
            if (string.IsNullOrWhiteSpace(name)) return;
            for (var i = 1; i < c.Items.Count; i++)
                if (string.Equals((string)c.Items[i], name, StringComparison.OrdinalIgnoreCase)) { c.SelectedIndex = i; return; }
        }

        private void LoadAppearance(string ruleId)
        {
            if (_workspace == null || _labelText == null) return;
            FillStyleChoices();
            var label = _workspace.Styles.FindAssignment(StyledKind.Label, null, ruleId, null);
            var tag = _workspace.Styles.FindAssignment(StyledKind.Tag, null, ruleId, null);
            _labelOn.Checked = label == null || label.Enabled != false;
            Choose(_labelText, label != null ? label.TextStyle : null);
            Choose(_labelLeaderStyle, label != null ? label.LeaderStyle : null);
            _labelOffset.Text = label != null && label.OffsetPlotted.HasValue
                ? label.OffsetPlotted.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
            Choose(_tagText, tag != null ? tag.TextStyle : null);
            foreach (var c in new Control[] { _labelOn, _labelText, _labelLeaderStyle, _labelOffset, _tagText }) c.Enabled = ruleId != null;
            ShowAppearance();
        }

        private void AppearanceEdited()
        {
            if (_loading || _workspace == null || _editingId == null) return;
            var label = _workspace.Styles.AssignmentFor(StyledKind.Label, null, _editingId, null);
            label.Enabled = _labelOn.Checked ? (bool?)null : false;
            label.TextStyle = _labelText.SelectedIndex > 0 ? (string)_labelText.SelectedItem : null;
            label.LeaderStyle = _labelLeaderStyle.SelectedIndex > 0 ? (string)_labelLeaderStyle.SelectedItem : null;
            double offset;
            var text = (_labelOffset.Text ?? string.Empty).Trim();
            label.OffsetPlotted = text.Length > 0 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out offset) && offset >= 0
                ? offset : (double?)null;
            var tag = _workspace.Styles.AssignmentFor(StyledKind.Tag, null, _editingId, null);
            tag.TextStyle = _tagText.SelectedIndex > 0 ? (string)_tagText.SelectedItem : null;
            MarkChanged();
            ShowAppearance();
        }

        /// <summary>The look this feature's label will get and where each part comes from,
        /// resolved through the same code the drawing commands use.</summary>
        private void ShowAppearance()
        {
            if (_appearance == null || _workspace == null || _settings == null) return;
            if (_editingId == null || _candidate == null) { _appearance.Text = string.Empty; return; }
            ParsedPoint parsed = null;
            try
            {
                var input = (_testInput.Text ?? string.Empty).Trim();
                if (input.Length > 0) parsed = new FieldCodeParser(_candidate).Parse("preview", input);
            }
            catch (System.Exception) { parsed = null; }
            if (parsed == null || !string.Equals(parsed.RuleId, _editingId, StringComparison.OrdinalIgnoreCase))
                parsed = new ParsedPoint { RuleId = _editingId, LabelLayer = _labelLayer.Text, LabelText = _identity.Text };

            var settings = _workspace.PreviewSettings(_settings);
            var r = StyleResolver.Resolve(settings, StyleSubject.For(StyledKind.Label, parsed, _candidate));
            var lines = new List<string>();
            if (!r.Enabled) lines.Add("Not labelled (" + r.OriginOf("Enabled") + ").");
            lines.Add(string.Format(CultureInfo.InvariantCulture, "Text: {0}, {1:0.###}\" -- {2}",
                string.IsNullOrEmpty(r.Text.DrawingTextStyle) ? "current text style" : r.Text.DrawingTextStyle,
                r.Text.HeightPlotted, r.OriginOf("Text height")));
            lines.Add("Leader: " + r.LeaderMode + (r.Leader.Name.IndexOf("(no style)", StringComparison.Ordinal) >= 0
                ? ", the drawing's multileader style" : ", " + r.Leader.Name) + " -- " + (r.OriginOf("Multileader style") ?? r.OriginOf("Leader")));
            lines.Add("Layer: " + (string.IsNullOrEmpty(r.Layer) ? "(none)" : r.Layer) + " -- " + r.OriginOf("Layer"));
            lines.Add("Mask: " + r.Text.Mask + " -- " + r.OriginOf("Mask"));
            foreach (var p in r.Problems) lines.Add("Problem: " + p);
            _appearance.Text = string.Join(Environment.NewLine, lines.ToArray());
            _stylePreview.ShowStyle(r, parsed.LabelText ?? parsed.RawDescription ?? "LABEL", r.LeaderMode != LeaderMode.Never,
                                    Context.Drawing.TextStyleFonts, parsed.MarkerRotationDegrees);
        }

        private string CodesOfSelected(string id)
        {
            var rule = _candidate != null ? _candidate.Codes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) : null;
            return rule == null ? "?" : CodesOf(rule);
        }

        private static string CodesOf(CodeRule rule)
        {
            if (rule.Species != null && rule.Species.Count > 0)
                return string.Join(", ", rule.Species.Keys.Take(6).ToArray()) + (rule.Species.Count > 6 ? ", ..." : string.Empty);
            var codes = RuleDocument.LiteralCodes(rule);
            return codes.Count > 0 ? string.Join(", ", codes.ToArray()) : rule.Id;
        }

        // ------------------------------------------------------------------ editing

        private void EditMade()
        {
            if (_loading) return;
            _stage.Stop();
            _stage.Start();
        }

        /// <summary>Puts what is in the editor onto the staged document, validates and previews.</summary>
        private void StageEdits()
        {
            _stage.Stop();
            var id = _editingId;
            if (id == null || _document == null || _loading) return;
            var edit = _document.GetEditable(id);
            if (edit == null) return;

            var before = Snapshot(edit);
            edit.Enabled = _enabled.Checked;
            edit.Description = (_description.Text ?? string.Empty).Trim();
            edit.LabelFormat = string.Join("\\P", (_labelFormat.Text ?? string.Empty).Replace("\r\n", "\n").Split('\n')
                .Select(line => line.TrimEnd()).Where(line => line.Length > 0).ToArray());
            edit.LabelLayer = (_labelLayer.Text ?? string.Empty).Trim();
            edit.Leader = _leader.SelectedItem != null ? Convert.ToString(_leader.SelectedItem) : "Auto";
            edit.TagPrefix = (_tagPrefix.Text ?? string.Empty).Trim();
            var choice = _family.SelectedItem as FamilyChoice;
            edit.Category = choice != null ? choice.Category.ToString() : string.Empty;
            if (edit.HasDripline) edit.DripLayer = (_dripLayer.Text ?? string.Empty).Trim();
            if (Snapshot(edit) == before) return;

            _document.ApplyEdit(edit);
            var problems = _document.Validate();
            _problems.ForeColor = Color.FromArgb(170, 0, 0);
            _problems.Text = problems.Count > 0 ? string.Join(Environment.NewLine, problems.ToArray()) : string.Empty;
            SetDirty(true);
            RefreshCandidate();
            UpdateRow(id);
            RunPreview();
        }

        private static string Snapshot(EditableRule e)
        {
            return string.Join("|", new[] { e.Enabled.ToString(), e.Description, e.LabelFormat, e.LabelLayer, e.Leader, e.TagPrefix, e.Category, e.DripLayer });
        }

        private void UpdateRow(string id)
        {
            var rule = _candidate != null ? _candidate.Codes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) : null;
            if (rule == null) return;
            foreach (ListViewItem item in _grid.Items)
            {
                if (!string.Equals((string)item.Tag, id, StringComparison.OrdinalIgnoreCase)) continue;
                item.SubItems[1].Text = string.IsNullOrEmpty(rule.Description) ? rule.Id : rule.Description;
                item.SubItems[2].Text = AnnotationPolicy.Name(FamilyOf(rule));
                item.SubItems[3].Text = rule.Enabled ? "Yes" : "no";
                item.ForeColor = rule.Enabled ? SystemColors.WindowText : SystemColors.GrayText;
            }
        }

        private void SetDirty(bool dirty)
        {
            _dirty = dirty;
            if (_unsaved != null) _unsaved.Text = dirty ? "Unsaved rule changes -- Save Rules writes them" : string.Empty;
        }

        private void SaveClicked()
        {
            var target = _resolution != null ? _resolution.SaveTarget : null;
            if (_document == null || string.IsNullOrEmpty(target)) return;
            var problems = _document.Validate();
            if (problems.Count > 0)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = "Not saved: " + string.Join(Environment.NewLine, problems.ToArray());
                return;
            }
            try
            {
                // Never the factory file: SaveTarget is the office configuration, or a
                // drawing override when one is active.
                _document.Save(target);
                ReloadClicked(false);
                _problems.ForeColor = SystemColors.ControlText;
                _problems.Text = "Saved to " + target + ". Previous version kept as .bak.";
            }
            catch (ConfigException ex)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = ex.Message;
            }
            catch (System.IO.IOException ex)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = "Could not write: " + ex.Message;
            }
        }

        private void RestoreFactoryClicked()
        {
            if (_resolution == null || string.IsNullOrEmpty(_resolution.FactoryPath)) return;
            var answer = MessageBox.Show(
                "Replace the office configuration with the shipped factory rules?\n\n" +
                "Your current office configuration will be kept as .bak. Drawing overrides are not touched.",
                "Restore Factory Defaults", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (answer != DialogResult.OK) return;
            try
            {
                RulesStore.RestoreFactoryDefaults(_resolution.FactoryPath, _resolution.OfficePath);
                ReloadClicked(false);
                _problems.ForeColor = SystemColors.ControlText;
                _problems.Text = "Factory defaults restored to " + _resolution.OfficePath + "; previous configuration kept as .bak.";
            }
            catch (ConfigException ex) { _problems.ForeColor = Color.FromArgb(170, 0, 0); _problems.Text = ex.Message; }
            catch (System.IO.IOException ex) { _problems.ForeColor = Color.FromArgb(170, 0, 0); _problems.Text = "Could not restore: " + ex.Message; }
        }

        // ------------------------------------------------------------------ preview

        /// <summary>Parses the sample description against the staged rules and renders what
        /// FTF would do. Purely in memory -- it can never touch the drawing.</summary>
        private void RunPreview()
        {
            if (_candidate == null || _preview == null) return;
            var text = (_testInput.Text ?? string.Empty).Trim();
            if (text.Length == 0) { _preview.Text = string.Empty; return; }
            try
            {
                var parsed = new FieldCodeParser(_candidate).Parse("preview", text);
                var rule = parsed.RuleId == null ? null
                    : _candidate.Codes.FirstOrDefault(c => string.Equals(c.Id, parsed.RuleId, StringComparison.OrdinalIgnoreCase));
                var title = rule != null && !string.IsNullOrEmpty(rule.Description) ? rule.Description : parsed.RuleId;
                _preview.Text = FeaturePreview.Render(parsed, title) + Environment.NewLine +
                                "Label All family: " + AnnotationPolicy.Name(AnnotationPolicy.CategoryOf(parsed, _candidate));
            }
            catch (System.Exception ex)
            {
                _preview.Text = ex.Message;
            }
            ShowAppearance();
        }

        /// <summary>Arriving from Unknown Codes or Standards ("Configure Rule"): show the rule
        /// asked for, or the rule that handles the code, or offer a starter rule for a code
        /// no rule handles yet. Nothing is saved until Save Rules.</summary>
        public override void OnShown()
        {
            if (!PointFeatureNavigation.HasPending) return;
            var ruleId = PointFeatureNavigation.PendingRuleId;
            var code = PointFeatureNavigation.PendingCode;
            var sample = PointFeatureNavigation.PendingSample;
            PointFeatureNavigation.Clear();
            if (_document == null || _candidate == null) return;

            _search.Text = string.Empty;
            _familyFilter.SelectedIndex = 0;
            var target = ruleId;
            if (target == null && code != null)
            {
                var bare = FieldCodes.Orient.OrientationStandard.BaseCode(code);
                var rule = _candidate.Codes.FirstOrDefault(r =>
                    RuleDocument.LiteralCodes(r).Any(c => string.Equals(c, bare, StringComparison.OrdinalIgnoreCase)) ||
                    (r.Species != null && r.Species.Keys.Any(k => string.Equals(k, bare, StringComparison.OrdinalIgnoreCase))));
                if (rule != null) target = rule.Id;
            }
            if (target == null && code != null)
            {
                var answer = MessageBox.Show(_grid,
                    "No rule handles the code " + code + " yet.\n\nAdd a starter rule for it? It matches " + code +
                    " followed by anything and labels it with the code; edit the label text, layer and family, then Save Rules.",
                    "Configure Rule", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) { _search.Text = code; return; }
                try
                {
                    target = _document.AddStarterRule(code, PmxLayers.LineLabelFallback);
                    SetDirty(true);
                    RefreshCandidate();
                }
                catch (ConfigException ex) { _problems.Text = ex.Message; return; }
            }
            PopulateGrid(target);
            if (sample != null) _testInput.Text = sample;
        }

        public override void LoadFrom(FtfSettings s)
        {
            _settings = s;
            if (_workspace != null) _workspace.NamesChanged -= FillStyleChoices;
            _workspace = StyleWorkspace.For(s);
            _workspace.NamesChanged += FillStyleChoices;
            _loading = true;
            try { LoadAppearance(_editingId); }
            finally { _loading = false; }
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            if (_workspace != null) _workspace.WriteTo(s);
        }
        public override void RestoreDefaults(FtfSettings s) { }
    }
}
