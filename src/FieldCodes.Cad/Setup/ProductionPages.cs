using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Settings;
using FieldCodes.Utilities;

namespace FieldCodes.Cad.Setup
{
    // ------------------------------------------------------------ storm & sewer dips

    internal sealed class DipsPage : SetupPage
    {
        private TextBox _codes;
        private DataGridView _systems;
        private TextBox _materials;
        private ComboBox _reference;
        private TextBox _cone;
        private TextBox _distance;
        private TextBox _doubleLine;
        private CheckBox _centerline;
        private TextBox _existingTolerance;
        private TextBox _crossing;
        private TextBox _elevDecimals;
        private TextBox _slopeDecimals;
        private TextBox _pipeLabel;
        private TextBox _header;
        private TextBox _rim;
        private TextBox _pipeLine;
        private TextBox _undipped;
        private TextBox _bottom;
        private TextBox _water;
        private TextBox _prefixInvert;
        private TextBox _prefixTop;
        private TextBox _prefixSpring;
        private TextBox _textHeight;
        private TextBox _labelOffset;
        private DataGridView _thresholds;
        private CheckedListBox _checks;
        private CheckBox _convention;
        private TextBox _conventionSource;
        private TextBox _prefixUnconfirmed;

        /// <summary>Checks a drafter may switch off, in plain words. Checks that stop
        /// invalid geometry from being drawn are not listed.</summary>
        private static readonly KeyValuePair<QcCode, string>[] CheckNames =
        {
            new KeyValuePair<QcCode, string>(QcCode.SlopeOutOfRange, "Slope outside the warning range"),
            new KeyValuePair<QcCode, string>(QcCode.InsufficientCover, "Cover under the warning threshold"),
            new KeyValuePair<QcCode, string>(QcCode.ReverseFlow, "IN/OUT disagrees with the calculated fall"),
            new KeyValuePair<QcCode, string>(QcCode.InvertAboveRim, "Measured elevation above the rim"),
            new KeyValuePair<QcCode, string>(QcCode.InvertBelowBottom, "Invert below the structure bottom"),
            new KeyValuePair<QcCode, string>(QcCode.PipeTooLargeForStructure, "Pipe wider than a KNOWN structure inside size"),
            new KeyValuePair<QcCode, string>(QcCode.SizeMismatch, "Size differs at the two ends"),
            new KeyValuePair<QcCode, string>(QcCode.MaterialMismatch, "Material differs at the two ends"),
            new KeyValuePair<QcCode, string>(QcCode.DirectionMismatch, "Observed directions do not line up"),
            new KeyValuePair<QcCode, string>(QcCode.MissingOppositePipe, "No dip observed at the other end"),
            new KeyValuePair<QcCode, string>(QcCode.DuplicatePipe, "Possible duplicate pipe entry"),
            new KeyValuePair<QcCode, string>(QcCode.SuspiciousStacking, "Pipes in the same direction overlap vertically"),
            new KeyValuePair<QcCode, string>(QcCode.CrossingClearance, "Storm / sanitary crossing clearance"),
            new KeyValuePair<QcCode, string>(QcCode.UnresolvedConnection, "Connection not resolved"),
            new KeyValuePair<QcCode, string>(QcCode.UnconfirmedReference, "Dip reference not stated / not confirmed"),
            new KeyValuePair<QcCode, string>(QcCode.SubmergedPipe, "Submerged pipe"),
            new KeyValuePair<QcCode, string>(QcCode.SiltedPipe, "Silted pipe"),
            new KeyValuePair<QcCode, string>(QcCode.BlockedPipe, "Blocked pipe"),
            new KeyValuePair<QcCode, string>(QcCode.UnableToDip, "Pipe not dipped"),
            new KeyValuePair<QcCode, string>(QcCode.UnknownDirection, "Direction unknown"),
            new KeyValuePair<QcCode, string>(QcCode.MalformedNote, "Unreadable field note line")
        };

        public override string Title { get { return "Storm & Sewer Dips"; } }
        public override string AffectedCommands { get { return "FTFDIP, FTFDIPCHECK"; } }

        protected override void BuildBody()
        {
            Heading("Structure codes");
            Note("One per line: CODE = WHAT IT IS, System, Round / Rectangular / NoSize[, standard inside width in inches]. Round structures are sized by diameter. " +
                 "System is Storm, Sanitary, Culvert, Water or Other. Leave the inside width off " +
                 "unless the office has a documented standard size for that type -- it is then " +
                 "reported as a PROFILE size, never as field observed. With no known size the " +
                 "\"pipe too large for structure\" check does not run.");
            _codes = AddFullWidth(new TextBox
            {
                Multiline = true, ScrollBars = ScrollBars.Vertical, Width = NoteWidth, Height = 160,
                Font = new Font(FontFamily.GenericMonospace, 9f)
            });

            Heading("Layers for each system");
            Note("FTF default layer names. Before a layer is created the drawing and the layer " +
                 "mappings (Settings > General) are checked; an existing or similar layer is used " +
                 "or offered instead of making a duplicate.");
            _systems = AddFullWidth(new DataGridView
            {
                Width = NoteWidth, Height = 150, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, BackgroundColor = SystemColors.Window,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            });
            _systems.Columns.Add(new DataGridViewTextBoxColumn { Name = "System", HeaderText = "System", ReadOnly = true });
            foreach (var c in new[] { "Abbrev", "Pipe layer", "Label layer", "Structure label layer" })
                _systems.Columns.Add(c, c);

            Heading("QC warning thresholds (not design requirements)");
            Note("These only raise review warnings; they never stop drafting and are not design " +
                 "criteria. Storm and sanitary start with the office's agreed warning values. " +
                 "Culvert, water and other have no thresholds yet, so their checks start off.");
            _thresholds = AddFullWidth(new DataGridView
            {
                Width = NoteWidth, Height = 150, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, BackgroundColor = SystemColors.Window,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            });
            _thresholds.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "System", ReadOnly = true });
            _thresholds.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Warn on slope" });
            _thresholds.Columns.Add("low", "Warn below %");
            _thresholds.Columns.Add("high", "Warn above %");
            _thresholds.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Warn on cover" });
            _thresholds.Columns.Add("cover", "Warn if cover under ft");

            Heading("Which checks run");
            Note("Untick a check to stop it being reported. Checks that stop invalid geometry " +
                 "(no survey point, both ends in one place) always run.");
            _checks = AddFullWidth(new CheckedListBox { Width = NoteWidth, Height = 190, CheckOnClick = true, IntegralHeight = false });
            foreach (var pair in CheckNames) _checks.Items.Add(pair.Value);

            Heading("Unmarked dips");
            Note("By office default a dip is the invert unless the note says TOP or the drafter " +
                 "ticks Top of pipe. Each pipe still records that its reference came from this " +
                 "default. Untick the default to have unmarked dips held as UNSPECIFIED until " +
                 "someone confirms them.");
            _materials = TextRow("Materials the notes may use", "Comma separated. Anything else in a pipe line is kept as a note.", 360);
            _reference = ComboRow("Assumed reference shown", Enum.GetNames(typeof(MeasurementReference))
                    .Where(n => n != MeasurementReference.Unspecified.ToString()).Cast<object>(),
                "Display only. Never used as fact until the drafter confirms it.");
            _prefixUnconfirmed = TextRow("Label word for an unconfirmed dip", "Keeps an unconfirmed elevation visibly different on the drawing, e.g. IE?");
            _convention = CheckRow("Treat a dip as the invert unless it is marked top of pipe (office default)",
                "Recorded on each pipe as coming from this default, so the basis is always visible.");
            _conventionSource = TextRow("Recorded as", "The wording saved on each pipe the default applies to.", 360);

            Heading("Finding connections");
            _cone = TextRow("Search cone (degrees either side)", "How far off the observed direction a structure can be.");
            _distance = TextRow("Search distance (feet)", null);
            _existingTolerance = TextRow("Existing pipe tolerance (feet)", "A drawn line this close to both structures counts as an existing pipe.");
            _crossing = TextRow("Storm / sanitary crossing clearance (feet)", null);

            Heading("Drafting");
            _doubleLine = TextRow("Double line above (inches)", "12 draws 12\" and smaller as one centerline; larger pipes as two lines at their outside width.");
            _centerline = CheckRow("Also draw the centerline between double lines", null);
            _elevDecimals = TextRow("Elevation decimals", null);
            _slopeDecimals = TextRow("Slope decimals", null);
            _textHeight = TextRow("Text height (plotted)", null);
            _labelOffset = TextRow("Pipe label offset (plotted)", null);

            Heading("Label wording");
            Note("Tokens in braces are filled in; a part in [square brackets] disappears when " +
                 "its token has no value -- so a pipe with no calculated slope simply has no \"@ slope\".");
            _pipeLabel = TextRow("Pipe label", "{size} {material} {system} {slope}", 360);
            _header = TextRow("Structure first line", "{code} {number} {type}", 360);
            _rim = TextRow("Rim line", "{rim}", 360);
            _pipeLine = TextRow("Pipe line", "{prefix} {role} {direction} {elevation} {size} {material}", 360);
            _undipped = TextRow("Pipe not dipped", "{direction} {size} {material}", 360);
            _bottom = TextRow("Bottom line", "{bottom}", 360);
            _water = TextRow("Water line", "{water}", 360);
            _prefixInvert = TextRow("Word for invert", null);
            _prefixTop = TextRow("Word for top of pipe", null);
            _prefixSpring = TextRow("Word for springline", null);
        }

        public override void LoadFrom(FtfSettings s)
        {
            var d = s.Dips;
            _codes.Text = string.Join(Environment.NewLine, d.StructureCodes.Select(c =>
                c.Code + " = " + c.Type + ", " + c.System + ", " + c.Shape +
                (c.InsideWidthIn.HasValue ? ", " + Fmt(c.InsideWidthIn.Value) : string.Empty)).ToArray());

            _systems.Rows.Clear();
            _thresholds.Rows.Clear();
            foreach (UtilitySystem system in Enum.GetValues(typeof(UtilitySystem)))
            {
                var st = d.Standard(system);
                _systems.Rows.Add(system.ToString(), st.Abbreviation, st.PipeLayer, st.LabelLayer, st.StructureLabelLayer);
                _thresholds.Rows.Add(system.ToString(), st.SlopeCheckEnabled, Fmt(st.MinSlopePercent), Fmt(st.MaxSlopePercent),
                                     st.CoverCheckEnabled, Fmt(st.MinCoverFt));
            }
            for (var k = 0; k < CheckNames.Length; k++)
                _checks.SetItemChecked(k, !d.DisabledChecks.Contains(CheckNames[k].Key));
            _convention.Checked = d.UnmarkedDipsAreInvertsByConvention;
            _conventionSource.Text = d.UnmarkedDipConventionSource ?? string.Empty;
            _prefixUnconfirmed.Text = d.PrefixUnconfirmed ?? string.Empty;

            _materials.Text = string.Join(", ", d.Materials.ToArray());
            ComboHelp.SelectOrAdd(_reference, d.AssumedPipeReference.ToString(), false);
            _cone.Text = Fmt(d.SearchConeDegrees);
            _distance.Text = Fmt(d.SearchDistanceFt);
            _existingTolerance.Text = Fmt(d.ExistingPipeToleranceFt);
            _crossing.Text = Fmt(d.CrossingClearanceFt);
            _doubleLine.Text = Fmt(d.DoubleLineThresholdIn);
            _centerline.Checked = d.CenterlineWithDoubleLine;
            _elevDecimals.Text = d.ElevationDecimals.ToString(CultureInfo.InvariantCulture);
            _slopeDecimals.Text = d.SlopeDecimals.ToString(CultureInfo.InvariantCulture);
            _textHeight.Text = Fmt(d.TextHeightPlotted);
            _labelOffset.Text = Fmt(d.PipeLabelOffsetPlotted);
            _pipeLabel.Text = d.PipeLabelFormat;
            _header.Text = d.StructureHeaderFormat;
            _rim.Text = d.RimLineFormat;
            _pipeLine.Text = d.PipeLineFormat;
            _undipped.Text = d.UndippedPipeLineFormat;
            _bottom.Text = d.BottomLineFormat;
            _water.Text = d.WaterLineFormat;
            _prefixInvert.Text = d.PrefixInvert;
            _prefixTop.Text = d.PrefixTop;
            _prefixSpring.Text = d.PrefixSpringline;
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            var d = s.Dips;

            var codes = new List<StructureCodeRule>();
            var lineNumber = 0;
            foreach (var raw in (_codes.Text ?? string.Empty).Split('\n'))
            {
                lineNumber++;
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) { problems.Add("Dips: structure code line " + lineNumber + " needs CODE = WHAT IT IS, System."); continue; }
                var parts = line.Substring(eq + 1).Split(',').Select(p => p.Trim()).ToArray();
                UtilitySystem system;
                if (parts.Length < 2 || !Enum.TryParse(parts[1], true, out system))
                { problems.Add("Dips: structure code line " + lineNumber + " needs a system (Storm, Sanitary, Culvert, Water, Other)."); continue; }
                // Optional third part: Round, Rectangular or NoSize. Then an optional standard width.
                var shape = StructureShape.Round;
                var next = 2;
                StructureShape parsedShape;
                if (parts.Length > 2 && Enum.TryParse(parts[2], true, out parsedShape)) { shape = parsedShape; next = 3; }
                double width = 0;
                var hasWidth = parts.Length > next && parts[next].Length > 0;
                if (hasWidth && (!double.TryParse(parts[next], NumberStyles.Float, CultureInfo.InvariantCulture, out width) || width <= 0))
                { problems.Add("Dips: structure code line " + lineNumber + " has an inside width that is not a positive number."); continue; }
                codes.Add(new StructureCodeRule
                {
                    Code = line.Substring(0, eq).Trim().ToUpperInvariant(),
                    Type = parts[0].ToUpperInvariant(),
                    System = system,
                    Shape = shape,
                    InsideWidthIn = hasWidth ? width : (double?)null
                });
            }
            foreach (var dup in codes.GroupBy(c => c.Code).Where(g => g.Count() > 1))
                problems.Add("Dips: structure code " + dup.Key + " is listed more than once.");
            d.StructureCodes = codes;

            var systems = new List<UtilitySystemStandard>();
            _thresholds.EndEdit();
            for (var r = 0; r < _systems.Rows.Count; r++)
            {
                var row = _systems.Rows[r];
                var limits = _thresholds.Rows[r];
                var name = Convert.ToString(row.Cells[0].Value);
                double min, max, cover;
                if (!TryCell(limits, 2, out min) || !TryCell(limits, 3, out max) || !TryCell(limits, 5, out cover))
                { problems.Add("Dips: " + name + " warning thresholds must be numbers (0 when unused)."); continue; }
                systems.Add(new UtilitySystemStandard
                {
                    System = (UtilitySystem)Enum.Parse(typeof(UtilitySystem), name),
                    Abbreviation = Cell(row, 1), PipeLayer = Cell(row, 2), LabelLayer = Cell(row, 3), StructureLabelLayer = Cell(row, 4),
                    SlopeCheckEnabled = Convert.ToBoolean(limits.Cells[1].Value ?? false),
                    CoverCheckEnabled = Convert.ToBoolean(limits.Cells[4].Value ?? false),
                    MinSlopePercent = min, MaxSlopePercent = max, MinCoverFt = cover
                });
            }
            d.Systems = systems;

            d.DisabledChecks = CheckNames.Where((pair, k) => !_checks.GetItemChecked(k)).Select(pair => pair.Key).ToList();
            d.UnmarkedDipsAreInvertsByConvention = _convention.Checked;
            d.UnmarkedDipConventionSource = (_conventionSource.Text ?? string.Empty).Trim();
            d.PrefixUnconfirmed = (_prefixUnconfirmed.Text ?? string.Empty).Trim();

            d.Materials = (_materials.Text ?? string.Empty).Split(',').Select(m => m.Trim().ToUpperInvariant()).Where(m => m.Length > 0).Distinct().ToList();
            d.AssumedPipeReference = (MeasurementReference)Enum.Parse(typeof(MeasurementReference), Convert.ToString(_reference.SelectedItem));

            var v = d.SearchConeDegrees; ReadDouble(_cone, "Dips: search cone", x => x > 0 && x < 90, "must be between 0 and 90", problems, ref v); d.SearchConeDegrees = v;
            v = d.SearchDistanceFt; ReadDouble(_distance, "Dips: search distance", x => x > 0, "must be greater than zero", problems, ref v); d.SearchDistanceFt = v;
            v = d.ExistingPipeToleranceFt; ReadDouble(_existingTolerance, "Dips: existing pipe tolerance", x => x >= 0, "cannot be negative", problems, ref v); d.ExistingPipeToleranceFt = v;
            v = d.CrossingClearanceFt; ReadDouble(_crossing, "Dips: crossing clearance", x => x >= 0, "cannot be negative", problems, ref v); d.CrossingClearanceFt = v;
            v = d.DoubleLineThresholdIn; ReadDouble(_doubleLine, "Dips: double line threshold", x => x >= 0, "cannot be negative", problems, ref v); d.DoubleLineThresholdIn = v;
            d.CenterlineWithDoubleLine = _centerline.Checked;
            var i = d.ElevationDecimals; ReadInt(_elevDecimals, "Dips: elevation decimals", x => x >= 0 && x <= 4, "must be between 0 and 4", problems, ref i); d.ElevationDecimals = i;
            i = d.SlopeDecimals; ReadInt(_slopeDecimals, "Dips: slope decimals", x => x >= 0 && x <= 4, "must be between 0 and 4", problems, ref i); d.SlopeDecimals = i;
            v = d.TextHeightPlotted; ReadDouble(_textHeight, "Dips: text height", x => x > 0, "must be greater than zero", problems, ref v); d.TextHeightPlotted = v;
            v = d.PipeLabelOffsetPlotted; ReadDouble(_labelOffset, "Dips: pipe label offset", x => x >= 0, "cannot be negative", problems, ref v); d.PipeLabelOffsetPlotted = v;

            d.PipeLabelFormat = Required(_pipeLabel, "pipe label", problems, d.PipeLabelFormat);
            d.StructureHeaderFormat = Required(_header, "structure first line", problems, d.StructureHeaderFormat);
            d.RimLineFormat = Required(_rim, "rim line", problems, d.RimLineFormat);
            d.PipeLineFormat = Required(_pipeLine, "pipe line", problems, d.PipeLineFormat);
            d.UndippedPipeLineFormat = Required(_undipped, "pipe not dipped", problems, d.UndippedPipeLineFormat);
            d.BottomLineFormat = Required(_bottom, "bottom line", problems, d.BottomLineFormat);
            d.WaterLineFormat = Required(_water, "water line", problems, d.WaterLineFormat);
            d.PrefixInvert = (_prefixInvert.Text ?? string.Empty).Trim();
            d.PrefixTop = (_prefixTop.Text ?? string.Empty).Trim();
            d.PrefixSpringline = (_prefixSpring.Text ?? string.Empty).Trim();
        }

        private static string Required(TextBox box, string name, ICollection<string> problems, string fallback)
        {
            var text = (box.Text ?? string.Empty).Trim();
            if (text.Length == 0) { problems.Add("Dips: the " + name + " wording cannot be empty."); return fallback; }
            return text;
        }

        private static string Cell(DataGridViewRow row, int index)
        {
            return (Convert.ToString(row.Cells[index].Value) ?? string.Empty).Trim();
        }

        private static bool TryCell(DataGridViewRow row, int index, out double value)
        {
            return double.TryParse(Cell(row, index), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value);
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Dips.RestoreDefaults();
            LoadFrom(s);
        }
    }

    // --------------------------------------------------------------- strip easements

    internal sealed class EasementsPage : SetupPage
    {
        private ComboBox _boundaryLayer, _sidelineLayer, _centerlineLayer, _hatchLayer, _dimLayer, _textLayer, _tableLayer, _tempLayer;
        private TextBox _tempPattern, _tempPurpose;
        private ComboBox _areaLayer;
        private TextBox _areaPattern, _areaPurpose, _areaTitle, _areaArea, _areaLegal;
        private CheckBox _labelCenterline, _bearingSpaces, _pointLabels;
        private TextBox _pocLabel, _pobLabel, _terminusLabel, _legalArea;
        private CheckBox _sidelines, _centerline, _hatch, _dims;
        private TextBox _pattern, _hatchScale, _dimStyle;
        private ComboBox _labelMode;
        private TextBox _linePrefix, _curvePrefix;
        private CheckBox _chord, _tangent, _footSymbol;
        private TextBox _seconds, _distanceDecimals;
        private TextBox _title, _purpose, _area, _acres, _sqftDecimals, _acresDecimals;
        private TextBox _textStyle, _textHeight, _tableStyle, _tolerance;

        public override string Title { get { return "Strip Easements"; } }
        public override string AffectedCommands { get { return "STRIPEASEMENT, FTFEASEMENTCHECK"; } }

        protected override void BuildBody()
        {
            Heading("Layers");
            var layers = new List<object>();
            foreach (var name in Context.Drawing.Layers) layers.Add(name);
            _boundaryLayer = ComboRow("Boundary", layers, null);
            _sidelineLayer = ComboRow("Sidelines", layers, null);
            _centerlineLayer = ComboRow("Centerline", layers, null);
            _hatchLayer = ComboRow("Hatch", layers, null);
            _dimLayer = ComboRow("Width dimensions", layers, null);
            _textLayer = ComboRow("Title, area and course labels", layers, null);
            _tableLayer = ComboRow("Line / curve tables", layers, null);
            _tempLayer = ComboRow("Temporary construction easement", layers, null);

            Heading("What gets drawn");
            _sidelines = CheckRow("Draw sidelines as separate lines (the closed boundary is always drawn)", null);
            _centerline = CheckRow("Draw the centerline", null);
            _hatch = CheckRow("Hatch the easement", null);
            _pattern = TextRow("Hatch pattern", "Any pattern name AutoCAD knows, e.g. ANSI31.");
            _hatchScale = TextRow("Hatch scale (plotted)", null);
            _dims = CheckRow("Dimension the width", null);
            _dimStyle = TextRow("Dimension style", "Leave empty to use the drawing's CURRENT dimension style.", 220);
            _tempPattern = TextRow("Temporary easement hatch", "Empty draws the temporary construction easement as an outline only.", 200);
            _tempPurpose = TextRow("Temporary easement purpose", "Used in its title, e.g. TEMPORARY CONSTRUCTION.", 260);

            Heading("Lines and curves");
            _labelMode = ComboRow("Course labels", Enum.GetNames(typeof(EasementLabelMode)).Cast<object>(),
                "Auto writes bearings and distances on the lines where they fit and tables the short ones; Direct writes all on the lines; Table tags L1/C1 and builds tables; None skips them.");
            _labelCenterline = CheckRow("Label the centerline and ties, as on an exhibit (off labels the outline courses)", null);
            _bearingSpaces = CheckRow("Write bearings with spaces: N 01°22'31\" E instead of N01°22'31\"E", null);
            _linePrefix = TextRow("Line tag prefix", null);
            _curvePrefix = TextRow("Curve tag prefix", null);
            _chord = CheckRow("Show chord bearing and length for curves", null);
            _tangent = CheckRow("Show tangent length for curves", null);
            _seconds = TextRow("Bearing seconds decimals", null);
            _distanceDecimals = TextRow("Distance decimals", null);
            _footSymbol = CheckRow("Write the foot symbol after distances", null);

            Heading("Construction areas (CONSTRUCTIONAREA)");
            _areaLayer = ComboRow("Layer", layers, "Clicked metes and bounds areas have their own layer.");
            _areaPattern = TextRow("Hatch", "Empty draws the outline only.", 200);
            _areaPurpose = TextRow("Default purpose", null, 260);
            _areaTitle = TextRow("Title", "{purpose}", 260);
            _areaArea = TextRow("Area line", "{purpose} and {sqft}", 360);
            _areaLegal = TextRow("Legal description area", "{purpose} and {sqft}", 420);

            Heading("Points");
            _pointLabels = CheckRow("Leader the Point of Commencement, Point of Beginning and terminus", null);
            _pocLabel = TextRow("Commencement label", null, 260);
            _pobLabel = TextRow("Beginning label", null, 260);
            _terminusLabel = TextRow("Terminus label", null, 260);

            Heading("Title and area");
            _title = TextRow("Title", "{width} and {purpose} are filled in.", 300);
            _purpose = TextRow("Default purpose", "Asked for each easement; this is the default.", 200);
            _area = TextRow("Area line", "{purpose} and {sqft}.", 360);
            _acres = TextRow("Acres line", "{acres}; leave empty to leave acres off.", 300);
            _legalArea = TextRow("Legal description area", "{purpose} and {sqft}.", 420);
            _sqftDecimals = TextRow("Square feet decimals", null);
            _acresDecimals = TextRow("Acres decimals", null);

            Heading("Text and tolerance");
            _textStyle = TextRow("Text style", "Empty uses the current style.", 200);
            _textHeight = TextRow("Text height (plotted)", null);
            _tableStyle = TextRow("Table style", "Empty uses the current table style.", 200);
            _tolerance = TextRow("Closure tolerance (feet)", "Gaps larger than this are errors, never bridged.");
        }

        public override void LoadFrom(FtfSettings s)
        {
            var e = s.Easements;
            ComboHelp.SelectOrAdd(_boundaryLayer, e.BoundaryLayer, false);
            ComboHelp.SelectOrAdd(_sidelineLayer, e.SidelineLayer, false);
            ComboHelp.SelectOrAdd(_centerlineLayer, e.CenterlineLayer, false);
            ComboHelp.SelectOrAdd(_hatchLayer, e.HatchLayer, false);
            ComboHelp.SelectOrAdd(_dimLayer, e.DimensionLayer, false);
            ComboHelp.SelectOrAdd(_textLayer, e.TextLayer, false);
            ComboHelp.SelectOrAdd(_tableLayer, e.TableLayer, false);
            ComboHelp.SelectOrAdd(_tempLayer, e.TemporaryLayer, false);
            _tempPattern.Text = e.TemporaryHatchPattern;
            _tempPurpose.Text = e.TemporaryPurpose;
            _sidelines.Checked = e.DrawSidelines;
            _centerline.Checked = e.DrawCenterline;
            _hatch.Checked = e.DrawHatch;
            _pattern.Text = e.HatchPattern;
            _hatchScale.Text = Fmt(e.HatchScale);
            _dims.Checked = e.DrawWidthDimensions;
            _dimStyle.Text = e.DimensionStyleOverride;
            ComboHelp.SelectOrAdd(_labelMode, e.LabelMode.ToString(), false);
            _labelCenterline.Checked = e.LabelCenterline;
            _bearingSpaces.Checked = e.BearingSpaces;
            _pointLabels.Checked = e.DrawPointLabels;
            ComboHelp.SelectOrAdd(_areaLayer, e.AreaLayer, false);
            _areaPattern.Text = e.AreaHatchPattern;
            _areaPurpose.Text = e.AreaPurpose;
            _areaTitle.Text = e.AreaTitleFormat;
            _areaArea.Text = e.AreaAreaFormat;
            _areaLegal.Text = e.AreaLegalFormat;
            _pocLabel.Text = e.CommencementLabel;
            _pobLabel.Text = e.BeginningLabel;
            _terminusLabel.Text = e.TerminusLabel;
            _legalArea.Text = e.LegalAreaFormat;
            _linePrefix.Text = e.LinePrefix;
            _curvePrefix.Text = e.CurvePrefix;
            _chord.Checked = e.CurveShowChord;
            _tangent.Checked = e.CurveShowTangent;
            _seconds.Text = e.BearingSecondsDecimals.ToString(CultureInfo.InvariantCulture);
            _distanceDecimals.Text = e.DistanceDecimals.ToString(CultureInfo.InvariantCulture);
            _footSymbol.Checked = e.FootSymbol;
            _title.Text = e.TitleFormat;
            _purpose.Text = e.DefaultPurpose;
            _area.Text = e.AreaFormat;
            _acres.Text = e.AcresFormat;
            _sqftDecimals.Text = e.AreaSquareFeetDecimals.ToString(CultureInfo.InvariantCulture);
            _acresDecimals.Text = e.AcresDecimals.ToString(CultureInfo.InvariantCulture);
            _textStyle.Text = e.TextStyle;
            _textHeight.Text = Fmt(e.TextHeightPlotted);
            _tableStyle.Text = e.TableStyle;
            _tolerance.Text = Fmt(e.ToleranceFt);
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            var e = s.Easements;
            e.BoundaryLayer = Convert.ToString(_boundaryLayer.SelectedItem) ?? string.Empty;
            e.SidelineLayer = Convert.ToString(_sidelineLayer.SelectedItem) ?? string.Empty;
            e.CenterlineLayer = Convert.ToString(_centerlineLayer.SelectedItem) ?? string.Empty;
            e.HatchLayer = Convert.ToString(_hatchLayer.SelectedItem) ?? string.Empty;
            e.DimensionLayer = Convert.ToString(_dimLayer.SelectedItem) ?? string.Empty;
            e.TextLayer = Convert.ToString(_textLayer.SelectedItem) ?? string.Empty;
            e.TableLayer = Convert.ToString(_tableLayer.SelectedItem) ?? string.Empty;
            e.TemporaryLayer = Convert.ToString(_tempLayer.SelectedItem) ?? string.Empty;
            e.TemporaryHatchPattern = (_tempPattern.Text ?? string.Empty).Trim();
            e.TemporaryPurpose = (_tempPurpose.Text ?? string.Empty).Trim().ToUpperInvariant();
            e.DrawSidelines = _sidelines.Checked;
            e.DrawCenterline = _centerline.Checked;
            e.DrawHatch = _hatch.Checked;
            e.HatchPattern = (_pattern.Text ?? string.Empty).Trim();
            var v = e.HatchScale; ReadDouble(_hatchScale, "Easements: hatch scale", x => x > 0, "must be greater than zero", problems, ref v); e.HatchScale = v;
            e.DrawWidthDimensions = _dims.Checked;
            e.DimensionStyleOverride = (_dimStyle.Text ?? string.Empty).Trim();
            e.LabelMode = (EasementLabelMode)Enum.Parse(typeof(EasementLabelMode), Convert.ToString(_labelMode.SelectedItem));
            e.LabelCenterline = _labelCenterline.Checked;
            e.BearingSpaces = _bearingSpaces.Checked;
            e.DrawPointLabels = _pointLabels.Checked;
            e.AreaLayer = Convert.ToString(_areaLayer.SelectedItem) ?? string.Empty;
            e.AreaHatchPattern = (_areaPattern.Text ?? string.Empty).Trim();
            e.AreaPurpose = (_areaPurpose.Text ?? string.Empty).Trim().ToUpperInvariant();
            e.AreaTitleFormat = (_areaTitle.Text ?? string.Empty).Trim();
            e.AreaAreaFormat = (_areaArea.Text ?? string.Empty).Trim();
            e.AreaLegalFormat = (_areaLegal.Text ?? string.Empty).Trim();
            e.CommencementLabel = (_pocLabel.Text ?? string.Empty).Trim();
            e.BeginningLabel = (_pobLabel.Text ?? string.Empty).Trim();
            e.TerminusLabel = (_terminusLabel.Text ?? string.Empty).Trim();
            e.LegalAreaFormat = (_legalArea.Text ?? string.Empty).Trim();
            e.LinePrefix = (_linePrefix.Text ?? string.Empty).Trim();
            e.CurvePrefix = (_curvePrefix.Text ?? string.Empty).Trim();
            e.CurveShowChord = _chord.Checked;
            e.CurveShowTangent = _tangent.Checked;
            var i = e.BearingSecondsDecimals; ReadInt(_seconds, "Easements: bearing seconds decimals", x => x >= 0 && x <= 2, "must be between 0 and 2", problems, ref i); e.BearingSecondsDecimals = i;
            i = e.DistanceDecimals; ReadInt(_distanceDecimals, "Easements: distance decimals", x => x >= 0 && x <= 4, "must be between 0 and 4", problems, ref i); e.DistanceDecimals = i;
            e.FootSymbol = _footSymbol.Checked;
            e.TitleFormat = (_title.Text ?? string.Empty).Trim();
            e.DefaultPurpose = (_purpose.Text ?? string.Empty).Trim().ToUpperInvariant();
            e.AreaFormat = (_area.Text ?? string.Empty).Trim();
            e.AcresFormat = (_acres.Text ?? string.Empty).Trim();
            i = e.AreaSquareFeetDecimals; ReadInt(_sqftDecimals, "Easements: square feet decimals", x => x >= 0 && x <= 2, "must be between 0 and 2", problems, ref i); e.AreaSquareFeetDecimals = i;
            i = e.AcresDecimals; ReadInt(_acresDecimals, "Easements: acres decimals", x => x >= 0 && x <= 4, "must be between 0 and 4", problems, ref i); e.AcresDecimals = i;
            e.TextStyle = (_textStyle.Text ?? string.Empty).Trim();
            v = e.TextHeightPlotted; ReadDouble(_textHeight, "Easements: text height", x => x > 0, "must be greater than zero", problems, ref v); e.TextHeightPlotted = v;
            e.TableStyle = (_tableStyle.Text ?? string.Empty).Trim();
            v = e.ToleranceFt; ReadDouble(_tolerance, "Easements: tolerance", x => x > 0 && x < 1, "must be between 0 and 1 foot", problems, ref v); e.ToleranceFt = v;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Easements.RestoreDefaults();
            LoadFrom(s);
        }
    }
}
