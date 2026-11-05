using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Editing
{
    /// <summary>The rules file's bench section (FTFBENCH) as Settings > Benches shows it.</summary>
    public sealed class EditableBench
    {
        /// <summary>Codes a bench end is shot with (a picked point coded otherwise is still drawn,
        /// with a note).</summary>
        public List<string> Codes { get; set; }

        /// <summary>The layer the outline is drawn on.</summary>
        public string Layer { get; set; }

        /// <summary>The layer the size label goes on.</summary>
        public string LabelLayer { get; set; }

        /// <summary>The label's wording: {length}, {depth} and {height} in feet, with an optional
        /// number format ({length:0.#}); [ ] keeps a part only when its fields have values; \P
        /// starts a new line.</summary>
        public string LabelFormat { get; set; }

        /// <summary>True when the rules file has its own bench section; false means these are the
        /// built-in values, which the first save writes into the file.</summary>
        public bool InFile { get; set; }

        public static EditableBench Defaults()
        {
            var d = new BenchRule();
            return new EditableBench { Codes = d.Codes.ToList(), Layer = d.Layer, LabelLayer = d.LabelLayer, LabelFormat = d.LabelFormat };
        }

        /// <summary>Codes as they are written: trimmed, upper case, blanks dropped, each once.</summary>
        public static List<string> CleanCodes(IEnumerable<string> codes)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var c in codes ?? Enumerable.Empty<string>())
            {
                var t = (c ?? string.Empty).Trim().ToUpperInvariant();
                if (t.Length > 0 && seen.Add(t)) result.Add(t);
            }
            return result;
        }

        /// <summary>Codes typed in one box, separated by commas, spaces or new lines.</summary>
        public static List<string> SplitCodes(string text)
        {
            return CleanCodes((text ?? string.Empty).Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }

    public sealed partial class RuleDocument
    {
        private const string BenchKey = "bench";
        private static readonly Regex BenchField = new Regex(@"\{(?<name>[A-Za-z_]+)(?::[^}]*)?\}");

        /// <summary>The bench section, with the built-in value for anything it leaves out.</summary>
        public EditableBench GetBench()
        {
            var result = EditableBench.Defaults();
            var o = _root[BenchKey] as JObject;
            if (o == null) return result;
            result.InFile = true;
            var codes = o["codes"] as JArray;
            if (codes != null) result.Codes = codes.Where(t => t.Type == JTokenType.String).Select(t => (string)t).ToList();
            if (o["layer"] != null && o["layer"].Type == JTokenType.String) result.Layer = (string)o["layer"];
            if (o["labelLayer"] != null && o["labelLayer"].Type == JTokenType.String) result.LabelLayer = (string)o["labelLayer"];
            if (o["labelFormat"] != null && o["labelFormat"].Type == JTokenType.String) result.LabelFormat = (string)o["labelFormat"];
            return result;
        }

        /// <summary>Writes what differs from the file into the bench section, creating it the first
        /// time; nothing else in the file is touched.</summary>
        public void ApplyBench(EditableBench edit)
        {
            if (edit == null) throw new ArgumentNullException("edit");
            var current = GetBench();
            var codes = EditableBench.CleanCodes(edit.Codes);
            if (!current.InFile || !codes.SequenceEqual(current.Codes ?? new List<string>(), StringComparer.Ordinal))
                BenchSection()["codes"] = new JArray(codes.Cast<object>().ToArray());
            var layer = (edit.Layer ?? string.Empty).Trim();
            if (!current.InFile || !string.Equals(current.Layer, layer, StringComparison.Ordinal)) BenchSection()["layer"] = layer;
            var labelLayer = (edit.LabelLayer ?? string.Empty).Trim();
            if (!current.InFile || !string.Equals(current.LabelLayer, labelLayer, StringComparison.Ordinal)) BenchSection()["labelLayer"] = labelLayer;
            var format = edit.LabelFormat ?? string.Empty;
            if (!current.InFile || !string.Equals(current.LabelFormat, format, StringComparison.Ordinal)) BenchSection()["labelFormat"] = format;
        }

        /// <summary>Puts this document's bench settings onto another (Save re-reads the file first).</summary>
        public void CopyBenchTo(RuleDocument other)
        {
            if (other == null) throw new ArgumentNullException("other");
            if (!(_root[BenchKey] is JObject)) return;
            other.ApplyBench(GetBench());
        }

        private JObject BenchSection()
        {
            var o = _root[BenchKey] as JObject;
            if (o == null)
            {
                o = new JObject();
                _root[BenchKey] = o;
            }
            return o;
        }

        private void BenchProblems(List<string> problems)
        {
            var o = _root[BenchKey] as JObject;
            if (o == null) return;
            const string page = "Benches: ";
            var b = GetBench();

            foreach (var key in new[] { "layer", "labelLayer", "labelFormat" })
                if (o[key] != null && o[key].Type != JTokenType.String) problems.Add(page + key + " must be text.");
            if (o["codes"] != null && !(o["codes"] is JArray)) problems.Add(page + "the codes must be a list.");

            string why;
            foreach (var layer in new[] { Tuple.Create("outline", b.Layer), Tuple.Create("label", b.LabelLayer) })
            {
                if (string.IsNullOrWhiteSpace(layer.Item2))
                {
                    problems.Add(page + "the " + layer.Item1 + " layer is required.");
                    continue;
                }
                if (!FieldCodes.Settings.LayerMap.IsValidName(layer.Item2, out why))
                    problems.Add(page + "the " + layer.Item1 + " layer \"" + layer.Item2 + "\" cannot be used: " + why + ".");
            }

            if (string.IsNullOrWhiteSpace(b.LabelFormat))
                problems.Add(page + "the label wording is required.");
            else
            {
                var unknown = BenchField.Matches(b.LabelFormat).Cast<Match>().Select(m => m.Groups["name"].Value)
                    .Where(n => !new[] { "length", "depth", "height" }.Contains(n, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (unknown.Count > 0)
                    problems.Add(page + "the label wording uses {" + string.Join("}, {", unknown.ToArray()) +
                                 "}; a bench label can use {length}, {depth} and {height}.");
                if (b.LabelFormat.Count(c => c == '{') != b.LabelFormat.Count(c => c == '}'))
                    problems.Add(page + "the label wording has a { without its } (or the other way round).");
            }

            var codes = b.Codes ?? new List<string>();
            if (codes.Any(string.IsNullOrWhiteSpace)) problems.Add(page + "a bench code is blank.");
            foreach (var c in codes.Where(c => !string.IsNullOrWhiteSpace(c) && c.Trim().IndexOf(' ') >= 0))
                problems.Add(page + "\"" + c.Trim() + "\" is not one code: a code has no spaces.");
            var duplicates = codes.Where(c => !string.IsNullOrWhiteSpace(c)).GroupBy(c => c.Trim(), StringComparer.OrdinalIgnoreCase)
                                  .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Count > 0) problems.Add(page + "listed twice: " + string.Join(", ", duplicates.ToArray()) + ".");
        }
    }
}
