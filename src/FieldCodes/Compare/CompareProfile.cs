using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Compare
{
    /// <summary>Case-insensitive DOS-style wildcards (* and ?), anchored, compiled once.</summary>
    public static class Wildcard
    {
        private static readonly Dictionary<string, Regex> Cache = new Dictionary<string, Regex>(StringComparer.Ordinal);

        public static bool Matches(string pattern, string text)
        {
            if (string.IsNullOrEmpty(pattern) || text == null) return false;
            Regex regex;
            lock (Cache)
            {
                if (!Cache.TryGetValue(pattern, out regex))
                {
                    var escaped = "^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    regex = new Regex(escaped, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    Cache[pattern] = regex;
                }
            }
            return regex.IsMatch(text.Trim());
        }

        public static bool MatchesAny(IEnumerable<string> patterns, string text)
        {
            if (patterns == null || text == null) return false;
            foreach (var p in patterns) if (Matches(p, text)) return true;
            return false;
        }
    }

    /// <summary>Matching tolerances. Each category may override the ones that apply to it.</summary>
    public sealed class CompareTolerances
    {
        /// <summary>Horizontal distance within which two point features may be the same object.</summary>
        [JsonProperty("pointFeet")] public double PointFeet { get; set; }

        /// <summary>Perpendicular offset within which linework is "on" the consultant's line.</summary>
        [JsonProperty("lineOffsetFeet")] public double LineOffsetFeet { get; set; }

        /// <summary>Direction difference (modulo 180) within which overlapping lines run together.</summary>
        [JsonProperty("angleDegrees")] public double AngleDegrees { get; set; }

        /// <summary>An uncovered run INSIDE covered linework up to this long is a gap in the
        /// consultant's drafting (a driveway break, a dropped vertex), not missing survey.</summary>
        [JsonProperty("gapFeet")] public double GapFeet { get; set; }

        /// <summary>No plotting sliver shorter than this is ever created.</summary>
        [JsonProperty("minUncoveredFeet")] public double MinUncoveredFeet { get; set; }

        /// <summary>Elevations closer than this agree.</summary>
        [JsonProperty("elevationFeet")] public double ElevationFeet { get; set; }
        /// <summary>A counterpart found within pointFeet but farther away than this is a
        /// discrepancy: ours keeps plotting, gets a revision cloud and a note in the drawing,
        /// and is listed in the report.</summary>
        [JsonProperty("pointDiscrepancyFeet")] public double PointDiscrepancyFeet { get; set; }

        /// <summary>How far from a feature its annotation may sit and still belong to it.</summary>
        [JsonProperty("annotationSearchFeet")] public double AnnotationSearchFeet { get; set; }

        /// <summary>Covered fraction at or above which a line counts as fully represented.</summary>
        [JsonProperty("fullCoverageFraction")] public double FullCoverageFraction { get; set; }

        /// <summary>Spacing of the samples taken along survey linework.</summary>
        [JsonProperty("sampleStepFeet")] public double SampleStepFeet { get; set; }

        /// <summary>A match whose typical offset exceeds this fraction of the tolerance is
        /// shown for review rather than accepted automatically.</summary>
        [JsonProperty("reviewOffsetFraction")] public double ReviewOffsetFraction { get; set; }

        /// <summary>Automatic results at or above this confidence may be applied without a click.</summary>
        [JsonProperty("autoAcceptConfidence")] public double AutoAcceptConfidence { get; set; }

        /// <summary>Maximum chord deviation when the reader samples arcs and splines.</summary>
        [JsonProperty("chordToleranceFeet")] public double ChordToleranceFeet { get; set; }

        /// <summary>An uncovered stretch is absorbed as a sliver or a drafting gap only while
        /// our line stays within this many tolerances of theirs. A short stretch that swings
        /// farther away is a real difference and keeps plotting however short it is.</summary>
        [JsonProperty("sliverNearFactor")] public double SliverNearFactor { get; set; }

        public CompareTolerances()
        {
            PointFeet = 2.0;
            LineOffsetFeet = 1.0;
            AngleDegrees = 15.0;
            GapFeet = 5.0;
            MinUncoveredFeet = 3.0;
            ElevationFeet = 0.5;
            PointDiscrepancyFeet = 0.5;
            AnnotationSearchFeet = 15.0;
            FullCoverageFraction = 0.97;
            SampleStepFeet = 1.0;
            ReviewOffsetFraction = 0.6;
            AutoAcceptConfidence = 0.75;
            ChordToleranceFeet = 0.05;
            SliverNearFactor = 2.0;
        }

        public void Validate(string prefix, ICollection<string> problems)
        {
            if (PointDiscrepancyFeet <= 0) problems.Add(prefix + "pointDiscrepancyFeet must be positive.");
            if (PointDiscrepancyFeet > PointFeet) problems.Add(prefix + "pointDiscrepancyFeet cannot exceed pointFeet (the search radius).");
            Check(PointFeet, prefix + "pointFeet must be positive", problems);
            Check(LineOffsetFeet, prefix + "lineOffsetFeet must be positive", problems);
            Check(AngleDegrees, prefix + "angleDegrees must be positive", problems);
            Check(SampleStepFeet, prefix + "sampleStepFeet must be positive", problems);
            if (double.IsNaN(GapFeet) || GapFeet < 0) problems.Add(prefix + "gapFeet must not be negative");
            if (double.IsNaN(MinUncoveredFeet) || MinUncoveredFeet < 0) problems.Add(prefix + "minUncoveredFeet must not be negative");
            if (double.IsNaN(FullCoverageFraction) || FullCoverageFraction <= 0.5 || FullCoverageFraction > 1.0) problems.Add(prefix + "fullCoverageFraction must be between 0.5 and 1");
            if (double.IsNaN(AutoAcceptConfidence) || AutoAcceptConfidence < 0.5 || AutoAcceptConfidence > 1.0) problems.Add(prefix + "autoAcceptConfidence must be between 0.5 and 1");
            if (double.IsNaN(SliverNearFactor) || SliverNearFactor < 1.0) problems.Add(prefix + "sliverNearFactor must be at least 1");
        }

        private static void Check(double v, string message, ICollection<string> problems)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) problems.Add(message);
        }
    }

    /// <summary>How one side of the comparison expresses a category: codes, layers, blocks, words.</summary>
    public sealed class SourceMapping
    {
        /// <summary>Survey / description codes, exact (case-insensitive, trailing digits ignored).</summary>
        [JsonProperty("codes")] public List<string> Codes { get; set; }
        /// <summary>Layer wildcards.</summary>
        [JsonProperty("layers")] public List<string> Layers { get; set; }
        /// <summary>Block name wildcards.</summary>
        [JsonProperty("blocks")] public List<string> Blocks { get; set; }
        /// <summary>Whole-word phrases found in descriptions, attributes or labels.</summary>
        [JsonProperty("keywords")] public List<string> Keywords { get; set; }

        public SourceMapping()
        {
            Codes = new List<string>();
            Layers = new List<string>();
            Blocks = new List<string>();
            Keywords = new List<string>();
        }

        public static SourceMapping Of(string[] codes, string[] layers, string[] blocks, string[] keywords)
        {
            var m = new SourceMapping();
            if (codes != null) m.Codes.AddRange(codes);
            if (layers != null) m.Layers.AddRange(layers);
            if (blocks != null) m.Blocks.AddRange(blocks);
            if (keywords != null) m.Keywords.AddRange(keywords);
            return m;
        }

        [JsonIgnore] public bool IsEmpty { get { return Codes.Count + Layers.Count + Blocks.Count + Keywords.Count == 0; } }
    }

    /// <summary>One physical feature type, as both sides draw it, with its tolerances.</summary>
    public sealed class CategoryRule
    {
        [JsonProperty("key")] public string Key { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("kind")] public FeatureKind Kind { get; set; }
        /// <summary>STORM, SANITARY, WATER, POWER, COMM, GAS, SURFACE, TOPO, SITE, OTHER.</summary>
        [JsonProperty("system")] public string System { get; set; }
        [JsonProperty("enabled")] public bool Enabled { get; set; }

        [JsonProperty("pointToleranceFeet")] public double? PointToleranceFeet { get; set; }
        /// <summary>Beyond this (but within pointToleranceFeet) a counterpart is a discrepancy.
        /// Unset: the profile's tolerances.pointDiscrepancyFeet, never more than the search radius.</summary>
        [JsonProperty("pointDiscrepancyFeet")] public double? PointDiscrepancyFeet { get; set; }
        [JsonProperty("lineOffsetToleranceFeet")] public double? LineOffsetToleranceFeet { get; set; }
        [JsonProperty("angleToleranceDegrees")] public double? AngleToleranceDegrees { get; set; }
        [JsonProperty("elevationToleranceFeet")] public double? ElevationToleranceFeet { get; set; }

        [JsonProperty("survey")] public SourceMapping Survey { get; set; }
        [JsonProperty("consultant")] public SourceMapping Consultant { get; set; }

        public CategoryRule()
        {
            Enabled = true;
            System = "OTHER";
            Survey = new SourceMapping();
            Consultant = new SourceMapping();
        }

        public SourceMapping For(CompareSource source) { return source == CompareSource.Survey ? Survey : Consultant; }

        [JsonIgnore]
        public bool IsUtility
        {
            get
            {
                switch ((System ?? string.Empty).ToUpperInvariant())
                {
                    case "STORM": case "SANITARY": case "WATER": case "POWER": case "COMM": case "GAS": return true;
                    default: return false;
                }
            }
        }
    }

    /// <summary>The nonplot layers comparison moves duplicate survey objects to. Fixed set,
    /// never one per run, so re-running cannot accumulate layers.</summary>
    public sealed class NonplotLayerNames
    {
        public const string ModeSuffix = "Suffix";
        public const string ModeFixed = "Fixed";

        /// <summary>"Suffix" (default): an object goes to its own layer's name plus the suffix
        /// (V-UTIL-POWR-POLE to V-UTIL-POWR-POLE-NP), created no-plot where missing with the
        /// source layer's colour. "Fixed": the five named layers below, by kind.</summary>
        [JsonProperty("mode")] public string Mode { get; set; }
        [JsonProperty("suffix")] public string Suffix { get; set; }
        /// <summary>A COGO point whose point style pins the marker to a fixed layer would keep
        /// plotting after a layer change alone. True: it gets the comparison's own point style
        /// (marker and label following the point's layer). False: it is left for a person,
        /// reported as NOT changed with the reason.</summary>
        [JsonProperty("restyleFixedMarkerPoints")] public bool RestyleFixedMarkerPoints { get; set; }
        [JsonProperty("point")] public string Point { get; set; }
        [JsonProperty("line")] public string Line { get; set; }
        [JsonProperty("block")] public string Block { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("utility")] public string Utility { get; set; }
        /// <summary>AutoCAD colour index for the nonplot layers (8 = dark grey).</summary>
        [JsonProperty("colorIndex")] public short ColorIndex { get; set; }

        public NonplotLayerNames()
        {
            Mode = ModeSuffix;
            Suffix = "-NP";
            RestyleFixedMarkerPoints = true;
            Point = "PMX-COMP-NP-POINT";
            Line = "PMX-COMP-NP-LINE";
            Block = "PMX-COMP-NP-BLOCK";
            Text = "PMX-COMP-NP-TEXT";
            Utility = "PMX-COMP-NP-UTILITY";
            ColorIndex = 8;
        }

        [JsonIgnore] public IEnumerable<string> All { get { return new[] { Point, Line, Block, Text, Utility }; } }

        [JsonIgnore] public bool UseSuffix { get { return !string.Equals(Mode, ModeFixed, StringComparison.OrdinalIgnoreCase); } }

        /// <summary>The nonplot layer for an object on sourceLayer whose fixed-mode layer
        /// would be fixedName.</summary>
        public string For(string sourceLayer, string fixedName)
        {
            if (!UseSuffix || string.IsNullOrWhiteSpace(sourceLayer)) return fixedName;
            if (IsNonplotName(sourceLayer)) return sourceLayer;
            return sourceLayer + Suffix;
        }

        /// <summary>One of the five fixed names.</summary>
        public bool Contains(string layer)
        {
            return All.Any(l => string.Equals(l, layer, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A name the comparison's nonplot scheme would produce: a fixed name, or
        /// in suffix mode anything ending with the suffix.</summary>
        public bool IsNonplotName(string layer)
        {
            if (string.IsNullOrEmpty(layer)) return false;
            if (Contains(layer)) return true;
            return UseSuffix && !string.IsNullOrEmpty(Suffix) && layer.Length > Suffix.Length && layer.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);
        }

        [JsonIgnore] public string Describe
        {
            get { return UseSuffix ? "<layer>" + Suffix + " (suffix mode)" : string.Join(", ", All.ToArray()) + " (fixed mode)"; }
        }
    }

    /// <summary>How annotation text is read into comparable facts.</summary>
    public sealed class AnnotationRules
    {
        [JsonProperty("rimKeywords")] public List<string> RimKeywords { get; set; }
        [JsonProperty("invertKeywords")] public List<string> InvertKeywords { get; set; }
        [JsonProperty("materials")] public List<string> Materials { get; set; }
        /// <summary>Words that mean the same thing on either side: CONC = CONCRETE.</summary>
        [JsonProperty("synonyms")] public List<List<string>> Synonyms { get; set; }
        /// <summary>Compare text-only annotation (no parsed facts) by normalised wording.</summary>
        [JsonProperty("compareTextWhenNoFacts")] public bool CompareTextWhenNoFacts { get; set; }

        public AnnotationRules()
        {
            RimKeywords = new List<string> { "RIM", "RIM EL", "RIM ELEV", "TOP", "TC", "GRATE" };
            InvertKeywords = new List<string> { "IE", "INV", "I.E.", "INVERT", "FL" };
            Materials = new List<string> { "PVC", "CONC", "CONCRETE", "RCP", "CMP", "DI", "DIP", "HDPE", "PE", "ADS", "CPP", "VCP", "CI", "STEEL", "STL", "AC", "CPEP", "CLAY", "COPPER" };
            Synonyms = new List<List<string>>
            {
                new List<string> { "CONC", "CONCRETE" },
                new List<string> { "ASPH", "ASPHALT", "AC" },
                new List<string> { "EOP", "EDGE OF PAVEMENT", "EP" },
                new List<string> { "EOC", "EDGE OF CONCRETE" },
                new List<string> { "GRVL", "GRAVEL" },
                new List<string> { "CONC WALK", "SIDEWALK", "CW", "CONCRETE WALK" }
            };
            CompareTextWhenNoFacts = true;
        }
    }

    /// <summary>What to do with survey linework that runs beyond the comparison limits.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum OutsideLimitsAction { Review, KeepPlotting, Nonplot }

    public sealed class CompareBoundary
    {
        /// <summary>Closed polygon, in drawing coordinates. Empty means no explicit limits.</summary>
        [JsonProperty("points")] public List<double[]> Points { get; set; }
        [JsonProperty("outsideLimitsAction")] public OutsideLimitsAction OutsideLimitsAction { get; set; }
        /// <summary>With no polygon, treat the consultant data's own extent (plus margin) as
        /// the limits, so survey continuing past their last sheet is not "missing".</summary>
        [JsonProperty("useConsultantExtentWhenNoBoundary")] public bool UseConsultantExtentWhenNoBoundary { get; set; }
        [JsonProperty("consultantExtentMarginFeet")] public double ConsultantExtentMarginFeet { get; set; }

        public CompareBoundary()
        {
            Points = new List<double[]>();
            OutsideLimitsAction = OutsideLimitsAction.Review;
            UseConsultantExtentWhenNoBoundary = true;
            ConsultantExtentMarginFeet = 10.0;
        }

        [JsonIgnore] public bool HasPolygon { get { return Points != null && Points.Count >= 3; } }

        [JsonIgnore]
        public List<CPoint> Polygon
        {
            get { return (Points ?? new List<double[]>()).Where(p => p != null && p.Length >= 2).Select(p => new CPoint(p[0], p[1])).ToList(); }
        }

        public void SetPolygon(IEnumerable<CPoint> points)
        {
            Points = (points ?? Enumerable.Empty<CPoint>()).Select(p => new[] { p.X, p.Y }).ToList();
        }
    }

    public sealed class UnitRules
    {
        /// <summary>Drawing units per foot in the consultant file when it is read as a separate
        /// file and its units differ from the survey. Null means the units must already agree;
        /// the comparison refuses to guess a scale.</summary>
        [JsonProperty("consultantUnitsPerFoot")] public double? ConsultantUnitsPerFoot { get; set; }

        /// <summary>An explicitly verified shift and rotation applied to a consultant FILE
        /// whose coordinate system differs from the survey's (never to attached xrefs, which
        /// the drafter already placed). Zero means none.</summary>
        [JsonProperty("consultantShiftX")] public double ConsultantShiftX { get; set; }
        [JsonProperty("consultantShiftY")] public double ConsultantShiftY { get; set; }
        [JsonProperty("consultantRotationDegrees")] public double ConsultantRotationDegrees { get; set; }

        [JsonIgnore] public bool HasTransform { get { return Math.Abs(ConsultantShiftX) > 1e-9 || Math.Abs(ConsultantShiftY) > 1e-9 || Math.Abs(ConsultantRotationDegrees) > 1e-9; } }
    }

    public sealed class QcThresholds
    {
        [JsonProperty("blockOnNeedsReview")] public bool BlockOnNeedsReview { get; set; }
        [JsonProperty("blockOnDiscrepancies")] public bool BlockOnDiscrepancies { get; set; }
        [JsonProperty("blockOnNotEvaluated")] public bool BlockOnNotEvaluated { get; set; }
        [JsonProperty("blockOnIncompleteSources")] public bool BlockOnIncompleteSources { get; set; }

        public QcThresholds()
        {
            BlockOnNeedsReview = true;
            BlockOnDiscrepancies = true;
            BlockOnNotEvaluated = true;
            BlockOnIncompleteSources = true;
        }
    }

    /// <summary>How discrepancies are marked in the drawing: a revision cloud around the
    /// object and a note beside it, on a plotting review layer, removed by restore.</summary>
    public sealed class ReviewMarks
    {
        [JsonProperty("drawClouds")] public bool DrawClouds { get; set; }
        [JsonProperty("layer")] public string Layer { get; set; }
        [JsonProperty("colorIndex")] public short ColorIndex { get; set; }
        /// <summary>Note text height in drawing feet (2 ft reads at 1" = 20' to 40').</summary>
        [JsonProperty("textHeightFeet")] public double TextHeightFeet { get; set; }
        /// <summary>Clearance between the object and its cloud, in feet.</summary>
        [JsonProperty("marginFeet")] public double MarginFeet { get; set; }

        public ReviewMarks()
        {
            DrawClouds = true;
            Layer = "PMX-COMP-REVIEW";
            ColorIndex = 1;
            TextHeightFeet = 2.0;
            MarginFeet = 3.0;
        }
    }

    /// <summary>How the aerial base map is cut back inside our limits of survey and how the
    /// seams between it and our linework are checked.</summary>
    public sealed class TrimRules
    {
        /// <summary>Layer wildcards of the closed polylines that mark our limits of survey,
        /// looked for inside the survey xrefs (or picked in the host drawing).</summary>
        [JsonProperty("limitsLayers")] public List<string> LimitsLayers { get; set; }
        /// <summary>How far from a cut to look for our continuing line.</summary>
        [JsonProperty("seamSearchFeet")] public double SeamSearchFeet { get; set; }
        /// <summary>Horizontal offset at the seam beyond which it is a discrepancy.</summary>
        [JsonProperty("seamOffsetFeet")] public double SeamOffsetFeet { get; set; }
        /// <summary>Elevation difference at the seam beyond which it is a discrepancy.</summary>
        [JsonProperty("seamElevationFeet")] public double SeamElevationFeet { get; set; }
        /// <summary>Direction difference beyond which the continuation is doubtful (review).</summary>
        [JsonProperty("seamAngleDegrees")] public double SeamAngleDegrees { get; set; }
        /// <summary>An outside remnant shorter than this is not worth its own piece: it goes
        /// nonplot with the rest of the line and the report says so.</summary>
        [JsonProperty("minPieceFeet")] public double MinPieceFeet { get; set; }
        [JsonProperty("drawClouds")] public bool DrawClouds { get; set; }

        public TrimRules()
        {
            LimitsLayers = new List<string> { "V-SURV-LIMIT*", "*-LIMITS*", "*LIMIT-OF-SURVEY*", "*SURVEY-LIMIT*", "*SURV-BNDY*" };
            SeamSearchFeet = 2.0;
            SeamOffsetFeet = 0.5;
            SeamElevationFeet = 0.5;
            SeamAngleDegrees = 20.0;
            MinPieceFeet = 0.5;
            DrawClouds = true;
        }
    }

    public sealed class IgnoreRules
    {
        /// <summary>Survey layers left out of the comparison entirely (never touched).</summary>
        [JsonProperty("surveyLayers")] public List<string> SurveyLayers { get; set; }
        /// <summary>Consultant layers that are not existing-conditions data (borders, notes).</summary>
        [JsonProperty("consultantLayers")] public List<string> ConsultantLayers { get; set; }
        [JsonProperty("blocks")] public List<string> Blocks { get; set; }

        public IgnoreRules()
        {
            SurveyLayers = new List<string> { "DEFPOINTS", "0", "*-VPRT*", "*TTLB*", "*BORDER*", "*NPLT*", "*NOPLOT*" };
            ConsultantLayers = new List<string> { "DEFPOINTS", "*-VPRT*", "*TTLB*", "*TITLE*", "*BORDER*", "*NPLT*", "*NOPLOT*", "*SHEET*", "*VIEWPORT*", "*MATCHLINE*", "*GRID*" };
            Blocks = new List<string>();
        }
    }

    /// <summary>
    /// A comparison profile: how one consultant's CAD standard maps to ours, with the
    /// tolerances, nonplot layers, limits and QC thresholds for the sites that share it.
    /// Plain JSON, edited by people, saved in the user's FieldToFinish profiles folder.
    /// </summary>
    public sealed class CompareProfile
    {
        public const string Extension = ".json";
        public const string FolderName = "compare-profiles";
        public const string DefaultName = "Default";
        public const string BesideDrawingFileName = "ftf-compare-profile.json";

        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
        [JsonProperty("consultant")] public string Consultant { get; set; }
        [JsonProperty("notes")] public string Notes { get; set; }
        [JsonProperty("tolerances")] public CompareTolerances Tolerances { get; set; }
        [JsonProperty("categories")] public List<CategoryRule> Categories { get; set; }
        [JsonProperty("nonplotLayers")] public NonplotLayerNames NonplotLayers { get; set; }
        [JsonProperty("review")] public ReviewMarks Review { get; set; }
        [JsonProperty("trim")] public TrimRules Trim { get; set; }
        [JsonProperty("annotation")] public AnnotationRules Annotation { get; set; }
        [JsonProperty("boundary")] public CompareBoundary Boundary { get; set; }
        [JsonProperty("units")] public UnitRules Units { get; set; }
        [JsonProperty("qc")] public QcThresholds Qc { get; set; }
        [JsonProperty("ignore")] public IgnoreRules Ignore { get; set; }

        /// <summary>Consultant blocks whose CONTENTS are linework (a curb drawn as a block):
        /// they are opened and their lines read. Every other block is a point feature.</summary>
        [JsonProperty("explodeBlocks")] public List<string> ExplodeBlocks { get; set; }

        /// <summary>Where approved deliverables are written. Empty asks.</summary>
        [JsonProperty("deliveryFolder")] public string DeliveryFolder { get; set; }

        public CompareProfile()
        {
            Name = DefaultName;
            Version = "1";
            Consultant = string.Empty;
            Notes = string.Empty;
            Tolerances = new CompareTolerances();
            Categories = new List<CategoryRule>();
            NonplotLayers = new NonplotLayerNames();
            Review = new ReviewMarks();
            Trim = new TrimRules();
            Annotation = new AnnotationRules();
            Boundary = new CompareBoundary();
            Units = new UnitRules();
            Qc = new QcThresholds();
            Ignore = new IgnoreRules();
            ExplodeBlocks = new List<string>();
            DeliveryFolder = string.Empty;
        }

        // ------------------------------------------------------------------ lookup

        public CategoryRule Category(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return Categories.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public double PointTolerance(CategoryRule c) { return c != null && c.PointToleranceFeet.HasValue ? c.PointToleranceFeet.Value : Tolerances.PointFeet; }
        /// <summary>The horizontal distance beyond which a found counterpart is a discrepancy
        /// rather than a match; never more than the category's search radius.</summary>
        public double PointDiscrepancyTolerance(CategoryRule c)
        {
            var value = c != null && c.PointDiscrepancyFeet.HasValue ? c.PointDiscrepancyFeet.Value : Tolerances.PointDiscrepancyFeet;
            return Math.Min(value, PointTolerance(c));
        }
        public double LineOffsetTolerance(CategoryRule c) { return c != null && c.LineOffsetToleranceFeet.HasValue ? c.LineOffsetToleranceFeet.Value : Tolerances.LineOffsetFeet; }
        public double AngleTolerance(CategoryRule c) { return c != null && c.AngleToleranceDegrees.HasValue ? c.AngleToleranceDegrees.Value : Tolerances.AngleDegrees; }
        public double ElevationTolerance(CategoryRule c) { return c != null && c.ElevationToleranceFeet.HasValue ? c.ElevationToleranceFeet.Value : Tolerances.ElevationFeet; }

        public bool IsIgnored(CompareFeature f)
        {
            if (f == null) return false;
            var layers = f.Source == CompareSource.Survey ? Ignore.SurveyLayers : Ignore.ConsultantLayers;
            if (Wildcard.MatchesAny(layers, f.Layer)) return true;
            if (!string.IsNullOrEmpty(f.BlockName) && Wildcard.MatchesAny(Ignore.Blocks, f.BlockName)) return true;
            return false;
        }

        /// <summary>The nonplot layer a given survey feature goes to.</summary>
        public string NonplotLayerFor(CompareFeature f, CategoryRule category)
        {
            string fixedName;
            if (f.Kind == FeatureKind.Annotation) fixedName = NonplotLayers.Text;
            else if (category != null && category.IsUtility) fixedName = NonplotLayers.Utility;
            else if (f.Kind == FeatureKind.Line) fixedName = NonplotLayers.Line;
            else fixedName = !string.IsNullOrEmpty(f.BlockName) ? NonplotLayers.Block : NonplotLayers.Point;
            return NonplotLayers.For(f.Layer, fixedName);
        }

        // -------------------------------------------------------------- validation

        public void Validate(ICollection<string> problems)
        {
            if (string.IsNullOrWhiteSpace(Name)) problems.Add("The profile needs a name.");
            Tolerances.Validate("tolerances.", problems);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Categories)
            {
                if (string.IsNullOrWhiteSpace(c.Key)) { problems.Add("A category has no key."); continue; }
                if (!keys.Add(c.Key)) problems.Add("Category key '" + c.Key + "' is used twice.");
                if (c.Kind == FeatureKind.Annotation) problems.Add("Category '" + c.Key + "': annotation is compared through its feature, not as a category.");
                if (c.PointToleranceFeet.HasValue && c.PointToleranceFeet.Value <= 0) problems.Add("Category '" + c.Key + "': pointToleranceFeet must be positive.");
                if (c.PointDiscrepancyFeet.HasValue && c.PointDiscrepancyFeet.Value <= 0) problems.Add("Category '" + c.Key + "': pointDiscrepancyFeet must be positive.");
                if (c.LineOffsetToleranceFeet.HasValue && c.LineOffsetToleranceFeet.Value <= 0) problems.Add("Category '" + c.Key + "': lineOffsetToleranceFeet must be positive.");
            }
            foreach (var layer in NonplotLayers.All)
                if (string.IsNullOrWhiteSpace(layer)) problems.Add("Every nonplot layer needs a name.");
            if (NonplotLayers.All.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 5)
                problems.Add("Nonplot layer names must be distinct.");
            if (!string.Equals(NonplotLayers.Mode, NonplotLayerNames.ModeSuffix, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(NonplotLayers.Mode, NonplotLayerNames.ModeFixed, StringComparison.OrdinalIgnoreCase))
                problems.Add("nonplotLayers.mode must be Suffix or Fixed.");
            if (NonplotLayers.UseSuffix)
            {
                if (string.IsNullOrWhiteSpace(NonplotLayers.Suffix)) problems.Add("nonplotLayers.suffix is needed in Suffix mode (for example -NP).");
                else if (NonplotLayers.Suffix.IndexOfAny("<>/\\\":;?*|,=`".ToCharArray()) >= 0) problems.Add("nonplotLayers.suffix contains a character AutoCAD does not allow in a layer name.");
            }
            if (Review.DrawClouds && string.IsNullOrWhiteSpace(Review.Layer)) problems.Add("review.layer is needed while review.drawClouds is true.");
            if (Trim.SeamOffsetFeet > Trim.SeamSearchFeet) problems.Add("trim.seamOffsetFeet cannot exceed trim.seamSearchFeet (the search radius).");
            if (Trim.LimitsLayers.Count == 0) problems.Add("trim.limitsLayers needs at least one layer wildcard for the limits of survey.");
            if (Units.ConsultantUnitsPerFoot.HasValue && Units.ConsultantUnitsPerFoot.Value <= 0)
                problems.Add("units.consultantUnitsPerFoot must be positive.");
            if (Boundary.Points != null && Boundary.Points.Count > 0 && Boundary.Points.Count < 3)
                problems.Add("A comparison boundary needs at least three points.");
        }

        // ------------------------------------------------------------- persistence

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.Indented, CompareJson.Settings); }

        public static CompareProfile FromJson(string json)
        {
            CompareProfile p;
            try { p = JsonConvert.DeserializeObject<CompareProfile>(json, CompareJson.Settings); }
            catch (JsonException ex) { throw new ConfigException("Comparison profile is not valid JSON: " + ex.Message, ex); }
            if (p == null) throw new ConfigException("Comparison profile is empty.");
            p.FillMissing();
            return p;
        }

        public void FillMissing()
        {
            if (Tolerances == null) Tolerances = new CompareTolerances();
            if (Categories == null) Categories = new List<CategoryRule>();
            foreach (var c in Categories)
            {
                if (c.Survey == null) c.Survey = new SourceMapping();
                if (c.Consultant == null) c.Consultant = new SourceMapping();
                if (string.IsNullOrEmpty(c.System)) c.System = "OTHER";
                if (string.IsNullOrEmpty(c.Name)) c.Name = c.Key;
            }
            if (NonplotLayers == null) NonplotLayers = new NonplotLayerNames();
            if (string.IsNullOrEmpty(NonplotLayers.Mode)) NonplotLayers.Mode = NonplotLayerNames.ModeSuffix;
            if (NonplotLayers.Suffix == null) NonplotLayers.Suffix = "-NP";
            if (Review == null) Review = new ReviewMarks();
            if (Trim == null) Trim = new TrimRules();
            if (Trim.LimitsLayers == null) Trim.LimitsLayers = new TrimRules().LimitsLayers;
            if (Trim.SeamSearchFeet <= 0) Trim.SeamSearchFeet = 2.0;
            if (Trim.SeamOffsetFeet <= 0) Trim.SeamOffsetFeet = 0.5;
            if (Trim.SeamElevationFeet <= 0) Trim.SeamElevationFeet = 0.5;
            if (Trim.SeamAngleDegrees <= 0) Trim.SeamAngleDegrees = 20.0;
            if (Trim.MinPieceFeet < 0) Trim.MinPieceFeet = 0.5;
            if (string.IsNullOrEmpty(Review.Layer)) Review.Layer = "PMX-COMP-REVIEW";
            if (Review.TextHeightFeet <= 0) Review.TextHeightFeet = 2.0;
            if (Review.MarginFeet <= 0) Review.MarginFeet = 3.0;
            if (Annotation == null) Annotation = new AnnotationRules();
            if (Boundary == null) Boundary = new CompareBoundary();
            if (Units == null) Units = new UnitRules();
            if (Qc == null) Qc = new QcThresholds();
            if (Ignore == null) Ignore = new IgnoreRules();
            if (ExplodeBlocks == null) ExplodeBlocks = new List<string>();
            if (Version == null) Version = "1";
            if (Name == null) Name = DefaultName;
        }

        public static CompareProfile Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Comparison profile not found.", path);
            return FromJson(File.ReadAllText(path));
        }

        /// <summary>Validates, then writes. An invalid profile is never written.</summary>
        public void Save(string path)
        {
            var problems = new List<string>();
            Validate(problems);
            if (problems.Count > 0) throw new ConfigException("Comparison profile not saved:\n  " + string.Join("\n  ", problems.ToArray()));
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson());
        }

        /// <summary>User profiles folder: beside the FTF settings file, so the same
        /// override and backup conventions apply.</summary>
        public static string ProfilesDirectory()
        {
            return Path.Combine(Path.GetDirectoryName(FieldCodes.Settings.FtfSettings.UserProfilePath()), FolderName);
        }

        public static string ProfilePath(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var safe = new string(name.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
            return Path.Combine(ProfilesDirectory(), safe + Extension);
        }

        public static IList<string> ListProfiles(string pluginDirectory)
        {
            var names = new List<string>();
            foreach (var dir in new[] { ProfilesDirectory(), pluginDirectory == null ? null : Path.Combine(pluginDirectory, FolderName) })
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                foreach (var file in Directory.GetFiles(dir, "*" + Extension))
                {
                    var n = Path.GetFileNameWithoutExtension(file);
                    if (!names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
                }
            }
            if (!names.Contains(DefaultName, StringComparer.OrdinalIgnoreCase)) names.Insert(0, DefaultName);
            return names.OrderBy(n => n == DefaultName ? 0 : 1).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Where a profile was found.</summary>
        public sealed class Resolution
        {
            public CompareProfile Profile { get; set; }
            public string LoadedFrom { get; set; }
            /// <summary>BesideDrawing, User, Factory, BuiltIn.</summary>
            public string Level { get; set; }
        }

        /// <summary>
        /// Finds a profile by name: a per-job file beside the drawing wins, then the user's
        /// profiles folder, then the factory copy shipped with the plugin, then the built-in
        /// default (seeded from rules.json when given).
        /// </summary>
        public static Resolution Resolve(string name, string drawingDirectory, string pluginDirectory, RulesConfig rules)
        {
            if (!string.IsNullOrEmpty(drawingDirectory))
            {
                var beside = Path.Combine(drawingDirectory, BesideDrawingFileName);
                if (SafeExists(beside)) return new Resolution { Profile = Load(beside), LoadedFrom = beside, Level = "BesideDrawing" };
            }
            var wanted = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
            var user = ProfilePath(wanted);
            if (user != null && SafeExists(user)) return new Resolution { Profile = Load(user), LoadedFrom = user, Level = "User" };
            if (!string.IsNullOrEmpty(pluginDirectory))
            {
                var factory = Path.Combine(Path.Combine(pluginDirectory, FolderName), wanted + Extension);
                if (SafeExists(factory)) return new Resolution { Profile = Load(factory), LoadedFrom = factory, Level = "Factory" };
            }
            if (!string.Equals(wanted, DefaultName, StringComparison.OrdinalIgnoreCase))
                throw new ConfigException("Comparison profile '" + wanted + "' was not found in " + ProfilesDirectory() +
                                          (pluginDirectory == null ? string.Empty : " or " + Path.Combine(pluginDirectory, FolderName)) + ".");
            var builtIn = CreateDefault();
            if (rules != null) builtIn.ApplySurveyDefaultsFrom(rules);
            return new Resolution { Profile = builtIn, LoadedFrom = null, Level = "BuiltIn" };
        }

        private static bool SafeExists(string path)
        {
            try { return File.Exists(path); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // ----------------------------------------------------------------- defaults

        /// <summary>
        /// Adds the office survey layers to each category from the Field to Finish rules:
        /// every line feature code in a category contributes the layer the figure prefix
        /// database assigns it, so the survey side of the mapping is never typed twice.
        /// </summary>
        public void ApplySurveyDefaultsFrom(RulesConfig rules)
        {
            if (rules == null) return;
            foreach (var c in Categories)
            {
                foreach (var code in c.Survey.Codes.ToList())
                {
                    foreach (var lf in rules.LineFeatures ?? new List<LineFeatureRule>())
                    {
                        if (!string.Equals(lf.Code, code, StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.IsNullOrWhiteSpace(lf.Layer)) continue;
                        if (!c.Survey.Layers.Contains(lf.Layer, StringComparer.OrdinalIgnoreCase)) c.Survey.Layers.Add(lf.Layer);
                    }
                }
            }
        }

        /// <summary>
        /// The built-in profile. Survey codes are the office Field to Finish codes (see
        /// config/rules.json); consultant mappings are deliberately broad wildcards that a
        /// real consultant profile narrows. Every mapping here is a starting point, not an
        /// office decision -- the first run reports the layers it could not place.
        /// </summary>
        public static CompareProfile CreateDefault()
        {
            var p = new CompareProfile { Name = DefaultName, Consultant = "(any)", Notes = "Built-in starting profile. Save a copy per consultant and narrow the consultant mappings." };
            var c = p.Categories;

            // ---- utility point features ----------------------------------------------
            c.Add(Pt("UTILITY-POLE", "Utility Pole", "POWER", 2.0,
                new[] { "PP", "UP", "PPU", "PPX", "POLE", "TP", "JP" }, new[] { "V-UTIL-POWR-POLE*", "V-UTIL-POWR*" }, new[] { "*POLE*", "*UPOLE*", "*PP*" },
                new[] { "*POLE*", "*PWR*", "*POWR*", "*POWER*", "*ELEC*", "*UTIL*OH*" }, new[] { "*POLE*", "*UP*", "*PP*", "*PWRP*" }, new[] { "UTILITY POLE", "POWER POLE", "PWR POLE", "POLE", "UP" }));
            c.Add(Pt("LIGHT-POLE", "Light Pole", "POWER", 2.0,
                new[] { "LT", "LP", "LITE", "SLP", "LGT" }, new[] { "V-UTIL-POWR-LITE*", "V-SITE-LITE*" }, new[] { "*LIGHT*", "*LITE*" },
                new[] { "*LIGHT*", "*LITE*", "*LUM*" }, new[] { "*LIGHT*", "*LITE*", "*LP*" }, new[] { "LIGHT POLE", "STREET LIGHT", "LUMINAIRE", "LIGHT STANDARD" }));
            c.Add(Pt("GUY-ANCHOR", "Guy Wire / Anchor", "POWER", 2.0,
                new[] { "GYA", "GA", "GUY", "GW", "ANC" }, new[] { "V-UTIL-POWR-GUY*", "V-UTIL-POWR-ANCH*" }, new[] { "*GUY*", "*ANCH*" },
                new[] { "*GUY*", "*ANCH*" }, new[] { "*GUY*", "*ANCH*" }, new[] { "GUY", "GUY WIRE", "GUY ANCHOR", "ANCHOR" }));
            c.Add(Pt("HANDHOLE", "Handhole", "COMM", 2.0,
                new[] { "HH", "PHH", "THH", "CHH", "EHH", "FHH", "HHE", "HHT" }, new[] { "V-UTIL-*HH*", "V-UTIL-*HNDH*" }, new[] { "*HH*", "*HANDHOLE*" },
                new[] { "*HH*", "*HANDHOLE*", "*HNDH*" }, new[] { "*HH*", "*HANDHOLE*" }, new[] { "HANDHOLE", "HAND HOLE", "ELEC HH", "TEL HH", "COMM HH", "HH" }));
            c.Add(Pt("VAULT", "Utility Vault", "POWER", 3.0,
                new[] { "UVLT", "PVT", "VLT", "TV", "EV", "PV", "CV", "VAULT" }, new[] { "V-UTIL-*VLT*", "V-UTIL-*VAULT*" }, new[] { "*VAULT*", "*VLT*" },
                new[] { "*VAULT*", "*VLT*" }, new[] { "*VAULT*", "*VLT*" }, new[] { "VAULT", "ELEC VAULT", "TEL VAULT", "COMM VAULT", "POWER VAULT" }));
            c.Add(Pt("CABINET", "Cabinet / Pedestal", "COMM", 2.0,
                new[] { "CAB", "PED", "TPED", "CPED", "PPED", "EPED", "CABINET", "TCAB", "SCAB" }, new[] { "V-UTIL-*CAB*", "V-UTIL-*PED*" }, new[] { "*CAB*", "*PED*" },
                new[] { "*CAB*", "*PED*" }, new[] { "*CAB*", "*PED*" }, new[] { "CABINET", "PEDESTAL", "PED", "TEL PED", "COMM PED", "SIGNAL CABINET" }));
            c.Add(Pt("JUNCTION-BOX", "Junction Box", "POWER", 2.0,
                new[] { "PJB", "JB", "EJB", "TJB", "SJB", "JBOX" }, new[] { "V-UTIL-*JB*", "V-UTIL-*JBOX*" }, new[] { "*JB*", "*JBOX*", "*JUNC*" },
                new[] { "*JB*", "*JBOX*", "*JUNC*" }, new[] { "*JB*", "*JBOX*", "*JUNC*" }, new[] { "JUNCTION BOX", "J-BOX", "JBOX", "JB" }));
            c.Add(Pt("STORM-MH", "Storm Manhole", "STORM", 2.0,
                new[] { "SDMH", "STMH", "DMH", "SDM" }, new[] { "V-UTIL-STRM-MH*", "V-UTIL-STRC*" }, new[] { "*SDMH*", "*STMH*", "*STORM*MH*" },
                new[] { "*STRM*MH*", "*STORM*MH*", "*SD*MH*", "*SDMH*", "*STM*MH*", "*DRAIN*MH*", "*STRM*STRC*", "*STRM*STRUCT*" }, new[] { "*SDMH*", "*STMH*", "*STORM*MH*", "*ST*MH*", "*MH*STM*", "*DMH*", "*STRM*MH*" }, new[] { "STORM MH", "STORM MANHOLE", "DRAIN MH", "DRAINAGE MANHOLE", "SDMH", "STM MH", "SD MH" }));
            c.Add(Pt("SAN-MH", "Sanitary Manhole", "SANITARY", 2.0,
                new[] { "SSMH", "SMH", "SAMH", "SSM" }, new[] { "V-UTIL-SSWR-MH*", "V-UTIL-SSWR*" }, new[] { "*SSMH*", "*SAN*MH*", "*SEW*MH*" },
                new[] { "*SSWR*MH*", "*SAN*MH*", "*SEW*MH*", "*SS*MH*", "*SSMH*", "*SSWR*STRC*", "*SEW*STRUCT*" }, new[] { "*SSMH*", "*SAN*MH*", "*SEW*MH*", "*SS*MH*", "*MH*SS*", "*SMH*" }, new[] { "SAN MH", "SANITARY MANHOLE", "SEWER MH", "SEWER MANHOLE", "SSMH", "SS MH" }));
            c.Add(Pt("CATCH-BASIN", "Catch Basin / Inlet", "STORM", 2.0,
                new[] { "CB", "CBS", "SDCB", "INLET", "CBG", "CBI", "DI", "YD", "CI" }, new[] { "V-UTIL-STRM-CB*", "V-UTIL-STRM*" }, new[] { "*CB*", "*CATCH*", "*INLET*", "*BASIN*" },
                new[] { "*-CB*", "*CB-*", "*CATCH*", "*INLET*", "*STRM*CB*", "*STORM*CB*", "*STRM*INL*", "*BASIN*" }, new[] { "*CB*", "*CATCH*", "*INLET*", "*BASIN*", "*-DI*", "*DI-*" }, new[] { "CATCH BASIN", "INLET", "CB", "DROP INLET", "CURB INLET", "YARD DRAIN", "GRATE INLET" }));
            c.Add(Pt("CLEANOUT", "Cleanout", "SANITARY", 2.0,
                new[] { "CO", "SSCO", "SDCO", "SCO", "CLNOUT" }, new[] { "V-UTIL-*CO*", "V-UTIL-SSWR*" }, new[] { "*CO*", "*CLEANOUT*" },
                new[] { "*CLEANOUT*", "*CLNOUT*", "*-CO*", "*CO-*", "*SSCO*", "*SDCO*" }, new[] { "*-CO*", "*CO-*", "*CLEANOUT*", "*CLNOUT*", "*SSCO*", "*SDCO*", "CO" }, new[] { "CLEANOUT", "CLEAN OUT", "SEWER CO", "CO", "SSCO" }));
            c.Add(Pt("WATER-VALVE", "Water Valve", "WATER", 2.0,
                new[] { "WV", "WVL", "WVLV", "GV", "BFV", "VLV" }, new[] { "V-UTIL-WATR-VLV*", "V-UTIL-WATR*" }, new[] { "*WV*", "*VALVE*", "*VLV*" },
                new[] { "*WATR*VLV*", "*WATR*VALVE*", "*WATER*VALVE*", "*WTR*VLV*", "*-WV*", "*WV-*", "*VALVE*", "*VLV*" }, new[] { "*WV*", "*VALVE*", "*VLV*", "*-GV*", "*GV-*" }, new[] { "WATER VALVE", "VALVE", "WV", "GATE VALVE", "BUTTERFLY VALVE", "WTR VALVE" }));
            c.Add(Pt("WATER-METER", "Water Meter", "WATER", 2.0,
                new[] { "WM", "WMT", "WMTR", "MTR", "METER" }, new[] { "V-UTIL-WATR-MTR*", "V-UTIL-WATR*" }, new[] { "*WM*", "*METER*", "*MTR*" },
                new[] { "*WATR*MTR*", "*WATR*METER*", "*WATER*METER*", "*WTR*MTR*", "*-WM*", "*WM-*", "*METER*", "*MTR*" }, new[] { "*WM*", "*METER*", "*MTR*" }, new[] { "WATER METER", "METER", "WM", "WTR METER" }));
            c.Add(Pt("HYDRANT", "Fire Hydrant", "WATER", 2.0,
                new[] { "FH", "WFH", "HYD", "FHYD" }, new[] { "V-UTIL-WATR-HYD*", "V-UTIL-WATR*" }, new[] { "*HYD*", "*FH*" },
                new[] { "*HYD*", "*-FH*", "*FH-*", "*HYDRANT*" }, new[] { "*HYD*", "*FH*" }, new[] { "FIRE HYDRANT", "HYDRANT", "HYD", "FH" }));
            c.Add(Pt("WATER-FITTING", "Water Fitting / Blowoff", "WATER", 2.0,
                new[] { "WBO", "BO", "WF", "WFIT", "ARV", "WAV", "WT" }, new[] { "V-UTIL-WATR*" }, new[] { "*BLOW*", "*BO*", "*ARV*", "*FITT*" },
                new[] { "*BLOW*", "*FITT*", "*ARV*", "*-BO*", "*BO-*" }, new[] { "*BLOW*", "*-BO*", "*BO-*", "*ARV*", "*FITT*", "*TEE*", "*BEND*" }, new[] { "BLOWOFF", "BLOW OFF", "AIR RELEASE", "ARV", "FITTING", "TEE", "BEND" }));
            c.Add(Pt("GAS-VALVE", "Gas Valve", "GAS", 2.0,
                new[] { "GVL", "GAV", "GSV" }, new[] { "V-UTIL-NGAS*" }, new[] { "*GAS*VLV*", "*GV*" },
                new[] { "*GAS*VLV*", "*GAS*VALVE*", "*NGAS*VLV*" }, new[] { "*GAS*VLV*", "*GAS*VALVE*", "*-GV*", "*GV-*", "GV" }, new[] { "GAS VALVE", "GV" }));
            c.Add(Pt("GAS-METER", "Gas Meter", "GAS", 2.0,
                new[] { "GM", "GMT", "GMTR" }, new[] { "V-UTIL-NGAS*" }, new[] { "*GAS*MTR*", "*GM*" },
                new[] { "*GAS*MTR*", "*GAS*METER*", "*NGAS*MTR*" }, new[] { "*GAS*MTR*", "*GAS*METER*", "*-GM*", "*GM-*", "GM" }, new[] { "GAS METER", "GM" }));
            c.Add(Pt("UTILITY-MARKER", "Utility Marker / Test Station", "OTHER", 2.0,
                new[] { "UM", "UMK", "MKR", "TS", "TSTA", "MARKER" }, new[] { "V-UTIL-*MRK*", "V-UTIL-*MARK*" }, new[] { "*MARK*", "*MRK*" },
                new[] { "*MARK*", "*MRK*", "*TEST*STA*" }, new[] { "*MARK*", "*MRK*", "*TSTA*" }, new[] { "UTILITY MARKER", "MARKER", "TEST STATION" }));

            // ---- site point features ---------------------------------------------------
            c.Add(Pt("SIGN", "Sign", "SITE", 2.0,
                new[] { "SN", "SNNP", "SIGN", "SGN" }, new[] { "V-SIGN*" }, new[] { "*SIGN*" },
                new[] { "*SIGN*" }, new[] { "*SIGN*" }, new[] { "SIGN" }));
            c.Add(Pt("MAILBOX", "Mailbox", "SITE", 2.0,
                new[] { "MB", "MBX", "MAIL" }, new[] { "V-SITE*" }, new[] { "*MAIL*", "*MB*" },
                new[] { "*MAIL*" }, new[] { "*MAIL*", "*-MB*", "*MB-*", "MB" }, new[] { "MAILBOX", "MAIL BOX", "MB" }));
            c.Add(Pt("POST", "Post / Bollard", "SITE", 1.5,
                new[] { "POST", "BOL", "BOLL", "PST" }, new[] { "V-SITE*" }, new[] { "*POST*", "*BOL*" },
                new[] { "*POST*", "*BOL*" }, new[] { "*POST*", "*BOL*" }, new[] { "POST", "BOLLARD" }));
            c.Add(Pt("TREE", "Tree", "SITE", 3.0,
                new[] { "CON", "DEC", "MAP", "TR", "TREE", "TRD", "TRC" }, new[] { "V-TREE*", "V-VEG*" }, new[] { "*TREE*", "*CONIF*", "*DECID*" },
                new[] { "*TREE*", "*VEG*", "*LAND*" }, new[] { "*TREE*", "*CONIF*", "*DECID*" }, new[] { "TREE", "CONIFER", "DECIDUOUS" }));
            c.Add(Pt("MONUMENT", "Survey Monument", "SITE", 1.0,
                new[] { "MON", "FMON", "XMON", "FND", "FIP", "FRB", "FPK", "FMAG" }, new[] { "V-CTRL*", "V-MONU*" }, new[] { "*MON*" },
                new[] { "*MON*", "*CTRL*", "*CONTROL*" }, new[] { "*MON*" }, new[] { "MONUMENT", "FOUND", "MON" }));

            // ---- linework --------------------------------------------------------------
            c.Add(Ln("CURB", "Curb", "SURFACE", 1.0,
                new[] { "CU", "CX" }, new[] { "V-SURF-CURB-E" }, new[] { "*CURB*" }, new[] { "CURB" }));
            c.Add(Ln("CURB-FLOWLINE", "Curb Flowline / Gutter", "SURFACE", 1.0,
                new[] { "CG" }, new[] { "V-SURF-CURB-FLOW-E" }, new[] { "*GUTT*", "*FLOW*", "*-FL-*", "*-FL", "*CURB*FL*", "*CURB*GUT*" }, new[] { "GUTTER", "FLOWLINE", "FL" }));
            c.Add(Ln("CURB-BACK", "Top Back of Curb", "SURFACE", 1.0,
                new[] { "TBC" }, new[] { "V-SURF-CURB-BACK-E" }, new[] { "*TBC*", "*BACK*CURB*", "*CURB*BACK*", "*-BOC*", "*BOC-*" }, new[] { "TBC", "BACK OF CURB", "BOC" }));
            c.Add(Ln("EDGE-PAVEMENT", "Edge of Pavement", "SURFACE", 1.0,
                new[] { "EP", "ASPH" }, new[] { "V-SURF-ASPH-E" }, new[] { "*-EOP*", "*EOP-*", "*-EP-*", "*-EP", "EP-*", "*PAVE*", "*PVMT*", "*ASPH*", "*EDGE*PAV*" }, new[] { "EDGE OF PAVEMENT", "EOP", "EP", "ASPHALT", "EDGE OF ASPHALT" }));
            c.Add(Ln("ASPHALT-FLOWLINE", "Asphalt Flowline", "SURFACE", 1.0,
                new[] { "AFL", "TILT" }, new[] { "V-SURF-ASPH-FLOW-E" }, new[] { "*AFL*", "*ASPH*FL*" }, new[] { "ASPHALT FLOWLINE", "AFL" }));
            c.Add(Ln("ASPHALT-PATCH", "Asphalt Patch", "SURFACE", 1.0,
                new[] { "PATCH" }, new[] { "V-SURF-ASPH-PATC-E" }, new[] { "*PATCH*" }, new[] { "PATCH" }));
            c.Add(Ln("EDGE-CONCRETE", "Edge of Concrete / Sidewalk", "SURFACE", 1.0,
                new[] { "EC", "CONC", "SW" }, new[] { "V-SURF-CONC-E" }, new[] { "*CONC*", "*-EOC*", "*EOC-*", "*SIDEWALK*", "*SDWK*", "*SWLK*", "*WALK*", "*-SW-*", "*-SW", "SW-*" }, new[] { "EDGE OF CONCRETE", "EOC", "SIDEWALK", "CONC WALK", "CONCRETE" }));
            c.Add(Ln("EDGE-GRAVEL", "Edge of Gravel", "SURFACE", 1.5,
                new[] { "EGR", "GRVL" }, new[] { "V-SURF-GRVL-E" }, new[] { "*GRAV*", "*GRVL*" }, new[] { "GRAVEL", "EDGE OF GRAVEL" }));
            c.Add(Ln("EDGE-GRASS", "Edge of Grass / Landscape", "SURFACE", 1.5,
                new[] { "EGG", "LNDS", "LAND" }, new[] { "V-SURF-GRAS-E" }, new[] { "*GRAS*", "*LAWN*", "*LNDS*", "*LANDS*" }, new[] { "GRASS", "LAWN", "LANDSCAPE" }));
            c.Add(Ln("PATH", "Path / Trail", "SURFACE", 1.5,
                new[] { "PATH", "TRL", "TRAIL" }, new[] { "V-SURF-TRAL-E" }, new[] { "*PATH*", "*TRAIL*", "*TRL*" }, new[] { "PATH", "TRAIL" }));
            c.Add(Ln("DRIVEWAY", "Driveway", "SURFACE", 1.0,
                new[] { "DW", "DWY", "DRVWY" }, new[] { "V-SURF-DRIV*", "V-SURF-DRWY*" }, new[] { "*DRIV*", "*DWY*", "*DRWY*" }, new[] { "DRIVEWAY", "DW" }));
            c.Add(Ln("FENCE", "Fence", "SITE", 1.0,
                new[] { "FNC", "FHW", "FBD", "FRL", "FBW", "FCK", "FEN", "FENCE" }, new[] { "V-SURF-FENC*" }, new[] { "*FENC*", "*FNC*", "*CLF*", "*CHAIN*" }, new[] { "FENCE", "CHAIN LINK", "WOOD FENCE", "CLF" }));
            c.Add(Ln("WALL", "Wall", "SITE", 1.0,
                new[] { "RWC", "RWB", "RWT", "RWRK", "RWBK", "RWMT", "RWWD", "WALL", "RW" }, new[] { "V-SURF-WALL*" }, new[] { "*WALL*", "*RW*", "*RETAIN*" }, new[] { "WALL", "RETAINING WALL", "ROCK WALL", "CONC WALL" }));
            c.Add(Ln("GUARDRAIL", "Guardrail", "SITE", 1.0,
                new[] { "GRL", "GR", "GDRL" }, new[] { "V-SURF-GRAL-E" }, new[] { "*GUARD*", "*GRAL*", "*GDRL*", "*BARRIER*" }, new[] { "GUARDRAIL", "GUARD RAIL", "BARRIER" }));
            c.Add(Ln("HANDRAIL", "Handrail", "SITE", 1.0,
                new[] { "HDR", "HR", "HNDRL" }, new[] { "V-SURF-HDRL-E" }, new[] { "*HANDRAIL*", "*HDRL*", "*HNDRL*", "*RAIL*" }, new[] { "HANDRAIL", "HAND RAIL" }));
            c.Add(Ln("BUILDING", "Building", "SITE", 1.0,
                new[] { "BLD", "BLC", "BLE", "BLDG" }, new[] { "V-SURF-BLDG*" }, new[] { "*BLDG*", "*BUILD*", "*STRUCT*", "*HOUSE*", "*ROOF*" }, new[] { "BUILDING", "BLDG", "STRUCTURE", "ROOF" }));
            c.Add(Ln("STAIRS", "Stairs", "SITE", 1.0,
                new[] { "STR", "STAIR", "STEP" }, new[] { "V-SURF-STAR-E" }, new[] { "*STAIR*", "*STEP*" }, new[] { "STAIRS", "STEPS" }));
            c.Add(Ln("STRIPE", "Pavement Striping", "SURFACE", 1.0,
                new[] { "LN", "LNSO", "FOG", "LNDY", "LNWH", "LNYE", "STRP" }, new[] { "V-CHAN-STRP*", "V-CHAN-SKST*" }, new[] { "*STRP*", "*STRIP*", "*PAINT*", "*MARK*", "*LANE*" }, new[] { "STRIPE", "STRIPING", "FOG LINE", "LANE LINE", "DOUBLE YELLOW", "PAINT" }));
            c.Add(Ln("MARKING", "Pavement Marking / Crosswalk", "SURFACE", 1.0,
                new[] { "XW", "JBAR", "BAR", "ARROW", "STOP" }, new[] { "V-CHAN-MRKG*" }, new[] { "*MRKG*", "*XWALK*", "*CROSS*", "*MARK*" }, new[] { "CROSSWALK", "STOP BAR", "MARKING" }));
            c.Add(Ln("STORM-PIPE", "Storm Pipe", "STORM", 2.0,
                new[] { "SDTD", "SDU", "SD", "STM", "CULV", "CVTL" }, new[] { "V-UTIL-STRM*" }, new[] { "*STRM*", "*STORM*", "*-SD-*", "*-SD", "SD-*", "*DRAIN*", "*CULV*" }, new[] { "STORM", "SD", "DRAIN", "CULVERT" }));
            c.Add(Ln("SAN-PIPE", "Sanitary Sewer Pipe", "SANITARY", 2.0,
                new[] { "SSU", "SS", "SAN", "SEW" }, new[] { "V-UTIL-SSWR*" }, new[] { "*SSWR*", "*SANIT*", "*-SAN-*", "*-SAN", "*SEWER*", "*-SS-*", "*-SS", "SS-*" }, new[] { "SANITARY", "SEWER", "SS" }));
            c.Add(Ln("WATER-LINE", "Water Line", "WATER", 2.0,
                new[] { "WU", "WL", "WAT", "WTR" }, new[] { "V-UTIL-WATR*" }, new[] { "*WATR*", "*WATER*", "*WTR*", "*-WL-*", "*-WL", "WL-*", "*-W-*", "*-W" }, new[] { "WATER", "WATERLINE", "WTR" }));
            c.Add(Ln("POWER-LINE", "Power Line", "POWER", 2.0,
                new[] { "PU", "POH", "UPD", "PWR", "ELEC", "OHP", "OHW" }, new[] { "V-UTIL-POWR*" }, new[] { "*POWR*", "*POWER*", "*PWR*", "*ELEC*", "*OHP*", "*OHW*", "*OVHD*", "*-OH-*", "*-OH", "*-E-*", "*-E" }, new[] { "POWER", "ELECTRIC", "OVERHEAD", "OHP", "OHW", "PWR" }));
            c.Add(Ln("COMM-LINE", "Communication Line", "COMM", 2.0,
                new[] { "TU", "CAU", "UCAD", "UFOD", "TEL", "COMM", "FO", "CATV", "CAOH" }, new[] { "V-UTIL-TELE*", "V-UTIL-CATV*", "V-UTIL-FIBR*", "V-UTIL-COMM*" }, new[] { "*TELE*", "*-TEL-*", "*-TEL", "*COMM*", "*CATV*", "*FIB*", "*-FO-*", "*-FO", "*CABLE*", "*PHONE*", "*-T-*", "*-T" }, new[] { "TELEPHONE", "TEL", "COMM", "FIBER", "CATV", "CABLE" }));
            c.Add(Ln("GAS-LINE", "Gas Line", "GAS", 2.0,
                new[] { "GU", "GAS", "GL" }, new[] { "V-UTIL-NGAS*" }, new[] { "*GAS*", "*NGAS*" }, new[] { "GAS", "NATURAL GAS" }));
            c.Add(Ln("TOPO-BREAKLINE", "Top / Toe / Bank", "TOPO", 2.0,
                new[] { "TOP", "TOE", "BANK", "TOH", "TSL", "DIKE", "CRW", "CLS" }, new[] { "V-TOPO*", "V-SURF-TOP*", "V-SURF-TOE*", "V-SURF-BANK*" }, new[] { "*TOP*", "*TOE*", "*BANK*", "*BREAK*", "*SLOPE*" }, new[] { "TOP OF SLOPE", "TOE OF SLOPE", "TOP", "TOE", "BANK" }));
            c.Add(Ln("WATER-EDGE", "Edge of Water / OHWM", "TOPO", 2.0,
                new[] { "EOW", "WLF", "OHWM", "WLOHWM", "HWM", "WLST", "CRK" }, new[] { "V-TOPO-WATR*", "V-SURF-WATR*" }, new[] { "*WATER*EDGE*", "*EOW*", "*OHWM*", "*SHORE*", "*CREEK*", "*STREAM*" }, new[] { "EDGE OF WATER", "OHWM", "ORDINARY HIGH WATER", "CREEK", "STREAM" }));
            c.Add(Ln("RAILROAD", "Railroad", "SITE", 1.5,
                new[] { "RRCL", "RRRAIL", "RRTIE", "RR" }, new[] { "V-RAIL*", "V-SURF-RAIL*" }, new[] { "*RAIL*", "*RR*", "*TRACK*" }, new[] { "RAILROAD", "RAIL", "TRACK" }));

            return p;
        }

        private static CategoryRule Pt(string key, string name, string system, double tol,
                                       string[] surveyCodes, string[] surveyLayers, string[] surveyBlocks,
                                       string[] consultantLayers, string[] consultantBlocks, string[] consultantKeywords)
        {
            return new CategoryRule
            {
                Key = key, Name = name, Kind = FeatureKind.Point, System = system, PointToleranceFeet = tol,
                Survey = SourceMapping.Of(surveyCodes, surveyLayers, surveyBlocks, null),
                Consultant = SourceMapping.Of(null, consultantLayers, consultantBlocks, consultantKeywords)
            };
        }

        private static CategoryRule Ln(string key, string name, string system, double offsetTol,
                                       string[] surveyCodes, string[] surveyLayers, string[] consultantLayers, string[] consultantKeywords)
        {
            return new CategoryRule
            {
                Key = key, Name = name, Kind = FeatureKind.Line, System = system, LineOffsetToleranceFeet = offsetTol,
                Survey = SourceMapping.Of(surveyCodes, surveyLayers, null, null),
                Consultant = SourceMapping.Of(null, consultantLayers, null, consultantKeywords)
            };
        }
    }
}
