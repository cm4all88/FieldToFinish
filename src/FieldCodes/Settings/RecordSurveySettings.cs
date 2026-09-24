using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace FieldCodes.Settings
{
    /// <summary>
    /// The office standard for one kind of recorded-survey course: which layer it is drawn
    /// on and how it is labelled. Names refer to resources that must already exist in the
    /// drawing or template; FTFRECORD checks them and reports what is missing rather than
    /// creating or substituting anything.
    /// </summary>
    public sealed class RecordEntityStandard
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        [JsonProperty("layer")] public string Layer { get; set; }
        [JsonProperty("linetype")] public string Linetype { get; set; }
        /// <summary>Layer for plain-text labels. Empty derives the office text layer from the line layer's family, as the line labels do.</summary>
        [JsonProperty("labelLayer")] public string LabelLayer { get; set; }
        /// <summary>Civil 3D general line label style, used when Civil 3D labels are on. Empty means none configured.</summary>
        [JsonProperty("lineLabelStyle")] public string LineLabelStyle { get; set; }
        [JsonProperty("curveLabelStyle")] public string CurveLabelStyle { get; set; }
        /// <summary>Text style for plain-text labels; empty uses the section default.</summary>
        [JsonProperty("textStyle")] public string TextStyle { get; set; }
        [JsonProperty("textHeightPlotted")] public double TextHeightPlotted { get; set; }
        /// <summary>Label the courses of this type at all.</summary>
        [JsonProperty("label")] public bool Label { get; set; }

        public RecordEntityStandard()
        {
            Enabled = true;
            Layer = string.Empty;
            Linetype = string.Empty;
            LabelLayer = string.Empty;
            LineLabelStyle = string.Empty;
            CurveLabelStyle = string.Empty;
            TextStyle = string.Empty;
            TextHeightPlotted = 0.08;
            Label = true;
        }

        public RecordEntityStandard Clone() { return (RecordEntityStandard)MemberwiseClone(); }
    }

    /// <summary>The block and layer for one monument status (found, set, calculated).</summary>
    public sealed class MonumentStandard
    {
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("block")] public string Block { get; set; }
        [JsonProperty("layer")] public string Layer { get; set; }
        [JsonProperty("labelLayer")] public string LabelLayer { get; set; }
        /// <summary>Block scale as plotted (one plotted unit at the annotation scale).</summary>
        [JsonProperty("scalePlotted")] public double ScalePlotted { get; set; }

        public MonumentStandard()
        {
            Block = string.Empty;
            Layer = string.Empty;
            LabelLayer = string.Empty;
            ScalePlotted = 1.0;
        }

        public MonumentStandard Clone() { return (MonumentStandard)MemberwiseClone(); }
    }

    /// <summary>
    /// How FTFRECORD reads a recorded survey, reviews it, builds it and labels it. The
    /// standards map survey entities to the office's Civil 3D resources by name; the
    /// shipped defaults are FTF's generic names, and an office profile replaces them.
    /// </summary>
    public sealed class RecordSurveySettings : ISettingsSection
    {
        public const string BuildFromMeasured = "Measured";
        public const string BuildFromRecord = "Record";
        public const string OcrWindows = "Windows";
        public const string OcrSidecar = "Sidecar";
        public const string LabelModeDirect = "Direct";
        public const string LabelModeTable = "Table";
        public const string LabelModeAuto = "Auto";
        public const string LabelModeNone = "None";
        public const string MissingStyleFlag = "Flag";
        public const string MissingStylePlainText = "PlainText";

        public string Title { get { return "Recorded Surveys"; } }
        public string AffectedCommands { get { return "FTFRECORD, FTFRECORDCHECK, FTFRECORDLABEL, FTFRECORDSOURCE, FTFRECORDREBUILD"; } }

        // ---- reading
        /// <summary>"Windows" (the built-in Windows OCR engine, reads PDF/TIFF/JPG/PNG) or "Sidecar" (a .ocr.json beside the document, from another OCR tool).</summary>
        [JsonProperty("ocrEngine")] public string OcrEngine { get; set; }
        [JsonProperty("ocrDpi")] public int OcrDpi { get; set; }
        /// <summary>Page rotations read, degrees, comma separated. Survey text runs at every angle; each pass catches another set.</summary>
        [JsonProperty("ocrRotations")] public string OcrRotations { get; set; }
        [JsonProperty("keepPageImages")] public bool KeepPageImages { get; set; }
        [JsonProperty("ocrLanguage")] public string OcrLanguage { get; set; }

        // ---- review
        /// <summary>Calls read below this confidence (0..1) must be reviewed before anything is built.</summary>
        [JsonProperty("reviewThreshold")] public double ReviewThreshold { get; set; }
        [JsonProperty("maxAlternatives")] public int MaxAlternatives { get; set; }
        /// <summary>Write the project (calls, review, build) as a .ftfrecord.json beside the drawing.</summary>
        [JsonProperty("writeProjectFile")] public bool WriteProjectFile { get; set; }

        // ---- geometry
        /// <summary>"Measured": build from measured values where the document gives them, record otherwise; "Record": always the record.</summary>
        [JsonProperty("buildFrom")] public string BuildFrom { get; set; }
        [JsonProperty("closureToleranceFt")] public double ClosureToleranceFt { get; set; }
        [JsonProperty("minimumClosurePrecision")] public double MinimumClosurePrecision { get; set; }
        [JsonProperty("distanceToleranceFt")] public double DistanceToleranceFt { get; set; }
        [JsonProperty("bearingToleranceSeconds")] public double BearingToleranceSeconds { get; set; }
        [JsonProperty("radiusToleranceFt")] public double RadiusToleranceFt { get; set; }
        [JsonProperty("arcToleranceFt")] public double ArcToleranceFt { get; set; }
        [JsonProperty("deltaToleranceSeconds")] public double DeltaToleranceSeconds { get; set; }
        [JsonProperty("chordToleranceFt")] public double ChordToleranceFt { get; set; }
        /// <summary>Course ends this close, feet, are the same corner (shared lot lines).</summary>
        [JsonProperty("sharedLineToleranceFt")] public double SharedLineToleranceFt { get; set; }
        /// <summary>Record and measured values further apart than these are reported (never reconciled).</summary>
        [JsonProperty("recordVsMeasuredDistanceWarnFt")] public double RecordVsMeasuredDistanceWarnFt { get; set; }
        [JsonProperty("recordVsMeasuredBearingWarnSeconds")] public double RecordVsMeasuredBearingWarnSeconds { get; set; }
        /// <summary>Draw each figure as separate lines and arcs (true) or one closed polyline per figure (false). Shared lines are always separate.</summary>
        [JsonProperty("separateCourses")] public bool SeparateCourses { get; set; }

        // ---- standards
        [JsonProperty("entities", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<RecordEntityStandard> Entities { get; set; }
        [JsonProperty("monuments", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<MonumentStandard> Monuments { get; set; }
        [JsonProperty("defaultObjectType")] public string DefaultObjectType { get; set; }
        [JsonProperty("lotObjectType")] public string LotObjectType { get; set; }
        /// <summary>Create a configured layer that is missing from the drawing. Off: missing layers are reported and their courses are not built.</summary>
        [JsonProperty("createMissingLayers")] public bool CreateMissingLayers { get; set; }
        [JsonProperty("drawMonuments")] public bool DrawMonuments { get; set; }
        [JsonProperty("tableLayer")] public string TableLayer { get; set; }
        [JsonProperty("tableStyle")] public string TableStyle { get; set; }

        // ---- labels
        [JsonProperty("useCivil3DLabels")] public bool UseCivil3DLabels { get; set; }
        /// <summary>"Flag": a missing label style is reported and those labels are withheld; "PlainText": plain text labels are placed and the style is still reported.</summary>
        [JsonProperty("whenLabelStyleMissing")] public string WhenLabelStyleMissing { get; set; }
        /// <summary>Direct, Table, Auto (table when the text does not fit along the course) or None.</summary>
        [JsonProperty("labelMode")] public string LabelMode { get; set; }
        [JsonProperty("textStyle")] public string TextStyle { get; set; }
        [JsonProperty("textHeightPlotted")] public double TextHeightPlotted { get; set; }
        [JsonProperty("offsetPlotted")] public double OffsetPlotted { get; set; }
        [JsonProperty("mask")] public bool Mask { get; set; }
        [JsonProperty("stacked")] public bool Stacked { get; set; }
        [JsonProperty("bearingSecondsDecimals")] public int BearingSecondsDecimals { get; set; }
        [JsonProperty("distanceDecimals")] public int DistanceDecimals { get; set; }
        [JsonProperty("footSymbol")] public bool FootSymbol { get; set; }
        [JsonProperty("bearingSpaces")] public bool BearingSpaces { get; set; }
        [JsonProperty("curveShowChord")] public bool CurveShowChord { get; set; }
        [JsonProperty("curveShowTangent")] public bool CurveShowTangent { get; set; }
        /// <summary>Label both values on a course that has record and measured: "{measured} (M)" over "{record} ({source})".</summary>
        [JsonProperty("labelRecordAndMeasured")] public bool LabelRecordAndMeasured { get; set; }
        [JsonProperty("measuredLabelFormat")] public string MeasuredLabelFormat { get; set; }
        [JsonProperty("recordLabelFormat")] public string RecordLabelFormat { get; set; }
        [JsonProperty("linePrefix")] public string LinePrefix { get; set; }
        [JsonProperty("curvePrefix")] public string CurvePrefix { get; set; }
        [JsonProperty("labelLots")] public bool LabelLots { get; set; }
        [JsonProperty("lotLabelFormat")] public string LotLabelFormat { get; set; }
        [JsonProperty("labelAreas")] public bool LabelAreas { get; set; }
        [JsonProperty("areaLabelFormat")] public string AreaLabelFormat { get; set; }
        [JsonProperty("lotTextLayer")] public string LotTextLayer { get; set; }
        [JsonProperty("lotTextHeightPlotted")] public double LotTextHeightPlotted { get; set; }

        public RecordSurveySettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            OcrEngine = OcrWindows;
            OcrDpi = 300;
            OcrRotations = "0,90,270,180";
            KeepPageImages = true;
            OcrLanguage = "en-US";

            ReviewThreshold = 0.85;
            MaxAlternatives = 2;
            WriteProjectFile = true;

            BuildFrom = BuildFromMeasured;
            ClosureToleranceFt = 0.02;
            MinimumClosurePrecision = 10000.0;
            DistanceToleranceFt = 0.01;
            BearingToleranceSeconds = 5.0;
            RadiusToleranceFt = 0.01;
            ArcToleranceFt = 0.02;
            DeltaToleranceSeconds = 10.0;
            ChordToleranceFt = 0.02;
            SharedLineToleranceFt = 0.05;
            RecordVsMeasuredDistanceWarnFt = 0.5;
            RecordVsMeasuredBearingWarnSeconds = 60.0;
            SeparateCourses = true;

            // FTF's generic names. V-PROP-BNDY-E, V-ALGN-CNTR-E and V-ESMT-E were read from the
            // office's real layer table; the rest follow the same NCS pattern. Every one is
            // checked against the drawing before use and reported when absent.
            Entities = new List<RecordEntityStandard>
            {
                new RecordEntityStandard { Name = "Boundary", Layer = "V-PROP-BNDY-E" },
                new RecordEntityStandard { Name = "Lot Line", Layer = "V-PROP-LOTL-E" },
                new RecordEntityStandard { Name = "Right of Way", Layer = "V-PROP-RWAY-E" },
                new RecordEntityStandard { Name = "Centerline", Layer = "V-ALGN-CNTR-E" },
                new RecordEntityStandard { Name = "Easement", Layer = "V-ESMT-E" },
                new RecordEntityStandard { Name = "Section Line", Layer = "V-PROP-SECT-E" },
                new RecordEntityStandard { Name = "Quarter Section", Layer = "V-PROP-QSEC-E" },
                new RecordEntityStandard { Name = "Tie Line", Layer = "V-PROP-TIE-E", Label = true },
                new RecordEntityStandard { Name = "Adjoiner", Layer = "V-PROP-ADJN-E", Label = false }
            };
            Monuments = new List<MonumentStandard>
            {
                new MonumentStandard { Status = "Found", Block = string.Empty, Layer = "V-CTRL-MONU-E" },
                new MonumentStandard { Status = "Set", Block = string.Empty, Layer = "V-CTRL-MONU-E" },
                new MonumentStandard { Status = "Calculated", Block = string.Empty, Layer = "V-CTRL-MONU-E" },
                new MonumentStandard { Status = "Unknown", Block = string.Empty, Layer = "V-CTRL-MONU-E" }
            };
            DefaultObjectType = "Boundary";
            LotObjectType = "Lot Line";
            CreateMissingLayers = false;
            DrawMonuments = true;
            TableLayer = "V-PROP-TABL-E";
            TableStyle = string.Empty;

            UseCivil3DLabels = true;
            WhenLabelStyleMissing = MissingStyleFlag;
            LabelMode = LabelModeAuto;
            TextStyle = string.Empty;
            TextHeightPlotted = 0.08;
            OffsetPlotted = 0.04;
            Mask = false;
            Stacked = true;
            BearingSecondsDecimals = 0;
            DistanceDecimals = 2;
            FootSymbol = true;
            BearingSpaces = true;
            CurveShowChord = true;
            CurveShowTangent = false;
            LabelRecordAndMeasured = true;
            MeasuredLabelFormat = "{value} (M)";
            RecordLabelFormat = "{value} ({source})";
            LinePrefix = "L";
            CurvePrefix = "C";
            LabelLots = true;
            LotLabelFormat = "LOT {lot}";
            LabelAreas = false;
            AreaLabelFormat = "{sqft} SQ. FT.";
            LotTextLayer = string.Empty;
            LotTextHeightPlotted = 0.12;
        }

        public RecordEntityStandard FindEntity(string name)
        {
            if (Entities == null || string.IsNullOrWhiteSpace(name)) return null;
            return Entities.FirstOrDefault(e => e != null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public MonumentStandard FindMonument(string status)
        {
            if (Monuments == null) return null;
            return Monuments.FirstOrDefault(m => m != null && string.Equals(m.Status, status, StringComparison.OrdinalIgnoreCase))
                   ?? Monuments.FirstOrDefault(m => m != null && string.Equals(m.Status, "Unknown", StringComparison.OrdinalIgnoreCase));
        }

        [JsonIgnore] public bool PreferMeasured { get { return !string.Equals(BuildFrom, BuildFromRecord, StringComparison.OrdinalIgnoreCase); } }

        /// <summary>The OCR pass rotations as numbers; a malformed list yields the default.</summary>
        public IList<double> RotationList()
        {
            var list = new List<double>();
            foreach (var part in (OcrRotations ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double d;
                if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) list.Add(d);
                else return new List<double> { 0, 90, 270, 180 };
            }
            if (list.Count == 0) list.Add(0);
            return list;
        }

        public void Validate(ICollection<string> problems)
        {
            const string p = "Recorded surveys: ";
            if (!string.Equals(OcrEngine, OcrWindows, StringComparison.OrdinalIgnoreCase) && !string.Equals(OcrEngine, OcrSidecar, StringComparison.OrdinalIgnoreCase))
                problems.Add(p + "OCR engine must be Windows or Sidecar.");
            if (OcrDpi < 72 || OcrDpi > 1200) problems.Add(p + "OCR resolution must be between 72 and 1200 dpi.");
            foreach (var part in (OcrRotations ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double d;
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) { problems.Add(p + "OCR rotations must be numbers separated by commas."); break; }
            }
            if (double.IsNaN(ReviewThreshold) || ReviewThreshold < 0 || ReviewThreshold > 1) problems.Add(p + "the review threshold must be between 0 and 1.");
            if (MaxAlternatives < 0 || MaxAlternatives > 10) problems.Add(p + "alternatives per value must be between 0 and 10.");
            if (!string.Equals(BuildFrom, BuildFromMeasured, StringComparison.OrdinalIgnoreCase) && !string.Equals(BuildFrom, BuildFromRecord, StringComparison.OrdinalIgnoreCase))
                problems.Add(p + "build from must be Measured or Record.");
            Require.Positive(ClosureToleranceFt, p + "closure tolerance must be greater than zero.", problems);
            Require.Positive(MinimumClosurePrecision, p + "minimum closure precision must be greater than zero.", problems);
            Require.Positive(DistanceToleranceFt, p + "distance tolerance must be greater than zero.", problems);
            Require.Positive(BearingToleranceSeconds, p + "bearing tolerance must be greater than zero.", problems);
            Require.Positive(RadiusToleranceFt, p + "radius tolerance must be greater than zero.", problems);
            Require.Positive(ArcToleranceFt, p + "arc tolerance must be greater than zero.", problems);
            Require.Positive(DeltaToleranceSeconds, p + "delta tolerance must be greater than zero.", problems);
            Require.Positive(ChordToleranceFt, p + "chord tolerance must be greater than zero.", problems);
            Require.Positive(SharedLineToleranceFt, p + "shared line tolerance must be greater than zero.", problems);
            Require.NotNegative(RecordVsMeasuredDistanceWarnFt, p + "record vs measured distance warning cannot be negative.", problems);
            Require.NotNegative(RecordVsMeasuredBearingWarnSeconds, p + "record vs measured bearing warning cannot be negative.", problems);

            if (Entities == null) Entities = new List<RecordEntityStandard>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Entities)
            {
                if (e == null) continue;
                if (string.IsNullOrWhiteSpace(e.Name)) { problems.Add(p + "every entity standard needs a name."); continue; }
                if (!seen.Add(e.Name.Trim())) problems.Add(p + e.Name + ": the entity name is used more than once.");
                if (e.TextHeightPlotted <= 0) problems.Add(p + e.Name + ": text height must be greater than zero.");
            }
            if (FindEntity(DefaultObjectType) == null) problems.Add(p + "the default object type \"" + DefaultObjectType + "\" is not an entity standard.");
            if (FindEntity(LotObjectType) == null) problems.Add(p + "the lot object type \"" + LotObjectType + "\" is not an entity standard.");
            if (Monuments == null) Monuments = new List<MonumentStandard>();
            foreach (var m in Monuments)
            {
                if (m == null) continue;
                if (string.IsNullOrWhiteSpace(m.Status)) problems.Add(p + "every monument standard needs a status.");
                if (m.ScalePlotted <= 0) problems.Add(p + "monument block scale must be greater than zero.");
            }

            if (!new[] { LabelModeDirect, LabelModeTable, LabelModeAuto, LabelModeNone }.Any(m => string.Equals(m, LabelMode, StringComparison.OrdinalIgnoreCase)))
                problems.Add(p + "label mode must be Direct, Table, Auto or None.");
            if (!new[] { MissingStyleFlag, MissingStylePlainText }.Any(m => string.Equals(m, WhenLabelStyleMissing, StringComparison.OrdinalIgnoreCase)))
                problems.Add(p + "when a label style is missing must be Flag or PlainText.");
            Require.Positive(TextHeightPlotted, p + "label text height must be greater than zero.", problems);
            Require.NotNegative(OffsetPlotted, p + "label offset cannot be negative.", problems);
            if (BearingSecondsDecimals < 0 || BearingSecondsDecimals > 3) problems.Add(p + "bearing seconds decimals must be between 0 and 3.");
            if (DistanceDecimals < 0 || DistanceDecimals > 4) problems.Add(p + "distance decimals must be between 0 and 4.");
            if (string.IsNullOrWhiteSpace(MeasuredLabelFormat) || !MeasuredLabelFormat.Contains("{value}")) problems.Add(p + "the measured label format must contain {value}.");
            if (string.IsNullOrWhiteSpace(RecordLabelFormat) || !RecordLabelFormat.Contains("{value}")) problems.Add(p + "the record label format must contain {value}.");
            Require.Positive(LotTextHeightPlotted, p + "lot text height must be greater than zero.", problems);
        }

        [JsonIgnore]
        public bool LabelsEnabled { get { return !string.Equals(LabelMode, LabelModeNone, StringComparison.OrdinalIgnoreCase); } }
        [JsonIgnore]
        public bool LabelTableMode { get { return string.Equals(LabelMode, LabelModeTable, StringComparison.OrdinalIgnoreCase); } }
        [JsonIgnore]
        public bool LabelAutoMode { get { return string.Equals(LabelMode, LabelModeAuto, StringComparison.OrdinalIgnoreCase); } }
        [JsonIgnore]
        public bool PlainTextWhenStyleMissing { get { return string.Equals(WhenLabelStyleMissing, MissingStylePlainText, StringComparison.OrdinalIgnoreCase); } }
    }
}
