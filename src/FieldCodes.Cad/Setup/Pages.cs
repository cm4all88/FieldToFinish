using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    // ------------------------------------------------------------------- general

    internal sealed class GeneralPage : SetupPage
    {
        private TextBox _unitsPerFoot;
        private ComboBox _reportLocation;
        private TextBox _reportFolder;
        private Button _browse;
        private CheckBox _writeWhenEmpty;
        private CheckBox _confirmDelete;
        private Label _scaleNote;
        private TextBox _layerMappings;

        public override string Title { get { return "General"; } }
        public override string AffectedCommands { get { return "All commands"; } }

        protected override void BuildBody()
        {
            Heading("Drawing");
            _unitsPerFoot = TextRow("Drawing units per survey foot",
                "1.0 when the drawing is in feet, 0.3048 when it is in metres.");
            _scaleNote = LiveNote();

            Heading("Exception report");
            _reportLocation = ComboRow("Write the report", new object[]
            {
                "Beside the drawing",
                "In a folder I choose"
            }, "Where the CSV of unparseable and suspicious points goes.");

            _reportFolder = TextRow("Report folder",
                "Always editable, so a blank or wrong path can be fixed here. Typing " +
                "a folder or browsing to one switches the report location to it; a " +
                "folder that does not exist yet is created when the report is written.",
                300);

            _browse = new Button { Text = "Browse...", AutoSize = true };
            _browse.Click += OnBrowse;
            AddFullWidth(_browse);

            _writeWhenEmpty = CheckRow("Write the report even when there is nothing to report",
                "Leaving this on means an old report can never be mistaken for the current run.");

            Heading("Safety");
            _confirmDelete = CheckRow("Ask before a re-run erases the previous run's work",
                "Every command deletes only what it drew before redrawing.");

            Heading("Layer mappings (project / profile)");
            Note("When a project standard names a layer differently from the FTF default, map it " +
                 "here, one per line: FTF-LAYER = PROJECT-LAYER. For example " +
                 "V-UTIL-STRM-E = C-STRM-PIPE. Production tools use the mapped layer, and never " +
                 "create a duplicate of a layer already in the drawing. Save to a drafting profile " +
                 "to keep a client's mappings together.");
            _layerMappings = AddFullWidth(new TextBox
            {
                Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 540, Height = 110,
                Font = new Font(FontFamily.GenericMonospace, 9f)
            });

            _reportLocation.SelectedIndexChanged += (s, e) => SyncFolderEnabled();
            _reportFolder.TextChanged += (s, e) => OnFolderTyped();
            _unitsPerFoot.TextChanged += (s, e) => Refresh();
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Folder for exception reports";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _reportFolder.Text = dialog.SelectedPath;
                    _reportLocation.SelectedIndex = 1;   // they clearly want it used
                }
            }
        }

        /// <summary>
        /// The folder box stays editable in every mode, so a blank or wrong path can
        /// always be corrected. Typing into it selects the custom location; nothing
        /// is ever locked behind the combo.
        /// </summary>
        private void SyncFolderEnabled()
        {
            var custom = _reportLocation.SelectedIndex == 1;
            _reportFolder.Enabled = true;
            _browse.Enabled = true;
            _reportFolder.BackColor = custom
                ? System.Drawing.SystemColors.Window
                : System.Drawing.SystemColors.Control;
        }

        private void OnFolderTyped()
        {
            if (_reportFolder.Focused &&
                !string.IsNullOrWhiteSpace(_reportFolder.Text) &&
                _reportLocation.SelectedIndex != 1)
                _reportLocation.SelectedIndex = 1;
        }

        public override void Refresh()
        {
            _scaleNote.Text = string.Format(CultureInfo.InvariantCulture,
                "Annotation scale {0} - one plotted unit is {1:0.###} drawing units.",
                Context.Drawing.AnnotationScaleName, Context.Drawing.Scale);
        }

        public override void LoadFrom(FtfSettings s)
        {
            _unitsPerFoot.Text = Fmt(s.General.UnitsPerFoot);
            _reportLocation.SelectedIndex =
                s.General.ReportLocation == ReportLocation.CustomFolder ? 1 : 0;
            _reportFolder.Text = s.General.ReportFolder ?? string.Empty;
            _writeWhenEmpty.Checked = s.General.WriteReportWhenEmpty;
            _confirmDelete.Checked = s.General.ConfirmBeforeDelete;
            _layerMappings.Text = string.Join(Environment.NewLine,
                (s.General.LayerMappings ?? new List<LayerMapping>()).Select(m => m.From + " = " + m.To).ToArray());
            SyncFolderEnabled();
            Refresh();
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            var units = s.General.UnitsPerFoot;
            ReadDouble(_unitsPerFoot, "General: drawing units per survey foot",
                       v => v > 0, "must be greater than zero", problems, ref units);
            s.General.UnitsPerFoot = units;

            s.General.ReportLocation = _reportLocation.SelectedIndex == 1
                ? ReportLocation.CustomFolder
                : ReportLocation.BesideDrawing;
            s.General.ReportFolder = (_reportFolder.Text ?? string.Empty).Trim();
            s.General.WriteReportWhenEmpty = _writeWhenEmpty.Checked;
            s.General.ConfirmBeforeDelete = _confirmDelete.Checked;

            var mappings = new List<LayerMapping>();
            var n = 0;
            foreach (var raw in (_layerMappings.Text ?? string.Empty).Split('\n'))
            {
                n++;
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0 || eq == line.Length - 1)
                {
                    problems.Add("General: layer mapping line " + n + " needs FTF-LAYER = PROJECT-LAYER.");
                    continue;
                }
                mappings.Add(new LayerMapping { From = line.Substring(0, eq).Trim(), To = line.Substring(eq + 1).Trim() });
            }
            s.General.LayerMappings = mappings;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.General.RestoreDefaults();
            LoadFrom(s);
        }
    }

    // --------------------------------------------------------------------- trees

    internal sealed class TreesPage : SetupPage
    {
        private TextBox _decimals;
        private ComboBox _average;
        private Label _worked;
        private CheckBox _unify;
        private ComboBox _dripLinetype;

        public override string Title { get { return "Trees & Driplines"; } }
        public override string AffectedCommands { get { return "FTFPOINTS, FTFDRIP"; } }

        protected override void BuildBody()
        {
            Heading("Trunk size");
            _decimals = TextRow("Decimal places in labels",
                "Display only. Geometry always uses the unrounded average.");

            _average = ComboRow("Multi-stem average", new object[]
            {
                StemAverageMethod.Arithmetic,
                StemAverageMethod.Quadratic,
                StemAverageMethod.LargestPlusHalf,
                StemAverageMethod.Sum
            }, "How the diameters of a cluster collapse to one reported size.");

            _worked = LiveNote();

            Heading("Driplines");
            _unify = CheckRow("Trim overlapping canopies to their outer envelope",
                "Off draws every canopy as a full circle, overlaps and all. A code " +
                "rule that states its own preference wins over this.");

            var linetypes = new List<object> { "(layer's linetype)" };
            foreach (var name in Context.Drawing.Linetypes) linetypes.Add(name);
            _dripLinetype = ComboRow("Drip linetype", linetypes,
                "The linetype the drip circles and arcs are drawn with -- typically a " +
                "dashed standard. A linetype the drawing does not have yet is loaded " +
                "from acad.lin automatically. The drip LAYER belongs to each species " +
                "rule, so different species can go on different layers.");

            Heading("Species");
            Note("Species codes come from the rules file and are listed on the Field Codes " +
                 "tab. The tree pattern is generated from them, so the two cannot drift apart.");

            _average.SelectedIndexChanged += (s, e) => Refresh();
            _decimals.TextChanged += (s, e) => Refresh();
        }

        public override void Refresh()
        {
            var method = _average.SelectedItem is StemAverageMethod
                ? (StemAverageMethod)_average.SelectedItem
                : StemAverageMethod.Arithmetic;

            var stems = new[] { 6.0, 8.0, 8.0, 12.0, 18.0, 18.0 };
            var value = FieldCodeParser.Average(stems, method);

            int places;
            if (!int.TryParse((_decimals.Text ?? "0").Trim(), out places) || places < 0 || places > 6)
                places = 0;

            _worked.Text = string.Format(CultureInfo.InvariantCulture,
                "A cluster coded 6 8 8 12 18 18 reports as {0} inches.",
                value.ToString("F" + places, CultureInfo.InvariantCulture));
        }

        public override void LoadFrom(FtfSettings s)
        {
            _decimals.Text = s.Trees.TrunkDecimals.ToString(CultureInfo.InvariantCulture);
            _average.SelectedItem = s.Trees.MultiStemAverage;
            _unify.Checked = s.Drip.UnifyByDefault;
            ComboHelp.SelectOrAdd(_dripLinetype, s.Drip.Linetype, true);
            Refresh();
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            var places = s.Trees.TrunkDecimals;
            ReadInt(_decimals, "Trees: decimal places", v => v >= 0 && v <= 6,
                    "must be between 0 and 6", problems, ref places);
            s.Trees.TrunkDecimals = places;

            if (_average.SelectedItem is StemAverageMethod)
                s.Trees.MultiStemAverage = (StemAverageMethod)_average.SelectedItem;

            s.Drip.UnifyByDefault = _unify.Checked;
            s.Drip.Linetype = _dripLinetype.SelectedIndex <= 0
                ? string.Empty
                : Convert.ToString(_dripLinetype.SelectedItem);
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Trees.RestoreDefaults();
            s.Drip.RestoreDefaults();
            LoadFrom(s);
        }
    }

    // -------------------------------------------------------------------- labels

    internal sealed class LabelsPage : SetupPage
    {
        private ComboBox _textStyle;
        private Label _styleWarning;
        private TextBox _height;
        private TextBox _padding;
        private TextBox _offset;
        private TextBox _ringStep;
        private TextBox _ringCount;
        private CheckBox _mask;
        private ComboBox _maskLayer;
        private CheckBox _arrowhead;
        private TextBox _arrowSize;
        private Label _preview;

        public override string Title { get { return "Point Labels"; } }
        public override string AffectedCommands { get { return "FTFLABELS"; } }

        protected override void BuildBody()
        {
            Heading("Text");
            Note("The text FTF places beside recognised survey points -- tree sizes, " +
                 "control aliases. Line Labels (next section) covers text along " +
                 "linework instead.");

            var styles = new List<object> { "(drawing's current style)" };
            foreach (var name in Context.Drawing.TextStyles) styles.Add(name);
            _textStyle = ComboRow("Text style", styles,
                "Collision boxes are measured from the text this style actually produces.");

            _styleWarning = LiveNote();
            _styleWarning.ForeColor = Color.FromArgb(160, 60, 0);

            _height = TextRow("Text height (plotted)", "e.g. 0.08 in on the sheet.");
            _padding = TextRow("Padding around text (plotted)", null);
            _preview = LiveNote();

            Heading("Where labels go");
            _offset = TextRow("Offset from the point (plotted)", "Gap before the first ring.");
            _ringStep = TextRow("Ring step (plotted)", "Extra offset each time it moves out.");
            _ringCount = TextRow("Rings to try",
                "Positions are tried NE, E, SE, NW, W, SW, N, S on each ring.");
            Note("A label that cannot fit on the first ring moves outward and gets a leader.");

            Heading("Masking and leaders");
            _mask = CheckRow("Draw a mask under each label",
                "Hides contours and canopies under the text.");

            var layers = new List<object> { "(same as the label's layer)" };
            foreach (var name in Context.Drawing.Layers) layers.Add(name);
            _maskLayer = ComboRow("Mask layer", layers, null);

            _arrowhead = CheckRow("Leaders have an arrowhead", null);
            _arrowSize = TextRow("Arrow size (plotted)", null);

            _textStyle.SelectedIndexChanged += (s, e) => Refresh();
            _height.TextChanged += (s, e) => Refresh();
            _offset.TextChanged += (s, e) => Refresh();
            _mask.CheckedChanged += (s, e) => _maskLayer.Enabled = _mask.Checked;
            _arrowhead.CheckedChanged += (s, e) => _arrowSize.Enabled = _arrowhead.Checked;
        }

        public override void Refresh()
        {
            var scale = Context.Drawing.Scale;

            double height, offset;
            var haveHeight = double.TryParse((_height.Text ?? "").Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out height);
            var haveOffset = double.TryParse((_offset.Text ?? "").Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out offset);

            _preview.Text = string.Format(CultureInfo.InvariantCulture,
                "At {0}: text {1}, offset {2} drawing units.",
                Context.Drawing.AnnotationScaleName,
                haveHeight ? (height * scale).ToString("0.###", CultureInfo.InvariantCulture) : "?",
                haveOffset ? (offset * scale).ToString("0.###", CultureInfo.InvariantCulture) : "?");

            var style = SelectedStyle();
            _styleWarning.Text =
                !string.IsNullOrEmpty(style) && Context.Drawing.FixedHeightTextStyles.Contains(style)
                    ? "This style has a fixed height, so AutoCAD ignores the height below."
                    : string.Empty;
        }

        private string SelectedStyle()
        {
            if (_textStyle.SelectedIndex <= 0) return string.Empty;
            return Convert.ToString(_textStyle.SelectedItem);
        }

        public override void LoadFrom(FtfSettings s)
        {
            ComboHelp.SelectOrAdd(_textStyle, s.Labels.TextStyle, true);
            _height.Text = Fmt(s.Labels.TextHeightPlotted);
            _padding.Text = Fmt(s.Labels.PaddingPlotted);
            _offset.Text = Fmt(s.Labels.BaseOffsetPlotted);
            _ringStep.Text = Fmt(s.Labels.RingStepPlotted);
            _ringCount.Text = s.Labels.RingCount.ToString(CultureInfo.InvariantCulture);
            _mask.Checked = s.Labels.DrawMask;
            ComboHelp.SelectOrAdd(_maskLayer, s.Labels.MaskLayer, true);
            _arrowhead.Checked = s.Labels.LeaderArrowhead;
            _arrowSize.Text = Fmt(s.Labels.LeaderArrowSizePlotted);

            _maskLayer.Enabled = _mask.Checked;
            _arrowSize.Enabled = _arrowhead.Checked;
            Refresh();
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            s.Labels.TextStyle = SelectedStyle();

            var height = s.Labels.TextHeightPlotted;
            ReadDouble(_height, "Labels: text height", v => v > 0,
                       "must be greater than zero", problems, ref height);
            s.Labels.TextHeightPlotted = height;

            var padding = s.Labels.PaddingPlotted;
            ReadDouble(_padding, "Labels: padding", v => v >= 0,
                       "cannot be negative", problems, ref padding);
            s.Labels.PaddingPlotted = padding;

            var offset = s.Labels.BaseOffsetPlotted;
            ReadDouble(_offset, "Labels: offset from the point", v => v >= 0,
                       "cannot be negative", problems, ref offset);
            s.Labels.BaseOffsetPlotted = offset;

            var step = s.Labels.RingStepPlotted;
            ReadDouble(_ringStep, "Labels: ring step", v => v > 0,
                       "must be greater than zero", problems, ref step);
            s.Labels.RingStepPlotted = step;

            var rings = s.Labels.RingCount;
            ReadInt(_ringCount, "Labels: rings to try", v => v >= 1,
                    "must be at least 1", problems, ref rings);
            s.Labels.RingCount = rings;

            s.Labels.DrawMask = _mask.Checked;
            s.Labels.MaskLayer = _maskLayer.SelectedIndex <= 0
                ? string.Empty
                : Convert.ToString(_maskLayer.SelectedItem);

            s.Labels.LeaderArrowhead = _arrowhead.Checked;

            var arrow = s.Labels.LeaderArrowSizePlotted;
            ReadDouble(_arrowSize, "Labels: arrow size", v => v > 0,
                       "must be greater than zero", problems, ref arrow);
            s.Labels.LeaderArrowSizePlotted = arrow;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Labels.RestoreDefaults();
            LoadFrom(s);
        }
    }

    // -------------------------------------------------------------- line labels

    internal sealed class LineLabelsPage : SetupPage
    {
        private CheckBox _enabled;
        private ComboBox _textStyle;
        private TextBox _height;
        private ComboBox _defaultLayer;
        private CheckBox _alignToLine;
        private ComboBox _defaultPlacement;
        private TextBox _sideOffset;
        private TextBox _repeatInterval;
        private TextBox _minLength;
        private TextBox _endClearance;
        private CheckBox _mask;

        public override string Title { get { return "Line Labels"; } }
        public override string AffectedCommands { get { return "FTFLINELABELS"; } }

        protected override void BuildBody()
        {
            Heading("Linework labelling");
            Note("The normal way to label a line is the FTFLABELLINE command: click the " +
                 "line, slide the live preview to where the label belongs, click to " +
                 "place. Everything on this page is the standard both that command and " +
                 "the optional automatic pass share -- what the text looks like, where " +
                 "it sits, how far it offsets. Civil 3D owns the linework; FTF only " +
                 "places the annotation.");

            _enabled = CheckRow("Also run the automatic whole-drawing pass (FTFLINELABELS)",
                "Labels every recognised line in one go: one label at the midpoint of " +
                "short features, repeated labels on long ones. Off by default because " +
                "line annotation is usually a judgement call -- FTFLABELLINE works " +
                "either way, and labels placed with it are never touched by this pass.");

            Heading("Text");
            var styles = new List<object> { "(drawing's current style)" };
            foreach (var name in Context.Drawing.TextStyles) styles.Add(name);
            _textStyle = ComboRow("Text style", styles, null);

            _height = TextRow("Text height (plotted)", null);

            var layers = new List<object>();
            foreach (var name in Context.Drawing.Layers) layers.Add(name);
            _defaultLayer = ComboRow("Fallback label layer", layers,
                "Last resort only. FTF first uses the feature rule's own label layer, " +
                "then the office text layer that matches the source layer (V-SURF-CURB-E " +
                "labels onto V-SURF-CURB-TEXT-E when it exists). This layer is used only " +
                "when neither is found.");

            _alignToLine = CheckRow("Align text to the line",
                "Text follows the line direction, flipped whenever it would read " +
                "upside down. Off keeps every label horizontal.");

            _defaultPlacement = ComboRow("Default placement",
                new object[] { "On line", "Left", "Right" },
                "For the automatic pass, when neither the source coding nor the " +
                "feature rule names a side. LEFT/RIGHT shot in the field always wins. " +
                "In FTFLABELLINE your cursor chooses the side instead.");

            _sideOffset = TextRow("Side offset (survey feet)",
                "How far an offset label sits from its line. In FTFLABELLINE this is " +
                "also the placing distance: pull the cursor past half this offset and " +
                "the label goes to that side; keep it closer and it snaps onto the line.");

            _mask = CheckRow("Draw a mask under each label",
                "The mask rotates with the text.");

            Heading("Automatic pass spacing (survey feet)");
            Note("These three only shape the automatic pass. With FTFLABELLINE you " +
                 "decide where every label goes, so none of them apply there.");
            _minLength = TextRow("Minimum feature length",
                "Features shorter than this get no label at all.");
            _repeatInterval = TextRow("Repeat interval",
                "Roughly one label per this distance on long runs. A 500 ft curb at " +
                "200 ft spacing gets 2 evenly spaced labels.");
            _endClearance = TextRow("End clearance",
                "Labels keep this far from the feature's ends.");
        }

        public override void LoadFrom(FtfSettings s)
        {
            _enabled.Checked = s.LineLabels.Enabled;
            ComboHelp.SelectOrAdd(_textStyle, s.LineLabels.TextStyle, true);
            _height.Text = Fmt(s.LineLabels.TextHeightPlotted);
            ComboHelp.SelectOrAdd(_defaultLayer, s.LineLabels.DefaultLabelLayer, false);
            _alignToLine.Checked = s.LineLabels.AlignToLine;
            ComboHelp.SelectOrAdd(_defaultPlacement,
                s.LineLabels.DefaultPlacement == "OnLine" ? "On line" : s.LineLabels.DefaultPlacement,
                false);
            _sideOffset.Text = Fmt(s.LineLabels.SideOffsetFeet);
            _mask.Checked = s.LineLabels.DrawMask;
            _minLength.Text = Fmt(s.LineLabels.MinLengthFeet);
            _repeatInterval.Text = Fmt(s.LineLabels.RepeatIntervalFeet);
            _endClearance.Text = Fmt(s.LineLabels.EndClearanceFeet);
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            s.LineLabels.Enabled = _enabled.Checked;
            s.LineLabels.TextStyle = _textStyle.SelectedIndex <= 0
                ? string.Empty : Convert.ToString(_textStyle.SelectedItem);

            var height = s.LineLabels.TextHeightPlotted;
            ReadDouble(_height, "Line labels: text height", v => v > 0,
                       "must be greater than zero", problems, ref height);
            s.LineLabels.TextHeightPlotted = height;

            s.LineLabels.DefaultLabelLayer =
                Convert.ToString(_defaultLayer.SelectedItem) ?? string.Empty;
            s.LineLabels.AlignToLine = _alignToLine.Checked;
            s.LineLabels.DefaultPlacement =
                Convert.ToString(_defaultPlacement.SelectedItem) == "On line"
                    ? "OnLine" : Convert.ToString(_defaultPlacement.SelectedItem);
            var sideOffset = s.LineLabels.SideOffsetFeet;
            ReadDouble(_sideOffset, "Line labels: side offset", v => v >= 0,
                       "cannot be negative", problems, ref sideOffset);
            s.LineLabels.SideOffsetFeet = sideOffset;
            s.LineLabels.DrawMask = _mask.Checked;

            var minLength = s.LineLabels.MinLengthFeet;
            ReadDouble(_minLength, "Line labels: minimum feature length", v => v >= 0,
                       "cannot be negative", problems, ref minLength);
            s.LineLabels.MinLengthFeet = minLength;

            var interval = s.LineLabels.RepeatIntervalFeet;
            ReadDouble(_repeatInterval, "Line labels: repeat interval", v => v > 0,
                       "must be greater than zero", problems, ref interval);
            s.LineLabels.RepeatIntervalFeet = interval;

            var clearance = s.LineLabels.EndClearanceFeet;
            ReadDouble(_endClearance, "Line labels: end clearance", v => v >= 0,
                       "cannot be negative", problems, ref clearance);
            s.LineLabels.EndClearanceFeet = clearance;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.LineLabels.RestoreDefaults();
            LoadFrom(s);
        }
    }

    // ------------------------------------------------------------ tags and table

    internal sealed class TagsPage : SetupPage
    {
        private ComboBox _tagLayer;
        private ComboBox _tagStyle;
        private TextBox _tagHeight;
        private TextBox _tagOffset;
        private TextBox _startNumber;
        private CheckBox _tagLeader;

        public override string Title { get { return "Tags"; } }
        public override string AffectedCommands { get { return "FTFTAGS"; } }

        protected override void BuildBody()
        {
            Heading("Tags on the plan");
            Note("Small numbered markers (T1, P2, S3) placed beside features that are " +
                 "listed in the schedule, so the plan and the table cross-reference.");

            var layers = new List<object> { "(the label's layer)" };
            foreach (var name in Context.Drawing.Layers) layers.Add(name);
            _tagLayer = ComboRow("Tag layer", layers, null);

            var styles = new List<object> { "(drawing's current style)" };
            foreach (var name in Context.Drawing.TextStyles) styles.Add(name);
            _tagStyle = ComboRow("Tag text style", styles, null);

            _tagHeight = TextRow("Tag text height (plotted)", null);
            _tagOffset = TextRow("Offset from the point (plotted)", null);
            _startNumber = TextRow("Start numbering at",
                "Only used for a prefix that has no tags yet.");

            _tagLeader = CheckRow("Draw a leader when a tag cannot sit next to its point",
                "Without this, a tag pushed clear of other work has nothing tying it back.");

            Note("Tag numbers are stored on the tags themselves. A re-run reissues the same " +
                 "number to the same point, and a deleted point leaves a gap rather than " +
                 "renumbering everything after it, because a tag on an issued plan must " +
                 "never move to a different feature.");
        }

        public override void LoadFrom(FtfSettings s)
        {
            ComboHelp.SelectOrAdd(_tagLayer, s.Tags.TagLayer, true);
            ComboHelp.SelectOrAdd(_tagStyle, s.Tags.TagTextStyle, true);
            _tagHeight.Text = Fmt(s.Tags.TagTextHeightPlotted);
            _tagOffset.Text = Fmt(s.Tags.TagOffsetPlotted);
            _startNumber.Text = s.Tags.StartNumber.ToString(CultureInfo.InvariantCulture);
            _tagLeader.Checked = s.Tags.TagLeader;
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            s.Tags.TagLayer = _tagLayer.SelectedIndex <= 0
                ? string.Empty : Convert.ToString(_tagLayer.SelectedItem);
            s.Tags.TagTextStyle = _tagStyle.SelectedIndex <= 0
                ? string.Empty : Convert.ToString(_tagStyle.SelectedItem);

            var height = s.Tags.TagTextHeightPlotted;
            ReadDouble(_tagHeight, "Tags: tag text height", v => v > 0,
                       "must be greater than zero", problems, ref height);
            s.Tags.TagTextHeightPlotted = height;

            var offset = s.Tags.TagOffsetPlotted;
            ReadDouble(_tagOffset, "Tags: offset from the point", v => v >= 0,
                       "cannot be negative", problems, ref offset);
            s.Tags.TagOffsetPlotted = offset;

            var start = s.Tags.StartNumber;
            ReadInt(_startNumber, "Tags: start number", v => v >= 0,
                    "cannot be negative", problems, ref start);
            s.Tags.StartNumber = start;

            s.Tags.TagLeader = _tagLeader.Checked;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            // Tag fields only. Tags and the schedule share one settings section, and
            // resetting "Tags" must not silently reset someone's table columns too.
            var fresh = new FieldCodes.Settings.TagSettings();
            s.Tags.TagLayer = fresh.TagLayer;
            s.Tags.TagTextStyle = fresh.TagTextStyle;
            s.Tags.TagTextHeightPlotted = fresh.TagTextHeightPlotted;
            s.Tags.TagOffsetPlotted = fresh.TagOffsetPlotted;
            s.Tags.StartNumber = fresh.StartNumber;
            s.Tags.TagLeader = fresh.TagLeader;
            LoadFrom(s);
        }
    }

    // ------------------------------------------------------------------- tables

    internal sealed class TablesPage : SetupPage
    {
        private TextBox _tableTitle;
        private ComboBox _tableLayer;
        private TextBox _tableHeight;
        private CheckedListBox _columns;

        public override string Title { get { return "Schedule"; } }
        public override string AffectedCommands { get { return "FTFTABLE"; } }

        protected override void BuildBody()
        {
            Heading("Schedule");
            Note("The table on the plan that lists every tagged feature -- the tree " +
                 "schedule. Each row is one tag; the columns below decide what it says.");
            _tableTitle = TextRow("Title", null, 240);

            var tableLayers = new List<object>();
            foreach (var name in Context.Drawing.Layers) tableLayers.Add(name);
            _tableLayer = ComboRow("Table layer", tableLayers, null);

            _tableHeight = TextRow("Table text height (plotted)", null);

            Note("Columns, in order. Tick the ones you want; use Up and Down to reorder.");

            _columns = new CheckedListBox
            {
                Width = 260,
                Height = 150,
                CheckOnClick = true,
                IntegralHeight = false
            };

            var up = new Button { Text = "Up" };
            var down = new Button { Text = "Down" };
            up.Click += (s, e) => Move(-1);
            down.Click += (s, e) => Move(1);

            AddWithSideButtons(_columns, SideButtons(up, down));

            Note("The schedule reappears where it already is on a re-run, so a table someone " +
                 "positioned on the sheet does not jump.");
        }

        private void Move(int delta)
        {
            var index = _columns.SelectedIndex;
            if (index < 0) return;

            var target = index + delta;
            if (target < 0 || target >= _columns.Items.Count) return;

            var item = _columns.Items[index];
            var was = _columns.GetItemChecked(index);

            _columns.Items.RemoveAt(index);
            _columns.Items.Insert(target, item);
            _columns.SetItemChecked(target, was);
            _columns.SelectedIndex = target;
        }

        public override void LoadFrom(FtfSettings s)
        {
            _tableTitle.Text = s.Tags.TableTitle ?? string.Empty;
            ComboHelp.SelectOrAdd(_tableLayer, s.Tags.TableLayer, false);
            _tableHeight.Text = Fmt(s.Tags.TableTextHeightPlotted);

            var chosen = s.Tags.TableColumns ?? new List<string>();
            var all = Enum.GetNames(typeof(FieldCodes.Tagging.TagTableColumn)).ToList();

            _columns.Items.Clear();
            foreach (var name in chosen)
                if (all.Contains(name, StringComparer.OrdinalIgnoreCase)) _columns.Items.Add(name);
            foreach (var name in all)
                if (!chosen.Contains(name, StringComparer.OrdinalIgnoreCase)) _columns.Items.Add(name);

            for (var i = 0; i < _columns.Items.Count; i++)
                _columns.SetItemChecked(i, i < chosen.Count);
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            s.Tags.TableTitle = (_tableTitle.Text ?? string.Empty).Trim();
            s.Tags.TableLayer = Convert.ToString(_tableLayer.SelectedItem) ?? string.Empty;

            var tableHeight = s.Tags.TableTextHeightPlotted;
            ReadDouble(_tableHeight, "Tables: table text height", v => v > 0,
                       "must be greater than zero", problems, ref tableHeight);
            s.Tags.TableTextHeightPlotted = tableHeight;

            var columns = new List<string>();
            for (var i = 0; i < _columns.Items.Count; i++)
                if (_columns.GetItemChecked(i)) columns.Add(Convert.ToString(_columns.Items[i]));

            if (columns.Count == 0)
                problems.Add("Tables: the schedule needs at least one column.");
            else
                s.Tags.TableColumns = columns;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            // Schedule fields only -- see the note in TagsPage.RestoreDefaults.
            var fresh = new FieldCodes.Settings.TagSettings();
            s.Tags.TableTitle = fresh.TableTitle;
            s.Tags.TableLayer = fresh.TableLayer;
            s.Tags.TableTextHeightPlotted = fresh.TableTextHeightPlotted;
            s.Tags.TableColumns = fresh.TableColumns;
            LoadFrom(s);
        }
    }

    // ---------------------------------------------------------------- draw order

    internal sealed class DrawOrderPage : SetupPage
    {
        private ListBox _protected;
        private ListBox _maskable;
        private ComboBox _layerPicker;

        public override string Title { get { return "Draw Order"; } }
        public override string AffectedCommands { get { return "FTFORDER, FTFLABELS"; } }

        protected override void BuildBody()
        {
            Heading("Bands, bottom to top");
            Note("Maskable linework, then masks, then symbols, then labels. Entities are " +
                 "sorted by layer; modifier suffixes such as -DEAD are ignored when " +
                 "matching, so a dead tree bands the same as a live one.");

            var items = new List<object>();
            foreach (var name in Context.Drawing.Layers) items.Add(name);
            _layerPicker = ComboRow("Layer from this drawing", items,
                "Pick a layer, then Add on either list. Wildcards such as V-UTIL-* can be typed.");

            Heading("Protected - never covered by a label");
            _protected = MakeList();
            AddWithSideButtons(_protected, ListButtons(_protected));

            Heading("Maskable - a label may sit on these");
            _maskable = MakeList();
            AddWithSideButtons(_maskable, ListButtons(_maskable));
        }

        private static ListBox MakeList()
        {
            return new ListBox
            {
                Width = 260,
                Height = 110,
                IntegralHeight = false,
                SelectionMode = SelectionMode.One
            };
        }

        private FlowLayoutPanel ListButtons(ListBox list)
        {
            var add = new Button { Text = "Add" };
            var typed = new Button { Text = "Type..." };
            var remove = new Button { Text = "Remove" };

            add.Click += (s, e) =>
            {
                if (_layerPicker.SelectedItem == null) return;
                AddUnique(list, Convert.ToString(_layerPicker.SelectedItem));
            };

            typed.Click += (s, e) =>
            {
                var value = Prompt.Show("Layer name or wildcard pattern", "Add layer");
                if (!string.IsNullOrWhiteSpace(value)) AddUnique(list, value.Trim());
            };

            remove.Click += (s, e) =>
            {
                if (list.SelectedIndex >= 0) list.Items.RemoveAt(list.SelectedIndex);
            };

            return SideButtons(add, typed, remove);
        }

        private static void AddUnique(ListBox list, string value)
        {
            foreach (var existing in list.Items)
                if (string.Equals(Convert.ToString(existing), value,
                                  StringComparison.OrdinalIgnoreCase)) return;
            list.Items.Add(value);
        }

        public override void LoadFrom(FtfSettings s)
        {
            _protected.Items.Clear();
            foreach (var l in s.DrawOrder.ProtectedLayers ?? new List<string>())
                _protected.Items.Add(l);

            _maskable.Items.Clear();
            foreach (var l in s.DrawOrder.MaskableLayers ?? new List<string>())
                _maskable.Items.Add(l);
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            s.DrawOrder.ProtectedLayers = _protected.Items.Cast<object>()
                .Select(o => Convert.ToString(o)).ToList();
            s.DrawOrder.MaskableLayers = _maskable.Items.Cast<object>()
                .Select(o => Convert.ToString(o)).ToList();
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.DrawOrder.RestoreDefaults();
            LoadFrom(s);
        }
    }

    // ------------------------------------------------------- cleanup and re-run

    internal sealed class CleanupPage : SetupPage
    {
        private TextBox _movedTolerance;

        public override string Title { get { return "Cleanup & Re-run"; } }
        public override string AffectedCommands { get { return "FTFCLEAN, and every re-run"; } }

        protected override void BuildBody()
        {
            Heading("Hand-placed labels");
            _movedTolerance = TextRow("Moved tolerance (drawing units)",
                "A label further than this from where the program put it counts as hand-placed.");
            Note("A hand-placed label is left exactly where it is on the next run, and the " +
                 "automatic labels route around it.");

            Heading("What each command removes before it redraws");
            ReadOnlyRow("FTFPOINTS", "blocks it placed");
            ReadOnlyRow("FTFDRIP", "drip circles and arcs it drew");
            ReadOnlyRow("FTFLABELS", "labels, masks and leaders, except hand-placed ones");
            ReadOnlyRow("FTFLINELABELS", "the line labels it placed automatically -- " +
                "labels placed by hand with FTFLABELLINE are always kept");
            ReadOnlyRow("FTFTAGS", "tags it drew, reissuing the same numbers");
            ReadOnlyRow("FTFTABLE", "the schedule, keeping its position");
            ReadOnlyRow("FTFCLEAN", "everything FTF owns, including FTFLABELLINE labels " +
                "-- drafted survey lines (FTFDRAWLINE) are deliberately left to FTFDRAFTCLEAN");
            Note("Ownership is recorded in XData on every entity the plugin creates. Nothing " +
                 "is ever removed by layer or block name, so hand-drawn work is safe.");
        }

        public override void LoadFrom(FtfSettings s)
        {
            _movedTolerance.Text = Fmt(s.Cleanup.MovedTolerance);
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            var tolerance = s.Cleanup.MovedTolerance;
            ReadDouble(_movedTolerance, "Cleanup: moved tolerance", v => v >= 0,
                       "cannot be negative", problems, ref tolerance);
            s.Cleanup.MovedTolerance = tolerance;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Cleanup.RestoreDefaults();
            LoadFrom(s);
        }
    }

    // ---------------------------------------------------------------- spot shots

    internal sealed class SpotsPage : SetupPage
    {
        private TextBox _height;
        private TextBox _angle;
        private TextBox _decimals;
        private TextBox _marker;
        private ComboBox _markerLayer;
        private ComboBox _textLayer;

        public override string Title { get { return "Spot Shots"; } }
        public override string AffectedCommands { get { return "FTFSPOT"; } }

        protected override void BuildBody()
        {
            Heading("Spot elevations");
            Note("FTFSPOT: click a point, get an X there and the surface elevation " +
                 "beside it. Spots placed close together automatically dodge each " +
                 "other, so ramps can carry as many as the reviewer wants.");

            _height = TextRow("Text height (plotted)", "0.06 is Leroy 60.");
            _angle = TextRow("Text angle (degrees)", "The office standard slant.");
            _decimals = TextRow("Elevation decimals", "246.31 at 2.");
            _marker = TextRow("X marker size (plotted)", null);

            var layers = new List<object>();
            foreach (var name in Context.Drawing.Layers) layers.Add(name);
            _markerLayer = ComboRow("Marker layer", layers, null);
            _textLayer = ComboRow("Text layer", layers, null);
        }

        public override void LoadFrom(FtfSettings s)
        {
            _height.Text = Fmt(s.Spots.TextPlotted);
            _angle.Text = Fmt(s.Spots.AngleDegrees);
            _decimals.Text = s.Spots.Decimals.ToString(CultureInfo.InvariantCulture);
            _marker.Text = Fmt(s.Spots.MarkerPlotted);
            ComboHelp.SelectOrAdd(_markerLayer, s.Spots.MarkerLayer, false);
            ComboHelp.SelectOrAdd(_textLayer, s.Spots.TextLayer, false);
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            var height = s.Spots.TextPlotted;
            ReadDouble(_height, "Spot shots: text height", v => v > 0,
                       "must be greater than zero", problems, ref height);
            s.Spots.TextPlotted = height;

            var angle = s.Spots.AngleDegrees;
            ReadDouble(_angle, "Spot shots: text angle", v => v > -360 && v < 360,
                       "must be a sensible angle", problems, ref angle);
            s.Spots.AngleDegrees = angle;

            var decimals = s.Spots.Decimals;
            ReadInt(_decimals, "Spot shots: decimals", v => v >= 0 && v <= 4,
                    "must be between 0 and 4", problems, ref decimals);
            s.Spots.Decimals = decimals;

            var marker = s.Spots.MarkerPlotted;
            ReadDouble(_marker, "Spot shots: marker size", v => v > 0,
                       "must be greater than zero", problems, ref marker);
            s.Spots.MarkerPlotted = marker;

            s.Spots.MarkerLayer = Convert.ToString(_markerLayer.SelectedItem) ?? string.Empty;
            s.Spots.TextLayer = Convert.ToString(_textLayer.SelectedItem) ?? string.Empty;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Spots.RestoreDefaults();
            LoadFrom(s);
        }
    }

    // -------------------------------------------------------------------- sheets

    internal sealed class SheetsPage : SetupPage
    {
        private TextBox _paperW;
        private TextBox _paperH;
        private TextBox _margin;
        private TextBox _scale;
        private TextBox _overlap;
        private ComboBox _windowLayer;
        private CheckBox _windowLabels;
        private ComboBox _matchLayer;
        private TextBox _matchFormat;
        private TextBox _matchHeight;
        private TextBox _keymapWidth;
        private Label _worked;

        public override string Title { get { return "Sheets"; } }
        public override string AffectedCommands { get { return "FTFSHEETPLAN, FTFSHEETS"; } }

        protected override void BuildBody()
        {
            Heading("Sheet planning");
            Note("FTFSHEETPLAN lays this paper over the site for the fewest prints " +
                 "and draws the proposed windows and match lines in model space. " +
                 "FTFSHEETS draws the same from the layouts' real viewports instead.");

            _paperW = TextRow("Paper width (inches)", "17 x 11 is ledger.");
            _paperH = TextRow("Paper height (inches)", null);
            _margin = TextRow("Margin (inches)", "Inside the paper edge, all four sides.");
            _scale = TextRow("Plot scale (feet per inch)", "20 means 1\" = 20'.");
            _overlap = TextRow("Sheet overlap (percent)",
                "Neighbouring sheets share this much, which is where the match " +
                "line sits.");
            _worked = LiveNote();

            Heading("What gets drawn");
            var layers = new List<object>();
            foreach (var name in Context.Drawing.Layers) layers.Add(name);
            _windowLayer = ComboRow("Window layer", layers,
                "Layer for the plot-window rectangles in model space.");
            _windowLabels = CheckRow("Write each sheet's name in its corner", null);
            _matchLayer = ComboRow("Match line layer", layers, null);
            _matchFormat = TextRow("Match line wording",
                "{sheet} is the neighbouring sheet's name.", 300);
            _matchHeight = TextRow("Match line text height (plotted)", null);
            _keymapWidth = TextRow("Key map width (inches)",
                "The index diagram FTFKEYMAP puts on each sheet.");

            _paperW.TextChanged += (s, e) => Refresh();
            _paperH.TextChanged += (s, e) => Refresh();
            _margin.TextChanged += (s, e) => Refresh();
            _scale.TextChanged += (s, e) => Refresh();
        }

        public override void Refresh()
        {
            double w, h, m, sc;
            if (double.TryParse(_paperW.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out w) &&
                double.TryParse(_paperH.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out h) &&
                double.TryParse(_margin.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out m) &&
                double.TryParse(_scale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out sc) &&
                w > 2 * m && h > 2 * m && sc > 0)
            {
                _worked.Text = string.Format(CultureInfo.InvariantCulture,
                    "One sheet prints {0:0.#} x {1:0.#} survey feet.",
                    (w - 2 * m) * sc, (h - 2 * m) * sc);
            }
            else
            {
                _worked.Text = string.Empty;
            }
        }

        public override void LoadFrom(FtfSettings s)
        {
            _paperW.Text = Fmt(s.Sheets.SheetWidthIn);
            _paperH.Text = Fmt(s.Sheets.SheetHeightIn);
            _margin.Text = Fmt(s.Sheets.MarginIn);
            _scale.Text = Fmt(s.Sheets.PlotScaleFeetPerInch);
            _overlap.Text = Fmt(s.Sheets.OverlapPercent);
            ComboHelp.SelectOrAdd(_windowLayer, s.Sheets.WindowLayer, false);
            _windowLabels.Checked = s.Sheets.DrawWindowLabels;
            ComboHelp.SelectOrAdd(_matchLayer, s.Sheets.MatchlineLayer, false);
            _matchFormat.Text = s.Sheets.MatchlineLabelFormat ?? string.Empty;
            _matchHeight.Text = Fmt(s.Sheets.MatchlineTextPlotted);
            _keymapWidth.Text = Fmt(s.Sheets.KeymapWidthIn);
            Refresh();
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            var w = s.Sheets.SheetWidthIn;
            ReadDouble(_paperW, "Sheets: paper width", v => v > 0,
                       "must be greater than zero", problems, ref w);
            s.Sheets.SheetWidthIn = w;

            var h = s.Sheets.SheetHeightIn;
            ReadDouble(_paperH, "Sheets: paper height", v => v > 0,
                       "must be greater than zero", problems, ref h);
            s.Sheets.SheetHeightIn = h;

            var m = s.Sheets.MarginIn;
            ReadDouble(_margin, "Sheets: margin", v => v >= 0,
                       "cannot be negative", problems, ref m);
            s.Sheets.MarginIn = m;

            var sc = s.Sheets.PlotScaleFeetPerInch;
            ReadDouble(_scale, "Sheets: plot scale", v => v > 0,
                       "must be greater than zero", problems, ref sc);
            s.Sheets.PlotScaleFeetPerInch = sc;

            var ov = s.Sheets.OverlapPercent;
            ReadDouble(_overlap, "Sheets: overlap", v => v >= 0 && v <= 45,
                       "must be between 0 and 45 percent", problems, ref ov);
            s.Sheets.OverlapPercent = ov;

            s.Sheets.WindowLayer = Convert.ToString(_windowLayer.SelectedItem) ?? string.Empty;
            s.Sheets.DrawWindowLabels = _windowLabels.Checked;
            s.Sheets.MatchlineLayer = Convert.ToString(_matchLayer.SelectedItem) ?? string.Empty;
            s.Sheets.MatchlineLabelFormat = (_matchFormat.Text ?? string.Empty).Trim();

            var th = s.Sheets.MatchlineTextPlotted;
            ReadDouble(_matchHeight, "Sheets: match line text height", v => v > 0,
                       "must be greater than zero", problems, ref th);
            s.Sheets.MatchlineTextPlotted = th;

            var kw = s.Sheets.KeymapWidthIn;
            ReadDouble(_keymapWidth, "Sheets: key map width", v => v > 0,
                       "must be greater than zero", problems, ref kw);
            s.Sheets.KeymapWidthIn = kw;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Sheets.RestoreDefaults();
            LoadFrom(s);
        }
    }

    // --------------------------------------------------------------- field codes

    internal sealed class FieldCodesPage : SetupPage
    {
        private TextBox _testInput;
        private Label _testResult;

        public override string Title { get { return "Field Codes"; } }
        public override string AffectedCommands { get { return "Read-only for now"; } }
        public override bool IsReadOnly { get { return true; } }

        protected override void BuildBody()
        {
            var rules = Context.Rules;

            Heading("Rules file");
            ReadOnlyRow("Location", Context.RulesPath ?? "(not found)");

            if (rules == null)
            {
                Note("The rules file did not load: " + (Context.RulesError ?? "unknown error"));
                return;
            }

            ReadOnlyRow("Version", rules.Version ?? "(none)");
            ReadOnlyRow("Code rules", string.Join(", ", rules.Codes.Select(c => c.Id).ToArray()));
            ReadOnlyRow("Linework codes", rules.LineworkCodes.Count +
                        " figure prefixes, handled as linework");
            ReadOnlyRow("Never drawn", string.Join(", ", rules.IgnoreCodes.ToArray()));

            Heading("Species");
            foreach (var code in rules.Codes)
            {
                if (code.Species == null || code.Species.Count == 0) continue;
                foreach (var pair in code.Species) ReadOnlyRow("    " + pair.Key, pair.Value);
            }

            Heading("Layers each rule draws on");
            foreach (var code in rules.Codes)
            {
                if (code.Label != null && !string.IsNullOrEmpty(code.Label.Layer))
                    ReadOnlyRow("    " + code.Id + " label", code.Label.Layer);
                if (code.DripLine != null && !string.IsNullOrEmpty(code.DripLine.Layer))
                    ReadOnlyRow("    " + code.Id + " drip line", code.DripLine.Layer);
                if (!string.IsNullOrEmpty(code.BlockLayer))
                    ReadOnlyRow("    " + code.Id + " block", code.BlockLayer);
            }

            Heading("Modifiers, in the order they apply");
            foreach (var mod in rules.Modifiers.OrderBy(m => m.Priority))
                ReadOnlyRow("    " + mod.Id, "priority " + mod.Priority +
                            (mod.Flag ? "  (flagged for review)" : string.Empty));

            Heading("Try a description");
            _testInput = TextRow("Description", "Type a field description to see how it parses.",
                                 260);
            _testResult = LiveNote();
            _testInput.TextChanged += (s, e) => RunTest();

            Note("These come from the rules file and are not editable here yet. Editing " +
                 "codes from this window is the next thing being built.");
        }

        private void RunTest()
        {
            var rules = Context.Rules;
            if (rules == null) return;

            var text = (_testInput.Text ?? string.Empty).Trim();
            if (text.Length == 0) { _testResult.Text = string.Empty; return; }

            try
            {
                var parsed = new FieldCodeParser(rules).Parse("test", text);

                if (parsed.Ignored)
                    _testResult.Text = "Linework or never-drawn - nothing is placed for this.";
                else if (parsed.NoContent)
                    _testResult.Text = "Bare code with no data - nothing to draw.";
                else if (parsed.Unhandled)
                    _testResult.Text = "No rule configured. It would appear in the unhandled report.";
                else if (parsed.HasErrors)
                    _testResult.Text = "ERROR: " + parsed.Diagnostics
                        .First(d => d.Severity == Severity.Error).Message;
                else
                    _testResult.Text = string.Format("OK - label \"{0}\"{1}",
                        parsed.LabelText,
                        parsed.DripRadius.HasValue
                            ? ", drip radius " + Fmt(parsed.DripRadius.Value)
                            : string.Empty);
            }
            catch (System.Exception ex)
            {
                _testResult.Text = ex.Message;
            }
        }

        public override void LoadFrom(FtfSettings s) { }
        public override void SaveTo(FtfSettings s, ICollection<string> problems) { }
        public override void RestoreDefaults(FtfSettings s) { }
    }

    // -------------------------------------------------------------------- shared

    internal static class ComboHelp
    {
        /// <summary>
        /// Selects a value, keeping it in the list even when this drawing does not have
        /// it -- silently switching a configured layer to something else would be worse
        /// than showing a name that is missing here.
        /// </summary>
        public static void SelectOrAdd(ComboBox combo, string value, bool hasBlankFirst)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (combo.Items.Count > 0) combo.SelectedIndex = 0;
                return;
            }

            var start = hasBlankFirst ? 1 : 0;
            for (var i = start; i < combo.Items.Count; i++)
            {
                if (string.Equals(Convert.ToString(combo.Items[i]), value,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            combo.Items.Add(value);
            combo.SelectedIndex = combo.Items.Count - 1;
        }
    }

    /// <summary>Minimal text prompt; WinForms has no built-in one.</summary>
    internal static class Prompt
    {
        public static string Show(string caption, string title)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(340, 110);

                var label = new Label { Text = caption, AutoSize = true, Location = new Point(12, 12) };
                var input = new TextBox { Bounds = new Rectangle(12, 34, 316, 22) };
                var ok = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Bounds = new Rectangle(168, 70, 76, 26)
                };
                var cancel = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Bounds = new Rectangle(252, 70, 76, 26)
                };

                form.Controls.Add(label);
                form.Controls.Add(input);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                return form.ShowDialog() == DialogResult.OK ? input.Text : null;
            }
        }
    }
}
