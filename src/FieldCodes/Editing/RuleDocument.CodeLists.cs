using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FieldCodes.Linework;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Editing
{
    /// <summary>One code FTF never draws (ignoreCodes), as Settings > Field Code Lists shows it.</summary>
    public sealed class EditableIgnoreCode
    {
        /// <summary>The bare code; a figure number after it is covered too (GS covers GS2).</summary>
        public string Code { get; set; }

        /// <summary>Why it is not drawn. Blank: none written (Point Accountability asks for one).</summary>
        public string Reason { get; set; }

        /// <summary>What holds its points instead: blank, or "surface" (a TIN built from them).</summary>
        public string Expect { get; set; }

        /// <summary>The values "expect" may take: blank (nothing) or surface.</summary>
        public static readonly string[] ExpectValues = { string.Empty, "surface" };
    }

    /// <summary>One bare symbol code labelled with itself (symbolLabels).</summary>
    public sealed class EditableSymbolLabel
    {
        public string Code { get; set; }

        /// <summary>The text layer the label goes on.</summary>
        public string Layer { get; set; }

        /// <summary>What the label says; blank: the code exactly as shot.</summary>
        public string Label { get; set; }

        /// <summary>Off: the entry stays in the file but the code is not labelled.</summary>
        public bool Enabled { get; set; }

        public EditableSymbolLabel() { Enabled = true; }
    }

    /// <summary>One description modifier (modifiers): a word after the code, matched by a pattern,
    /// that changes how the point is labelled and drawn.</summary>
    public sealed class EditableModifier
    {
        /// <summary>Its name; the tag table's notes column shows it (DEAD).</summary>
        public string Id { get; set; }

        /// <summary>The pattern one word is matched against (^DEAD$).</summary>
        public string Pattern { get; set; }

        /// <summary>Lower applies first, whatever order the words were written in.</summary>
        public int Priority { get; set; }

        /// <summary>Replaces the whole label (with {fields}); blank: the label is kept.</summary>
        public string LabelFormat { get; set; }

        /// <summary>Added to the label's first line, exactly as written (" (DEAD)").</summary>
        public string LabelSuffix { get; set; }

        /// <summary>A block put in place of the rule's own.</summary>
        public string Block { get; set; }

        /// <summary>Added to the label, symbol and dripline layer names ("-DEAD").</summary>
        public string LayerSuffix { get; set; }

        /// <summary>The named part of the pattern holding a stem count (CL3: count).</summary>
        public string StemCountFrom { get; set; }

        /// <summary>The point is listed as a warning for review.</summary>
        public bool Flag { get; set; }
    }

    /// <summary>The office's Civil 3D linework command codes (lineworkCommands): which words after a
    /// line code begin, end or close it, and the escape character.</summary>
    public sealed class EditableLineworkCommands
    {
        /// <summary>The commands, in the order the page lists them, by their key in the rules file.</summary>
        public static readonly string[] Keys =
        {
            "begin", "end", "close", "continue", "curveBegin", "curveEnd", "rectangle", "rightTurn",
            "extend", "circle", "pointOnCurve", "offset", "recallPoint", "connectPoint"
        };

        /// <summary>Codes per command key; a command with no codes is not used.</summary>
        public Dictionary<string, List<string>> Codes { get; set; }

        /// <summary>A word starting with this is plain text (PMX: "/"); blank: none.</summary>
        public string Escape { get; set; }

        public EditableLineworkCommands()
        {
            Codes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var key in Keys) Codes[key] = new List<string>();
            Escape = string.Empty;
        }

        /// <summary>The codes listed for a command (never null).</summary>
        public List<string> For(string key)
        {
            List<string> list;
            if (Codes == null) Codes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (!Codes.TryGetValue(key, out list) || list == null)
            {
                list = new List<string>();
                Codes[key] = list;
            }
            return list;
        }

        /// <summary>The built-in set, PMX-Universal: B begin, E end, C close... and "/".</summary>
        public static EditableLineworkCommands BuiltIn()
        {
            return From(LineworkCommandSettings.Pmx());
        }

        public static EditableLineworkCommands From(LineworkCommandSettings settings)
        {
            var result = new EditableLineworkCommands();
            var s = settings ?? LineworkCommandSettings.Pmx();
            foreach (var key in Keys)
                result.Codes[key] = (ListOf(s, key) ?? new List<string>()).ToList();
            result.Escape = s.Escape ?? string.Empty;
            return result;
        }

        /// <summary>What the command is called on the page.</summary>
        public static string Name(string key)
        {
            switch (key)
            {
                case "begin": return "Begin";
                case "end": return "End";
                case "close": return "Close";
                case "continue": return "Continue";
                case "curveBegin": return "Begin curve";
                case "curveEnd": return "End curve";
                case "rectangle": return "Rectangle";
                case "rightTurn": return "Right turn";
                case "extend": return "Extend";
                case "circle": return "Circle";
                case "pointOnCurve": return "Point on curve";
                case "offset": return "Offset";
                case "recallPoint": return "Recall point";
                case "connectPoint": return "Connect point";
                default: return key;
            }
        }

        /// <summary>What the command does, in a few words.</summary>
        public static string Meaning(string key)
        {
            switch (key)
            {
                case "begin": return "Starts the line at this shot.";
                case "end": return "Ends the line at this shot.";
                case "close": return "Ends the line and closes it back to where it began.";
                case "continue": return "Carries the line on from its last shot.";
                case "curveBegin": return "Starts a curve at this shot.";
                case "curveEnd": return "Ends the curve at this shot.";
                case "rectangle": return "Makes a rectangle from the shots; a number after it belongs to it.";
                case "rightTurn": return "Turns the line at a right angle.";
                case "extend": return "Extends the line.";
                case "circle": return "Makes a circle; a number after it belongs to it.";
                case "pointOnCurve": return "Marks a shot on a curve.";
                case "offset": return "Offsets the line (horizontal, vertical, stop); the number after it is the distance.";
                case "recallPoint": return "Brings an earlier point (its number follows) into the line.";
                case "connectPoint": return "Connects the line to a point (its number follows).";
                default: return string.Empty;
            }
        }

        internal static List<string> ListOf(LineworkCommandSettings s, string key)
        {
            switch (key)
            {
                case "begin": return s.Begin;
                case "end": return s.End;
                case "close": return s.Close;
                case "continue": return s.Continue;
                case "curveBegin": return s.CurveBegin;
                case "curveEnd": return s.CurveEnd;
                case "rectangle": return s.Rectangle;
                case "rightTurn": return s.RightTurn;
                case "extend": return s.Extend;
                case "circle": return s.Circle;
                case "pointOnCurve": return s.PointOnCurve;
                case "offset": return s.Offset;
                case "recallPoint": return s.RecallPoint;
                case "connectPoint": return s.ConnectPoint;
                default: return null;
            }
        }
    }

    /// <summary>
    /// The rules file's lists that say how the crew's field codes are read, as Settings > Field
    /// Code Lists shows them: the codes never drawn (ignoreCodes), the bare symbol codes labelled
    /// with themselves (symbolLabels), the control codes (controlCodes), the linework codes
    /// (lineworkCodes), the linework command codes (lineworkCommands) and the description
    /// modifiers (modifiers). Entries are kept in the file's order.
    /// </summary>
    public sealed class EditableCodeLists
    {
        /// <summary>The page's tab names, which every check message names.</summary>
        public const string NeverDrawnTab = "Never Drawn";
        public const string SymbolLabelsTab = "Labelled Symbols";
        public const string ControlTab = "Control";
        public const string LineworkTab = "Linework";
        public const string LineCommandsTab = "Line Commands";
        public const string ModifiersTab = "Modifiers";

        public List<EditableIgnoreCode> IgnoreCodes { get; set; }
        public List<EditableSymbolLabel> SymbolLabels { get; set; }
        public List<string> ControlCodes { get; set; }
        public List<string> LineworkCodes { get; set; }
        public EditableLineworkCommands LineworkCommands { get; set; }
        public List<EditableModifier> Modifiers { get; set; }

        /// <summary>True when the rules file lists its own linework commands; false means the
        /// built-in set is shown, which the first save writes into the file.</summary>
        public bool LineworkCommandsInFile { get; set; }

        /// <summary>Empty lists and the built-in linework commands: what a rules file without
        /// any of these keys means.</summary>
        public static EditableCodeLists Defaults()
        {
            return new EditableCodeLists
            {
                IgnoreCodes = new List<EditableIgnoreCode>(),
                SymbolLabels = new List<EditableSymbolLabel>(),
                ControlCodes = new List<string>(),
                LineworkCodes = new List<string>(),
                LineworkCommands = EditableLineworkCommands.BuiltIn(),
                Modifiers = new List<EditableModifier>()
            };
        }

        /// <summary>Codes typed in one box, one per line or separated by commas or spaces: trimmed,
        /// blanks dropped, kept in order and as typed (a code typed twice is reported, not dropped).</summary>
        public static List<string> SplitCodes(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
        }
    }

    /// <summary>
    /// What the lists make of one description, in plain words, for the page's "Try a description"
    /// box: never drawn (and why), a labelled bare symbol, a control point, linework with its
    /// commands, the modifiers it carries, or what else the parser says.
    /// </summary>
    public static class CodeListPreview
    {
        public static IList<string> Read(RulesConfig cfg, string description)
        {
            var lines = new List<string>();
            if (cfg == null || string.IsNullOrWhiteSpace(description)) return lines;
            var text = Regex.Replace(description.Trim(), @"\s+", " ");
            var token = text.Split(' ')[0];

            var control = (cfg.ControlCodes ?? new List<string>()).Any(c => string.Equals((c ?? string.Empty).Trim(), token, StringComparison.OrdinalIgnoreCase));
            if (control) lines.Add("Control: " + token + " is a control code, so FTFCONTROL lists the point in the control table.");

            ParsedPoint parsed;
            try { parsed = new FieldCodeParser(cfg).Parse("preview", text); }
            catch (ArgumentException ex) { lines.Add("Could not be read: " + ex.Message); return lines; }

            var ignoreEntry = NotedCodeList.EntryFor(cfg.IgnoreCodes, token);
            if (parsed.Ignored && ignoreEntry != null && (cfg.IgnorePatterns ?? new List<Regex>()).Any(p => p.IsMatch(text)))
            {
                var note = cfg.IgnoreNoteFor(token);
                lines.Add("Never drawn (" + ignoreEntry + ")" + (note != null && note.HasReason ? ": " + note.Reason.Trim() : ": no reason is written for it.") +
                          (note != null && note.ExpectsSurface ? " Its points are expected in a surface." : string.Empty));
                return lines;
            }

            var shot = LineworkCoding.Parse(text, cfg);
            foreach (var figure in shot.Figures)
            {
                var parts = new List<string>();
                foreach (var command in figure.Commands)
                    parts.Add(EditableLineworkCommands.Name(KeyOf(command.Kind)).ToLowerInvariant() +
                              (command.Parameters.Count > 0 ? " " + string.Join(" ", command.Parameters.ToArray()) : string.Empty));
                if (figure.Values.Count > 0) parts.Add("value " + string.Join(" ", figure.Values.ToArray()));
                if (figure.Words.Count > 0) parts.Add("words " + string.Join(" ", figure.Words.ToArray()));
                lines.Add("Linework: line " + figure.FigureId + (figure.Number.Length > 0 ? " (code " + figure.Code + ", line " + figure.Number + ")" : string.Empty) +
                          (parts.Count > 0 ? ": " + string.Join(", ", parts.ToArray()) : string.Empty) + ".");
            }
            if (parsed.Ignored)
            {
                if (shot.Figures.Count == 0) lines.Add("Linework: Civil 3D draws the line; the point needs no rule.");
                return lines;
            }

            if (parsed.RuleId == FieldCodeParser.SymbolLabelRuleId)
                lines.Add("Labelled symbol: labelled \"" + parsed.LabelDisplay + "\" on " + (parsed.LabelLayer ?? "(no layer)") + ".");
            else if (parsed.Recognized)
                lines.Add("Rule " + parsed.RuleId + (string.IsNullOrEmpty(parsed.LabelText) ? ": no label." : ": labelled \"" + parsed.LabelDisplay + "\"" +
                          (string.IsNullOrEmpty(parsed.LabelLayer) ? "." : " on " + parsed.LabelLayer + ".")));
            // A control point needs no rule: FTFCONTROL's table is what it gets.
            else if (parsed.NoContent && !control)
                lines.Add("A bare code: nothing is labelled (its point style shows it).");
            else if (parsed.Unhandled && !control)
                lines.Add("No rule reads " + token + " yet: it is listed as unhandled.");

            if (parsed.Modifiers.Count > 0)
                lines.Add("Modifiers: " + string.Join(", ", parsed.Modifiers.Select(m => m.Id + " (" + m.Token + ")").ToArray()) + ".");
            foreach (var d in parsed.Diagnostics.Where(d => d.Severity == Severity.Error))
                lines.Add("Error: " + d.Message);
            return lines;
        }

        private static string KeyOf(LineworkCommandKind kind)
        {
            switch (kind)
            {
                case LineworkCommandKind.Begin: return "begin";
                case LineworkCommandKind.End: return "end";
                case LineworkCommandKind.Close: return "close";
                case LineworkCommandKind.Continue: return "continue";
                case LineworkCommandKind.CurveBegin: return "curveBegin";
                case LineworkCommandKind.CurveEnd: return "curveEnd";
                case LineworkCommandKind.Rectangle: return "rectangle";
                case LineworkCommandKind.RightTurn: return "rightTurn";
                case LineworkCommandKind.Extend: return "extend";
                case LineworkCommandKind.Circle: return "circle";
                case LineworkCommandKind.PointOnCurve: return "pointOnCurve";
                case LineworkCommandKind.Offset: return "offset";
                case LineworkCommandKind.RecallPoint: return "recallPoint";
                default: return "connectPoint";
            }
        }
    }

    public sealed partial class RuleDocument
    {
        private const string IgnoreCodesKey = "ignoreCodes";
        private const string SymbolLabelsKey = "symbolLabels";
        private const string ControlCodesKey = "controlCodes";
        private const string LineworkCodesKey = "lineworkCodes";
        private const string LineworkCommandsKey = "lineworkCommands";
        private const string ModifiersKey = "modifiers";
        private const string CodeListsPage = "Field Code Lists: ";

        private static readonly Regex PlainCode = new Regex(@"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant);

        // ------------------------------------------------------------------ reading

        /// <summary>The six lists as the file has them, in its order; the built-in linework
        /// commands for any the file leaves out.</summary>
        public EditableCodeLists GetCodeLists()
        {
            var result = EditableCodeLists.Defaults();
            result.IgnoreCodes = IgnoreEntries().Select(e => e.Value).ToList();
            result.SymbolLabels = ObjectEntries(SymbolLabelsKey).Select(ReadSymbolLabel).ToList();
            result.ControlCodes = StringEntries(ControlCodesKey);
            result.LineworkCodes = StringEntries(LineworkCodesKey);
            var commands = _root[LineworkCommandsKey] as JObject;
            result.LineworkCommandsInFile = commands != null;
            result.LineworkCommands = ReadLineworkCommands(commands);
            result.Modifiers = ObjectEntries(ModifiersKey).Select(ReadModifier).ToList();
            return result;
        }

        /// <summary>The ignore entries with the token each came from: a code, or an object with
        /// its code (blank strings and anything else are left out, as the rules reader does).</summary>
        private List<KeyValuePair<JToken, EditableIgnoreCode>> IgnoreEntries()
        {
            var list = new List<KeyValuePair<JToken, EditableIgnoreCode>>();
            var token = _root[IgnoreCodesKey];
            var entries = token is JArray ? (IEnumerable<JToken>)token : token != null && token.Type == JTokenType.String ? new[] { token } : new JToken[0];
            foreach (var t in entries)
            {
                if (t.Type == JTokenType.String)
                {
                    var code = ((string)t).Trim();
                    if (code.Length > 0) list.Add(new KeyValuePair<JToken, EditableIgnoreCode>(t, new EditableIgnoreCode { Code = code, Reason = string.Empty, Expect = string.Empty }));
                }
                else if (t.Type == JTokenType.Object)
                {
                    var o = (JObject)t;
                    list.Add(new KeyValuePair<JToken, EditableIgnoreCode>(t, new EditableIgnoreCode
                    {
                        Code = Text(o, "code").Trim(), Reason = Text(o, "reason"), Expect = Text(o, "expect").Trim()
                    }));
                }
            }
            return list;
        }

        private IList<JObject> ObjectEntries(string key)
        {
            var array = _root[key] as JArray;
            return array == null ? new List<JObject>() : array.OfType<JObject>().ToList();
        }

        /// <summary>A list of codes as the file has it, trimmed, blanks left out (a single code
        /// written without its [ ] is read as that code, and reported).</summary>
        private List<string> StringEntries(string key)
        {
            var token = _root[key];
            var entries = token is JArray ? (IEnumerable<JToken>)token : token != null && token.Type == JTokenType.String ? new[] { token } : new JToken[0];
            return entries.Where(t => t.Type == JTokenType.String).Select(t => ((string)t).Trim()).Where(c => c.Length > 0).ToList();
        }

        private static EditableSymbolLabel ReadSymbolLabel(JObject o)
        {
            var enabled = o["enabled"];
            return new EditableSymbolLabel
            {
                Code = Text(o, "code").Trim(),
                Layer = Text(o, "layer").Trim(),
                Label = Text(o, "label"),
                Enabled = enabled == null || enabled.Type != JTokenType.Boolean || (bool)enabled
            };
        }

        private static EditableModifier ReadModifier(JObject o)
        {
            var priority = o["priority"];
            return new EditableModifier
            {
                Id = Text(o, "id").Trim(),
                Pattern = Text(o, "pattern"),
                Priority = priority != null && priority.Type == JTokenType.Integer ? (int)priority : 0,
                LabelFormat = Text(o, "labelFormat"),
                LabelSuffix = Text(o, "labelSuffix"),
                Block = Text(o, "block").Trim(),
                LayerSuffix = Text(o, "layerSuffix"),
                StemCountFrom = Text(o, "stemCountFrom").Trim(),
                Flag = Flag(o["flag"])
            };
        }

        /// <summary>A command the file leaves out (or writes null) has its built-in codes, and a
        /// missing escape is "/", as the rules reader fills them.</summary>
        private static EditableLineworkCommands ReadLineworkCommands(JObject o)
        {
            var result = EditableLineworkCommands.BuiltIn();
            if (o == null) return result;
            foreach (var key in EditableLineworkCommands.Keys)
            {
                var array = o[key] as JArray;
                if (array != null) result.Codes[key] = array.Where(t => t.Type == JTokenType.String).Select(t => ((string)t).Trim()).Where(c => c.Length > 0).ToList();
            }
            var escape = o["escape"];
            if (escape != null && escape.Type == JTokenType.String) result.Escape = ((string)escape).Trim();
            return result;
        }

        private static string Text(JObject o, string name)
        {
            var t = o[name];
            return t != null && t.Type == JTokenType.String ? (string)t : string.Empty;
        }

        // ------------------------------------------------------------------ editing

        /// <summary>Writes each list that differs from the file, keeping the order given; an entry
        /// that was in the file keeps its other properties ("by", "at", comments, anything this
        /// build does not know). A list that did not change, and the rest of the file, are not
        /// touched. The built-in linework commands are written in the first time.</summary>
        public void ApplyCodeLists(EditableCodeLists edit)
        {
            if (edit == null) throw new ArgumentNullException("edit");
            ApplyIgnoreCodes(edit.IgnoreCodes);
            ApplySymbolLabels(edit.SymbolLabels);
            ApplyPlainCodes(ControlCodesKey, edit.ControlCodes);
            ApplyPlainCodes(LineworkCodesKey, edit.LineworkCodes);
            ApplyLineworkCommands(edit.LineworkCommands);
            ApplyModifiers(edit.Modifiers);
        }

        /// <summary>Puts this document's lists onto another (Save re-reads the file first): each
        /// list this document has, and nothing it does not.</summary>
        public void CopyCodeListsTo(RuleDocument other)
        {
            if (other == null) throw new ArgumentNullException("other");
            var mine = GetCodeLists();
            if (_root[IgnoreCodesKey] != null) other.ApplyIgnoreCodes(mine.IgnoreCodes);
            if (_root[SymbolLabelsKey] != null) other.ApplySymbolLabels(mine.SymbolLabels);
            if (_root[ControlCodesKey] != null) other.ApplyPlainCodes(ControlCodesKey, mine.ControlCodes);
            if (_root[LineworkCodesKey] != null) other.ApplyPlainCodes(LineworkCodesKey, mine.LineworkCodes);
            if (_root[LineworkCommandsKey] is JObject) other.ApplyLineworkCommands(mine.LineworkCommands);
            if (_root[ModifiersKey] != null) other.ApplyModifiers(mine.Modifiers);
        }

        private void ApplyPlainCodes(string key, IEnumerable<string> edit)
        {
            var codes = (edit ?? Enumerable.Empty<string>()).Select(c => (c ?? string.Empty).Trim()).Where(c => c.Length > 0).ToList();
            if (codes.SequenceEqual(StringEntries(key), StringComparer.Ordinal)) return;
            _root[key] = new JArray(codes.Cast<object>().ToArray());
        }

        private void ApplyIgnoreCodes(IEnumerable<EditableIgnoreCode> edit)
        {
            var wanted = (edit ?? Enumerable.Empty<EditableIgnoreCode>()).Where(e => e != null).Select(e => new EditableIgnoreCode
            {
                Code = (e.Code ?? string.Empty).Trim(), Reason = (e.Reason ?? string.Empty).Trim(), Expect = (e.Expect ?? string.Empty).Trim()
            }).ToList();
            var current = IgnoreEntries();
            if (wanted.Count == current.Count && wanted.Zip(current, (w, c) => SameIgnore(w, c.Value)).All(same => same)) return;

            var keys = current.Select(c => c.Value.Code).ToList();
            var used = new bool[current.Count];
            var wantedKeys = new HashSet<string>(wanted.Select(w => w.Code), StringComparer.OrdinalIgnoreCase);
            var result = new JArray();
            for (var i = 0; i < wanted.Count; i++)
            {
                var e = wanted[i];
                var at = Counterpart(keys, used, i, e.Code, wantedKeys);
                if (at >= 0 && SameIgnore(e, current[at].Value)) { result.Add(current[at].Key.DeepClone()); continue; }
                var o = at >= 0 ? current[at].Key.DeepClone() as JObject : null;
                if (o == null)
                {
                    if (e.Reason.Length == 0 && e.Expect.Length == 0) { result.Add(new JValue(e.Code)); continue; }
                    o = new JObject { ["code"] = e.Code };
                }
                var was = at >= 0 ? current[at].Value : new EditableIgnoreCode { Code = e.Code, Reason = string.Empty, Expect = string.Empty };
                if (!string.Equals(was.Code, e.Code, StringComparison.Ordinal) || o["code"] == null) o["code"] = e.Code;
                if (!string.Equals(was.Reason.Trim(), e.Reason, StringComparison.Ordinal)) SetOrRemove(o, "reason", e.Reason);
                if (!string.Equals(was.Expect, e.Expect, StringComparison.OrdinalIgnoreCase)) SetOrRemove(o, "expect", e.Expect);
                // Nothing left but the code: written as the code, as the rest of the list is.
                if (o.Properties().All(p => p.Name == "code")) result.Add(new JValue(e.Code));
                else result.Add(o);
            }
            _root[IgnoreCodesKey] = result;
        }

        private static bool SameIgnore(EditableIgnoreCode a, EditableIgnoreCode b)
        {
            return string.Equals((a.Code ?? string.Empty).Trim(), (b.Code ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                   string.Equals((a.Reason ?? string.Empty).Trim(), (b.Reason ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                   string.Equals((a.Expect ?? string.Empty).Trim(), (b.Expect ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private void ApplySymbolLabels(IEnumerable<EditableSymbolLabel> edit)
        {
            var wanted = (edit ?? Enumerable.Empty<EditableSymbolLabel>()).Where(e => e != null).Select(e => new EditableSymbolLabel
            {
                Code = (e.Code ?? string.Empty).Trim(), Layer = (e.Layer ?? string.Empty).Trim(), Label = (e.Label ?? string.Empty).Trim(), Enabled = e.Enabled
            }).ToList();
            var objects = ObjectEntries(SymbolLabelsKey);
            var current = objects.Select(ReadSymbolLabel).ToList();
            if (wanted.Count == current.Count && wanted.Zip(current, SameSymbol).All(same => same)) return;

            var keys = current.Select(c => c.Code).ToList();
            var used = new bool[current.Count];
            var wantedKeys = new HashSet<string>(wanted.Select(w => w.Code), StringComparer.OrdinalIgnoreCase);
            var result = new JArray();
            for (var i = 0; i < wanted.Count; i++)
            {
                var e = wanted[i];
                var at = Counterpart(keys, used, i, e.Code, wantedKeys);
                if (at >= 0 && SameSymbol(e, current[at])) { result.Add(objects[at].DeepClone()); continue; }
                var o = at >= 0 ? (JObject)objects[at].DeepClone() : new JObject();
                var was = at >= 0 ? current[at] : null;
                if (was == null || !string.Equals(was.Code, e.Code, StringComparison.Ordinal)) o["code"] = e.Code;
                if (was == null || !string.Equals(was.Layer, e.Layer, StringComparison.Ordinal)) SetOrRemove(o, "layer", e.Layer);
                if (was == null || !string.Equals(was.Label.Trim(), e.Label, StringComparison.Ordinal)) SetOrRemove(o, "label", e.Label);
                // Absent means on, so "enabled" is only written to turn an entry off.
                if (was == null || was.Enabled != e.Enabled)
                {
                    if (e.Enabled) o.Remove("enabled");
                    else o["enabled"] = false;
                }
                result.Add(o);
            }
            _root[SymbolLabelsKey] = result;
        }

        private static bool SameSymbol(EditableSymbolLabel a, EditableSymbolLabel b)
        {
            return string.Equals((a.Code ?? string.Empty).Trim(), (b.Code ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                   string.Equals((a.Layer ?? string.Empty).Trim(), (b.Layer ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                   string.Equals((a.Label ?? string.Empty).Trim(), (b.Label ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                   a.Enabled == b.Enabled;
        }

        private void ApplyModifiers(IEnumerable<EditableModifier> edit)
        {
            var wanted = (edit ?? Enumerable.Empty<EditableModifier>()).Where(e => e != null).Select(e => new EditableModifier
            {
                Id = (e.Id ?? string.Empty).Trim(), Pattern = e.Pattern ?? string.Empty, Priority = e.Priority,
                LabelFormat = e.LabelFormat ?? string.Empty, LabelSuffix = e.LabelSuffix ?? string.Empty,
                Block = (e.Block ?? string.Empty).Trim(), LayerSuffix = e.LayerSuffix ?? string.Empty,
                StemCountFrom = (e.StemCountFrom ?? string.Empty).Trim(), Flag = e.Flag
            }).ToList();
            var objects = ObjectEntries(ModifiersKey);
            var current = objects.Select(ReadModifier).ToList();
            if (wanted.Count == current.Count && wanted.Zip(current, SameModifier).All(same => same)) return;

            var keys = current.Select(c => c.Id).ToList();
            var used = new bool[current.Count];
            var wantedKeys = new HashSet<string>(wanted.Select(w => w.Id), StringComparer.OrdinalIgnoreCase);
            var result = new JArray();
            for (var i = 0; i < wanted.Count; i++)
            {
                var e = wanted[i];
                var at = Counterpart(keys, used, i, e.Id, wantedKeys);
                if (at >= 0 && SameModifier(e, current[at])) { result.Add(objects[at].DeepClone()); continue; }
                var o = at >= 0 ? (JObject)objects[at].DeepClone() : new JObject();
                var was = at >= 0 ? current[at] : null;
                if (was == null || !string.Equals(was.Id, e.Id, StringComparison.Ordinal)) o["id"] = e.Id;
                if (was == null || !string.Equals(was.Pattern, e.Pattern, StringComparison.Ordinal)) SetOrRemove(o, "pattern", e.Pattern);
                if (was == null || was.Priority != e.Priority) o["priority"] = e.Priority;
                // Label wording is kept exactly as typed: " (DEAD)" starts with its space.
                if (was == null || !string.Equals(was.LabelFormat, e.LabelFormat, StringComparison.Ordinal)) SetOrRemoveExact(o, "labelFormat", e.LabelFormat);
                if (was == null || !string.Equals(was.LabelSuffix, e.LabelSuffix, StringComparison.Ordinal)) SetOrRemoveExact(o, "labelSuffix", e.LabelSuffix);
                if (was == null || !string.Equals(was.Block, e.Block, StringComparison.Ordinal)) SetOrRemove(o, "block", e.Block);
                if (was == null || !string.Equals(was.LayerSuffix, e.LayerSuffix, StringComparison.Ordinal)) SetOrRemoveExact(o, "layerSuffix", e.LayerSuffix);
                if (was == null || !string.Equals(was.StemCountFrom, e.StemCountFrom, StringComparison.Ordinal)) SetOrRemove(o, "stemCountFrom", e.StemCountFrom);
                if (was == null || was.Flag != e.Flag)
                {
                    if (e.Flag) o["flag"] = true;
                    else o.Remove("flag");
                }
                result.Add(o);
            }
            _root[ModifiersKey] = result;
        }

        private static bool SameModifier(EditableModifier a, EditableModifier b)
        {
            return string.Equals((a.Id ?? string.Empty).Trim(), (b.Id ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                   string.Equals(a.Pattern ?? string.Empty, b.Pattern ?? string.Empty, StringComparison.Ordinal) &&
                   a.Priority == b.Priority &&
                   string.Equals(a.LabelFormat ?? string.Empty, b.LabelFormat ?? string.Empty, StringComparison.Ordinal) &&
                   string.Equals(a.LabelSuffix ?? string.Empty, b.LabelSuffix ?? string.Empty, StringComparison.Ordinal) &&
                   string.Equals((a.Block ?? string.Empty).Trim(), (b.Block ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                   string.Equals(a.LayerSuffix ?? string.Empty, b.LayerSuffix ?? string.Empty, StringComparison.Ordinal) &&
                   string.Equals((a.StemCountFrom ?? string.Empty).Trim(), (b.StemCountFrom ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                   a.Flag == b.Flag;
        }

        /// <summary>Blank removes it; anything else is written exactly, its spaces kept.</summary>
        private static void SetOrRemoveExact(JObject owner, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) owner.Remove(name);
            else owner[name] = value;
        }

        /// <summary>Writes the commands that differ, or the whole set the first time (the file had
        /// none, so the built-in set was shown).</summary>
        private void ApplyLineworkCommands(EditableLineworkCommands edit)
        {
            if (edit == null) return;
            var o = _root[LineworkCommandsKey] as JObject;
            var current = ReadLineworkCommands(o);
            var escape = (edit.Escape ?? string.Empty).Trim();
            if (o == null)
            {
                o = new JObject();
                foreach (var key in EditableLineworkCommands.Keys)
                    o[key] = new JArray(CleanTokens(edit.For(key)).Cast<object>().ToArray());
                o["escape"] = escape;
                _root[LineworkCommandsKey] = o;
                return;
            }
            foreach (var key in EditableLineworkCommands.Keys)
            {
                var tokens = CleanTokens(edit.For(key));
                if (!tokens.SequenceEqual(current.For(key), StringComparer.Ordinal))
                    o[key] = new JArray(tokens.Cast<object>().ToArray());
            }
            if (!string.Equals(current.Escape ?? string.Empty, escape, StringComparison.Ordinal)) o["escape"] = escape;
        }

        private static List<string> CleanTokens(IEnumerable<string> tokens)
        {
            return (tokens ?? Enumerable.Empty<string>()).Select(t => (t ?? string.Empty).Trim()).Where(t => t.Length > 0).ToList();
        }

        /// <summary>The entry of the file an edited entry stands for: the first not yet taken with
        /// the same code, or else the one in the same place when its code is no longer listed (a
        /// code typed over keeps its entry's other properties). -1 for a new entry.</summary>
        private static int Counterpart(IList<string> keys, bool[] used, int position, string key, ICollection<string> wantedKeys)
        {
            for (var j = 0; j < keys.Count; j++)
                if (!used[j] && string.Equals(keys[j], key, StringComparison.OrdinalIgnoreCase)) { used[j] = true; return j; }
            if (position < keys.Count && !used[position] && !wantedKeys.Contains(keys[position])) { used[position] = true; return position; }
            return -1;
        }

        // ------------------------------------------------------------------ checks

        private void CodeListProblems(List<string> problems)
        {
            IgnoreCodeProblems(problems);
            SymbolLabelProblems(problems);
            PlainCodeProblems(ControlCodesKey, EditableCodeLists.ControlTab, false, problems);
            PlainCodeProblems(LineworkCodesKey, EditableCodeLists.LineworkTab, true, problems);
            LineworkCommandProblems(problems);
            ModifierProblems(problems);
        }

        private static string At(string tab, int row) { return CodeListsPage + tab + ", row " + row.ToString(CultureInfo.InvariantCulture) + ": "; }

        private static string In(string tab) { return CodeListsPage + tab + ": "; }


        private static bool NotFlag(JToken t) { return t != null && t.Type != JTokenType.Null && t.Type != JTokenType.Boolean; }

        private static void Twice(string tab, IEnumerable<string> codes, List<string> problems)
        {
            var twice = codes.Where(c => !string.IsNullOrWhiteSpace(c)).GroupBy(c => c.Trim(), StringComparer.OrdinalIgnoreCase)
                             .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (twice.Count > 0) problems.Add(In(tab) + "listed twice: " + string.Join(", ", twice.ToArray()) + ".");
        }

        private void IgnoreCodeProblems(List<string> problems)
        {
            const string tab = EditableCodeLists.NeverDrawnTab;
            var token = _root[IgnoreCodesKey];
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.String) return;
            var array = token as JArray;
            if (array == null) { problems.Add(In(tab) + "ignoreCodes must be a list of codes."); return; }
            var codes = new List<string>();
            var row = 0;
            foreach (var t in array)
            {
                row++;
                string code;
                if (t.Type == JTokenType.Null) continue;
                if (t.Type == JTokenType.String)
                {
                    code = ((string)t).Trim();
                    if (code.Length == 0) continue;
                }
                else if (t.Type == JTokenType.Object)
                {
                    var o = (JObject)t;
                    if (NotText(o["code"])) { problems.Add(At(tab, row) + "the code must be text."); continue; }
                    code = Text(o, "code").Trim();
                    var name = code.Length > 0 ? In(tab) + code + ": " : At(tab, row);
                    if (code.Length == 0) problems.Add(At(tab, row) + "the code is blank.");
                    foreach (var key in new[] { "reason", "expect", "by" })
                        if (NotText(o[key])) problems.Add(name + "\"" + key + "\" must be text.");
                    var expect = Text(o, "expect").Trim();
                    if (expect.Length > 0 && !EditableIgnoreCode.ExpectValues.Contains(expect, StringComparer.OrdinalIgnoreCase))
                        problems.Add(name + "what holds its points must be blank or surface, not \"" + expect + "\".");
                    var when = o["at"];
                    DateTime ignored;
                    if (when != null && when.Type != JTokenType.Null && when.Type != JTokenType.Date &&
                        !(when.Type == JTokenType.String && DateTime.TryParse((string)when, CultureInfo.InvariantCulture, DateTimeStyles.None, out ignored)))
                        problems.Add(name + "\"at\" must be a date.");
                    if (code.Length == 0) continue;
                }
                else
                {
                    problems.Add(At(tab, row) + "each entry must be a code or { \"code\", \"reason\", \"expect\" }.");
                    continue;
                }
                if (!PlainCode.IsMatch(code))
                    problems.Add(In(tab) + "\"" + code + "\" is not a plain field code (letters, digits, - or _ only).");
                codes.Add(code);
            }
            Twice(tab, codes, problems);
        }

        private void SymbolLabelProblems(List<string> problems)
        {
            const string tab = EditableCodeLists.SymbolLabelsTab;
            var token = _root[SymbolLabelsKey];
            if (token == null || token.Type == JTokenType.Null) return;
            var array = token as JArray;
            if (array == null) { problems.Add(In(tab) + "symbolLabels must be a list of { \"code\", \"layer\" } entries."); return; }
            var codes = new List<string>();
            var row = 0;
            foreach (var t in array)
            {
                row++;
                if (t.Type == JTokenType.Null) continue;
                var o = t as JObject;
                if (o == null) { problems.Add(At(tab, row) + "each entry must be { \"code\", \"layer\", \"label\" }."); continue; }
                var bad = new[] { "code", "layer", "label" }.Where(k => NotText(o[k])).ToList();
                foreach (var key in bad) problems.Add(At(tab, row) + "\"" + key + "\" must be text.");
                if (NotFlag(o["enabled"])) problems.Add(At(tab, row) + "\"enabled\" (On) must be true or false.");
                if (bad.Count > 0) continue;

                var s = ReadSymbolLabel(o);
                if (s.Code.Length == 0) { problems.Add(At(tab, row) + "the code is blank."); continue; }
                if (s.Code.IndexOfAny(new[] { ' ', '\t' }) >= 0) problems.Add(In(tab) + "\"" + s.Code + "\" is not one code: a code has no spaces.");
                codes.Add(s.Code);
                string why;
                if (s.Layer.Length == 0) problems.Add(In(tab) + s.Code + " has no text layer.");
                else if (!FieldCodes.Settings.LayerMap.IsValidName(s.Layer, out why))
                    problems.Add(In(tab) + s.Code + ": the layer \"" + s.Layer + "\" cannot be used: " + why + ".");
            }
            Twice(tab, codes, problems);
        }

        private void PlainCodeProblems(string key, string tab, bool plainOnly, List<string> problems)
        {
            var token = _root[key];
            if (token == null || token.Type == JTokenType.Null) return;
            var array = token as JArray;
            if (array == null) { problems.Add(In(tab) + key + " must be a list of codes."); return; }
            var codes = new List<string>();
            var row = 0;
            foreach (var t in array)
            {
                row++;
                if (t.Type == JTokenType.Null) continue;
                if (t.Type != JTokenType.String) { problems.Add(At(tab, row) + "each entry must be a code (text)."); continue; }
                var code = ((string)t).Trim();
                if (code.Length == 0) continue;
                if (plainOnly && !PlainCode.IsMatch(code))
                    problems.Add(In(tab) + "\"" + code + "\" is not a plain field code (letters, digits, - or _ only).");
                else if (!plainOnly && code.IndexOfAny(new[] { ' ', '\t' }) >= 0)
                    problems.Add(In(tab) + "\"" + code + "\" is not one code: a code has no spaces.");
                codes.Add(code);
            }
            Twice(tab, codes, problems);
        }

        private void LineworkCommandProblems(List<string> problems)
        {
            const string tab = EditableCodeLists.LineCommandsTab;
            var token = _root[LineworkCommandsKey];
            if (token == null || token.Type == JTokenType.Null) return;
            var o = token as JObject;
            if (o == null) { problems.Add(In(tab) + "lineworkCommands must be { \"begin\": [\"B\"], \"end\": [\"E\"], ... }."); return; }
            var typesOk = true;
            foreach (var key in EditableLineworkCommands.Keys)
            {
                var v = o[key];
                if (v == null || v.Type == JTokenType.Null) continue;
                var name = EditableLineworkCommands.Name(key);
                var array = v as JArray;
                if (array == null) { problems.Add(In(tab) + "the codes for " + name + " must be a list."); typesOk = false; continue; }
                if (array.Any(t => t.Type != JTokenType.String && t.Type != JTokenType.Null)) { problems.Add(In(tab) + name + ": each code must be text."); typesOk = false; continue; }
                foreach (var code in array.Where(t => t.Type == JTokenType.String).Select(t => ((string)t).Trim()).Where(c => c.IndexOfAny(new[] { ' ', '\t' }) >= 0))
                    problems.Add(In(tab) + name + ": \"" + code + "\" is not one code: a code has no spaces.");
            }
            var escape = o["escape"];
            if (NotText(escape)) { problems.Add(In(tab) + "the escape character must be text."); typesOk = false; }
            else if (escape != null && escape.Type == JTokenType.String)
            {
                var e = ((string)escape).Trim();
                if (e.Any(char.IsLetterOrDigit) || e.Any(char.IsWhiteSpace))
                    problems.Add(In(tab) + "the escape \"" + e + "\" must be a symbol such as /, not a letter, digit or space: every word starting with it would be read as plain text.");
            }
            if (!typesOk) return;

            // What the rules reader will have: the file's codes, the built-in ones for any command
            // it leaves out. A code may do one thing only.
            var commands = ReadLineworkCommands(o);
            var owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in EditableLineworkCommands.Keys)
                foreach (var code in commands.For(key).Select(c => c.Trim().ToUpperInvariant()).Distinct())
                {
                    List<string> names;
                    if (!owners.TryGetValue(code, out names)) { names = new List<string>(); owners[code] = names; }
                    names.Add(EditableLineworkCommands.Name(key) + (o[key] == null || o[key].Type == JTokenType.Null ? " (built in)" : string.Empty));
                }
            foreach (var clash in owners.Where(p => p.Value.Count > 1))
                problems.Add(In(tab) + clash.Key + " is listed for " + string.Join(" and ", clash.Value.ToArray()) + "; a code can do one thing only.");
        }

        private void ModifierProblems(List<string> problems)
        {
            const string tab = EditableCodeLists.ModifiersTab;
            var token = _root[ModifiersKey];
            if (token == null || token.Type == JTokenType.Null) return;
            var array = token as JArray;
            if (array == null) { problems.Add(In(tab) + "modifiers must be a list of { \"id\", \"pattern\" } entries."); return; }
            var ids = new List<string>();
            var replacers = new List<EditableModifier>();
            var row = 0;
            foreach (var t in array)
            {
                row++;
                if (t.Type == JTokenType.Null) continue;
                var o = t as JObject;
                if (o == null) { problems.Add(At(tab, row) + "each entry must be { \"id\", \"pattern\", ... }."); continue; }
                var bad = new[] { "id", "pattern", "labelFormat", "labelSuffix", "block", "layerSuffix", "stemCountFrom" }.Where(k => NotText(o[k])).ToList();
                foreach (var key in bad) problems.Add(At(tab, row) + "\"" + key + "\" must be text.");
                var priority = o["priority"];
                if (priority != null && priority.Type != JTokenType.Null && priority.Type != JTokenType.Integer)
                    problems.Add(At(tab, row) + "the order (priority) must be a whole number.");
                if (NotFlag(o["flag"])) problems.Add(At(tab, row) + "\"flag\" (Flag for review) must be true or false.");
                if (bad.Count > 0) continue;

                var m = ReadModifier(o);
                var name = m.Id.Length > 0 ? In(tab) + m.Id + ": " : At(tab, row);
                if (m.Id.Length == 0) problems.Add(At(tab, row) + "the name (id) is blank.");
                else ids.Add(m.Id);
                if (m.LabelFormat.Length > 0) replacers.Add(m);

                if (string.IsNullOrWhiteSpace(m.Pattern)) problems.Add(name + "the pattern is blank.");
                else
                {
                    Regex regex = null;
                    try { regex = new Regex(m.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
                    catch (ArgumentException ex) { problems.Add(name + "the pattern \"" + m.Pattern + "\" is not a valid pattern: " + ex.Message); }
                    if (regex != null && m.StemCountFrom.Length > 0 &&
                        !regex.GetGroupNames().Contains(m.StemCountFrom, StringComparer.Ordinal))
                        problems.Add(name + "the stem count is read from \"" + m.StemCountFrom + "\", which is not a named part of the pattern (write it as (?<" +
                                     m.StemCountFrom + ">[0-9]+)).");
                }
                string why;
                if (m.LayerSuffix.Length > 0 && !FieldCodes.Settings.LayerMap.IsValidName("V" + m.LayerSuffix, out why))
                    problems.Add(name + "the layer ending \"" + m.LayerSuffix + "\" cannot be used: " + why + ".");
            }
            Twice(tab, ids, problems);
            foreach (var g in replacers.GroupBy(m => m.Priority).Where(g => g.Count() > 1))
                problems.Add(In(tab) + string.Join(" and ", g.Select(m => m.Id.Length > 0 ? m.Id : "(no name)").ToArray()) +
                             " both replace the whole label at order " + g.Key.ToString(CultureInfo.InvariantCulture) +
                             "; give them different orders so the result does not depend on which is read first.");
        }
    }
}
