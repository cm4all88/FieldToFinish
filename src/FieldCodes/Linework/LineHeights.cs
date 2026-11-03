using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FieldCodes.Linework
{
    /// <summary>A fence height read off one shot, and what it was read from.</summary>
    public sealed class RecordedHeight
    {
        /// <summary>Distance along the line, in drawing units.</summary>
        public double Station { get; set; }
        public double Feet { get; set; }
        /// <summary>The height as written ("3.5", "6'").</summary>
        public string Written { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Fence heights the crew writes on the line's shots: "FNC B 3.5" is the fence code, B the
    /// begin-line code, and 3.5 the height in feet. The height holds along the line until a
    /// shot records another ("FNC 5"), and goes into the fence's label ("3.5' FENCE").
    ///
    /// Only a number after the code and the line codes counts; a figure number fused to the
    /// code ("FNC1 B 5") is not a height. A value that cannot be a fence height is reported,
    /// not used.
    /// </summary>
    public static class LineHeights
    {
        /// <summary>Civil 3D linework codes a crew writes after a figure code (begin, end,
        /// close, curve, rectangle...). They are skipped when looking for the height.</summary>
        public static readonly IList<string> LineCodes = new List<string>
        {
            "B", "BEG", "BEGIN", "E", "END", "C", "CLS", "CLOSE", "OC", "BC", "EC", "PC", "PT", "CC",
            "ST", "SP", "ESP", "BS", "ES", "CONT", "CNT", "T", "P",
            // PMX-Universal.f2f_xdef: continue, rectangle, right turn, extend, circle.
            "U", "G", "RT", "X", "CIR"
        }.AsReadOnly();

        /// <summary>Line codes followed by their own numbers (rectangle sides, offsets).</summary>
        public static readonly IList<string> ParameterCodes = new List<string> { "R", "RECT", "OFF", "OS", "HO", "VO", "SO", "RPN", "CPN" }.AsReadOnly();

        private static readonly Regex Number = new Regex(@"^(?<v>[0-9]+(?:\.[0-9]+)?)\s*(?:'|FT|FEET|FOOT)?$",
                                                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The height a description records, or null when it records none. <paramref name="problem"/>
        /// is set when there is a number that cannot be a fence height (outside 0.5 to
        /// <paramref name="maxFeet"/> ft) or a second, different number.
        /// </summary>
        public static double? Read(string description, double maxFeet, out string written, out string problem)
        {
            written = null;
            problem = null;
            if (string.IsNullOrWhiteSpace(description)) return null;
            var tokens = description.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return ReadTokens(tokens.Skip(1).ToList(), description, maxFeet, out written, out problem);
        }

        private static double? ReadTokens(IList<string> tokens, string description, double maxFeet,
                                          out string written, out string problem)
        {
            written = null;
            problem = null;
            double? found = null;
            for (var i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (LineCodes.Contains(t.ToUpperInvariant())) continue;
                // A line code that takes numbers (a rectangle's width, an offset): they are not heights.
                if (ParameterCodes.Contains(t.ToUpperInvariant()))
                {
                    while (i + 1 < tokens.Count && Number.IsMatch(tokens[i + 1])) i++;
                    continue;
                }
                var m = Number.Match(t);
                if (!m.Success) continue;
                double v;
                if (!double.TryParse(m.Groups["v"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                if (found.HasValue)
                {
                    if (Math.Abs(found.Value - v) > 1e-9)
                    {
                        problem = "two heights in \"" + description.Trim() + "\" (" + written + " and " + t + "); neither was used";
                        return null;
                    }
                    continue;
                }
                if (v < 0.5 || v > maxFeet)
                {
                    problem = "\"" + t + "\" in \"" + description.Trim() + "\" is not a fence height (0.5 to " +
                              maxFeet.ToString("0.#", CultureInfo.InvariantCulture) + " ft)";
                    return null;
                }
                found = v;
                written = m.Groups["v"].Value;
            }
            return found;
        }

        /// <summary>
        /// The height recorded after this feature's own code, wherever the code sits in the
        /// description: "FGP FNC B 5" is a gate post shot on the fence, and 5 is the fence's
        /// height. Reading stops at <paramref name="stopCodes"/> (a gate post's own code and
        /// its width: "FNC B 5 FGP 12" is a 5 ft fence and a 12 ft gate). Null when the
        /// description does not carry one of <paramref name="codes"/>.
        /// </summary>
        public static double? Read(string description, ICollection<string> codes, ICollection<string> stopCodes,
                                   double maxFeet, out string written, out string problem)
        {
            written = null;
            problem = null;
            var after = TokensAfter(description, codes, stopCodes);
            if (after == null) return null;
            return ReadTokens(after, description, maxFeet, out written, out problem);
        }

        /// <summary>
        /// The words after the first of <paramref name="codes"/> in a description (a fused
        /// figure number allowed: FNC1), up to the next of <paramref name="stopCodes"/>.
        /// Null when none of the codes is there.
        /// </summary>
        public static IList<string> TokensAfter(string description, ICollection<string> codes, ICollection<string> stopCodes)
        {
            if (string.IsNullOrWhiteSpace(description) || codes == null || codes.Count == 0) return null;
            var tokens = description.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < tokens.Length; i++)
            {
                if (!ContainsCode(codes, CodeOf(tokens[i]))) continue;
                var after = new List<string>();
                for (var j = i + 1; j < tokens.Length; j++)
                {
                    if (stopCodes != null && ContainsCode(stopCodes, CodeOf(tokens[j]))) break;
                    if (ContainsCode(codes, CodeOf(tokens[j]))) break;
                    after.Add(tokens[j]);
                }
                return after;
            }
            return null;
        }

        private static bool ContainsCode(ICollection<string> codes, string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            foreach (var c in codes)
                if (string.Equals(c == null ? null : c.Trim(), code, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The height in force at a station: the last one recorded at or before it.
        /// Before the first recorded height there is none.</summary>
        public static RecordedHeight At(IEnumerable<RecordedHeight> recorded, double station, double tolerance = 1e-6)
        {
            return (recorded ?? Enumerable.Empty<RecordedHeight>())
                .Where(h => h.Station <= station + tolerance)
                .OrderBy(h => h.Station)
                .LastOrDefault();
        }

        /// <summary>The fence's label with its height: the format's {height} and {label}
        /// ("{height}' {label}" makes "3.5' FENCE"). Decimals below 0 keep the height as
        /// written (5 stays 5, 3.5 stays 3.5).</summary>
        public static string Compose(string format, string label, RecordedHeight height, int decimals)
        {
            if (height == null) return label;
            var f = string.IsNullOrWhiteSpace(format) ? DefaultFormat : format;
            var value = decimals < 0
                ? (height.Written ?? height.Feet.ToString("0.##", CultureInfo.InvariantCulture))
                : height.Feet.ToString("F" + Math.Min(decimals, 4).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return f.Replace("{height}", value).Replace("{label}", label ?? string.Empty).Trim();
        }

        public const string DefaultFormat = "{height}' {label}";

        /// <summary>The code a description starts with, without a fused figure number
        /// ("FNC1 B 5" gives FNC).</summary>
        public static string CodeOf(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return string.Empty;
            var first = description.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
            var end = first.Length;
            while (end > 1 && char.IsDigit(first[end - 1])) end--;
            return first.Substring(0, end);
        }

        /// <summary>Whether a line feature reads heights: its own setting, else any feature
        /// whose name or label says FENCE.</summary>
        public static bool Applies(LineFeatureRule feature)
        {
            if (feature == null) return false;
            if (feature.ReadHeight.HasValue) return feature.ReadHeight.Value;
            return Contains(feature.Label, "FENCE") || Contains(feature.Name, "FENCE");
        }

        private static bool Contains(string s, string word)
        {
            return !string.IsNullOrEmpty(s) && s.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
