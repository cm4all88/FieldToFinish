using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Cad.Ui;
using FieldCodes.Editing;
using FieldCodes.Geometry;
using FieldCodes.Orient;
using FieldCodes.Settings;
using FieldCodes.Symbols;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Symbols and Rotations, two tabs over the active rules file:
    ///
    /// Symbol standard -- the "orientation" symbol rules FTFORIENT and FTFPOINTS use to turn the
    /// markers Civil 3D draws: codes (first match wins, so a code's own rule goes above its
    /// family's: "Override for Code"), kind, block, native zero, how often the symbol repeats,
    /// two-sided signs and labels that turn with the marker; plus when FTFORIENT applies a decision.
    /// A rules file with no list of its own uses the built-in PMX list; the first edit copies that
    /// list into the file (the page says so).
    ///
    /// Blocks FTF inserts -- the code rules that name a block, and how FTFPOINTS places it when the
    /// rule opts in: block, layer, scale (fixed or from a field), insertion (on the point or
    /// offset), rotation (field azimuth, none, the point's marker), whether it is inserted at all
    /// (turning that on asks first: it puts a second symbol over Civil 3D's), and attribute text.
    ///
    /// Blocks are chosen from the drawing's and the approved symbol library's; a library block is
    /// brought into the drawing only by Bring into Drawing. Previews draw the block turned to a
    /// sample facing with its native zero and insertion point marked.
    ///
    /// Edits are staged on a RuleDocument (comments and unknown properties kept); Save Symbols
    /// re-reads the file and writes this page's parts onto it, to the office configuration or the
    /// drawing override -- never the factory file.
    ///
    /// UNTESTED as UI; the document editing, validation, preview arithmetic and placement rules are
    /// unit tested.
    /// </summary>
    internal sealed class SymbolsPage : SetupPage
    {
        private static readonly Color Bad = Color.FromArgb(170, 0, 0);
        private const string LabelBlock = "(label)";

        // ---- state
        private RulesResolution _resolution;
        private string _activePath;
        private RuleDocument _document;
        private RulesConfig _candidate;
        private List<EditableSymbol> _symbols = new List<EditableSymbol>();
        private bool _builtIn = true;
        private bool _symbolsTouched;
        private bool _applyTouched;
        private readonly HashSet<string> _blockTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _extraBlockRules = new List<string>();
        private bool _dirty;
        private bool _loading;
        private string _fallbackLibrary = string.Empty;
        private IList<string> _libraryBlocks = new List<string>();
        private string _libraryProblem;
        private string _libraryRead;
        private int _symbolIndex = -1;
        private string _blockId;
        private readonly Timer _stage = new Timer { Interval = 350 };
        private Action _pending;

        // ---- shared
        private Label _source;
        private Label _problems;
        private Label _unsaved;
        private Label _libraryNote;
        private TextBox _library;
        private Button _save;

        // ---- symbol standard
        private Label _banner;
        private ListView _symbolGrid;
        private Label _symbolIdentity;
        private TextBox _codes;
        private ComboBox _kind;
        private BlockChooser _symbolBlock;
        private Button _bringSymbol;
        private NumericUpDown _zero;
        private CheckBox _pickZero;
        private ComboBox _period;
        private CheckBox _twoSided;
        private CheckBox _rotateLabel;
        private TextBox _meaning;
        private BlockPreview _symbolPreview;
        private TextBox _testCode;
        private Label _testResult;
        private NumericUpDown _confidence;
        private ComboBox _tier;

        // ---- inserted blocks
        private ListView _blockGrid;
        private Label _blockIdentity;
        private CheckBox _visible;
        private BlockChooser _insertBlock;
        private Button _bringInsert;
        private ComboBox _blockLayer;
        private ComboBox _scaleMode;
        private TextBox _scale;
        private ComboBox _scaleField;
        private Label _divideBy;
        private TextBox _divisor;
        private ComboBox _placement;
        private Label _offsetCaption;
        private Label _offsetYCaption;
        private TextBox _offsetX;
        private TextBox _offsetY;
        private ComboBox _rotation;
        private Label _rotationNote;
        private DataGridView _attributes;
        private TextBox _sample;
        private Label _blockResult;
        private BlockPreview _blockPreview;

        public override string Title { get { return "Symbols and Rotations"; } }
        public override string AffectedCommands { get { return "FTFORIENT, FTFORIENTRESET, FTFPOINTS (field azimuths; blocks a rule opts in to)"; } }

        // The rules file has its own Save; the settings footer must not offer to restore this page.
        public override bool IsReadOnly { get { return true; } }

        protected override void BuildBody() { }

        protected override Control BuildFilled()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Size = new Size(1000, 680),
                // Edits here go to the rules file through Save Symbols, not to the settings.
                Tag = SettingsPanel.ViewOnly
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _source = new Label { AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 4) };
            root.Controls.Add(_source, 0, 0);
            root.Controls.Add(BuildLibraryRow(), 0, 1);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var symbolsTab = new TabPage("Symbol standard (Civil 3D markers)");
            symbolsTab.Controls.Add(BuildSymbolsTab());
            var blocksTab = new TabPage("Blocks FTF inserts (opt-in rules)");
            blocksTab.Controls.Add(BuildBlocksTab());
            tabs.TabPages.Add(symbolsTab);
            tabs.TabPages.Add(blocksTab);
            root.Controls.Add(tabs, 0, 2);

            _problems = new Label { AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = Bad, Margin = new Padding(0, 6, 0, 4) };
            root.Controls.Add(_problems, 0, 3);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            _save = new Button { Text = "Save Symbols", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
            var discard = new Button { Text = "Discard Edits", AutoSize = true };
            _unsaved = new Label { AutoSize = true, ForeColor = Color.FromArgb(160, 90, 0), Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(8, 8, 0, 0) };
            _save.Click += (s, e) => { FlushStage(); SaveClicked(); };
            discard.Click += (s, e) => { _stage.Stop(); _pending = null; Reload(true); };
            Tip(_save, "Writes the symbol standard and block settings to the active rules configuration (atomic; the previous version is kept as .bak).");
            Tip(discard, "Throws away what has not been saved and reads the rules file again.");
            buttons.Controls.Add(_save);
            buttons.Controls.Add(discard);
            buttons.Controls.Add(_unsaved);
            root.Controls.Add(buttons, 0, 4);

            _stage.Tick += (s, e) => FlushStage();
            Reload(false);
            return root;
        }

        public override void LoadFrom(FtfSettings s)
        {
            // With no library of its own, the standard offers the Exhibits block library.
            _fallbackLibrary = s != null && s.Exhibits != null ? (s.Exhibits.BlockLibrary ?? string.Empty).Trim() : string.Empty;
            if (ReadLibrary(false))
            {
                ShowSymbolPreview();
                LoadBlockEditor();
            }
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems) { }
        public override void RestoreDefaults(FtfSettings s) { }

        // ================================================================== layout

        private Control BuildLibraryRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
            row.Controls.Add(new Label { Text = "Approved symbol library (drawing)", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
            _library = new TextBox { Width = 380 };
            Tip(_library, "A drawing holding the office's approved symbol blocks. Its blocks are offered beside the drawing's own; one is " +
                          "copied into the drawing only when you press Bring into Drawing. Saved in the rules file (orientation > symbolLibrary).");
            var browse = new Button { Text = "Browse...", AutoSize = true };
            browse.Click += (s, e) =>
            {
                using (var dialog = new OpenFileDialog { Filter = "Drawings (*.dwg)|*.dwg", Title = "Approved symbol library" })
                {
                    if (dialog.ShowDialog() != DialogResult.OK) return;
                    _library.Text = dialog.FileName;
                    LibraryChanged();
                }
            };
            var read = new Button { Text = "Read Library", AutoSize = true };
            read.Click += (s, e) => LibraryChanged();
            _library.Leave += (s, e) => LibraryChanged();
            _libraryNote = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(900, 0), Margin = new Padding(0, 2, 0, 0) };
            row.Controls.Add(_library);
            row.Controls.Add(browse);
            row.Controls.Add(read);
            row.SetFlowBreak(read, true);
            row.Controls.Add(_libraryNote);
            return row;
        }

        private SplitContainer NewSplit()
        {
            // Sized before the splitter is set: WinForms refuses a splitter position outside the
            // control's current width. Dock fills it afterwards.
            var split = new SplitContainer { Orientation = Orientation.Vertical, Size = new Size(1000, 520), FixedPanel = FixedPanel.Panel1 };
            split.Panel1MinSize = 300;
            split.SplitterDistance = 470;
            split.Dock = DockStyle.Fill;
            return split;
        }

        private Control BuildSymbolsTab()
        {
            var split = NewSplit();

            // ---- left: the rules, in order
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _banner = new Label { AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(0, 2, 0, 4) };
            left.Controls.Add(_banner, 0, 0);

            _symbolGrid = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true, Dock = DockStyle.Fill };
            _symbolGrid.Columns.Add("#", 30);
            _symbolGrid.Columns.Add("Codes", 120);
            _symbolGrid.Columns.Add("Kind", 85);
            _symbolGrid.Columns.Add("Block", 90);
            _symbolGrid.Columns.Add("Zero", 42);
            _symbolGrid.Columns.Add("Turns", 70);
            _symbolGrid.SelectedIndexChanged += (s, e) => { if (!_loading) SymbolSelected(); };
            left.Controls.Add(_symbolGrid, 0, 1);

            var rowButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
            var add = SmallButton("Add", "A new rule below the selected one.", AddSymbol);
            var remove = SmallButton("Remove", "Removes the selected rule.", RemoveSymbol);
            var up = SmallButton("Move Up", "First match wins: a code's own rule goes above its family's.", () => MoveSymbol(-1));
            var down = SmallButton("Move Down", null, () => MoveSymbol(1));
            var overrideButton = SmallButton("Override for Code...", "Gives one code a rule of its own, above the rule that covers it now (starts as a copy of it).", OverrideForCode);
            var builtIn = SmallButton("Use Built-in PMX List", "Removes the rules file's own list so the built-in PMX standard applies again.", UseBuiltIn);
            foreach (var b in new[] { add, remove, up, down, overrideButton, builtIn }) rowButtons.Controls.Add(b);
            left.Controls.Add(rowButtons, 0, 2);

            var apply = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 8, 0, 0) };
            apply.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            apply.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _confidence = new NumericUpDown { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05m, Width = 64 };
            _tier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
            foreach (var t in new[] { OrientationTier.Measured, OrientationTier.Relationship, OrientationTier.Alignment, OrientationTier.Inferred })
                _tier.Items.Add(t.ToString());
            apply.Controls.Add(new Label { Text = "FTFORIENT turns a marker at confidence of at least", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 0);
            apply.Controls.Add(_confidence, 1, 0);
            apply.Controls.Add(new Label { Text = "...and evidence at least as strong as", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 1);
            apply.Controls.Add(_tier, 1, 1);
            Tip(_tier, "Measured (the crew's direction), Relationship (stop bar, approach, luminaire shot), Alignment (the road or main beside it), " +
                       "Inferred (nearest-road reasoning). Anything weaker is listed for review and left alone.");
            _confidence.ValueChanged += (s, e) => { if (!_loading) StageApply(); };
            _tier.SelectedIndexChanged += (s, e) => { if (!_loading) StageApply(); };
            left.Controls.Add(apply, 0, 3);
            split.Panel1.Controls.Add(left);

            // ---- right: the selected rule
            var rows = new EditorRows(Tip);
            _symbolIdentity = rows.Full(new Label { Text = "(select a symbol rule)", Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold), AutoSize = true });
            _codes = rows.Labelled("Codes", new TextBox { Width = 240 },
                "Point codes this rule covers, separated by spaces. * and ? are wildcards (SN* is every code starting SN); figure " +
                "digits are ignored (SN2 reads as SN). The first matching rule wins.");
            _kind = rows.Labelled("Kind", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 },
                "What FTFORIENT reasons about: a sign faces traffic, a hydrant's nozzle the street, a valve lies along its main...");
            foreach (var k in Enum.GetNames(typeof(OrientKind))) _kind.Items.Add(k);

            _symbolBlock = new BlockChooser { Width = 220 };
            _bringSymbol = new Button { Text = "Bring into Drawing", AutoSize = true, Enabled = false };
            _bringSymbol.Click += (s, e) => BringIn(_symbolBlock);
            rows.Labelled("Block", Pair(_symbolBlock, _bringSymbol),
                "The block the symbol is drawn with (for people and this preview; codes decide what the rule covers). " +
                "Type to search the drawing's and the library's blocks; a wildcard such as SSV-SN* is allowed.");

            _zero = new NumericUpDown { Minimum = 0, Maximum = 360, DecimalPlaces = 1, Increment = 15, Width = 70 };
            _pickZero = new CheckBox { Text = "Click the preview to set it", AutoSize = true, Margin = new Padding(8, 4, 0, 0) };
            rows.Labelled("Native zero (AutoCAD deg)", Pair(_zero, _pickZero),
                "The direction the block's meaningful direction points at rotation 0 -- a sign's face, a nozzle, an arm, a valve's pipe: " +
                "0 east, 90 north. A marker is turned by the facing direction minus this.");
            _period = rows.Labelled("Turns", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 }, null);
            _period.Items.Add(new Choice(360, "Has one direction (repeats every 360)"));
            _period.Items.Add(new Choice(180, "Is an axis: looks the same turned half way (repeats every 180)"));
            _period.Items.Add(new Choice(0, "Looks the same at every rotation: never turned (0)"));
            _twoSided = rows.Full(new CheckBox { Text = "Read from both sides (street name blades): only a measured direction turns it", AutoSize = true });
            _rotateLabel = rows.Full(new CheckBox { Text = "Turn the point label with the marker (painted words)", AutoSize = true });
            _meaning = rows.Labelled("What the zero means", new TextBox { Width = 260 }, "For people: \"sign face (panel north of the post)\".");

            rows.Heading("Preview");
            _symbolPreview = rows.Full(new BlockPreview { Size = new Size(360, 340) });
            rows.Note("Red: the insertion point. Blue: the native zero, turned with the symbol. Orange: the test facing. " +
                      "On a symbol with a direction, blue lands on orange: that is the rotation FTFPOINTS and FTFORIENT give.");
            _testCode = rows.Labelled("Which rule does a code get?", new TextBox { Width = 120, Tag = SettingsPanel.ViewOnly },
                "Type a point code: the rule it gets is shown (first match wins).");
            _testResult = rows.Note(string.Empty);

            _codes.TextChanged += (s, e) => Schedule(StageSymbol);
            _meaning.TextChanged += (s, e) => Schedule(StageSymbol);
            _symbolBlock.TextChanged += (s, e) => { if (!_loading) { Schedule(StageSymbol); UpdateBringButtons(); } };
            _symbolBlock.SelectedIndexChanged += (s, e) => { if (!_loading) { Schedule(StageSymbol); UpdateBringButtons(); } };
            foreach (var c in new[] { _kind, _period }) c.SelectedIndexChanged += (s, e) => { if (!_loading) StageSymbol(); };
            _zero.ValueChanged += (s, e) => { if (!_loading) StageSymbol(); };
            foreach (var c in new[] { _twoSided, _rotateLabel }) c.CheckedChanged += (s, e) => { if (!_loading) StageSymbol(); };
            _pickZero.CheckedChanged += (s, e) => _symbolPreview.PickReferenceOnClick = _pickZero.Checked;
            _symbolPreview.ReferencePicked += (s, e) =>
            {
                _zero.Value = Math.Min(_zero.Maximum, (decimal)Math.Round(_symbolPreview.ReferenceDegrees, 1));
            };
            _testCode.TextChanged += (s, e) => RunTestCode();

            split.Panel2.Controls.Add(rows.Scroller());
            return split;
        }

        private Control BuildBlocksTab()
        {
            var split = NewSplit();

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 4),
                Text = "Civil 3D's point styles draw the survey symbols; FTF inserts a block only for a rule that opts in, where the point " +
                       "style cannot. These are the rules that name a block. Turning insertion on asks first."
            }, 0, 0);

            _blockGrid = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true, Dock = DockStyle.Fill };
            _blockGrid.Columns.Add("Feature", 110);
            _blockGrid.Columns.Add("Codes", 80);
            _blockGrid.Columns.Add("Block", 100);
            _blockGrid.Columns.Add("Inserted", 58);
            _blockGrid.Columns.Add("Layer", 100);
            _blockGrid.SelectedIndexChanged += (s, e) => { if (!_loading) BlockSelected(); };
            left.Controls.Add(_blockGrid, 0, 1);

            var rowButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
            rowButtons.Controls.Add(SmallButton("Add Block to Rule...", "Gives a code rule a block (it is not inserted until you turn insertion on).", AddBlockToRule));
            rowButtons.Controls.Add(SmallButton("Take Block off Rule", "Removes the block and every block setting from the selected rule.", TakeBlockOff));
            left.Controls.Add(rowButtons, 0, 2);
            split.Panel1.Controls.Add(left);

            var rows = new EditorRows(Tip);
            _blockIdentity = rows.Full(new Label { Text = "(select a rule)", Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold), AutoSize = true });
            _visible = rows.Full(new CheckBox { Text = "FTF inserts this block (on: visible in the drawing after FTFPOINTS)", AutoSize = true });
            Tip(_visible, "Off: the rule names a block but FTF inserts nothing (Civil 3D's point style owns the symbol). " +
                          "On puts FTF's block on every point the rule matches, on top of whatever the point style draws.");

            _insertBlock = new BlockChooser { Width = 220 };
            _bringInsert = new Button { Text = "Bring into Drawing", AutoSize = true, Enabled = false };
            _bringInsert.Click += (s, e) => BringIn(_insertBlock);
            rows.Labelled("Block", Pair(_insertBlock, _bringInsert),
                "Type to search. {placeholders} are allowed (TREE-{species}). FTFPOINTS needs the block in the drawing: a library block " +
                "is brought in only with Bring into Drawing.");
            _blockLayer = rows.Labelled("Symbol layer", new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 240, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems },
                "Blank: the current layer. A layer the drawing lacks is created on first use.");
            if (Context.Drawing != null) foreach (var l in Context.Drawing.Layers) _blockLayer.Items.Add(l);

            _scaleMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            _scaleMode.Items.AddRange(new object[] { "Fixed", "From a field" });
            _scale = new TextBox { Width = 60 };
            _scaleField = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 110 };
            _divideBy = new Label { Text = "divided by", AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
            _divisor = new TextBox { Width = 50 };
            rows.Labelled("Symbol scale", Flow(_scaleMode, _scale, _scaleField, _divideBy, _divisor),
                "Fixed: the same scale on every point. From a field: a number the crew shot (a trunk diameter), divided by the divisor.");

            _placement = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
            _placement.Items.AddRange(new object[] { "On the point", "Offset east / north", "Offset turning with the block" });
            _offsetCaption = new Label { Text = "x", AutoSize = true, Margin = new Padding(6, 6, 2, 0) };
            _offsetX = new TextBox { Width = 50 };
            _offsetY = new TextBox { Width = 50 };
            _offsetYCaption = new Label { Text = "y", AutoSize = true, Margin = new Padding(4, 6, 2, 0) };
            rows.Labelled("Insertion point", Flow(_placement, _offsetCaption, _offsetX, _offsetYCaption, _offsetY),
                "Where the block's base point goes. Offsets are drawing units and are not scaled with the block.");

            _rotation = rows.Labelled("Rotation method", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 }, null);
            _rotation.Items.AddRange(new object[]
            {
                "Field azimuth (the rule's rotation field)", "None: never turned", "The point's marker (as FTFPOINTS / FTFORIENT set it)"
            });
            _rotationNote = rows.Note(string.Empty);

            rows.Heading("Attribute text");
            _attributes = rows.Full(new DataGridView
            {
                Width = 440, Height = 130, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect,
                BackgroundColor = SystemColors.Window
            });
            _attributes.Columns.Add("tag", "Attribute tag");
            _attributes.Columns.Add("template", "Text (template)");
            _attributes.Columns[0].FillWeight = 35;
            _attributes.Columns[1].FillWeight = 65;
            rows.Note("{point} point number, {number} or any group of the rule's pattern, {elevation:0.00}, {northing}, {easting}, {raw}, " +
                      "{note}, {label}, {azimuth}; [text {x}] appears only when {x} has a value. A tag left blank keeps the block's own " +
                      "default. Attributes are created only when at least one has text here.");

            rows.Heading("Preview");
            _sample = rows.Labelled("Description to try", new TextBox { Width = 220, Tag = SettingsPanel.ViewOnly },
                "Parsed with your unsaved edits; nothing in the drawing changes.");
            _blockResult = rows.Full(new Label { AutoSize = true, MaximumSize = new Size(460, 0), Font = new Font("Consolas", 8.5f), Margin = new Padding(0, 4, 0, 4) });
            _blockPreview = rows.Full(new BlockPreview { Size = new Size(360, 320), RotationFollowsAzimuth = false });

            _visible.CheckedChanged += (s, e) => { if (!_loading) VisibilityToggled(); };
            foreach (var c in new Control[] { _scale, _divisor, _offsetX, _offsetY }) c.TextChanged += (s, e) => Schedule(StageBlock);
            foreach (var c in new ComboBox[] { _insertBlock, _blockLayer, _scaleField })
            {
                c.TextChanged += (s, e) => { if (!_loading) { Schedule(StageBlock); UpdateBringButtons(); } };
                c.SelectedIndexChanged += (s, e) => { if (!_loading) { Schedule(StageBlock); UpdateBringButtons(); } };
            }
            foreach (var c in new[] { _scaleMode, _placement, _rotation })
                c.SelectedIndexChanged += (s, e) => { if (!_loading) { ShowBlockParts(); StageBlock(); } };
            _attributes.CellValueChanged += (s, e) => { if (!_loading) Schedule(StageBlock); };
            _attributes.UserDeletedRow += (s, e) => { if (!_loading) Schedule(StageBlock); };
            _sample.TextChanged += (s, e) =>
            {
                if (_loading) return;
                RunBlockSample();
                if (_insertBlock.BlockName.IndexOf('{') >= 0) ShowInsertPreview();
            };
            _blockPreview.TestAzimuthChanged += (s, e) => ShowInsertRotation();

            split.Panel2.Controls.Add(rows.Scroller());
            return split;
        }

        // ================================================================== loading

        /// <summary>Discards staged edits and reads the ACTIVE rules file again.</summary>
        private void Reload(bool announce)
        {
            _resolution = Context.RulesResolution == null
                ? null
                : RulesStore.Resolve(Context.RulesResolution.DrawingDirectory, Context.RulesResolution.PluginDirectory);
            _activePath = _resolution != null ? _resolution.ActivePath : Context.RulesPath;
            _document = null;
            string loadProblem = null;
            try
            {
                if (!string.IsNullOrEmpty(_activePath)) _document = RuleDocument.Load(_activePath);
            }
            catch (ConfigException ex) { loadProblem = ex.Message; }
            catch (IOException ex) { loadProblem = ex.Message; }

            _symbolsTouched = false;
            _applyTouched = false;
            _blockTouched.Clear();
            _extraBlockRules.Clear();
            _builtIn = _document == null || _document.SymbolsAreBuiltIn();
            _symbols = _document != null
                ? _document.GetSymbols().ToList()
                : OrientationStandard.Pmx().Symbols.Select(r => EditableSymbol.From(r, -1)).ToList();

            _loading = true;
            try
            {
                var apply = _document != null ? _document.GetOrientationApply() : new EditableOrientationApply { MinimumConfidence = 0.6, WeakestTier = "Inferred", SymbolLibrary = string.Empty };
                _confidence.Value = Math.Max(_confidence.Minimum, Math.Min(_confidence.Maximum, (decimal)apply.MinimumConfidence));
                var tier = _tier.Items.Cast<object>().FirstOrDefault(o => string.Equals(o.ToString(), apply.WeakestTier, StringComparison.OrdinalIgnoreCase));
                _tier.SelectedItem = tier ?? "Inferred";
                _library.Text = apply.SymbolLibrary ?? string.Empty;
            }
            finally { _loading = false; }

            RefreshCandidate();
            ShowSource();
            ShowBanner();
            ReadLibrary(true);                // before the editors: they look blocks up in it
            _symbolIndex = Math.Min(Math.Max(_symbolIndex, 0), _symbols.Count - 1);
            RefreshSymbolGrid();
            PopulateBlockGrid(_blockId);
            SetDirty(false);
            _save.Enabled = _document != null;
            if (loadProblem != null) Problem("The rules file could not be read, so the built-in standard is shown and nothing can be saved: " + loadProblem);
            else if (announce) Info("Reloaded from " + _activePath + " (unsaved edits discarded).");
            else Info(string.Empty);
        }

        private void ShowSource()
        {
            if (_resolution == null) { _source.Text = string.Empty; return; }
            switch (_resolution.Source)
            {
                case RulesSource.Drawing:
                    _source.Text = "Editing the DRAWING OVERRIDE (this job only): " + _resolution.ActivePath;
                    break;
                case RulesSource.Office:
                    _source.Text = "Editing the OFFICE STANDARD: " + _resolution.ActivePath;
                    break;
                default:
                    _source.Text = "Showing the FACTORY rules. Your first save creates the office standard at " +
                                   _resolution.OfficePath + "; the shipped file is never edited.";
                    break;
            }
        }

        private void ShowBanner()
        {
            if (_builtIn)
            {
                _banner.ForeColor = Color.FromArgb(160, 90, 0);
                _banner.Text = "The built-in PMX symbol list is in force: the rules file has no list of its own. Your first edit " +
                               "copies these " + _symbols.Count + " rules into the rules file, where they become the office's to change.";
            }
            else
            {
                _banner.ForeColor = SystemColors.GrayText;
                _banner.Text = "The rules file's own symbol list (" + _symbols.Count + " rules). First match wins, top to bottom: " +
                               "a code's own rule goes above its family's.";
            }
        }

        private void RefreshCandidate()
        {
            _candidate = null;
            if (_document == null) { _candidate = Context.Rules; return; }
            try { _candidate = _document.CompileCandidate(); }
            catch (ConfigException) { _candidate = null; }
            catch (Newtonsoft.Json.JsonException) { _candidate = null; }
        }

        // ================================================================== library

        private string EffectiveLibrary
        {
            get { return !string.IsNullOrWhiteSpace(_library.Text) ? _library.Text.Trim() : _fallbackLibrary; }
        }

        private void LibraryChanged()
        {
            if (_loading) return;
            if (_document != null)
            {
                var apply = _document.GetOrientationApply();
                var typed = (_library.Text ?? string.Empty).Trim();
                if (!string.Equals(apply.SymbolLibrary ?? string.Empty, typed, StringComparison.Ordinal))
                {
                    apply.SymbolLibrary = typed;
                    _document.ApplyOrientationApply(apply);
                    _applyTouched = true;
                    SetDirty(true);
                }
            }
            if (ReadLibrary(true))
            {
                ShowSymbolPreview();
                LoadBlockEditor();
            }
        }

        /// <summary>Lists the library's blocks (once per file; the reader caches) and offers them.
        /// True when the library was (re)read.</summary>
        private bool ReadLibrary(bool force)
        {
            var path = EffectiveLibrary;
            var read = force || !string.Equals(path, _libraryRead, StringComparison.OrdinalIgnoreCase);
            if (read)
            {
                _libraryProblem = null;
                _libraryBlocks = string.IsNullOrWhiteSpace(path) ? new List<string>() : SymbolLibraryReader.BlockNames(path, out _libraryProblem);
                _libraryRead = path;
            }

            var own = !string.IsNullOrWhiteSpace(_library.Text);
            if (string.IsNullOrWhiteSpace(path))
                _libraryNote.Text = "No library: only this drawing's blocks are offered. Name the office's symbol drawing to offer its blocks too.";
            else if (_libraryProblem != null)
                _libraryNote.Text = _libraryProblem;
            else
                _libraryNote.Text = (own ? string.Empty : "Using the Easement Exhibits block library (" + path + "). ") +
                                    _libraryBlocks.Count + " block(s) in the library. A library block is copied into this drawing only when you press Bring into Drawing.";

            var drawing = Context.Drawing != null ? Context.Drawing.Blocks : (IList<string>)new List<string>();
            _symbolBlock.SetBlocks(drawing, _libraryBlocks);
            _insertBlock.SetBlocks(drawing, _libraryBlocks);
            UpdateBringButtons();
            return read;
        }

        private void UpdateBringButtons()
        {
            _bringSymbol.Enabled = _symbolBlock.InLibrary(_symbolBlock.BlockName) && !_symbolBlock.InDrawing(_symbolBlock.BlockName);
            _bringInsert.Enabled = _insertBlock.InLibrary(_insertBlock.BlockName) && !_insertBlock.InDrawing(_insertBlock.BlockName);
        }

        /// <summary>Copies one library block into the drawing -- only ever from this button.</summary>
        private void BringIn(BlockChooser chooser)
        {
            var name = chooser.BlockName;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || string.IsNullOrWhiteSpace(name)) return;
            string message;
            bool ok;
            using (doc.LockDocument())
                ok = SymbolLibraryReader.BringIn(doc.Database, EffectiveLibrary, name, out message);
            if (ok)
            {
                if (Context.Drawing != null && !Context.Drawing.Blocks.Contains(name, StringComparer.OrdinalIgnoreCase))
                    Context.Drawing.Blocks.Add(name);
                _symbolBlock.AddDrawingBlock(name);
                _insertBlock.AddDrawingBlock(name);
                Info(message);
            }
            else Problem(message);
            UpdateBringButtons();
            ShowSymbolPreview();
            ShowInsertPreview();
        }

        /// <summary>Shows a block by name: the drawing's (a wildcard shows the first match), else
        /// the library's without bringing it in, else a note.</summary>
        private void ShowNamedBlock(BlockPreview preview, string blockName)
        {
            var name = (blockName ?? string.Empty).Trim();
            preview.Caption = null;
            if (name.Length == 0) { preview.ShowMessage("No block named: choose the block this symbol is drawn with to see its native zero."); return; }
            if (string.Equals(name, LabelBlock, StringComparison.OrdinalIgnoreCase))
            {
                preview.ShowMessage("Drawn by the point label, not a block: the label turns with the marker.");
                return;
            }
            if (name.IndexOf('{') >= 0) { preview.ShowMessage("\"" + name + "\" is worked out per point; try a description below to see which block."); return; }

            var wildcard = name.IndexOfAny(new[] { '*', '?' }) >= 0;
            var drawing = Context.Drawing != null ? Context.Drawing.Blocks : (IList<string>)new List<string>();
            var inDrawing = wildcard
                ? drawing.FirstOrDefault(b => OrientationStandard.CodeMatches(name, b))
                : drawing.FirstOrDefault(b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase));
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (inDrawing != null && doc != null)
            {
                preview.ShowBlock(doc.Database, inDrawing);
                if (wildcard) preview.Caption = name + ": showing " + inDrawing;
                return;
            }

            var inLibrary = wildcard
                ? _libraryBlocks.FirstOrDefault(b => OrientationStandard.CodeMatches(name, b))
                : _libraryBlocks.FirstOrDefault(b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase));
            var sketch = inLibrary != null ? SymbolLibraryReader.Sketch(EffectiveLibrary, inLibrary) : null;
            if (sketch != null)
            {
                preview.ShowSketch(sketch);
                preview.Caption = "From the symbol library: not in this drawing yet.";
                return;
            }
            preview.ShowMessage("\"" + name + "\" is not in this drawing" + (_libraryBlocks.Count > 0 ? " or the symbol library." : "."));
        }

        // ================================================================== symbol standard

        private int SelectedSymbolIndex()
        {
            return _symbolGrid.SelectedItems.Count == 0 ? -1 : (int)_symbolGrid.SelectedItems[0].Tag;
        }

        private void RefreshSymbolGrid()
        {
            _loading = true;
            try
            {
                _symbolGrid.BeginUpdate();
                _symbolGrid.Items.Clear();
                for (var i = 0; i < _symbols.Count; i++)
                {
                    var item = new ListViewItem(SymbolCells(i)) { Tag = i };
                    if (_builtIn) item.ForeColor = SystemColors.GrayText;
                    _symbolGrid.Items.Add(item);
                }
                _symbolGrid.EndUpdate();
                if (_symbolIndex >= _symbols.Count) _symbolIndex = _symbols.Count - 1;
                if (_symbolIndex >= 0)
                {
                    _symbolGrid.Items[_symbolIndex].Selected = true;
                    _symbolGrid.Items[_symbolIndex].EnsureVisible();
                }
            }
            finally { _loading = false; }
            LoadSymbolEditor();
            RunTestCode();
        }

        private string[] SymbolCells(int i)
        {
            var s = _symbols[i];
            var turns = s.Period <= 0 ? "never" : s.Period < 360 - 1e-9 ? "axis (180)" : "360";
            if (s.TwoSided) turns += ", 2-sided";
            if (s.RotateLabel) turns += ", label";
            return new[]
            {
                (i + 1).ToString(CultureInfo.InvariantCulture), s.CodesText, s.Kind ?? string.Empty, s.Block ?? string.Empty,
                s.ReferenceCadDegrees.ToString("0.#", CultureInfo.InvariantCulture), turns
            };
        }

        private void UpdateSymbolRow(int i)
        {
            if (i < 0 || i >= _symbolGrid.Items.Count || i >= _symbols.Count) return;
            var cells = SymbolCells(i);
            var item = _symbolGrid.Items[i];
            for (var c = 0; c < cells.Length && c < item.SubItems.Count; c++) item.SubItems[c].Text = cells[c];
            item.ForeColor = _builtIn ? SystemColors.GrayText : SystemColors.WindowText;
        }

        private void SymbolSelected()
        {
            var index = SelectedSymbolIndex();
            if (index < 0 || index == _symbolIndex) return;
            FlushStage();                     // what was typed belongs to the row it was typed on
            _symbolIndex = index;
            LoadSymbolEditor();
        }

        private void LoadSymbolEditor()
        {
            var have = _symbolIndex >= 0 && _symbolIndex < _symbols.Count;
            var editable = have && _document != null;
            _loading = true;
            try
            {
                foreach (var c in new Control[] { _codes, _kind, _symbolBlock, _zero, _period, _twoSided, _rotateLabel, _meaning, _pickZero })
                    c.Enabled = editable;
                if (!have)
                {
                    _symbolIdentity.Text = "(select a symbol rule)";
                    _symbolPreview.ShowMessage("(select a symbol rule)");
                    return;
                }
                var s = _symbols[_symbolIndex];
                _symbolIdentity.Text = "Rule " + (_symbolIndex + 1) + " of " + _symbols.Count + (_builtIn ? "  (built-in PMX list)" : string.Empty);
                _codes.Text = s.CodesText;
                var kind = _kind.Items.Cast<object>().FirstOrDefault(o => string.Equals(o.ToString(), s.Kind, StringComparison.OrdinalIgnoreCase));
                if (kind == null && !string.IsNullOrWhiteSpace(s.Kind)) { _kind.Items.Add(s.Kind); kind = s.Kind; }
                _kind.SelectedItem = kind;
                _symbolBlock.BlockName = s.Block ?? string.Empty;
                _zero.Value = Math.Max(_zero.Minimum, Math.Min(_zero.Maximum, (decimal)Math.Round(s.ReferenceCadDegrees, 1)));
                var period = _period.Items.Cast<Choice>().FirstOrDefault(p => Math.Abs(p.Value - s.Period) < 1e-9);
                if (period == null)
                {
                    period = new Choice(s.Period, s.Period.ToString("0.#", CultureInfo.InvariantCulture) + " (not allowed: choose 360, 180 or 0)");
                    _period.Items.Add(period);
                }
                _period.SelectedItem = period;
                _twoSided.Checked = s.TwoSided;
                _rotateLabel.Checked = s.RotateLabel;
                _meaning.Text = s.Meaning ?? string.Empty;
            }
            finally { _loading = false; }
            UpdateBringButtons();
            ShowSymbolPreview();
        }

        private EditableSymbol ReadSymbolEditor(EditableSymbol into)
        {
            into.Codes = EditableSymbol.ParseCodes(_codes.Text);
            into.Kind = Convert.ToString(_kind.SelectedItem);
            var block = _symbolBlock.BlockName;
            into.Block = string.IsNullOrWhiteSpace(block) ? null : block;
            into.ReferenceCadDegrees = (double)_zero.Value;
            var period = _period.SelectedItem as Choice;
            if (period != null) into.Period = period.Value;
            into.TwoSided = _twoSided.Checked;
            into.RotateLabel = _rotateLabel.Checked;
            into.Meaning = string.IsNullOrWhiteSpace(_meaning.Text) ? null : _meaning.Text.Trim();
            return into;
        }

        private static string Snapshot(EditableSymbol s)
        {
            return string.Join("|", new[]
            {
                s.CodesText, s.Kind ?? string.Empty, s.Block ?? string.Empty, s.ReferenceCadDegrees.ToString("R", CultureInfo.InvariantCulture),
                s.Period.ToString("R", CultureInfo.InvariantCulture), s.TwoSided.ToString(), s.RotateLabel.ToString(), s.Meaning ?? string.Empty
            });
        }

        /// <summary>Puts the editor's values on the selected row and the staged document.</summary>
        private void StageSymbol()
        {
            if (_loading || _document == null || _symbolIndex < 0 || _symbolIndex >= _symbols.Count) return;
            var edited = ReadSymbolEditor(_symbols[_symbolIndex].Clone());
            if (Snapshot(edited) == Snapshot(_symbols[_symbolIndex])) { ShowSymbolPreview(); return; }
            EnsureOwnList();
            edited.SourceIndex = _symbols[_symbolIndex].SourceIndex;
            _symbols[_symbolIndex] = edited;
            CommitSymbols(false);
        }

        /// <summary>The first edit of the built-in list copies it into the document, and says so.</summary>
        private void EnsureOwnList()
        {
            if (!_builtIn || _document == null) return;
            _document.MaterializeSymbols();
            _symbols = _document.GetSymbols().ToList();
            _builtIn = false;
            _symbolsTouched = true;
            foreach (ListViewItem item in _symbolGrid.Items) item.ForeColor = SystemColors.WindowText;
            ShowBanner();
            Info("The built-in PMX symbol list (" + _symbols.Count + " rules) was copied into the rules file so it can be edited. " +
                 "Nothing changes for FTFORIENT until you Save Symbols.");
        }

        /// <summary>Writes the working list onto the staged document and refreshes what shows it.</summary>
        private void CommitSymbols(bool rebuildGrid)
        {
            if (_document == null) return;
            try { _document.SetSymbols(_symbols); }
            catch (ConfigException ex) { Problem(ex.Message); return; }
            _symbolsTouched = true;
            SetDirty(true);
            ShowValidation();
            if (rebuildGrid) RefreshSymbolGrid();
            else
            {
                UpdateSymbolRow(_symbolIndex);
                ShowSymbolPreview();
                RunTestCode();
            }
            ShowBanner();
        }

        private void AddSymbol()
        {
            if (_document == null) return;
            FlushStage();
            EnsureOwnList();
            var at = _symbolIndex >= 0 ? _symbolIndex + 1 : _symbols.Count;
            _symbols.Insert(at, new EditableSymbol { Kind = OrientKind.Other.ToString(), Period = 360 });
            _symbolIndex = at;
            CommitSymbols(true);
            _codes.Focus();
            Problem("New rule " + (at + 1) + ": type its codes. A rule with no codes cannot be saved.");
        }

        private void RemoveSymbol()
        {
            if (_document == null || _symbolIndex < 0) return;
            if (_symbols.Count <= 1)
            {
                Problem("The list needs at least one rule. To go back to the built-in PMX list, use Use Built-in PMX List.");
                return;
            }
            FlushStage();
            EnsureOwnList();
            _symbols.RemoveAt(_symbolIndex);
            _symbolIndex = Math.Min(_symbolIndex, _symbols.Count - 1);
            CommitSymbols(true);
        }

        private void MoveSymbol(int step)
        {
            if (_document == null || _symbolIndex < 0) return;
            var to = _symbolIndex + step;
            if (to < 0 || to >= _symbols.Count) return;
            FlushStage();
            EnsureOwnList();
            var row = _symbols[_symbolIndex];
            _symbols[_symbolIndex] = _symbols[to];
            _symbols[to] = row;
            _symbolIndex = to;
            CommitSymbols(true);
        }

        /// <summary>A rule of its own for one code, above the family rule that covers it now.</summary>
        private void OverrideForCode()
        {
            if (_document == null) return;
            FlushStage();
            var suggestion = (_testCode.Text ?? string.Empty).Trim();
            if (suggestion.Length == 0 && _symbolIndex >= 0)
                suggestion = _symbols[_symbolIndex].Codes.FirstOrDefault(c => c.IndexOfAny(new[] { '*', '?' }) < 0) ?? string.Empty;
            var code = Ask("Override for Code", "Point code to give a symbol rule of its own (it goes directly above the rule that covers it now, starting as a copy):", suggestion);
            if (string.IsNullOrWhiteSpace(code)) return;

            EnsureOwnList();
            var working = _symbols.Select(s => s.Clone()).ToList();
            int index;
            try { index = RuleDocument.OverrideSymbolFor(working, code); }
            catch (ConfigException ex) { Problem(ex.Message); return; }
            _symbols = working;
            _symbolIndex = index;
            CommitSymbols(true);
            Info("Rule " + (index + 1) + " is now " + OrientationStandard.BaseCode(code) + "'s own, above the rule it came from (now rule " +
                 (index + 2) + "). Change its zero or block, then Save Symbols.");
        }

        private void UseBuiltIn()
        {
            if (_document == null || _builtIn) return;
            var answer = MessageBox.Show(_symbolGrid,
                "Remove the rules file's own symbol list so the built-in PMX standard applies again?\n\nThe file's list (and any change made to it) is gone once you Save Symbols.",
                "Use Built-in PMX List", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;
            _stage.Stop();
            _pending = null;
            _document.UseBuiltInSymbols();
            _symbols = _document.GetSymbols().ToList();
            _builtIn = true;
            _symbolsTouched = true;
            SetDirty(true);
            ShowBanner();
            ShowValidation();
            RefreshSymbolGrid();
        }

        private void StageApply()
        {
            if (_document == null) return;
            var apply = _document.GetOrientationApply();
            apply.MinimumConfidence = (double)_confidence.Value;
            apply.WeakestTier = Convert.ToString(_tier.SelectedItem);
            apply.SymbolLibrary = (_library.Text ?? string.Empty).Trim();
            _document.ApplyOrientationApply(apply);
            _applyTouched = true;
            SetDirty(true);
            ShowValidation();
        }

        private void ShowSymbolPreview()
        {
            if (_symbolIndex < 0 || _symbolIndex >= _symbols.Count) { _symbolPreview.ShowMessage("(select a symbol rule)"); return; }
            var current = _loading || _document == null ? _symbols[_symbolIndex] : ReadSymbolEditor(_symbols[_symbolIndex].Clone());
            _symbolPreview.ApplySymbolRule(current.ToRule());
            ShowNamedBlock(_symbolPreview, current.Block);
        }

        /// <summary>Which rule a typed code gets, first match wins.</summary>
        private void RunTestCode()
        {
            if (_testResult == null) return;
            var code = (_testCode.Text ?? string.Empty).Trim();
            if (code.Length == 0)
            {
                _testResult.Text = "Type a code to see which rule it gets.";
                return;
            }
            var standard = new OrientationStandard { Symbols = _symbols.Select(s => s.ToRule()).ToList() };
            var index = standard.SymbolIndexFor(code);
            if (index < 0)
            {
                _testResult.Text = OrientationStandard.BaseCode(code) + ": no symbol rule covers it, so FTFORIENT leaves its marker alone " +
                                   "and a field azimuth turns the marker by the azimuth alone.";
                return;
            }
            var rule = _symbols[index];
            _testResult.Text = OrientationStandard.BaseCode(code) + " gets rule " + (index + 1) + " (" + rule.CodesText + "): " + rule.Kind +
                               ", zero " + rule.ReferenceCadDegrees.ToString("0.#", CultureInfo.InvariantCulture) +
                               (rule.Period <= 0 ? ", never turned." : ".") +
                               " Override for Code gives it a rule of its own.";
        }

        // ================================================================== inserted blocks

        private string SelectedBlockId()
        {
            return _blockGrid.SelectedItems.Count == 0 ? null : (string)_blockGrid.SelectedItems[0].Tag;
        }

        private void PopulateBlockGrid(string selectId)
        {
            _loading = true;
            try
            {
                _blockGrid.BeginUpdate();
                _blockGrid.Items.Clear();
                if (_document != null)
                {
                    var ids = _document.BlockRuleIds().ToList();
                    foreach (var extra in _extraBlockRules) if (!ids.Contains(extra, StringComparer.OrdinalIgnoreCase)) ids.Add(extra);
                    foreach (var id in ids)
                    {
                        var item = new ListViewItem(BlockCells(id)) { Tag = id };
                        _blockGrid.Items.Add(item);
                        if (selectId != null && string.Equals(id, selectId, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
                    }
                }
                _blockGrid.EndUpdate();
                if (_blockGrid.SelectedItems.Count == 0 && _blockGrid.Items.Count > 0) _blockGrid.Items[0].Selected = true;
            }
            finally { _loading = false; }
            _blockId = SelectedBlockId();
            LoadBlockEditor();
        }

        private string[] BlockCells(string id)
        {
            var e = _document.GetBlockInsert(id);
            if (e == null) return new[] { id, string.Empty, string.Empty, string.Empty, string.Empty };
            return new[]
            {
                string.IsNullOrWhiteSpace(e.Description) ? id : e.Description, CodesOf(id),
                string.IsNullOrWhiteSpace(e.Block) ? "(none yet)" : e.Block, e.InsertBlock ? "Yes" : "no", e.BlockLayer ?? string.Empty
            };
        }

        private void UpdateBlockRow(string id)
        {
            foreach (ListViewItem item in _blockGrid.Items)
            {
                if (!string.Equals((string)item.Tag, id, StringComparison.OrdinalIgnoreCase)) continue;
                var cells = BlockCells(id);
                for (var c = 0; c < cells.Length && c < item.SubItems.Count; c++) item.SubItems[c].Text = cells[c];
            }
        }

        private CodeRule CompiledRule(string id)
        {
            return _candidate == null || id == null ? null
                : _candidate.Codes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private string CodesOf(string id)
        {
            var rule = CompiledRule(id);
            if (rule == null) return id;
            if (rule.Species != null && rule.Species.Count > 0)
                return string.Join(", ", rule.Species.Keys.Take(4).ToArray()) + (rule.Species.Count > 4 ? ", ..." : string.Empty);
            var codes = RuleDocument.LiteralCodes(rule);
            return codes.Count > 0 ? string.Join(", ", codes.ToArray()) : id;
        }

        private void BlockSelected()
        {
            var id = SelectedBlockId();
            if (id == null || string.Equals(id, _blockId, StringComparison.OrdinalIgnoreCase)) return;
            FlushStage();
            _blockId = id;
            LoadBlockEditor();
        }

        private void LoadBlockEditor()
        {
            var e = _document != null && _blockId != null ? _document.GetBlockInsert(_blockId) : null;
            _loading = true;
            try
            {
                foreach (var c in new Control[] { _visible, _insertBlock, _blockLayer, _scaleMode, _scale, _scaleField, _divisor, _placement, _offsetX, _offsetY, _rotation, _attributes, _sample })
                    c.Enabled = e != null;
                if (e == null)
                {
                    _blockIdentity.Text = "(no rule names a block: Add Block to Rule)";
                    _blockPreview.ShowMessage("(select a rule)");
                    _blockResult.Text = string.Empty;
                    _rotationNote.Text = string.Empty;
                    _attributes.Rows.Clear();
                    return;
                }
                _blockIdentity.Text = (string.IsNullOrWhiteSpace(e.Description) ? e.Id : e.Description) + "  --  code " + CodesOf(e.Id) + "  (rule '" + e.Id + "')";
                _visible.Checked = e.InsertBlock;
                _insertBlock.BlockName = e.Block;
                _blockLayer.Text = e.BlockLayer;
                _scaleMode.SelectedIndex = string.IsNullOrWhiteSpace(e.BlockScaleFrom) ? 0 : 1;
                _scale.Text = Fmt(e.BlockScale);
                _scaleField.Items.Clear();
                foreach (var f in FieldsOf(_blockId)) _scaleField.Items.Add(f);
                _scaleField.Text = e.BlockScaleFrom;
                _divisor.Text = Fmt(e.BlockScaleDivisor);
                _placement.SelectedIndex = (int)e.Placement;
                _offsetX.Text = Fmt(e.OffsetX);
                _offsetY.Text = Fmt(e.OffsetY);
                _rotation.SelectedIndex = (int)e.Rotation;
                _rotationNote.Text = string.IsNullOrWhiteSpace(e.FieldRotation)
                    ? "This rule reads no rotation field, so \"Field azimuth\" leaves the block at 0."
                    : "The rule reads its rotation from field '" + e.FieldRotation + "'. \"The point's marker\" follows FTFORIENT when FTFPOINTS runs after it.";
                FillAttributeGrid(e);
                _sample.Text = SampleFor(_blockId);
                ShowBlockParts();
            }
            finally { _loading = false; }
            UpdateBringButtons();
            ShowInsertPreview();
            RunBlockSample();
        }

        /// <summary>The block's attribute tags (drawing or library) with their templates, then
        /// any template whose tag the block does not have.</summary>
        private void FillAttributeGrid(EditableBlockInsert e)
        {
            _attributes.Rows.Clear();
            var templates = e.Attributes ?? new List<KeyValuePair<string, string>>();
            var tags = BlockTags(e.Block);
            foreach (var tag in tags)
            {
                var t = templates.FirstOrDefault(kv => string.Equals(kv.Key, tag, StringComparison.OrdinalIgnoreCase));
                _attributes.Rows.Add(tag, t.Key != null ? t.Value : string.Empty);
            }
            foreach (var kv in templates.Where(kv => !tags.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)))
                _attributes.Rows.Add(kv.Key, kv.Value);
        }

        private IList<string> BlockTags(string blockName)
        {
            var name = (blockName ?? string.Empty).Trim();
            if (name.Length == 0 || name.IndexOfAny(new[] { '*', '?', '{' }) >= 0) return new List<string>();
            BlockSketch sketch = null;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            try
            {
                if (_insertBlock.InDrawing(name) && doc != null)
                    using (doc.LockDocument()) sketch = BlockSketchReader.Read(doc.Database, name);
                else if (_insertBlock.InLibrary(name))
                    sketch = SymbolLibraryReader.Sketch(EffectiveLibrary, name);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { sketch = null; }
            return sketch != null ? sketch.AttributeTags : new List<string>();
        }

        /// <summary>The fields a scale can be read from: the pattern's named groups and the derived trunk fields.</summary>
        private IList<string> FieldsOf(string id)
        {
            var rule = CompiledRule(id);
            var fields = new List<string>();
            if (rule == null || rule.Regex == null) return fields;
            int ignored;
            fields.AddRange(rule.Regex.GetGroupNames().Where(g => !int.TryParse(g, out ignored)));
            if (fields.Contains("trunk")) fields.AddRange(new[] { "trunk.exact", "trunk.count" });
            return fields;
        }

        private string SampleFor(string id)
        {
            var rule = CompiledRule(id);
            if (rule == null) return string.Empty;
            if (rule.Species != null && rule.Species.Count > 0) return rule.Species.Keys.First() + " 18 . 20";
            var code = RuleDocument.LiteralCodes(rule).FirstOrDefault();
            if (code == null) return string.Empty;
            return code + " 1234";
        }

        private void ShowBlockParts()
        {
            var fromField = _scaleMode.SelectedIndex == 1;
            _scale.Visible = !fromField;
            _scaleField.Visible = fromField;
            _divideBy.Visible = fromField;
            _divisor.Visible = fromField;
            var offset = _placement.SelectedIndex > 0;
            _offsetCaption.Visible = offset;
            _offsetX.Visible = offset;
            _offsetY.Visible = offset;
            _offsetYCaption.Visible = offset;
        }

        /// <summary>Puts the editor's values on the selected rule of the staged document.</summary>
        private void StageBlock()
        {
            if (_loading || _document == null || _blockId == null) return;
            var e = _document.GetBlockInsert(_blockId);
            if (e == null) return;
            var before = BlockSnapshot(e);
            var input = new List<string>();

            e.Block = _insertBlock.BlockName;
            e.BlockLayer = (_blockLayer.Text ?? string.Empty).Trim();
            if (_scaleMode.SelectedIndex == 1)
            {
                e.BlockScaleFrom = (_scaleField.Text ?? string.Empty).Trim();
                e.BlockScaleDivisor = Read(_divisor, "the scale divisor", e.BlockScaleDivisor, input);
                if (e.BlockScaleFrom.Length == 0) input.Add("Choose the field the scale is read from (or use a fixed scale).");
            }
            else
            {
                e.BlockScaleFrom = string.Empty;
                e.BlockScale = Read(_scale, "the scale", e.BlockScale, input);
            }
            e.Placement = (BlockPlacement)Math.Max(0, _placement.SelectedIndex);
            e.OffsetX = Read(_offsetX, "the x offset", e.OffsetX, input);
            e.OffsetY = Read(_offsetY, "the y offset", e.OffsetY, input);
            e.Rotation = (BlockRotationMethod)Math.Max(0, _rotation.SelectedIndex);
            e.Attributes = new List<KeyValuePair<string, string>>();
            foreach (DataGridViewRow row in _attributes.Rows)
            {
                if (row.IsNewRow) continue;
                var tag = Convert.ToString(row.Cells[0].Value ?? string.Empty).Trim();
                var template = Convert.ToString(row.Cells[1].Value ?? string.Empty);
                if (tag.Length > 0 && template.Trim().Length > 0) e.Attributes.Add(new KeyValuePair<string, string>(tag, template));
            }

            if (BlockSnapshot(e) != before)
            {
                _document.ApplyBlockInsert(e);
                _blockTouched.Add(_blockId);
                SetDirty(true);
                RefreshCandidate();
                UpdateBlockRow(_blockId);
            }
            ShowValidation(input);
            ShowInsertPreview();
            RunBlockSample();
        }

        private static string BlockSnapshot(EditableBlockInsert e)
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join("|", new[]
            {
                e.Block ?? string.Empty, e.BlockLayer ?? string.Empty, e.BlockScale.ToString("R", c), e.BlockScaleFrom ?? string.Empty,
                e.BlockScaleDivisor.ToString("R", c), e.Placement.ToString(), e.OffsetX.ToString("R", c), e.OffsetY.ToString("R", c),
                e.Rotation.ToString(), string.Join(";", (e.Attributes ?? new List<KeyValuePair<string, string>>()).Select(kv => kv.Key + "=" + kv.Value).ToArray())
            });
        }

        private static double Read(TextBox box, string what, double keep, List<string> problems)
        {
            double v;
            if (double.TryParse((box.Text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            if (box.Visible) problems.Add("'" + box.Text + "' is not a number (" + what + ").");
            return keep;
        }

        /// <summary>Insertion on asks first: FTF's block goes on top of whatever the point style draws.</summary>
        private void VisibilityToggled()
        {
            if (_document == null || _blockId == null) return;
            FlushStage();
            var on = _visible.Checked;
            if (on)
            {
                var name = _insertBlock.BlockName;
                string refusal = null;
                if (string.IsNullOrWhiteSpace(name)) refusal = "Choose the block first.";
                if (refusal == null)
                {
                    var answer = MessageBox.Show(_blockGrid,
                        "FTFPOINTS will insert \"" + name + "\" at every point rule '" + _blockId + "' matches (" + CodesOf(_blockId) + "), " +
                        "on top of whatever Civil 3D's point style draws there.\n\n" +
                        "If the point style already draws this symbol, the plan will show it twice: turn the symbol off in the point " +
                        "style, or leave FTF's insertion off.\n\nTurn insertion on?",
                        "FTF inserts a block", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes) refusal = string.Empty;
                }
                if (refusal != null)
                {
                    _loading = true;
                    try { _visible.Checked = false; }
                    finally { _loading = false; }
                    if (refusal.Length > 0) Problem(refusal);
                    return;
                }
            }
            _document.SetInsertBlock(_blockId, on);
            _blockTouched.Add(_blockId);
            SetDirty(true);
            RefreshCandidate();
            UpdateBlockRow(_blockId);
            ShowValidation();
            RunBlockSample();
            if (on && !_insertBlock.InDrawing(_insertBlock.BlockName) && _insertBlock.BlockName.IndexOf('{') < 0)
                Info("Insertion is on. \"" + _insertBlock.BlockName + "\" is not in this drawing yet: FTFPOINTS reports it missing until it is" +
                     (_insertBlock.InLibrary(_insertBlock.BlockName) ? " (Bring into Drawing copies it from the library)." : "."));
        }

        private void AddBlockToRule()
        {
            if (_document == null) return;
            FlushStage();
            var taken = new HashSet<string>(_document.BlockRuleIds().Concat(_extraBlockRules), StringComparer.OrdinalIgnoreCase);
            var choices = _document.RuleIds().Where(r => !taken.Contains(r)).ToList();
            if (choices.Count == 0) { Info("Every rule already names a block."); return; }
            var id = Pick("Add Block to Rule", "The rule to give a block (nothing is inserted until you turn insertion on):",
                          choices.Select(c => c + "  --  " + CodesOf(c)).ToList());
            if (id == null) return;
            id = id.Split(new[] { "  --  " }, StringSplitOptions.None)[0];
            _extraBlockRules.Add(id);
            PopulateBlockGrid(id);
            _insertBlock.Focus();
        }

        private void TakeBlockOff()
        {
            if (_document == null || _blockId == null) return;
            var answer = MessageBox.Show(_blockGrid, "Take the block and every block setting off rule '" + _blockId + "'? FTF will insert nothing for it.",
                                         "Take Block off Rule", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;
            _stage.Stop();
            _pending = null;
            _document.RemoveBlockInsert(_blockId);
            _blockTouched.Add(_blockId);
            _extraBlockRules.RemoveAll(x => string.Equals(x, _blockId, StringComparison.OrdinalIgnoreCase));
            _blockId = null;
            SetDirty(true);
            RefreshCandidate();
            ShowValidation();
            PopulateBlockGrid(null);
        }

        /// <summary>The block, its native zero when the standard describes it, turned as FTFPOINTS would turn it.</summary>
        private void ShowInsertPreview()
        {
            if (_document == null || _blockId == null) { _blockPreview.ShowMessage("(select a rule)"); return; }
            var name = _insertBlock.BlockName;
            if (name.IndexOf('{') >= 0) name = SampleBlockName() ?? name;   // TREE-{species}: the sample's block
            var std = new OrientationStandard { Symbols = _symbols.Select(s => s.ToRule()).ToList() };
            var symbol = std.Symbols.FirstOrDefault(s => s.DrawnWith(name));
            _blockPreview.ApplySymbolRule(symbol);
            ShowNamedBlock(_blockPreview, name);
            if (symbol == null && _blockPreview.Sketch != null)
                _blockPreview.Caption = "The standard has no native zero for this block: the arrow is its x axis.";
            ShowInsertRotation();
        }

        /// <summary>The block a {placeholder} name makes for the sample description, or null.</summary>
        private string SampleBlockName()
        {
            var rule = CompiledRule(_blockId);
            var text = (_sample.Text ?? string.Empty).Trim();
            if (rule == null || _candidate == null || text.Length == 0) return null;
            try
            {
                var p = new FieldCodeParser(_candidate).Parse("1001", text);
                return string.Equals(p.RuleId, rule.Id, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.BlockName) ? p.BlockName : null;
            }
            catch (System.Exception) { return null; }
        }

        /// <summary>The rotation the inserted block gets for the preview's test facing.</summary>
        private void ShowInsertRotation()
        {
            var rule = CompiledRule(_blockId);
            if (rule == null) return;
            var az = _blockPreview.TestAzimuth;
            var std = new OrientationStandard { Symbols = _symbols.Select(s => s.ToRule()).ToList() };
            var code = RuleDocument.LiteralCodes(rule).FirstOrDefault() ?? string.Empty;
            var p = new ParsedPoint { Code = code, RuleId = rule.Id, RotationIsAzimuth = true };
            p.RotationDegrees = rule.Rotation != null ? Angles.AzimuthToCadDegrees(az) : (double?)null;
            if (p.RotationDegrees.HasValue) p.MarkerRotationDegrees = SymbolRotation.MarkerDegreesFor(std, p);
            var marker = SymbolRotation.MarkerDegrees(std.SymbolFor(code), az) ?? Angles.AzimuthToCadDegrees(az);
            var degrees = BlockInsertion.RotationDegrees(rule, p, std, _insertBlock.BlockName, marker);
            _blockPreview.Rotation = degrees ?? 0.0;
        }

        /// <summary>Parses the sample with the staged rules and shows what FTFPOINTS would insert.</summary>
        private void RunBlockSample()
        {
            if (_blockResult == null) return;
            var text = (_sample.Text ?? string.Empty).Trim();
            var rule = CompiledRule(_blockId);
            if (rule == null || text.Length == 0 || _candidate == null) { _blockResult.Text = _candidate == null ? "Fix the problems below to preview." : string.Empty; return; }
            ParsedPoint p;
            try { p = new FieldCodeParser(_candidate).Parse("1001", text); }
            catch (System.Exception ex) { _blockResult.Text = ex.Message; return; }
            if (!string.Equals(p.RuleId, rule.Id, StringComparison.OrdinalIgnoreCase))
            {
                _blockResult.Text = "\"" + text + "\" is read by " + (p.RuleId != null ? "rule '" + p.RuleId + "'" : "no rule") + ", not this one.";
                return;
            }
            var c = CultureInfo.InvariantCulture;
            var std = _candidate.Orientation;
            var marker = p.MarkerRotationDegrees ?? p.RotationDegrees;
            var degrees = BlockInsertion.RotationDegrees(rule, p, std, p.BlockName, marker);
            var at = BlockInsertion.Position(rule, new FieldCodes.Easements.P2(0, 0), degrees ?? 0);
            var lines = new List<string>
            {
                "Block    " + (string.IsNullOrEmpty(p.BlockName) ? "(none)" : p.BlockName) + (p.InsertBlock ? string.Empty : "   (not inserted: insertion is off)"),
                "Layer    " + (string.IsNullOrEmpty(p.BlockLayer) ? "(current layer)" : p.BlockLayer),
                "Scale    " + BlockInsertion.Scale(rule, p).ToString("0.###", c),
                "Rotation " + (degrees.HasValue ? degrees.Value.ToString("0.#", c) + " (AutoCAD degrees)" : "0"),
                "Base pt  " + (Math.Abs(at.X) < 1e-12 && Math.Abs(at.Y) < 1e-12 ? "on the point" : string.Format(c, "{0:0.###}, {1:0.###} from the point", at.X, at.Y))
            };
            if (rule.BlockAttributes != null && rule.BlockAttributes.Count > 0)
            {
                var values = AttributeTemplate.Values(p, 5000.0, 10000.0, 101.25);
                lines.Add("Attributes (sample point 1001, N 10000.00 E 5000.00, elevation 101.25):");
                foreach (var kv in rule.BlockAttributes.Where(kv => !kv.Key.StartsWith("//", StringComparison.Ordinal)))
                    lines.Add("  " + kv.Key + " = " + AttributeTemplate.Format(kv.Value, values));
            }
            foreach (var d in p.Diagnostics) lines.Add(d.ToString());
            _blockResult.Text = string.Join(Environment.NewLine, lines.ToArray());
        }

        // ================================================================== save

        private void SaveClicked()
        {
            var target = _resolution != null ? _resolution.SaveTarget : null;
            if (_document == null || string.IsNullOrEmpty(target)) { Problem("There is no rules file to save to."); return; }
            if (!_dirty) { Info("No symbol changes to save."); return; }

            var problems = _document.Validate();
            if (problems.Count > 0) { Problem("Not saved: " + string.Join(Environment.NewLine, problems.ToArray())); return; }

            // The file as it is now (Point Features may have saved it since this page read it), with
            // this page's parts put on it -- so neither page's save loses the other's.
            RuleDocument fresh;
            try { fresh = RuleDocument.Load(_activePath); }
            catch (ConfigException ex) { Problem("Not saved: " + ex.Message); return; }
            catch (IOException ex) { Problem("Not saved: " + ex.Message); return; }
            if (_symbolsTouched) _document.CopySymbolsTo(fresh);
            if (_applyTouched) _document.CopyOrientationApplyTo(fresh);
            foreach (var id in _blockTouched) _document.CopyBlockInsertTo(fresh, id);

            problems = fresh.Validate();
            if (problems.Count > 0) { Problem("Not saved: " + string.Join(Environment.NewLine, problems.ToArray())); return; }
            try
            {
                // Never the factory file: SaveTarget is the office configuration or the drawing override.
                fresh.Save(target);
            }
            catch (ConfigException ex) { Problem(ex.Message); return; }
            catch (IOException ex) { Problem("Could not write: " + ex.Message); return; }
            catch (UnauthorizedAccessException ex) { Problem("Could not write: " + ex.Message); return; }

            Reload(false);
            Info("Saved to " + target + ". Previous version kept as .bak. FTFORIENT and FTFPOINTS use it from their next run.");
        }

        // ================================================================== plumbing

        private void Schedule(Action action)
        {
            if (_loading) return;
            if (_pending != null && _pending != action) FlushStage();
            _pending = action;
            _stage.Stop();
            _stage.Start();
        }

        private void FlushStage()
        {
            _stage.Stop();
            var pending = _pending;
            _pending = null;
            if (pending != null) pending();
        }

        private void SetDirty(bool dirty)
        {
            _dirty = dirty;
            if (_unsaved != null) _unsaved.Text = dirty ? "Unsaved symbol changes -- Save Symbols writes them" : string.Empty;
        }

        private void ShowValidation(IList<string> extra = null)
        {
            var problems = new List<string>();
            if (extra != null) problems.AddRange(extra);
            if (_document != null) problems.AddRange(_document.Validate());
            if (problems.Count > 0) Problem(string.Join(Environment.NewLine, problems.ToArray()));
            else if (_problems.ForeColor == Bad) Info(string.Empty);
        }

        private void Problem(string text)
        {
            _problems.ForeColor = Bad;
            _problems.Text = text ?? string.Empty;
        }

        private void Info(string text)
        {
            _problems.ForeColor = SystemColors.ControlText;
            _problems.Text = text ?? string.Empty;
        }

        private static Button SmallButton(string text, string hint, Action click)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 4, 4) };
            b.Click += (s, e) => click();
            if (!string.IsNullOrEmpty(hint)) Tip(b, hint);
            return b;
        }

        private static FlowLayoutPanel Pair(Control a, Control b)
        {
            return Flow(a, b);
        }

        private static FlowLayoutPanel Flow(params Control[] controls)
        {
            var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            foreach (var c in controls)
            {
                if (c.Margin == Padding.Empty || c is TextBox || c is ComboBox) c.Margin = new Padding(0, 0, 4, 0);
                flow.Controls.Add(c);
            }
            return flow;
        }

        private string Ask(string title, string caption, string suggestion)
        {
            using (var form = new Form { Text = title, Width = 460, Height = 150, StartPosition = FormStartPosition.CenterParent,
                                          FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var label = new Label { Text = caption, Dock = DockStyle.Top, Height = 36 };
                var box = new TextBox { Dock = DockStyle.Top, Text = suggestion ?? string.Empty };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
                form.Controls.Add(ok);
                form.Controls.Add(box);
                form.Controls.Add(label);
                form.AcceptButton = ok;
                return form.ShowDialog(_symbolGrid) == DialogResult.OK ? (box.Text ?? string.Empty).Trim() : null;
            }
        }

        private string Pick(string title, string caption, IList<string> choices)
        {
            using (var form = new Form { Text = title, Width = 460, Height = 380, StartPosition = FormStartPosition.CenterParent,
                                          FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var label = new Label { Text = caption, Dock = DockStyle.Top, Height = 36 };
                var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
                foreach (var c in choices) list.Items.Add(c);
                if (list.Items.Count > 0) list.SelectedIndex = 0;
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
                list.DoubleClick += (s, e) => { form.DialogResult = DialogResult.OK; form.Close(); };
                form.Controls.Add(list);
                form.Controls.Add(ok);
                form.Controls.Add(label);
                form.AcceptButton = ok;
                return form.ShowDialog(_blockGrid) == DialogResult.OK && list.SelectedItem != null ? list.SelectedItem.ToString() : null;
            }
        }

        private sealed class Choice
        {
            public readonly double Value;
            private readonly string _text;
            public Choice(double value, string text) { Value = value; _text = text; }
            public override string ToString() { return _text; }
        }

        /// <summary>Caption-and-input rows in a scrolling column (the editors on the right).</summary>
        private sealed class EditorRows
        {
            private readonly TableLayoutPanel _grid;
            private readonly Action<Control, string> _tip;
            private int _row;

            public EditorRows(Action<Control, string> tip)
            {
                _tip = tip;
                _grid = new TableLayoutPanel
                {
                    Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
                    GrowStyle = TableLayoutPanelGrowStyle.AddRows
                };
                _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            }

            public Panel Scroller()
            {
                var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 0, 4, 0) };
                scroll.Controls.Add(_grid);
                return scroll;
            }

            public T Full<T>(T control) where T : Control
            {
                _grid.Controls.Add(control, 0, _row);
                _grid.SetColumnSpan(control, 2);
                _row++;
                return control;
            }

            public T Labelled<T>(string caption, T control, string hint) where T : Control
            {
                var label = new Label { Text = caption.Replace("&", "&&"), AutoSize = true, Margin = new Padding(0, 6, 10, 3) };
                control.Margin = new Padding(0, 3, 0, 3);
                _grid.Controls.Add(label, 0, _row);
                _grid.Controls.Add(control, 1, _row);
                _row++;
                if (!string.IsNullOrEmpty(hint)) { _tip(control, hint); _tip(label, hint); }
                return control;
            }

            public Label Heading(string text)
            {
                return Full(new Label { Text = text, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
            }

            public Label Note(string text)
            {
                return Full(new Label { Text = text, AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 6) });
            }
        }
    }
}
