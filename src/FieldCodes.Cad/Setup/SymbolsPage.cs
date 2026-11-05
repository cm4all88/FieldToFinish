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
    /// Settings > Symbols and Rotations, four tabs over the active rules file:
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
    /// Rotation evidence and review -- how FTFORIENT weighs road lines, how near is near for each
    /// relationship it looks for (intersections, stop bars, posts, panels, luminaires, mains, valve
    /// clusters, hydrants), what a fallback rule may do, hand rotations, and the QC graphics, layers
    /// and sizes.
    ///
    /// Road lines and lists -- which survey linework is which road or utility line (role, layers,
    /// codes; PMX's list until the first edit copies it into the file), and the stop sign codes, stop
    /// sign words and luminaire codes, one a line.
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
    [SettingsPage(Group = "FieldToFinish", Label = "Symbols and Rotations", Order = 40, Commands = "FTFORIENT,FTFORIENTRESET,FTFPOINTS")]
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
        private TabControl _tabs;

        /// <summary>Set by a Point Features screen ("Symbol and block for this feature"): the
        /// code whose symbol rule to show, and the rule whose inserted block to show.</summary>
        internal static string PendingCode;
        internal static string PendingRuleId;
        private Label _testResult;
        private NumericUpDown _confidence;
        private ComboBox _tier;
        private CheckBox _markingWords;
        private CheckBox _markingFollow;
        private NumericUpDown _markingFeet;
        private NumericUpDown _markingTurn;

        // Rotation evidence and review: how road lines are weighed, what fallbacks may do, QC.
        private NumericUpDown _roadFeet;
        private NumericUpDown _extendFeet;
        private NumericUpDown _tangentFeet;
        private NumericUpDown _toleranceDegrees;
        private readonly Dictionary<string, NumericUpDown> _trust = new Dictionary<string, NumericUpDown>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ComboBox> _fallbacks = new Dictionary<string, ComboBox>(StringComparer.OrdinalIgnoreCase);
        private ComboBox _intersections;
        private ComboBox _handTurned;
        private ComboBox _qcGraphics;
        private CheckBox _qcConnections;

        // The rest of the orientation section: distances, QC layers and sizes (evidence tab), road
        // lines and the stop sign and luminaire lists (their own tab). Staged by StageDetails.
        private bool _detailsTouched;
        private readonly Dictionary<string, NumericUpDown> _distances = new Dictionary<string, NumericUpDown>(StringComparer.OrdinalIgnoreCase);
        private ComboBox _qcLayer;
        private ComboBox _qcInferredLayer;
        private NumericUpDown _qcArrow;
        private NumericUpDown _qcText;
        private Label _linesBanner;
        private DataGridView _lineGrid;
        private TextBox _stopCodes;
        private TextBox _stopWords;
        private TextBox _luminaireCodes;
        private Label _stopCodesNote;
        private Label _stopWordsNote;
        private Label _luminaireNote;

        /// <summary>The road and utility line roles, in words, as the lines grid offers them.</summary>
        private static readonly Dictionary<string, string> RoleWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Centerline", "Centre line (two-way road)" },
            { "OneWay", "One-way road (drawn in the direction of travel)" },
            { "LaneLine", "Lane or fog line" },
            { "Edge", "Pavement edge, curb or flowline" },
            { "RoadContext", "Edge of concrete or sidewalk (support only)" },
            { "StopBar", "Stop bar" },
            { "Crosswalk", "Crosswalk" },
            { "SignPanel", "Shot sign panel" },
            { "WaterMain", "Water line" },
            { "GasMain", "Gas line" },
            { "Other", "Not a road or utility line" }
        };

        /// <summary>A choice shown in words, stored as the rules file's value.</summary>
        private sealed class WordChoice
        {
            public readonly string Value;
            public readonly string Text;
            public WordChoice(string value, string text) { Value = value; Text = text; }
            public override string ToString() { return Text; }
        }

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

            var tabs = _tabs = new TabControl { Dock = DockStyle.Fill };
            var symbolsTab = new TabPage("Symbol standard (Civil 3D markers)");
            symbolsTab.Controls.Add(BuildSymbolsTab());
            var blocksTab = new TabPage("Blocks FTF inserts (opt-in rules)");
            blocksTab.Controls.Add(BuildBlocksTab());
            var evidenceTab = new TabPage("Rotation evidence and review");
            evidenceTab.Controls.Add(BuildEvidenceTab());
            tabs.TabPages.Add(symbolsTab);
            tabs.TabPages.Add(blocksTab);
            tabs.TabPages.Add(evidenceTab);
            var linesTab = new TabPage("Road lines and lists");
            linesTab.Controls.Add(BuildLinesTab());
            tabs.TabPages.Add(linesTab);
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

            // Pavement arrows and words: the direction of travel the crew wrote ("ASTO S").
            _markingWords = new CheckBox { Text = "Pavement markings: read a written direction of travel (ASTO S, SB, SOUTHBOUND)", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            Tip(_markingWords, "Read from the raw description, the full description (STRAIGHT ARROW S) and the note. A measured azimuth (AZ 197) still wins. " +
                               "Off: arrows are turned by stop bars, intersection approaches and keep-right only.");
            _markingFollow = new CheckBox { Text = "...and follow the lane's road line (curves, the approach street at intersections)", AutoSize = true };
            Tip(_markingFollow, "The arrow takes the direction of the road line beside it that is closest to what was written. Off: it points exactly the compass " +
                                "direction written, on the drawing's grid (north is +Y, whatever way the view is turned).");
            _markingFeet = new NumericUpDown { Minimum = 1, Maximum = 500, DecimalPlaces = 0, Increment = 5, Width = 64 };
            _markingTurn = new NumericUpDown { Minimum = 0, Maximum = 90, DecimalPlaces = 0, Increment = 5, Width = 64 };
            apply.Controls.Add(_markingWords, 0, 2);
            apply.SetColumnSpan(_markingWords, 2);
            apply.Controls.Add(_markingFollow, 0, 3);
            apply.SetColumnSpan(_markingFollow, 2);
            apply.Controls.Add(new Label { Text = "Road line within (ft)", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 4);
            apply.Controls.Add(_markingFeet, 1, 4);
            apply.Controls.Add(new Label { Text = "Review if the road is further off the written direction than (deg)", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 5);
            apply.Controls.Add(_markingTurn, 1, 5);
            Tip(_markingTurn, "An E arrow on a north-south road is listed for review instead of being guessed. Crews write the nearest compass point, so 45 to 60 suits most offices.");
            _markingWords.CheckedChanged += (s, e) => { if (!_loading) StageApply(); };
            _markingFollow.CheckedChanged += (s, e) => { if (!_loading) StageApply(); };
            _markingFeet.ValueChanged += (s, e) => { if (!_loading) StageApply(); };
            _markingTurn.ValueChanged += (s, e) => { if (!_loading) StageApply(); };
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

        /// <summary>
        /// How FTFORIENT weighs the road lines around a feature, how far it follows one that stops
        /// short, when road lines disagree, what a feature's fallback rule may do, how hand
        /// rotations are kept, and which QC graphics it draws. All of it is the orientation section
        /// of the rules file (the office's drafting standard), like the rest of this page.
        /// </summary>
        private Control BuildEvidenceTab()
        {
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 6, 4, 6) };
            var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            scroll.Controls.Add(grid);
            var row = 0;
            Action<string, string> heading = (text, note) =>
            {
                var h = new Label { Text = text, AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(0, row == 0 ? 0 : 12, 0, 2) };
                grid.Controls.Add(h, 0, row);
                grid.SetColumnSpan(h, 2);
                row++;
                if (note == null) return;
                var n = new Label { Text = note, AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 4) };
                grid.Controls.Add(n, 0, row);
                grid.SetColumnSpan(n, 2);
                row++;
            };
            Action<string, Control, string> add = (text, control, tip) =>
            {
                grid.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, row);
                grid.Controls.Add(control, 1, row);
                if (tip != null) Tip(control, tip);
                row++;
            };
            Func<decimal, decimal, int, decimal, NumericUpDown> number = (min, max, places, step) =>
            {
                var n = new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = places, Increment = step, Width = 70 };
                n.ValueChanged += (s, e) => { if (!_loading) StageApply(); };
                return n;
            };
            Func<WordChoice[], ComboBox> choose = choices =>
            {
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
                foreach (var ch in choices) c.Items.Add(ch);
                c.SelectedIndexChanged += (s, e) => { if (!_loading) StageApply(); };
                return c;
            };

            heading("Road lines a direction is taken from",
                    "A sign, light, hydrant or arrow takes its road's direction from the road lines near it: centre lines, lane and fog lines, " +
                    "pavement edges, curbs and flowlines, and (as support only) edges of concrete. Fences, property lines and utilities are never " +
                    "road lines. Nothing here changes survey linework.");
            _roadFeet = number(5, 500, 0, 5);
            add("Look for road lines within (ft)", _roadFeet, "Road lines further than this from a feature are not considered.");
            _extendFeet = number(0, 200, 0, 5);
            add("Follow a line that stops short at most (ft)", _extendFeet,
                "A pavement edge or stripe that ends before the feature is followed past its end, along its last stretch, this far at most, with less " +
                "confidence the further it goes. Further than this it is not used. Where the line resumes beyond the feature, the gap is bridged.");
            _tangentFeet = number(2, 200, 0, 5);
            add("Take a line's direction over (ft)", _tangentFeet,
                "The road's direction where the feature stands, over this length of line: a curve gives its local tangent, and a small jog in a " +
                "shot edge does not swing the result.");
            _toleranceDegrees = number(1, 90, 0, 1);
            add("Road lines disagree beyond (degrees)", _toleranceDegrees,
                "Road lines near a feature that run further apart than this send it to review instead of one being picked.");
            foreach (var role in new[] { Tuple.Create("Centerline", "Trust centre lines (and one-way lines)"), Tuple.Create("LaneLine", "Trust lane and fog lines"),
                                         Tuple.Create("Edge", "Trust pavement edges, curbs, flowlines"), Tuple.Create("RoadContext", "Trust edges of concrete, sidewalks") })
            {
                var n = number(0, 1, 2, 0.05m);
                _trust[role.Item1] = n;
                add(role.Item2 + " (0 to 1)", n, "How far this kind of line is trusted as the road's direction, against the others. 0: never used.");
            }
            _intersections = choose(new[] { new WordChoice("Approach", "Decide from the approach it stands on"), new WordChoice("Review", "List for review") });
            add("Signs and markings at an intersection", _intersections, null);

            heading("How near is near",
                    "How far FTFORIENT looks for the thing that turns a feature: an intersection, a stop bar, the post a sign shares, a shot sign " +
                    "panel, a luminaire shot, a water or gas line, the rest of a valve cluster, a hydrant. Further than this, it is not related.");
            foreach (var d in new[]
            {
                Tuple.Create("intersectionSearchFeet", "Intersections within (ft)",
                             "A sign or signal pole this close to an intersection is decided by the approach it stands on: a sign faces the traffic " +
                             "coming in, a mast arm reaches across its leg. A stop bar this close to one takes that approach's traffic. Also how far back " +
                             "along an approach a sign may stand and still belong to it."),
                Tuple.Create("stopBarSearchFeet", "A stop or yield sign's stop bar within (ft)",
                             "A stop or yield sign (Road lines and lists says which) faces the traffic that stops at a stop bar this close to it, " +
                             "standing on that traffic's right."),
                Tuple.Create("samePostFeet", "Signs on one post within (ft)",
                             "Signs this close together are on one post. One whose facing was not recorded (FACES N) goes to review, because signs on " +
                             "one post often face different ways."),
                Tuple.Create("panelSnapFeet", "A shot sign panel within (ft)",
                             "A sign this close to a shot sign panel line (SNL) is turned by the panel: its face is on the side away from the post."),
                Tuple.Create("luminaireSearchFeet", "A luminaire shot within (ft) of its pole",
                             "A light's arm points from the pole to the nearest luminaire shot (Road lines and lists says which codes) this close. " +
                             "More shots within half this distance are more arms."),
                Tuple.Create("mainSnapFeet", "A valve on its water or gas line within (ft)",
                             "A valve this close to a surveyed water or gas line lies along it. Further off, it is worked out with the valves around it."),
                Tuple.Create("valveClusterFeet", "Valves in one cluster within (ft)",
                             "Valves this close to one another are worked out together: a tee, or a hydrant's valves."),
                Tuple.Create("hydrantSearchFeet", "A hydrant within (ft) of a valve cluster",
                             "A water valve cluster with a hydrant this close is read as that hydrant's tee and lateral. With two hydrants about as " +
                             "close, its valves go to review.")
            })
            {
                var range = RuleDocument.OrientationDistances.First(r => r.Key == d.Item1);
                var small = range.Minimum < 1;
                var n = new NumericUpDown
                {
                    Minimum = (decimal)range.Minimum, Maximum = (decimal)range.Maximum, DecimalPlaces = small ? 2 : 0,
                    Increment = small ? 0.5m : 5m, Width = 70
                };
                n.ValueChanged += (s, e) => { if (!_loading) StageDetails(); };
                _distances[d.Item1] = n;
                add(d.Item2, n, d.Item3 + " (" + range.Minimum.ToString("0.##", CultureInfo.InvariantCulture) + " to " +
                                range.Maximum.ToString("0.##", CultureInfo.InvariantCulture) + " ft.)");
            }

            heading("What a fallback rule may do",
                    "A fallback rule decides only when nothing recorded or related does: a light's arm toward the road, a hydrant's nozzle toward the " +
                    "street, a sign facing the traffic beside it. Apply turns the symbol (the report says it was the fallback); Review lists it with the " +
                    "suggestion; Leave leaves it as surveyed.");
            foreach (var kind in new[] { Tuple.Create("Sign", "Signs"), Tuple.Create("StreetLight", "Street lights and power poles with arms"),
                                         Tuple.Create("SignalPole", "Signal poles"), Tuple.Create("Hydrant", "Hydrants"),
                                         Tuple.Create("PavementArrow", "Pavement arrows"), Tuple.Create("PavementWord", "Pavement words"),
                                         Tuple.Create("WaterValve", "Water valves"), Tuple.Create("GasValve", "Gas valves") })
            {
                var c = choose(new[] { new WordChoice("Apply", "Apply"), new WordChoice("Review", "Review"), new WordChoice("Leave", "Leave as surveyed") });
                _fallbacks[kind.Item1] = c;
                add(kind.Item2, c, null);
            }

            heading("Rotations set by hand, and QC graphics", null);
            _handTurned = choose(new[] { new WordChoice("MeasuredReplaces", "Keep, unless the crew measured the direction"), new WordChoice("Keep", "Always keep") });
            add("A marker turned by hand", _handTurned, "A direction set or accepted with FTFORIENTPOINT is always kept, whatever this says.");
            _qcGraphics = choose(new[] { new WordChoice("ReviewOnly", "Only what needs review"), new WordChoice("All", "Every decision"), new WordChoice("Off", "None") });
            add("QC graphics FTFORIENT draws (not plotted)", _qcGraphics, "Preview always draws every decision. QC graphics are sized by the annotation scale and removed by FTFORIENTRESET.");
            _qcConnections = new CheckBox { Text = "Draw inferred underground connections (water main, hydrant lateral)", AutoSize = true };
            _qcConnections.CheckedChanged += (s, e) => { if (!_loading) StageApply(); };
            Tip(_qcConnections, "Off: they are on the review list and in the report only. Never drawn as utility linework, never plotted.");
            grid.Controls.Add(_qcConnections, 0, row);
            grid.SetColumnSpan(_qcConnections, 2);
            row++;

            Func<ComboBox> layerBox = () =>
            {
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 260, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
                if (Context != null && Context.Drawing != null && Context.Drawing.Layers != null)
                    foreach (var l in Context.Drawing.Layers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) c.Items.Add(l);
                c.TextChanged += (s, e) => { if (!_loading) Schedule(StageDetails); };
                return c;
            };
            _qcLayer = layerBox();
            add("QC layer (made not to plot)", _qcLayer,
                "FTFORIENT's direction arrows and notes go on this layer; FTFORIENTRESET removes them. FTFORIENT sets the layer not to plot, " +
                "so use a layer kept for QC only, never one with plan work on it. Pick one of this drawing's layers or type a name. PMX: FTF-QC-ORIENT.");
            _qcInferredLayer = layerBox();
            add("Inferred connections layer (made not to plot)", _qcInferredLayer,
                "Inferred water mains and hydrant laterals go on this layer when they are drawn (the box above); never utility linework. " +
                "FTFORIENT sets it not to plot, so use a QC-only layer. PMX: FTF-QC-ORIENT-INFERRED.");
            _qcArrow = new NumericUpDown { Minimum = (decimal)RuleDocument.QcArrowMinimum, Maximum = (decimal)RuleDocument.QcArrowMaximum, DecimalPlaces = 2, Increment = 0.05m, Width = 70 };
            _qcArrow.ValueChanged += (s, e) => { if (!_loading) StageDetails(); };
            add("QC arrow length (inches, plotted)", _qcArrow, "The length of a QC direction arrow on the sheet; the drawing length follows the annotation scale. PMX: 0.3.");
            _qcText = new NumericUpDown { Minimum = 0.01m, Maximum = 1m, DecimalPlaces = 3, Increment = 0.01m, Width = 70 };
            _qcText.ValueChanged += (s, e) => { if (!_loading) StageDetails(); };
            add("QC text height (inches, plotted)", _qcText, "The height of QC notes on the sheet; the drawing height follows the annotation scale. PMX: 0.06.");
            return scroll;
        }

        private static void Select(ComboBox box, string value)
        {
            if (box == null) return;
            foreach (var item in box.Items)
            {
                var c = item as WordChoice;
                if (c != null && string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = item; return; }
            }
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        private static string Chosen(ComboBox box)
        {
            var c = box != null ? box.SelectedItem as WordChoice : null;
            return c != null ? c.Value : null;
        }

        private static void SetNumber(NumericUpDown n, double value)
        {
            if (n == null) return;
            n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, (decimal)value));
        }

        /// <summary>
        /// Which survey linework is which road or utility line (FTFORIENT reads a feature's road, stop
        /// bar, shot sign panel, water or gas line from it), and the codes and words that make a sign a
        /// stop or yield sign and a shot a luminaire. Each list in the rules file replaces PMX's whole
        /// list; until the file names one, PMX's is shown.
        /// </summary>
        private Control BuildLinesTab()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(6, 4, 4, 4) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _linesBanner = new Label { AutoSize = true, MaximumSize = new Size(940, 0), Margin = new Padding(0, 2, 0, 4) };
            root.Controls.Add(_linesBanner, 0, 0);

            _lineGrid = new DataGridView
            {
                Dock = DockStyle.Fill, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false, EditMode = DataGridViewEditMode.EditOnEnter,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, MinimumSize = new Size(0, 160)
            };
            var role = new DataGridViewComboBoxColumn
            {
                HeaderText = "What the line is", Width = 290, FlatStyle = FlatStyle.Flat,
                ToolTipText = "Centre lines, one-way lines, lane lines and edges give a road's direction (Rotation evidence says how far each is trusted); " +
                              "edges of concrete only support one. Stop bars turn stop signs and pavement arrows; a shot sign panel turns its sign; " +
                              "water and gas lines turn their valves. Not a road or utility line: never used (for linework a pattern would otherwise catch)."
            };
            foreach (var name in EditableOrientationDetails.Roles) role.Items.Add(RoleWords[name]);
            _lineGrid.Columns.Add(role);
            _lineGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Layers", Width = 380,
                ToolTipText = "Layer names, separated by commas; * and ? are wildcards (V-ROAD-CNTR* is every layer starting V-ROAD-CNTR)."
            });
            _lineGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Figure codes", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                ToolTipText = "Survey figure names or codes, separated by spaces; figure digits are ignored (EP1 is EP); * and ? are wildcards. " +
                              "A figure's code is looked at before its layer."
            });
            _lineGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_lineGrid.IsCurrentCellDirty && _lineGrid.CurrentCell is DataGridViewComboBoxCell) _lineGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _lineGrid.CellValueChanged += (s, e) => { if (!_loading) Schedule(StageDetails); };
            _lineGrid.DataError += (s, e) => { e.ThrowException = false; };
            root.Controls.Add(_lineGrid, 0, 1);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
            buttons.Controls.Add(SmallButton("Add", "A new rule below the selected one.", AddLine));
            buttons.Controls.Add(SmallButton("Remove", "Removes the selected rule.", RemoveLine));
            buttons.Controls.Add(SmallButton("Move Up", "The first rule that matches wins: a specific layer goes above a wildcard that also covers it.", () => MoveLine(-1)));
            buttons.Controls.Add(SmallButton("Move Down", null, () => MoveLine(1)));
            buttons.Controls.Add(SmallButton("Use the PMX List", "Shows PMX's built-in list; saving takes the rules file's own list out, so PMX's applies again.", UseBuiltInLinesClicked));
            root.Controls.Add(buttons, 0, 2);

            root.Controls.Add(new Label
            {
                Text = "Sign and light codes, one a line. Each list in the rules file replaces PMX's whole list; an empty box is PMX's list.",
                AutoSize = true, MaximumSize = new Size(940, 0), Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(0, 10, 0, 2)
            }, 0, 3);

            var lists = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, RowCount = 4, Margin = new Padding(0) };
            for (var i = 0; i < 3; i++) lists.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Func<int, string, string, TextBox> list = (column, caption, tip) =>
            {
                lists.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 2, 16, 2) }, column, 0);
                var box = new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Width = 230, Height = 110, Margin = new Padding(0, 0, 16, 2) };
                box.TextChanged += (s, e) => { if (!_loading) Schedule(StageDetails); };
                Tip(box, tip);
                lists.Controls.Add(box, column, 1);
                return box;
            };
            _stopCodes = list(0, "Stop and yield sign codes",
                "Sign codes that control an approach: a stop bar or the intersection decides which way they face. Figure digits are ignored; * and ? are wildcards.");
            _stopWords = list(1, "Words that make a sign a stop or yield sign",
                "A sign whose description or note has one of these words (whole words; ALL WAY may have a space) is a stop or yield sign, whatever its code.");
            _luminaireCodes = list(2, "Luminaire codes (the end of a light's arm)",
                "Codes of a shot at a light's head: the light's arm points from the pole to the nearest one (Rotation evidence says how near).");
            Func<int, Label> note = column =>
            {
                var n = new Label { AutoSize = true, MaximumSize = new Size(240, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 16, 2) };
                lists.Controls.Add(n, column, 2);
                return n;
            };
            _stopCodesNote = note(0);
            _stopWordsNote = note(1);
            _luminaireNote = note(2);
            var pmx = OrientationStandard.Pmx();
            Action<int, TextBox, List<string>> pmxButton = (column, box, values) =>
                lists.Controls.Add(SmallButton("Use the PMX List", "Puts PMX's list back: " + string.Join(", ", values.ToArray()) + ".",
                                               () => { box.Text = string.Join(Environment.NewLine, values.ToArray()); }), column, 3);
            pmxButton(0, _stopCodes, pmx.StopSignCodes);
            pmxButton(1, _stopWords, pmx.StopSignWords);
            pmxButton(2, _luminaireCodes, pmx.LuminaireCodes);
            root.Controls.Add(lists, 0, 4);
            return root;
        }

        private static double DistanceOf(EditableOrientationDetails d, string key)
        {
            switch (key)
            {
                case "intersectionSearchFeet": return d.IntersectionSearchFeet;
                case "stopBarSearchFeet": return d.StopBarSearchFeet;
                case "samePostFeet": return d.SamePostFeet;
                case "valveClusterFeet": return d.ValveClusterFeet;
                case "hydrantSearchFeet": return d.HydrantSearchFeet;
                case "mainSnapFeet": return d.MainSnapFeet;
                case "panelSnapFeet": return d.PanelSnapFeet;
                case "luminaireSearchFeet": return d.LuminaireSearchFeet;
                default: return 0;
            }
        }

        private static void SetDistance(EditableOrientationDetails d, string key, double value)
        {
            switch (key)
            {
                case "intersectionSearchFeet": d.IntersectionSearchFeet = value; break;
                case "stopBarSearchFeet": d.StopBarSearchFeet = value; break;
                case "samePostFeet": d.SamePostFeet = value; break;
                case "valveClusterFeet": d.ValveClusterFeet = value; break;
                case "hydrantSearchFeet": d.HydrantSearchFeet = value; break;
                case "mainSnapFeet": d.MainSnapFeet = value; break;
                case "panelSnapFeet": d.PanelSnapFeet = value; break;
                case "luminaireSearchFeet": d.LuminaireSearchFeet = value; break;
            }
        }

        /// <summary>Shows the lines, lists, distances and QC layers (call with _loading set).</summary>
        private void FillDetails(EditableOrientationDetails d)
        {
            foreach (var n in _distances) SetNumber(n.Value, DistanceOf(d, n.Key));
            ShowLayer(_qcLayer, d.QcLayer);
            ShowLayer(_qcInferredLayer, d.QcInferredLayer);
            SetNumber(_qcArrow, d.QcArrowPlotted);
            SetNumber(_qcText, d.QcTextPlotted);
            FillLineGrid(d.Lines);
            _stopCodes.Text = string.Join(Environment.NewLine, d.StopSignCodes.ToArray());
            _stopWords.Text = string.Join(Environment.NewLine, d.StopSignWords.ToArray());
            _luminaireCodes.Text = string.Join(Environment.NewLine, d.LuminaireCodes.ToArray());
            ShowDetailNotes(d);
        }

        /// <summary>A layer box shows the name as written, offered in its list even when this drawing lacks it.</summary>
        private static void ShowLayer(ComboBox box, string layer)
        {
            var name = (layer ?? string.Empty).Trim();
            if (name.Length > 0 && !box.Items.Cast<object>().Any(i => string.Equals(Convert.ToString(i), name, StringComparison.OrdinalIgnoreCase)))
                box.Items.Add(name);
            box.Text = layer ?? string.Empty;
        }

        private void FillLineGrid(IEnumerable<EditableLineRole> lines)
        {
            var role = (DataGridViewComboBoxColumn)_lineGrid.Columns[0];
            _lineGrid.Rows.Clear();
            foreach (var l in lines)
            {
                // A role this build does not know is shown as written, so the check can name it.
                var words = RoleText(l.Role);
                if (!role.Items.Contains(words)) role.Items.Add(words);
                var i = _lineGrid.Rows.Add(words, l.LayersText, l.CodesText);
                _lineGrid.Rows[i].Tag = l.SourceIndex;
            }
        }

        private static string RoleText(string role)
        {
            string words;
            return RoleWords.TryGetValue((role ?? string.Empty).Trim(), out words) ? words : (role ?? string.Empty).Trim();
        }

        private static string RoleName(string words)
        {
            var hit = RoleWords.FirstOrDefault(kv => string.Equals(kv.Value, words, StringComparison.Ordinal));
            return hit.Key ?? (words ?? string.Empty).Trim();
        }

        private List<EditableLineRole> ReadLineGrid()
        {
            _lineGrid.EndEdit();
            var result = new List<EditableLineRole>();
            foreach (DataGridViewRow row in _lineGrid.Rows)
            {
                if (row.IsNewRow) continue;
                result.Add(new EditableLineRole
                {
                    SourceIndex = row.Tag is int ? (int)row.Tag : -1,
                    Role = RoleName(Convert.ToString(row.Cells[0].Value)),
                    Layers = EditableLineRole.ParseLayers(Convert.ToString(row.Cells[1].Value)),
                    Codes = EditableLineRole.ParseCodes(Convert.ToString(row.Cells[2].Value))
                });
            }
            return result;
        }

        private void ShowDetailNotes(EditableOrientationDetails d)
        {
            if (d.LinesBuiltIn)
            {
                _linesBanner.ForeColor = Color.FromArgb(160, 90, 0);
                _linesBanner.Text = "PMX's built-in list is in force: the rules file has none of its own. Your first edit copies these " + d.Lines.Count +
                                    " rules into the rules file, where they become the office's to change (a list in the file replaces PMX's whole list).";
            }
            else
            {
                _linesBanner.ForeColor = SystemColors.GrayText;
                _linesBanner.Text = "The rules file's own list (" + d.Lines.Count + " rules), in place of PMX's. A line's figure code is looked at first, " +
                                    "then its layer; the first rule that matches wins. Use the PMX List goes back to the built-in one.";
            }
            Func<bool, string> say = builtIn => builtIn ? "PMX's list (the rules file names none)." : "The rules file's own list. Empty: PMX's list applies.";
            _stopCodesNote.Text = say(d.StopSignCodesBuiltIn);
            _stopWordsNote.Text = say(d.StopSignWordsBuiltIn);
            _luminaireNote.Text = say(d.LuminaireCodesBuiltIn);
        }

        private int SelectedLine()
        {
            return _lineGrid.CurrentCell != null ? _lineGrid.CurrentCell.RowIndex : -1;
        }

        private void AddLine()
        {
            var at = SelectedLine() + 1;
            if (at <= 0 || at > _lineGrid.Rows.Count) at = _lineGrid.Rows.Count;
            _loading = true;
            try
            {
                _lineGrid.Rows.Insert(at, RoleText("Other"), string.Empty, string.Empty);
                _lineGrid.Rows[at].Tag = -1;
            }
            finally { _loading = false; }
            _lineGrid.CurrentCell = _lineGrid.Rows[at].Cells[0];
            StageDetails();
        }

        private void RemoveLine()
        {
            var i = SelectedLine();
            if (i < 0 || i >= _lineGrid.Rows.Count) return;
            _loading = true;
            try { _lineGrid.Rows.RemoveAt(i); }
            finally { _loading = false; }
            StageDetails();
        }

        private void MoveLine(int step)
        {
            var i = SelectedLine();
            var j = i + step;
            if (i < 0 || j < 0 || j >= _lineGrid.Rows.Count) return;
            var lines = ReadLineGrid();
            var moved = lines[i];
            lines.RemoveAt(i);
            lines.Insert(j, moved);
            _loading = true;
            try { FillLineGrid(lines); }
            finally { _loading = false; }
            _lineGrid.CurrentCell = _lineGrid.Rows[j].Cells[0];
            StageDetails();
        }

        private void UseBuiltInLinesClicked()
        {
            _loading = true;
            try { FillLineGrid(OrientationStandard.Pmx().Lines.Select(r => EditableLineRole.From(r, -1))); }
            finally { _loading = false; }
            StageDetails();
        }

        /// <summary>Puts the lines, lists, distances and QC layers on the staged document.</summary>
        private void StageDetails()
        {
            if (_document == null || _loading || _lineGrid == null) return;
            var d = _document.GetOrientationDetails();
            d.Lines = ReadLineGrid();
            d.StopSignCodes = EditableOrientationDetails.ParseList(_stopCodes.Text);
            d.StopSignWords = EditableOrientationDetails.ParseList(_stopWords.Text);
            d.LuminaireCodes = EditableOrientationDetails.ParseList(_luminaireCodes.Text);
            foreach (var n in _distances) SetDistance(d, n.Key, (double)n.Value.Value);
            d.QcLayer = (_qcLayer.Text ?? string.Empty).Trim();
            d.QcInferredLayer = (_qcInferredLayer.Text ?? string.Empty).Trim();
            d.QcArrowPlotted = (double)_qcArrow.Value;
            d.QcTextPlotted = (double)_qcText.Value;
            _document.ApplyOrientationDetails(d);
            // Written rows now sit at their grid places in the file (or are PMX's again).
            for (var i = 0; i < d.Lines.Count && i < _lineGrid.Rows.Count; i++) _lineGrid.Rows[i].Tag = d.Lines[i].SourceIndex;
            _detailsTouched = true;
            SetDirty(true);
            ShowDetailNotes(_document.GetOrientationDetails());
            ShowValidation();
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
            _detailsTouched = false;
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
                var defaults = new OrientationStandard();
                _markingWords.Checked = _document != null ? apply.MarkingDirectionWords : defaults.MarkingDirectionWords;
                _markingFollow.Checked = _document != null ? apply.MarkingFollowRoad : defaults.MarkingFollowRoad;
                _markingFeet.Value = Math.Max(_markingFeet.Minimum, Math.Min(_markingFeet.Maximum, (decimal)(_document != null ? apply.MarkingRoadFeet : defaults.MarkingRoadFeet)));
                _markingTurn.Value = Math.Max(_markingTurn.Minimum, Math.Min(_markingTurn.Maximum, (decimal)(_document != null ? apply.MarkingMaxTurnDegrees : defaults.MarkingMaxTurnDegrees)));
                if (_document != null)
                {
                    SetNumber(_roadFeet, apply.RoadSearchFeet);
                    SetNumber(_extendFeet, apply.MaxExtensionFeet);
                    SetNumber(_tangentFeet, apply.LocalTangentFeet);
                    SetNumber(_toleranceDegrees, apply.AlignmentToleranceDegrees);
                    foreach (var t in _trust) SetNumber(t.Value, apply.Reliability != null && apply.Reliability.ContainsKey(t.Key) ? apply.Reliability[t.Key] : 0);
                    foreach (var f in _fallbacks) Select(f.Value, apply.Fallbacks != null && apply.Fallbacks.ContainsKey(f.Key) ? apply.Fallbacks[f.Key] : "Apply");
                    Select(_intersections, apply.IntersectionHandling);
                    Select(_handTurned, apply.HandTurned);
                    Select(_qcGraphics, apply.QcGraphics);
                    _qcConnections.Checked = apply.QcShowInferredConnections;
                }
                else
                {
                    SetNumber(_roadFeet, defaults.RoadSearchFeet);
                    SetNumber(_extendFeet, defaults.MaxExtensionFeet);
                    SetNumber(_tangentFeet, defaults.LocalTangentFeet);
                    SetNumber(_toleranceDegrees, defaults.AlignmentToleranceDegrees);
                    foreach (var t in _trust) { LineRole role; SetNumber(t.Value, Enum.TryParse(t.Key, out role) ? defaults.ReliabilityOf(role) : 0); }
                    foreach (var f in _fallbacks) { OrientKind kind; Select(f.Value, Enum.TryParse(f.Key, out kind) ? defaults.FallbackFor(kind).ToString() : "Apply"); }
                    Select(_intersections, defaults.IntersectionHandling.ToString());
                    Select(_handTurned, defaults.HandTurned.ToString());
                    Select(_qcGraphics, defaults.QcGraphics.ToString());
                    _qcConnections.Checked = defaults.QcShowInferredConnections;
                }
                FillDetails(_document != null ? _document.GetOrientationDetails() : EditableOrientationDetails.BuiltIn());
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
            apply.MarkingDirectionWords = _markingWords.Checked;
            apply.MarkingFollowRoad = _markingFollow.Checked;
            apply.MarkingRoadFeet = (double)_markingFeet.Value;
            apply.MarkingMaxTurnDegrees = (double)_markingTurn.Value;
            if (_roadFeet != null)
            {
                apply.RoadSearchFeet = (double)_roadFeet.Value;
                apply.MaxExtensionFeet = (double)_extendFeet.Value;
                apply.LocalTangentFeet = (double)_tangentFeet.Value;
                apply.AlignmentToleranceDegrees = (double)_toleranceDegrees.Value;
                foreach (var t in _trust) apply.Reliability[t.Key] = (double)t.Value.Value;
                foreach (var f in _fallbacks) { var v = Chosen(f.Value); if (v != null) apply.Fallbacks[f.Key] = v; }
                apply.IntersectionHandling = Chosen(_intersections) ?? apply.IntersectionHandling;
                apply.HandTurned = Chosen(_handTurned) ?? apply.HandTurned;
                apply.QcGraphics = Chosen(_qcGraphics) ?? apply.QcGraphics;
                apply.QcShowInferredConnections = _qcConnections.Checked;
            }
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

        /// <summary>Arriving from a Point Features screen: show that feature's symbol rule (and
        /// its inserted block, when it has one).</summary>
        public override void OnShown()
        {
            var code = PendingCode;
            var ruleId = PendingRuleId;
            PendingCode = null;
            PendingRuleId = null;
            if (_testCode == null || (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(ruleId))) return;
            if (!string.IsNullOrWhiteSpace(code))
            {
                _testCode.Text = code.Trim();
                var standard = new OrientationStandard { Symbols = _symbols.Select(x => x.ToRule()).ToList() };
                var index = standard.SymbolIndexFor(code.Trim());
                if (index >= 0 && index < _symbolGrid.Items.Count)
                {
                    _symbolGrid.SelectedItems.Clear();
                    _symbolGrid.Items[index].Selected = true;
                    _symbolGrid.Items[index].EnsureVisible();
                }
            }
            if (!string.IsNullOrWhiteSpace(ruleId))
                foreach (ListViewItem item in _blockGrid.Items)
                    if (string.Equals(item.Tag as string, ruleId.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        item.Selected = true;
                        item.EnsureVisible();
                        if (_tabs != null && _tabs.TabPages.Count > 1 && string.IsNullOrWhiteSpace(code)) _tabs.SelectedIndex = 1;
                    }
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
            if (_detailsTouched) _document.CopyOrientationDetailsTo(fresh);
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
