using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Settings
{
    /// <summary>How a structure is sized: round ones by diameter, rectangular ones by
    /// width and length, and small fittings (cleanouts, end sections) not at all.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum StructureShape { Round, Rectangular, NoSize }

    /// <summary>A structure code and what it is. Codes are configuration, never
    /// hard coded into the dip builder.</summary>
    public sealed class StructureCodeRule
    {
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("type")] public string Type { get; set; }

        [JsonProperty("system")]
        [JsonConverter(typeof(StringEnumConverter))]
        public UtilitySystem System { get; set; }

        /// <summary>Round (sized by diameter), Rectangular (width x length) or NoSize.</summary>
        [JsonProperty("shape")]
        public StructureShape Shape { get; set; }

        /// <summary>Standard inside width in inches for this structure type, ONLY when the
        /// office has a documented standard size. Used with source PROFILE, never as a
        /// field observation. Empty (the default) leaves the size unknown, which turns the
        /// "pipe too large for structure" check off for the structure.</summary>
        [JsonProperty("insideWidthIn")] public double? InsideWidthIn { get; set; }
    }

    /// <summary>Drafting layers and QC WARNING thresholds for one utility system. The
    /// thresholds raise review warnings only; they are not design requirements.</summary>
    public sealed class UtilitySystemStandard
    {
        [JsonProperty("system")]
        [JsonConverter(typeof(StringEnumConverter))]
        public UtilitySystem System { get; set; }

        /// <summary>Abbreviation in labels: SD, SS, CULV.</summary>
        [JsonProperty("abbreviation")] public string Abbreviation { get; set; }

        [JsonProperty("pipeLayer")] public string PipeLayer { get; set; }
        [JsonProperty("labelLayer")] public string LabelLayer { get; set; }
        [JsonProperty("structureLabelLayer")] public string StructureLabelLayer { get; set; }

        /// <summary>Whether the slope warning runs for this system.</summary>
        [JsonProperty("slopeCheckEnabled")] public bool SlopeCheckEnabled { get; set; }

        /// <summary>Whether the cover warning runs for this system.</summary>
        [JsonProperty("coverCheckEnabled")] public bool CoverCheckEnabled { get; set; }

        /// <summary>Slope warning limits in percent. Warnings only, never blocks.</summary>
        [JsonProperty("minSlopePercent")] public double MinSlopePercent { get; set; }
        [JsonProperty("maxSlopePercent")] public double MaxSlopePercent { get; set; }

        /// <summary>Cover over the pipe crown at the structure below which a warning is
        /// raised, feet.</summary>
        [JsonProperty("minCoverFt")] public double MinCoverFt { get; set; }
    }

    /// <summary>
    /// Which pipe sizes and materials show first when a pipe is added at one kind of structure. Structure types,
    /// a system, or neither (any structure). Speed tools only: nothing outside these lists is ever rejected.
    /// </summary>
    public sealed class PipeChoiceRule
    {
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>Structure codes this rule is for (CB, SDMH). Empty: decided by the system instead.</summary>
        [JsonProperty("structureCodes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> StructureCodes { get; set; }

        /// <summary>The system this rule is for when it names no structure codes. Null with no codes: any structure.</summary>
        [JsonProperty("system")]
        [JsonConverter(typeof(StringEnumConverter))]
        public UtilitySystem? System { get; set; }

        [JsonProperty("commonSizes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<double> CommonSizes { get; set; }

        /// <summary>Sizes behind the "Larger" button. Only a path to more choices: any size can be typed there.</summary>
        [JsonProperty("largerSizes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<double> LargerSizes { get; set; }

        [JsonProperty("commonMaterials", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> CommonMaterials { get; set; }

        /// <summary>Materials behind "More...". Empty: every other office material.</summary>
        [JsonProperty("moreMaterials", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> MoreMaterials { get; set; }

        public PipeChoiceRule()
        {
            StructureCodes = new List<string>();
            CommonSizes = new List<double>();
            LargerSizes = new List<double>();
            CommonMaterials = new List<string>();
            MoreMaterials = new List<string>();
        }

        public void Normalize()
        {
            StructureCodes = Words(StructureCodes);
            CommonMaterials = Words(CommonMaterials);
            MoreMaterials = Words(MoreMaterials);
            if (CommonSizes == null) CommonSizes = new List<double>();
            if (LargerSizes == null) LargerSizes = new List<double>();
        }

        /// <summary>What the rule applies to, in words.</summary>
        public string Describe()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name.Trim();
            if (StructureCodes != null && StructureCodes.Count > 0) return string.Join(", ", StructureCodes.ToArray());
            return System.HasValue ? System.Value.ToString() : "Any structure";
        }

        private static List<string> Words(IEnumerable<string> values)
        {
            return (values ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v))
                                                         .Select(v => v.Trim().ToUpperInvariant()).Distinct().ToList();
        }
    }

    /// <summary>The buttons resolved for one structure: shown first, and behind Larger / More.</summary>
    public sealed class PipeChoiceSet
    {
        public string RuleName { get; set; }
        public List<double> CommonSizes { get; private set; }
        public List<double> LargerSizes { get; private set; }
        public List<string> CommonMaterials { get; private set; }
        public List<string> MoreMaterials { get; private set; }

        public PipeChoiceSet()
        {
            CommonSizes = new List<double>();
            LargerSizes = new List<double>();
            CommonMaterials = new List<string>();
            MoreMaterials = new List<string>();
        }
    }

    public sealed class UtilitySettings : ISettingsSection
    {
        public string Title { get { return "Storm & Sewer Dips"; } }
        public string AffectedCommands { get { return "FTFDIP"; } }

        [JsonProperty("structureCodes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<StructureCodeRule> StructureCodes { get; set; }

        [JsonProperty("systems", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<UtilitySystemStandard> Systems { get; set; }

        /// <summary>Pipe material abbreviations the note parser recognises.</summary>
        [JsonProperty("materials", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Materials { get; set; }

        // Pick lists in the Dip Builder window. The "common" values show first; the rest sit under "More...".
        // Suggestions only: any value can still be typed, and the observed field value always wins.

        /// <summary>Structure codes shown first in the Type list; the other structure codes are under More.</summary>
        [JsonProperty("commonStructureCodes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> CommonStructureCodes { get; set; }

        /// <summary>
        /// The size and material buttons offered when a pipe is added, by structure type and system. The first rule
        /// naming the structure's type wins, then the first rule for its system with no types named, then the rule
        /// with neither (any structure). Only decides which buttons show first: any size or material can be entered.
        /// </summary>
        [JsonProperty("pipeChoices", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<PipeChoiceRule> PipeChoices { get; set; }

        /// <summary>Round structure diameters (inches) shown first, and those under More. Rectangular structures get
        /// no size list: their inside dimensions are always typed as measured.</summary>
        [JsonProperty("structureDiametersCommon", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<double> StructureDiametersCommon { get; set; }
        [JsonProperty("structureDiametersMore", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<double> StructureDiametersMore { get; set; }

        /// <summary>The reference SHOWN as an assumption for an unmarked dip ("Assumed
        /// reference: Invert"). Display only: the pipe stays Unspecified until the
        /// drafter confirms it.</summary>
        [JsonProperty("assumedPipeReference")]
        [JsonConverter(typeof(StringEnumConverter))]
        public MeasurementReference AssumedPipeReference { get; set; }

        /// <summary>When on (the office default), a dip that does not say INV or TOP is
        /// classified as the invert, with basis "field note convention" recorded on the
        /// pipe. When off, such dips stay Unspecified until the drafter confirms them.</summary>
        [JsonProperty("unmarkedDipsAreInvertsByConvention")] public bool UnmarkedDipsAreInvertsByConvention { get; set; }

        /// <summary>Where that convention is documented (sheet name and revision). Required
        /// when the convention is on, and recorded on every pipe it classifies.</summary>
        [JsonProperty("unmarkedDipConventionSource")] public string UnmarkedDipConventionSource { get; set; }

        /// <summary>QC checks the drafter has switched off. Checks that stop invalid
        /// geometry from being drawn cannot be switched off.</summary>
        [JsonProperty("disabledChecks", ObjectCreationHandling = ObjectCreationHandling.Replace, ItemConverterType = typeof(StringEnumConverter))]
        public List<QcCode> DisabledChecks { get; set; }

        /// <summary>Half-angle of the directional search cone, degrees.</summary>
        [JsonProperty("searchConeDegrees")] public double SearchConeDegrees { get; set; }

        /// <summary>Farthest a connecting structure is searched for, feet.</summary>
        [JsonProperty("searchDistanceFt")] public double SearchDistanceFt { get; set; }

        /// <summary>Pipes wider than this are drawn as a double line. Inches.</summary>
        [JsonProperty("doubleLineThresholdIn")] public double DoubleLineThresholdIn { get; set; }

        /// <summary>Also draw the centerline between double lines.</summary>
        [JsonProperty("centerlineWithDoubleLine")] public bool CenterlineWithDoubleLine { get; set; }

        /// <summary>A CAD line within this distance of both structures is treated as
        /// an existing pipe between them, feet.</summary>
        [JsonProperty("existingPipeToleranceFt")] public double ExistingPipeToleranceFt { get; set; }

        /// <summary>Minimum vertical clearance where storm and sanitary cross, feet.</summary>
        [JsonProperty("crossingClearanceFt")] public double CrossingClearanceFt { get; set; }

        [JsonProperty("elevationDecimals")] public int ElevationDecimals { get; set; }
        [JsonProperty("slopeDecimals")] public int SlopeDecimals { get; set; }

        /// <summary>Pipe label. Bracketed parts drop out when a token inside has no
        /// value -- a pipe without a calculated slope simply omits "@ slope".</summary>
        [JsonProperty("pipeLabelFormat")] public string PipeLabelFormat { get; set; }

        [JsonProperty("structureHeaderFormat")] public string StructureHeaderFormat { get; set; }
        [JsonProperty("rimLineFormat")] public string RimLineFormat { get; set; }
        [JsonProperty("pipeLineFormat")] public string PipeLineFormat { get; set; }
        [JsonProperty("undippedPipeLineFormat")] public string UndippedPipeLineFormat { get; set; }
        [JsonProperty("bottomLineFormat")] public string BottomLineFormat { get; set; }
        [JsonProperty("waterLineFormat")] public string WaterLineFormat { get; set; }

        /// <summary>Label word for each measurement reference: IE, TOP, SPR.</summary>
        [JsonProperty("referencePrefixInvert")] public string PrefixInvert { get; set; }
        [JsonProperty("referencePrefixTop")] public string PrefixTop { get; set; }
        [JsonProperty("referencePrefixSpringline")] public string PrefixSpringline { get; set; }

        /// <summary>Label word for a dip whose reference has not been confirmed. Kept
        /// visibly different from the invert word.</summary>
        [JsonProperty("referencePrefixUnconfirmed")] public string PrefixUnconfirmed { get; set; }

        /// <summary>Text style for pipe and structure labels (office: Survey). Empty or missing in the drawing uses the current style.</summary>
        [JsonProperty("textStyle")] public string TextStyle { get; set; }

        /// <summary>Multileader style for structure labels (office: xPMX SURV Text Arrow Anno, the Storm Callout / Sewer
        /// palette tools). An annotative style makes the label annotative, as the office tools do. Empty or missing in
        /// the drawing uses the current style.</summary>
        [JsonProperty("leaderStyle")] public string LeaderStyle { get; set; }
        [JsonProperty("textHeightPlotted")] public double TextHeightPlotted { get; set; }
        [JsonProperty("pipeLabelOffsetPlotted")] public double PipeLabelOffsetPlotted { get; set; }

        public UtilitySettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            // Codes from the office code sheet (PMX Field Code rev 2025-01-13) plus the
            // generic names drafters use. Add a code here to teach the builder about it.
            StructureCodes = new List<StructureCodeRule>
            {
                Code("CB", "CATCH BASIN", UtilitySystem.Storm, StructureShape.Rectangular),
                Code("CBR", "CATCH BASIN (ROUND)", UtilitySystem.Storm, StructureShape.Round),
                Code("CBS", "CATCH BASIN (SOLID LID)", UtilitySystem.Storm, StructureShape.Rectangular),
                Code("DI", "DROP INLET", UtilitySystem.Storm, StructureShape.Rectangular),
                Code("INLET", "CURB INLET", UtilitySystem.Storm, StructureShape.Rectangular),
                Code("SDAD", "AREA DRAIN", UtilitySystem.Storm, StructureShape.Rectangular),
                Code("SDCO", "STORM CLEANOUT", UtilitySystem.Storm, StructureShape.NoSize),
                Code("SDMH", "STORM MANHOLE", UtilitySystem.Storm, StructureShape.Round),
                Code("SDMHOF", "STORM MANHOLE (OFFSET)", UtilitySystem.Storm, StructureShape.Round),
                Code("SDVT", "STORM VAULT", UtilitySystem.Storm, StructureShape.Rectangular),
                Code("DW", "DRY WELL", UtilitySystem.Storm, StructureShape.Round),
                Code("OUTFALL", "OUTFALL", UtilitySystem.Storm, StructureShape.NoSize),
                Code("MH", "MANHOLE", UtilitySystem.Other, StructureShape.Round),
                Code("SSMH", "SANITARY MANHOLE", UtilitySystem.Sanitary, StructureShape.Round),
                Code("SSMHF", "SANITARY MANHOLE (FM)", UtilitySystem.Sanitary, StructureShape.Round),
                Code("SSCO", "SEWER CLEANOUT", UtilitySystem.Sanitary, StructureShape.NoSize),
                Code("CO", "CLEANOUT", UtilitySystem.Sanitary, StructureShape.NoSize),
                Code("SSVT", "SEWER VAULT", UtilitySystem.Sanitary, StructureShape.Rectangular),
                Code("SSWET", "SEWER WET WELL", UtilitySystem.Sanitary, StructureShape.Round),
                Code("CULV", "CULVERT END", UtilitySystem.Culvert, StructureShape.NoSize),
                Code("CVTL", "CULVERT END", UtilitySystem.Culvert, StructureShape.NoSize),
                Code("HEADWALL", "HEADWALL", UtilitySystem.Culvert, StructureShape.NoSize),
                Code("FES", "FLARED END SECTION", UtilitySystem.Culvert, StructureShape.NoSize),
                Code("UMH", "MANHOLE (UNKNOWN)", UtilitySystem.Other, StructureShape.Round)
            };

            Systems = new List<UtilitySystemStandard>
            {
                // Default WARNING thresholds for review, not design requirements.
                MakeSystem(UtilitySystem.Storm, "SD", "V-UTIL-STRM-E", 0.3, 20.0, 1.0, true),
                MakeSystem(UtilitySystem.Sanitary, "SS", "V-UTIL-SSWR-E", 0.4, 15.0, 3.0, true),
                // No thresholds have been set for these yet, so their checks start off.
                // The office has no culvert layer: culverts draw on the storm layers (office decision 2026-09-18).
                MakeSystem(UtilitySystem.Culvert, "CULV", "V-UTIL-STRM-E", 0.0, 0.0, 0.0, false),
                MakeSystem(UtilitySystem.Water, "W", "V-UTIL-WATR-E", 0.0, 0.0, 0.0, false),
                MakeSystem(UtilitySystem.Other, "UTIL", "V-UTIL-E", 0.0, 0.0, 0.0, false)
            };

            Materials = new List<string>
            {
                "RCP", "CMP", "CPEP", "HDPE", "PVC", "DI", "DIP", "CI", "VCP", "CLAY",
                "CONC", "STEEL", "AC", "ADS", "PE", "UNK"
            };

            // Common / More split from the Puget Sound field defaults (King, Pierce, Snohomish; 2026-09-18), in the
            // office's own codes. Round sizes follow the WSDOT / King County Type 2 catch basin and manhole family.
            CommonStructureCodes = new List<string> { "CB", "CBR", "SDMH", "SSMH", "MH" };
            PipeChoices = DefaultPipeChoices();
            StructureDiametersCommon = new List<double> { 48, 54, 60, 72, 96 };
            StructureDiametersMore = new List<double> { 36, 42, 84, 108, 120 };

            AssumedPipeReference = MeasurementReference.Invert;
            // Office decision: a dip is the invert unless the note or the drafter says
            // top of pipe. Still recorded on each pipe as coming from this default.
            UnmarkedDipsAreInvertsByConvention = true;
            UnmarkedDipConventionSource = "Office default: dips are inverts unless marked top of pipe";
            DisabledChecks = new List<QcCode>();
            SearchConeDegrees = 15.0;
            SearchDistanceFt = 600.0;
            DoubleLineThresholdIn = 12.0;
            CenterlineWithDoubleLine = false;
            ExistingPipeToleranceFt = 2.0;
            CrossingClearanceFt = 1.0;
            ElevationDecimals = 2;
            SlopeDecimals = 2;

            PipeLabelFormat = "{size} {material} {system}[ @ {slope}]";
            StructureHeaderFormat = "{code} {number}[ {size}]";
            RimLineFormat = "RIM = {rim}";
            PipeLineFormat = "{prefix}[ {role}] ({direction}) = {elevation} {size} {material}";
            UndippedPipeLineFormat = "({direction}) {size} {material} - NOT DIPPED";
            BottomLineFormat = "BOT = {bottom}";
            WaterLineFormat = "WL = {water}";
            PrefixInvert = "IE";
            PrefixTop = "TOP";
            PrefixSpringline = "SPR";
            PrefixUnconfirmed = "IE?";

            // The office survey standard (PMX Survey Standards C3D.dwt; Storm Callout and Sewer palette tools).
            TextStyle = "Survey";
            LeaderStyle = "xPMX SURV Text Arrow Anno";
            TextHeightPlotted = 0.08;
            PipeLabelOffsetPlotted = 0.06;
        }

        private static StructureCodeRule Code(string code, string type, UtilitySystem system, StructureShape shape)
        {
            return new StructureCodeRule { Code = code, Type = type, System = system, Shape = shape };
        }

        private static UtilitySystemStandard MakeSystem(UtilitySystem system, string abbreviation,
                                                    string layer, double min, double max,
                                                    double cover, bool checksOn)
        {
            return new UtilitySystemStandard
            {
                System = system,
                SlopeCheckEnabled = checksOn,
                CoverCheckEnabled = checksOn,
                Abbreviation = abbreviation,
                PipeLayer = layer,
                LabelLayer = layer.Replace("-E", "-TEXT-E"),
                StructureLabelLayer = layer.Replace("-E", "-TEXT-E"),
                MinSlopePercent = min,
                MaxSlopePercent = max,
                MinCoverFt = cover
            };
        }

        public StructureCodeRule FindCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || StructureCodes == null) return null;
            foreach (var rule in StructureCodes)
                if (string.Equals(rule.Code, code.Trim(), StringComparison.OrdinalIgnoreCase))
                    return rule;
            return null;
        }

        /// <summary>The Type pick list: the common codes (only those still defined), then every other structure code.</summary>
        public void StructureCodeChoices(out List<string> common, out List<string> more)
        {
            Split((StructureCodes ?? new List<StructureCodeRule>()).Select(c => c.Code), CommonStructureCodes, out common, out more);
        }

        /// <summary>
        /// The buttons for a pipe at a structure of this type and system. Sizes and materials are kept exactly as
        /// configured; "more" materials default to every other office material when the rule lists none, and
        /// "larger" sizes to the any-structure rule's sizes when it lists none.
        /// </summary>
        public PipeChoiceSet PipeChoicesFor(string structureType, UtilitySystem system)
        {
            var rules = (PipeChoices ?? DefaultPipeChoices()).Where(r => r != null).ToList();
            foreach (var r in rules) r.Normalize();
            var code = (structureType ?? string.Empty).Trim().ToUpperInvariant();

            var any = rules.FirstOrDefault(r => r.StructureCodes.Count == 0 && r.System == null);
            var rule = (code.Length > 0 ? rules.FirstOrDefault(r => r.StructureCodes.Contains(code)) : null)
                       ?? rules.FirstOrDefault(r => r.StructureCodes.Count == 0 && r.System == system)
                       ?? any
                       ?? new PipeChoiceRule();

            var set = new PipeChoiceSet { RuleName = rule.Describe() };
            set.CommonSizes.AddRange(rule.CommonSizes.Distinct());
            var larger = rule.LargerSizes.Count > 0 || any == null || any == rule
                ? rule.LargerSizes
                : any.CommonSizes.Concat(any.LargerSizes).ToList();
            set.LargerSizes.AddRange(larger.Distinct().Where(v => !set.CommonSizes.Contains(v)).OrderBy(v => v));

            set.CommonMaterials.AddRange(rule.CommonMaterials.Distinct());
            var more = rule.MoreMaterials.Count > 0
                ? rule.MoreMaterials
                : (Materials ?? new List<string>()).Select(m => (m ?? string.Empty).Trim().ToUpperInvariant()).ToList();
            set.MoreMaterials.AddRange(more.Where(m => m.Length > 0 && !set.CommonMaterials.Contains(m)).Distinct());
            return set;
        }

        /// <summary>
        /// Starting points for the pipe buttons, from the Puget Sound field defaults (King, Pierce, Snohomish;
        /// 2026-09-18) written in the office's own codes. The usual sizes are the ones worth one click at that kind of
        /// structure -- shortcuts, not a range the structure is limited to: Larger holds the rest and any size can be
        /// typed. Speed only, never a standard: the office edits these.
        /// </summary>
        public static List<PipeChoiceRule> DefaultPipeChoices()
        {
            return new List<PipeChoiceRule>
            {
                new PipeChoiceRule
                {
                    Name = "Catch basins and inlets",
                    StructureCodes = { "CB", "CBR", "CBS", "DI", "INLET", "SDAD" },
                    CommonSizes = { 6, 8, 10, 12, 15, 18, 24 },
                    LargerSizes = { 4, 21, 27, 30, 36, 42, 48, 54, 60 },
                    CommonMaterials = { "PVC", "RCP", "CPEP", "HDPE", "CMP", "CONC", "UNK" }
                },
                new PipeChoiceRule
                {
                    Name = "Storm", System = UtilitySystem.Storm,
                    CommonSizes = { 8, 10, 12, 15, 18, 24, 30, 36, 48 },
                    LargerSizes = { 4, 6, 21, 27, 33, 42, 54, 60, 66, 72, 84, 96, 108, 120 },
                    CommonMaterials = { "RCP", "CMP", "PVC", "HDPE", "CPEP", "CONC", "UNK" }
                },
                new PipeChoiceRule
                {
                    Name = "Sanitary", System = UtilitySystem.Sanitary,
                    CommonSizes = { 4, 6, 8, 10, 12, 15, 18, 24 },
                    LargerSizes = { 21, 27, 30, 33, 36, 42, 48, 54, 60, 72 },
                    CommonMaterials = { "VCP", "PVC", "RCP", "DI", "CONC", "UNK" }
                },
                new PipeChoiceRule
                {
                    Name = "Culverts", System = UtilitySystem.Culvert,
                    CommonSizes = { 12, 15, 18, 24, 30, 36, 48, 60 },
                    LargerSizes = { 8, 10, 42, 54, 66, 72, 84, 96, 108, 120, 144 },
                    CommonMaterials = { "CMP", "RCP", "HDPE", "CPEP", "PVC", "CONC", "UNK" }
                },
                new PipeChoiceRule
                {
                    Name = "Any structure",
                    CommonSizes = { 4, 6, 8, 10, 12, 15, 18, 24, 30, 36, 42, 48 },
                    LargerSizes = { 1, 1.25, 1.5, 2, 3, 14, 16, 20, 21, 27, 33, 54, 60, 66, 72, 78, 84, 90, 96, 102, 108, 120, 144 },
                    CommonMaterials = { "RCP", "PVC", "CMP", "HDPE", "VCP", "DI", "CONC", "UNK" }
                }
            };
        }

        /// <summary>A size in inches as the pick lists show it: 8, 1.25.</summary>
        public static string SizeText(double inches)
        {
            return inches.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void Split(IEnumerable<string> all, IEnumerable<string> commonNames, out List<string> common, out List<string> more)
        {
            var known = (all ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v))
                                                         .Select(v => v.Trim().ToUpperInvariant()).Distinct().ToList();
            var wanted = new HashSet<string>((commonNames ?? Enumerable.Empty<string>()).Where(v => v != null)
                                                                                     .Select(v => v.Trim().ToUpperInvariant()));
            // Common values keep the order the office listed them in; the rest keep the order of the full list.
            common = (commonNames ?? Enumerable.Empty<string>()).Where(v => v != null).Select(v => v.Trim().ToUpperInvariant())
                                                               .Where(known.Contains).Distinct().ToList();
            more = known.Where(v => !wanted.Contains(v)).ToList();
        }

        public UtilitySystemStandard Standard(UtilitySystem system)
        {
            if (Systems != null)
                foreach (var s in Systems)
                    if (s.System == system) return s;
            return MakeSystem(system, system.ToString().ToUpperInvariant(), "V-UTIL-E", 0, 0, 0, false);
        }

        public void Validate(ICollection<string> problems)
        {
            if (StructureCodes == null) StructureCodes = new List<StructureCodeRule>();
            if (Systems == null) Systems = new List<UtilitySystemStandard>();
            if (Materials == null) Materials = new List<string>();
            if (CommonStructureCodes == null) CommonStructureCodes = new List<string>();
            // Settings saved before pipe choices existed get the defaults; a saved empty list stays empty.
            if (PipeChoices == null) PipeChoices = DefaultPipeChoices();
            if (StructureDiametersCommon == null) StructureDiametersCommon = new List<double>();
            if (StructureDiametersMore == null) StructureDiametersMore = new List<double>();
            foreach (var rule in PipeChoices.Where(r => r != null))
            {
                rule.Normalize();
                if (rule.CommonSizes.Concat(rule.LargerSizes).Any(v => double.IsNaN(v) || v <= 0))
                    problems.Add("Dips: pipe sizes for \"" + rule.Describe() + "\" must be greater than zero.");
            }
            if (StructureDiametersCommon.Concat(StructureDiametersMore).Any(v => double.IsNaN(v) || v <= 0))
                problems.Add("Dips: structure diameters in the pick list must be greater than zero.");
            if (DisabledChecks == null) DisabledChecks = new List<QcCode>();
            if (UnmarkedDipsAreInvertsByConvention && string.IsNullOrWhiteSpace(UnmarkedDipConventionSource))
                problems.Add("Dips: say where the office convention that unmarked dips are inverts is documented, or turn it off.");
            if (AssumedPipeReference == MeasurementReference.Unspecified)
                problems.Add("Dips: the assumed reference shown for unmarked dips must be a real reference.");

            if (double.IsNaN(SearchConeDegrees) || SearchConeDegrees <= 0 || SearchConeDegrees >= 90)
                problems.Add("Dips: the search cone must be between 0 and 90 degrees.");
            Require.Positive(SearchDistanceFt, "Dips: the search distance must be greater than zero.", problems);
            Require.NotNegative(DoubleLineThresholdIn, "Dips: the double-line threshold cannot be negative.", problems);
            Require.NotNegative(ExistingPipeToleranceFt, "Dips: the existing-pipe tolerance cannot be negative.", problems);
            Require.Positive(TextHeightPlotted, "Dips: text height must be greater than zero.", problems);
            if (ElevationDecimals < 0 || ElevationDecimals > 4)
                problems.Add("Dips: elevation decimals must be between 0 and 4.");
            if (SlopeDecimals < 0 || SlopeDecimals > 4)
                problems.Add("Dips: slope decimals must be between 0 and 4.");

            foreach (var s in Systems)
            {
                if (s.SlopeCheckEnabled && s.MinSlopePercent > s.MaxSlopePercent)
                    problems.Add("Dips: " + s.System + " minimum slope is above its maximum.");
                if (string.IsNullOrWhiteSpace(s.PipeLayer))
                    problems.Add("Dips: " + s.System + " needs a pipe layer.");
            }
        }
    }
}
