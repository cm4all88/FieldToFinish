using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Recorded Surveys: how FTFRECORD reads, reviews, builds and labels, and the
    /// standards that map survey entities onto this drawing's layers, styles and blocks. The
    /// dropdown lists come from the open drawing, so a name is chosen, never typed wrong; a
    /// name that is not in the drawing is still allowed (a template may be loaded later) and
    /// is reported as missing at run time.
    /// </summary>
    internal sealed class RecordSurveyPage : SetupPage
    {
        private ComboBox _engine, _buildFrom, _labelMode, _whenMissing, _textStyle;
        private TextBox _dpi, _rotations, _threshold, _alternatives, _closure, _precision, _distanceTol, _bearingTol, _radiusTol, _arcTol, _deltaTol, _chordTol, _sharedTol, _rmDist, _rmBearing;
        private TextBox _textHeight, _offset, _seconds, _decimals, _measuredFormat, _recordFormat, _tableLayer, _lotFormat, _lotHeight;
        private CheckBox _keepImages, _projectFile, _createLayers, _monuments, _civil, _mask, _stacked, _foot, _spaces, _chord, _tangent, _recordAndMeasured, _lotLabels, _areas, _separate;
        private DataGridView _entities, _monumentGrid;

        public override string Title { get { return "Recorded Surveys"; } }
        public override string AffectedCommands { get { return "FTFRECORD, FTFRECORDCHECK, FTFRECORDLABEL, FTFRECORDSOURCE, FTFRECORDREBUILD"; } }

        protected override void BuildBody()
        {
            Heading("Reading the document");
            Note("FTFRECORD reads PDF, TIFF, JPG and PNG with the OCR engine Windows ships with; nothing leaves the machine. " +
                 "Survey annotation runs at every angle, so each page is read at each of the rotations below and the passes are merged. " +
                 "Sidecar reads a .ocr.json beside the document written by another OCR tool instead.");
            _engine = ComboRow("OCR engine", new object[] { RecordSurveySettings.OcrWindows, RecordSurveySettings.OcrSidecar }, "Windows: the built-in engine. Sidecar: a .ocr.json in the FTF OCR schema beside the document.", 140);
            _dpi = TextRow("Resolution (dpi)", "Pages are rendered at this resolution for OCR. 300 suits most scans; 400 for small annotation.");
            _rotations = TextRow("Page rotations read (degrees)", "Comma separated. 0,90,270,180 reads text at every right angle; add 45,135,225,315 for plats with diagonal lots (slower).", 220);
            _keepImages = CheckRow("Keep page images for the review window", "Saved under %LOCALAPPDATA%\\FieldToFinish\\record-pages, never beside the document.");

            Heading("Review");
            Note("Windows OCR reports no per-word confidence: a line read once is 0.90, the same text read again in another pass is 0.97, and every repaired look-alike character lowers it. " +
                 "Calls under the threshold, with alternative readings, or incomplete must be approved by a person before geometry is built.");
            _threshold = TextRow("Review threshold (0 to 1)", "Calls read below this confidence stop the build until reviewed.");
            _alternatives = TextRow("Digit alternatives offered per doubtful number", "How many single-digit confusions (3/8, 5/8, 5/6, 1/7, 0/8) to offer for a low-confidence distance. Offered only; never applied on their own.");
            _projectFile = CheckRow("Write the project (calls, review, build) beside the drawing as .ftfrecord.json", "The same record is stored in the drawing; the file is for review outside Civil 3D.");

            Heading("Geometry");
            _buildFrom = ComboRow("Build from", new object[] { RecordSurveySettings.BuildFromMeasured, RecordSurveySettings.BuildFromRecord }, "Measured: a Record of Survey's (M) values where given, the record otherwise. Record: always the record value. Both are kept on every course.", 140);
            _separate = CheckRow("Draw each course as its own line or arc", "Shared lot lines are always one entity. (A closed polyline per figure is not offered in this version.)");
            _closure = TextRow("Closure tolerance (ft)", "A figure whose misclosure is at or under this closes. Nothing is ever adjusted to force closure.");
            _precision = TextRow("Minimum closure precision (1:N)", "A closed figure worse than this is reported.");
            _distanceTol = TextRow("Distance tolerance (ft)", "Bearing and distance comparisons in FTFRECORDCHECK.");
            _bearingTol = TextRow("Bearing tolerance (seconds)", null);
            _radiusTol = TextRow("Radius tolerance (ft)", null);
            _arcTol = TextRow("Arc length tolerance (ft)", null);
            _deltaTol = TextRow("Delta tolerance (seconds)", null);
            _chordTol = TextRow("Chord tolerance (ft)", null);
            _sharedTol = TextRow("Shared corner tolerance (ft)", "Course ends this close are the same corner: the line between two lots is built once.");
            _rmDist = TextRow("Report record vs measured beyond (ft)", "A course whose record and measured distances differ by more than this is reported (never reconciled).");
            _rmBearing = TextRow("Report record vs measured beyond (seconds)", null);

            Heading("Standards: survey entities to this drawing's resources");
            Note("Each course type is drawn on the layer named here and labelled with the Civil 3D label style named here (or plain text in the text style). " +
                 "FTFRECORD checks every name against the open drawing: a missing layer withholds those courses, a missing style withholds those labels -- " +
                 "nothing is substituted or invented. Choose names from this drawing's own lists. Empty label layer: the office text layer in the line layer's family is used, as line labels do.");
            _entities = AddFullWidth(new DataGridView
            {
                Width = NoteWidth + 120, Height = 230, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = false,
                BackgroundColor = SystemColors.Window, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            });
            _entities.Columns.Add("Name", "Entity");
            _entities.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "On", FillWeight = 30 });
            _entities.Columns.Add(Combo("Layer", "Layer", Context.Drawing.Layers));
            _entities.Columns.Add(Combo("Linetype", "Linetype", Context.Drawing.Linetypes));
            _entities.Columns.Add(Combo("LabelLayer", "Label layer", Context.Drawing.Layers));
            _entities.Columns.Add(Combo("LineLabelStyle", "C3D line label style", Context.Drawing.LineLabelStyles));
            _entities.Columns.Add(Combo("CurveLabelStyle", "C3D curve label style", Context.Drawing.CurveLabelStyles));
            _entities.Columns.Add(Combo("TextStyle", "Text style", Context.Drawing.TextStyles));
            _entities.Columns.Add("Height", "Text ht (plotted)");
            _entities.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Label", HeaderText = "Label", FillWeight = 40 });
            _createLayers = CheckRow("Create a configured layer that is missing from the drawing (otherwise its courses are withheld and reported)", null);

            Heading("Monuments");
            Note("The block and layer for found, set and calculated monuments. No block is shipped: choose the office symbol from this drawing. Monuments without a block are listed in the review but not drawn.");
            _monumentGrid = AddFullWidth(new DataGridView
            {
                Width = NoteWidth + 120, Height = 120, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                BackgroundColor = SystemColors.Window, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            });
            _monumentGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", ReadOnly = true });
            _monumentGrid.Columns.Add(Combo("Block", "Block", Context.Drawing.Blocks));
            _monumentGrid.Columns.Add(Combo("Layer", "Layer", Context.Drawing.Layers));
            _monumentGrid.Columns.Add(Combo("LabelLayer", "Label layer", Context.Drawing.Layers));
            _monumentGrid.Columns.Add("Scale", "Scale (plotted)");
            _monuments = CheckRow("Place monument symbols at reconstructed corners", null);

            Heading("Labels");
            Note("Bearing/distance and curve labels are Civil 3D general line/curve labels where a style is named and present (editable, style-driven), " +
                 "otherwise plain text. Placement avoids other courses, monuments and other labels; shared lot lines are labelled once; text is never upside down.");
            _civil = CheckRow("Use Civil 3D label objects where the standard names a style", null);
            _whenMissing = ComboRow("When a named label style is missing", new object[] { RecordSurveySettings.MissingStyleFlag, RecordSurveySettings.MissingStylePlainText }, "Flag: report it and withhold those labels. PlainText: place plain text and still report it.", 120);
            _labelMode = ComboRow("Label mode", new object[] { RecordSurveySettings.LabelModeAuto, RecordSurveySettings.LabelModeDirect, RecordSurveySettings.LabelModeTable, RecordSurveySettings.LabelModeNone }, "Auto: on the line when it fits, else tagged into a line/curve table. Direct: always on the line. Table: always tagged. None: no labels.", 120);
            _textStyle = ComboRow("Text style (plain-text labels)", new object[] { string.Empty }.Concat(Context.Drawing.TextStyles.Cast<object>()), "Used when an entity standard names none. Empty: the drawing's current style, reported as a fallback.");
            _textHeight = TextRow("Text height (plotted)", null);
            _offset = TextRow("Offset from the line (plotted)", null);
            _mask = CheckRow("Wipeout under plain-text labels", null);
            _stacked = CheckRow("Bearing over distance (two lines) instead of one line", null);
            _seconds = TextRow("Bearing seconds decimals", null);
            _decimals = TextRow("Distance decimals", null);
            _foot = CheckRow("Foot symbol on distances", null);
            _spaces = CheckRow("Spaces in bearings (N 01°22'31\" E rather than N01°22'31\"E)", null);
            _chord = CheckRow("Curve labels show the chord bearing and distance", null);
            _tangent = CheckRow("Curve labels show the tangent length", null);
            _recordAndMeasured = CheckRow("Label both values on a course with record and measured", null);
            _measuredFormat = TextRow("Measured label format", "{value} is the bearing and distance; e.g. \"{value} (M)\".", 200);
            _recordFormat = TextRow("Record label format", "{value} and {source} (R1); e.g. \"{value} ({source})\".", 200);
            _tableLayer = TextRow("Line/curve table layer", "Must exist in the drawing when the table is needed.", 200);
            _lotLabels = CheckRow("Label lots and tracts inside each closed figure", null);
            _lotFormat = TextRow("Lot label format", "{lot} and {block}.", 160);
            _lotHeight = TextRow("Lot text height (plotted)", null);
            _areas = CheckRow("Add the area under the lot label (from the traversed calls)", null);
        }

        private static DataGridViewComboBoxColumn Combo(string name, string header, IEnumerable<string> items)
        {
            var column = new DataGridViewComboBoxColumn { Name = name, HeaderText = header, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing };
            column.Items.Add(string.Empty);
            foreach (var item in items) column.Items.Add(item);
            return column;
        }

        /// <summary>A combo cell accepts a value even when it is not in this drawing's list -- a template may bring it -- rather than throwing.</summary>
        private static void SetCombo(DataGridViewRow row, string column, string value)
        {
            var cell = row.Cells[column] as DataGridViewComboBoxCell;
            var text = value ?? string.Empty;
            if (cell != null && !cell.Items.Contains(text)) cell.Items.Add(text);
            row.Cells[column].Value = text;
        }

        public override void LoadFrom(FtfSettings s)
        {
            var r = s.RecordSurvey;
            _engine.SelectedItem = Match(_engine, r.OcrEngine);
            _dpi.Text = r.OcrDpi.ToString(CultureInfo.InvariantCulture);
            _rotations.Text = r.OcrRotations;
            _keepImages.Checked = r.KeepPageImages;
            _threshold.Text = Fmt(r.ReviewThreshold);
            _alternatives.Text = r.MaxAlternatives.ToString(CultureInfo.InvariantCulture);
            _projectFile.Checked = r.WriteProjectFile;
            _buildFrom.SelectedItem = Match(_buildFrom, r.BuildFrom);
            _separate.Checked = r.SeparateCourses;
            _closure.Text = Fmt(r.ClosureToleranceFt); _precision.Text = Fmt(r.MinimumClosurePrecision); _distanceTol.Text = Fmt(r.DistanceToleranceFt);
            _bearingTol.Text = Fmt(r.BearingToleranceSeconds); _radiusTol.Text = Fmt(r.RadiusToleranceFt); _arcTol.Text = Fmt(r.ArcToleranceFt);
            _deltaTol.Text = Fmt(r.DeltaToleranceSeconds); _chordTol.Text = Fmt(r.ChordToleranceFt); _sharedTol.Text = Fmt(r.SharedLineToleranceFt);
            _rmDist.Text = Fmt(r.RecordVsMeasuredDistanceWarnFt); _rmBearing.Text = Fmt(r.RecordVsMeasuredBearingWarnSeconds);

            _entities.Rows.Clear();
            foreach (var e in r.Entities)
            {
                var i = _entities.Rows.Add();
                var row = _entities.Rows[i];
                row.Cells["Name"].Value = e.Name;
                row.Cells["Enabled"].Value = e.Enabled;
                SetCombo(row, "Layer", e.Layer); SetCombo(row, "Linetype", e.Linetype); SetCombo(row, "LabelLayer", e.LabelLayer);
                SetCombo(row, "LineLabelStyle", e.LineLabelStyle); SetCombo(row, "CurveLabelStyle", e.CurveLabelStyle); SetCombo(row, "TextStyle", e.TextStyle);
                row.Cells["Height"].Value = Fmt(e.TextHeightPlotted);
                row.Cells["Label"].Value = e.Label;
            }
            _createLayers.Checked = r.CreateMissingLayers;

            _monumentGrid.Rows.Clear();
            foreach (var m in r.Monuments)
            {
                var i = _monumentGrid.Rows.Add();
                var row = _monumentGrid.Rows[i];
                row.Cells["Status"].Value = m.Status;
                SetCombo(row, "Block", m.Block); SetCombo(row, "Layer", m.Layer); SetCombo(row, "LabelLayer", m.LabelLayer);
                row.Cells["Scale"].Value = Fmt(m.ScalePlotted);
            }
            _monuments.Checked = r.DrawMonuments;

            _civil.Checked = r.UseCivil3DLabels;
            _whenMissing.SelectedItem = Match(_whenMissing, r.WhenLabelStyleMissing);
            _labelMode.SelectedItem = Match(_labelMode, r.LabelMode);
            _textStyle.SelectedItem = _textStyle.Items.Contains(r.TextStyle ?? string.Empty) ? r.TextStyle ?? string.Empty : string.Empty;
            _textHeight.Text = Fmt(r.TextHeightPlotted); _offset.Text = Fmt(r.OffsetPlotted);
            _mask.Checked = r.Mask; _stacked.Checked = r.Stacked;
            _seconds.Text = r.BearingSecondsDecimals.ToString(CultureInfo.InvariantCulture); _decimals.Text = r.DistanceDecimals.ToString(CultureInfo.InvariantCulture);
            _foot.Checked = r.FootSymbol; _spaces.Checked = r.BearingSpaces; _chord.Checked = r.CurveShowChord; _tangent.Checked = r.CurveShowTangent;
            _recordAndMeasured.Checked = r.LabelRecordAndMeasured;
            _measuredFormat.Text = r.MeasuredLabelFormat; _recordFormat.Text = r.RecordLabelFormat; _tableLayer.Text = r.TableLayer;
            _lotLabels.Checked = r.LabelLots; _lotFormat.Text = r.LotLabelFormat; _lotHeight.Text = Fmt(r.LotTextHeightPlotted); _areas.Checked = r.LabelAreas;
        }

        private static object Match(ComboBox combo, string value)
        {
            foreach (var item in combo.Items) if (string.Equals(item as string, value, StringComparison.OrdinalIgnoreCase)) return item;
            return combo.Items.Count > 0 ? combo.Items[0] : null;
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            var r = s.RecordSurvey;
            r.OcrEngine = _engine.SelectedItem as string ?? RecordSurveySettings.OcrWindows;
            var dpi = r.OcrDpi; if (ReadInt(_dpi, "Recorded surveys: OCR resolution", v => v >= 72 && v <= 1200, "must be between 72 and 1200", problems, ref dpi)) r.OcrDpi = dpi;
            r.OcrRotations = _rotations.Text.Trim();
            r.KeepPageImages = _keepImages.Checked;
            var threshold = r.ReviewThreshold; if (ReadDouble(_threshold, "Recorded surveys: review threshold", v => v >= 0 && v <= 1, "must be between 0 and 1", problems, ref threshold)) r.ReviewThreshold = threshold;
            var alternatives = r.MaxAlternatives; if (ReadInt(_alternatives, "Recorded surveys: alternatives", v => v >= 0 && v <= 10, "must be between 0 and 10", problems, ref alternatives)) r.MaxAlternatives = alternatives;
            r.WriteProjectFile = _projectFile.Checked;
            r.BuildFrom = _buildFrom.SelectedItem as string ?? RecordSurveySettings.BuildFromMeasured;
            r.SeparateCourses = _separate.Checked;
            Positive(_closure, "closure tolerance", problems, v => r.ClosureToleranceFt = v);
            Positive(_precision, "minimum closure precision", problems, v => r.MinimumClosurePrecision = v);
            Positive(_distanceTol, "distance tolerance", problems, v => r.DistanceToleranceFt = v);
            Positive(_bearingTol, "bearing tolerance", problems, v => r.BearingToleranceSeconds = v);
            Positive(_radiusTol, "radius tolerance", problems, v => r.RadiusToleranceFt = v);
            Positive(_arcTol, "arc tolerance", problems, v => r.ArcToleranceFt = v);
            Positive(_deltaTol, "delta tolerance", problems, v => r.DeltaToleranceSeconds = v);
            Positive(_chordTol, "chord tolerance", problems, v => r.ChordToleranceFt = v);
            Positive(_sharedTol, "shared corner tolerance", problems, v => r.SharedLineToleranceFt = v);
            NotNegative(_rmDist, "record vs measured distance warning", problems, v => r.RecordVsMeasuredDistanceWarnFt = v);
            NotNegative(_rmBearing, "record vs measured bearing warning", problems, v => r.RecordVsMeasuredBearingWarnSeconds = v);

            var entities = new List<RecordEntityStandard>();
            foreach (DataGridViewRow row in _entities.Rows)
            {
                if (row.IsNewRow) continue;
                var name = (row.Cells["Name"].Value as string ?? string.Empty).Trim();
                if (name.Length == 0) continue;
                var e = new RecordEntityStandard
                {
                    Name = name, Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? true, CultureInfo.InvariantCulture),
                    Layer = Cell(row, "Layer"), Linetype = Cell(row, "Linetype"), LabelLayer = Cell(row, "LabelLayer"),
                    LineLabelStyle = Cell(row, "LineLabelStyle"), CurveLabelStyle = Cell(row, "CurveLabelStyle"), TextStyle = Cell(row, "TextStyle"),
                    Label = Convert.ToBoolean(row.Cells["Label"].Value ?? true, CultureInfo.InvariantCulture)
                };
                double height;
                if (!double.TryParse(Cell(row, "Height"), NumberStyles.Float, CultureInfo.InvariantCulture, out height) || height <= 0)
                    problems.Add("Recorded surveys: " + name + ": text height must be a number greater than zero.");
                else e.TextHeightPlotted = height;
                entities.Add(e);
            }
            if (entities.Count > 0) r.Entities = entities;
            r.CreateMissingLayers = _createLayers.Checked;

            var monuments = new List<MonumentStandard>();
            foreach (DataGridViewRow row in _monumentGrid.Rows)
            {
                var m = new MonumentStandard { Status = Cell(row, "Status"), Block = Cell(row, "Block"), Layer = Cell(row, "Layer"), LabelLayer = Cell(row, "LabelLayer") };
                double scale;
                if (!double.TryParse(Cell(row, "Scale"), NumberStyles.Float, CultureInfo.InvariantCulture, out scale) || scale <= 0)
                    problems.Add("Recorded surveys: monument " + m.Status + ": scale must be greater than zero.");
                else m.ScalePlotted = scale;
                monuments.Add(m);
            }
            if (monuments.Count > 0) r.Monuments = monuments;
            r.DrawMonuments = _monuments.Checked;

            r.UseCivil3DLabels = _civil.Checked;
            r.WhenLabelStyleMissing = _whenMissing.SelectedItem as string ?? RecordSurveySettings.MissingStyleFlag;
            r.LabelMode = _labelMode.SelectedItem as string ?? RecordSurveySettings.LabelModeAuto;
            r.TextStyle = _textStyle.SelectedItem as string ?? string.Empty;
            Positive(_textHeight, "label text height", problems, v => r.TextHeightPlotted = v);
            NotNegative(_offset, "label offset", problems, v => r.OffsetPlotted = v);
            r.Mask = _mask.Checked; r.Stacked = _stacked.Checked;
            var seconds = r.BearingSecondsDecimals; if (ReadInt(_seconds, "Recorded surveys: bearing seconds decimals", v => v >= 0 && v <= 3, "must be between 0 and 3", problems, ref seconds)) r.BearingSecondsDecimals = seconds;
            var decimals = r.DistanceDecimals; if (ReadInt(_decimals, "Recorded surveys: distance decimals", v => v >= 0 && v <= 4, "must be between 0 and 4", problems, ref decimals)) r.DistanceDecimals = decimals;
            r.FootSymbol = _foot.Checked; r.BearingSpaces = _spaces.Checked; r.CurveShowChord = _chord.Checked; r.CurveShowTangent = _tangent.Checked;
            r.LabelRecordAndMeasured = _recordAndMeasured.Checked;
            r.MeasuredLabelFormat = _measuredFormat.Text; r.RecordLabelFormat = _recordFormat.Text; r.TableLayer = _tableLayer.Text.Trim();
            r.LabelLots = _lotLabels.Checked; r.LotLabelFormat = _lotFormat.Text; r.LabelAreas = _areas.Checked;
            Positive(_lotHeight, "lot text height", problems, v => r.LotTextHeightPlotted = v);
            r.Validate(problems);
        }

        private static string Cell(DataGridViewRow row, string column)
        {
            return (row.Cells[column].Value as string ?? string.Empty).Trim();
        }

        private static void Positive(TextBox box, string name, ICollection<string> problems, Action<double> set)
        {
            var v = 0.0;
            if (ReadDouble(box, "Recorded surveys: " + name, x => x > 0, "must be greater than zero", problems, ref v)) set(v);
        }

        private static void NotNegative(TextBox box, string name, ICollection<string> problems, Action<double> set)
        {
            var v = 0.0;
            if (ReadDouble(box, "Recorded surveys: " + name, x => x >= 0, "cannot be negative", problems, ref v)) set(v);
        }

        public override void RestoreDefaults(FtfSettings settings)
        {
            settings.RecordSurvey.RestoreDefaults();
            LoadFrom(settings);
        }
    }
}
