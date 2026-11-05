using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Editing
{
    /// <summary>
    /// One rule of the orientation section's "lines" (Settings > Symbols and Rotations > Road lines
    /// and lists): survey linework on these layers, or with these figure codes, is this kind of road or
    /// utility line. A figure's code is looked at before its layer; the first rule that matches wins.
    /// </summary>
    public sealed class EditableLineRole
    {
        public EditableLineRole()
        {
            Role = FieldCodes.Orient.LineRole.Other.ToString();
            Layers = new List<string>();
            Codes = new List<string>();
            SourceIndex = -1;
        }

        /// <summary>Where the entry sat in the file's list when it was read (-1: a new row, or a
        /// built-in PMX row not yet in the file). Writing the list back starts from that entry, so its
        /// comments and any property this build does not know survive.</summary>
        public int SourceIndex { get; set; }

        /// <summary>A <see cref="FieldCodes.Orient.LineRole"/> name.</summary>
        public string Role { get; set; }

        /// <summary>Layer names or patterns (* and ? allowed).</summary>
        public List<string> Layers { get; set; }

        /// <summary>Figure names or codes (trailing figure digits ignored: BAR1 is BAR; * and ? allowed).</summary>
        public List<string> Codes { get; set; }

        public string LayersText { get { return string.Join(", ", (Layers ?? new List<string>()).ToArray()); } }

        public string CodesText { get { return string.Join(" ", (Codes ?? new List<string>()).ToArray()); } }

        /// <summary>Layers typed in one box, separated by commas, semicolons or new lines (a layer
        /// name may have spaces in it). Each once.</summary>
        public static List<string> ParseLayers(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Codes typed in one box, separated by spaces, commas, semicolons or new lines;
        /// upper case, each once.</summary>
        public static List<string> ParseCodes(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim().ToUpperInvariant())
                .Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static EditableLineRole From(FieldCodes.Orient.LineRoleRule rule, int sourceIndex)
        {
            return new EditableLineRole
            {
                SourceIndex = sourceIndex,
                Role = rule.Role.ToString(),
                Layers = (rule.Layers ?? new List<string>()).ToList(),
                Codes = (rule.Codes ?? new List<string>()).ToList()
            };
        }

        public EditableLineRole Clone()
        {
            var copy = (EditableLineRole)MemberwiseClone();
            copy.Layers = (Layers ?? new List<string>()).ToList();
            copy.Codes = (Codes ?? new List<string>()).ToList();
            return copy;
        }

        internal bool SameAs(EditableLineRole other)
        {
            return other != null &&
                   string.Equals((Role ?? string.Empty).Trim(), (other.Role ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                   SameList(Layers, other.Layers) && SameList(Codes, other.Codes);
        }

        internal static bool SameList(IList<string> a, IList<string> b)
        {
            var x = (a ?? new List<string>()).Select(s => (s ?? string.Empty).Trim()).ToList();
            var y = (b ?? new List<string>()).Select(s => (s ?? string.Empty).Trim()).ToList();
            return x.SequenceEqual(y, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The rest of the orientation section: which linework is which road or utility line, the codes
    /// and words that make a sign a stop or yield sign, the codes of a luminaire shot, how near is
    /// near for each relationship FTFORIENT looks for, and the QC layers and sizes. A list the file
    /// names replaces PMX's whole list (an empty one, or none, is PMX's).
    /// </summary>
    public sealed class EditableOrientationDetails
    {
        public EditableOrientationDetails()
        {
            Lines = new List<EditableLineRole>();
            StopSignCodes = new List<string>();
            StopSignWords = new List<string>();
            LuminaireCodes = new List<string>();
        }

        /// <summary>lines: in force, in order (PMX's when the file has none). Null when written: leave the file's alone.</summary>
        public List<EditableLineRole> Lines { get; set; }

        /// <summary>Read only: the file has no lines list of its own, so PMX's is in force.</summary>
        public bool LinesBuiltIn { get; set; }

        /// <summary>stopSignCodes: sign codes that control an approach (stop, yield). Empty: PMX's list.</summary>
        public List<string> StopSignCodes { get; set; }
        public bool StopSignCodesBuiltIn { get; set; }

        /// <summary>stopSignWords: words in a sign's description or note that make it a stop or yield sign.</summary>
        public List<string> StopSignWords { get; set; }
        public bool StopSignWordsBuiltIn { get; set; }

        /// <summary>luminaireCodes: codes of a shot at a light's head (the end of its arm).</summary>
        public List<string> LuminaireCodes { get; set; }
        public bool LuminaireCodesBuiltIn { get; set; }

        public double IntersectionSearchFeet { get; set; }
        public double StopBarSearchFeet { get; set; }
        public double SamePostFeet { get; set; }
        public double ValveClusterFeet { get; set; }
        public double HydrantSearchFeet { get; set; }
        public double MainSnapFeet { get; set; }
        public double PanelSnapFeet { get; set; }
        public double LuminaireSearchFeet { get; set; }

        /// <summary>qcLayer: the non-plotting layer FTFORIENT draws its direction arrows and notes on.</summary>
        public string QcLayer { get; set; }

        /// <summary>qcInferredLayer: the non-plotting layer for inferred underground connections.</summary>
        public string QcInferredLayer { get; set; }

        /// <summary>qcArrowPlotted: a QC arrow's length, inches on the sheet.</summary>
        public double QcArrowPlotted { get; set; }

        /// <summary>qcTextPlotted: QC text height, inches on the sheet.</summary>
        public double QcTextPlotted { get; set; }

        /// <summary>The roles a line rule can give, in the order the editor offers them.</summary>
        public static readonly string[] Roles =
        {
            "Centerline", "OneWay", "LaneLine", "Edge", "RoadContext", "StopBar", "Crosswalk", "SignPanel", "WaterMain", "GasMain", "Other"
        };

        /// <summary>What a rules file with no orientation section gets.</summary>
        public static EditableOrientationDetails BuiltIn()
        {
            var d = new FieldCodes.Orient.OrientationStandard();
            var pmx = FieldCodes.Orient.OrientationStandard.Pmx();
            return new EditableOrientationDetails
            {
                Lines = pmx.Lines.Select(r => EditableLineRole.From(r, -1)).ToList(),
                LinesBuiltIn = true,
                StopSignCodes = pmx.StopSignCodes.ToList(),
                StopSignCodesBuiltIn = true,
                StopSignWords = pmx.StopSignWords.ToList(),
                StopSignWordsBuiltIn = true,
                LuminaireCodes = pmx.LuminaireCodes.ToList(),
                LuminaireCodesBuiltIn = true,
                IntersectionSearchFeet = d.IntersectionSearchFeet,
                StopBarSearchFeet = d.StopBarSearchFeet,
                SamePostFeet = d.SamePostFeet,
                ValveClusterFeet = d.ValveClusterFeet,
                HydrantSearchFeet = d.HydrantSearchFeet,
                MainSnapFeet = d.MainSnapFeet,
                PanelSnapFeet = d.PanelSnapFeet,
                LuminaireSearchFeet = d.LuminaireSearchFeet,
                QcLayer = d.QcLayer,
                QcInferredLayer = d.QcInferredLayer,
                QcArrowPlotted = d.QcArrowPlotted,
                QcTextPlotted = d.QcTextPlotted
            };
        }

        /// <summary>A list typed one entry per line: trimmed, upper case, blank lines dropped. A
        /// repeated entry is kept, so the check can name it.</summary>
        public static List<string> ParseList(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length > 0)
                .ToList();
        }
    }

    /// <summary>One of the orientation section's distances and the values the editor and the check allow.</summary>
    public sealed class OrientationDistance
    {
        public OrientationDistance(string key, double minimum, double maximum, string what)
        {
            Key = key; Minimum = minimum; Maximum = maximum; What = what;
        }

        /// <summary>The rules file's name for it (intersectionSearchFeet).</summary>
        public string Key { get; private set; }
        public double Minimum { get; private set; }
        public double Maximum { get; private set; }

        /// <summary>What it is, in words, for the check's message.</summary>
        public string What { get; private set; }
    }

    public sealed partial class RuleDocument
    {
        /// <summary>Written above a lines list the editor copies in from the built-in standard.</summary>
        public const string LinesComment =
            "Which survey linework is which road or utility line, edited in Settings > Symbols and Rotations > Road lines and lists " +
            "(copied from the built-in PMX list on the first edit). Each rule: role (Centerline, OneWay, LaneLine, Edge, RoadContext, " +
            "StopBar, Crosswalk, SignPanel, WaterMain, GasMain, Other), layers (* and ? allowed) and codes (figure codes, digits ignored). " +
            "A figure's code is looked at before its layer; the first rule that matches wins. Remove this list to go back to the built-in PMX list.";

        /// <summary>The distances of the orientation section, with the range each may have.</summary>
        public static readonly IList<OrientationDistance> OrientationDistances = new List<OrientationDistance>
        {
            new OrientationDistance("intersectionSearchFeet", 10, 500, "the distance from an intersection at which a sign, signal pole or stop bar is decided by its approach"),
            new OrientationDistance("stopBarSearchFeet", 5, 300, "the distance a stop or yield sign may be from its stop bar"),
            new OrientationDistance("samePostFeet", 0.1, 10, "the distance within which signs are on one post"),
            new OrientationDistance("panelSnapFeet", 0.1, 20, "the distance a sign may be from its shot sign panel"),
            new OrientationDistance("luminaireSearchFeet", 2, 200, "the distance a luminaire shot may be from its light pole"),
            new OrientationDistance("mainSnapFeet", 0.5, 50, "the distance a valve may be from its water or gas line"),
            new OrientationDistance("valveClusterFeet", 1, 200, "the distance within which valves are one cluster"),
            new OrientationDistance("hydrantSearchFeet", 5, 300, "the distance a hydrant may be from a valve cluster it is fed by")
        }.AsReadOnly();

        /// <summary>The range of a QC arrow's plotted length, inches.</summary>
        public const double QcArrowMinimum = 0.05, QcArrowMaximum = 2.0;

        /// <summary>The orientation section's lines, lists, distances and QC layers, with PMX's or the
        /// built-in value for anything it leaves out.</summary>
        public EditableOrientationDetails GetOrientationDetails()
        {
            var result = EditableOrientationDetails.BuiltIn();
            var o = Orientation(false);
            if (o == null) return result;

            var lines = o["lines"] as JArray;
            if (lines != null && lines.Count > 0)
            {
                result.LinesBuiltIn = false;
                result.Lines = new List<EditableLineRole>();
                for (var i = 0; i < lines.Count; i++)
                {
                    var entry = lines[i] as JObject;
                    if (entry == null) continue;
                    var role = entry["role"];
                    result.Lines.Add(new EditableLineRole
                    {
                        SourceIndex = i,
                        Role = role != null && role.Type != JTokenType.Null ? role.ToString().Trim() : string.Empty,
                        Layers = ReadCodes(entry["layers"]),
                        Codes = ReadCodes(entry["codes"])
                    });
                }
            }

            List<string> list;
            if (FileList(o, "stopSignCodes", out list)) { result.StopSignCodes = Clean(list); result.StopSignCodesBuiltIn = false; }
            if (FileList(o, "stopSignWords", out list)) { result.StopSignWords = Clean(list); result.StopSignWordsBuiltIn = false; }
            if (FileList(o, "luminaireCodes", out list)) { result.LuminaireCodes = Clean(list); result.LuminaireCodesBuiltIn = false; }

            result.IntersectionSearchFeet = Number(o["intersectionSearchFeet"], result.IntersectionSearchFeet);
            result.StopBarSearchFeet = Number(o["stopBarSearchFeet"], result.StopBarSearchFeet);
            result.SamePostFeet = Number(o["samePostFeet"], result.SamePostFeet);
            result.ValveClusterFeet = Number(o["valveClusterFeet"], result.ValveClusterFeet);
            result.HydrantSearchFeet = Number(o["hydrantSearchFeet"], result.HydrantSearchFeet);
            result.MainSnapFeet = Number(o["mainSnapFeet"], result.MainSnapFeet);
            result.PanelSnapFeet = Number(o["panelSnapFeet"], result.PanelSnapFeet);
            result.LuminaireSearchFeet = Number(o["luminaireSearchFeet"], result.LuminaireSearchFeet);
            result.QcLayer = Text(o, "qcLayer", result.QcLayer);
            result.QcInferredLayer = Text(o, "qcInferredLayer", result.QcInferredLayer);
            result.QcArrowPlotted = Number(o["qcArrowPlotted"], result.QcArrowPlotted);
            result.QcTextPlotted = Number(o["qcTextPlotted"], result.QcTextPlotted);
            return result;
        }

        /// <summary>
        /// Writes what differs from the file; a value that did not change is not rewritten, and nothing
        /// else in the file is touched. A list the same as PMX's, or empty, is taken out of the file so
        /// PMX's list applies (the same behaviour, and later corrections to it still arrive). Lines null:
        /// the file's lines are left alone. Each written line row's SourceIndex becomes its new place.
        /// </summary>
        public void ApplyOrientationDetails(EditableOrientationDetails edit)
        {
            if (edit == null) throw new ArgumentNullException("edit");
            var current = GetOrientationDetails();
            var o = Orientation(false);

            if (edit.Lines != null) WriteLines(edit.Lines, current, o);
            WriteList("stopSignCodes", edit.StopSignCodes, FieldCodes.Orient.OrientationStandard.Pmx().StopSignCodes);
            WriteList("stopSignWords", edit.StopSignWords, FieldCodes.Orient.OrientationStandard.Pmx().StopSignWords);
            WriteList("luminaireCodes", edit.LuminaireCodes, FieldCodes.Orient.OrientationStandard.Pmx().LuminaireCodes);

            foreach (var n in new[]
            {
                Tuple.Create("intersectionSearchFeet", current.IntersectionSearchFeet, edit.IntersectionSearchFeet),
                Tuple.Create("stopBarSearchFeet", current.StopBarSearchFeet, edit.StopBarSearchFeet),
                Tuple.Create("samePostFeet", current.SamePostFeet, edit.SamePostFeet),
                Tuple.Create("valveClusterFeet", current.ValveClusterFeet, edit.ValveClusterFeet),
                Tuple.Create("hydrantSearchFeet", current.HydrantSearchFeet, edit.HydrantSearchFeet),
                Tuple.Create("mainSnapFeet", current.MainSnapFeet, edit.MainSnapFeet),
                Tuple.Create("panelSnapFeet", current.PanelSnapFeet, edit.PanelSnapFeet),
                Tuple.Create("luminaireSearchFeet", current.LuminaireSearchFeet, edit.LuminaireSearchFeet),
                Tuple.Create("qcArrowPlotted", current.QcArrowPlotted, edit.QcArrowPlotted),
                Tuple.Create("qcTextPlotted", current.QcTextPlotted, edit.QcTextPlotted)
            })
                if (Math.Abs(n.Item2 - n.Item3) > 1e-12) Orientation(true)[n.Item1] = n.Item3;

            foreach (var t in new[] { Tuple.Create("qcLayer", current.QcLayer, edit.QcLayer), Tuple.Create("qcInferredLayer", current.QcInferredLayer, edit.QcInferredLayer) })
            {
                var value = (t.Item3 ?? string.Empty).Trim();
                if (!string.Equals(t.Item2 ?? string.Empty, value, StringComparison.Ordinal)) Orientation(true)[t.Item1] = value;
            }
        }

        /// <summary>Removes the file's lines list (and the editor's note above it): PMX's is in force again.</summary>
        public void UseBuiltInLines()
        {
            var o = Orientation(false);
            if (o == null) return;
            o.Remove("lines");
            if (string.Equals((string)o["//lines"], LinesComment, StringComparison.Ordinal)) o.Remove("//lines");
        }

        /// <summary>Puts this document's lines, lists, distances and QC layers onto <paramref name="target"/>
        /// (Save re-reads the file first). The lines list is copied whole, its entries' comments with it.</summary>
        public void CopyOrientationDetailsTo(RuleDocument target)
        {
            if (target == null) throw new ArgumentNullException("target");
            var mine = GetOrientationDetails();
            mine.Lines = null;
            target.ApplyOrientationDetails(mine);

            var o = Orientation(false);
            var lines = o != null ? o["lines"] as JArray : null;
            if (lines == null || lines.Count == 0) { target.UseBuiltInLines(); return; }
            var theirs = target.Orientation(true);
            if (theirs["lines"] != null && JToken.DeepEquals(theirs["lines"], lines)) return;
            if (o["//lines"] != null && theirs["//lines"] == null && theirs["lines"] == null) theirs["//lines"] = o["//lines"].DeepClone();
            theirs["lines"] = lines.DeepClone();
        }

        // ------------------------------------------------------------------ writing

        private void WriteLines(List<EditableLineRole> rows, EditableOrientationDetails current, JObject o)
        {
            var clean = rows.Where(r => r != null).ToList();
            var same = clean.Count == current.Lines.Count && clean.Zip(current.Lines, (a, b) => a.SameAs(b)).All(x => x);
            // The file's own list as written must say the same too: an entry that is not a rule, or a
            // layer given as text rather than a list, is put right by the first edit.
            if (same && !current.LinesBuiltIn) same = WrittenAs(o["lines"] as JArray, clean);
            if (same)
            {
                for (var i = 0; i < clean.Count; i++) clean[i].SourceIndex = current.Lines[i].SourceIndex;
                return;
            }

            var pmx = FieldCodes.Orient.OrientationStandard.Pmx().Lines.Select(r => EditableLineRole.From(r, -1)).ToList();
            var isPmx = clean.Count == pmx.Count && clean.Zip(pmx, (a, b) => a.SameAs(b)).All(x => x);
            if (clean.Count == 0 || isPmx)
            {
                UseBuiltInLines();
                foreach (var r in clean) r.SourceIndex = -1;
                return;
            }

            var old = o != null ? o["lines"] as JArray ?? new JArray() : new JArray();
            var list = new JArray();
            foreach (var r in clean)
            {
                var source = r.SourceIndex >= 0 && r.SourceIndex < old.Count ? old[r.SourceIndex] as JObject : null;
                var entry = source != null ? (JObject)source.DeepClone() : new JObject();
                entry["role"] = (r.Role ?? string.Empty).Trim();
                entry["layers"] = new JArray((r.Layers ?? new List<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Cast<object>().ToArray());
                entry["codes"] = new JArray((r.Codes ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Cast<object>().ToArray());
                list.Add(entry);
            }
            var orientation = Orientation(true);
            if (orientation["//lines"] == null && orientation["lines"] == null) orientation["//lines"] = LinesComment;
            orientation["lines"] = list;
            for (var i = 0; i < clean.Count; i++) clean[i].SourceIndex = i;
        }

        /// <summary>True when every entry of the file's lines list is a rule saying exactly what its row says.</summary>
        private static bool WrittenAs(JArray file, IList<EditableLineRole> rows)
        {
            if (file == null || file.Count != rows.Count) return false;
            for (var i = 0; i < rows.Count; i++)
            {
                var entry = file[i] as JObject;
                if (entry == null || entry["role"] == null || entry["role"].Type != JTokenType.String) return false;
                if (!string.Equals(((string)entry["role"]).Trim(), (rows[i].Role ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) return false;
                foreach (var part in new[] { Tuple.Create("layers", rows[i].Layers), Tuple.Create("codes", rows[i].Codes) })
                {
                    var t = entry[part.Item1];
                    if (t == null) { if ((part.Item2 ?? new List<string>()).Count > 0) return false; continue; }
                    var array = t as JArray;
                    if (array == null || array.Any(x => x.Type != JTokenType.String)) return false;
                    if (!EditableLineRole.SameList(array.Select(x => (string)x).ToList(), part.Item2)) return false;
                }
            }
            return true;
        }

        /// <summary>A list written whole when it differs from the file's (blanks in the file count as a
        /// difference, so editing takes them out); PMX's, or an empty one, is written as absence.</summary>
        private void WriteList(string key, IList<string> edit, IList<string> pmx)
        {
            if (edit == null) return;
            var o = Orientation(false);
            var token = o != null ? o[key] : null;
            // A list written wrongly (text, numbers) is put right by the first edit.
            var wellFormed = token == null || token.Type == JTokenType.Null ||
                             (token is JArray && ((JArray)token).All(x => x.Type == JTokenType.String));
            List<string> written;
            var inFile = FileList(o, key, out written);
            var raw = inFile ? written : pmx.ToList();
            var wanted = Clean(edit);
            if (wellFormed && EditableLineRole.SameList(raw, wanted)) return;
            if (wanted.Count == 0 || EditableLineRole.SameList(wanted, pmx))
            {
                if (o != null) o.Remove(key);
                return;
            }
            Orientation(true)[key] = new JArray(wanted.Cast<object>().ToArray());
        }

        // ------------------------------------------------------------------ reading

        /// <summary>True when the file names a list of its own for <paramref name="key"/> (an empty
        /// list is not one: PMX's applies).</summary>
        private static bool FileList(JObject o, string key, out List<string> list)
        {
            list = null;
            var array = o != null ? o[key] as JArray : null;
            if (array == null || array.Count == 0) return false;
            list = RawList(array);
            return true;
        }

        private static List<string> RawList(JToken token)
        {
            var array = token as JArray;
            if (array == null) return new List<string>();
            return array.Select(t => t.Type == JTokenType.Null ? string.Empty : t.ToString().Trim()).ToList();
        }

        private static List<string> Clean(IEnumerable<string> list)
        {
            return (list ?? Enumerable.Empty<string>()).Select(s => (s ?? string.Empty).Trim()).Where(s => s.Length > 0).ToList();
        }

        private static string Text(JObject o, string key, string absentMeans)
        {
            var t = o != null ? o[key] : null;
            if (t == null || t.Type == JTokenType.Null) return absentMeans;
            return t.ToString();
        }

        // ------------------------------------------------------------------ checks

        /// <summary>The lines, lists, distances and QC layers of the orientation section, as written: a
        /// row that would not even load is named by its number.</summary>
        private void OrientationDetailProblems(List<string> problems)
        {
            var o = _root["orientation"] as JObject;
            if (o == null) return;
            const string page = "Symbols and Rotations: ";
            double v;

            foreach (var d in OrientationDistances)
            {
                var t = o[d.Key];
                if (t != null && (!TryNumber(t, out v) || v < d.Minimum || v > d.Maximum))
                    problems.Add(page + d.What + " must be between " + Show(d.Minimum) + " and " + Show(d.Maximum) + " ft (not " + t + ").");
            }
            var arrow = o["qcArrowPlotted"];
            if (arrow != null && (!TryNumber(arrow, out v) || v < QcArrowMinimum || v > QcArrowMaximum))
                problems.Add(page + "the plotted length of a QC arrow must be between " + Show(QcArrowMinimum) + " and " + Show(QcArrowMaximum) + " inch (not " + arrow + ").");

            foreach (var layer in new[] { Tuple.Create("qcLayer", "QC layer (direction arrows and notes)"), Tuple.Create("qcInferredLayer", "QC layer for inferred connections") })
            {
                var t = o[layer.Item1];
                if (t == null || t.Type == JTokenType.Null) continue;
                string why;
                if (t.Type != JTokenType.String) problems.Add(page + "the " + layer.Item2 + " must be a layer name.");
                else if (string.IsNullOrWhiteSpace((string)t)) problems.Add(page + "the " + layer.Item2 + " is required.");
                else if (!FieldCodes.Settings.LayerMap.IsValidName((string)t, out why))
                    problems.Add(page + "the " + layer.Item2 + " \"" + (string)t + "\" cannot be used: " + why + ".");
            }

            foreach (var list in new[]
            {
                Tuple.Create("stopSignCodes", "stop and yield sign codes", true),
                Tuple.Create("stopSignWords", "stop sign words", false),
                Tuple.Create("luminaireCodes", "luminaire codes", true)
            })
            {
                var t = o[list.Item1];
                if (t == null || t.Type == JTokenType.Null) continue;
                var array = t as JArray;
                if (array == null) { problems.Add(page + "the " + list.Item2 + " must be a list."); continue; }
                if (array.Any(x => x.Type != JTokenType.String))
                { problems.Add(page + "the " + list.Item2 + " must be a list of text."); continue; }
                var entries = array.Select(x => ((string)x).Trim()).ToList();
                if (entries.Any(e => e.Length == 0))
                    problems.Add(page + "the " + list.Item2 + " have a blank entry" +
                                 (list.Item3 ? "." : ", which would make every sign a stop sign."));
                if (list.Item3)
                    foreach (var c in entries.Where(e => e.IndexOfAny(new[] { ' ', '\t' }) >= 0))
                        problems.Add(page + "\"" + c + "\" in the " + list.Item2 + " is not one code: one code per line, no spaces.");
                var twice = entries.Where(e => e.Length > 0).GroupBy(e => e, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (twice.Count > 0) problems.Add(page + "listed twice in the " + list.Item2 + ": " + string.Join(", ", twice.ToArray()) + ".");
            }

            LineProblems(o["lines"], page, problems);
        }

        private static readonly char[] BadLayerPatternCharacters = "<>/\\\":;|,=`".ToCharArray();

        private static void LineProblems(JToken token, string page, List<string> problems)
        {
            if (token == null || token.Type == JTokenType.Null) return;
            var array = token as JArray;
            if (array == null) { problems.Add(page + "\"lines\" must be a list of road and utility line rules (role, layers, codes)."); return; }

            var codeOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var layerOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var row = 0;
            foreach (var item in array)
            {
                row++;
                var entry = item as JObject;
                if (entry == null) { problems.Add(page + "line rule " + row + " is not a rule (role, layers, codes)."); continue; }
                var role = entry["role"];
                FieldCodes.Orient.LineRole r;
                var roleText = role != null && role.Type == JTokenType.String ? ((string)role).Trim() : string.Empty;
                var known = roleText.Length > 0 && Enum.TryParse(roleText, true, out r) && Enum.IsDefined(typeof(FieldCodes.Orient.LineRole), r);
                var where = page + "line rule " + row + (known ? " (" + roleText + ")" : string.Empty) + ": ";
                if (roleText.Length == 0) problems.Add(where + "needs a role (what kind of line it is).");
                else if (!known)
                    problems.Add(where + "role '" + roleText + "' is not one of " + string.Join(", ", EditableOrientationDetails.Roles) + ".");

                var listsRead = true;
                foreach (var part in new[] { "layers", "codes" })
                {
                    var t = entry[part];
                    if (t == null || t.Type == JTokenType.Null) continue;
                    if (!(t is JArray) || ((JArray)t).Any(x => x.Type != JTokenType.String))
                    {
                        problems.Add(where + "its " + part + " must be a list of text.");
                        listsRead = false;
                    }
                }
                if (!listsRead) continue;

                var layers = ReadCodes(entry["layers"]);
                var codes = ReadCodes(entry["codes"]);
                if (layers.Count == 0 && codes.Count == 0)
                    problems.Add(where + "names no layers and no codes, so no line is ever given it.");
                foreach (var l in layers.Where(l => l.IndexOfAny(BadLayerPatternCharacters) >= 0))
                    problems.Add(where + "layer \"" + l + "\" has a character AutoCAD does not allow in a layer name (< > / \\ \" : ; | , = `).");
                foreach (var c in codes.Where(c => c.IndexOfAny(new[] { ' ', '\t' }) >= 0))
                    problems.Add(where + "code '" + c + "' has a space in it; one code per entry.");

                var label = "rule " + row + (known ? " (" + roleText + ")" : string.Empty);
                foreach (var c in codes.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string first;
                    if (codeOwner.TryGetValue(c, out first))
                        problems.Add(where + "code " + c.ToUpperInvariant() + " is also in " + first + ", which is looked at first, so it never counts here.");
                    else codeOwner[c] = label;
                }
                foreach (var l in layers.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string first;
                    if (layerOwner.TryGetValue(l, out first))
                        problems.Add(where + "layer " + l + " is also in " + first + ", which is looked at first, so it never counts here.");
                    else layerOwner[l] = label;
                }
            }
        }

        private static string Show(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
