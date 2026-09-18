using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Settings
{
    /// <summary>Where the exception report is written.</summary>
    public enum ReportLocation
    {
        /// <summary>Next to the drawing, named after it.</summary>
        BesideDrawing = 0,
        /// <summary>A fixed folder, named after the drawing.</summary>
        CustomFolder = 1
    }

    /// <summary>
    /// Every settings section implements this so the setup UI, validation and JSON
    /// round-trip can treat them uniformly. Adding a section for a new command means
    /// writing one class -- nothing else needs to change.
    /// </summary>
    public interface ISettingsSection
    {
        /// <summary>Short name shown in the setup window.</summary>
        string Title { get; }

        /// <summary>Commands this section changes the behaviour of.</summary>
        string AffectedCommands { get; }

        /// <summary>Adds a message per problem. Empty means valid.</summary>
        void Validate(ICollection<string> problems);

        /// <summary>Returns every value to its shipped default.</summary>
        void RestoreDefaults();
    }

    /// <summary>
    /// Numeric validation that NaN cannot slip through. Plain comparisons like
    /// "v &lt;= 0" are FALSE for NaN, so a hand-edited settings file containing NaN
    /// used to validate clean and then poison every downstream calculation.
    /// </summary>
    internal static class Require
    {
        public static void Positive(double v, string message, ICollection<string> problems)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) problems.Add(message);
        }

        public static void NotNegative(double v, string message, ICollection<string> problems)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0) problems.Add(message);
        }
    }

    // ------------------------------------------------------------------- general

    public sealed class GeneralSettings : ISettingsSection
    {
        public string Title { get { return "General"; } }
        public string AffectedCommands { get { return "All commands"; } }

        /// <summary>Drawing units per survey foot. 1.0 for feet, 0.3048 for metres.</summary>
        [JsonProperty("unitsPerFoot")]
        public double UnitsPerFoot { get; set; }

        [JsonProperty("reportLocation")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ReportLocation ReportLocation { get; set; }

        /// <summary>Used only when <see cref="ReportLocation"/> is CustomFolder.</summary>
        [JsonProperty("reportFolder")]
        public string ReportFolder { get; set; }

        /// <summary>Write the report even when there is nothing to report, so a stale
        /// report from an earlier run cannot be mistaken for the current one.</summary>
        [JsonProperty("writeReportWhenEmpty")]
        public bool WriteReportWhenEmpty { get; set; }

        /// <summary>Ask before a re-run erases what the previous run drew.</summary>
        [JsonProperty("confirmBeforeDelete")]
        public bool ConfirmBeforeDelete { get; set; }

        /// <summary>Project / profile layer mappings: an FTF default layer name and the
        /// layer the project standard uses instead. Checked before any production layer
        /// is created.</summary>
        [JsonProperty("layerMappings", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<LayerMapping> LayerMappings { get; set; }

        public GeneralSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            UnitsPerFoot = 1.0;
            ReportLocation = ReportLocation.BesideDrawing;
            ReportFolder = string.Empty;
            WriteReportWhenEmpty = true;
            ConfirmBeforeDelete = false;
            LayerMappings = new List<LayerMapping>();
        }

        public void Validate(ICollection<string> problems)
        {
            Require.Positive(UnitsPerFoot,
                "General: drawing units per survey foot must be greater than zero.", problems);

            if (ReportLocation == ReportLocation.CustomFolder &&
                string.IsNullOrWhiteSpace(ReportFolder))
                problems.Add("General: a report folder is required when the location is a custom folder.");

            if (!string.IsNullOrWhiteSpace(ReportFolder) &&
                ReportFolder.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
                problems.Add("General: the report folder is not a valid folder path.");

            if (LayerMappings == null) LayerMappings = new List<LayerMapping>();
            foreach (var m in LayerMappings)
                if (m == null || string.IsNullOrWhiteSpace(m.From) || string.IsNullOrWhiteSpace(m.To))
                {
                    problems.Add("General: every layer mapping needs both the FTF layer and the project layer.");
                    break;
                }
        }
    }

    /// <summary>One FTF default layer and the project standard layer used in its place.</summary>
    public sealed class LayerMapping
    {
        [JsonProperty("from")] public string From { get; set; }
        [JsonProperty("to")] public string To { get; set; }
    }

    // --------------------------------------------------------------------- trees

    public sealed class TreeSettings : ISettingsSection
    {
        public string Title { get { return "Trees"; } }
        public string AffectedCommands { get { return "FTFTREES"; } }

        /// <summary>Decimal places when a trunk size is written into a label. Display
        /// only -- geometry uses the unrounded average.</summary>
        [JsonProperty("trunkDecimals")]
        public int TrunkDecimals { get; set; }

        [JsonProperty("multiStemAverage")]
        [JsonConverter(typeof(StringEnumConverter))]
        public StemAverageMethod MultiStemAverage { get; set; }

        public TreeSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            TrunkDecimals = 0;
            MultiStemAverage = StemAverageMethod.Arithmetic;
        }

        public void Validate(ICollection<string> problems)
        {
            if (TrunkDecimals < 0 || TrunkDecimals > 6)
                problems.Add("Trees: trunk decimal places must be between 0 and 6.");
        }
    }

    // ---------------------------------------------------------------- drip lines

    public sealed class DripSettings : ISettingsSection
    {
        public string Title { get { return "Drip Lines"; } }
        public string AffectedCommands { get { return "FTFDRIP"; } }

        /// <summary>
        /// Trim overlapping canopies to their outer envelope. Used when a code rule
        /// does not state its own preference.
        ///
        /// The trimmer's tolerance and minimum sweep are deliberately not settings:
        /// they are numerical guards against zero-length slivers at tangency, not
        /// preferences, and tuning them produces degenerate arcs.
        /// </summary>
        [JsonProperty("unifyByDefault")]
        public bool UnifyByDefault { get; set; }

        /// <summary>Linetype for the drip circles and arcs -- typically a dashed
        /// office standard. Empty means the drip layer's own linetype. Loaded from
        /// acad.lin on first use if the drawing does not have it yet.</summary>
        [JsonProperty("linetype")]
        public string Linetype { get; set; }

        public DripSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            UnifyByDefault = true;
            Linetype = string.Empty;
        }

        public void Validate(ICollection<string> problems) { }
    }

    // -------------------------------------------------------------------- labels

    public sealed class LabelSettings : ISettingsSection
    {
        public string Title { get { return "Labels"; } }
        public string AffectedCommands { get { return "FTFLABELS"; } }

        /// <summary>
        /// Drawing text style used for labels. Empty means the drawing's current style.
        /// Collision boxes are measured from the text this style actually produces --
        /// there is no character-width estimate any more.
        /// </summary>
        [JsonProperty("textStyle")]
        public string TextStyle { get; set; }

        /// <summary>Text height as plotted. Ignored by AutoCAD if the chosen text
        /// style has a fixed height.</summary>
        [JsonProperty("textHeightPlotted")]
        public double TextHeightPlotted { get; set; }

        [JsonProperty("paddingPlotted")]
        public double PaddingPlotted { get; set; }

        [JsonProperty("baseOffsetPlotted")]
        public double BaseOffsetPlotted { get; set; }

        [JsonProperty("ringStepPlotted")]
        public double RingStepPlotted { get; set; }

        [JsonProperty("ringCount")]
        public int RingCount { get; set; }

        /// <summary>Draw a wipeout under each label.</summary>
        [JsonProperty("drawMask")]
        public bool DrawMask { get; set; }

        /// <summary>Layer for the wipeout. Empty means the label's own layer.</summary>
        [JsonProperty("maskLayer")]
        public string MaskLayer { get; set; }

        [JsonProperty("leaderArrowhead")]
        public bool LeaderArrowhead { get; set; }

        [JsonProperty("leaderArrowSizePlotted")]
        public double LeaderArrowSizePlotted { get; set; }

        public LabelSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            TextStyle = string.Empty;
            TextHeightPlotted = 0.08;
            PaddingPlotted = 0.02;
            BaseOffsetPlotted = 0.06;
            RingStepPlotted = 0.06;
            RingCount = 4;
            DrawMask = true;
            MaskLayer = string.Empty;
            LeaderArrowhead = false;
            LeaderArrowSizePlotted = 0.06;
        }

        public void Validate(ICollection<string> problems)
        {
            Require.Positive(TextHeightPlotted,
                "Labels: text height must be greater than zero.", problems);
            Require.NotNegative(PaddingPlotted,
                "Labels: padding cannot be negative.", problems);
            Require.NotNegative(BaseOffsetPlotted,
                "Labels: offset from point cannot be negative.", problems);
            if (RingCount < 1)
                problems.Add("Labels: ring count must be at least 1.");
            if (RingCount > 1)
                Require.Positive(RingStepPlotted,
                    "Labels: ring step must be greater than zero when ring count is above 1.",
                    problems);
            if (LeaderArrowhead)
                Require.Positive(LeaderArrowSizePlotted,
                    "Labels: leader arrow size must be greater than zero when arrowheads are on.",
                    problems);
        }
    }

    // ---------------------------------------------------------------- draw order

    public sealed class DrawOrderSettings : ISettingsSection
    {
        public string Title { get { return "Draw Order"; } }
        public string AffectedCommands { get { return "FTFORDER, FTFLABELS"; } }

        /// <summary>Layers that must stay legible: never overlapped by a label,
        /// banded above masks. Wildcards allowed (V-UTIL-*). Replace, not append:
        /// the constructor seeds defaults, and without this Newtonsoft would add a
        /// loaded file's entries AFTER the seeds, doubling the list on every load.</summary>
        [JsonProperty("protectedLayers", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> ProtectedLayers { get; set; }

        /// <summary>Linework a label may sit on and mask freely. Replace, not
        /// append -- see ProtectedLayers.</summary>
        [JsonProperty("maskableLayers", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> MaskableLayers { get; set; }

        public DrawOrderSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            ProtectedLayers = new List<string> { "V-SIGN", "V-UTIL-*", "V-STRC-*", "C-STRM-STRC" };
            MaskableLayers = new List<string> { "V-TREE-DRIP", "C-TOPO-*", "V-ROAD-*" };
        }

        public void Validate(ICollection<string> problems)
        {
            if (ProtectedLayers == null) ProtectedLayers = new List<string>();
            if (MaskableLayers == null) MaskableLayers = new List<string>();
        }
    }

    // ------------------------------------------------------------- line labels

    public sealed class LineLabelSettings : ISettingsSection
    {
        public string Title { get { return "Line Labels"; } }
        public string AffectedCommands { get { return "FTFLINELABELS"; } }

        /// <summary>Master switch for the linework labelling stage.</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        /// <summary>Text style. Empty means the drawing's current style.</summary>
        [JsonProperty("textStyle")]
        public string TextStyle { get; set; }

        [JsonProperty("textHeightPlotted")]
        public double TextHeightPlotted { get; set; }

        /// <summary>Layer for line labels when the feature does not name its own.</summary>
        [JsonProperty("defaultLabelLayer")]
        public string DefaultLabelLayer { get; set; }

        /// <summary>Text follows the line direction, or stays horizontal. Along-line
        /// text is always normalised so it can never read upside down.</summary>
        [JsonProperty("alignToLine")]
        public bool AlignToLine { get; set; }

        /// <summary>Spacing between repeated labels on long features, in survey feet.</summary>
        [JsonProperty("repeatIntervalFeet")]
        public double RepeatIntervalFeet { get; set; }

        /// <summary>Features shorter than this get no label at all, in survey feet.</summary>
        [JsonProperty("minLengthFeet")]
        public double MinLengthFeet { get; set; }

        /// <summary>Labels keep this far from the feature's ends, in survey feet.</summary>
        [JsonProperty("endClearanceFeet")]
        public double EndClearanceFeet { get; set; }

        /// <summary>Default side when neither the source coding nor the feature rule
        /// says: "OnLine", "Left" or "Right".</summary>
        [JsonProperty("defaultPlacement")]
        public string DefaultPlacement { get; set; }

        /// <summary>Perpendicular offset for left/right placement, in survey feet.</summary>
        [JsonProperty("sideOffsetFeet")]
        public double SideOffsetFeet { get; set; }

        /// <summary>Wipeout under each line label.</summary>
        [JsonProperty("drawMask")]
        public bool DrawMask { get; set; }

        public LineLabelSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            // The AUTOMATIC whole-drawing pass is opt-in: the finished survey shows
            // line annotation is context-dependent drafting, so the primary workflow
            // is the interactive FTFLABELLINE. This switch gates only the bulk
            // FTFLINELABELS stage; interactive placement is always available.
            Enabled = false;
            TextStyle = string.Empty;
            TextHeightPlotted = 0.08;
            DefaultLabelLayer = "V-LINE-TEXT";
            AlignToLine = true;
            RepeatIntervalFeet = 200.0;
            MinLengthFeet = 10.0;
            EndClearanceFeet = 5.0;
            DefaultPlacement = "OnLine";
            SideOffsetFeet = 1.0;
            DrawMask = true;
        }

        public void Validate(ICollection<string> problems)
        {
            Require.Positive(TextHeightPlotted,
                "Line labels: text height must be greater than zero.", problems);
            Require.Positive(RepeatIntervalFeet,
                "Line labels: repeat interval must be greater than zero.", problems);
            Require.NotNegative(MinLengthFeet,
                "Line labels: minimum feature length cannot be negative.", problems);
            Require.NotNegative(EndClearanceFeet,
                "Line labels: end clearance cannot be negative.", problems);
            if (string.IsNullOrWhiteSpace(DefaultLabelLayer))
                problems.Add("Line labels: a default label layer is required.");
            Require.NotNegative(SideOffsetFeet,
                "Line labels: side offset cannot be negative.", problems);
        }
    }

    // ------------------------------------------------------------ tags and table

    public sealed class TagSettings : ISettingsSection
    {
        public string Title { get { return "Tags & Table"; } }
        public string AffectedCommands { get { return "FTFTAGS, FTFTABLE"; } }

        /// <summary>Layer for the tags on the plan. Empty means the label's layer.</summary>
        [JsonProperty("tagLayer")]
        public string TagLayer { get; set; }

        [JsonProperty("tagTextStyle")]
        public string TagTextStyle { get; set; }

        [JsonProperty("tagTextHeightPlotted")]
        public double TagTextHeightPlotted { get; set; }

        /// <summary>Gap between the point and the tag, as plotted.</summary>
        [JsonProperty("tagOffsetPlotted")]
        public double TagOffsetPlotted { get; set; }

        /// <summary>
        /// First number issued for a prefix that has none yet. Existing numbers are
        /// always carried over regardless -- a tag on an issued plan never moves.
        /// </summary>
        [JsonProperty("startNumber")]
        public int StartNumber { get; set; }

        /// <summary>
        /// Draw a leader when a tag cannot sit on the first ring. A tag pushed clear of
        /// other work has nothing tying it to its point otherwise, and an unattached
        /// number on a plan is worse than no number.
        /// </summary>
        [JsonProperty("tagLeader")]
        public bool TagLeader { get; set; }

        [JsonProperty("tableTitle")]
        public string TableTitle { get; set; }

        [JsonProperty("tableLayer")]
        public string TableLayer { get; set; }

        [JsonProperty("tableTextHeightPlotted")]
        public double TableTextHeightPlotted { get; set; }

        /// <summary>Column keys, in order. Names match the TagTableColumn values.
        /// Replace, not append: the constructor seeds the default columns, and
        /// without this Newtonsoft would append a loaded file's columns to the
        /// seeds, doubling the schedule on every load.</summary>
        [JsonProperty("tableColumns", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> TableColumns { get; set; }

        public TagSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            TagLayer = "V-TREE-TAG";
            TagTextStyle = string.Empty;
            TagTextHeightPlotted = 0.08;
            TagOffsetPlotted = 0.04;
            StartNumber = 1;
            TagLeader = true;

            TableTitle = "TREE SCHEDULE";
            TableLayer = "V-TREE-TABL";
            TableTextHeightPlotted = 0.08;
            TableColumns = new List<string>
            {
                "Tag", "PointNumber", "Species", "Size", "StemCount", "DripRadius", "Notes"
            };
        }

        public void Validate(ICollection<string> problems)
        {
            Require.Positive(TagTextHeightPlotted,
                "Tags: tag text height must be greater than zero.", problems);
            Require.NotNegative(TagOffsetPlotted,
                "Tags: tag offset cannot be negative.", problems);
            if (StartNumber < 0)
                problems.Add("Tags: start number cannot be negative.");
            Require.Positive(TableTextHeightPlotted,
                "Tags: table text height must be greater than zero.", problems);
            if (TableColumns == null || TableColumns.Count == 0)
                problems.Add("Tags: the table needs at least one column.");
        }
    }

    // ------------------------------------------------------------- spot shots

    public sealed class SpotSettings : ISettingsSection
    {
        public string Title { get { return "Spot Shots"; } }
        public string AffectedCommands { get { return "FTFSPOT"; } }

        /// <summary>Spot text height as plotted -- 0.06 is Leroy 60.</summary>
        [JsonProperty("textPlotted")]
        public double TextPlotted { get; set; }

        /// <summary>The angle the spot text is drawn at, degrees.</summary>
        [JsonProperty("angleDegrees")]
        public double AngleDegrees { get; set; }

        /// <summary>Decimal places on the elevation.</summary>
        [JsonProperty("decimals")]
        public int Decimals { get; set; }

        /// <summary>Size of the X marker as plotted.</summary>
        [JsonProperty("markerPlotted")]
        public double MarkerPlotted { get; set; }

        [JsonProperty("markerLayer")]
        public string MarkerLayer { get; set; }

        [JsonProperty("textLayer")]
        public string TextLayer { get; set; }

        public SpotSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            TextPlotted = 0.06;
            AngleDegrees = 45.0;
            Decimals = 2;
            MarkerPlotted = 0.04;
            MarkerLayer = "V-TOPO-SPOT-PNTS-E";
            TextLayer = "V-TOPO-SPOT-TEXT-E";
        }

        public void Validate(ICollection<string> problems)
        {
            Require.Positive(TextPlotted,
                "Spot shots: text height must be greater than zero.", problems);
            Require.Positive(MarkerPlotted,
                "Spot shots: marker size must be greater than zero.", problems);
            if (Decimals < 0 || Decimals > 4)
                problems.Add("Spot shots: decimals must be between 0 and 4.");
            if (double.IsNaN(AngleDegrees) || double.IsInfinity(AngleDegrees))
                problems.Add("Spot shots: the text angle is not a number.");
            if (string.IsNullOrWhiteSpace(MarkerLayer))
                problems.Add("Spot shots: a marker layer is required.");
            if (string.IsNullOrWhiteSpace(TextLayer))
                problems.Add("Spot shots: a text layer is required.");
        }
    }

    // ------------------------------------------------------------------ sheets

    public sealed class SheetSettings : ISettingsSection
    {
        public string Title { get { return "Sheets"; } }
        public string AffectedCommands { get { return "FTFSHEETS"; } }

        /// <summary>Layer for the plot-window footprints drawn in model space.</summary>
        [JsonProperty("windowLayer")]
        public string WindowLayer { get; set; }

        /// <summary>Write each footprint's layout name inside its corner.</summary>
        [JsonProperty("drawWindowLabels")]
        public bool DrawWindowLabels { get; set; }

        /// <summary>Layer for the match lines between adjacent sheets.</summary>
        [JsonProperty("matchlineLayer")]
        public string MatchlineLayer { get; set; }

        /// <summary>Match line wording; {sheet} is the neighbouring layout's name.</summary>
        [JsonProperty("matchlineLabelFormat")]
        public string MatchlineLabelFormat { get; set; }

        /// <summary>Match line text height as plotted.</summary>
        [JsonProperty("matchlineTextPlotted")]
        public double MatchlineTextPlotted { get; set; }

        /// <summary>Paper size for the sheet planner, inches. 17 x 11 is ledger.</summary>
        [JsonProperty("sheetWidthIn")]
        public double SheetWidthIn { get; set; }

        [JsonProperty("sheetHeightIn")]
        public double SheetHeightIn { get; set; }

        /// <summary>Margin inside the paper edge, inches, all four sides.</summary>
        [JsonProperty("marginIn")]
        public double MarginIn { get; set; }

        /// <summary>Plot scale as survey feet per plotted inch -- 20 means 1"=20'.</summary>
        [JsonProperty("plotScaleFeetPerInch")]
        public double PlotScaleFeetPerInch { get; set; }

        /// <summary>How much neighbouring sheets overlap, percent of a sheet.</summary>
        [JsonProperty("overlapPercent")]
        public double OverlapPercent { get; set; }

        /// <summary>Width of the key map index diagram on each sheet, inches.</summary>
        [JsonProperty("keymapWidthIn")]
        public double KeymapWidthIn { get; set; }

        public SheetSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            WindowLayer = "V-SHEET-WIND";
            DrawWindowLabels = true;
            MatchlineLayer = "V-SHEET-MTCH";
            MatchlineLabelFormat = "MATCHLINE - SEE SHEET {sheet}";
            MatchlineTextPlotted = 0.10;
            SheetWidthIn = 17.0;
            SheetHeightIn = 11.0;
            MarginIn = 0.5;
            PlotScaleFeetPerInch = 20.0;
            OverlapPercent = 5.0;
            KeymapWidthIn = 2.0;
        }

        public void Validate(ICollection<string> problems)
        {
            if (string.IsNullOrWhiteSpace(WindowLayer))
                problems.Add("Sheets: a window layer is required.");
            if (string.IsNullOrWhiteSpace(MatchlineLayer))
                problems.Add("Sheets: a match line layer is required.");
            Require.Positive(MatchlineTextPlotted,
                "Sheets: match line text height must be greater than zero.", problems);
            Require.Positive(SheetWidthIn, "Sheets: paper width must be greater than zero.", problems);
            Require.Positive(SheetHeightIn, "Sheets: paper height must be greater than zero.", problems);
            Require.NotNegative(MarginIn, "Sheets: the margin cannot be negative.", problems);
            if (!double.IsNaN(MarginIn) &&
                MarginIn * 2 >= Math.Min(SheetWidthIn, SheetHeightIn))
                problems.Add("Sheets: the margins leave no printable area.");
            Require.Positive(PlotScaleFeetPerInch,
                "Sheets: the plot scale must be greater than zero.", problems);
            if (OverlapPercent < 0 || OverlapPercent > 45 || double.IsNaN(OverlapPercent))
                problems.Add("Sheets: overlap must be between 0 and 45 percent.");
            Require.Positive(KeymapWidthIn,
                "Sheets: the key map width must be greater than zero.", problems);
        }
    }

    // ------------------------------------------------------- cleanup and re-run

    public sealed class CleanupSettings : ISettingsSection
    {
        public string Title { get { return "Cleanup & Re-run"; } }
        public string AffectedCommands { get { return "FTFCLEAN, and every re-run"; } }

        /// <summary>
        /// How far a label may sit from the position stored in its XData before it
        /// counts as hand-placed. In drawing units. A hand-placed label is left alone
        /// on re-run and becomes an obstacle the automatic ones route around.
        /// </summary>
        [JsonProperty("movedTolerance")]
        public double MovedTolerance { get; set; }

        public CleanupSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            MovedTolerance = 0.001;
        }

        public void Validate(ICollection<string> problems)
        {
            Require.NotNegative(MovedTolerance,
                "Cleanup: hand-moved tolerance cannot be negative.", problems);
        }
    }
}
