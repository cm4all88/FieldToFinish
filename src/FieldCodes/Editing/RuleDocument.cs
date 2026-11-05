using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Editing
{
    /// <summary>
    /// The surveyor-facing view of one feature rule: the concepts the editor exposes,
    /// nothing more. Everything absent from this class is preserved untouched in the
    /// underlying document.
    /// </summary>
    public sealed class EditableRule
    {
        public string Id { get; set; }
        public bool Enabled { get; set; }
        public string Description { get; set; }

        /// <summary>The match pattern. Advanced; validated before it can be saved.</summary>
        public string Match { get; set; }

        public string LabelFormat { get; set; }
        public string LabelLayer { get; set; }
        public string Leader { get; set; }
        public string TagPrefix { get; set; }

        /// <summary>Label All family (Trees, Utilities, StormSewer, Signs, Control, Pavement,
        /// OtherPoints); blank means worked out from the rule and code.</summary>
        public string Category { get; set; }

        /// <summary>Editable only when the rule already has a dripline.</summary>
        public bool HasDripline { get; set; }
        public string DripLayer { get; set; }

        // Read-only context the editor displays but never writes.
        public string RotationSummary { get; set; }
        public string SymbolSummary { get; set; }
        public bool InTable { get { return !string.IsNullOrEmpty(TagPrefix); } }
    }

    /// <summary>One line feature as the Line Labels page edits it.</summary>
    public sealed class EditableLineFeature
    {
        public string Code { get; set; }
        public string Name { get; set; }
        /// <summary>The layer the figure is identified on (read only here).</summary>
        public string Layer { get; set; }
        /// <summary>What the label says; blank means "identified, not labelled".</summary>
        public string Label { get; set; }
        public string LabelLayer { get; set; }
        /// <summary>Left, Right, OnLine, or blank for Auto.</summary>
        public string Placement { get; set; }
    }

    /// <summary>
    /// One symbol rule of the orientation standard (rules.json "orientation" > "symbols") as
    /// Settings > Symbols and Rotations edits it: which codes, what kind of feature, the block it
    /// is drawn with, the block's native zero and how often it repeats.
    /// </summary>
    public sealed class EditableSymbol
    {
        public EditableSymbol()
        {
            Codes = new List<string>();
            Kind = FieldCodes.Orient.OrientKind.Other.ToString();
            Period = 360;
            SourceIndex = -1;
        }

        /// <summary>Where the entry sat in the file's list when it was read (-1: a new row, or a
        /// built-in PMX row not yet in the file). Writing the list back starts from that entry, so
        /// its comments and any property this build does not know survive.</summary>
        public int SourceIndex { get; set; }

        /// <summary>Point codes, wildcards * and ? allowed. First matching rule wins.</summary>
        public List<string> Codes { get; set; }

        /// <summary>An <see cref="FieldCodes.Orient.OrientKind"/> name.</summary>
        public string Kind { get; set; }

        /// <summary>The block it is drawn with, for people and the preview (wildcards allowed).</summary>
        public string Block { get; set; }

        /// <summary>Native zero: the AutoCAD direction the block's meaningful direction points at rotation 0.</summary>
        public double ReferenceCadDegrees { get; set; }

        /// <summary>360 one direction, 180 an axis, 0 never turned.</summary>
        public double Period { get; set; }

        public bool TwoSided { get; set; }
        public bool RotateLabel { get; set; }
        public string Meaning { get; set; }

        public string CodesText { get { return string.Join(" ", (Codes ?? new List<string>()).ToArray()); } }

        /// <summary>"SN*, snst wv" -> SN*, SNST, WV: codes as typed in one box.</summary>
        public static List<string> ParseCodes(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim().ToUpperInvariant())
                .Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The rule as FTFPOINTS / FTFORIENT would use it (an unknown kind reads as Other).</summary>
        public FieldCodes.Orient.SymbolRule ToRule()
        {
            FieldCodes.Orient.OrientKind kind;
            if (!Enum.TryParse(Kind ?? string.Empty, true, out kind)) kind = FieldCodes.Orient.OrientKind.Other;
            return new FieldCodes.Orient.SymbolRule
            {
                Codes = (Codes ?? new List<string>()).ToList(), Kind = kind, Block = Block,
                ReferenceCadDegrees = ReferenceCadDegrees, Period = Period, TwoSided = TwoSided,
                RotateLabel = RotateLabel, Meaning = Meaning
            };
        }

        public static EditableSymbol From(FieldCodes.Orient.SymbolRule rule, int sourceIndex)
        {
            return new EditableSymbol
            {
                SourceIndex = sourceIndex,
                Codes = (rule.Codes ?? new List<string>()).ToList(),
                Kind = rule.Kind.ToString(),
                Block = rule.Block,
                ReferenceCadDegrees = rule.ReferenceCadDegrees,
                Period = rule.Period,
                TwoSided = rule.TwoSided,
                RotateLabel = rule.RotateLabel,
                Meaning = rule.Meaning
            };
        }

        public EditableSymbol Clone()
        {
            var copy = (EditableSymbol)MemberwiseClone();
            copy.Codes = (Codes ?? new List<string>()).ToList();
            return copy;
        }
    }

    /// <summary>When FTFORIENT turns a marker, and the office's approved symbol library.</summary>
    public sealed class EditableOrientationApply
    {
        /// <summary>applyMinimumConfidence, 0 to 1.</summary>
        public double MinimumConfidence { get; set; }

        /// <summary>applyWeakestTier: Measured, Relationship, Alignment or Inferred.</summary>
        public string WeakestTier { get; set; }

        /// <summary>symbolLibrary: a drawing of approved symbol blocks; blank for none.</summary>
        public string SymbolLibrary { get; set; }

        /// <summary>markingDirectionWords: read "ASTO S" / "SB" / "SOUTHBOUND" as the direction of travel.</summary>
        public bool MarkingDirectionWords { get; set; }

        /// <summary>markingFollowRoad: match the written direction to the lane's road line.</summary>
        public bool MarkingFollowRoad { get; set; }

        /// <summary>markingRoadFeet: how far a road line may be to be matched.</summary>
        public double MarkingRoadFeet { get; set; }

        /// <summary>markingMaxTurnDegrees: the most the road may turn the written direction.</summary>
        public double MarkingMaxTurnDegrees { get; set; }

        /// <summary>roadSearchFeet: how far from a feature road lines are looked for.</summary>
        public double RoadSearchFeet { get; set; }

        /// <summary>maxExtensionFeet: how far a road line that stops short may be followed past its end.</summary>
        public double MaxExtensionFeet { get; set; }

        /// <summary>localTangentFeet: the length of road line its direction is taken over.</summary>
        public double LocalTangentFeet { get; set; }

        /// <summary>alignmentToleranceDegrees: road lines further apart than this disagree.</summary>
        public double AlignmentToleranceDegrees { get; set; }

        /// <summary>intersectionHandling: Approach or Review.</summary>
        public string IntersectionHandling { get; set; }

        /// <summary>handTurned: Keep or MeasuredReplaces.</summary>
        public string HandTurned { get; set; }

        /// <summary>qcGraphics: Off, ReviewOnly or All.</summary>
        public string QcGraphics { get; set; }

        /// <summary>qcShowInferredConnections.</summary>
        public bool QcShowInferredConnections { get; set; }

        /// <summary>fallbacks: per kind (Sign, StreetLight, SignalPole, Hydrant, PavementArrow, PavementWord,
        /// WaterValve, GasValve), Apply, Review or Leave.</summary>
        public Dictionary<string, string> Fallbacks { get; set; }

        /// <summary>roadReferences: how far each kind of road line is trusted (Centerline, LaneLine, Edge,
        /// RoadContext), 0 to 1. One-way lines are trusted as centre lines.</summary>
        public Dictionary<string, double> Reliability { get; set; }

        /// <summary>The kinds whose fallback the Settings page edits.</summary>
        public static readonly string[] FallbackKinds =
            { "Sign", "StreetLight", "SignalPole", "Hydrant", "PavementArrow", "PavementWord", "WaterValve", "GasValve" };

        /// <summary>The road line kinds whose trust the Settings page edits.</summary>
        public static readonly string[] ReliabilityRoles = { "Centerline", "LaneLine", "Edge", "RoadContext" };
    }

    /// <summary>
    /// How FTFPOINTS inserts a block for a code rule that opted in (insertBlock). The opt-in itself
    /// is shown here but written only by <see cref="RuleDocument.SetInsertBlock"/>, which the editor
    /// calls after the user confirms: a second symbol on top of Civil 3D's is never a side effect.
    /// </summary>
    public sealed class EditableBlockInsert
    {
        public EditableBlockInsert()
        {
            BlockScale = 1.0;
            BlockScaleDivisor = 1.0;
            Attributes = new List<KeyValuePair<string, string>>();
        }

        public string Id { get; set; }
        public string Description { get; set; }

        /// <summary>Read only through this object: see <see cref="RuleDocument.SetInsertBlock"/>.</summary>
        public bool InsertBlock { get; set; }

        public string Block { get; set; }
        public string BlockLayer { get; set; }

        /// <summary>Fixed scale, used when <see cref="BlockScaleFrom"/> is blank.</summary>
        public double BlockScale { get; set; }

        /// <summary>A field (named group or derived field) the scale is read from; blank for a fixed scale.</summary>
        public string BlockScaleFrom { get; set; }
        public double BlockScaleDivisor { get; set; }

        public FieldCodes.Symbols.BlockPlacement Placement { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }

        public FieldCodes.Symbols.BlockRotationMethod Rotation { get; set; }

        /// <summary>The field the rule reads a rotation from, or blank (read only context).</summary>
        public string FieldRotation { get; set; }

        /// <summary>Attribute tag and text template, in order.</summary>
        public List<KeyValuePair<string, string>> Attributes { get; set; }
    }

    /// <summary>
    /// The rules file as an editable document.
    ///
    /// All edits happen on the JSON DOM, never by deserialising and re-serialising:
    /// the file carries documentation comments ("//..." keys), and future versions
    /// may add properties this build has never heard of. A round trip through the
    /// typed objects would silently destroy both. This class writes exactly the
    /// properties the editor owns and leaves every other byte of structure alone --
    /// an older UI can never delete configuration added later.
    ///
    /// Saving is atomic: the new content is fully serialised and validated first,
    /// written to a temp file, and swapped in with a backup. The original file as it
    /// was before the very first edit is kept permanently alongside.
    /// </summary>
    public sealed partial class RuleDocument
    {
        /// <summary>Kept forever: the file as it was before the editor's first write.</summary>
        public const string OriginalBackupSuffix = ".original.bak";

        private readonly JObject _root;

        private RuleDocument(JObject root)
        {
            _root = root;
        }

        public static RuleDocument Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Rules file not found.", path);
            return FromText(File.ReadAllText(path));
        }

        public static RuleDocument FromText(string json)
        {
            try
            {
                return new RuleDocument(JObject.Parse(json));
            }
            catch (JsonException ex)
            {
                throw new ConfigException("Rules file is not valid JSON: " + ex.Message, ex);
            }
        }

        // ------------------------------------------------------------------ reading

        public IList<string> RuleIds()
        {
            return Rules().Select(r => (string)r["id"]).Where(id => id != null).ToList();
        }

        public EditableRule GetEditable(string id)
        {
            var rule = FindRule(id);
            if (rule == null) return null;

            var label = rule["label"] as JObject;
            var drip = rule["dripLine"] as JObject;
            var rotation = rule["rotation"] as JObject;

            var editable = new EditableRule
            {
                Id = id,
                Enabled = rule["enabled"] == null || rule["enabled"].Value<bool>(),
                Description = (string)rule["description"] ?? string.Empty,
                Match = (string)rule["match"] ?? string.Empty,
                LabelFormat = label != null ? (string)label["format"] ?? string.Empty : string.Empty,
                LabelLayer = label != null ? (string)label["layer"] ?? string.Empty : string.Empty,
                Leader = label != null ? (string)label["leader"] ?? "Auto" : "Auto",
                TagPrefix = (string)rule["tagPrefix"] ?? string.Empty,
                Category = (string)rule["category"] ?? string.Empty,
                HasDripline = drip != null,
                DripLayer = drip != null ? (string)drip["layer"] ?? string.Empty : string.Empty
            };

            editable.RotationSummary = rotation == null
                ? "None"
                : ((string)rotation["mode"] ?? "Azimuth") +
                  " from field '" + ((string)rotation["from"] ?? "?") + "'";

            var insert = rule["insertBlock"] != null && rule["insertBlock"].Value<bool>();
            editable.SymbolSummary = insert
                ? "FTF inserts block " + ((string)rule["block"] ?? "?") + " (opt-in rule)"
                : "Civil 3D (description keys) - FTF inserts nothing";

            return editable;
        }

        // ------------------------------------------------------------------ editing

        /// <summary>
        /// Writes the editable fields back onto the document. Only the properties the
        /// editor owns are touched; sibling properties (clusterFormat, validate,
        /// species, unknown future keys, comments) are untouched by construction.
        /// </summary>
        public void ApplyEdit(EditableRule edit)
        {
            if (edit == null) throw new ArgumentNullException("edit");

            var rule = FindRule(edit.Id);
            if (rule == null)
                throw new ConfigException("No rule with id '" + edit.Id + "'.");

            // enabled: absent means true, so the property only exists when disabling.
            if (edit.Enabled) rule.Remove("enabled");
            else rule["enabled"] = false;

            SetOrRemove(rule, "description", edit.Description);
            if (!string.IsNullOrWhiteSpace(edit.Match)) rule["match"] = edit.Match;
            SetOrRemove(rule, "tagPrefix", edit.TagPrefix);
            SetOrRemove(rule, "category", edit.Category);

            // Label: created when text is set on a rule that had none; the object is
            // only removed when it would carry nothing at all, so a clusterFormat or
            // any future property keeps it alive.
            var label = rule["label"] as JObject;
            var wantsLabel = !string.IsNullOrWhiteSpace(edit.LabelFormat);

            if (label == null && wantsLabel)
            {
                label = new JObject();
                rule["label"] = label;
            }

            if (label != null)
            {
                SetOrRemove(label, "format", edit.LabelFormat);
                SetOrRemove(label, "layer", edit.LabelLayer);

                if (!string.IsNullOrWhiteSpace(edit.Leader) && edit.Leader != "Auto")
                    label["leader"] = edit.Leader;
                else
                    label.Remove("leader");

                if (!label.Properties().Any())
                    rule.Remove("label");
            }

            // Dripline: the editor can retarget its layer, never create or delete the
            // dripline itself -- that is a feature-design decision, not a setting.
            var drip = rule["dripLine"] as JObject;
            if (drip != null && !string.IsNullOrWhiteSpace(edit.DripLayer))
                drip["layer"] = edit.DripLayer;
        }

        private static void SetOrRemove(JObject owner, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) owner.Remove(name);
            else owner[name] = value;
        }

        // --------------------------------------------------------------- validation

        /// <summary>Empty list means the document is safe to save.</summary>
        public IList<string> Validate()
        {
            var problems = new List<string>();

            // Checked on the document first: a symbol kind the enum does not know would stop the
            // typed read below with a JSON error that names no row.
            OrientationProblems(problems);
            WetlandBufferProblems(problems);
            BenchProblems(problems);
            if (problems.Count > 0) return problems;

            RulesConfig cfg;
            try
            {
                cfg = JsonConvert.DeserializeObject<RulesConfig>(Render());
                if (cfg == null) { problems.Add("Rules document is empty."); return problems; }
                cfg.Compile();
            }
            catch (ConfigException ex)
            {
                problems.Add(ex.Message);
                return problems;
            }
            catch (JsonException ex)
            {
                problems.Add("Not valid JSON: " + ex.Message);
                return problems;
            }

            EditorChecks(cfg, problems);
            if (!SymbolsAreBuiltIn() && cfg.Orientation != null)
                SymbolShadowing(cfg.Orientation.Symbols, problems);
            return problems;
        }

        /// <summary>
        /// The checks Compile() does not make: they only matter once rules are being
        /// edited by hand in a UI rather than authored deliberately in the file.
        /// </summary>
        private static void EditorChecks(RulesConfig cfg, List<string> problems)
        {
            var derivedFields = new[] { "trunk.stems", "trunk.count", "trunk.exact" };
            var modifierGroups = cfg.Modifiers
                .Where(m => m.Regex != null)
                .SelectMany(m => m.Regex.GetGroupNames())
                .Where(NotNumeric)
                .ToList();

            foreach (var lf in cfg.LineFeatures ?? new List<LineFeatureRule>())
            {
                CheckLayerName("Line feature '" + lf.Code + "': label layer", lf.LabelLayer, problems);
                if (!string.IsNullOrWhiteSpace(lf.Placement) &&
                    !new[] { "Left", "Right", "OnLine" }.Contains(lf.Placement.Trim(), StringComparer.OrdinalIgnoreCase))
                    problems.Add("Line feature '" + lf.Code + "': side must be Left, Right or OnLine (or blank for Auto).");
            }

            foreach (var rule in cfg.Codes)
            {
                if (rule.Regex == null) continue;   // already reported by Compile
                var where = "Rule '" + rule.Id + "': ";

                var groups = rule.Regex.GetGroupNames().Where(NotNumeric).ToList();

                if (rule.Label != null && !string.IsNullOrWhiteSpace(rule.Label.Format) &&
                    string.IsNullOrWhiteSpace(rule.Label.Layer))
                    problems.Add(where + "a label needs a layer.");

                CheckLayerName(where + "label layer",
                    rule.Label != null ? rule.Label.Layer : null, problems);
                CheckLayerName(where + "dripline layer",
                    rule.DripLine != null ? rule.DripLine.Layer : null, problems);

                FieldCodes.Settings.AnnotationCategory family;
                if (!string.IsNullOrWhiteSpace(rule.Category) &&
                    (!Enum.TryParse(rule.Category.Trim(), true, out family) || family == FieldCodes.Settings.AnnotationCategory.Lines))
                    problems.Add(where + "category '" + rule.Category + "' is not a point family " +
                                 "(Trees, Utilities, StormSewer, Signs, Control, Pavement, OtherPoints).");

                if (!string.IsNullOrEmpty(rule.TagPrefix) &&
                    !Regex.IsMatch(rule.TagPrefix, "^[A-Za-z_-]+$"))
                    problems.Add(where + "tag prefix '" + rule.TagPrefix +
                                 "' may only contain letters, '-' or '_' -- a digit would " +
                                 "merge with the tag number.");

                if (rule.Rotation != null &&
                    (string.IsNullOrWhiteSpace(rule.Rotation.From) ||
                     !groups.Contains(rule.Rotation.From)))
                    problems.Add(where + "rotation reads field '" +
                                 (rule.Rotation.From ?? "(none)") +
                                 "', which is not a named group in the pattern.");

                var valid = new HashSet<string>(groups, StringComparer.OrdinalIgnoreCase);
                foreach (var f in derivedFields) valid.Add(f);
                foreach (var g in modifierGroups) valid.Add(g);

                if (rule.Notes != null)
                {
                    // The note groups are fields too, and {notes} is all of them in order.
                    foreach (var g in rule.Notes.GroupNames()) valid.Add(g);
                    valid.Add("notes");
                    if (!groups.Contains("mods"))
                        problems.Add(where + "has notes but its pattern has no (?<mods>...) group, so no note is ever read.");

                    foreach (var term in rule.Notes.Terms.Where(t => t != null && t.Regex != null))
                    {
                        var termGroups = new HashSet<string>(term.Regex.GetGroupNames().Where(NotNumeric), StringComparer.OrdinalIgnoreCase);
                        termGroups.Add("token");
                        CheckPlaceholders(where + "note '" + term.Match + "' wording", term.Text, termGroups, problems);
                    }
                }

                if (rule.Label != null)
                {
                    CheckPlaceholders(where + "label", rule.Label.Format, valid, problems);
                    CheckPlaceholders(where + "cluster label", rule.Label.ClusterFormat, valid, problems);
                }

                CheckBlockInsertion(where, rule, valid, problems);
            }

            CheckConflicts(cfg, problems);
        }

        /// <summary>The opted-in block's settings: a name to insert, a scale that can be worked
        /// out, attribute templates that name real fields.</summary>
        private static void CheckBlockInsertion(string where, CodeRule rule, HashSet<string> valid, List<string> problems)
        {
            if (rule.InsertBlock && string.IsNullOrWhiteSpace(rule.Block))
                problems.Add(where + "inserts a block (insertBlock) but names no block.");
            CheckLayerName(where + "block layer", rule.BlockLayer, problems);

            if (string.IsNullOrWhiteSpace(rule.BlockScaleFrom))
            {
                if (!(rule.BlockScale > 0) || double.IsInfinity(rule.BlockScale))
                    problems.Add(where + "block scale must be a number greater than zero.");
            }
            else
            {
                if (!valid.Contains(rule.BlockScaleFrom.Trim()))
                    problems.Add(where + "block scale reads field '" + rule.BlockScaleFrom +
                                 "', which is neither a named group in the pattern nor a derived field.");
                if (Math.Abs(rule.BlockScaleDivisor) < 1e-9 || double.IsNaN(rule.BlockScaleDivisor))
                    problems.Add(where + "block scale divisor cannot be zero.");
            }

            if (double.IsNaN(rule.BlockOffsetX) || double.IsInfinity(rule.BlockOffsetX) ||
                double.IsNaN(rule.BlockOffsetY) || double.IsInfinity(rule.BlockOffsetY))
                problems.Add(where + "block offset must be a number.");

            if (rule.BlockAttributes == null || rule.BlockAttributes.Count == 0) return;
            var tokens = new HashSet<string>(valid, StringComparer.OrdinalIgnoreCase);
            foreach (var t in FieldCodes.Symbols.AttributeTemplate.BuiltInTokens) tokens.Add(t);
            tokens.Add("code");
            tokens.Add("species");
            foreach (var kv in rule.BlockAttributes)
            {
                if (kv.Key.StartsWith("//", StringComparison.Ordinal)) continue;
                if (!FieldCodes.Symbols.AttributeTemplate.IsValidTag(kv.Key))
                    problems.Add(where + "attribute tag '" + kv.Key + "' cannot be blank or contain spaces or braces.");
                CheckPlaceholders(where + "attribute " + kv.Key, kv.Value, tokens, problems);
            }
        }

        // --------------------------------------------------------- orientation checks

        private static readonly double[] Periods = { 0, 180, 360 };

        /// <summary>The "orientation" section as written: thresholds in range, and every symbol
        /// row with codes, a kind the standard knows, a native zero of 0-360 and a period of 0, 180
        /// or 360. Run on the document, so a row that would not even load is named by its number.</summary>
        private void OrientationProblems(List<string> problems)
        {
            var o = _root["orientation"] as JObject;
            if (o == null) return;
            const string page = "Symbols and Rotations: ";

            var conf = o["applyMinimumConfidence"];
            double v;
            if (conf != null && (!TryNumber(conf, out v) || v < 0 || v > 1))
                problems.Add(page + "the confidence a marker needs before FTFORIENT turns it must be between 0 and 1.");

            var roadFeet = o["markingRoadFeet"];
            if (roadFeet != null && (!TryNumber(roadFeet, out v) || v <= 0 || v > 500))
                problems.Add(page + "the distance a road line may be from a pavement marking must be more than 0 and at most 500 ft.");
            var maxTurn = o["markingMaxTurnDegrees"];
            if (maxTurn != null && (!TryNumber(maxTurn, out v) || v < 0 || v > 90))
                problems.Add(page + "the most the road may turn a marking's written direction must be between 0 and 90 degrees.");

            foreach (var range in new[]
            {
                Tuple.Create("roadSearchFeet", 1.0, 500.0, "the distance road lines are looked for must be between 1 and 500 ft."),
                Tuple.Create("maxExtensionFeet", 0.0, 200.0, "how far a road line may be followed past its end must be between 0 and 200 ft."),
                Tuple.Create("localTangentFeet", 1.0, 200.0, "the length of road line its direction is taken over must be between 1 and 200 ft."),
                Tuple.Create("alignmentToleranceDegrees", 1.0, 90.0, "the angle at which road lines disagree must be between 1 and 90 degrees."),
                Tuple.Create("qcTextPlotted", 0.01, 1.0, "the plotted height of QC text must be between 0.01 and 1 inch.")
            })
            {
                var t = o[range.Item1];
                if (t != null && (!TryNumber(t, out v) || v < range.Item2 || v > range.Item3)) problems.Add(page + range.Item4);
            }
            foreach (var word in new[]
            {
                Tuple.Create("intersectionHandling", typeof(FieldCodes.Orient.IntersectionHandling)),
                Tuple.Create("handTurned", typeof(FieldCodes.Orient.HandTurnedRotation)),
                Tuple.Create("qcGraphics", typeof(FieldCodes.Orient.QcGraphicsMode))
            })
            {
                var t = o[word.Item1];
                if (t != null && t.Type != JTokenType.Null && !Enum.GetNames(word.Item2).Any(n => string.Equals(n, t.ToString(), StringComparison.OrdinalIgnoreCase)))
                    problems.Add(page + word.Item1 + " must be one of " + string.Join(", ", Enum.GetNames(word.Item2)) + " (not '" + t + "').");
            }
            var fallbacks = o["fallbacks"];
            if (fallbacks != null && fallbacks.Type != JTokenType.Null)
            {
                var obj = fallbacks as JObject;
                if (obj == null) problems.Add(page + "\"fallbacks\" must name kinds of feature, each Apply, Review or Leave.");
                else
                    foreach (var p in obj.Properties())
                    {
                        FieldCodes.Orient.OrientKind k;
                        FieldCodes.Orient.OrientFallback f;
                        if (!Enum.TryParse(p.Name, true, out k) || !Enum.IsDefined(typeof(FieldCodes.Orient.OrientKind), k))
                            problems.Add(page + "fallback for '" + p.Name + "': not a kind of feature FTFORIENT knows.");
                        else if (p.Value.Type != JTokenType.String || !Enum.TryParse(p.Value.ToString(), true, out f) || !Enum.IsDefined(typeof(FieldCodes.Orient.OrientFallback), f))
                            problems.Add(page + "fallback for " + p.Name + " must be Apply, Review or Leave (not '" + p.Value + "').");
                    }
            }
            var references = o["roadReferences"];
            if (references != null && references.Type != JTokenType.Null)
            {
                var arr = references as JArray;
                if (arr == null) problems.Add(page + "\"roadReferences\" must be a list of { role, reliability }.");
                else
                    foreach (var entry in arr)
                    {
                        var e = entry as JObject;
                        FieldCodes.Orient.LineRole role;
                        if (e == null || e["role"] == null || !Enum.TryParse(e["role"].ToString(), true, out role))
                        { problems.Add(page + "a road reference needs a role (Centerline, OneWay, LaneLine, Edge, RoadContext)."); continue; }
                        if (e["reliability"] == null || !TryNumber(e["reliability"], out v) || v < 0 || v > 1)
                            problems.Add(page + "the trust in " + role + " lines must be between 0 and 1.");
                    }
            }

            var tier = o["applyWeakestTier"];
            if (tier != null && tier.Type != JTokenType.Null)
            {
                FieldCodes.Orient.OrientationTier t;
                if (!Enum.TryParse(tier.ToString(), true, out t) || !Enum.IsDefined(typeof(FieldCodes.Orient.OrientationTier), t) ||
                    t == FieldCodes.Orient.OrientationTier.Review || t == FieldCodes.Orient.OrientationTier.NotDirectional)
                    problems.Add(page + "the weakest evidence applied must be Measured, Relationship, Alignment or Inferred (not '" + tier + "').");
            }

            var list = o["symbols"];
            if (list == null || list.Type == JTokenType.Null) return;
            var array = list as JArray;
            if (array == null) { problems.Add(page + "\"symbols\" must be a list."); return; }

            var row = 0;
            foreach (var token in array)
            {
                row++;
                var entry = token as JObject;
                if (entry == null) { problems.Add(page + "symbol rule " + row + " is not a rule (an object with codes, kind...)."); continue; }
                var codes = ReadCodes(entry["codes"]);
                var where = page + "symbol rule " + row + (codes.Count > 0 ? " (" + string.Join(" ", codes.ToArray()) + ")" : string.Empty) + ": ";

                if (codes.Count == 0)
                    problems.Add(where + "needs at least one code.");
                foreach (var c in codes.Where(c => c.IndexOfAny(new[] { ' ', '\t' }) >= 0))
                    problems.Add(where + "code '" + c + "' has a space in it; one code per entry.");

                var kind = entry["kind"];
                if (kind != null && kind.Type != JTokenType.Null)
                {
                    FieldCodes.Orient.OrientKind k;
                    if (!Enum.TryParse(kind.ToString(), true, out k) || !Enum.IsDefined(typeof(FieldCodes.Orient.OrientKind), k))
                        problems.Add(where + "kind '" + kind + "' is not one of " +
                                     string.Join(", ", Enum.GetNames(typeof(FieldCodes.Orient.OrientKind))) + ".");
                }

                var period = entry["period"];
                if (period != null && (!TryNumber(period, out v) || !Periods.Any(p => Math.Abs(p - v) < 1e-9)))
                    problems.Add(where + "repeats every '" + period + "' degrees; it must be 360 (one direction), " +
                                 "180 (an axis, such as a valve) or 0 (looks the same at every rotation: never turned).");

                var zero = entry["referenceCadDegrees"];
                if (zero != null && (!TryNumber(zero, out v) || v < 0 || v > 360))
                    problems.Add(where + "the native zero must be between 0 and 360 AutoCAD degrees (0 east, 90 north).");
            }
        }

        /// <summary>First match wins, so a code listed below a rule that already matches it is
        /// never used: almost always a specific code put under its family by mistake.</summary>
        private static void SymbolShadowing(IList<FieldCodes.Orient.SymbolRule> symbols, List<string> problems)
        {
            if (symbols == null) return;
            for (var j = 1; j < symbols.Count; j++)
            {
                foreach (var code in (symbols[j].Codes ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c) && c.IndexOfAny(new[] { '*', '?' }) < 0))
                {
                    for (var i = 0; i < j; i++)
                    {
                        var earlier = symbols[i].Codes ?? new List<string>();
                        if (!earlier.Any(p => FieldCodes.Orient.OrientationStandard.CodeMatches(p, code.Trim()))) continue;
                        problems.Add("Symbols and Rotations: symbol rule " + (j + 1) + " lists " + code +
                                     ", but rule " + (i + 1) + " (" + string.Join(" ", earlier.ToArray()) +
                                     ") matches it first, so it is never used. Move the more specific rule up.");
                        break;
                    }
                }
            }
        }

        private static void CheckLayerName(string where, string layer, List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(layer)) return;
            if (layer.IndexOfAny(new[]
                { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' }) >= 0)
                problems.Add(where + " '" + layer + "' contains characters AutoCAD does " +
                             "not allow in a layer name.");
        }

        private static readonly Regex Placeholder =
            new Regex(@"\{(?<name>[A-Za-z_][A-Za-z0-9_.]*)(?::[^}]+)?\}",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static void CheckPlaceholders(string where, string template,
                                              HashSet<string> valid, List<string> problems)
        {
            if (string.IsNullOrEmpty(template)) return;

            foreach (Match m in Placeholder.Matches(template))
            {
                var name = m.Groups["name"].Value;
                if (!valid.Contains(name))
                    problems.Add(where + " uses {" + name + "}, which is neither a named " +
                                 "group in the pattern nor a derived field.");

                // The format specifier after the colon is fed to double.ToString at
                // parse time. An invalid one used to pass validation here and then
                // throw on EVERY point, killing whole runs. Prove it formats now.
                var colon = m.Value.IndexOf(':');
                if (colon >= 0)
                {
                    var fmt = m.Value.Substring(colon + 1,
                        m.Value.Length - colon - 2);   // strip the closing brace
                    try
                    {
                        (0.0).ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch (FormatException)
                    {
                        problems.Add(where + " uses the number format '" + fmt +
                                     "' in {" + name + "}, which is not a valid " +
                                     ".NET numeric format.");
                    }
                }
            }
        }

        /// <summary>
        /// Two enabled rules claiming the same code would make the winner depend on
        /// file order. Detection is by probing each rule's literal codes against every
        /// other rule's real regex, so it can miss exotic patterns but never cries wolf.
        /// </summary>
        private static void CheckConflicts(RulesConfig cfg, List<string> problems)
        {
            var enabled = cfg.Codes.Where(c => c.Enabled && c.Regex != null).ToList();
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in enabled)
            {
                foreach (var token in LiteralCodes(rule))
                {
                    if (reported.Contains(token)) continue;

                    var probe = token + " 1";
                    var claimants = enabled
                        .Where(c => c.Regex.IsMatch(probe))
                        .Select(c => c.Id)
                        .ToList();

                    if (claimants.Count > 1)
                    {
                        reported.Add(token);
                        problems.Add("Code '" + token + "' is claimed by more than one " +
                                     "enabled rule: " + string.Join(", ", claimants.ToArray()) +
                                     ". Whichever loads first would win.");
                    }
                }
            }
        }

        /// <summary>Best-effort literal codes out of a rule pattern. Public because the
        /// editor UI shows them as the feature code column.</summary>
        public static IList<string> LiteralCodes(CodeRule rule)
        {
            if (rule.Match != null &&
                rule.Match.IndexOf("{SPECIES}", StringComparison.OrdinalIgnoreCase) >= 0)
                return rule.Species.Keys.ToList();

            var match = rule.Match ?? string.Empty;
            var start = match.IndexOf("(?<code>", StringComparison.Ordinal);
            if (start < 0) return new List<string>();

            start += 8;
            var depth = 1;
            var end = start;
            while (end < match.Length && depth > 0)
            {
                if (match[end] == '(') depth++;
                else if (match[end] == ')') depth--;
                if (depth > 0) end++;
            }

            var tokens = new List<string>();
            foreach (var alt in match.Substring(start, end - start).Split('|'))
            {
                var cleaned = alt.Replace("(?:", "").Replace(")", "").Replace("?", "").Trim();

                // A token still carrying regex machinery cannot be probed literally.
                if (cleaned.Length == 0 ||
                    cleaned.IndexOfAny(new[] { '[', ']', '\\', '(', '+', '*', '.', '^', '$', '{' }) >= 0)
                    continue;

                tokens.Add(cleaned);
            }

            return tokens;
        }

        // ------------------------------------------------------------------ output

        public string Render()
        {
            return _root.ToString(Formatting.Indented);
        }

        /// <summary>The document compiled as the parser would see it, for previews.</summary>
        public RulesConfig CompileCandidate()
        {
            var cfg = JsonConvert.DeserializeObject<RulesConfig>(Render());
            if (cfg == null) throw new ConfigException("Rules document is empty.");
            cfg.Compile();
            return cfg;
        }

        /// <summary>
        /// Atomic save. Order matters: serialise and validate completely before any
        /// file is touched, write to a temp file, then swap. A crash at any point
        /// leaves either the old file or the new file -- never a partial one.
        /// </summary>
        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");

            var problems = Validate();
            if (problems.Count > 0)
                throw new ConfigException("Refusing to save invalid rules:" +
                    Environment.NewLine + string.Join(Environment.NewLine, problems.ToArray()));

            var text = Render();

            // First save into the office level creates its directory.
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // The file as it was before the editor ever touched it, kept permanently.
            var original = path + OriginalBackupSuffix;
            if (File.Exists(path) && !File.Exists(original))
                File.Copy(path, original);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);

            if (File.Exists(path))
                File.Replace(tmp, path, path + ".bak");
            else
                File.Move(tmp, path);
        }

        // ------------------------------------------------------------------ helpers

        private JArray Rules()
        {
            return _root["codes"] as JArray ?? new JArray();
        }

        // ------------------------------------------------------------------ new rules

        /// <summary>
        /// Adds a starter rule for a code no rule handles yet (Unknown Codes > Configure Rule):
        /// the code followed by anything, labelled with the code, on the general text layer.
        /// It is a starting point to edit -- label text, layer, family -- and is only written
        /// when the rules are saved. Returns the new rule's id; an existing id is not replaced.
        /// </summary>
        public string AddStarterRule(string code, string labelLayer)
        {
            var clean = (code ?? string.Empty).Trim().ToUpperInvariant();
            if (!Regex.IsMatch(clean, "^[A-Z][A-Z0-9_-]*$"))
                throw new ConfigException("'" + code + "' is not a field code a rule can match (letters, then letters or digits).");
            var codes = _root["codes"] as JArray;
            if (codes == null) { codes = new JArray(); _root["codes"] = codes; }
            var id = clean.ToLowerInvariant();
            var n = 2;
            while (FindRule(id) != null) id = clean.ToLowerInvariant() + "-" + n++;
            codes.Add(new JObject
            {
                ["id"] = id,
                ["description"] = clean,
                ["match"] = "^(?<code>" + Regex.Escape(clean) + ")(?<mods>(?:\\s+\\S+)*)\\s*$",
                ["label"] = new JObject { ["format"] = "{code}", ["layer"] = string.IsNullOrWhiteSpace(labelLayer) ? "V-TEXT-E" : labelLayer.Trim() }
            });
            return id;
        }

        // ------------------------------------------------------------------ line features

        /// <summary>The line features (lineFeatures) as the Line Labels page edits them: what
        /// the label says, its layer and side. The figure code and the layer it is identified
        /// on are read only (they come from the office figure prefix database).</summary>
        public IList<EditableLineFeature> GetLineFeatures()
        {
            var list = new List<EditableLineFeature>();
            foreach (var f in (_root["lineFeatures"] as JArray ?? new JArray()).OfType<JObject>())
                list.Add(new EditableLineFeature
                {
                    Code = (string)f["code"] ?? string.Empty,
                    Name = (string)f["name"] ?? string.Empty,
                    Layer = (string)f["layer"] ?? string.Empty,
                    Label = (string)f["label"] ?? string.Empty,
                    LabelLayer = (string)f["labelLayer"] ?? string.Empty,
                    Placement = (string)f["placement"] ?? string.Empty
                });
            return list;
        }

        /// <summary>Writes one line feature's label text, layer and side back; every other
        /// property of the entry (aliases, alwaysLabel, comments) is kept.</summary>
        public void ApplyLineFeature(EditableLineFeature edit)
        {
            if (edit == null) throw new ArgumentNullException("edit");
            var f = (_root["lineFeatures"] as JArray ?? new JArray()).OfType<JObject>()
                .FirstOrDefault(x => string.Equals((string)x["code"], edit.Code, StringComparison.OrdinalIgnoreCase));
            if (f == null) throw new ConfigException("No line feature with code '" + edit.Code + "'.");
            SetOrRemove(f, "label", edit.Label);
            SetOrRemove(f, "labelLayer", edit.LabelLayer);
            var side = (edit.Placement ?? string.Empty).Trim();
            if (side.Length == 0 || side.Equals("Auto", StringComparison.OrdinalIgnoreCase)) f.Remove("placement");
            else f["placement"] = side;
        }

        // ------------------------------------------------------------------ symbols

        /// <summary>Written above a symbol list the editor copies in from the built-in standard.</summary>
        public const string SymbolsComment =
            "Symbol standard, edited in Settings > Symbols and Rotations (copied from the built-in PMX list on the first edit). " +
            "Each rule: codes (wildcards * ?), kind, block (for people and the preview), referenceCadDegrees = the block's native zero " +
            "(the AutoCAD direction its meaningful direction points at rotation 0), period (360 one direction, 180 an axis, 0 never turned). " +
            "First match wins: a code's own rule goes above its family's. Remove this list to go back to the built-in PMX standard.";

        /// <summary>True when the file names no symbol list of its own, so the built-in PMX list
        /// (<see cref="FieldCodes.Orient.OrientationStandard.Pmx"/>) is in force.</summary>
        public bool SymbolsAreBuiltIn()
        {
            var list = SymbolArray();
            return list == null || !list.OfType<JObject>().Any();
        }

        /// <summary>
        /// The symbol rules in force, in order (first match wins). When the file has no list of its
        /// own these are the built-in PMX rules, every one with SourceIndex -1: call
        /// <see cref="MaterializeSymbols"/> before editing so the list is in the file.
        /// </summary>
        public IList<EditableSymbol> GetSymbols()
        {
            if (SymbolsAreBuiltIn())
                return FieldCodes.Orient.OrientationStandard.Pmx().Symbols.Select(r => EditableSymbol.From(r, -1)).ToList();

            var result = new List<EditableSymbol>();
            var list = SymbolArray();
            for (var i = 0; i < list.Count; i++)
            {
                var entry = list[i] as JObject;
                if (entry == null) continue;
                result.Add(new EditableSymbol
                {
                    SourceIndex = i,
                    Codes = ReadCodes(entry["codes"]),
                    Kind = entry["kind"] != null && entry["kind"].Type != JTokenType.Null ? entry["kind"].ToString() : FieldCodes.Orient.OrientKind.Other.ToString(),
                    Block = (string)entry["block"],
                    ReferenceCadDegrees = Number(entry["referenceCadDegrees"], 0),
                    Period = Number(entry["period"], 360),
                    TwoSided = Flag(entry["twoSided"]),
                    RotateLabel = Flag(entry["rotateLabel"]),
                    Meaning = (string)entry["meaning"]
                });
            }
            return result;
        }

        /// <summary>
        /// Copies the built-in PMX symbol list into the file ("orientation" > "symbols", the
        /// section created if the file has none) so it can be edited: from then on the file's list,
        /// not the built-in one, is in force. Nothing changes in behaviour -- the rules are the same
        /// ones -- until they are edited. False when the file already has its own list.
        /// </summary>
        public bool MaterializeSymbols()
        {
            if (!SymbolsAreBuiltIn()) return false;
            var orientation = Orientation(true);
            var list = new JArray();
            foreach (var rule in FieldCodes.Orient.OrientationStandard.Pmx().Symbols)
                list.Add(WriteSymbol(new JObject(), EditableSymbol.From(rule, -1)));
            if (orientation["//symbols"] == null) orientation["//symbols"] = SymbolsComment;
            orientation["symbols"] = list;
            return true;
        }

        /// <summary>
        /// Replaces the file's symbol list with <paramref name="symbols"/>, in that order. A row read
        /// from the file (SourceIndex) keeps that entry's comments and unknown properties; only the
        /// properties the editor owns are written. Each row's SourceIndex is updated to its new place.
        /// An empty list is refused: use <see cref="UseBuiltInSymbols"/> to go back to PMX's.
        /// </summary>
        public void SetSymbols(IList<EditableSymbol> symbols)
        {
            if (symbols == null) throw new ArgumentNullException("symbols");
            if (symbols.Count == 0)
                throw new ConfigException("A symbol list needs at least one rule. To go back to the built-in PMX standard, use the built-in list instead.");

            var orientation = Orientation(true);
            var old = orientation["symbols"] as JArray ?? new JArray();
            var list = new JArray();
            foreach (var s in symbols)
            {
                var source = s.SourceIndex >= 0 && s.SourceIndex < old.Count ? old[s.SourceIndex] as JObject : null;
                list.Add(WriteSymbol(source != null ? (JObject)source.DeepClone() : new JObject(), s));
            }
            if (orientation["//symbols"] == null && orientation["symbols"] == null) orientation["//symbols"] = SymbolsComment;
            orientation["symbols"] = list;
            for (var i = 0; i < symbols.Count; i++) symbols[i].SourceIndex = i;
        }

        /// <summary>Removes the file's symbol list (and the editor's note above it): the built-in
        /// PMX standard is in force again. Every other part of "orientation" stays.</summary>
        public void UseBuiltInSymbols()
        {
            var orientation = Orientation(false);
            if (orientation == null) return;
            orientation.Remove("symbols");
            if (string.Equals((string)orientation["//symbols"], SymbolsComment, StringComparison.Ordinal))
                orientation.Remove("//symbols");
        }

        /// <summary>
        /// A rule of its own for one code, put directly above the rule that covers it now (first
        /// match wins), starting as a copy of that rule. A literal entry for the code in the family
        /// rule is taken out, since it could never be reached under the override. Returns the new
        /// row's index. Refused when no rule covers the code, or the rule covering it is already
        /// that code's alone.
        /// </summary>
        public static int OverrideSymbolFor(IList<EditableSymbol> symbols, string code)
        {
            if (symbols == null) throw new ArgumentNullException("symbols");
            var bare = FieldCodes.Orient.OrientationStandard.BaseCode(code);
            if (bare.Length == 0) throw new ConfigException("Type the point code to give its own symbol rule.");

            var index = -1;
            for (var i = 0; i < symbols.Count && index < 0; i++)
                if ((symbols[i].Codes ?? new List<string>()).Any(c => FieldCodes.Orient.OrientationStandard.CodeMatches(c, bare)))
                    index = i;
            if (index < 0)
                throw new ConfigException("No symbol rule covers " + bare + " yet: add a new rule for it instead.");

            var family = symbols[index];
            if (family.Codes.Count == 1 && string.Equals(family.Codes[0].Trim(), bare, StringComparison.OrdinalIgnoreCase))
                throw new ConfigException("Rule " + (index + 1) + " is already " + bare + "'s own rule: edit it directly.");

            var own = family.Clone();
            own.SourceIndex = -1;
            own.Codes = new List<string> { bare };
            family.Codes.RemoveAll(c => string.Equals(c.Trim(), bare, StringComparison.OrdinalIgnoreCase));
            symbols.Insert(index, own);
            return index;
        }

        public EditableOrientationApply GetOrientationApply()
        {
            var defaults = new FieldCodes.Orient.OrientationStandard();
            var o = Orientation(false);
            var tier = o != null && o["applyWeakestTier"] != null && o["applyWeakestTier"].Type != JTokenType.Null
                ? o["applyWeakestTier"].ToString() : defaults.ApplyWeakestTier.ToString();
            return new EditableOrientationApply
            {
                MinimumConfidence = o != null ? Number(o["applyMinimumConfidence"], defaults.ApplyMinimumConfidence) : defaults.ApplyMinimumConfidence,
                WeakestTier = tier,
                SymbolLibrary = o != null ? (string)o["symbolLibrary"] ?? string.Empty : string.Empty,
                MarkingDirectionWords = Flag(o, "markingDirectionWords", defaults.MarkingDirectionWords),
                MarkingFollowRoad = Flag(o, "markingFollowRoad", defaults.MarkingFollowRoad),
                MarkingRoadFeet = o != null ? Number(o["markingRoadFeet"], defaults.MarkingRoadFeet) : defaults.MarkingRoadFeet,
                MarkingMaxTurnDegrees = o != null ? Number(o["markingMaxTurnDegrees"], defaults.MarkingMaxTurnDegrees) : defaults.MarkingMaxTurnDegrees,
                RoadSearchFeet = o != null ? Number(o["roadSearchFeet"], defaults.RoadSearchFeet) : defaults.RoadSearchFeet,
                MaxExtensionFeet = o != null ? Number(o["maxExtensionFeet"], defaults.MaxExtensionFeet) : defaults.MaxExtensionFeet,
                LocalTangentFeet = o != null ? Number(o["localTangentFeet"], defaults.LocalTangentFeet) : defaults.LocalTangentFeet,
                AlignmentToleranceDegrees = o != null ? Number(o["alignmentToleranceDegrees"], defaults.AlignmentToleranceDegrees) : defaults.AlignmentToleranceDegrees,
                IntersectionHandling = Word(o, "intersectionHandling", defaults.IntersectionHandling.ToString()),
                HandTurned = Word(o, "handTurned", defaults.HandTurned.ToString()),
                QcGraphics = Word(o, "qcGraphics", defaults.QcGraphics.ToString()),
                QcShowInferredConnections = Flag(o, "qcShowInferredConnections", defaults.QcShowInferredConnections),
                Fallbacks = ReadFallbacks(o, defaults),
                Reliability = ReadReliability(o, defaults)
            };
        }

        private static string Word(JObject o, string name, string absentMeans)
        {
            var t = o != null ? o[name] : null;
            return t != null && t.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)t) ? ((string)t).Trim() : absentMeans;
        }

        private static Dictionary<string, string> ReadFallbacks(JObject o, FieldCodes.Orient.OrientationStandard defaults)
        {
            var written = o != null ? o["fallbacks"] as JObject : null;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kind in EditableOrientationApply.FallbackKinds)
            {
                string value = null;
                if (written != null)
                {
                    var t = written.Properties().FirstOrDefault(p => string.Equals(p.Name, kind, StringComparison.OrdinalIgnoreCase));
                    // A fallbacks list the office wrote replaces the built-in one: a kind it leaves out applies.
                    value = t != null && t.Value.Type == JTokenType.String ? ((string)t.Value).Trim() : FieldCodes.Orient.OrientFallback.Apply.ToString();
                }
                else
                {
                    FieldCodes.Orient.OrientKind k;
                    value = Enum.TryParse(kind, out k) ? defaults.FallbackFor(k).ToString() : FieldCodes.Orient.OrientFallback.Apply.ToString();
                }
                map[kind] = value;
            }
            return map;
        }

        private static Dictionary<string, double> ReadReliability(JObject o, FieldCodes.Orient.OrientationStandard defaults)
        {
            var written = o != null ? o["roadReferences"] as JArray : null;
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in EditableOrientationApply.ReliabilityRoles)
            {
                FieldCodes.Orient.LineRole r;
                var fallback = Enum.TryParse(role, out r) ? defaults.ReliabilityOf(r) : 0;
                var value = fallback;
                if (written != null)
                {
                    value = 0;     // a list the office wrote replaces the built-in one
                    foreach (var entry in written.OfType<JObject>())
                    {
                        var name = entry["role"];
                        if (name != null && string.Equals(name.ToString(), role, StringComparison.OrdinalIgnoreCase))
                            value = Number(entry["reliability"], 0);
                    }
                }
                map[role] = value;
            }
            return map;
        }

        private static bool Flag(JObject o, string name, bool absentMeans)
        {
            var t = o != null ? o[name] : null;
            return t != null && t.Type == JTokenType.Boolean ? (bool)t : absentMeans;
        }

        /// <summary>Writes the thresholds and library path; a value that did not change is not rewritten.</summary>
        public void ApplyOrientationApply(EditableOrientationApply edit)
        {
            if (edit == null) throw new ArgumentNullException("edit");
            var current = GetOrientationApply();
            var o = Orientation(false);
            if (Math.Abs(current.MinimumConfidence - edit.MinimumConfidence) > 1e-12)
                Orientation(true)["applyMinimumConfidence"] = edit.MinimumConfidence;
            if (!string.Equals(current.WeakestTier, edit.WeakestTier, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(edit.WeakestTier))
                Orientation(true)["applyWeakestTier"] = edit.WeakestTier.Trim();
            if (!string.Equals(current.SymbolLibrary ?? string.Empty, (edit.SymbolLibrary ?? string.Empty).Trim(), StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(edit.SymbolLibrary)) { if (o != null) o.Remove("symbolLibrary"); }
                else Orientation(true)["symbolLibrary"] = edit.SymbolLibrary.Trim();
            }
            if (current.MarkingDirectionWords != edit.MarkingDirectionWords) Orientation(true)["markingDirectionWords"] = edit.MarkingDirectionWords;
            if (current.MarkingFollowRoad != edit.MarkingFollowRoad) Orientation(true)["markingFollowRoad"] = edit.MarkingFollowRoad;
            if (Math.Abs(current.MarkingRoadFeet - edit.MarkingRoadFeet) > 1e-12) Orientation(true)["markingRoadFeet"] = edit.MarkingRoadFeet;
            if (Math.Abs(current.MarkingMaxTurnDegrees - edit.MarkingMaxTurnDegrees) > 1e-12) Orientation(true)["markingMaxTurnDegrees"] = edit.MarkingMaxTurnDegrees;
            if (Math.Abs(current.RoadSearchFeet - edit.RoadSearchFeet) > 1e-12) Orientation(true)["roadSearchFeet"] = edit.RoadSearchFeet;
            if (Math.Abs(current.MaxExtensionFeet - edit.MaxExtensionFeet) > 1e-12) Orientation(true)["maxExtensionFeet"] = edit.MaxExtensionFeet;
            if (Math.Abs(current.LocalTangentFeet - edit.LocalTangentFeet) > 1e-12) Orientation(true)["localTangentFeet"] = edit.LocalTangentFeet;
            if (Math.Abs(current.AlignmentToleranceDegrees - edit.AlignmentToleranceDegrees) > 1e-12) Orientation(true)["alignmentToleranceDegrees"] = edit.AlignmentToleranceDegrees;
            foreach (var w in new[] { Tuple.Create("intersectionHandling", current.IntersectionHandling, edit.IntersectionHandling),
                                      Tuple.Create("handTurned", current.HandTurned, edit.HandTurned),
                                      Tuple.Create("qcGraphics", current.QcGraphics, edit.QcGraphics) })
                if (!string.IsNullOrWhiteSpace(w.Item3) && !string.Equals(w.Item2, w.Item3, StringComparison.OrdinalIgnoreCase))
                    Orientation(true)[w.Item1] = w.Item3.Trim();
            if (current.QcShowInferredConnections != edit.QcShowInferredConnections) Orientation(true)["qcShowInferredConnections"] = edit.QcShowInferredConnections;

            // The fallbacks and the trust in road lines are written whole when any changes: a
            // list in the rules file replaces the built-in one, so a partial list would change the rest.
            if (edit.Fallbacks != null && EditableOrientationApply.FallbackKinds.Any(k =>
                    !string.Equals(Lookup(current.Fallbacks, k), Lookup(edit.Fallbacks, k), StringComparison.OrdinalIgnoreCase)))
            {
                var obj = new JObject();
                foreach (var k in EditableOrientationApply.FallbackKinds) obj[k] = Lookup(edit.Fallbacks, k) ?? FieldCodes.Orient.OrientFallback.Apply.ToString();
                Orientation(true)["fallbacks"] = obj;
            }
            if (edit.Reliability != null && EditableOrientationApply.ReliabilityRoles.Any(k =>
                    Math.Abs(LookupNumber(current.Reliability, k) - LookupNumber(edit.Reliability, k)) > 1e-12))
            {
                var list = new JArray();
                foreach (var role in EditableOrientationApply.ReliabilityRoles)
                {
                    var value = LookupNumber(edit.Reliability, role);
                    list.Add(new JObject { ["role"] = role, ["reliability"] = value });
                    if (role == "Centerline") list.Add(new JObject { ["role"] = "OneWay", ["reliability"] = value });
                }
                Orientation(true)["roadReferences"] = list;
            }
        }

        private static string Lookup(IDictionary<string, string> map, string key)
        {
            string v;
            return map != null && map.TryGetValue(key, out v) ? v : null;
        }

        private static double LookupNumber(IDictionary<string, double> map, string key)
        {
            double v;
            return map != null && map.TryGetValue(key, out v) ? v : 0;
        }

        private JArray SymbolArray()
        {
            var o = Orientation(false);
            return o != null ? o["symbols"] as JArray : null;
        }

        private JObject Orientation(bool create)
        {
            var o = _root["orientation"] as JObject;
            if (o == null && create)
            {
                o = new JObject();
                _root["orientation"] = o;
            }
            return o;
        }

        /// <summary>Only the properties the editor owns; false flags are left out, as the shipped file does.</summary>
        private static JObject WriteSymbol(JObject entry, EditableSymbol s)
        {
            entry["codes"] = new JArray((s.Codes ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Cast<object>().ToArray());
            entry["kind"] = string.IsNullOrWhiteSpace(s.Kind) ? FieldCodes.Orient.OrientKind.Other.ToString() : s.Kind.Trim();
            SetOrRemove(entry, "block", s.Block);
            entry["referenceCadDegrees"] = s.ReferenceCadDegrees;
            entry["period"] = s.Period;
            if (s.TwoSided) entry["twoSided"] = true; else entry.Remove("twoSided");
            if (s.RotateLabel) entry["rotateLabel"] = true; else entry.Remove("rotateLabel");
            SetOrRemove(entry, "meaning", s.Meaning);
            return entry;
        }

        private static List<string> ReadCodes(JToken token)
        {
            var array = token as JArray;
            if (array != null)
                return array.Where(t => t.Type == JTokenType.String).Select(t => ((string)t).Trim()).Where(c => c.Length > 0).ToList();
            if (token != null && token.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)token))
                return new List<string> { ((string)token).Trim() };
            return new List<string>();
        }

        private static bool TryNumber(JToken token, out double value)
        {
            value = 0;
            if (token == null) return false;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<double>();
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            return token.Type == JTokenType.String &&
                   double.TryParse((string)token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static double Number(JToken token, double fallback)
        {
            double v;
            return TryNumber(token, out v) ? v : fallback;
        }

        private static bool Flag(JToken token)
        {
            return token != null && token.Type == JTokenType.Boolean && token.Value<bool>();
        }

        // ------------------------------------------------------------- inserted blocks

        /// <summary>The code rules that name a block or opt in to inserting one, in file order:
        /// the ones Settings > Symbols and Rotations lists under "Blocks FTF inserts".</summary>
        public IList<string> BlockRuleIds()
        {
            return Rules().OfType<JObject>()
                .Where(r => !string.IsNullOrWhiteSpace((string)r["block"]) || Flag(r["insertBlock"]))
                .Select(r => (string)r["id"]).Where(id => id != null).ToList();
        }

        public EditableBlockInsert GetBlockInsert(string id)
        {
            var rule = FindRule(id);
            if (rule == null) return null;

            FieldCodes.Symbols.BlockPlacement placement;
            if (!Enum.TryParse((string)rule["blockInsertion"] ?? string.Empty, true, out placement)) placement = FieldCodes.Symbols.BlockPlacement.AtPoint;
            FieldCodes.Symbols.BlockRotationMethod rotation;
            if (!Enum.TryParse((string)rule["blockRotation"] ?? string.Empty, true, out rotation)) rotation = FieldCodes.Symbols.BlockRotationMethod.Field;

            var edit = new EditableBlockInsert
            {
                Id = (string)rule["id"],
                Description = (string)rule["description"] ?? string.Empty,
                InsertBlock = Flag(rule["insertBlock"]),
                Block = (string)rule["block"] ?? string.Empty,
                BlockLayer = (string)rule["blockLayer"] ?? string.Empty,
                BlockScale = Number(rule["blockScale"], 1.0),
                BlockScaleFrom = (string)rule["blockScaleFrom"] ?? string.Empty,
                BlockScaleDivisor = Number(rule["blockScaleDivisor"], 1.0),
                Placement = placement,
                OffsetX = Number(rule["blockOffsetX"], 0),
                OffsetY = Number(rule["blockOffsetY"], 0),
                Rotation = rotation,
                FieldRotation = rule["rotation"] is JObject ? (string)rule["rotation"]["from"] ?? string.Empty : string.Empty
            };
            var attributes = rule["blockAttributes"] as JObject;
            if (attributes != null)
                foreach (var p in attributes.Properties().Where(p => !p.Name.StartsWith("//", StringComparison.Ordinal)))
                    edit.Attributes.Add(new KeyValuePair<string, string>(p.Name, p.Value.Type == JTokenType.Null ? string.Empty : p.Value.ToString()));
            return edit;
        }

        /// <summary>
        /// Writes how the rule's block is placed: name, layer, scale, insertion, rotation and
        /// attribute templates. Defaults are written as absence, so an untouched rule stays as it
        /// was. NEVER changes insertBlock: that is <see cref="SetInsertBlock"/>'s alone.
        /// </summary>
        public void ApplyBlockInsert(EditableBlockInsert edit)
        {
            if (edit == null) throw new ArgumentNullException("edit");
            var rule = FindRule(edit.Id);
            if (rule == null) throw new ConfigException("No rule with id '" + edit.Id + "'.");

            SetOrRemove(rule, "block", edit.Block == null ? null : edit.Block.Trim());
            SetOrRemove(rule, "blockLayer", edit.BlockLayer == null ? null : edit.BlockLayer.Trim());

            if (string.IsNullOrWhiteSpace(edit.BlockScaleFrom))
            {
                rule.Remove("blockScaleFrom");
                rule.Remove("blockScaleDivisor");
                WriteNumber(rule, "blockScale", edit.BlockScale, 1.0);
            }
            else
            {
                rule["blockScaleFrom"] = edit.BlockScaleFrom.Trim();
                WriteNumber(rule, "blockScaleDivisor", edit.BlockScaleDivisor, 1.0);
                rule.Remove("blockScale");
            }

            if (edit.Placement == FieldCodes.Symbols.BlockPlacement.AtPoint)
            {
                rule.Remove("blockInsertion");
                rule.Remove("blockOffsetX");
                rule.Remove("blockOffsetY");
            }
            else
            {
                rule["blockInsertion"] = edit.Placement.ToString();
                WriteNumber(rule, "blockOffsetX", edit.OffsetX, 0.0);
                WriteNumber(rule, "blockOffsetY", edit.OffsetY, 0.0);
            }

            if (edit.Rotation == FieldCodes.Symbols.BlockRotationMethod.Field) rule.Remove("blockRotation");
            else rule["blockRotation"] = edit.Rotation.ToString();

            // Attribute templates: the editor's pairs, after any comment keys already there.
            var existing = rule["blockAttributes"] as JObject;
            var attributes = new JObject();
            if (existing != null)
                foreach (var p in existing.Properties().Where(p => p.Name.StartsWith("//", StringComparison.Ordinal)))
                    attributes.Add(p.Name, p.Value.DeepClone());
            var any = false;
            foreach (var kv in edit.Attributes ?? new List<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrEmpty(kv.Value) || attributes[kv.Key.Trim()] != null) continue;
                attributes.Add(kv.Key.Trim(), kv.Value);
                any = true;
            }
            if (any || attributes.Properties().Any()) rule["blockAttributes"] = attributes;
            else rule.Remove("blockAttributes");
        }

        /// <summary>
        /// The opt-in itself. FTF then inserts the rule's block on every point the rule matches, on
        /// top of whatever Civil 3D's point style draws -- so the editor asks before it calls this
        /// with true. Off removes the property (absent is off).
        /// </summary>
        public void SetInsertBlock(string id, bool insert)
        {
            var rule = FindRule(id);
            if (rule == null) throw new ConfigException("No rule with id '" + id + "'.");
            if (insert) rule["insertBlock"] = true;
            else rule.Remove("insertBlock");
        }

        /// <summary>Takes the block off a rule: insertBlock and every block property the editor
        /// owns. Comments (such as the tree rule's "//block") stay.</summary>
        public void RemoveBlockInsert(string id)
        {
            var rule = FindRule(id);
            if (rule == null) throw new ConfigException("No rule with id '" + id + "'.");
            foreach (var name in new[] { "insertBlock", "block", "blockLayer", "blockScale", "blockScaleFrom", "blockScaleDivisor",
                                         "blockInsertion", "blockOffsetX", "blockOffsetY", "blockRotation", "blockAttributes" })
                rule.Remove(name);
        }

        // ------------------------------------------------------------------ merging

        // An editor page stages its edits on its own copy of the document; another page may save
        // the same file meanwhile. Saving therefore re-reads the file and copies across only the
        // parts this page owns, so neither page's save loses the other's work.

        /// <summary>Puts this document's symbol list onto <paramref name="target"/> -- or its
        /// absence, so the built-in list is in force there too. Entries are copied whole.</summary>
        public void CopySymbolsTo(RuleDocument target)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (SymbolsAreBuiltIn()) { target.UseBuiltInSymbols(); return; }
            var mine = Orientation(false);
            var theirs = target.Orientation(true);
            if (mine["//symbols"] != null && theirs["//symbols"] == null && theirs["symbols"] == null)
                theirs["//symbols"] = mine["//symbols"].DeepClone();
            theirs["symbols"] = SymbolArray().DeepClone();
        }

        /// <summary>Puts this document's FTFORIENT thresholds and symbol library onto <paramref name="target"/>.</summary>
        public void CopyOrientationApplyTo(RuleDocument target)
        {
            if (target == null) throw new ArgumentNullException("target");
            target.ApplyOrientationApply(GetOrientationApply());
        }

        /// <summary>Puts one rule's block settings, opt-in included, onto the same rule of
        /// <paramref name="target"/> (the opt-in was confirmed when it was made here).</summary>
        public void CopyBlockInsertTo(RuleDocument target, string id)
        {
            if (target == null) throw new ArgumentNullException("target");
            var mine = GetBlockInsert(id);
            if (mine == null || target.FindRule(id) == null) return;
            // Defaults are written as absence, so a block taken off here comes off there too.
            target.ApplyBlockInsert(mine);
            target.SetInsertBlock(id, mine.InsertBlock);
        }

        private static void WriteNumber(JObject owner, string name, double value, double absentMeans)
        {
            if (Math.Abs(value - absentMeans) < 1e-12) owner.Remove(name);
            else owner[name] = value;
        }

        private JObject FindRule(string id)
        {
            return Rules().OfType<JObject>().FirstOrDefault(
                r => string.Equals((string)r["id"], id, StringComparison.OrdinalIgnoreCase));
        }

        private static bool NotNumeric(string name)
        {
            int ignored;
            return !int.TryParse(name, out ignored);
        }
    }
}
