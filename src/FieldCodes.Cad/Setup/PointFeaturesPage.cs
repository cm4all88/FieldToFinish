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
    /// Everything a code rule says is edited here, in plain words: name, family, the match
    /// pattern and its species, the label and cluster label, the notes vocabulary, range checks,
    /// rotation, dripline and tag -- and a feature can be added or deleted. Edits are staged as
    /// they are typed (validated, previewed, marked unsaved); Save Rules writes them, onto the file
    /// as it is then, so another page's save in between is kept. What comes from Civil 3D (the
    /// symbol) or from other pages (the inserted block, the modifiers) is shown greyed.
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
        private TextBox _match;
        private Label _matchInfo;
        private TextBox _species;
        private TextBox _clusterFormat;
        private CheckBox _notesOn;
        private Label _notesHint;
        private ComboBox _notesUnknown;
        private TextBox _notesUnknownGroup;
        private DataGridView _terms;
        private Control _termButtons;
        private DataGridView _groups;
        private TextBox _noteOrder;
        private DataGridView _checks;
        private CheckBox _rotationOn;
        private ComboBox _rotationFrom;
        private ComboBox _rotationMode;
        private CheckBox _dripOn;
        private ComboBox _dripRadius;
        private ComboBox _dripLayer;
        private CheckBox _dripUnify;
        private Label _symbolRow;
        private Label _problems;
        private Button _delete;
        private readonly Dictionary<Control, Label> _captions = new Dictionary<Control, Label>();

        // What this page changed in the rules file, for Save: the features edited, added or deleted
        // (in the order first touched) and whether the drafting standard was.
        private readonly List<string> _touched = new List<string>();
        private bool _standardTouched;

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
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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

            var listButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
            var add = new Button { Text = "New Feature...", AutoSize = true };
            _delete = new Button { Text = "Delete Feature...", AutoSize = true };
            Tip(add, "A new feature for a field code no feature handles yet: it matches the code followed by anything and " +
                     "labels it with the code, ready to edit. Staged; Save Rules writes it.");
            Tip(_delete, "Deletes the selected feature, after saying what else names it. Staged; Save Rules writes it, " +
                         "Discard Edits brings it back.");
            add.Click += (s, e) => NewClicked();
            _delete.Click += (s, e) => DeleteClicked();
            listButtons.Controls.Add(add);
            listButtons.Controls.Add(_delete);
            panel.Controls.Add(listButtons, 0, 3);

            _unsaved = new Label { AutoSize = true, ForeColor = Color.FromArgb(160, 90, 0), Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(0, 4, 0, 0) };
            panel.Controls.Add(_unsaved, 0, 4);
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
                _captions[c] = l;
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
            Func<string, Label> note = t =>
            {
                var l = new Label { Text = t, AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 4) };
                full(l);
                return l;
            };

            _identity = new Label { Text = "(select a feature)", Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold), AutoSize = true };
            full(_identity);

            heading("Feature");
            _description = (TextBox)labelled("Feature name", new TextBox { Width = 240 }, "Shown in lists, previews and problems.");
            _enabled = new CheckBox { Text = "Enabled", AutoSize = true };
            Tip(_enabled, "A disabled feature stops matching: its codes fall through to the features below it, " +
                          "else they are reported in Unknown Codes as not configured.");
            full(_enabled);
            _family = (ComboBox)labelled("Label All family", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 },
                "Which family Label All and Process Drawing put this feature in (Settings > Label All decides which families run).");

            heading("Descriptions it matches");
            _match = (TextBox)labelled("Match pattern", new TextBox
            {
                Width = 380, Height = 48, Multiline = true, WordWrap = true, AcceptsReturn = false,
                Font = new Font("Consolas", 9f)
            },
                "A regular expression tried against the whole description as the crew typed it (extra spaces " +
                "collapsed, upper or lower case alike). Features are tried top to bottom in the rules file and " +
                "the first enabled one that matches wins. It is not an exact or prefix match on its own: ^ is the start " +
                "of the description and $ the end, and without them it matches anywhere inside one.\n\n" +
                "Named groups become fields: (?<code>...) the code, (?<species>...) the species code, " +
                "(?<trunk>...) trunk sizes, (?<drip>...) a drip radius, (?<rotation>...) an angle, (?<mods>...) the " +
                "words after (modifiers such as DEAD, and notes); any other (?<name>...) is {name} in the label. " +
                "{SPECIES} stands for the species codes listed below.\n\nExample: ^(?<code>MB)\\s+(?<number>[0-9]+)" +
                "(?<mods>(?:\\s+\\S+)*)\\s*$ matches MB 12 and MB 12 DEAD.");
            _matchInfo = note(string.Empty);
            _species = (TextBox)labelled("Species", new TextBox
            {
                Width = 300, Height = 96, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical
            },
                "One per line: CODE = NAME, as on the office code sheet (CON = CONIFER). The code is what the crew " +
                "types; the name is what {species} puts in the label. {SPECIES} in the match pattern stands for exactly " +
                "these codes, so a code not listed here is not this feature: it goes to Unknown Codes with its " +
                "description, ready to add. Codes are matched upper or lower case alike.");

            heading("Label");
            _labelFormat = (TextBox)labelled("Label text", new TextBox { Width = 300, Height = 58, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical },
                "One label line per row. {placeholders} come from the pattern's fields and the note groups ({owner}, {attach}); " +
                "{name:0.0} formats a number. Text in [square brackets] shows only when a placeholder inside it has a value; " +
                "an empty row is dropped. A modifier's suffix (DEAD) goes on the first line.");
            _clusterFormat = (TextBox)labelled("Cluster label", new TextBox { Width = 300, Height = 40, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical },
                "Used instead of the label text when the crew recorded two or more trunk sizes (a cluster, CON 6 8 12 . 20). " +
                "{trunk.count} is the number of stems, {trunk} their combined size (Settings > Trees chooses how stems are " +
                "combined), {trunk.stems} the sizes as shot. Blank: clusters use the label text too. A modifier that " +
                "replaces the whole label (CL3) still wins.");
            _labelLayer = (ComboBox)labelled("Label layer", new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 240, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems },
                "Pick a layer in the drawing or type a new one (created on first use). Settings > Layers can send it to a project layer.");
            foreach (var l in Context.Drawing.Layers) _labelLayer.Items.Add(l);
            _leader = (ComboBox)labelled("Leader", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 },
                "Auto draws a leader only when the label cannot sit next to the point.");
            _leader.Items.AddRange(new object[] { "Auto", "Always", "Never" });

            heading("Notes (the crew's words after the code)");
            _notesOn = new CheckBox { Text = "Read the words after the code as notes", AutoSize = true };
            Tip(_notesOn, "On: the words after the code (the pattern's (?<mods>...) group) are read with the terms below, " +
                          "then as modifiers (DEAD, REM, PROT), and each lands in a group the label can name ({owner}). " +
                          "Off: every word after the code must be a modifier, or the point is an error and is not drawn.");
            full(_notesOn);
            _notesHint = note(string.Empty);
            _notesUnknown = (ComboBox)labelled("A word in no term", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 },
                "What happens to a word none of the terms (and no modifier) reads. Flag: labelled as written and the point " +
                "is listed for review in the exception report, so the office can add the word. Keep: labelled as written, " +
                "quietly. Refuse: the point is an error and is not drawn.");
            _notesUnknownGroup = (TextBox)labelled("Unknown words go in group", new TextBox { Width = 120 },
                "The group a word in no term is kept in, in the order it was written: {other} in the label. " +
                "Letters, digits and _ only.");
            full(new Label { Text = "Terms (tried top to bottom at each word; the first that fits reads it)", AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
            _terms = NewGrid(150);
            _terms.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Words the crew writes", FillWeight = 44,
                ToolTipText = "A regular expression over whole words, upper or lower case alike: XFMR|TRANS. It may span words " +
                              "(NO\\s+PARK(?:ING)?) and capture values for the wording (SPEED\\s*(?<mph>[0-9]+))."
            });
            _terms.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Label wording", FillWeight = 32,
                ToolTipText = "What the label says for it: XFMR, SPEED LIMIT {mph}. {token} is the words as written. Blank: the words as written."
            });
            _terms.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Group", FillWeight = 24,
                ToolTipText = "The group it lands in, named in the label as {group}: owner, attach. Letters, digits and _ only."
            });
            full(_terms);
            var termButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            var up = new Button { Text = "Move Up", AutoSize = true };
            var down = new Button { Text = "Move Down", AutoSize = true };
            var removeTerm = new Button { Text = "Remove Term", AutoSize = true };
            Tip(up, "Terms are tried in this order, and within a group the values read in this order (unless the group reads as written).");
            up.Click += (s, e) => MoveRow(_terms, -1);
            down.Click += (s, e) => MoveRow(_terms, 1);
            removeTerm.Click += (s, e) => RemoveRow(_terms);
            termButtons.Controls.AddRange(new Control[] { up, down, removeTerm });
            _termButtons = termButtons;
            full(termButtons);
            full(new Label { Text = "How each group reads", AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
            _groups = NewGrid(110);
            _groups.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Group", FillWeight = 30, ToolTipText = "A group the terms or the unknown words use." });
            var groupOrder = new DataGridViewComboBoxColumn
            {
                HeaderText = "Values read", FillWeight = 40, FlatStyle = FlatStyle.Flat,
                ToolTipText = "In term order: the order the terms are listed, whatever order the crew wrote them (XFMR TEL and TEL XFMR label " +
                              "the same). As written: the crew's order, for wording such as NO PARKING ANYTIME. (default): as written for the " +
                              "unknown words' group, term order for the rest."
            };
            groupOrder.Items.AddRange(GroupOrderChoices.Cast<object>().ToArray());
            _groups.Columns.Add(groupOrder);
            _groups.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Joined by", FillWeight = 30,
                ToolTipText = "Put between the group's values, spaces included. Blank: a comma and a space in term order, a space as written."
            });
            full(_groups);
            _noteOrder = (TextBox)labelled("{notes} reads groups", new TextBox { Width = 160, Height = 60, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical },
                "One group per line: the order {notes} in the label puts the groups in. Blank: the order the groups first " +
                "appear in the terms, the unknown words last. Naming the groups in the label ({number} {owner}) does not need this.");

            heading("Range checks");
            note("A number read from the description outside its range is reported (Warning: drawn and listed for review; " +
                 "Error: not drawn; Info: noted). A blank or non-numeric value is not checked.");
            _checks = NewGrid(96);
            var checkField = new DataGridViewComboBoxColumn
            {
                HeaderText = "Field", FillWeight = 22, FlatStyle = FlatStyle.Flat,
                ToolTipText = "The number checked: a field of the pattern (drip, trunk, rotation...) or trunk.count, the number of stems."
            };
            _checks.Columns.Add(checkField);
            _checks.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Lowest", FillWeight = 12, ToolTipText = "Blank: no lower limit." });
            _checks.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Highest", FillWeight = 12, ToolTipText = "Blank: no upper limit." });
            var severity = new DataGridViewComboBoxColumn { HeaderText = "If outside", FillWeight = 22, FlatStyle = FlatStyle.Flat, ToolTipText = "Warning: drawn and listed for review. Error: not drawn. Info: noted only." };
            severity.Items.AddRange(SeverityChoices.Cast<object>().ToArray());
            _checks.Columns.Add(severity);
            _checks.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Report says", FillWeight = 32, ToolTipText = "Blank: \"Value x for 'field' is outside the expected range.\" The value found is added." });
            full(_checks);
            var removeCheck = new Button { Text = "Remove Check", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            removeCheck.Click += (s, e) => RemoveRow(_checks);
            full(removeCheck);

            heading("Rotation");
            _rotationOn = new CheckBox { Text = "Turn the Civil 3D marker by an angle in the description", AutoSize = true };
            Tip(_rotationOn, "FTFPOINTS turns the existing marker (FTF never draws a second symbol). A description with no angle " +
                             "leaves the marker as Civil 3D placed it; an angle that is not a number is an error.");
            full(_rotationOn);
            _rotationFrom = (ComboBox)labelled("Angle field", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 },
                "The field of the match pattern that holds the angle, such as (?<rotation>[0-9.]+).");
            _rotationMode = (ComboBox)labelled("The angle is", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 },
                "Azimuth: the way the feature faces, clockwise from north; the symbol's own zero (Settings > Symbols and " +
                "Rotations) is taken off, so a sign faces where the crew shot it. AutoCAD angle: counter-clockwise from " +
                "east, given to the marker as it is.");

            heading("Dripline");
            _dripOn = new CheckBox { Text = "Draw a dripline (canopy circle) with FTFDRIP", AutoSize = true };
            Tip(_dripOn, "FTFDRIP draws a circle of the drip radius around each point this feature matches. A description " +
                         "with no radius is an error (the point is not drawn), so make the radius part of the pattern.");
            full(_dripOn);
            _dripRadius = (ComboBox)labelled("Radius field", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 },
                "The field of the match pattern that holds the drip radius, in survey feet: (?<drip>...) in CON 18 . 25.");
            _dripLayer = (ComboBox)labelled("Dripline layer", new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 240 },
                "Pick a layer in the drawing or type a new one. A modifier's layer suffix (DEAD: -DEAD) is added.");
            foreach (var l in Context.Drawing.Layers) _dripLayer.Items.Add(l);
            _dripUnify = new CheckBox { Text = "Trim where canopies overlap (one outline)", AutoSize = true };
            Tip(_dripUnify, "On: overlapping canopies are trimmed to their outer outline, when Settings > Driplines trims too. " +
                            "Off: this feature's canopies are always drawn as whole circles.");
            full(_dripUnify);

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

            heading("From Civil 3D and other pages");
            _symbolRow = readOnly();
            labelled("Symbol", _symbolRow, "Civil 3D's description key draws the symbol; FTF never draws a second one unless the " +
                                          "feature opts in to inserting a block (Symbols and Rotations).");
            var symbolLink = new LinkLabel { Text = "Edit this feature's symbol and block", AutoSize = true, Margin = new Padding(0, 2, 0, 4) };
            Tip(symbolLink, "Settings > Symbols and Rotations, opened at this feature's symbol rule and inserted block: block, layer, native zero, rotation, scale, insertion point, attributes, with a preview.");
            symbolLink.LinkClicked += (s, e) =>
            {
                if (Context.Navigate == null || _editingId == null) return;
                var compiled = ListedRule(_editingId);
                SymbolsPage.PendingCode = compiled != null ? RuleDocument.LiteralCodes(compiled).FirstOrDefault() : null;
                SymbolsPage.PendingRuleId = _editingId;
                Context.Navigate("Symbols and Rotations");
            };
            full(symbolLink);
            var modifiers = readOnly();
            modifiers.Text = ModifierSummary();
            labelled("Modifiers", modifiers, null);

            heading("Preview");
            _testInput = (TextBox)labelled("Description", new TextBox { Width = 260 },
                "A description as the crew would type it; parsed with your staged edits. Nothing in the drawing changes.");
            _testInput.Tag = Ui.SettingsPanel.ViewOnly;
            _preview = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 400, Height = 130,
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
            var standard = new Button { Text = "Drafting Standard...", AutoSize = true };
            Tip(standard, "What each field code and family needs on the plan (symbol, label, surface, rotation, review, styles, exceptions), " +
                          "read by point accountability, Label All, FTFLABELS and FTFORIENT alike. Staged here; Save Rules writes it.");
            standard.Click += (s, e) => StandardClicked();
            save.Click += (s, e) => { StageEdits(); SaveClicked(); };
            discard.Click += (s, e) => ReloadClicked(true);
            _restoreFactory.Click += (s, e) => RestoreFactoryClicked();
            Tip(save, "Writes every feature edited, added or deleted here, and the drafting standard, to the active rules " +
                      "configuration (atomic, previous version kept as .bak). What other pages saved meanwhile is kept.");
            Tip(discard, "Forgets every staged feature edit, addition and deletion and reads the rules again.");
            Tip(_restoreFactory, "Replaces the office configuration with the shipped rules -- a separate, deliberate act.");
            buttons.Controls.Add(save);
            buttons.Controls.Add(discard);
            buttons.Controls.Add(standard);
            buttons.Controls.Add(_restoreFactory);
            full(buttons);

            foreach (var c in new Control[] { _description, _labelFormat, _clusterFormat, _tagPrefix, _match, _species, _notesUnknownGroup, _noteOrder })
                c.TextChanged += (s, e) => EditMade();
            foreach (var c in new[] { _enabled, _notesOn, _rotationOn, _dripOn, _dripUnify }) c.CheckedChanged += (s, e) => { EnableParts(); EditMade(); };
            foreach (var c in new[] { _family, _leader, _notesUnknown, _rotationFrom, _rotationMode, _dripRadius }) c.SelectedIndexChanged += (s, e) => EditMade();
            foreach (var c in new[] { _labelLayer, _dripLayer }) { c.TextChanged += (s, e) => EditMade(); c.SelectedIndexChanged += (s, e) => EditMade(); }
            foreach (var g in new[] { _terms, _groups, _checks })
            {
                g.CellValueChanged += (s, e) => EditMade();
                g.RowsRemoved += (s, e) => EditMade();
            }

            scroll.Controls.Add(grid);
            return scroll;
        }

        // Plain-language choices, each mapped to the word the rules file stores.
        private static readonly string[] UnknownChoices =
        {
            "Flag: label it as written, list the point for review",
            "Keep: label it as written, quietly",
            "Refuse: the point is an error and is not drawn"
        };
        private static readonly string[] UnknownWords = { "Flag", "Keep", "Refuse" };
        private static readonly string[] GroupOrderChoices = { "(default)", "In term order", "As written" };
        private static readonly string[] GroupOrderWords = { string.Empty, "Vocabulary", "AsShot" };
        private static readonly string[] SeverityChoices = { "Warning", "Error", "Info" };
        private static readonly string[] RotationChoices =
        {
            "Azimuth: clockwise from north, the way it faces",
            "AutoCAD angle: counter-clockwise from east"
        };
        private static readonly string[] RotationWords = { "Azimuth", "Cartesian" };

        private static DataGridView NewGrid(int height)
        {
            var g = new DataGridView
            {
                Width = 440, Height = height,
                AllowUserToAddRows = true, AllowUserToDeleteRows = true, AllowUserToResizeRows = false,
                RowHeadersWidth = 24, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
                EditMode = DataGridViewEditMode.EditOnEnter, BackgroundColor = SystemColors.Window,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };
            // A drop-down cell commits as soon as it is chosen, so the preview follows at once.
            g.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (g.IsCurrentCellDirty && g.CurrentCell is DataGridViewComboBoxCell) g.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            g.DataError += (s, e) => { e.ThrowException = false; };
            return g;
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
            _stage.Stop();
            _resolution =Context.RulesResolution == null
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
            _touched.Clear();
            _standardTouched = false;
            ShowSource();
            RefreshCandidate(true);
            // What the editor holds belongs to the document just discarded: it is shown afresh, never staged.
            var keep = SelectedId();
            _editingId = null;
            PopulateGrid(keep);
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

        /// <summary>The staged rules compiled for the preview. While they do not compile (a pattern
        /// being typed) the last good compile is kept and the checks say what is wrong.</summary>
        private void RefreshCandidate(bool report = false)
        {
            if (_document == null) { _candidate = Context.Rules; return; }
            try { _candidate = _document.CompileCandidate(); }
            catch (ConfigException ex) { if (report) _problems.Text = ex.Message; }
            catch (Newtonsoft.Json.JsonException ex) { if (report) _problems.Text = "The rules do not read: " + ex.Message; }
        }

        /// <summary>The feature as the staged document has it, compiled or not: what the list and the
        /// editor's headings show (id, name, family, enabled, codes).</summary>
        private CodeRule ListedRule(string id)
        {
            if (id == null) return null;
            var e = _document != null ? _document.GetEditable(id) : null;
            if (e == null)
                return _candidate != null ? _candidate.Codes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) : null;
            var rule = new CodeRule { Id = e.Id, Description = e.Description, Category = e.Category, Enabled = e.Enabled, Match = e.Match };
            foreach (var p in e.Species.Where(p => !string.IsNullOrWhiteSpace(p.Key))) rule.Species[p.Key.Trim()] = p.Value;
            return rule;
        }

        private IEnumerable<CodeRule> ListedRules()
        {
            if (_document != null) return _document.RuleIds().Select(ListedRule).Where(r => r != null);
            return _candidate != null ? _candidate.Codes : Enumerable.Empty<CodeRule>();
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
            {
                foreach (var rule in ListedRules())
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
                foreach (var c in EditorInputs()) c.Enabled = have;
                if (_delete != null) _delete.Enabled = have;
                if (!have)
                {
                    _identity.Text = "(select a feature)";
                    _matchInfo.Text = string.Empty;
                    _notesHint.Text = string.Empty;
                    return;
                }

                _identity.Text = (string.IsNullOrEmpty(rule.Description) ? rule.Id : rule.Description) +
                                 "  --  code " + CodesOfSelected(id) + "  (rule '" + rule.Id + "')";
                _symbolRow.Text = rule.SymbolSummary;
                _description.Text = rule.Description;
                _enabled.Checked = rule.Enabled;

                _family.Items.Clear();
                var compiled = ListedRule(id);
                var auto = compiled != null ? AnnotationPolicy.Name(AnnotationPolicy.CategoryOf(new ParsedPoint { RuleId = id, Code = RuleDocument.LiteralCodes(compiled).FirstOrDefault() }, null)) : "-";
                _family.Items.Add(Automatic + " " + auto);
                foreach (var c in AnnotationPolicy.All().Where(c => c != AnnotationCategory.Lines)) _family.Items.Add(new FamilyChoice(c));
                AnnotationCategory own;
                _family.SelectedIndex = !string.IsNullOrWhiteSpace(rule.Category) && Enum.TryParse(rule.Category, true, out own)
                    ? AnnotationPolicy.All().Where(c => c != AnnotationCategory.Lines).ToList().IndexOf(own) + 1 : 0;

                _match.Text = rule.Match ?? string.Empty;
                _species.Text = string.Join(Environment.NewLine, rule.Species.Select(p => p.Key + " = " + p.Value).ToArray());
                _labelFormat.Text = Lines(rule.LabelFormat);
                _clusterFormat.Text = Lines(rule.ClusterFormat);
                _labelLayer.Text = rule.LabelLayer ?? string.Empty;
                _leader.SelectedItem = rule.Leader;
                if (_leader.SelectedIndex < 0) _leader.SelectedIndex = 0;
                _tagPrefix.Text = rule.TagPrefix;

                var info = RuleDocument.ReadPattern(rule.Match, rule.Species.Select(p => p.Key));
                FillFieldChoices(info, rule.RotationFrom, rule.DripRadiusFrom);

                _notesOn.Checked = rule.HasNotes;
                FillChoices(_notesUnknown, UnknownChoices, UnknownWords, rule.NotesUnknown);
                _notesUnknownGroup.Text = rule.NotesUnknownGroup ?? string.Empty;
                FillTerms(rule.NoteTerms);
                FillGroups(rule);
                _noteOrder.Text = string.Join(Environment.NewLine, rule.NoteOrder.ToArray());

                FillChecks(rule.Checks, info);

                _rotationOn.Checked = rule.HasRotation;
                FillChoices(_rotationMode, RotationChoices, RotationWords, rule.RotationMode);

                _dripOn.Checked = rule.HasDripline;
                _dripLayer.Text = rule.DripLayer ?? string.Empty;
                _dripUnify.Checked = rule.DripUnify;

                ShowPattern(info);
                EnableParts();

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

        /// <summary>Every input of the rule editor, enabled only while a feature is selected.</summary>
        private IEnumerable<Control> EditorInputs()
        {
            return new Control[]
            {
                _enabled, _description, _family, _match, _species, _labelFormat, _clusterFormat, _labelLayer, _leader,
                _notesOn, _notesUnknown, _notesUnknownGroup, _terms, _termButtons, _groups, _noteOrder, _checks,
                _rotationOn, _rotationFrom, _rotationMode, _dripOn, _dripRadius, _dripLayer, _dripUnify, _tagPrefix
            };
        }

        /// <summary>A part's own settings are editable only while the part is switched on.</summary>
        private void EnableParts()
        {
            var have = _editingId != null && _document != null && _enabled.Enabled;
            foreach (var c in new Control[] { _notesUnknown, _notesUnknownGroup, _terms, _termButtons, _groups, _noteOrder })
                c.Enabled = have && _notesOn.Checked;
            foreach (var c in new Control[] { _rotationFrom, _rotationMode }) c.Enabled = have && _rotationOn.Checked;
            foreach (var c in new Control[] { _dripRadius, _dripLayer, _dripUnify }) c.Enabled = have && _dripOn.Checked;
            if (_loading) return;

            // Switching a part on starts it from the usual field and layer, so it works at once.
            if (_rotationOn.Checked && _rotationFrom.SelectedIndex < 0 && _rotationFrom.Items.Contains("rotation")) _rotationFrom.SelectedItem = "rotation";
            if (_dripOn.Checked && _dripRadius.SelectedIndex < 0 && _dripRadius.Items.Contains("drip")) _dripRadius.SelectedItem = "drip";
            if (_dripOn.Checked && string.IsNullOrWhiteSpace(_dripLayer.Text)) _dripLayer.Text = PmxLayers.DripLayer;
        }

        /// <summary>What the pattern matches, in words, and which parts of the editor it makes relevant.</summary>
        private void ShowPattern(PatternInfo info)
        {
            var ids = _document != null ? _document.RuleIds() : new List<string>();
            var at = _editingId != null ? ids.ToList().FindIndex(i => string.Equals(i, _editingId, StringComparison.OrdinalIgnoreCase)) : -1;
            var order = at >= 0 ? " Tried " + Ordinal(at + 1) + " of the " + ids.Count + " features; the first enabled one that matches wins." : string.Empty;
            if (info.Problem != null)
                _matchInfo.Text = "Problem: " + info.Problem;
            else
                _matchInfo.Text = "Codes: " + (info.Codes.Count > 0 ? string.Join(", ", info.Codes.Take(12).ToArray()) + (info.Codes.Count > 12 ? ", ..." : string.Empty)
                                               : "none spelled out (the pattern is not a plain list of codes)") +
                                  ". Fields: " + (info.Fields.Count > 0 ? string.Join(", ", info.Fields.ToArray()) : "none") + "." + order;

            ShowRow(_species, info.UsesSpecies || (_species.Text ?? string.Empty).Trim().Length > 0);
            ShowRow(_clusterFormat, info.ReadsTrunk || (_clusterFormat.Text ?? string.Empty).Trim().Length > 0);
            _notesHint.Text = info.Problem != null || info.ReadsWords ? string.Empty
                : "The match pattern keeps no words after the code (no (?<mods>...) group), so neither notes nor modifiers are ever read.";
            _notesHint.Visible = _notesHint.Text.Length > 0;
        }

        private static string Ordinal(int n)
        {
            var teen = n % 100 >= 11 && n % 100 <= 13;
            var suffix = teen ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th";
            return n.ToString(CultureInfo.InvariantCulture) + suffix;
        }

        private void ShowRow(Control c, bool visible)
        {
            if (!visible && c.ContainsFocus) return;     // never hide the box being typed in
            c.Visible = visible;
            Label caption;
            if (_captions.TryGetValue(c, out caption)) caption.Visible = visible;
        }

        private static string Lines(string template)
        {
            return (template ?? string.Empty).Replace("\\P", Environment.NewLine);
        }

        private static string Template(string text)
        {
            return string.Join("\\P", (text ?? string.Empty).Replace("\r\n", "\n").Split('\n')
                .Select(line => line.TrimEnd()).Where(line => line.Length > 0).ToArray());
        }

        // ------------------------------------------------------------------ choices

        /// <summary>A drop-down entry: the plain words shown and the word the rules file stores.</summary>
        private sealed class Choice
        {
            public readonly string Word;
            private readonly string _text;
            public Choice(string word, string text) { Word = word; _text = text; }
            public override string ToString() { return _text; }
        }

        /// <summary>Fills a drop-down with the choices; a word the file has that is none of them is
        /// shown as it is, so nothing is changed behind the user's back.</summary>
        private static void FillChoices(ComboBox box, string[] texts, string[] words, string current)
        {
            box.Items.Clear();
            for (var i = 0; i < words.Length; i++) box.Items.Add(new Choice(words[i], texts[i]));
            var at = Array.FindIndex(words, w => string.Equals(w, (current ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (at < 0 && !string.IsNullOrWhiteSpace(current))
            {
                box.Items.Add(new Choice(current, "'" + current + "' -- not understood: choose one"));
                at = box.Items.Count - 1;
            }
            box.SelectedIndex = at < 0 ? 0 : at;
        }

        private static string WordOf(ComboBox box, string fallback)
        {
            var c = box.SelectedItem as Choice;
            return c != null ? c.Word : fallback;
        }

        /// <summary>The fields a rotation, dripline or range check can read: the pattern's named groups
        /// that can hold a number. Each box keeps what it had, so a field the pattern lost still shows
        /// (and the checks name it).</summary>
        private void FillFieldChoices(PatternInfo info, string rotationFrom, string dripFrom)
        {
            var fields = NumberFields(info);
            foreach (var pair in new[] { Tuple.Create(_rotationFrom, rotationFrom), Tuple.Create(_dripRadius, dripFrom) })
            {
                var box = pair.Item1;
                var keep = pair.Item2 ?? string.Empty;
                box.BeginUpdate();
                box.Items.Clear();
                foreach (var f in fields) box.Items.Add(f);
                if (keep.Length > 0 && !box.Items.Contains(keep)) box.Items.Add(keep);
                box.SelectedItem = keep.Length > 0 ? keep : null;
                if (keep.Length == 0) box.SelectedIndex = -1;
                box.EndUpdate();
            }
        }

        private static List<string> NumberFields(PatternInfo info)
        {
            return info.Fields.Where(f => !new[] { "code", "species", "mods" }.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        private static string FieldOf(ComboBox box)
        {
            return box.SelectedItem as string ?? string.Empty;
        }

        // ------------------------------------------------------------------ lists

        private void FillTerms(IEnumerable<EditableNoteTerm> terms)
        {
            _terms.Rows.Clear();
            foreach (var t in terms)
            {
                var i = _terms.Rows.Add(t.Match, t.Text, t.Group);
                _terms.Rows[i].Tag = t.SourceIndex;
            }
        }

        /// <summary>Every group the feature uses, each with its reading order and joiner (blank: the default).</summary>
        private void FillGroups(EditableRule rule)
        {
            var orderColumn = (DataGridViewComboBoxColumn)_groups.Columns[1];
            orderColumn.Items.Clear();
            orderColumn.Items.AddRange(GroupOrderChoices.Cast<object>().ToArray());
            var names = new List<string>();
            Action<string> add = n => { if (!string.IsNullOrWhiteSpace(n) && !names.Contains(n.Trim(), StringComparer.OrdinalIgnoreCase)) names.Add(n.Trim()); };
            foreach (var g in rule.NoteGroups) add(g.Name);
            foreach (var t in rule.NoteTerms) add(t.Group);
            add(rule.NotesUnknownGroup);
            foreach (var o in rule.NoteOrder) add(o);

            _groups.Rows.Clear();
            foreach (var name in names)
            {
                var own = rule.NoteGroups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
                var word = own != null ? own.Order ?? string.Empty : string.Empty;
                var at = Array.FindIndex(GroupOrderWords, w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase));
                var shown = at >= 0 ? GroupOrderChoices[at] : word;
                if (at < 0 && !orderColumn.Items.Contains(word)) orderColumn.Items.Add(word);
                _groups.Rows.Add(name, shown, own != null ? own.Joiner : null);
            }
        }

        private void FillChecks(IEnumerable<EditableRangeCheck> checks, PatternInfo info)
        {
            var list = checks.ToList();
            var fieldColumn = (DataGridViewComboBoxColumn)_checks.Columns[0];
            var severityColumn = (DataGridViewComboBoxColumn)_checks.Columns[3];
            fieldColumn.Items.Clear();
            foreach (var f in CheckFields(info)) fieldColumn.Items.Add(f);
            foreach (var c in list.Where(c => !string.IsNullOrWhiteSpace(c.Field) && !fieldColumn.Items.Contains(c.Field))) fieldColumn.Items.Add(c.Field);
            severityColumn.Items.Clear();
            severityColumn.Items.AddRange(SeverityChoices.Cast<object>().ToArray());
            foreach (var c in list.Where(c => !string.IsNullOrWhiteSpace(c.Severity) && !SeverityChoices.Contains(c.Severity, StringComparer.OrdinalIgnoreCase)))
                if (!severityColumn.Items.Contains(c.Severity)) severityColumn.Items.Add(c.Severity);

            _checks.Rows.Clear();
            foreach (var c in list)
            {
                var severity = SeverityChoices.FirstOrDefault(x => string.Equals(x, c.Severity, StringComparison.OrdinalIgnoreCase)) ?? c.Severity;
                var i = _checks.Rows.Add(string.IsNullOrWhiteSpace(c.Field) ? null : c.Field, Number(c.Min), Number(c.Max), severity, c.Message);
                _checks.Rows[i].Tag = c.SourceIndex;
            }
        }

        /// <summary>The fields a range check can read: the pattern's, the stem count and exact size when
        /// it reads trunk sizes, and what a modifier captures (CL3: count).</summary>
        private IEnumerable<string> CheckFields(PatternInfo info)
        {
            var fields = NumberFields(info);
            if (info.ReadsTrunk) fields.AddRange(new[] { "trunk.count", "trunk.exact" });
            if (_candidate != null)
                foreach (var m in _candidate.Modifiers.Where(m => m.Regex != null))
                    foreach (var g in m.Regex.GetGroupNames())
                    {
                        int ignored;
                        if (!int.TryParse(g, out ignored) && !fields.Contains(g, StringComparer.OrdinalIgnoreCase)) fields.Add(g);
                    }
            return fields;
        }

        private static string Number(double? v)
        {
            return v.HasValue ? v.Value.ToString("R", CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string Cell(DataGridViewRow row, int column)
        {
            return (Convert.ToString(row.Cells[column].Value) ?? string.Empty).Trim();
        }

        private static void MoveRow(DataGridView grid, int by)
        {
            var row = grid.CurrentRow;
            if (row == null || row.IsNewRow) return;
            grid.EndEdit();
            var from = row.Index;
            var to = from + by;
            if (to < 0 || to >= grid.Rows.Count - (grid.AllowUserToAddRows ? 1 : 0)) return;
            var column = grid.CurrentCell != null ? grid.CurrentCell.ColumnIndex : 0;
            grid.Rows.RemoveAt(from);
            grid.Rows.Insert(to, row);
            grid.CurrentCell = row.Cells[column];
        }

        private static void RemoveRow(DataGridView grid)
        {
            var row = grid.CurrentRow;
            if (row == null || row.IsNewRow) return;
            grid.EndEdit();
            grid.Rows.Remove(row);
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
            var rule = ListedRule(id);
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
            foreach (var g in new[] { _terms, _groups, _checks }) g.EndEdit();

            edit.Enabled = _enabled.Checked;
            edit.Description = (_description.Text ?? string.Empty).Trim();
            edit.Match = (_match.Text ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
            edit.Species = ReadSpecies();
            edit.LabelFormat = Template(_labelFormat.Text);
            edit.ClusterFormat = Template(_clusterFormat.Text);
            edit.LabelLayer = (_labelLayer.Text ?? string.Empty).Trim();
            edit.Leader = _leader.SelectedItem != null ? Convert.ToString(_leader.SelectedItem) : "Auto";
            edit.TagPrefix = (_tagPrefix.Text ?? string.Empty).Trim();
            var choice = _family.SelectedItem as FamilyChoice;
            edit.Category = choice != null ? choice.Category.ToString() : string.Empty;

            edit.HasNotes = _notesOn.Checked;
            edit.NotesUnknown = WordOf(_notesUnknown, edit.NotesUnknown);
            edit.NotesUnknownGroup = (_notesUnknownGroup.Text ?? string.Empty).Trim();
            var termRows = new List<DataGridViewRow>();
            edit.NoteTerms = ReadTerms(termRows);
            edit.NoteGroups = ReadGroups();
            edit.NoteOrder = (_noteOrder.Text ?? string.Empty).Replace("\r\n", "\n").Split('\n')
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

            string checksProblem;
            var checkRows = new List<DataGridViewRow>();
            var checks = ReadChecks(checkRows, out checksProblem);
            if (checksProblem == null) edit.Checks = checks;     // a box that is not a number keeps the file's checks

            edit.HasRotation = _rotationOn.Checked;
            edit.RotationFrom = FieldOf(_rotationFrom);
            edit.RotationMode = WordOf(_rotationMode, edit.RotationMode);

            edit.HasDripline = _dripOn.Checked;
            edit.DripLayer = (_dripLayer.Text ?? string.Empty).Trim();
            edit.DripRadiusFrom = FieldOf(_dripRadius);
            edit.DripUnify = _dripUnify.Checked;

            var before = _document.Render();
            try { _document.ApplyEdit(edit); }
            catch (ConfigException ex)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = ex.Message;
                return;
            }

            // The list rows now stand for the entries as written (ApplyEdit renumbers them).
            for (var i = 0; i < termRows.Count; i++) termRows[i].Tag = edit.NoteTerms[i].SourceIndex;
            if (checksProblem == null) for (var i = 0; i < checkRows.Count; i++) checkRows[i].Tag = checks[i].SourceIndex;

            var info = RuleDocument.ReadPattern(edit.Match, edit.Species.Select(p => p.Key));
            var wasLoading = _loading;
            _loading = true;
            try
            {
                FillFieldChoices(info, edit.RotationFrom, edit.DripRadiusFrom);
                var fieldColumn = (DataGridViewComboBoxColumn)_checks.Columns[0];
                foreach (var f in CheckFields(info)) if (!fieldColumn.Items.Contains(f)) fieldColumn.Items.Add(f);
                ShowPattern(info);
            }
            finally { _loading = wasLoading; }

            if (_document.Render() == before)
            {
                if (checksProblem != null) { _problems.ForeColor = Color.FromArgb(170, 0, 0); _problems.Text = checksProblem; }
                return;
            }

            if (!_touched.Contains(id, StringComparer.OrdinalIgnoreCase)) _touched.Add(id);
            var problems = _document.Validate().ToList();
            if (checksProblem != null) problems.Insert(0, checksProblem);
            _problems.ForeColor = Color.FromArgb(170, 0, 0);
            _problems.Text = problems.Count > 0 ? string.Join(Environment.NewLine, problems.ToArray()) : string.Empty;
            SetDirty(true);
            RefreshCandidate();
            UpdateRow(id);
            RunPreview();
        }

        /// <summary>The species box: one CODE = NAME per line (a line without = is a code with no name,
        /// which the checks name).</summary>
        private List<KeyValuePair<string, string>> ReadSpecies()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var raw in (_species.Text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var eq = line.IndexOf('=');
                list.Add(eq < 0
                    ? new KeyValuePair<string, string>(line, string.Empty)
                    : new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
            }
            return list;
        }

        private List<EditableNoteTerm> ReadTerms(List<DataGridViewRow> rows)
        {
            var terms = new List<EditableNoteTerm>();
            foreach (DataGridViewRow row in _terms.Rows)
            {
                if (row.IsNewRow) continue;
                var t = new EditableNoteTerm
                {
                    SourceIndex = row.Tag is int ? (int)row.Tag : -1,
                    Match = Cell(row, 0), Text = Cell(row, 1), Group = Cell(row, 2)
                };
                if (t.Match.Length == 0 && t.Text.Length == 0 && t.Group.Length == 0) continue;
                terms.Add(t);
                rows.Add(row);
            }
            return terms;
        }

        private List<EditableNoteGroup> ReadGroups()
        {
            var groups = new List<EditableNoteGroup>();
            foreach (DataGridViewRow row in _groups.Rows)
            {
                if (row.IsNewRow) continue;
                var shown = Cell(row, 1);
                var at = Array.IndexOf(GroupOrderChoices, shown);
                var joiner = Convert.ToString(row.Cells[2].Value);
                groups.Add(new EditableNoteGroup
                {
                    Name = Cell(row, 0),
                    Order = at >= 0 ? GroupOrderWords[at] : shown,
                    Joiner = string.IsNullOrEmpty(joiner) ? null : joiner
                });
            }
            return groups;
        }

        private List<EditableRangeCheck> ReadChecks(List<DataGridViewRow> rows, out string problem)
        {
            problem = null;
            var checks = new List<EditableRangeCheck>();
            var n = 0;
            foreach (DataGridViewRow row in _checks.Rows)
            {
                if (row.IsNewRow) continue;
                n++;
                var c = new EditableRangeCheck
                {
                    SourceIndex = row.Tag is int ? (int)row.Tag : -1,
                    Field = Cell(row, 0),
                    Severity = Cell(row, 3).Length > 0 ? Cell(row, 3) : "Warning",
                    Message = Cell(row, 4)
                };
                for (var k = 1; k <= 2; k++)
                {
                    var text = Cell(row, k);
                    if (text.Length == 0) continue;
                    double v;
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsNaN(v) || double.IsInfinity(v))
                    {
                        if (problem == null)
                            problem = RuleDocument.PointFeaturesPrefix + "range check " + n + ": the " + (k == 1 ? "lowest" : "highest") +
                                      " value '" + text + "' is not a number (not staged until it is).";
                        continue;
                    }
                    if (k == 1) c.Min = v; else c.Max = v;
                }
                if (c.Field.Length == 0 && !c.Min.HasValue && !c.Max.HasValue && c.Message.Length == 0 && Cell(row, 1).Length == 0 && Cell(row, 2).Length == 0) continue;
                checks.Add(c);
                rows.Add(row);
            }
            return checks;
        }

        private void UpdateRow(string id)
        {
            var rule = ListedRule(id);
            if (rule == null) return;
            foreach (ListViewItem item in _grid.Items)
            {
                if (!string.Equals((string)item.Tag, id, StringComparison.OrdinalIgnoreCase)) continue;
                item.SubItems[0].Text = CodesOf(rule);
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
            if (!_dirty)
            {
                _problems.ForeColor = SystemColors.ControlText;
                _problems.Text = "No rule changes to save.";
                return;
            }
            var problems = _document.Validate();
            if (problems.Count > 0)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = "Not saved: " + string.Join(Environment.NewLine, problems.ToArray());
                return;
            }
            try
            {
                // The file as it is now -- Symbols and Rotations, Benches, Wetland Buffers or Line Labels
                // may have saved it since this page read it -- with this page's own changes put on it:
                // the features edited, added or deleted here, and the drafting standard. So no page's
                // save undoes another's. The first save from the factory rules starts from them.
                var source = System.IO.File.Exists(target) ? target : _resolution.ActivePath;
                var fresh = RuleDocument.Load(source);
                var order = _document.RuleIds().ToList();
                foreach (var id in _touched.OrderBy(i => order.FindIndex(o => string.Equals(o, i, StringComparison.OrdinalIgnoreCase))))
                    _document.CopyCodeRuleTo(fresh, id);
                if (_standardTouched)
                {
                    if (_document.FeatureStandardIsBuiltIn()) fresh.UseBuiltInFeatureStandard();
                    else fresh.SetFeatureStandard(_document.GetFeatureRequirements(), _document.GetSignCategories());
                }

                problems = fresh.Validate();
                if (problems.Count > 0)
                {
                    _problems.ForeColor = Color.FromArgb(170, 0, 0);
                    _problems.Text = "Not saved (with what is in the file now): " + string.Join(Environment.NewLine, problems.ToArray());
                    return;
                }

                // Never the factory file: SaveTarget is the office configuration, or a
                // drawing override when one is active.
                fresh.Save(target);
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
            catch (UnauthorizedAccessException ex)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = "Could not write: " + ex.Message;
            }
        }

        // ------------------------------------------------------------------ add and delete

        /// <summary>Delete Feature: says what the feature is and what else names it, then stages the
        /// deletion. Refused for the last feature (the rules need one).</summary>
        private void DeleteClicked()
        {
            if (_document == null || _editingId == null) return;
            StageEdits();
            var id = _editingId;
            var rule = _document.GetEditable(id);
            if (rule == null) return;
            var name = string.IsNullOrWhiteSpace(rule.Description) ? id : rule.Description.Trim();
            var codes = CodesOfSelected(id);

            var text = new System.Text.StringBuilder();
            text.AppendLine("Delete the feature " + name + " (code " + codes + ", rule '" + id + "')?");
            text.AppendLine();
            text.AppendLine("Points coded " + codes + " with anything after the code then go to the next feature that " +
                            "matches them, else to Unknown Codes; a bare code is left as Civil 3D drew it. Nothing in a " +
                            "drawing changes now.");
            var also = _document.ReferencesTo(id).ToList();
            var styles = _workspace != null && _workspace.Styles != null && _workspace.Styles.Assignments != null
                ? _workspace.Styles.Assignments.Count(a => a != null && !string.IsNullOrWhiteSpace(a.Rule) &&
                                                           string.Equals(a.Rule.Trim(), id, StringComparison.OrdinalIgnoreCase))
                : 0;
            if (styles > 0)
                also.Add("Settings > Style Assignments has " + styles + " row(s) for this feature: they stay, and stop applying. " +
                         "Remove them there (saved with the window's Save).");
            if (also.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Also:");
                foreach (var a in also) text.AppendLine("  - " + a);
            }
            text.AppendLine();
            text.Append("Nothing is written until Save Rules; Discard Edits brings it back.");

            var answer = MessageBox.Show(_grid, text.ToString(), "Delete Feature", MessageBoxButtons.YesNo,
                                         MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            var ids = _document.RuleIds().ToList();
            var at = ids.FindIndex(i => string.Equals(i, id, StringComparison.OrdinalIgnoreCase));
            try { _document.DeleteRule(id); }
            catch (ConfigException ex)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = ex.Message;
                return;
            }
            if (!_touched.Contains(id, StringComparer.OrdinalIgnoreCase)) _touched.Add(id);
            _editingId = null;
            SetDirty(true);
            RefreshCandidate();
            var remaining = _document.RuleIds();
            PopulateGrid(remaining.Count == 0 ? null : remaining[Math.Min(Math.Max(at, 0), remaining.Count - 1)]);
            var problems = _document.Validate();
            _problems.ForeColor = problems.Count > 0 ? Color.FromArgb(170, 0, 0) : SystemColors.ControlText;
            _problems.Text = problems.Count > 0
                ? string.Join(Environment.NewLine, problems.ToArray())
                : "Deleted " + name + ": Save Rules writes it, Discard Edits brings it back.";
        }

        /// <summary>New Feature: a starter rule for a code no feature handles, or the feature that
        /// already handles it.</summary>
        private void NewClicked()
        {
            if (_document == null || _candidate == null) return;
            StageEdits();
            var code = PromptForCode(_grid != null ? _grid.FindForm() : null);
            if (string.IsNullOrWhiteSpace(code)) return;
            var bare = code.Trim().ToUpperInvariant();
            var parser = new FieldCodeParser(_candidate);
            var handled = ListedRules().FirstOrDefault(r => r.Enabled &&
                (RuleDocument.LiteralCodes(r).Any(c => string.Equals(c, bare, StringComparison.OrdinalIgnoreCase)) ||
                 (r.Species != null && r.Species.ContainsKey(bare))));
            if (handled != null)
            {
                _search.Text = string.Empty;
                _familyFilter.SelectedIndex = 0;
                PopulateGrid(handled.Id);
                _problems.ForeColor = SystemColors.ControlText;
                _problems.Text = bare + " is already handled by " + (string.IsNullOrEmpty(handled.Description) ? handled.Id : handled.Description) + " (selected).";
                return;
            }
            string id;
            try { id = _document.AddStarterRule(bare, PmxLayers.LineLabelFallback); }
            catch (ConfigException ex)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = ex.Message;
                return;
            }
            if (!_touched.Contains(id, StringComparer.OrdinalIgnoreCase)) _touched.Add(id);
            SetDirty(true);
            RefreshCandidate();
            _search.Text = string.Empty;
            _familyFilter.SelectedIndex = 0;
            PopulateGrid(id);
            _testInput.Text = bare + " 1";
            var sample = parser.Parse("preview", bare + " 1");
            _problems.ForeColor = SystemColors.ControlText;
            _problems.Text = "New feature for " + bare + (sample.Recognized ? " (it was handled by " + sample.RuleId + " before; the first match wins)" : string.Empty) +
                             ": set its name, label, layer and family, then Save Rules.";
        }

        /// <summary>A small prompt for the new feature's field code.</summary>
        private static string PromptForCode(IWin32Window owner)
        {
            using (var form = new Form
            {
                Text = "New Feature", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10)
            })
            {
                var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
                layout.Controls.Add(new Label
                {
                    Text = "The field code the crew types for it (letters first, then letters or digits), such as WMT:",
                    AutoSize = true, MaximumSize = new Size(320, 0)
                });
                var box = new TextBox { Width = 160, CharacterCasing = CharacterCasing.Upper };
                layout.Controls.Add(box);
                var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
                var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, AutoSize = true };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);
                layout.Controls.Add(buttons);
                form.Controls.Add(layout);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                return form.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
            }
        }

        /// <summary>Settings > Point Features > Drafting Standard: edits the staged document's
        /// featureStandard; Save Rules writes it with the rest.</summary>
        private void StandardClicked()
        {
            if (_document == null) return;
            StageEdits();
            try
            {
                using (var form = new FeatureStandardForm(_document))
                {
                    if (form.ShowDialog(_grid != null ? _grid.FindForm() : null) != DialogResult.OK) return;
                }
                _standardTouched = true;
            }
            catch (ConfigException ex)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = ex.Message;
                return;
            }
            SetDirty(true);
            RefreshCandidate();
            RunPreview();
            _problems.ForeColor = SystemColors.ControlText;
            _problems.Text = "Drafting standard staged" + (_document.FeatureStandardIsBuiltIn() ? " (the built-in PMX standard)" : string.Empty) +
                             ": Save Rules writes it.";
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
                var standard = FieldCodes.Standards.FeatureRequirements.For(_candidate, parsed, text);
                _preview.Text = FeaturePreview.Render(parsed, title) + Environment.NewLine +
                                Details(parsed, rule) +
                                "Label All family: " + AnnotationPolicy.Name(AnnotationPolicy.CategoryOf(parsed, _candidate)) + Environment.NewLine +
                                "Drafting standard: " + standard.Source + " -- " + standard.Describe() + " (Drafting Standard...)";
            }
            catch (System.Exception ex)
            {
                _preview.Text = ex.Message;
            }
            ShowAppearance();
        }

        /// <summary>What the parse made of the parts edited on this page that the summary above does
        /// not show: species, cluster, notes by group, how the dripline is drawn, and every check
        /// the description set off (the summary names only the first error).</summary>
        private static string Details(ParsedPoint parsed, CodeRule rule)
        {
            var lines = new List<string>();
            if (rule != null && parsed.Recognized && !parsed.Ignored)
            {
                string code;
                if (!string.IsNullOrEmpty(parsed.Species))
                    lines.Add("Species: " + parsed.Species + (parsed.Fields.TryGetValue("code", out code) ? " (" + code + ")" : string.Empty));
                if (parsed.Stems.Count > 1)
                    lines.Add("Cluster of " + parsed.Stems.Count + " stems: " +
                              (rule.Label != null && !string.IsNullOrEmpty(rule.Label.ClusterFormat) ? "the cluster label" : "the label text") + " is used.");
                if (rule.Notes != null)
                {
                    var groups = new List<string>();
                    foreach (var g in rule.Notes.GroupNames())
                    {
                        string value;
                        if (parsed.Fields.TryGetValue(g, out value) && !string.IsNullOrWhiteSpace(value)) groups.Add(g + " = " + value);
                    }
                    lines.Add("Notes: " + (groups.Count > 0 ? string.Join("; ", groups.ToArray()) : "none read"));
                }
                if (parsed.HasDripLine)
                    lines.Add("Dripline drawn " + (parsed.DripUnify ? "trimmed where canopies overlap (when Settings > Driplines trims)" : "as a whole circle"));
            }
            var shown = parsed.HasErrors ? parsed.Diagnostics.First(d => d.Severity == Severity.Error) : null;
            foreach (var d in parsed.Diagnostics.Where(d => d != shown && d.Code != "UNHANDLED"))
                lines.Add(d.Severity + ": " + d.Message);
            return lines.Count > 0 ? string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine : string.Empty;
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
                    if (!_touched.Contains(target, StringComparer.OrdinalIgnoreCase)) _touched.Add(target);
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
