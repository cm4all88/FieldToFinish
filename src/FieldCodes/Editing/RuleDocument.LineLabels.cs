using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Editing
{
    /// <summary>
    /// One line feature (the rules file's lineFeatures) as Settings > Line Labels edits it: every
    /// field of <see cref="LineFeatureRule"/>. Anything else on the entry (a "//layer" note, a
    /// property a later build reads) is kept by ApplyLineFeature.
    /// </summary>
    public sealed class EditableLineFeature
    {
        public EditableLineFeature()
        {
            SourceIndex = -1;
            Enabled = true;
            Aliases = new List<string>();
            AltLabels = new List<string>();
            BetweenLabels = new List<string>();
        }

        /// <summary>Its place in the file's lineFeatures list: how ApplyLineFeature and
        /// RemoveLineFeature find it, so its code can be changed. -1 finds it by Code.</summary>
        public int SourceIndex { get; set; }

        /// <summary>The figure code the crew shoots it with (FNC): a survey figure named FNC1, a TBC
        /// line coded FNC and a line drawn through shots coded FNC are this feature.</summary>
        public string Code { get; set; }

        /// <summary>What the feature is called in FTFLINES and the review lists.</summary>
        public string Name { get; set; }

        /// <summary>The layer the office figure prefix database puts the figure on: a plain
        /// polyline on it is this feature (or one of the features sharing the layer). Blank: found
        /// by its figure name, TBC name or shots only.</summary>
        public string Layer { get; set; }

        /// <summary>What the label says; blank means "identified, not labelled".</summary>
        public string Label { get; set; }

        /// <summary>The label layer; blank lets FTF choose (the text layer that matches the line's
        /// layer, else the fallback label layer).</summary>
        public string LabelLayer { get; set; }

        /// <summary>Left, Right, OnLine, or blank for Auto (the settings' default placement).</summary>
        public string Placement { get; set; }

        /// <summary>Other names it goes by: TBC export names ("Edge of Conc") and other codes it is
        /// shot with. Matched whole, ignoring case.</summary>
        public List<string> Aliases { get; set; }

        /// <summary>Other wordings FTFLABELLINE's Wording option offers ("EOP").</summary>
        public List<string> AltLabels { get; set; }

        /// <summary>What FTFLABELBETWEEN labels the surface between two of these edges; the first
        /// is its default, the rest its Wording choices.</summary>
        public List<string> BetweenLabels { get; set; }

        /// <summary>False: still identified (FTFLINES names it), never labelled.</summary>
        public bool Enabled { get; set; }

        /// <summary>Labelled even when its linetype draws its own letter or symbol.</summary>
        public bool AlwaysLabel { get; set; }

        /// <summary>Reads a height from its shots (FNC B 3.5) for the label: true, false, or null
        /// for automatic (when its name or label says FENCE).</summary>
        public bool? ReadHeight { get; set; }

        /// <summary>A list as it is written: trimmed, blanks dropped, each once (ignoring case), in
        /// the order given.</summary>
        public static List<string> CleanList(IEnumerable<string> items)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var item in items ?? Enumerable.Empty<string>())
            {
                var t = (item ?? string.Empty).Trim();
                if (t.Length > 0 && seen.Add(t)) result.Add(t);
            }
            return result;
        }

        /// <summary>A one-per-line box as a list (blank lines dropped, each once).</summary>
        public static List<string> SplitLines(string text)
        {
            return CleanList((text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>Why a code cannot be a line feature's, or null when it can: one word, not
        /// ending in a number (FTF reads a number at the end as the figure number).</summary>
        public static string CodeProblem(string code)
        {
            var c = (code ?? string.Empty).Trim();
            if (c.Length == 0) return "a line feature needs a code: the figure code the crew shoots it with (FNC).";
            if (c.Any(char.IsWhiteSpace)) return "\"" + c + "\" is not one code: a code has no spaces.";
            if (char.IsDigit(c[c.Length - 1]))
                return "the code \"" + c + "\" ends in a number, which FTF reads as the figure number (FNC2 is FNC's second line), " +
                       "so a line coded " + c + " would be taken for " + (LineworkName(c) ?? "another code") + ".";
            return null;
        }

        private static string LineworkName(string code)
        {
            var end = code.Length;
            while (end > 0 && char.IsDigit(code[end - 1])) end--;
            return end == 0 ? null : code.Substring(0, end);
        }
    }

    /// <summary>One span marker (spanMarkers): a word in the notes of two shots along a line that
    /// marks a span between them, labelled centred between the pair.</summary>
    public sealed class EditableSpanMarker
    {
        public EditableSpanMarker()
        {
            SourceIndex = -1;
            Enabled = true;
        }

        /// <summary>Its place in the file's spanMarkers list (its other properties are kept);
        /// -1 for a new one.</summary>
        public int SourceIndex { get; set; }

        /// <summary>The note word (FGP), matched as a whole word of a shot's description.</summary>
        public string Token { get; set; }

        /// <summary>What the label says; blank says the note word itself.</summary>
        public string Label { get; set; }

        public bool Enabled { get; set; }

        /// <summary>The text a marked span is labelled with.</summary>
        public string Shows
        {
            get { return string.IsNullOrWhiteSpace(Label) ? (Token ?? string.Empty).Trim() : Label; }
        }
    }

    /// <summary>The rest of the rules file Settings > Line Labels edits: the stairs label
    /// (FTFLABELSTAIRS), the span markers (FTFLABELLINE, FTFLINELABELS) and the TBC names mapped to
    /// nothing (the line inventory).</summary>
    public sealed class EditableLineLabelRules
    {
        /// <summary>What FTFLABELSTAIRS says; {count} is the number of stair lines picked.</summary>
        public string StairsLabelFormat { get; set; }

        public List<EditableSpanMarker> SpanMarkers { get; set; }

        /// <summary>TBC export names recognised but identifying nothing ("INFO LINE").</summary>
        public List<string> IgnoreLineNames { get; set; }

        /// <summary>What a rules file that says nothing about them means (RulesConfig's own
        /// values): the built-in stairs wording, no span markers, no ignored names.</summary>
        public static EditableLineLabelRules Defaults()
        {
            return new EditableLineLabelRules
            {
                StairsLabelFormat = FieldCodes.Linework.LineLabelPlanner.DefaultStairsFormat,
                SpanMarkers = new List<EditableSpanMarker>(),
                IgnoreLineNames = new List<string>()
            };
        }
    }

    public sealed partial class RuleDocument
    {
        private const string LineFeaturesKey = "lineFeatures";
        private const string SpanMarkersKey = "spanMarkers";
        private const string IgnoreLineNamesKey = "ignoreLineNames";
        private const string StairsKey = "stairsLabelFormat";
        private static readonly string[] LineLabelKeys = { LineFeaturesKey, SpanMarkersKey, IgnoreLineNamesKey, StairsKey };
        private static readonly string[] LineFeatureTextKeys = { "code", "name", "layer", "label", "labelLayer", "placement" };
        private static readonly string[] LineFeatureListKeys = { "aliases", "altLabels", "betweenLabels" };
        private static readonly Regex StairsField = new Regex(@"\{(?<name>[^{}]*)\}");

        // ------------------------------------------------------------------ line features

        /// <summary>The line features (lineFeatures) as the Line Labels page edits them, in file
        /// order, each with its place in the list.</summary>
        public IList<EditableLineFeature> GetLineFeatures()
        {
            var list = new List<EditableLineFeature>();
            var array = _root[LineFeaturesKey] as JArray;
            if (array == null) return list;
            for (var i = 0; i < array.Count; i++)
            {
                var f = array[i] as JObject;
                if (f != null) list.Add(ReadLineFeature(f, i));
            }
            return list;
        }

        /// <summary>Writes one line feature back, found by its place in the list (or by its code
        /// when it has none). Only the fields that changed are written; every other property of the
        /// entry (a "//layer" note, a property a later build reads) is kept.</summary>
        public void ApplyLineFeature(EditableLineFeature edit)
        {
            if (edit == null) throw new ArgumentNullException("edit");
            var f = FindLineFeature(edit);
            if (f == null) throw new ConfigException("No line feature with code '" + edit.Code + "'.");
            WriteLineFeature(f, edit);
        }

        /// <summary>Adds a line feature at the end of the list and returns it as GetLineFeatures
        /// shows it. Its code is written in capitals and must be one word that no other line
        /// feature has.</summary>
        public EditableLineFeature AddLineFeature(EditableLineFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            var code = (feature.Code ?? string.Empty).Trim().ToUpperInvariant();
            var problem = EditableLineFeature.CodeProblem(code);
            if (problem != null) throw new ConfigException(Capital(problem));
            if (GetLineFeatures().Any(f => string.Equals((f.Code ?? string.Empty).Trim(), code, StringComparison.OrdinalIgnoreCase)))
                throw new ConfigException("There is already a line feature with the code " + code + ".");

            var token = _root[LineFeaturesKey];
            var array = token as JArray;
            if (token != null && token.Type != JTokenType.Null && array == null)
                throw new ConfigException("The rules file's lineFeatures is not a list, so nothing can be added to it.");
            if (array == null)
            {
                array = new JArray();
                _root[LineFeaturesKey] = array;
            }

            var entry = new JObject { ["code"] = code };
            array.Add(entry);
            var values = Copy(feature);
            values.Code = code;
            WriteLineFeature(entry, values);
            return ReadLineFeature(entry, array.Count - 1);
        }

        /// <summary>Removes a line feature, found as ApplyLineFeature finds it. False when there is
        /// no such feature. The places of the features after it move up by one.</summary>
        public bool RemoveLineFeature(EditableLineFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            var f = FindLineFeature(feature);
            if (f == null) return false;
            f.Remove();
            return true;
        }

        private JObject FindLineFeature(EditableLineFeature edit)
        {
            var array = _root[LineFeaturesKey] as JArray;
            if (array == null) return null;
            if (edit.SourceIndex >= 0) return edit.SourceIndex < array.Count ? array[edit.SourceIndex] as JObject : null;
            var code = (edit.Code ?? string.Empty).Trim();
            return array.OfType<JObject>().FirstOrDefault(x => string.Equals(PlainText(x["code"]).Trim(), code, StringComparison.OrdinalIgnoreCase));
        }

        private static EditableLineFeature ReadLineFeature(JObject f, int index)
        {
            var readHeight = f["readHeight"];
            return new EditableLineFeature
            {
                SourceIndex = index,
                Code = PlainText(f["code"]),
                Name = PlainText(f["name"]),
                Layer = PlainText(f["layer"]),
                Label = PlainText(f["label"]),
                LabelLayer = PlainText(f["labelLayer"]),
                Placement = PlainText(f["placement"]),
                Aliases = TextList(f["aliases"]),
                AltLabels = TextList(f["altLabels"]),
                BetweenLabels = TextList(f["betweenLabels"]),
                Enabled = Flag(f, "enabled", true),
                AlwaysLabel = Flag(f, "alwaysLabel", false),
                ReadHeight = readHeight != null && readHeight.Type == JTokenType.Boolean ? (bool?)(bool)readHeight : null
            };
        }

        private static void WriteLineFeature(JObject f, EditableLineFeature edit)
        {
            var current = ReadLineFeature(f, -1);
            WriteText(f, "code", current.Code, edit.Code, true);
            WriteText(f, "name", current.Name, edit.Name, false);
            WriteText(f, "layer", current.Layer, edit.Layer, false);
            WriteText(f, "label", current.Label, edit.Label, false);
            WriteText(f, "labelLayer", current.LabelLayer, edit.LabelLayer, false);
            var side = (edit.Placement ?? string.Empty).Trim();
            if (side.Equals("Auto", StringComparison.OrdinalIgnoreCase)) side = string.Empty;
            WriteText(f, "placement", current.Placement, side, false);
            WriteList(f, "aliases", edit.Aliases);
            WriteList(f, "altLabels", edit.AltLabels);
            WriteList(f, "betweenLabels", edit.BetweenLabels);
            WriteFlag(f, "enabled", current.Enabled, edit.Enabled, true);
            WriteFlag(f, "alwaysLabel", current.AlwaysLabel, edit.AlwaysLabel, false);

            var t = f["readHeight"];
            var bad = t != null && t.Type != JTokenType.Boolean && t.Type != JTokenType.Null;
            if (bad || current.ReadHeight != edit.ReadHeight)
            {
                if (edit.ReadHeight.HasValue) f["readHeight"] = edit.ReadHeight.Value;
                else f.Remove("readHeight");
            }
        }

        private static EditableLineFeature Copy(EditableLineFeature f)
        {
            return new EditableLineFeature
            {
                SourceIndex = f.SourceIndex, Code = f.Code, Name = f.Name, Layer = f.Layer, Label = f.Label,
                LabelLayer = f.LabelLayer, Placement = f.Placement,
                Aliases = (f.Aliases ?? new List<string>()).ToList(),
                AltLabels = (f.AltLabels ?? new List<string>()).ToList(),
                BetweenLabels = (f.BetweenLabels ?? new List<string>()).ToList(),
                Enabled = f.Enabled, AlwaysLabel = f.AlwaysLabel, ReadHeight = f.ReadHeight
            };
        }

        // ------------------------------------------------- stairs, span markers, ignored names

        /// <summary>The stairs label, span markers and ignored TBC names, with the built-in value
        /// for anything the file leaves out.</summary>
        public EditableLineLabelRules GetLineLabelRules()
        {
            var result = EditableLineLabelRules.Defaults();
            var stairs = _root[StairsKey];
            if (stairs != null && stairs.Type == JTokenType.String) result.StairsLabelFormat = (string)stairs;

            var spans = _root[SpanMarkersKey] as JArray;
            if (spans != null)
                for (var i = 0; i < spans.Count; i++)
                {
                    var m = spans[i] as JObject;
                    if (m == null) continue;
                    result.SpanMarkers.Add(new EditableSpanMarker
                    {
                        SourceIndex = i,
                        Token = PlainText(m["token"]),
                        Label = PlainText(m["label"]),
                        Enabled = Flag(m, "enabled", true)
                    });
                }

            if (_root[IgnoreLineNamesKey] is JArray) result.IgnoreLineNames = TextList(_root[IgnoreLineNamesKey]);
            return result;
        }

        /// <summary>Writes what differs from the file; nothing else in it is touched. A span marker
        /// keeps the other properties of the entry it was read from.</summary>
        public void ApplyLineLabelRules(EditableLineLabelRules edit)
        {
            if (edit == null) throw new ArgumentNullException("edit");
            var current = GetLineLabelRules();

            var format = edit.StairsLabelFormat ?? string.Empty;
            var stairs = _root[StairsKey];
            var badStairs = stairs != null && stairs.Type != JTokenType.String && stairs.Type != JTokenType.Null;
            if (badStairs || !string.Equals(current.StairsLabelFormat, format, StringComparison.Ordinal)) _root[StairsKey] = format;

            ApplySpanMarkers(edit.SpanMarkers ?? new List<EditableSpanMarker>());

            var names = EditableLineFeature.CleanList(edit.IgnoreLineNames);
            var token = _root[IgnoreLineNamesKey];
            var unchanged = token == null || token.Type == JTokenType.Null
                ? names.Count == 0
                : token is JArray && EditableLineFeature.CleanList(TextList(token)).SequenceEqual(names, StringComparer.Ordinal);
            if (!unchanged) _root[IgnoreLineNamesKey] = new JArray(names.Cast<object>().ToArray());
        }

        private void ApplySpanMarkers(IList<EditableSpanMarker> markers)
        {
            var token = _root[SpanMarkersKey];
            var current = token as JArray;
            var result = new JArray();
            foreach (var m in markers.Where(m => m != null))
            {
                var source = current != null && m.SourceIndex >= 0 && m.SourceIndex < current.Count ? current[m.SourceIndex] as JObject : null;
                var entry = source != null ? (JObject)source.DeepClone() : new JObject();
                WriteText(entry, "token", PlainText(entry["token"]), m.Token, true);
                WriteText(entry, "label", PlainText(entry["label"]), m.Label, false);
                WriteFlag(entry, "enabled", Flag(entry, "enabled", true), m.Enabled, true);
                result.Add(entry);
            }
            if (token == null || token.Type == JTokenType.Null)
            {
                if (result.Count == 0) return;
            }
            else if (current != null && JToken.DeepEquals(current, result)) return;
            _root[SpanMarkersKey] = result;
        }

        /// <summary>Puts this document's line features, span markers, ignored TBC names and stairs
        /// label onto another (Save re-reads the file first, so another page's save in between is
        /// kept). Each is copied only when this document has it and the other's differs.</summary>
        public void CopyLineLabelRulesTo(RuleDocument other)
        {
            if (other == null) throw new ArgumentNullException("other");
            foreach (var key in LineLabelKeys)
            {
                var mine = _root[key];
                if (mine == null) continue;
                var theirs = other._root[key];
                if (theirs != null && JToken.DeepEquals(mine, theirs)) continue;
                other._root[key] = mine.DeepClone();
            }
        }

        // ------------------------------------------------------------------ checks

        private void LineLabelProblems(List<string> problems)
        {
            const string page = "Line Labels: ";
            LineFeatureProblems(page, problems);
            SpanMarkerProblems(page, problems);
            IgnoredNameProblems(page, problems);
            StairsProblems(page, problems);
        }

        private void LineFeatureProblems(string page, List<string> problems)
        {
            var token = _root[LineFeaturesKey];
            if (token == null || token.Type == JTokenType.Null) return;
            var array = token as JArray;
            if (array == null) { problems.Add(page + "the line features (lineFeatures) must be a list."); return; }

            var codes = new List<string>();
            for (var i = 0; i < array.Count; i++)
            {
                var f = array[i] as JObject;
                if (f == null) { problems.Add(page + "line feature " + (i + 1) + " is not an entry with a code."); continue; }
                var code = PlainText(f["code"]).Trim();
                var who = page + "line feature " + (code.Length > 0 ? code : (i + 1).ToString(CultureInfo.InvariantCulture)) + ": ";

                foreach (var key in LineFeatureTextKeys)
                    if (NotText(f[key])) problems.Add(who + key + " must be text.");
                foreach (var key in LineFeatureListKeys)
                {
                    var list = f[key];
                    if (list == null || list.Type == JTokenType.Null) continue;
                    if (!(list is JArray)) problems.Add(who + key + " must be a list.");
                    else if (list.Any(t => t.Type != JTokenType.String)) problems.Add(who + "every entry of " + key + " must be text.");
                }
                foreach (var key in new[] { "enabled", "alwaysLabel" })
                    if (f[key] != null && f[key].Type != JTokenType.Boolean) problems.Add(who + key + " must be true or false.");
                if (f["readHeight"] != null && f["readHeight"].Type != JTokenType.Boolean && f["readHeight"].Type != JTokenType.Null)
                    problems.Add(who + "readHeight must be true, false or left out (automatic).");

                var codeProblem = EditableLineFeature.CodeProblem(code);
                if (codeProblem != null) problems.Add(who + codeProblem);
                else codes.Add(code);

                string why;
                var layer = PlainText(f["layer"]);
                if (!string.IsNullOrWhiteSpace(layer) && !FieldCodes.Settings.LayerMap.IsValidName(layer, out why))
                    problems.Add(who + "the layer it is identified on, \"" + layer + "\", cannot be used: " + why + ".");
                var labelLayer = PlainText(f["labelLayer"]);
                if (!string.IsNullOrWhiteSpace(labelLayer) && !FieldCodes.Settings.LayerMap.IsValidName(labelLayer, out why))
                    problems.Add(who + "the label layer \"" + labelLayer + "\" cannot be used: " + why + ".");
                var side = PlainText(f["placement"]).Trim();
                if (side.Length > 0 && !new[] { "Left", "Right", "OnLine" }.Contains(side, StringComparer.OrdinalIgnoreCase))
                    problems.Add(who + "the side must be Left, Right or OnLine (or left out for Auto), not \"" + side + "\".");
            }

            foreach (var twice in codes.GroupBy(c => c, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                problems.Add(page + "the code " + twice.Key.ToUpperInvariant() + " is given to " + twice.Count() +
                             " line features: give each its own code (a line coded " + twice.Key.ToUpperInvariant() + " is only ever the first).");
        }

        private void SpanMarkerProblems(string page, List<string> problems)
        {
            var token = _root[SpanMarkersKey];
            if (token == null || token.Type == JTokenType.Null) return;
            var array = token as JArray;
            if (array == null) { problems.Add(page + "the span markers (spanMarkers) must be a list."); return; }

            var words = new List<string>();
            for (var i = 0; i < array.Count; i++)
            {
                var m = array[i] as JObject;
                var n = (i + 1).ToString(CultureInfo.InvariantCulture);
                if (m == null) { problems.Add(page + "span marker " + n + " is not an entry with a note word."); continue; }
                var word = PlainText(m["token"]).Trim();
                var who = page + "span marker " + (word.Length > 0 ? word : n) + ": ";
                foreach (var key in new[] { "token", "label" })
                    if (NotText(m[key])) problems.Add(who + key + " must be text.");
                if (m["enabled"] != null && m["enabled"].Type != JTokenType.Boolean) problems.Add(who + "enabled must be true or false.");

                if (word.Length == 0)
                    problems.Add(page + "span marker " + n + " has no note word (the word in the shots' notes that marks the span, such as FGP).");
                else if (word.Any(char.IsWhiteSpace))
                    problems.Add(page + "the span marker \"" + word + "\" is not one word: it is matched as a whole word of a shot's note, so it cannot have spaces.");
                else words.Add(word);
            }

            foreach (var twice in words.GroupBy(w => w, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                problems.Add(page + "the span marker " + twice.Key + " is listed " + twice.Count() + " times: each marked span would be labelled once per row.");
        }

        private void IgnoredNameProblems(string page, List<string> problems)
        {
            var token = _root[IgnoreLineNamesKey];
            if (token == null || token.Type == JTokenType.Null) return;
            var array = token as JArray;
            if (array == null) { problems.Add(page + "the ignored TBC names (ignoreLineNames) must be a list."); return; }
            if (array.Any(t => t.Type != JTokenType.String)) problems.Add(page + "every ignored TBC name must be text.");

            // A name a line feature claims is identified as that feature first; ignoring it does nothing.
            var features = GetLineFeatures()
                .Select(f => new LineFeatureRule { Code = f.Code, Name = f.Name, Label = f.Label, Aliases = f.Aliases })
                .ToList();
            var catalog = new FieldCodes.Linework.LineworkCatalog(features);
            foreach (var name in EditableLineFeature.CleanList(TextList(token)))
            {
                var claimed = catalog.FindByTrimbleName(name).FirstOrDefault();
                if (claimed == null) continue;
                var what = claimed.Aliases.Any(a => string.Equals((a ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    ? "one of " + claimed.Code + "'s other names"
                    : string.Equals(name, claimed.Name, StringComparison.OrdinalIgnoreCase)
                        ? claimed.Code + "'s feature name"
                        : string.Equals(name, claimed.Label, StringComparison.OrdinalIgnoreCase)
                            ? claimed.Code + "'s label text"
                            : "read as the code " + claimed.Code;
                problems.Add(page + "\"" + name + "\" is an ignored TBC name but is also " + what + ", so a TBC line named so is " +
                             claimed.Code + " and ignoring it does nothing. Take it off one of the two.");
            }
        }

        private void StairsProblems(string page, List<string> problems)
        {
            var token = _root[StairsKey];
            if (token == null || token.Type == JTokenType.Null) return;
            if (token.Type != JTokenType.String) { problems.Add(page + "the stairs label (stairsLabelFormat) must be text."); return; }
            var format = (string)token;
            if (string.IsNullOrWhiteSpace(format))
            {
                problems.Add(page + "the stairs label is empty: FTFLABELSTAIRS would place a label with nothing in it.");
                return;
            }
            var unknown = StairsField.Matches(format).Cast<Match>().Select(m => m.Groups["name"].Value)
                                     .Where(n => n != "count").Distinct(StringComparer.Ordinal).ToList();
            if (unknown.Count > 0)
                problems.Add(page + "the stairs label uses {" + string.Join("}, {", unknown.ToArray()) +
                             "}; it can use {count} (the number of stair lines picked), written in lower case.");
            if (format.Count(c => c == '{') != format.Count(c => c == '}'))
                problems.Add(page + "the stairs label has a { without its } (or the other way round).");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A text property's value; empty when it is absent, null or not a plain value.</summary>
        private static string PlainText(JToken token)
        {
            var value = token as JValue;
            if (value == null || value.Value == null) return string.Empty;
            return Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        /// <summary>The text entries of a list (a single text value reads as a list of one).</summary>
        private static List<string> TextList(JToken token)
        {
            var array = token as JArray;
            if (array != null) return array.Where(t => t.Type == JTokenType.String).Select(t => (string)t).ToList();
            return token != null && token.Type == JTokenType.String ? new List<string> { (string)token } : new List<string>();
        }

        private static bool NotText(JToken token)
        {
            return token != null && token.Type != JTokenType.String && token.Type != JTokenType.Null;
        }

        /// <summary>Writes a trimmed value when it differs from the file's (or the file's is not
        /// text); a blank one removes the property unless it is kept as "".</summary>
        private static void WriteText(JObject o, string key, string current, string wanted, bool keepBlank)
        {
            var value = (wanted ?? string.Empty).Trim();
            if (!NotText(o[key]) && string.Equals((current ?? string.Empty).Trim(), value, StringComparison.Ordinal)) return;
            if (value.Length == 0 && !keepBlank) o.Remove(key);
            else o[key] = value;
        }

        /// <summary>Writes a list when it differs from the file's; an empty one removes it.</summary>
        private static void WriteList(JObject o, string key, IEnumerable<string> wanted)
        {
            var clean = EditableLineFeature.CleanList(wanted);
            var token = o[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                if (clean.Count == 0) return;
            }
            else if (token is JArray && token.All(t => t.Type == JTokenType.String) &&
                     EditableLineFeature.CleanList(TextList(token)).SequenceEqual(clean, StringComparer.Ordinal)) return;
            if (clean.Count == 0) o.Remove(key);
            else o[key] = new JArray(clean.Cast<object>().ToArray());
        }

        /// <summary>Writes a true/false when it differs from the file's (or the file's is not one);
        /// the value an absent property means is written by removing it.</summary>
        private static void WriteFlag(JObject o, string key, bool current, bool wanted, bool absentMeans)
        {
            var t = o[key];
            var bad = t != null && t.Type != JTokenType.Boolean;
            if (!bad && current == wanted) return;
            if (wanted == absentMeans) o.Remove(key);
            else o[key] = wanted;
        }

        private static string Capital(string s)
        {
            return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
