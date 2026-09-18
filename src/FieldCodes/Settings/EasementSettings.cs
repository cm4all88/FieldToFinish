using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Settings
{
    /// <summary>Direct writes bearings and distances on the lines; Table tags them and
    /// lists them in a table; Auto writes them on the line where the text fits and puts
    /// the short ones in the table; None skips course labels.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum EasementLabelMode { Direct, Table, None, Auto }

    public sealed class EasementSettings : ISettingsSection
    {
        public string Title { get { return "Strip Easements"; } }
        public string AffectedCommands { get { return "STRIPEASEMENT, FTFEASEMENTCHECK"; } }

        [JsonProperty("boundaryLayer")] public string BoundaryLayer { get; set; }
        [JsonProperty("sidelineLayer")] public string SidelineLayer { get; set; }
        [JsonProperty("centerlineLayer")] public string CenterlineLayer { get; set; }
        [JsonProperty("hatchLayer")] public string HatchLayer { get; set; }
        [JsonProperty("dimensionLayer")] public string DimensionLayer { get; set; }
        [JsonProperty("textLayer")] public string TextLayer { get; set; }
        [JsonProperty("tableLayer")] public string TableLayer { get; set; }

        /// <summary>Outline of a temporary construction easement.</summary>
        [JsonProperty("temporaryLayer")] public string TemporaryLayer { get; set; }

        /// <summary>Hatch for a temporary construction easement; empty draws its outline only.</summary>
        [JsonProperty("temporaryHatchPattern")] public string TemporaryHatchPattern { get; set; }

        /// <summary>Clicked metes and bounds areas (construction areas) have their own layer and hatch.</summary>
        [JsonProperty("areaLayer")] public string AreaLayer { get; set; }
        [JsonProperty("areaHatchPattern")] public string AreaHatchPattern { get; set; }
        [JsonProperty("areaPurpose")] public string AreaPurpose { get; set; }
        [JsonProperty("areaTitleFormat")] public string AreaTitleFormat { get; set; }
        [JsonProperty("areaAreaFormat")] public string AreaAreaFormat { get; set; }
        [JsonProperty("areaLegalFormat")] public string AreaLegalFormat { get; set; }

        /// <summary>Purpose written in a temporary construction easement's title.</summary>
        [JsonProperty("temporaryPurpose")] public string TemporaryPurpose { get; set; }

        [JsonProperty("drawCenterline")] public bool DrawCenterline { get; set; }
        [JsonProperty("drawSidelines")] public bool DrawSidelines { get; set; }
        [JsonProperty("drawHatch")] public bool DrawHatch { get; set; }
        [JsonProperty("hatchPattern")] public string HatchPattern { get; set; }
        [JsonProperty("hatchScale")] public double HatchScale { get; set; }
        [JsonProperty("drawWidthDimensions")] public bool DrawWidthDimensions { get; set; }

        /// <summary>Dimension style for width dimensions. Empty means the drawing's
        /// CURRENT dimension style.</summary>
        [JsonProperty("dimensionStyleOverride")] public string DimensionStyleOverride { get; set; }

        [JsonProperty("labelMode")] public EasementLabelMode LabelMode { get; set; }
        /// <summary>Ask where the model-space line/curve table goes (false: beside the easement without asking).</summary>
        [JsonProperty("askTableLocation")] public bool AskTableLocation { get; set; }

        /// <summary>Label the centerline courses and ties, as an exhibit does; false labels
        /// the courses around the easement outline instead.</summary>
        [JsonProperty("labelCenterline")] public bool LabelCenterline { get; set; }

        /// <summary>Bearings with spaces, N 01°22'31" E; without, N01°22'31"E.</summary>
        [JsonProperty("bearingSpaces")] public bool BearingSpaces { get; set; }

        [JsonProperty("drawPointLabels")] public bool DrawPointLabels { get; set; }
        [JsonProperty("commencementLabel")] public string CommencementLabel { get; set; }
        [JsonProperty("beginningLabel")] public string BeginningLabel { get; set; }
        [JsonProperty("terminusLabel")] public string TerminusLabel { get; set; }

        /// <summary>The area sentence for a legal description.</summary>
        [JsonProperty("legalAreaFormat")] public string LegalAreaFormat { get; set; }
        [JsonProperty("linePrefix")] public string LinePrefix { get; set; }
        [JsonProperty("curvePrefix")] public string CurvePrefix { get; set; }
        [JsonProperty("curveShowChord")] public bool CurveShowChord { get; set; }
        [JsonProperty("curveShowTangent")] public bool CurveShowTangent { get; set; }

        [JsonProperty("bearingSecondsDecimals")] public int BearingSecondsDecimals { get; set; }
        [JsonProperty("distanceDecimals")] public int DistanceDecimals { get; set; }
        [JsonProperty("footSymbol")] public bool FootSymbol { get; set; }

        [JsonProperty("titleFormat")] public string TitleFormat { get; set; }
        [JsonProperty("defaultPurpose")] public string DefaultPurpose { get; set; }
        [JsonProperty("areaFormat")] public string AreaFormat { get; set; }
        [JsonProperty("acresFormat")] public string AcresFormat { get; set; }
        [JsonProperty("areaSquareFeetDecimals")] public int AreaSquareFeetDecimals { get; set; }
        [JsonProperty("acresDecimals")] public int AcresDecimals { get; set; }

        [JsonProperty("textStyle")] public string TextStyle { get; set; }
        [JsonProperty("textHeightPlotted")] public double TextHeightPlotted { get; set; }
        [JsonProperty("tableStyle")] public string TableStyle { get; set; }

        /// <summary>How far the stated (rounded) legal courses may land from the CAD corners, feet,
        /// before the legal draft is flagged as not reproducing the drawing.</summary>
        [JsonProperty("reproductionToleranceFt")] public double ReproductionToleranceFt { get; set; }

        /// <summary>A closed course description closing worse than 1:N is flagged.</summary>
        [JsonProperty("minimumClosurePrecision")] public double MinimumClosurePrecision { get; set; }

        /// <summary>Closure and matching tolerance, feet.</summary>
        [JsonProperty("toleranceFt")] public double ToleranceFt { get; set; }

        public EasementSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            BoundaryLayer = "V-ESMT-E";
            SidelineLayer = "V-ESMT-E";
            CenterlineLayer = "V-ESMT-CNTR-E";
            HatchLayer = "V-ESMT-PATT-E";
            DimensionLayer = "V-ESMT-DIMS-E";
            TextLayer = "V-ESMT-TEXT-E";
            TableLayer = "V-ESMT-TABL-E";
            TemporaryLayer = "V-ESMT-TEMP-E";
            TemporaryHatchPattern = string.Empty;
            TemporaryPurpose = "TEMPORARY CONSTRUCTION";
            AreaLayer = "V-ESMT-CONS-E";
            AreaHatchPattern = "ANSI37";
            AreaPurpose = "TEMPORARY CONSTRUCTION";
            AreaTitleFormat = "{purpose} AREA";
            AreaAreaFormat = "APPROX. {purpose} AREA = {sqft} SF";
            AreaLegalFormat = "SAID {purpose} AREA CONTAINING {sqft} SQUARE FEET, MORE OR LESS.";

            DrawCenterline = false;
            DrawSidelines = false;
            DrawHatch = true;
            HatchPattern = "ANSI31";
            HatchScale = 1.0;
            DrawWidthDimensions = true;
            DimensionStyleOverride = string.Empty;

            LabelMode = EasementLabelMode.Auto;
            AskTableLocation = true;
            LabelCenterline = true;
            BearingSpaces = false;
            DrawPointLabels = true;
            CommencementLabel = "POINT OF COMMENCEMENT";
            BeginningLabel = "POINT OF BEGINNING";
            TerminusLabel = "POINT OF TERMINUS";
            LegalAreaFormat = "SAID {purpose} EASEMENT CONTAINING {sqft} SQUARE FEET, MORE OR LESS.";
            LinePrefix = "L";
            CurvePrefix = "C";
            CurveShowChord = true;
            CurveShowTangent = false;

            BearingSecondsDecimals = 0;
            DistanceDecimals = 2;
            FootSymbol = true;

            TitleFormat = "{width}' WIDE {purpose} EASEMENT";
            DefaultPurpose = "UTILITY";
            AreaFormat = "APPROX. {purpose} EASEMENT AREA = {sqft} SF";
            AcresFormat = string.Empty;
            AreaSquareFeetDecimals = 0;
            AcresDecimals = 3;

            TextStyle = string.Empty;
            TextHeightPlotted = 0.08;
            TableStyle = string.Empty;
            ToleranceFt = 0.005;
            ReproductionToleranceFt = 0.02;
            MinimumClosurePrecision = 10000;
        }

        public void Validate(ICollection<string> problems)
        {
            foreach (var layer in new[] { BoundaryLayer, SidelineLayer, CenterlineLayer, HatchLayer,
                                          DimensionLayer, TextLayer, TableLayer, TemporaryLayer, AreaLayer })
                if (string.IsNullOrWhiteSpace(layer))
                {
                    problems.Add("Easements: every easement layer needs a name.");
                    break;
                }
            Require.Positive(TextHeightPlotted, "Easements: text height must be greater than zero.", problems);
            Require.Positive(ToleranceFt, "Easements: the tolerance must be greater than zero.", problems);
            Require.Positive(HatchScale, "Easements: the hatch scale must be greater than zero.", problems);
            if (DistanceDecimals < 0 || DistanceDecimals > 4)
                problems.Add("Easements: distance decimals must be between 0 and 4.");
            if (BearingSecondsDecimals < 0 || BearingSecondsDecimals > 2)
                problems.Add("Easements: bearing seconds decimals must be between 0 and 2.");
            if (AreaSquareFeetDecimals < 0 || AreaSquareFeetDecimals > 2 || AcresDecimals < 0 || AcresDecimals > 4)
                problems.Add("Easements: area precision is out of range.");
            if (string.IsNullOrWhiteSpace(TitleFormat))
                problems.Add("Easements: a title format is required.");
            if (string.IsNullOrWhiteSpace(AreaFormat) || !AreaFormat.Contains("{sqft}"))
                problems.Add("Easements: the area line needs {sqft}.");
        }
    }
}
