using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FieldCodes.Compare
{
    /// <summary>
    /// Reads annotation text into comparable facts: rim and invert elevations (with
    /// direction), pipe sizes, materials, structure identifiers, and otherwise the
    /// normalised wording. MText formatting is stripped first. Nothing is guessed: a
    /// number with no keyword is an ELEV fact only when the text is nothing else.
    /// </summary>
    public static class AnnotationParser
    {
        private static readonly Regex MtextCodes = new Regex(@"\\[A-Za-z][^;\\]*;|\\[Pp~]|[{}]", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex Number = new Regex(@"[-+]?\d+(\.\d+)?", RegexOptions.Compiled);
        private static readonly Regex Size = new Regex(@"(?<![\d.])(\d+(?:\.\d+)?)\s*(?:""|”|''|\bIN\b|\bINCH\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IdAtStart = new Regex(@"^\s*([A-Z]{2,6})\s*#?\s*(\d{1,6}[A-Z]?)\b", RegexOptions.Compiled);
        private static readonly Regex HashId = new Regex(@"#\s*(\d{1,6}[A-Z]?)\b", RegexOptions.Compiled);
        private static readonly Regex OnlyNumber = new Regex(@"^\s*[-+]?\d+(\.\d+)?\s*$", RegexOptions.Compiled);
        private static readonly string[] Directions = { "NE", "NW", "SE", "SW", "N", "S", "E", "W", "IN", "OUT" };

        /// <summary>Removes MText formatting and AutoCAD %% codes; upper-cases; collapses spaces.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var t = MtextCodes.Replace(text, " ");
            t = t.Replace("%%C", " DIA ").Replace("%%c", " DIA ").Replace("%%D", "°").Replace("%%d", "°").Replace("%%P", "+/-").Replace("%%p", "+/-");
            t = Spaces.Replace(t, " ").Trim().ToUpperInvariant();
            return t;
        }

        /// <summary>Canonical wording for equality tests: cleaned, synonyms folded to the
        /// first word of their group, punctuation that carries no meaning dropped.</summary>
        public static string Normalize(string text, AnnotationRules rules)
        {
            var t = Clean(text);
            if (t.Length == 0) return t;
            t = t.Replace("=", " ").Replace(":", " ").Replace(",", " ");
            t = Regex.Replace(t, @"(?<!\d)\.|\.(?!\d)", " ");
            t = Spaces.Replace(t, " ").Trim();
            if (rules != null && rules.Synonyms != null)
            {
                foreach (var group in rules.Synonyms)
                {
                    if (group == null || group.Count < 2) continue;
                    var canonical = group[0].Trim().ToUpperInvariant();
                    for (var i = 1; i < group.Count; i++)
                    {
                        var alt = group[i].Trim().ToUpperInvariant();
                        if (alt.Length == 0) continue;
                        t = Regex.Replace(t, @"(^|[^A-Z0-9])" + Regex.Escape(alt) + @"([^A-Z0-9]|$)", "$1" + canonical + "$2");
                    }
                }
            }
            return Spaces.Replace(t, " ").Trim();
        }

        public static bool SameText(string a, string b, AnnotationRules rules)
        {
            return string.Equals(Normalize(a, rules), Normalize(b, rules), StringComparison.Ordinal);
        }

        public static List<AnnotationFact> Parse(string text, AnnotationRules rules)
        {
            rules = rules ?? new AnnotationRules();
            var facts = new List<AnnotationFact>();
            var t = Clean(text);
            if (t.Length == 0) return facts;

            if (OnlyNumber.IsMatch(t))
            {
                facts.Add(new AnnotationFact { Key = "ELEV", Text = t, Number = ParseNumber(t) });
                return facts;
            }

            var consumed = new bool[t.Length];

            // Identifiers: "SDMH 1234 ..." at the start, or "#1234" anywhere.
            var id = IdAtStart.Match(t);
            if (id.Success && !IsKeyword(id.Groups[1].Value, rules))
            {
                facts.Add(new AnnotationFact { Key = "ID", Text = id.Groups[1].Value + " " + id.Groups[2].Value });
                Mark(consumed, id.Index, id.Length);
            }
            foreach (Match m in HashId.Matches(t))
            {
                if (!facts.Any(f => f.Key == "ID" && f.Text.EndsWith(" " + m.Groups[1].Value, StringComparison.Ordinal)))
                    facts.Add(new AnnotationFact { Key = "ID", Text = m.Groups[1].Value });
                Mark(consumed, m.Index, m.Length);
            }

            // Elevations: keyword [direction] [=:] number, or keyword number direction.
            foreach (var keyword in rules.RimKeywords ?? new List<string>()) ParseElevations(t, keyword, "RIM", facts, consumed, false);
            foreach (var keyword in rules.InvertKeywords ?? new List<string>()) ParseElevations(t, keyword, "IE", facts, consumed, true);

            // Pipe sizes: 12", 12 IN.
            foreach (Match m in Size.Matches(t))
            {
                if (Taken(consumed, m.Index, m.Length)) continue;
                facts.Add(new AnnotationFact { Key = "SIZE", Text = m.Groups[1].Value, Number = ParseNumber(m.Groups[1].Value) });
                Mark(consumed, m.Index, m.Length);
            }

            // Materials, as whole words; synonyms fold to the canonical word.
            foreach (var material in rules.Materials ?? new List<string>())
            {
                var mat = material.Trim().ToUpperInvariant();
                if (mat.Length == 0) continue;
                var m = Regex.Match(t, @"(^|[^A-Z0-9])" + Regex.Escape(mat) + @"([^A-Z0-9]|$)");
                if (!m.Success) continue;
                var canonical = Canonical(mat, rules);
                if (!facts.Any(f => f.Key == "MAT" && f.Text == canonical))
                    facts.Add(new AnnotationFact { Key = "MAT", Text = canonical });
            }

            // A material word on its own ("EDGE OF CONC", "CONC WALK") is wording, not a
            // pipe material: it only counts as a value beside a size, an elevation or an id.
            if (facts.Count > 0 && facts.All(f => f.Key == "MAT"))
                facts.Clear();
            if (facts.Count == 0)
                facts.Add(new AnnotationFact { Key = "TEXT", Text = Normalize(t, rules) });
            return facts;
        }

        private static void ParseElevations(string t, string keyword, string key, List<AnnotationFact> facts, bool[] consumed, bool withDirection)
        {
            var kw = (keyword ?? string.Empty).Trim().ToUpperInvariant();
            if (kw.Length == 0) return;
            var kwPattern = Regex.Escape(kw).Replace("\\ ", "\\s+");
            var dir = withDirection ? @"(?:\(?(?<dir>" + string.Join("|", Directions) + @")\)?)?" : string.Empty;
            // keyword (dir)? [=:]? number (dir)?
            var pattern = @"(^|[^A-Z0-9])" + kwPattern + @"\.?\s*" + dir + @"\s*[=:]?\s*(?<num>[-+]?\d+\.\d+|[-+]?\d{2,})" +
                          (withDirection ? @"(?:\s*\(?(?<dir2>" + string.Join("|", Directions) + @")\)?)?" : string.Empty) + @"(?![\d.])";
            foreach (Match m in Regex.Matches(t, pattern))
            {
                if (Taken(consumed, m.Index, m.Length)) continue;
                var number = ParseNumber(m.Groups["num"].Value);
                if (!number.HasValue) continue;
                var direction = withDirection ? (m.Groups["dir"].Success ? m.Groups["dir"].Value : m.Groups["dir2"].Success ? m.Groups["dir2"].Value : null) : null;
                var factKey = key + (string.IsNullOrEmpty(direction) ? string.Empty : "-" + direction);
                if (!facts.Any(f => f.Key == factKey && f.Number == number))
                    facts.Add(new AnnotationFact { Key = factKey, Text = m.Groups["num"].Value, Number = number });
                Mark(consumed, m.Index, m.Length);
            }
        }

        private static bool IsKeyword(string token, AnnotationRules rules)
        {
            var all = (rules.RimKeywords ?? new List<string>()).Concat(rules.InvertKeywords ?? new List<string>()).Concat(rules.Materials ?? new List<string>());
            return all.Any(k => string.Equals(k.Trim(), token, StringComparison.OrdinalIgnoreCase)) || Directions.Contains(token);
        }

        private static string Canonical(string word, AnnotationRules rules)
        {
            if (rules.Synonyms == null) return word;
            foreach (var group in rules.Synonyms)
                if (group != null && group.Count > 0 && group.Any(g => string.Equals(g.Trim(), word, StringComparison.OrdinalIgnoreCase)))
                    return group[0].Trim().ToUpperInvariant();
            return word;
        }

        private static double? ParseNumber(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : (double?)null;
        }

        private static void Mark(bool[] consumed, int index, int length)
        {
            for (var i = index; i < index + length && i < consumed.Length; i++) consumed[i] = true;
        }

        private static bool Taken(bool[] consumed, int index, int length)
        {
            for (var i = index; i < index + length && i < consumed.Length; i++) if (consumed[i]) return true;
            return false;
        }

        /// <summary>Facts that are safe to compare as values: everything but TEXT.</summary>
        public static bool IsValueFact(AnnotationFact f) { return f != null && f.Key != "TEXT"; }
    }
}
