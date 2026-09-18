using System;
using System.Collections.Generic;
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

        [JsonProperty("textStyle")] public string TextStyle { get; set; }
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
                MakeSystem(UtilitySystem.Culvert, "CULV", "V-UTIL-CULV-E", 0.0, 0.0, 0.0, false),
                MakeSystem(UtilitySystem.Water, "W", "V-UTIL-WATR-E", 0.0, 0.0, 0.0, false),
                MakeSystem(UtilitySystem.Other, "UTIL", "V-UTIL-E", 0.0, 0.0, 0.0, false)
            };

            Materials = new List<string>
            {
                "RCP", "CMP", "CPEP", "HDPE", "PVC", "DI", "DIP", "CI", "VCP", "CLAY",
                "CONC", "STEEL", "AC", "ADS", "PE", "UNK"
            };

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

            TextStyle = string.Empty;
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
