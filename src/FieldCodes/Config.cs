using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace FieldCodes
{
    public sealed class RulesConfig
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        /// <summary>Drawing units per survey foot. 1.0 for feet, 0.3048 for metres.</summary>
        [JsonProperty("unitsPerFoot")]
        public double UnitsPerFoot { get; set; }

        /// <summary>Decimal places used when a trunk size is written into a label.</summary>
        [JsonProperty("trunkDecimals")]
        public int TrunkDecimals { get; set; }

        [JsonProperty("multiStemAverage")]
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public StemAverageMethod MultiStemAverage { get; set; }

        /// <summary>Layers whose entities must never be masked or overlapped by a label.</summary>
        [JsonProperty("protectedLayers")]
        public List<string> ProtectedLayers { get; set; }

        /// <summary>Layers a label may sit on top of and mask freely.</summary>
        [JsonProperty("maskableLayers")]
        public List<string> MaskableLayers { get; set; }

        /// <summary>
        /// Field codes this tool deliberately does not draw. Listed as bare codes; a
        /// trailing sequence number is matched automatically, so "EC" also covers EC2.
        /// </summary>
        [JsonProperty("ignoreCodes")]
        public List<string> IgnoreCodes { get; set; }

        [JsonIgnore]
        public IList<Regex> IgnorePatterns { get; private set; }

        /// <summary>
        /// Codes whose points define linework rather than a feature. TBC and Civil 3D
        /// turn these into figures; this tool labels the resulting polylines, so the
        /// points themselves need no rule and are not reported as unhandled.
        /// </summary>
        [JsonProperty("lineworkCodes")]
        public List<string> LineworkCodes { get; set; }

        [JsonIgnore]
        public IList<Regex> LineworkPatterns { get; private set; }

        /// <summary>Line features FTF can identify for finishing. See LineFeatureRule.</summary>
        [JsonProperty("lineFeatures")]
        public List<LineFeatureRule> LineFeatures { get; set; }

        /// <summary>
        /// Bare symbol codes that get a label anyway. The office rule stands -- a
        /// bare code draws nothing -- EXCEPT for codes listed here, which label the
        /// symbol with the code itself ("CB" beside the catch basin) on the same
        /// text layer the code's data-carrying rule uses. Every entry is an explicit
        /// office decision; an unlisted bare code still does nothing.
        /// </summary>
        [JsonProperty("symbolLabels")]
        public List<SymbolLabelRule> SymbolLabels { get; set; }

        /// <summary>
        /// Notes on the survey points ALONG a line that mark a span between two of
        /// them -- two fence shots noted GATE define a gate, labelled centred
        /// between the pair. Token-exact matching; strict pairing; an odd marker is
        /// reported, never guessed into a span.
        /// </summary>
        [JsonProperty("spanMarkers")]
        public List<SpanMarkerRule> SpanMarkers { get; set; }

        /// <summary>What FTFLABELSTAIRS says, with {count} filled in from the
        /// number of stair lines selected -- each drawn tread edge is one line.</summary>
        [JsonProperty("stairsLabelFormat")]
        public string StairsLabelFormat { get; set; }

        /// <summary>Codes whose points belong in the control table -- the survey
        /// control and found monuments. Matched against the first token of the
        /// raw description, exactly.</summary>
        [JsonProperty("controlCodes")]
        public List<string> ControlCodes { get; set; }

        /// <summary>
        /// TBC export names deliberately mapped to nothing ("INFO LINE"): the name is
        /// recognised, so the inventory stops reporting it as unknown, but no feature
        /// is identified and nothing is drawn. Exact, case-insensitive, trimmed --
        /// the same matching discipline as LineFeatureRule.Aliases.
        /// </summary>
        [JsonProperty("ignoreLineNames")]
        public List<string> IgnoreLineNames { get; set; }

        [JsonProperty("labelPlacement")]
        public LabelPlacementRule LabelPlacement { get; set; }

        [JsonProperty("codes")]
        public List<CodeRule> Codes { get; set; }

        [JsonProperty("modifiers")]
        public List<ModifierRule> Modifiers { get; set; }

        public RulesConfig()
        {
            UnitsPerFoot = 1.0;
            TrunkDecimals = 0;
            MultiStemAverage = StemAverageMethod.Arithmetic;
            ProtectedLayers = new List<string>();
            MaskableLayers = new List<string>();
            Codes = new List<CodeRule>();
            Modifiers = new List<ModifierRule>();
            LabelPlacement = new LabelPlacementRule();
            IgnoreCodes = new List<string>();
            IgnorePatterns = new List<Regex>();
            LineworkCodes = new List<string>();
            LineworkPatterns = new List<Regex>();
            LineFeatures = new List<LineFeatureRule>();
            IgnoreLineNames = new List<string>();
            SymbolLabels = new List<SymbolLabelRule>();
            SpanMarkers = new List<SpanMarkerRule>();
            StairsLabelFormat = "{count} STEPS";
            ControlCodes = new List<string>();
        }

        public static RulesConfig Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Rules file not found.", path);

            RulesConfig cfg;
            try
            {
                cfg = JsonConvert.DeserializeObject<RulesConfig>(File.ReadAllText(path));
            }
            catch (JsonException ex)
            {
                throw new ConfigException("Rules file is not valid JSON: " + ex.Message, ex);
            }

            if (cfg == null)
                throw new ConfigException("Rules file is empty.");

            cfg.Compile();
            return cfg;
        }

        /// <summary>
        /// Compiles regexes and enforces structural invariants. Throws rather than
        /// deferring config errors to per-point runtime failures.
        /// </summary>
        public void Compile()
        {
            var problems = new List<string>();

            if (UnitsPerFoot <= 0)
                problems.Add("unitsPerFoot must be greater than zero.");

            // Ignore entries are bare codes, not regexes: they come from a survey code
            // list, and letting a stray character there silently swallow tree points
            // would defeat the point of the strict parser.
            IgnorePatterns = CodeListCompiler.Compile(IgnoreCodes, "ignoreCodes", problems);
            LineworkPatterns = CodeListCompiler.Compile(LineworkCodes, "lineworkCodes", problems);

            var symbolCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var symbol in SymbolLabels ?? new List<SymbolLabelRule>())
            {
                if (symbol == null || string.IsNullOrWhiteSpace(symbol.Code))
                {
                    problems.Add("symbolLabels: an entry is missing its code.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(symbol.Layer))
                    problems.Add("symbolLabels: '" + symbol.Code + "' is missing its layer.");
                if (!symbolCodes.Add(symbol.Code.Trim()))
                    problems.Add("symbolLabels: '" + symbol.Code + "' is listed twice.");
            }

            if (Codes.Count == 0)
                problems.Add("No code rules defined.");

            // labelPlacement is not validated here any more. It is seed data for a
            // first-time migration into ftf-settings.json, and MigrateFrom already
            // ignores values that make no sense. A stale seed must not stop the
            // grammar from loading -- FtfSettings.Validate guards the live values.

            foreach (var c in Codes)
            {
                if (string.IsNullOrWhiteSpace(c.Id))
                    problems.Add("A code rule is missing 'id'.");
                if (string.IsNullOrWhiteSpace(c.Match))
                {
                    problems.Add(string.Format("Code rule '{0}' is missing 'match'.", c.Id));
                    continue;
                }
                // {SPECIES} expands to the keys of this rule's species map, so the
                // pattern and the map cannot drift apart. Without it the pattern has to
                // capture species loosely, and a loose capture swallows every other
                // code of the same shape -- "PP 1234" would parse as a tree of unknown
                // species rather than a power pole carrying a number.
                var pattern = c.Match;
                if (pattern.IndexOf("{SPECIES}", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (c.Species == null || c.Species.Count == 0)
                    {
                        problems.Add(string.Format(
                            "Code rule '{0}' uses {{SPECIES}} but defines no species.", c.Id));
                        continue;
                    }

                    var alternation = "(?:" + string.Join("|",
                        c.Species.Keys.Select(Regex.Escape).ToArray()) + ")";

                    pattern = Regex.Replace(pattern, Regex.Escape("{SPECIES}"),
                                            alternation.Replace("$", "$$"),
                                            RegexOptions.IgnoreCase);
                }

                try
                {
                    c.Regex = new Regex(pattern,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
                }
                catch (ArgumentException ex)
                {
                    problems.Add(string.Format("Code rule '{0}' has an invalid regex: {1}", c.Id, ex.Message));
                }
            }

            var dupIds = Codes.GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                              .Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var d in dupIds)
                problems.Add(string.Format("Duplicate code rule id '{0}'.", d));

            foreach (var m in Modifiers)
            {
                if (string.IsNullOrWhiteSpace(m.Id))
                    problems.Add("A modifier rule is missing 'id'.");
                if (string.IsNullOrWhiteSpace(m.Pattern))
                {
                    problems.Add(string.Format("Modifier '{0}' is missing 'pattern'.", m.Id));
                    continue;
                }
                try
                {
                    m.Regex = new Regex(m.Pattern,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
                }
                catch (ArgumentException ex)
                {
                    problems.Add(string.Format("Modifier '{0}' has an invalid regex: {1}", m.Id, ex.Message));
                }
            }

            // Two modifiers that both replace the whole label would make the result
            // depend on evaluation order. Catch it now, not on the drawing.
            var replacers = Modifiers.Where(m => !string.IsNullOrEmpty(m.LabelFormat)).ToList();
            var priorityClash = replacers.GroupBy(m => m.Priority).Where(g => g.Count() > 1);
            foreach (var g in priorityClash)
                problems.Add(string.Format(
                    "Modifiers [{0}] all set 'labelFormat' at priority {1}; give them distinct priorities.",
                    string.Join(", ", g.Select(m => m.Id).ToArray()), g.Key));

            if (problems.Count > 0)
                throw new ConfigException("Rules file is invalid:" + Environment.NewLine +
                                          string.Join(Environment.NewLine, problems.ToArray()));
        }
    }

    public static class CodeListCompiler
    {
        /// <summary>
        /// Turns a list of bare field codes into anchored patterns.
        ///
        /// Entries are plain codes, not regexes: they come from a survey code list, and
        /// letting a stray character there silently swallow real feature codes would
        /// defeat the point of a strict parser.
        ///
        /// A trailing sequence number is matched too, so EC covers EC1 and EC2. For
        /// linework those numbers denote separate parallel strings rather than repeats,
        /// but for deciding "is this linework at all" grouping them is correct.
        /// </summary>
        public static IList<Regex> Compile(IEnumerable<string> codes, string listName,
                                           ICollection<string> problems)
        {
            var patterns = new List<Regex>();

            foreach (var code in codes ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(code)) continue;

                var trimmed = code.Trim();
                if (!Regex.IsMatch(trimmed, @"^[A-Za-z0-9_-]+$"))
                {
                    problems.Add(string.Format(
                        "{0} entry '{1}' must be a plain field code (letters, digits, - or _).",
                        listName, trimmed));
                    continue;
                }

                patterns.Add(new Regex(
                    "^" + Regex.Escape(trimmed) + "[0-9]*(?:\\s|$)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
            }

            return patterns;
        }
    }

    /// <summary>One span marker: the token shot in the field ("GATE") and what the
    /// label between the marked pair says.</summary>
    public sealed class SpanMarkerRule
    {
        [JsonProperty("token")] public string Token { get; set; }

        /// <summary>Absent means the label says the token itself.</summary>
        [JsonProperty("label")] public string Label { get; set; }

        [JsonProperty("enabled")] public bool Enabled { get; set; }

        public SpanMarkerRule() { Enabled = true; }
    }

    /// <summary>
    /// One bare symbol code that gets labelled with itself. "CB" beside the catch
    /// basin symbol, on the storm text layer -- the code list, verbatim. The layer
    /// comes from the office standard for that feature family; the optional label
    /// overrides the default of the code exactly as shot.
    /// </summary>
    public sealed class SymbolLabelRule
    {
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("layer")] public string Layer { get; set; }

        /// <summary>Absent means the label says the code exactly as shot.</summary>
        [JsonProperty("label")] public string Label { get; set; }

        /// <summary>Absent means enabled, so no existing file changes behaviour.</summary>
        [JsonProperty("enabled")] public bool Enabled { get; set; }

        public SymbolLabelRule() { Enabled = true; }
    }

    /// <summary>
    /// Identifies one kind of Civil 3D linework: the figure code the field crew shot
    /// and the layer the office Figure Prefix Database assigns it. Identification
    /// only -- what the finished label says is a separate, deliberately unconfigured
    /// standard. FTF never creates this linework; Civil 3D does.
    /// </summary>
    public sealed class LineFeatureRule
    {
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("layer")] public string Layer { get; set; }

        /// <summary>
        /// What the line label says ("CHAIN LINK FENCE"). Absent means the labelling
        /// standard is not configured: the feature is identified in the inventory but
        /// nothing is drawn for it.
        /// </summary>
        [JsonProperty("label")] public string Label { get; set; }

        /// <summary>Layer for this feature's labels. Absent uses the default line
        /// label layer from settings.</summary>
        [JsonProperty("labelLayer")] public string LabelLayer { get; set; }

        /// <summary>Which side of the line this feature labels on: "Left", "Right",
        /// "OnLine", or absent for Auto (fall through to the settings default). A
        /// directional modifier in the source coding overrides this per line.</summary>
        [JsonProperty("placement")] public string Placement { get; set; }

        /// <summary>
        /// Known TBC export names that identify this feature ("Edge of Conc" -> EC).
        /// Matching is exact after trimming, case-insensitive -- never substring,
        /// never fuzzy. Every entry is a confirmed office mapping, not a guess.
        /// </summary>
        [JsonProperty("aliases")] public List<string> Aliases { get; set; }

        /// <summary>
        /// Alternate wordings the surveyor can switch to while placing this label
        /// interactively -- "EDGE OF PAVEMENT" or "EOP". The main Label stays the
        /// default; these are offered by FTFLABELLINE's Wording option.
        /// </summary>
        [JsonProperty("altLabels")] public List<string> AltLabels { get; set; }

        /// <summary>
        /// What the SURFACE between two of these edges is called -- the label
        /// FTFLABELBETWEEN places. An asphalt edge labels "EDGE OF PAVEMENT" along
        /// itself, but the area between two of them is "ASPHALT" / "ASPH". First
        /// entry is the default; the rest are Wording alternates. Empty falls back
        /// to the edge label.
        /// </summary>
        [JsonProperty("betweenLabels")] public List<string> BetweenLabels { get; set; }

        /// <summary>Absent means enabled, so no existing file changes behaviour.</summary>
        [JsonProperty("enabled")] public bool Enabled { get; set; }

        public LineFeatureRule()
        {
            Enabled = true;
            Aliases = new List<string>();
            AltLabels = new List<string>();
            BetweenLabels = new List<string>();
        }
    }

    public sealed class CodeRule
    {
        [JsonProperty("id")] public string Id { get; set; }

        /// <summary>
        /// A disabled rule stops matching entirely: its codes fall through to the
        /// bare-code / unknown handling as if the rule did not exist. Absent means
        /// enabled, so no existing rules file changes behaviour.
        /// </summary>
        [JsonProperty("enabled")] public bool Enabled { get; set; }

        /// <summary>Optional human note shown in the rule editor.</summary>
        [JsonProperty("description")] public string Description { get; set; }

        /// <summary>
        /// Regex with named groups. Recognised group names: code, species, trunk, drip,
        /// rotation, mods. Any other named group is available to label templates.
        /// </summary>
        [JsonProperty("match")] public string Match { get; set; }

        [JsonProperty("species")] public Dictionary<string, string> Species { get; set; }

        /// <summary>
        /// OPT IN, DEFAULT FALSE. Civil 3D's description keys already place the survey
        /// symbol; FTF polishes that result rather than recreating it. Naming a block
        /// therefore inserts nothing on its own -- this flag has to be turned on as
        /// well, deliberately, for a symbol Civil 3D genuinely cannot provide.
        ///
        /// Without it, adding one "block" key to an ordinary rule would silently put a
        /// second symbol on top of every point that rule matched.
        /// </summary>
        [JsonProperty("insertBlock")] public bool InsertBlock { get; set; }

        [JsonProperty("block")] public string Block { get; set; }
        [JsonProperty("blockLayer")] public string BlockLayer { get; set; }
        [JsonProperty("blockScaleFrom")] public string BlockScaleFrom { get; set; }
        [JsonProperty("blockScaleDivisor")] public double BlockScaleDivisor { get; set; }

        [JsonProperty("rotation")] public RotationRule Rotation { get; set; }
        [JsonProperty("dripLine")] public DripRule DripLine { get; set; }
        [JsonProperty("label")] public LabelRule Label { get; set; }
        [JsonProperty("tagPrefix")] public string TagPrefix { get; set; }
        [JsonProperty("validate")] public List<RangeCheck> Validate { get; set; }

        [JsonIgnore] public Regex Regex { get; set; }

        public CodeRule()
        {
            Enabled = true;
            Species = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            BlockScaleDivisor = 1.0;
            Validate = new List<RangeCheck>();
        }
    }

    public enum RotationMode
    {
        /// <summary>Degrees clockwise from north (survey convention).</summary>
        Azimuth,
        /// <summary>Degrees counter-clockwise from east (AutoCAD convention).</summary>
        Cartesian
    }

    public sealed class RotationRule
    {
        [JsonProperty("from")] public string From { get; set; }

        [JsonProperty("mode")]
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public RotationMode Mode { get; set; }

        public RotationRule() { Mode = RotationMode.Azimuth; }
    }

    public sealed class DripRule
    {
        [JsonProperty("layer")] public string Layer { get; set; }
        /// <summary>Named group holding the drip radius, in survey feet.</summary>
        [JsonProperty("radiusFrom")] public string RadiusFrom { get; set; }
        [JsonProperty("unify")] public bool Unify { get; set; }

        public DripRule() { Unify = true; }
    }

    public sealed class LabelRule
    {
        [JsonProperty("format")] public string Format { get; set; }

        /// <summary>
        /// Used instead of <see cref="Format"/> when more than one stem diameter was
        /// recorded, because more than one diameter is what makes it a cluster.
        /// Falls back to Format when not set.
        /// </summary>
        [JsonProperty("clusterFormat")] public string ClusterFormat { get; set; }

        [JsonProperty("layer")] public string Layer { get; set; }

        [JsonProperty("leader")]
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public LeaderMode Leader { get; set; }

        public LabelRule() { Leader = LeaderMode.Auto; }
    }

    public sealed class ModifierRule
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("pattern")] public string Pattern { get; set; }

        /// <summary>Lower numbers apply first. Application order is by priority,
        /// never by the order tokens appear in the description.</summary>
        [JsonProperty("priority")] public int Priority { get; set; }

        /// <summary>Replaces the base label format entirely.</summary>
        [JsonProperty("labelFormat")] public string LabelFormat { get; set; }

        /// <summary>Appended to whatever the label currently is.</summary>
        [JsonProperty("labelSuffix")] public string LabelSuffix { get; set; }

        [JsonProperty("block")] public string Block { get; set; }
        [JsonProperty("layerSuffix")] public string LayerSuffix { get; set; }

        /// <summary>Named group in this modifier's pattern holding a stem count.</summary>
        [JsonProperty("stemCountFrom")] public string StemCountFrom { get; set; }

        /// <summary>Emit a warning when this modifier is present, for review layers.</summary>
        [JsonProperty("flag")] public bool Flag { get; set; }

        [JsonIgnore] public Regex Regex { get; set; }
    }

    /// <summary>
    /// LEGACY. These values moved to ftf-settings.json, which FTFSETUP edits. This
    /// class survives only so a rules file written before the settings file existed
    /// can seed one -- see FtfSettings.MigrateFrom. Nothing reads it at draw time.
    ///
    /// characterWidthFactor is gone entirely: label collision boxes are measured from
    /// the text AutoCAD actually produces, so there is nothing left to estimate.
    /// </summary>
    public sealed class LabelPlacementRule
    {
        /// <summary>Text height as plotted.</summary>
        [JsonProperty("textHeightPlotted")] public double TextHeightPlotted { get; set; }

        /// <summary>Breathing room added around the text box, as plotted.</summary>
        [JsonProperty("paddingPlotted")] public double PaddingPlotted { get; set; }

        /// <summary>Gap between the point and the near edge of the label, as plotted.</summary>
        [JsonProperty("baseOffsetPlotted")] public double BaseOffsetPlotted { get; set; }

        /// <summary>How much further out each successive ring sits, as plotted.</summary>
        [JsonProperty("ringStepPlotted")] public double RingStepPlotted { get; set; }

        /// <summary>Rings tried, including the base ring.</summary>
        [JsonProperty("ringCount")] public int RingCount { get; set; }

        /// <summary>How far a label may drift from its stored XData position before
        /// it counts as having been moved by hand. In drawing units.</summary>
        [JsonProperty("movedTolerance")] public double MovedTolerance { get; set; }

        public LabelPlacementRule()
        {
            TextHeightPlotted = 0.08;
            PaddingPlotted = 0.02;
            BaseOffsetPlotted = 0.06;
            RingStepPlotted = 0.06;
            RingCount = 4;
            MovedTolerance = 1e-4;
        }
    }

    public sealed class RangeCheck
    {
        [JsonProperty("field")] public string Field { get; set; }
        [JsonProperty("min")] public double? Min { get; set; }
        [JsonProperty("max")] public double? Max { get; set; }
        [JsonProperty("message")] public string Message { get; set; }

        [JsonProperty("severity")]
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public Severity Severity { get; set; }

        public RangeCheck() { Severity = Severity.Warning; }
    }

    public class ConfigException : Exception
    {
        public ConfigException(string message) : base(message) { }
        public ConfigException(string message, Exception inner) : base(message, inner) { }
    }
}
