using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Editing
{
    /// <summary>One range check of a code rule (validate): a number read from the description that
    /// should lie between a lowest and a highest value. FieldCodeParser.RunChecks reads it; a value
    /// outside is reported with the check's severity, a blank or non-numeric value is not checked.</summary>
    public sealed class EditableRangeCheck
    {
        public EditableRangeCheck()
        {
            SourceIndex = -1;
            Severity = "Warning";
        }

        /// <summary>Where the entry sat in the file's list when it was read (-1: a new row). Writing
        /// the list back starts from that entry, so anything this build does not know survives.</summary>
        public int SourceIndex { get; set; }

        /// <summary>The field checked: a named group of the pattern or a derived field (trunk.count).</summary>
        public string Field { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }

        /// <summary>Info, Warning or Error (Error: the point is not drawn).</summary>
        public string Severity { get; set; }

        /// <summary>What the report says; blank: "Value x for 'field' is outside the expected range."</summary>
        public string Message { get; set; }
    }

    /// <summary>One term of a notes vocabulary: the words a crew writes, the label's wording and the
    /// group it lands in.</summary>
    public sealed class EditableNoteTerm
    {
        public EditableNoteTerm() { SourceIndex = -1; }

        /// <summary>As <see cref="EditableRangeCheck.SourceIndex"/>.</summary>
        public int SourceIndex { get; set; }

        /// <summary>A regex over whole words, case-insensitive ("XFMR|TRANS", "SPEED\s+(?&lt;mph&gt;[0-9]+)").</summary>
        public string Match { get; set; }

        /// <summary>The label wording; blank: the words as written.</summary>
        public string Text { get; set; }

        public string Group { get; set; }
    }

    /// <summary>How one note group reads (notes.groups): its order and joiner.</summary>
    public sealed class EditableNoteGroup
    {
        public string Name { get; set; }

        /// <summary>Vocabulary (the terms' order), AsShot (as written), or blank for the default:
        /// AsShot for the unknown group, Vocabulary for the rest.</summary>
        public string Order { get; set; }

        /// <summary>Put between the group's values; null for the default (", " in vocabulary order, " " as shot).</summary>
        public string Joiner { get; set; }
    }

    /// <summary>What a match pattern makes of a description, for the editor and its checks.</summary>
    public sealed class PatternInfo
    {
        public PatternInfo()
        {
            Fields = new List<string>();
            Codes = new List<string>();
        }

        /// <summary>Why the pattern cannot be used, in plain words; null when it can.</summary>
        public string Problem { get; set; }

        /// <summary>The pattern's named groups: the fields a label, rotation or dripline can read.</summary>
        public IList<string> Fields { get; set; }

        /// <summary>The field codes the pattern spells out (best effort; the species codes for {SPECIES}).</summary>
        public IList<string> Codes { get; set; }

        /// <summary>The pattern names {SPECIES} or a (?&lt;species&gt;...) group: the species list is used.</summary>
        public bool UsesSpecies { get; set; }

        /// <summary>The pattern reads trunk sizes, so a cluster (two or more) can happen.</summary>
        public bool ReadsTrunk { get { return Fields.Contains("trunk", StringComparer.OrdinalIgnoreCase); } }

        /// <summary>The pattern keeps the words after the code (a (?&lt;mods&gt;...) group) for modifiers and notes.</summary>
        public bool ReadsWords { get { return Fields.Contains("mods", StringComparer.OrdinalIgnoreCase); } }
    }

    /// <summary>
    /// The code rules ("codes") as Settings > Point Features edits them, beyond the label: the match
    /// pattern, species, cluster label, range checks, notes vocabulary, rotation and dripline, and
    /// deleting a feature. Every edit is written only where it differs from the file, and every list
    /// entry read from the file keeps its comments and unknown properties.
    /// </summary>
    public sealed partial class RuleDocument
    {
        /// <summary>Starts every problem the code-rule checks report.</summary>
        public const string PointFeaturesPrefix = "Point Features: ";

        /// <summary>The fields the parser works out from (?&lt;trunk&gt;...) for a label or a check.</summary>
        public static readonly string[] DerivedTrunkFields = { "trunk.count", "trunk.exact", "trunk.stems" };

        private static readonly string[] Severities = { "Info", "Warning", "Error" };
        private static readonly string[] RotationModes = { "Azimuth", "Cartesian" };
        private static readonly string[] UnknownModes = { "Keep", "Flag", "Refuse" };
        private static readonly string[] GroupOrders = { "Vocabulary", "AsShot" };
        private static readonly string[] Leaders = { "Auto", "Always", "Never" };
        private static readonly Regex GroupName = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

        /// <summary>
        /// The match pattern as the parser uses it (RulesConfig.Compile, FieldCodeParser.Parse): a
        /// regular expression tried against the whole description, spaces collapsed, ignoring case.
        /// Enabled rules are tried in file order and the first that matches wins. {SPECIES} stands for
        /// the species codes. Named groups become fields: code, species, trunk, drip, rotation, mods
        /// (the words after: modifiers and notes), and any other name a label can use.
        /// </summary>
        public static PatternInfo ReadPattern(string match, IEnumerable<string> speciesCodes)
        {
            var info = new PatternInfo();
            var species = (speciesCodes ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (string.IsNullOrWhiteSpace(match))
            {
                info.Problem = "there is no match pattern, so it can never match a description.";
                return info;
            }

            var pattern = match;
            var expands = match.IndexOf("{SPECIES}", StringComparison.OrdinalIgnoreCase) >= 0;
            info.UsesSpecies = expands || match.IndexOf("(?<species>", StringComparison.Ordinal) >= 0;
            if (expands)
            {
                if (species.Count == 0)
                {
                    info.Problem = "the match pattern uses {SPECIES} but no species are listed, so it can never match.";
                    return info;
                }
                var alternation = "(?:" + string.Join("|", species.Select(Regex.Escape).ToArray()) + ")";
                pattern = Regex.Replace(pattern, Regex.Escape("{SPECIES}"), alternation.Replace("$", "$$"), RegexOptions.IgnoreCase);
            }

            Regex rx;
            try { rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
            catch (ArgumentException ex)
            {
                info.Problem = "the match pattern is an invalid regex (regular expression): " + ex.Message;
                return info;
            }

            info.Fields = rx.GetGroupNames().Where(NotNumeric).ToList();
            var probe = new CodeRule { Match = match };
            foreach (var s in species) probe.Species[s.Trim()] = s.Trim();
            info.Codes = LiteralCodes(probe).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Matching nothing at all and a made-up code alike: it matches any description.
            if (rx.IsMatch(string.Empty) && rx.IsMatch("QZXV 9 QZXV"))
                info.Problem = "the match pattern matches any description at all, so it would claim every point before the features below it.";
            return info;
        }

        // ------------------------------------------------------------------ reading

        private static void ReadCodeRuleParts(JObject rule, EditableRule e)
        {
            var label = rule["label"] as JObject;
            e.ClusterFormat = label != null ? Text(label["clusterFormat"]) : string.Empty;

            var species = rule["species"] as JObject;
            if (species != null)
                foreach (var p in species.Properties())
                    e.Species.Add(new KeyValuePair<string, string>(p.Name, Text(p.Value)));

            var rotation = rule["rotation"] as JObject;
            e.HasRotation = rotation != null;
            if (rotation != null)
            {
                e.RotationFrom = Text(rotation["from"]);
                e.RotationMode = Text(rotation["mode"]);
                if (e.RotationMode.Length == 0) e.RotationMode = "Azimuth";
            }
            else e.RotationFrom = string.Empty;

            var drip = rule["dripLine"] as JObject;
            e.DripRadiusFrom = drip != null ? Text(drip["radiusFrom"]) : string.Empty;
            e.DripUnify = drip == null || drip["unify"] == null || drip["unify"].Type != JTokenType.Boolean || (bool)drip["unify"];

            var checks = rule["validate"] as JArray;
            if (checks != null)
                for (var i = 0; i < checks.Count; i++)
                {
                    var c = checks[i] as JObject;
                    if (c == null) continue;
                    var severity = Text(c["severity"]);
                    e.Checks.Add(new EditableRangeCheck
                    {
                        SourceIndex = i,
                        Field = Text(c["field"]),
                        Min = OptionalNumber(c["min"]),
                        Max = OptionalNumber(c["max"]),
                        Severity = severity.Length > 0 ? severity : "Warning",
                        Message = Text(c["message"])
                    });
                }

            var notes = rule["notes"] as JObject;
            e.HasNotes = notes != null;
            if (notes == null) return;
            var unknown = Text(notes["unknown"]);
            e.NotesUnknown = unknown.Length > 0 ? unknown : "Flag";
            // Absent means NotesRule's own default; written blank it is blank (and the checks say so).
            e.NotesUnknownGroup = notes["unknownGroup"] == null ? "other" : Text(notes["unknownGroup"]);

            var terms = notes["terms"] as JArray;
            if (terms != null)
                for (var i = 0; i < terms.Count; i++)
                {
                    var t = terms[i] as JObject;
                    if (t == null) continue;
                    e.NoteTerms.Add(new EditableNoteTerm { SourceIndex = i, Match = Text(t["match"]), Text = Text(t["text"]), Group = Text(t["group"]) });
                }

            var groups = notes["groups"] as JObject;
            if (groups != null)
                foreach (var p in groups.Properties().Where(p => !p.Name.StartsWith("//", StringComparison.Ordinal)))
                {
                    var g = p.Value as JObject;
                    if (g == null) continue;
                    var joiner = g["joiner"];
                    e.NoteGroups.Add(new EditableNoteGroup
                    {
                        Name = p.Name,
                        Order = Text(g["order"]),
                        Joiner = joiner == null || joiner.Type == JTokenType.Null ? null : Text(joiner)
                    });
                }

            var order = notes["order"] as JArray;
            if (order != null) e.NoteOrder.AddRange(order.Select(Text));
        }

        private static string Text(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return string.Empty;
            return token.Type == JTokenType.String ? (string)token : token.ToString();
        }

        private static double? OptionalNumber(JToken token)
        {
            double v;
            return TryNumber(token, out v) ? v : (double?)null;
        }

        // ------------------------------------------------------------------ writing

        /// <summary>What the document cannot hold at all (two species or note groups of one name), refused
        /// before anything is written.</summary>
        private static void RefuseUnwritable(EditableRule edit)
        {
            var where = PointFeaturesPrefix + (string.IsNullOrWhiteSpace(edit.Description) ? "rule '" + edit.Id + "'" : edit.Description.Trim() + " (rule '" + edit.Id + "')") + ": ";
            var twice = CleanSpecies(edit.Species).GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (twice.Count > 0)
                throw new ConfigException(where + "species " + string.Join(", ", twice.ToArray()) + " listed twice; each code once.");
            var groups = CleanGroups(edit.NoteGroups).GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (groups.Count > 0)
                throw new ConfigException(where + "note group " + string.Join(", ", groups.ToArray()) + " has two settings rows; keep one.");
        }

        private static void ApplyCodeRuleParts(JObject rule, EditableRule current, EditableRule edit)
        {
            ApplySpecies(rule, current, edit);
            ApplyRotation(rule, current, edit);
            ApplyDripline(rule, current, edit);
            ApplyChecks(rule, current, edit);
            ApplyNotes(rule, current, edit);
        }

        private static List<KeyValuePair<string, string>> CleanSpecies(IEnumerable<KeyValuePair<string, string>> species)
        {
            return (species ?? Enumerable.Empty<KeyValuePair<string, string>>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Key) || !string.IsNullOrWhiteSpace(p.Value))
                .Select(p => new KeyValuePair<string, string>((p.Key ?? string.Empty).Trim(), (p.Value ?? string.Empty).Trim()))
                .ToList();
        }

        private static void ApplySpecies(JObject rule, EditableRule current, EditableRule edit)
        {
            var want = CleanSpecies(edit.Species);
            var have = current.Species ?? new List<KeyValuePair<string, string>>();
            if (want.Count == have.Count && want.Select((p, i) => p.Key == have[i].Key && p.Value == have[i].Value).All(x => x)) return;
            if (want.Count == 0) { rule.Remove("species"); return; }
            var o = new JObject();
            foreach (var p in want) o[p.Key] = p.Value;
            rule["species"] = o;
        }

        private static void ApplyRotation(JObject rule, EditableRule current, EditableRule edit)
        {
            if (!edit.HasRotation)
            {
                if (current.HasRotation) rule.Remove("rotation");
                return;
            }
            var from = (edit.RotationFrom ?? string.Empty).Trim();
            var mode = string.IsNullOrWhiteSpace(edit.RotationMode) ? "Azimuth" : edit.RotationMode.Trim();
            var o = rule["rotation"] as JObject;
            if (o == null)
            {
                o = new JObject();
                if (from.Length > 0) o["from"] = from;
                o["mode"] = mode;
                rule["rotation"] = o;
                return;
            }
            if (Differs(current.RotationFrom, from)) SetOrRemove(o, "from", from);
            if (!string.Equals(current.RotationMode, mode, StringComparison.OrdinalIgnoreCase)) o["mode"] = mode;
        }

        private static void ApplyDripline(JObject rule, EditableRule current, EditableRule edit)
        {
            if (!edit.HasDripline)
            {
                if (current.HasDripline) rule.Remove("dripLine");
                return;
            }
            var layer = (edit.DripLayer ?? string.Empty).Trim();
            var radius = (edit.DripRadiusFrom ?? string.Empty).Trim();
            var d = rule["dripLine"] as JObject;
            if (d == null)
            {
                d = new JObject();
                SetOrRemove(d, "layer", layer);
                SetOrRemove(d, "radiusFrom", radius);
                if (!edit.DripUnify) d["unify"] = false;
                rule["dripLine"] = d;
                return;
            }
            if (Differs(current.DripLayer, layer)) SetOrRemove(d, "layer", layer);
            if (Differs(current.DripRadiusFrom, radius)) SetOrRemove(d, "radiusFrom", radius);
            if (current.DripUnify != edit.DripUnify) d["unify"] = edit.DripUnify;
        }

        private static List<EditableRangeCheck> CleanChecks(IEnumerable<EditableRangeCheck> checks)
        {
            return (checks ?? Enumerable.Empty<EditableRangeCheck>())
                .Where(c => c != null && (!string.IsNullOrWhiteSpace(c.Field) || c.Min.HasValue || c.Max.HasValue || !string.IsNullOrWhiteSpace(c.Message)))
                .ToList();
        }

        private static bool SameCheck(EditableRangeCheck a, EditableRangeCheck b)
        {
            return !Differs(a.Field, (b.Field ?? string.Empty).Trim()) && a.Min == b.Min && a.Max == b.Max &&
                   string.Equals(Severity(a), Severity(b), StringComparison.OrdinalIgnoreCase) && !Differs(a.Message, b.Message);
        }

        private static string Severity(EditableRangeCheck c)
        {
            return string.IsNullOrWhiteSpace(c.Severity) ? "Warning" : c.Severity.Trim();
        }

        private static void ApplyChecks(JObject rule, EditableRule current, EditableRule edit)
        {
            var want = CleanChecks(edit.Checks);
            var have = current.Checks ?? new List<EditableRangeCheck>();
            if (want.Count == have.Count && want.Select((c, i) => c.SourceIndex == have[i].SourceIndex && SameCheck(have[i], c)).All(x => x)) return;

            var old = rule["validate"] as JArray ?? new JArray();
            var list = new JArray();
            foreach (var c in want)
            {
                var source = c.SourceIndex >= 0 && c.SourceIndex < old.Count ? old[c.SourceIndex] as JObject : null;
                var was = source != null ? have.FirstOrDefault(h => h.SourceIndex == c.SourceIndex) : null;
                var o = source != null ? (JObject)source.DeepClone() : new JObject();
                var field = (c.Field ?? string.Empty).Trim();
                if (was == null || Differs(was.Field, field)) SetOrRemove(o, "field", field);
                if (was == null || was.Min != c.Min) { if (c.Min.HasValue) o["min"] = c.Min.Value; else o.Remove("min"); }
                if (was == null || was.Max != c.Max) { if (c.Max.HasValue) o["max"] = c.Max.Value; else o.Remove("max"); }
                if (was == null || !string.Equals(Severity(was), Severity(c), StringComparison.OrdinalIgnoreCase)) o["severity"] = Severity(c);
                if (was == null || Differs(was.Message, c.Message)) SetOrRemove(o, "message", c.Message == null ? null : c.Message.Trim());
                list.Add(o);
            }
            if (list.Count == 0) rule.Remove("validate");
            else rule["validate"] = list;
            for (var i = 0; i < want.Count; i++) want[i].SourceIndex = i;
        }

        private static List<EditableNoteTerm> CleanTerms(IEnumerable<EditableNoteTerm> terms)
        {
            return (terms ?? Enumerable.Empty<EditableNoteTerm>())
                .Where(t => t != null && (!string.IsNullOrWhiteSpace(t.Match) || !string.IsNullOrWhiteSpace(t.Text) || !string.IsNullOrWhiteSpace(t.Group)))
                .ToList();
        }

        private static List<EditableNoteGroup> CleanGroups(IEnumerable<EditableNoteGroup> groups)
        {
            return (groups ?? Enumerable.Empty<EditableNoteGroup>())
                .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Name) && (!string.IsNullOrWhiteSpace(g.Order) || g.Joiner != null))
                .Select(g => new EditableNoteGroup { Name = g.Name.Trim(), Order = string.IsNullOrWhiteSpace(g.Order) ? string.Empty : g.Order.Trim(), Joiner = g.Joiner })
                .ToList();
        }

        private static List<string> CleanOrder(IEnumerable<string> order)
        {
            return (order ?? Enumerable.Empty<string>()).Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToList();
        }

        private static void ApplyNotes(JObject rule, EditableRule current, EditableRule edit)
        {
            if (!edit.HasNotes)
            {
                if (current.HasNotes) rule.Remove("notes");
                return;
            }

            var n = rule["notes"] as JObject;
            var created = n == null;
            if (created)
            {
                n = new JObject();
                rule["notes"] = n;
            }

            var unknown = string.IsNullOrWhiteSpace(edit.NotesUnknown) ? "Flag" : edit.NotesUnknown.Trim();
            if (created || !string.Equals(current.NotesUnknown, unknown, StringComparison.OrdinalIgnoreCase)) n["unknown"] = unknown;
            var unknownGroup = (edit.NotesUnknownGroup ?? string.Empty).Trim();
            if (created ? unknownGroup.Length > 0 : !string.Equals(current.NotesUnknownGroup ?? string.Empty, unknownGroup, StringComparison.Ordinal))
                n["unknownGroup"] = unknownGroup;

            // Terms: the list is rewritten only when something in it changed; an entry read from the
            // file is the starting point of its row, so its comments survive.
            var terms = CleanTerms(edit.NoteTerms);
            var haveTerms = current.NoteTerms ?? new List<EditableNoteTerm>();
            var sameTerms = !created && terms.Count == haveTerms.Count && terms.Select((t, i) => t.SourceIndex == haveTerms[i].SourceIndex &&
                !Differs(haveTerms[i].Match, (t.Match ?? string.Empty).Trim()) && !Differs(haveTerms[i].Text, (t.Text ?? string.Empty).Trim()) &&
                !Differs(haveTerms[i].Group, (t.Group ?? string.Empty).Trim())).All(x => x);
            if (!sameTerms && !(created && terms.Count == 0))
            {
                var old = n["terms"] as JArray ?? new JArray();
                var list = new JArray();
                foreach (var t in terms)
                {
                    var source = t.SourceIndex >= 0 && t.SourceIndex < old.Count ? old[t.SourceIndex] as JObject : null;
                    var was = source != null ? haveTerms.FirstOrDefault(h => h.SourceIndex == t.SourceIndex) : null;
                    var o = source != null ? (JObject)source.DeepClone() : new JObject();
                    var match = (t.Match ?? string.Empty).Trim();
                    var text = (t.Text ?? string.Empty).Trim();
                    var group = (t.Group ?? string.Empty).Trim();
                    if (was == null || Differs(was.Match, match)) SetOrRemove(o, "match", match);
                    if (was == null || Differs(was.Text, text)) SetOrRemove(o, "text", text);
                    if (was == null || Differs(was.Group, group)) SetOrRemove(o, "group", group);
                    list.Add(o);
                }
                n["terms"] = list;
                for (var i = 0; i < terms.Count; i++) terms[i].SourceIndex = i;
            }

            // Groups: only those with an order or joiner of their own are written.
            var groups = CleanGroups(edit.NoteGroups);
            var haveGroups = CleanGroups(current.NoteGroups);
            var sameGroups = groups.Count == haveGroups.Count && groups.Select((g, i) =>
                string.Equals(g.Name, haveGroups[i].Name, StringComparison.Ordinal) &&
                string.Equals(g.Order, haveGroups[i].Order, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(g.Joiner, haveGroups[i].Joiner, StringComparison.Ordinal)).All(x => x);
            if (!sameGroups)
            {
                var old = n["groups"] as JObject;
                var o = new JObject();
                if (old != null)
                    foreach (var p in old.Properties().Where(p => p.Name.StartsWith("//", StringComparison.Ordinal)))
                        o.Add(p.Name, p.Value.DeepClone());
                foreach (var g in groups)
                {
                    var source = old != null ? old.Properties().FirstOrDefault(p => string.Equals(p.Name, g.Name, StringComparison.OrdinalIgnoreCase)) : null;
                    var entry = source != null && source.Value is JObject ? (JObject)source.Value.DeepClone() : new JObject();
                    if (g.Order.Length > 0) entry["order"] = g.Order; else entry.Remove("order");
                    if (g.Joiner != null) entry["joiner"] = g.Joiner; else entry.Remove("joiner");
                    o[g.Name] = entry;
                }
                if (groups.Count == 0 && !o.Properties().Any()) n.Remove("groups");
                else n["groups"] = o;
            }

            var order = CleanOrder(edit.NoteOrder);
            if (!order.SequenceEqual(CleanOrder(current.NoteOrder), StringComparer.Ordinal))
            {
                if (order.Count == 0) n.Remove("order");
                else n["order"] = new JArray(order.Cast<object>().ToArray());
            }
        }

        // ------------------------------------------------------------------ deleting

        /// <summary>
        /// Removes one feature (one entry of "codes") and nothing else. Refused when it is the only
        /// one: a rules file needs at least one code rule. Its codes are then unconfigured again: a
        /// point with data after the code is listed in Unknown Codes, a bare code is left to Civil 3D.
        /// </summary>
        public void DeleteRule(string id)
        {
            var rule = FindRule(id);
            if (rule == null) throw new ConfigException("There is no feature with id '" + id + "' to delete.");
            if (Rules().OfType<JObject>().Count() <= 1)
                throw new ConfigException(PointFeaturesPrefix + NameOf(rule) + " is the only point feature, and the rules need at least one, so it cannot be deleted.");
            rule.Remove();
        }

        /// <summary>
        /// What else in the rules file names the feature or its codes, in plain words, for the
        /// confirmation before <see cref="DeleteRule"/>: its inserted block, the drafting standard,
        /// the symbol rules and the bare-code labels. None of them is changed by deleting the feature.
        /// </summary>
        public IList<string> ReferencesTo(string id)
        {
            var result = new List<string>();
            var rule = FindRule(id);
            if (rule == null) return result;
            if (Flag(rule["insertBlock"]) || !string.IsNullOrWhiteSpace(Text(rule["block"])))
                result.Add("Its inserted block (" + (Text(rule["block"]).Length > 0 ? Text(rule["block"]) : "none named") +
                           ", Settings > Symbols and Rotations) is deleted with it.");

            var codes = CodesOf(rule);
            if (codes.Count == 0) return result;

            try
            {
                foreach (var r in GetFeatureRequirements().Where(r => r != null && r.Enabled && r.Codes != null))
                {
                    var hit = codes.Where(c => r.Codes.Any(p => !string.IsNullOrWhiteSpace(p) && FieldCodes.Orient.OrientationStandard.CodeMatches(p.Trim(), c))).ToList();
                    if (hit.Count > 0)
                        result.Add("The drafting standard's row " + (string.IsNullOrWhiteSpace(r.Name) ? string.Join(" ", r.Codes.ToArray()) : "\"" + r.Name.Trim() + "\"") +
                                   " covers " + string.Join(", ", hit.ToArray()) + ": it stays, and point accountability keeps checking those points against it.");
                }
            }
            catch (ConfigException) { }

            try
            {
                var symbols = GetSymbols();
                for (var i = 0; i < symbols.Count; i++)
                {
                    var hit = codes.Where(c => (symbols[i].Codes ?? new List<string>()).Any(p => FieldCodes.Orient.OrientationStandard.CodeMatches(p, c))).ToList();
                    if (hit.Count > 0)
                        result.Add("Symbol rule " + (i + 1) + " (" + symbols[i].CodesText + ", Settings > Symbols and Rotations) covers " + string.Join(", ", hit.ToArray()) +
                                   ": it stays; with no feature reading an angle, those markers are only turned by FTFORIENT.");
                }
            }
            catch (ConfigException) { }

            var labels = (_root["symbolLabels"] as JArray ?? new JArray()).OfType<JObject>()
                .Select(s => Text(s["code"]).Trim()).Where(c => codes.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            if (labels.Count > 0)
                result.Add("A bare " + string.Join(", ", labels.ToArray()) + " is still labelled with its code (symbolLabels).");
            return result;
        }

        /// <summary>The field codes a rule in the file spells out (its species for {SPECIES}).</summary>
        private static List<string> CodesOf(JObject rule)
        {
            var species = rule["species"] as JObject;
            var info = ReadPattern(Text(rule["match"]), species != null ? species.Properties().Select(p => p.Name) : null);
            return info.Codes.ToList();
        }

        // ------------------------------------------------------------------ merging

        /// <summary>The properties of a code rule Settings > Point Features owns. The inserted block's
        /// (insertBlock, block...) belong to Symbols and Rotations.</summary>
        private static readonly string[] PointFeatureProperties =
            { "enabled", "description", "category", "match", "species", "label", "tagPrefix", "validate", "notes", "rotation", "dripLine" };

        /// <summary>
        /// Puts this document's version of one feature onto <paramref name="target"/> (Save re-reads the
        /// file first, so another page's save in between is not lost): deleted here, it is deleted there;
        /// added here, it is added at the same place in the list; otherwise only the properties Point
        /// Features owns are copied, so the feature's inserted block and comments there are kept.
        /// </summary>
        public void CopyCodeRuleTo(RuleDocument target, string id)
        {
            if (target == null) throw new ArgumentNullException("target");
            var mine = FindRule(id);
            var theirs = target.FindRule(id);
            if (mine == null)
            {
                if (theirs != null) theirs.Remove();
                return;
            }
            if (theirs == null)
            {
                var codes = target._root["codes"] as JArray;
                if (codes == null) { codes = new JArray(); target._root["codes"] = codes; }
                var index = Rules().IndexOf(mine);
                codes.Insert(Math.Max(0, Math.Min(index, codes.Count)), mine.DeepClone());
                return;
            }
            foreach (var name in PointFeatureProperties)
            {
                var value = mine[name];
                if (value == null) theirs.Remove(name);
                else if (!JToken.DeepEquals(value, theirs[name])) theirs[name] = value.DeepClone();
            }
        }

        // --------------------------------------------------------------- validation

        private static string NameOf(JObject rule)
        {
            var id = Text(rule["id"]);
            var name = Text(rule["description"]).Trim();
            return name.Length > 0 ? name + " (rule '" + id + "')" : "rule '" + id + "'";
        }

        /// <summary>
        /// The code rules as written, each mistake named by its feature: what would stop the rules
        /// loading (a pattern that does not compile, a word the file does not know, a number that is
        /// not one) and the species and notes vocabulary. Run on the document, before the typed read.
        /// </summary>
        private void CodeRuleProblems(List<string> problems)
        {
            var list = _root["codes"];
            if (list == null || list.Type == JTokenType.Null) return;
            var codes = list as JArray;
            if (codes == null) { problems.Add(PointFeaturesPrefix + "\"codes\" must be a list of features."); return; }

            var n = 0;
            foreach (var token in codes)
            {
                n++;
                var rule = token as JObject;
                if (rule == null) { problems.Add(PointFeaturesPrefix + "feature " + n + " in the list is not a feature (an object with id, match...)."); continue; }
                if (Text(rule["id"]).Trim().Length == 0) { problems.Add(PointFeaturesPrefix + "feature " + n + " in the list has no id."); continue; }
                var where = PointFeaturesPrefix + NameOf(rule) + ": ";

                foreach (var key in new[] { "description", "category", "match", "tagPrefix" })
                    if (rule[key] != null && rule[key].Type != JTokenType.String && rule[key].Type != JTokenType.Null)
                        problems.Add(where + key + " must be text.");
                if (rule["enabled"] != null && rule["enabled"].Type != JTokenType.Boolean)
                    problems.Add(where + "\"enabled\" must be true or false.");

                var species = SpeciesProblems(rule, where, problems);
                var info = ReadPattern(Text(rule["match"]), species);
                if (info.Problem != null) problems.Add(where + info.Problem);

                LabelWordProblems(rule, where, problems);
                RotationWordProblems(rule, where, problems);
                DriplineWordProblems(rule, where, problems);
                RangeCheckWordProblems(rule, where, problems);
                NotesProblems(rule, where, problems);
            }
        }

        /// <summary>The species map: CODE = NAME, codes plain, names given. Returns the codes.</summary>
        private static List<string> SpeciesProblems(JObject rule, string where, List<string> problems)
        {
            var codes = new List<string>();
            var token = rule["species"];
            if (token == null || token.Type == JTokenType.Null) return codes;
            var map = token as JObject;
            if (map == null) { problems.Add(where + "the species must be a list of code = name."); return codes; }
            foreach (var p in map.Properties())
            {
                var code = p.Name.Trim();
                if (code.Length == 0) { problems.Add(where + "a species has no code."); continue; }
                codes.Add(code);
                if (!PlainCode.IsMatch(code))
                    problems.Add(where + "species code '" + code + "' must be one plain code (letters, digits, - or _), as the crew types it.");
                if (p.Value.Type != JTokenType.String)
                    problems.Add(where + "the name of species " + code + " must be text.");
                else if (string.IsNullOrWhiteSpace((string)p.Value))
                    problems.Add(where + "species " + code + " has no name, so its label would leave the species out.");
            }
            foreach (var g in codes.GroupBy(c => c, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                problems.Add(where + "species " + g.Key + " is listed twice (codes ignore case).");
            return codes;
        }

        private static void LabelWordProblems(JObject rule, string where, List<string> problems)
        {
            var token = rule["label"];
            if (token == null || token.Type == JTokenType.Null) return;
            var label = token as JObject;
            if (label == null) { problems.Add(where + "\"label\" must be an object with format, layer and leader."); return; }
            foreach (var key in new[] { "format", "clusterFormat", "layer" })
                if (label[key] != null && label[key].Type != JTokenType.String && label[key].Type != JTokenType.Null)
                    problems.Add(where + "the label's " + key + " must be text.");
            WordProblem(label["leader"], Leaders, where + "the leader", problems);
        }

        private static void RotationWordProblems(JObject rule, string where, List<string> problems)
        {
            var token = rule["rotation"];
            if (token == null || token.Type == JTokenType.Null) return;
            var rotation = token as JObject;
            if (rotation == null) { problems.Add(where + "\"rotation\" must be an object with from and mode."); return; }
            if (Text(rotation["from"]).Trim().Length == 0)
                problems.Add(where + "turns the Civil 3D marker but names no field to read the angle from.");
            WordProblem(rotation["mode"], RotationModes, where + "the marker angle", problems);
        }

        private static void DriplineWordProblems(JObject rule, string where, List<string> problems)
        {
            var token = rule["dripLine"];
            if (token == null || token.Type == JTokenType.Null) return;
            var drip = token as JObject;
            if (drip == null) { problems.Add(where + "\"dripLine\" must be an object with layer, radiusFrom and unify."); return; }
            if (drip["unify"] != null && drip["unify"].Type != JTokenType.Boolean)
                problems.Add(where + "the dripline's \"unify\" (trim overlapping canopies) must be true or false.");
        }

        private static void RangeCheckWordProblems(JObject rule, string where, List<string> problems)
        {
            var token = rule["validate"];
            if (token == null || token.Type == JTokenType.Null) return;
            var checks = token as JArray;
            if (checks == null) { problems.Add(where + "the range checks (validate) must be a list."); return; }
            for (var i = 0; i < checks.Count; i++)
            {
                var c = checks[i] as JObject;
                var what = where + "range check " + (i + 1);
                if (c == null) { problems.Add(what + " is not a check (field, min, max, severity, message)."); continue; }
                double v;
                foreach (var key in new[] { "min", "max" })
                    if (c[key] != null && c[key].Type != JTokenType.Null && !TryNumber(c[key], out v))
                        problems.Add(what + ": its " + (key == "min" ? "lowest" : "highest") + " value '" + c[key] + "' is not a number.");
                if (c["message"] != null && c["message"].Type != JTokenType.String && c["message"].Type != JTokenType.Null)
                    problems.Add(what + ": its message must be text.");
                WordProblem(c["severity"], Severities, what + ": the severity", problems);
            }
        }

        private static void NotesProblems(JObject rule, string where, List<string> problems)
        {
            var token = rule["notes"];
            if (token == null || token.Type == JTokenType.Null) return;
            var notes = token as JObject;
            if (notes == null) { problems.Add(where + "\"notes\" must be an object (unknown, unknownGroup, terms, groups, order)."); return; }

            var unknown = notes["unknown"];
            WordProblem(unknown, UnknownModes, where + "what happens to a word in no term", problems);
            var refuse = unknown != null && string.Equals(Text(unknown), "Refuse", StringComparison.OrdinalIgnoreCase);
            var unknownGroup = notes["unknownGroup"] == null ? "other" : Text(notes["unknownGroup"]).Trim();
            if (!refuse && unknownGroup.Length == 0)
                problems.Add(where + "words in no term are labelled, but no group is named to keep them in: name one (\"other\" is usual), or choose not to draw such a point.");
            else if (unknownGroup.Length > 0 && !GroupName.IsMatch(unknownGroup))
                problems.Add(where + "the group for unknown words, '" + unknownGroup + "', cannot be named in a label: use letters, digits and _ only.");

            var termGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (unknownGroup.Length > 0) termGroups.Add(unknownGroup);
            var termsToken = notes["terms"];
            var terms = termsToken as JArray;
            if (termsToken != null && termsToken.Type != JTokenType.Null && terms == null)
                problems.Add(where + "the note terms must be a list.");
            for (var i = 0; terms != null && i < terms.Count; i++)
            {
                var t = terms[i] as JObject;
                var what = where + "note term " + (i + 1);
                if (t == null) { problems.Add(what + " is not a term (match, text, group)."); continue; }
                var match = Text(t["match"]).Trim();
                var group = Text(t["group"]).Trim();
                if (match.Length == 0) problems.Add(what + " has no words to match.");
                else
                {
                    what += " ('" + match + "')";
                    try
                    {
                        if (new Regex(@"^(?:" + match + @")(?=\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).IsMatch(string.Empty))
                            problems.Add(what + " matches nothing at all, so it would read every word.");
                    }
                    catch (ArgumentException ex) { problems.Add(what + " is an invalid regex (regular expression): " + ex.Message); }
                }
                if (group.Length == 0) problems.Add(what + " names no group to put the words in.");
                else if (!GroupName.IsMatch(group)) problems.Add(what + ": group '" + group + "' cannot be named in a label: use letters, digits and _ only.");
                else termGroups.Add(group);
            }

            var groupsToken = notes["groups"];
            var groups = groupsToken as JObject;
            if (groupsToken != null && groupsToken.Type != JTokenType.Null && groups == null)
                problems.Add(where + "the note group settings must be an object of group = { order, joiner }.");
            foreach (var p in groups != null ? groups.Properties().Where(p => !p.Name.StartsWith("//", StringComparison.Ordinal)) : Enumerable.Empty<JProperty>())
            {
                var what = where + "note group '" + p.Name + "'";
                var g = p.Value as JObject;
                if (g == null) { problems.Add(what + " must have an order and/or a joiner."); continue; }
                WordProblem(g["order"], GroupOrders, what + ": the order", problems);
                if (g["joiner"] != null && g["joiner"].Type != JTokenType.String && g["joiner"].Type != JTokenType.Null)
                    problems.Add(what + ": the joiner must be text.");
                if (!termGroups.Contains(p.Name.Trim()))
                    problems.Add(what + " has settings, but no term (nor the unknown words) uses that group.");
            }

            var orderToken = notes["order"];
            var order = orderToken as JArray;
            if (orderToken != null && orderToken.Type != JTokenType.Null && order == null)
                problems.Add(where + "the {notes} group order must be a list of group names.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in order ?? new JArray())
            {
                var name = Text(o).Trim();
                if (name.Length == 0) continue;
                if (!seen.Add(name)) problems.Add(where + "the {notes} order lists '" + name + "' twice.");
                else if (!termGroups.Contains(name)) problems.Add(where + "the {notes} order lists '" + name + "', but no term (nor the unknown words) uses that group.");
            }
        }

        /// <summary>A word the file must spell as one of <paramref name="allowed"/> (or leave out).</summary>
        private static void WordProblem(JToken token, string[] allowed, string what, List<string> problems)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type == JTokenType.String && allowed.Any(a => string.Equals(a, ((string)token).Trim(), StringComparison.OrdinalIgnoreCase))) return;
            problems.Add(what + " must be " + string.Join(", ", allowed.Take(allowed.Length - 1).ToArray()) + " or " + allowed.Last() + " (not '" + token + "').");
        }

        /// <summary>
        /// The checks that need the rule as the parser reads it (its pattern compiled): the fields a
        /// dripline and the range checks read exist, a dripline can be drawn, and each range check
        /// checks something. Called from EditorChecks for each rule whose pattern compiled.
        /// </summary>
        private static void CodeRuleChecks(CodeRule rule, IList<string> groups, HashSet<string> valid, List<string> problems)
        {
            var where = PointFeaturesPrefix + (string.IsNullOrWhiteSpace(rule.Description) ? "rule '" + rule.Id + "'" : rule.Description.Trim() + " (rule '" + rule.Id + "')") + ": ";
            var fields = groups.Count > 0 ? " (its fields: " + string.Join(", ", groups.ToArray()) + ")" : " (it has no named fields)";

            if (rule.DripLine != null)
            {
                if (string.IsNullOrWhiteSpace(rule.DripLine.Layer))
                    problems.Add(where + "draws a dripline but names no layer for it, so FTFDRIP would draw nothing.");
                if (string.IsNullOrWhiteSpace(rule.DripLine.RadiusFrom))
                    problems.Add(where + "draws a dripline but names no field to read its radius from, so FTFDRIP would draw nothing.");
                else if (!groups.Contains(rule.DripLine.RadiusFrom.Trim(), StringComparer.OrdinalIgnoreCase))
                    problems.Add(where + "reads the dripline radius from '" + rule.DripLine.RadiusFrom + "', which the match pattern does not capture" + fields + ".");
            }

            var n = 0;
            foreach (var check in rule.Validate ?? new List<RangeCheck>())
            {
                n++;
                if (check == null) continue;
                var what = where + "range check " + n;
                if (string.IsNullOrWhiteSpace(check.Field))
                    problems.Add(what + " names no field to check.");
                else if (!valid.Contains(check.Field.Trim()))
                    problems.Add(what + " reads '" + check.Field + "', which this feature never fills, so it would never run" + fields + ".");
                if (!check.Min.HasValue && !check.Max.HasValue)
                    problems.Add(what + (string.IsNullOrWhiteSpace(check.Field) ? string.Empty : " (" + check.Field.Trim() + ")") + " has no lowest or highest value, so it checks nothing.");
                else if (check.Min.HasValue && check.Max.HasValue && check.Min.Value > check.Max.Value)
                    problems.Add(what + " (" + (check.Field ?? string.Empty).Trim() + "): the lowest value " +
                                 check.Min.Value.ToString(CultureInfo.InvariantCulture) + " is above the highest " +
                                 check.Max.Value.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }
    }
}
