using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Orient
{
    /// <summary>
    /// One symbol family: which codes it covers, what kind of feature it is, and how its
    /// block is drawn at rotation zero.
    ///
    /// <see cref="ReferenceCadDegrees"/> is the direction (AutoCAD degrees, counter-clockwise
    /// from east) of the symbol's meaningful direction in the block definition: a sign's face,
    /// a hydrant's pumper nozzle, a light's arm, a valve's pipe axis, an arrow's direction of
    /// travel, a painted word's "up". <see cref="Period"/> is how often the symbol repeats:
    /// 360 for a symbol with one meaningful direction, 180 for an axis (a bowtie valve), 0
    /// for a symbol that looks the same at every rotation (a round light): those are never
    /// rotated.
    /// </summary>
    public sealed class SymbolRule
    {
        /// <summary>Point codes, first word of the description, figure digits ignored.
        /// Wildcards * and ? are allowed. The first matching rule wins.</summary>
        [JsonProperty("codes")] public List<string> Codes { get; set; }

        [JsonProperty("kind")]
        [JsonConverter(typeof(StringEnumConverter))]
        public OrientKind Kind { get; set; }

        /// <summary>The block it was read from, for people; matching is by code.</summary>
        [JsonProperty("block")] public string Block { get; set; }

        [JsonProperty("referenceCadDegrees")] public double ReferenceCadDegrees { get; set; }
        [JsonProperty("period")] public double Period { get; set; }

        /// <summary>Signs readable from both sides (street names): only a measured direction
        /// orients them.</summary>
        [JsonProperty("twoSided")] public bool TwoSided { get; set; }

        /// <summary>Rotate the point label with the marker (painted words read to the driver).</summary>
        [JsonProperty("rotateLabel")] public bool RotateLabel { get; set; }

        [JsonProperty("meaning")] public string Meaning { get; set; }

        public SymbolRule()
        {
            Codes = new List<string>();
            Period = 360;
        }

        public bool Directional { get { return Period > 0; } }

        /// <summary>True when a block of this name is this symbol's block (wildcards allowed),
        /// so its zero is the one this rule describes.</summary>
        public bool DrawnWith(string blockName)
        {
            return !string.IsNullOrWhiteSpace(blockName) && OrientationStandard.Glob(Block, blockName.Trim());
        }
    }

    /// <summary>Which survey linework is what, by layer pattern or by figure code.</summary>
    public sealed class LineRoleRule
    {
        [JsonProperty("role")]
        [JsonConverter(typeof(StringEnumConverter))]
        public LineRole Role { get; set; }

        [JsonProperty("layers")] public List<string> Layers { get; set; }

        /// <summary>Survey figure names / codes, figure digits ignored (BAR1 is BAR).</summary>
        [JsonProperty("codes")] public List<string> Codes { get; set; }

        public LineRoleRule() { Layers = new List<string>(); Codes = new List<string>(); }
    }

    /// <summary>
    /// The office standard for orienting symbols: the "orientation" section of rules.json.
    /// Defaults are PMX's, read from the blocks in a completed PMX drawing (SSV-SN: post at
    /// the insertion point, panel north of it, so the face points north at rotation zero;
    /// SSV-WFH: nozzles east; SSV-WVL: bowtie on a north-south axis; SSV-LTARM: arm north;
    /// SSV-AS*/AL*/AR*: arrow tip at the insertion point, travelling north; SSV-LT, SSV-WVT:
    /// round or lettered, never rotated).
    /// </summary>
    public sealed class OrientationStandard
    {
        [JsonProperty("symbols", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<SymbolRule> Symbols { get; set; }

        [JsonProperty("lines", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<LineRoleRule> Lines { get; set; }

        /// <summary>Sign codes that control an approach (stop, yield): stop bars and
        /// intersections decide which way they face.</summary>
        [JsonProperty("stopSignCodes", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> StopSignCodes { get; set; }

        /// <summary>Words in a sign's description or note that make it a stop/yield sign.</summary>
        [JsonProperty("stopSignWords", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> StopSignWords { get; set; }

        /// <summary>Codes of a shot at a luminaire (end of an arm): the arm points from the pole to it.</summary>
        [JsonProperty("luminaireCodes", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> LuminaireCodes { get; set; }

        // ------------------------------------------------------------ distances, feet

        [JsonProperty("roadSearchFeet")] public double RoadSearchFeet { get; set; }
        [JsonProperty("intersectionSearchFeet")] public double IntersectionSearchFeet { get; set; }
        [JsonProperty("stopBarSearchFeet")] public double StopBarSearchFeet { get; set; }
        [JsonProperty("samePostFeet")] public double SamePostFeet { get; set; }
        [JsonProperty("valveClusterFeet")] public double ValveClusterFeet { get; set; }
        [JsonProperty("hydrantSearchFeet")] public double HydrantSearchFeet { get; set; }
        [JsonProperty("mainSnapFeet")] public double MainSnapFeet { get; set; }
        [JsonProperty("panelSnapFeet")] public double PanelSnapFeet { get; set; }
        [JsonProperty("luminaireSearchFeet")] public double LuminaireSearchFeet { get; set; }

        // ------------------------------------------------------------ applying

        /// <summary>FTFORIENT rotates a marker only at or above this confidence...</summary>
        [JsonProperty("applyMinimumConfidence")] public double ApplyMinimumConfidence { get; set; }

        /// <summary>...and only for decisions at least this strong (Measured, Relationship,
        /// Alignment or Inferred). Review is never applied.</summary>
        [JsonProperty("applyWeakestTier")]
        [JsonConverter(typeof(StringEnumConverter))]
        public OrientationTier ApplyWeakestTier { get; set; }

        /// <summary>Non-plotting QC layers: decisions, and inferred connections apart.</summary>
        [JsonProperty("qcLayer")] public string QcLayer { get; set; }
        [JsonProperty("qcInferredLayer")] public string QcInferredLayer { get; set; }

        /// <summary>Length of a QC direction arrow, inches on the sheet.</summary>
        [JsonProperty("qcArrowPlotted")] public double QcArrowPlotted { get; set; }

        /// <summary>The office's approved symbol library: a drawing whose block definitions
        /// Settings > Symbols and Rotations offers beside the drawing's own (and brings in only
        /// when asked). Nothing reads it while a command runs. Empty: none.</summary>
        [JsonProperty("symbolLibrary")] public string SymbolLibrary { get; set; }

        public OrientationStandard()
        {
            Symbols = new List<SymbolRule>();
            Lines = new List<LineRoleRule>();
            StopSignCodes = new List<string>();
            StopSignWords = new List<string>();
            LuminaireCodes = new List<string>();
            RoadSearchFeet = 80;
            IntersectionSearchFeet = 150;
            StopBarSearchFeet = 60;
            SamePostFeet = 1.5;
            ValveClusterFeet = 20;
            HydrantSearchFeet = 60;
            MainSnapFeet = 5;
            PanelSnapFeet = 3;
            LuminaireSearchFeet = 40;
            ApplyMinimumConfidence = 0.6;
            ApplyWeakestTier = OrientationTier.Inferred;
            QcLayer = "FTF-QC-ORIENT";
            QcInferredLayer = "FTF-QC-ORIENT-INFERRED";
            QcArrowPlotted = 0.3;
        }

        /// <summary>PMX's standard, as the blocks in its template are drawn.</summary>
        public static OrientationStandard Pmx()
        {
            var s = new OrientationStandard();
            Func<OrientKind, double, double, string, string, string[], SymbolRule> sym =
                (kind, reference, period, block, meaning, codes) => new SymbolRule
                {
                    Kind = kind, ReferenceCadDegrees = reference, Period = period, Block = block,
                    Meaning = meaning, Codes = codes.ToList()
                };

            // A sign line is linework (the panel), not a sign point.
            s.Symbols.Add(sym(OrientKind.Other, 0, 0, null, "sign panel line (linework)", new[] { "SNL*" }));
            s.Symbols.Add(new SymbolRule
            {
                Kind = OrientKind.Sign, ReferenceCadDegrees = 90, Period = 360, Block = "SSV-SNST", TwoSided = true,
                Meaning = "street name blades read from both sides", Codes = new List<string> { "SNST" }
            });
            s.Symbols.Add(sym(OrientKind.Sign, 90, 360, "SSV-SN*", "sign face (panel north of the post)", new[] { "SN*" }));
            s.Symbols.Add(sym(OrientKind.Hydrant, 0, 360, "SSV-WFH", "pumper nozzle", new[] { "WFH", "FH", "FHY" }));
            s.Symbols.Add(sym(OrientKind.WaterValve, 90, 180, "SSV-WVL", "pipe axis (bowtie)", new[] { "WVL", "WV", "WIRV" }));
            s.Symbols.Add(sym(OrientKind.GasValve, 0, 180, "SSV-GV", "pipe axis", new[] { "GVL", "GV" }));
            s.Symbols.Add(sym(OrientKind.StreetLight, 90, 360, "SSV-LTARM", "arm, pole to luminaire",
                              new[] { "LTARM", "LTARMW", "PPL", "PPLU", "PPLUX", "PPLX" }));
            s.Symbols.Add(sym(OrientKind.StreetLight, 0, 0, "SSV-LT", "round light: never rotated", new[] { "LT", "LTYARD" }));
            s.Symbols.Add(sym(OrientKind.SignalPole, 90, 360, "SSV-TSP", "mast arm", new[] { "TSP", "TSPL" }));
            s.Symbols.Add(sym(OrientKind.PavementArrow, 90, 360, "SSV-ASTO", "direction of travel (tip at insertion)",
                              new[] { "ASTO", "ALTO", "ARTO", "ASLT", "ASRT", "ASLR" }));
            s.Symbols.Add(new SymbolRule
            {
                Kind = OrientKind.PavementWord, ReferenceCadDegrees = 90, Period = 360, Block = "(label)",
                RotateLabel = true, Meaning = "reads to the approaching driver", Codes = new List<string> { "BART" }
            });

            Func<LineRole, string[], string[], LineRoleRule> line = (role, layers, codes) =>
                new LineRoleRule { Role = role, Layers = layers.ToList(), Codes = codes.ToList() };
            s.Lines.Add(line(LineRole.StopBar, new string[0], new[] { "BAR" }));
            s.Lines.Add(line(LineRole.Crosswalk, new string[0], new[] { "XW" }));
            s.Lines.Add(line(LineRole.SignPanel, new[] { "V-SURF-SIGN-E" }, new[] { "SNL" }));
            s.Lines.Add(line(LineRole.OneWay, new[] { "V-ROAD-ONEWAY*", "V-ALGN-ONEWAY*" }, new string[0]));
            s.Lines.Add(line(LineRole.Centerline, new[] { "V-ALGN-CNTR-E", "V-ROAD-CNTR*", "V-CHAN-STRP-DBL1-E",
                                                          "V-CHAN-SKST-DBL1-E", "V-SURF-ASPH-CNTR-E" },
                             new[] { "LNDY", "CL" }));
            s.Lines.Add(line(LineRole.Edge, new[] { "V-SURF-ASPH-E", "V-SURF-CURB-E", "V-SURF-CURB-BACK-E",
                                                    "V-SURF-CURB-FLOW-E", "V-SURF-ASPH-FLOW-E" },
                             new[] { "EP", "CU", "CG", "TBC", "AFL" }));
            s.Lines.Add(line(LineRole.LaneLine, new[] { "V-CHAN-STRP-E", "V-CHAN-SKST-E" }, new[] { "FOG", "LN", "LNSO" }));
            s.Lines.Add(line(LineRole.WaterMain, new[] { "V-UTIL-WATR-E", "V-UTIL-WATR-UNDR-E" }, new[] { "WU" }));
            s.Lines.Add(line(LineRole.GasMain, new[] { "V-UTIL-NGAS-E", "V-UTIL-NGAS-UNDR-E" }, new[] { "GU" }));

            s.StopSignCodes.AddRange(new[] { "SNS", "SNY" });
            s.StopSignWords.AddRange(new[] { "STOP", "YIELD", "ALL WAY", "ALLWAY" });
            s.LuminaireCodes.AddRange(new[] { "LUM", "LTA", "LTHEAD" });
            return s;
        }

        /// <summary>The symbol rule for a point code, or null.</summary>
        public SymbolRule SymbolFor(string code)
        {
            var bare = BaseCode(code);
            if (bare.Length == 0) return null;
            return Symbols.FirstOrDefault(r => r.Codes.Any(c => Glob(c, bare) || Glob(c, (code ?? "").Trim())));
        }

        /// <summary>The role of a line from its figure name/code, then its layer.</summary>
        public LineRole RoleFor(string layer, string figureName)
        {
            var code = BaseCode(figureName);
            if (code.Length > 0)
                foreach (var r in Lines)
                    if (r.Codes.Any(c => Glob(c, code))) return r.Role;
            foreach (var r in Lines)
                if (!string.IsNullOrWhiteSpace(layer) && r.Layers.Any(l => Glob(l, layer.Trim()))) return r.Role;
            return LineRole.Other;
        }

        /// <summary>True for a shot on a light's head or arm end (used to aim the arm).</summary>
        public bool IsLuminaire(string code)
        {
            var bare = BaseCode(code);
            return bare.Length > 0 && LuminaireCodes.Any(c => Glob(c, bare));
        }

        public bool IsStopSign(OrientFeature f)
        {
            if (StopSignCodes.Any(c => Glob(c, BaseCode(f.Code)))) return true;
            var text = ((f.Description ?? "") + " " + (f.Note ?? "")).ToUpperInvariant();
            return StopSignWords.Any(w => Regex.IsMatch(text, @"\b" + Regex.Escape(w.ToUpperInvariant()) + @"\b"));
        }

        /// <summary>The first word without trailing figure digits: EP1 -> EP, TSP -> TSP.</summary>
        public static string BaseCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            var first = code.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
            var end = first.Length;
            while (end > 1 && char.IsDigit(first[end - 1])) end--;
            return first.Substring(0, end);
        }

        /// <summary>The index of the rule <see cref="SymbolFor"/> picks for a code, or -1: what an
        /// editor highlights as "this code's rule" (first match wins).</summary>
        public int SymbolIndexFor(string code)
        {
            var rule = SymbolFor(code);
            return rule == null ? -1 : Symbols.IndexOf(rule);
        }

        /// <summary>True when a code pattern (wildcards * and ?) matches a value, as symbol and
        /// line rules match codes.</summary>
        public static bool CodeMatches(string pattern, string value)
        {
            return Glob(pattern, value);
        }

        internal static bool Glob(string pattern, string value)
        {
            if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrEmpty(value)) return false;
            var rx = "^" + Regex.Escape(pattern.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(value, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
