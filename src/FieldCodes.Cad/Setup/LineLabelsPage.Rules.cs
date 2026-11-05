using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Editing;
using FieldCodes.Linework;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Line Labels, the rules file's part: the line features (every field of each, with
    /// Add and Remove), the span markers (GATE), the TBC names mapped to nothing and the stairs
    /// label, one tab each. Every edit is staged on a RuleDocument (the rest of the file and of each
    /// entry kept) and checked as it is typed. Save Line Label Rules re-reads the file, puts these
    /// four on it (so another page's save in between is kept), checks it and writes it to the
    /// office standard or the drawing override -- never the factory file; Discard Edits reads the
    /// file again. The window's Save writes ftf-settings.json only (the Text style column is a
    /// setting and goes with it).
    ///
    /// UNTESTED as UI; the document editing and checks are unit tested
    /// (LineLabelRulesEditingTests).
    /// </summary>
    internal sealed partial class LineLabelsPage
    {
        private const string AutomaticLayer = "(automatic)";
        private const int StyleColumn = 6;
        private static readonly Color RulesBad = Color.FromArgb(170, 0, 0);
        private static readonly Color RulesPending = Color.FromArgb(160, 90, 0);
        private static readonly string[] Sides = { "Auto", "Left", "Right", "On line" };
        private static readonly string[] HeightChoices =
        {
            "Automatic: when its name or label says FENCE", "Yes, read heights from its shots", "No, never"
        };

        private RuleDocument _rulesDoc;
        private RulesResolution _rulesWhere;
        private string _rulesPath;
        private bool _rulesDirty;
        private bool _rulesLoading;
        private Label _rulesSource;
        private Label _rulesStatus;
        private Label _rulesUnsaved;
        private Button _rulesSave;
        private TabControl _rulesTabs;
        private TabPage _featuresTab;
        private FieldCodes.Settings.GateSettings _gates;

        // Line features
        private DataGridView _features;
        private bool _loadingStyles;
        private EditableLineFeature _shown;
        private Label _detailsTitle;
        private Control _details;
        private TextBox _fCode;
        private TextBox _fName;
        private ComboBox _fLayer;
        private TextBox _fLabel;
        private ComboBox _fLabelLayer;
        private ComboBox _fSide;
        private CheckBox _fEnabled;
        private CheckBox _fAlways;
        private ComboBox _fHeights;
        private TextBox _fAlt;
        private TextBox _fBetween;
        private TextBox _fAliases;
        private Button _addFeature;
        private Button _removeFeature;
        private string _pendingLayer;
        private string _pendingFigure;

        // Span markers, ignored names, stairs
        private DataGridView _spans;
        private Label _spansNote;
        private TextBox _ignored;
        private TextBox _stairs;
        private Label _stairsPreview;

        private readonly ToolTip _rulesTips = new ToolTip { AutoPopDelay = 20000 };

        private void RulesTip(Control c, string text)
        {
            _rulesTips.SetToolTip(c, text);
        }

        // ================================================================== building

        private void BuildRulesPart()
        {
            Heading("Saved in the rules file: line features, span markers, ignored TBC names, stairs");
            Note("What each line feature is and what its label says, the notes that mark a gate, the TBC names that mean " +
                 "nothing and the stairs label live in the rules file, so they are saved with Save Line Label Rules below, " +
                 "not with the window's Save.");
            _rulesSource = LiveNote();

            _rulesTabs = new TabControl
            {
                Width = 660, Height = 640, Margin = new Padding(0, 4, 0, 4),
                // Edits here are staged on the rules file and saved by Save Line Label Rules; the window
                // does not count them as unsaved settings.
                Tag = FieldCodes.Cad.Ui.SettingsPanel.ViewOnly
            };
            _featuresTab = Tab("Line features", BuildFeaturesTab());
            var spans = Tab("Span markers (GATE)", BuildSpansTab());
            var ignored = Tab("Ignored TBC names", BuildIgnoredTab());
            var stairs = Tab("Stairs label", BuildStairsTab());
            _rulesTabs.TabPages.AddRange(new[] { _featuresTab, spans, ignored, stairs });
            AddFullWidth(_rulesTabs);

            _rulesStatus = LiveNote();
            _rulesSave = new Button { Text = "Save Line Label Rules", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
            var discard = new Button { Text = "Discard Edits", AutoSize = true };
            _rulesUnsaved = new Label { AutoSize = true, ForeColor = RulesPending, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(8, 8, 0, 0) };
            RulesTip(_rulesSave, "Writes the four tabs above to the active rules configuration (atomic; the previous version is kept as .bak). " +
                                 "The window's Save does not write the rules file.");
            RulesTip(discard, "Throws away what has not been saved on these four tabs and reads the rules file again.");
            _rulesSave.Click += (s, e) => SaveRulesClicked();
            discard.Click += (s, e) => ReloadRules(true);
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
            buttons.Controls.Add(_rulesSave);
            buttons.Controls.Add(discard);
            buttons.Controls.Add(_rulesUnsaved);
            AddFullWidth(buttons);

            ReloadRules(false);
        }

        /// <summary>A tab holding one column of rows, scrolling when they are taller than it.</summary>
        private static TabPage Tab(string text, Control content)
        {
            var page = new TabPage(text) { AutoScroll = true };
            content.Location = new Point(8, 8);
            page.Controls.Add(content);
            return page;
        }

        private static TableLayoutPanel Column()
        {
            var t = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        private static Label Grey(string text)
        {
            return new Label
            {
                Text = text, ForeColor = SystemColors.GrayText, AutoSize = true,
                MaximumSize = new Size(610, 0), Margin = new Padding(0, 2, 0, 6)
            };
        }

        private Control BuildFeaturesTab()
        {
            var column = Column();
            column.Controls.Add(Grey(
                "One row per line feature FTF recognises. Click one to edit it below. A line is a feature when its survey figure, " +
                "its TBC name, the shots it was drawn through or its layer say so. Empty label text: identified but not labelled. " +
                "The Text style column picks a style from Settings > Text Styles; it is a setting, saved with the window's Save."));

            _features = new DataGridView
            {
                Width = 620, Height = 200, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window,
                Tag = FieldCodes.Cad.Ui.SettingsPanel.ViewOnly
            };
            var ro = Color.FromArgb(246, 246, 248);
            foreach (var c in new[] { Tuple.Create("Code", 10), Tuple.Create("Feature", 22), Tuple.Create("Label text", 24),
                                      Tuple.Create("Label layer", 22), Tuple.Create("Side", 9), Tuple.Create("Labelled", 10) })
                _features.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = c.Item1, FillWeight = c.Item2, ReadOnly = true, DefaultCellStyle = { BackColor = ro } });
            var styleColumn = new DataGridViewComboBoxColumn { HeaderText = "Text style", FillWeight = 18, FlatStyle = FlatStyle.Flat };
            styleColumn.Items.Add("(default)");
            styleColumn.ToolTipText = "A named style from Settings > Text Styles for this feature's labels. Saved with the window's Save (settings), not Save Line Label Rules.";
            _features.Columns.Add(styleColumn);
            _features.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_features.IsCurrentCellDirty && _features.CurrentCell != null && _features.CurrentCell.ColumnIndex == StyleColumn)
                    _features.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _features.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == StyleColumn) StyleChosen(_features.Rows[e.RowIndex]);
            };
            _features.DataError += (s, e) => { e.ThrowException = false; };
            _features.SelectionChanged += (s, e) => { if (!_rulesLoading) ShowSelectedFeature(); };
            column.Controls.Add(_features);

            _addFeature = new Button { Text = "Add Line Feature...", AutoSize = true };
            _removeFeature = new Button { Text = "Remove Line Feature", AutoSize = true };
            RulesTip(_addFeature, "Adds a line feature: you give its code, then its name, layer and label text below.");
            RulesTip(_removeFeature, "Removes the selected line feature (asks first). Lines coded with it are then not identified or labelled as it.");
            _addFeature.Click += (s, e) => AddFeatureClicked();
            _removeFeature.Click += (s, e) => RemoveFeatureClicked();
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 4) };
            buttons.Controls.Add(_addFeature);
            buttons.Controls.Add(_removeFeature);
            column.Controls.Add(buttons);

            _detailsTitle = new Label { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(0, 8, 0, 2) };
            column.Controls.Add(_detailsTitle);
            _details = BuildDetails();
            column.Controls.Add(_details);
            return column;
        }

        private Control BuildDetails()
        {
            var t = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 4),
                ColumnCount = 2, GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _fCode = new TextBox { Width = 110, CharacterCasing = CharacterCasing.Upper };
            DetailRow(t, "Code", _fCode,
                "The figure code the crew shoots this feature with (FNC). A line is this feature when its survey figure is named with " +
                "the code and a number (FNC1, FNC2), when its TBC name is the code, or when the shots it was drawn through carry the " +
                "code. One word, no number at the end, and each code on one feature only.");
            _fName = new TextBox { Width = 260 };
            DetailRow(t, "Feature name", _fName,
                "What FTFLINES and the review lists call it (Chain Link Fence). A TBC line whose name is exactly this (ignoring case) is this feature too.");
            _fLayer = LayerBox(false);
            DetailRow(t, "Identified on layer", _fLayer,
                "The layer the office figure prefix database puts this figure on. A plain polyline on this layer is taken as this feature; " +
                "when several features share the layer, FTF uses the label they agree on, or asks you. Leave it empty to identify the " +
                "feature only by its figure name, TBC name or shots.");
            _fLabel = new TextBox { Width = 260 };
            DetailRow(t, "Label text", _fLabel,
                "What the label says (CHAIN LINK FENCE). Empty: the feature is identified but not labelled (the Lines review says no " +
                "label text is configured). A TBC line whose name is exactly this is this feature too. When the feature reads heights " +
                "(below), the label gets the height as Fence heights above says (3.5' FENCE).");
            _fLabelLayer = LayerBox(true);
            DetailRow(t, "Label layer", _fLabelLayer,
                "The layer this feature's labels go on. " + AutomaticLayer + ": the text layer that matches the line's own layer when the " +
                "drawing has it (V-SURF-CURB-E labels onto V-SURF-CURB-TEXT-E), else its family's only text layer, else the Fallback " +
                "label layer above.");
            _fSide = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            _fSide.Items.AddRange(Sides.Cast<object>().ToArray());
            DetailRow(t, "Side", _fSide,
                "Which side of the line the automatic pass puts the label. Auto uses the Default placement above. LEFT or RIGHT shot " +
                "in the field always wins; in FTFLABELLINE your cursor picks the side.");
            _fHeights = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            _fHeights.Items.AddRange(HeightChoices.Cast<object>().ToArray());
            DetailRow(t, "Read heights from its shots", _fHeights,
                "A shot written with a number after the code (FNC B 3.5) gives the line that height from there on, and the label " +
                "says it (3.5' FENCE, worded under Fence heights above). Gate posts are paired along the lines that read heights. " +
                "Automatic: features whose name or label says FENCE do.");

            _fEnabled = new CheckBox { Text = "Label this feature", AutoSize = true, Margin = new Padding(0, 4, 0, 2) };
            RulesTip(_fEnabled, "Off: FTF still recognises the line (FTFLINES names it) but never labels it, and the Lines review says it is turned off.");
            t.Controls.Add(_fEnabled);
            t.SetColumnSpan(_fEnabled, 2);
            _fAlways = new CheckBox { Text = "Label it even when its linetype draws its own letter or symbol", AutoSize = true, Margin = new Padding(0, 2, 0, 6) };
            RulesTip(_fAlways, "A line whose linetype already draws a letter or symbol (water ---W---, a fence's x's) is normally left to the legend " +
                               "(the \"Leave lettered and symbol linetypes to the legend\" switch above). Ticked, this feature is labelled anyway; " +
                               "every fence is, as shipped. On a layer several features share, all of them must be ticked.");
            t.Controls.Add(_fAlways);
            t.SetColumnSpan(_fAlways, 2);

            var lists = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Margin = new Padding(0, 4, 0, 0) };
            _fAlt = ListBox(lists, 0, "Other wordings, one per line",
                "The wordings FTFLABELLINE's Wording option lets you switch to while placing this label (EOP for EDGE OF PAVEMENT). " +
                "The label text stays the default.");
            _fBetween = ListBox(lists, 1, "Between two of these edges",
                "What FTFLABELBETWEEN labels the surface between two of these edges (ASPHALT between two asphalt edges), one per line. " +
                "The first is the default; the rest are offered by its Wording option. FTFLABELDRIVEWAY also offers one that spells the " +
                "layer's type out (ASPHALT DWY for ASPH). Empty: the label text and other wordings are used.");
            _fAliases = ListBox(lists, 2, "Other names (TBC names, codes)",
                "Other names this feature goes by, one per line: the TBC export names of its lines (Edge of Conc) and other codes the " +
                "crew shoots it with. Matched whole, ignoring case, never part of a name. A TBC line with one of these names is this " +
                "feature; a shot coded with one counts as this feature's for heights and line continuity and is not reported as an " +
                "unknown word.");
            t.Controls.Add(lists);
            t.SetColumnSpan(lists, 2);

            _fCode.TextChanged += (s, e) => FeatureEdited();
            _fName.TextChanged += (s, e) => FeatureEdited();
            _fLayer.TextChanged += (s, e) => FeatureEdited();
            _fLabel.TextChanged += (s, e) => FeatureEdited();
            _fLabelLayer.TextChanged += (s, e) => FeatureEdited();
            _fSide.SelectedIndexChanged += (s, e) => FeatureEdited();
            _fHeights.SelectedIndexChanged += (s, e) => FeatureEdited();
            _fEnabled.CheckedChanged += (s, e) => FeatureEdited();
            _fAlways.CheckedChanged += (s, e) => FeatureEdited();
            _fAlt.TextChanged += (s, e) => FeatureEdited();
            _fBetween.TextChanged += (s, e) => FeatureEdited();
            _fAliases.TextChanged += (s, e) => FeatureEdited();
            return t;
        }

        private void DetailRow(TableLayoutPanel t, string caption, Control input, string hint)
        {
            var label = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 3) };
            input.Margin = new Padding(0, 3, 0, 3);
            input.Anchor = AnchorStyles.Left;
            t.Controls.Add(label);
            t.Controls.Add(input);
            RulesTip(label, hint);
            RulesTip(input, hint);
        }

        private TextBox ListBox(TableLayoutPanel lists, int column, string caption, string hint)
        {
            var label = new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 0, 8, 2) };
            var box = new TextBox
            {
                Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = false,
                Width = 190, Height = 90, Margin = new Padding(0, 0, 8, 0)
            };
            lists.Controls.Add(label, column, 0);
            lists.Controls.Add(box, column, 1);
            RulesTip(label, hint);
            RulesTip(box, hint);
            return box;
        }

        /// <summary>A layer box: one of this drawing's layers or a typed name.</summary>
        private ComboBox LayerBox(bool automatic)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 280, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
            if (automatic) combo.Items.Add(AutomaticLayer);
            foreach (var name in (Context.Drawing.Layers ?? new List<string>()).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) combo.Items.Add(name);
            return combo;
        }

        private Control BuildSpansTab()
        {
            var column = Column();
            column.Controls.Add(Grey(
                "A word in the notes of shots along a line that marks a span between two of them: two fence shots noted FGP are a gate, " +
                "labelled centred between the pair, on the line. FTFLABELLINE (for the line you click) and the automatic pass " +
                "(FTFLINELABELS, Label All) place these. The word must be a whole word of the note (FCK B GATE carries GATE; " +
                "GATEPOST does not); marked shots pair up in order along the line, and an odd one out is reported, never guessed."));

            _spans = new DataGridView
            {
                Width = 520, Height = 180, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window,
                Tag = FieldCodes.Cad.Ui.SettingsPanel.ViewOnly
            };
            _spans.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "On", FillWeight = 10, ToolTipText = "Off: the marker is kept but nothing is labelled for it." });
            _spans.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Note word", FillWeight = 30, ToolTipText = "The word in the shots' notes that marks the span (FGP). One word; each once." });
            _spans.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Label", FillWeight = 60, ToolTipText = "What the label between the pair says (GATE). Empty: the note word itself." });
            _spans.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_spans.IsCurrentCellDirty && _spans.CurrentCell != null && _spans.CurrentCell.ColumnIndex == 0)
                    _spans.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _spans.CellValueChanged += (s, e) => { if (e.RowIndex >= 0) StageRules(); };
            _spans.DataError += (s, e) => { e.ThrowException = false; };
            column.Controls.Add(_spans);

            var add = new Button { Text = "Add Span Marker", AutoSize = true };
            var remove = new Button { Text = "Remove Span Marker", AutoSize = true };
            RulesTip(add, "Adds a row: type the note word and what its label says.");
            RulesTip(remove, "Removes the selected row.");
            add.Click += (s, e) =>
            {
                var i = _spans.Rows.Add(true, string.Empty, string.Empty);
                _spans.Rows[i].Tag = -1;
                _spans.CurrentCell = _spans.Rows[i].Cells[1];
                _spans.BeginEdit(true);
                StageRules();
            };
            remove.Click += (s, e) =>
            {
                if (_spans.CurrentRow == null) return;
                _spans.EndEdit();
                _spans.Rows.Remove(_spans.CurrentRow);
                StageRules();
            };
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 4) };
            buttons.Controls.Add(add);
            buttons.Controls.Add(remove);
            column.Controls.Add(buttons);

            _spansNote = Grey(string.Empty);
            _spansNote.ForeColor = RulesPending;
            column.Controls.Add(_spansNote);
            var gates = new LinkLabel { Text = "Gate posts paired with their width (6' GATE): Settings > Gates", AutoSize = true, Margin = new Padding(0, 2, 0, 4) };
            gates.LinkClicked += (s, e) => { if (Context.Navigate != null) Context.Navigate("Gates"); };
            RulesTip(gates, "With gate finding on, gate posts are paired by the gate pass and labelled with the gate's width; a span marker for a gate post code is then not used.");
            column.Controls.Add(gates);
            return column;
        }

        private Control BuildIgnoredTab()
        {
            var column = Column();
            column.Controls.Add(Grey(
                "TBC export names that are deliberately not a line feature (INFO LINE), one per line. FTFLINES and the Lines review then " +
                "list such a line as known and ignored instead of unidentified; nothing is drawn or labelled for it. Matched whole, " +
                "ignoring case. To make a TBC name mean a feature instead, put it in that feature's Other names (Line features tab)."));
            _ignored = new TextBox
            {
                Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = false,
                Width = 360, Height = 220, Margin = new Padding(0, 4, 0, 4)
            };
            RulesTip(_ignored, "One TBC name per line, as the TBC export writes it.");
            _ignored.TextChanged += (s, e) => StageRules();
            column.Controls.Add(_ignored);
            return column;
        }

        private Control BuildStairsTab()
        {
            var column = Column();
            column.Controls.Add(Grey(
                "FTFLABELSTAIRS (FTFS) labels a staircase from its tread lines: pick them, and the label goes centred in them, reading " +
                "along the treads, on the stairs' text layer. {count} is the number of lines picked (each drawn tread edge is one)."));
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
            row.Controls.Add(new Label { Text = "Stairs label", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
            _stairs = new TextBox { Width = 240 };
            RulesTip(_stairs, "What FTFLABELSTAIRS says. {count} (in lower case) is replaced by the number of stair lines picked: \"{count} STEPS\" labels 5 STEPS.");
            row.Controls.Add(_stairs);
            var count = new Button { Text = "Put In Count", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
            RulesTip(count, "Puts {count}, the number of stair lines picked, where the cursor is.");
            count.Click += (s, e) => { _stairs.SelectedText = "{count}"; _stairs.Focus(); };
            row.Controls.Add(count);
            column.Controls.Add(row);
            _stairsPreview = Grey(string.Empty);
            column.Controls.Add(_stairsPreview);
            _stairs.TextChanged += (s, e) => StageRules();
            return column;
        }

        // ================================================================== showing

        private void ReloadRules(bool announce)
        {
            var keep = _shown != null ? _shown.Code : null;
            _rulesWhere = Context.RulesResolution == null ? null
                : RulesStore.Resolve(Context.RulesResolution.DrawingDirectory, Context.RulesResolution.PluginDirectory);
            _rulesPath = _rulesWhere != null ? _rulesWhere.ActivePath : Context.RulesPath;
            _rulesDoc = null;
            string loadProblem = null;
            try
            {
                if (!string.IsNullOrEmpty(_rulesPath)) _rulesDoc = RuleDocument.Load(_rulesPath);
            }
            catch (ConfigException ex) { loadProblem = ex.Message; }
            catch (IOException ex) { loadProblem = ex.Message; }

            _rulesLoading = true;
            try
            {
                FillFeatures(keep);
                ShowRules(_rulesDoc != null ? _rulesDoc.GetLineLabelRules() : EditableLineLabelRules.Defaults());
            }
            finally { _rulesLoading = false; }
            ShowSelectedFeature();
            ShowRulesSource();
            SetRulesDirty(false);
            RulesEdited();
            var editable = _rulesDoc != null;
            _rulesSave.Enabled = editable && _rulesWhere != null && !string.IsNullOrEmpty(_rulesWhere.SaveTarget);
            _addFeature.Enabled = editable;
            _spans.Enabled = editable;
            _ignored.ReadOnly = !editable;
            _stairs.ReadOnly = !editable;
            if (loadProblem != null) RulesProblem("The rules file could not be read, so the line feature list is empty and nothing here can be saved: " + loadProblem);
            else if (announce) RulesInfo("Reloaded from " + _rulesPath + " (unsaved edits discarded).");
            else RulesInfo(string.Empty);
        }

        private void ShowRulesSource()
        {
            var where = string.Empty;
            if (_rulesWhere != null)
            {
                switch (_rulesWhere.Source)
                {
                    case RulesSource.Drawing:
                        where = "Editing the DRAWING OVERRIDE (this job only): " + _rulesWhere.ActivePath;
                        break;
                    case RulesSource.Office:
                        where = "Editing the OFFICE STANDARD: " + _rulesWhere.ActivePath;
                        break;
                    default:
                        where = "Showing the FACTORY rules. Your first save creates the office standard at " + _rulesWhere.OfficePath +
                                "; the shipped file is never edited.";
                        break;
                }
            }
            else if (!string.IsNullOrEmpty(_rulesPath)) where = "Rules: " + _rulesPath;
            _rulesSource.Text = where;
        }

        /// <summary>Fills the line feature list from the staged document, selecting the feature
        /// with that code (or the first).</summary>
        private void FillFeatures(string select)
        {
            var wasLoading = _rulesLoading;
            _rulesLoading = true;
            try
            {
                _features.Rows.Clear();
                var features = _rulesDoc != null ? _rulesDoc.GetLineFeatures() : new List<EditableLineFeature>();
                DataGridViewRow chosen = null;
                foreach (var f in features)
                {
                    var i = _features.Rows.Add();
                    var row = _features.Rows[i];
                    row.Tag = f.SourceIndex;
                    ShowRow(row, f);
                    if (chosen == null && select != null && string.Equals((f.Code ?? string.Empty).Trim(), select.Trim(), StringComparison.OrdinalIgnoreCase))
                        chosen = row;
                }
                if (chosen == null && _features.Rows.Count > 0) chosen = _features.Rows[0];
                _features.ClearSelection();
                if (chosen != null)
                {
                    chosen.Selected = true;
                    _features.CurrentCell = chosen.Cells[0];
                }
                FillStyles();
            }
            finally { _rulesLoading = wasLoading; }
        }

        private static void ShowRow(DataGridViewRow row, EditableLineFeature f)
        {
            row.Cells[0].Value = f.Code;
            row.Cells[1].Value = string.IsNullOrWhiteSpace(f.Name) ? f.Code : f.Name;
            row.Cells[2].Value = f.Label;
            row.Cells[3].Value = string.IsNullOrWhiteSpace(f.LabelLayer) ? AutomaticLayer : f.LabelLayer;
            row.Cells[4].Value = SideText(f.Placement);
            row.Cells[5].Value = !f.Enabled ? "Off" : string.IsNullOrWhiteSpace(f.Label) ? "No text" : "Yes";
            row.Cells[1].ToolTipText = string.IsNullOrWhiteSpace(f.Layer) ? "Identified by figure name, TBC name or shots only" : "Identified on layer " + f.Layer;
            row.DefaultCellStyle.ForeColor = f.Enabled ? SystemColors.ControlText : SystemColors.GrayText;
        }

        private static string SideText(string placement)
        {
            var p = (placement ?? string.Empty).Trim();
            if (p.Length == 0) return "Auto";
            if (p.Equals("OnLine", StringComparison.OrdinalIgnoreCase)) return "On line";
            if (p.Equals("Left", StringComparison.OrdinalIgnoreCase)) return "Left";
            if (p.Equals("Right", StringComparison.OrdinalIgnoreCase)) return "Right";
            return p;
        }

        private DataGridViewRow SelectedFeatureRow()
        {
            if (_features.SelectedRows.Count > 0) return _features.SelectedRows[0];
            return _features.CurrentRow;
        }

        /// <summary>Shows the selected feature's fields in the panel below the list.</summary>
        private void ShowSelectedFeature()
        {
            var row = SelectedFeatureRow();
            _shown = null;
            if (row != null && row.Tag is int && _rulesDoc != null)
            {
                var index = (int)row.Tag;
                _shown = _rulesDoc.GetLineFeatures().FirstOrDefault(f => f.SourceIndex == index);
            }

            var wasLoading = _rulesLoading;
            _rulesLoading = true;
            try
            {
                var f = _shown ?? new EditableLineFeature();
                ShowDetailsTitle();
                _fCode.Text = f.Code ?? string.Empty;
                _fName.Text = f.Name ?? string.Empty;
                _fLayer.Text = f.Layer ?? string.Empty;
                _fLabel.Text = f.Label ?? string.Empty;
                _fLabelLayer.Text = string.IsNullOrWhiteSpace(f.LabelLayer) ? AutomaticLayer : f.LabelLayer;
                ComboHelp.SelectOrAdd(_fSide, SideText(f.Placement), false);
                _fHeights.SelectedIndex = !f.ReadHeight.HasValue ? 0 : f.ReadHeight.Value ? 1 : 2;
                _fEnabled.Checked = f.Enabled;
                _fAlways.Checked = f.AlwaysLabel;
                _fAlt.Lines = (f.AltLabels ?? new List<string>()).ToArray();
                _fBetween.Lines = (f.BetweenLabels ?? new List<string>()).ToArray();
                _fAliases.Lines = (f.Aliases ?? new List<string>()).ToArray();
            }
            finally { _rulesLoading = wasLoading; }
            _details.Enabled = _shown != null;
            _removeFeature.Enabled = _shown != null;
        }

        private void ShowRules(EditableLineLabelRules r)
        {
            _stairs.Text = r.StairsLabelFormat ?? string.Empty;
            _ignored.Lines = (r.IgnoreLineNames ?? new List<string>()).ToArray();
            _spans.Rows.Clear();
            foreach (var m in r.SpanMarkers ?? new List<EditableSpanMarker>())
            {
                var i = _spans.Rows.Add(m.Enabled, m.Token ?? string.Empty, m.Label ?? string.Empty);
                _spans.Rows[i].Tag = m.SourceIndex;
            }
        }

        /// <summary>After every load and edit: the stairs preview and the gate post note.</summary>
        private void RulesEdited()
        {
            if (_stairs == null) return;     // the page is not built yet
            var format = _stairs.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(format))
            {
                _stairsPreview.ForeColor = RulesPending;
                _stairsPreview.Text = "The stairs label is empty.";
            }
            else
            {
                _stairsPreview.ForeColor = SystemColors.GrayText;
                _stairsPreview.Text = "5 stair lines are labelled:  " + LineLabelPlanner.StairsText(format, 5) +
                                      (format.Contains("{count}") ? string.Empty : "   (the number of lines is not in the label)");
            }

            var notes = new List<string>();
            var posts = _gates != null && _gates.PostCodes != null ? _gates.PostCodes : new List<string>();
            foreach (DataGridViewRow row in _spans.Rows)
            {
                var token = (Convert.ToString(row.Cells[1].Value) ?? string.Empty).Trim();
                if (token.Length == 0 || !posts.Any(p => string.Equals((p ?? string.Empty).Trim(), token, StringComparison.OrdinalIgnoreCase))) continue;
                notes.Add(_gates.Enabled
                    ? token + " is a gate post code (Settings > Gates). While gate finding is on, the gate pass labels those gates with their width, so this row is not used."
                    : token + " is a gate post code, and gate finding is off (Settings > Gates), so this row labels those gates.");
            }
            _spansNote.Text = string.Join(Environment.NewLine, notes.ToArray());
        }

        // ================================================================== editing

        private EditableLineLabelRules ReadRulesControls()
        {
            var r = new EditableLineLabelRules
            {
                StairsLabelFormat = _stairs.Text ?? string.Empty,
                IgnoreLineNames = EditableLineFeature.SplitLines(_ignored.Text),
                SpanMarkers = new List<EditableSpanMarker>()
            };
            foreach (DataGridViewRow row in _spans.Rows)
                r.SpanMarkers.Add(new EditableSpanMarker
                {
                    SourceIndex = row.Tag is int ? (int)row.Tag : -1,
                    Enabled = row.Cells[0].Value is bool ? (bool)row.Cells[0].Value : true,
                    Token = Convert.ToString(row.Cells[1].Value) ?? string.Empty,
                    Label = Convert.ToString(row.Cells[2].Value) ?? string.Empty
                });
            return r;
        }

        /// <summary>Puts the stairs label, span markers and ignored names on the staged document and
        /// shows what is wrong in the whole file, if anything.</summary>
        private void StageRules()
        {
            if (_rulesLoading || _rulesStatus == null) return;
            if (_rulesDoc != null)
            {
                _rulesDoc.ApplyLineLabelRules(ReadRulesControls());
                // A written list is in row order, so each row's entry is now at its own place.
                var markers = _rulesDoc.GetLineLabelRules().SpanMarkers;
                if (markers.Count == _spans.Rows.Count)
                    for (var i = 0; i < markers.Count; i++) _spans.Rows[i].Tag = markers[i].SourceIndex;
            }
            SetRulesDirty(true);
            RulesEdited();
            ShowRulesProblems(null);
        }

        /// <summary>What is wrong in the staged file, or the message when nothing is.</summary>
        private void ShowRulesProblems(string whenFine)
        {
            var problems = _rulesDoc != null ? _rulesDoc.Validate() : new List<string>();
            if (problems.Count > 0) RulesProblem(string.Join(Environment.NewLine, problems.ToArray()));
            else RulesInfo(whenFine ?? string.Empty);
        }

        private EditableLineFeature ReadDetails()
        {
            var layer = (_fLabelLayer.Text ?? string.Empty).Trim();
            var side = Convert.ToString(_fSide.SelectedItem) ?? "Auto";
            return new EditableLineFeature
            {
                SourceIndex = _shown.SourceIndex,
                Code = (_fCode.Text ?? string.Empty).Trim(),
                Name = (_fName.Text ?? string.Empty).Trim(),
                Layer = (_fLayer.Text ?? string.Empty).Trim(),
                Label = (_fLabel.Text ?? string.Empty).Trim(),
                LabelLayer = layer == AutomaticLayer ? string.Empty : layer,
                Placement = side == "On line" ? "OnLine" : side == "Auto" ? string.Empty : side,
                ReadHeight = _fHeights.SelectedIndex == 1 ? true : _fHeights.SelectedIndex == 2 ? (bool?)false : null,
                Enabled = _fEnabled.Checked,
                AlwaysLabel = _fAlways.Checked,
                AltLabels = EditableLineFeature.SplitLines(_fAlt.Text),
                BetweenLabels = EditableLineFeature.SplitLines(_fBetween.Text),
                Aliases = EditableLineFeature.SplitLines(_fAliases.Text)
            };
        }

        /// <summary>A field of the selected feature changed: stage it and refresh its row.</summary>
        private void FeatureEdited()
        {
            if (_rulesLoading || _shown == null || _rulesDoc == null) return;
            var edit = ReadDetails();
            var codeChanged = !string.Equals((edit.Code ?? string.Empty).Trim(), (_shown.Code ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
            try { _rulesDoc.ApplyLineFeature(edit); }
            catch (ConfigException ex) { RulesProblem(ex.Message); return; }
            _shown = _rulesDoc.GetLineFeatures().FirstOrDefault(f => f.SourceIndex == edit.SourceIndex) ?? _shown;

            var row = SelectedFeatureRow();
            if (row != null)
            {
                var wasLoading = _rulesLoading;
                _rulesLoading = true;
                try
                {
                    ShowRow(row, _shown);
                    if (codeChanged) ShowStyle(row);
                }
                finally { _rulesLoading = wasLoading; }
            }
            ShowDetailsTitle();
            SetRulesDirty(true);
            ShowRulesProblems(null);
        }

        private void ShowDetailsTitle()
        {
            _detailsTitle.Text = _shown == null
                ? "No line feature selected"
                : "Line feature " + _shown.Code + (string.IsNullOrWhiteSpace(_shown.Name) ? string.Empty : ": " + _shown.Name);
        }

        private void AddFeatureClicked()
        {
            if (_rulesDoc == null) return;
            var suggestion = string.Empty;
            if (!string.IsNullOrWhiteSpace(_pendingFigure))
            {
                var f = _pendingFigure.Trim();
                var end = f.Length;
                while (end > 0 && char.IsDigit(f[end - 1])) end--;
                suggestion = f.Substring(0, end).ToUpperInvariant();
            }
            var code = AskCode(suggestion);
            if (string.IsNullOrWhiteSpace(code)) return;
            EditableLineFeature added;
            try
            {
                added = _rulesDoc.AddLineFeature(new EditableLineFeature { Code = code, Layer = _pendingLayer ?? string.Empty });
            }
            catch (ConfigException ex) { RulesProblem("Not added: " + ex.Message); return; }
            _pendingLayer = null;
            _pendingFigure = null;
            FillFeatures(added.Code);
            ShowSelectedFeature();
            SetRulesDirty(true);
            ShowRulesProblems("Added " + added.Code + " at the end of the list: give it a feature name, the layer it is identified on and its label text, then Save Line Label Rules.");
            _fName.Focus();
        }

        private void RemoveFeatureClicked()
        {
            if (_rulesDoc == null || _shown == null) return;
            var name = _shown.Code + (string.IsNullOrWhiteSpace(_shown.Name) ? string.Empty : " (" + _shown.Name + ")");
            var answer = MessageBox.Show(_features,
                "Remove the line feature " + name + "?\n\nLines coded " + _shown.Code + " are then no longer identified or labelled as it. " +
                "Nothing is written until Save Line Label Rules; Discard Edits brings it back.",
                "Remove Line Feature", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (answer != DialogResult.OK) return;
            var position = SelectedFeatureRow() != null ? SelectedFeatureRow().Index : 0;
            if (!_rulesDoc.RemoveLineFeature(_shown)) return;
            var features = _rulesDoc.GetLineFeatures();
            var next = features.Count == 0 ? null : features[Math.Min(position, features.Count - 1)].Code;
            _shown = null;
            FillFeatures(next);
            ShowSelectedFeature();
            SetRulesDirty(true);
            ShowRulesProblems("Removed " + name + ". Save Line Label Rules writes it.");
        }

        /// <summary>Asks for a new line feature's code; null when cancelled.</summary>
        private static string AskCode(string suggestion)
        {
            using (var form = new Form())
            {
                form.Text = "Add Line Feature";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(380, 120);
                var label = new Label
                {
                    Text = "The code the crew shoots it with (FNC, RWG): one word, no number at the end.",
                    AutoSize = false, Bounds = new Rectangle(12, 10, 356, 34)
                };
                var input = new TextBox { Bounds = new Rectangle(12, 48, 160, 22), CharacterCasing = CharacterCasing.Upper, Text = suggestion ?? string.Empty };
                var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Bounds = new Rectangle(208, 84, 76, 26) };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(292, 84, 76, 26) };
                form.Controls.Add(label);
                form.Controls.Add(input);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                return form.ShowDialog() == DialogResult.OK ? input.Text : null;
            }
        }

        // ================================================================== text styles (settings)

        /// <summary>Line features use the same styles as points: a rule-free, code-level line
        /// label assignment (Settings > Style Assignments).</summary>
        private void FillStyles()
        {
            if (_styles == null || _features == null) return;
            _loadingStyles = true;
            try
            {
                var column = (DataGridViewComboBoxColumn)_features.Columns[StyleColumn];
                column.Items.Clear();
                column.Items.Add("(default)");
                foreach (var t in _styles.Styles.Text) if (!string.IsNullOrWhiteSpace(t.Name)) column.Items.Add(t.Name);
                foreach (DataGridViewRow r in _features.Rows) ShowStyle(r);
            }
            finally { _loadingStyles = false; }
        }

        private void ShowStyle(DataGridViewRow r)
        {
            if (_styles == null) return;
            var was = _loadingStyles;
            _loadingStyles = true;
            try
            {
                var column = (DataGridViewComboBoxColumn)_features.Columns[StyleColumn];
                var code = Convert.ToString(r.Cells[0].Value);
                var a = _styles.Styles.FindAssignment(FieldCodes.Styles.StyledKind.LineLabel, null, null, code);
                r.Cells[StyleColumn].Value = a != null && !string.IsNullOrWhiteSpace(a.TextStyle) && column.Items.Contains(a.TextStyle) ? a.TextStyle : "(default)";
            }
            finally { _loadingStyles = was; }
        }

        private void StyleChosen(DataGridViewRow r)
        {
            if (_loadingStyles || _styles == null) return;
            var code = Convert.ToString(r.Cells[0].Value);
            var chosen = Convert.ToString(r.Cells[StyleColumn].Value);
            var a = _styles.Styles.AssignmentFor(FieldCodes.Styles.StyledKind.LineLabel, null, null, code);
            a.TextStyle = string.IsNullOrWhiteSpace(chosen) || chosen == "(default)" ? null : chosen;
            MarkChanged();
        }

        // ================================================================== saving

        private void SaveRulesClicked()
        {
            var target = _rulesWhere != null ? _rulesWhere.SaveTarget : null;
            if (_rulesDoc == null || string.IsNullOrEmpty(target)) { RulesProblem("There is no rules file to save to."); return; }
            _spans.EndEdit();
            if (!_rulesDirty) { RulesInfo("No line label rule changes to save."); return; }

            _rulesDoc.ApplyLineLabelRules(ReadRulesControls());
            var problems = _rulesDoc.Validate();
            if (problems.Count > 0) { RulesProblem("Not saved: " + string.Join(Environment.NewLine, problems.ToArray())); return; }

            // The file as it is now (another page may have saved it since this one read it), with
            // this page's part put on it, so neither page's save loses the other's.
            RuleDocument fresh;
            try { fresh = RuleDocument.Load(_rulesPath); }
            catch (ConfigException ex) { RulesProblem("Not saved: " + ex.Message); return; }
            catch (IOException ex) { RulesProblem("Not saved: " + ex.Message); return; }
            _rulesDoc.CopyLineLabelRulesTo(fresh);

            problems = fresh.Validate();
            if (problems.Count > 0) { RulesProblem("Not saved: " + string.Join(Environment.NewLine, problems.ToArray())); return; }
            try
            {
                // Never the factory file: SaveTarget is the office configuration or the drawing override.
                fresh.Save(target);
            }
            catch (ConfigException ex) { RulesProblem(ex.Message); return; }
            catch (IOException ex) { RulesProblem("Could not write: " + ex.Message); return; }
            catch (UnauthorizedAccessException ex) { RulesProblem("Could not write: " + ex.Message); return; }

            ReloadRules(false);
            RulesInfo("Saved to " + target + ". Previous version kept as .bak. FTFLINELABELS, FTFLABELLINE, FTFLABELBETWEEN, " +
                      "FTFLABELSTAIRS and FTFLINES use it from their next run.");
        }

        private void SetRulesDirty(bool dirty)
        {
            _rulesDirty = dirty;
            if (_rulesUnsaved != null)
                _rulesUnsaved.Text = dirty ? "Unsaved line label rule changes -- Save Line Label Rules writes them" : string.Empty;
        }

        private void RulesInfo(string text)
        {
            _rulesStatus.ForeColor = SystemColors.ControlText;
            _rulesStatus.Text = text ?? string.Empty;
        }

        private void RulesProblem(string text)
        {
            _rulesStatus.ForeColor = RulesBad;
            _rulesStatus.Text = text ?? string.Empty;
        }

        // ================================================================== arriving from the Lines review

        /// <summary>Arriving from the Lines review ("Line Label Settings"): select the feature it
        /// asked about, or, for a line that is not one yet, get Add Line Feature ready with its
        /// layer.</summary>
        public override void OnShown()
        {
            if (!LineLabelNavigation.HasPending || _features == null) return;
            var code = LineLabelNavigation.PendingFeature ??
                       (LineLabelNavigation.PendingCandidates != null ? LineLabelNavigation.PendingCandidates.FirstOrDefault() : null);
            var layer = LineLabelNavigation.PendingLayer;
            var figure = LineLabelNavigation.PendingFigure;
            LineLabelNavigation.Clear();
            _rulesTabs.SelectedTab = _featuresTab;
            if (string.IsNullOrWhiteSpace(code))
            {
                _pendingLayer = layer;
                _pendingFigure = figure;
                RulesInfo("That line (layer " + (layer ?? "?") + ") is not a line feature yet. Add Line Feature adds one" +
                          (layer != null ? " identified on " + layer : string.Empty) +
                          ": give it the code the crew shoots it with, then its name and label text.");
                return;
            }
            foreach (DataGridViewRow r in _features.Rows)
                if (string.Equals(Convert.ToString(r.Cells[0].Value), code.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    _features.ClearSelection();
                    r.Selected = true;
                    _features.CurrentCell = r.Cells[0];
                    _features.FirstDisplayedScrollingRowIndex = r.Index;
                    ShowSelectedFeature();
                    _fLabel.Focus();
                    return;
                }
        }
    }
}
