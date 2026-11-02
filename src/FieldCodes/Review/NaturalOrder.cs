using System;
using System.Collections.Generic;
using System.Globalization;

namespace FieldCodes.Review
{
    /// <summary>
    /// The order the review tables sort a column in. A column of numbers sorts as
    /// numbers ("9" before "10", "1,204" as 1204, "152.3 ft" by its value); text
    /// sorts case-insensitively with any digit runs inside it compared by value, so
    /// "PT 9" comes before "PT 10" and "RWC2" before "RWC10". Placeholders such as
    /// "-" or an empty cell sort after real values ascending.
    ///
    /// Pure, so the table helper in the plugin needs nothing but this to sort.
    /// </summary>
    public sealed class NaturalOrder : IComparer<string>
    {
        public static readonly NaturalOrder Instance = new NaturalOrder();

        private static readonly HashSet<string> Units = new HashSet<string>(
            new[] { "ft", "in", "m", "mm", "cm", "deg", "%", "'", "\"", "pt", "pts" },
            StringComparer.OrdinalIgnoreCase);

        public int Compare(string x, string y)
        {
            var xBlank = IsPlaceholder(x);
            var yBlank = IsPlaceholder(y);
            if (xBlank || yBlank)
                return xBlank == yBlank ? 0 : (xBlank ? 1 : -1);

            double a, b;
            if (TryNumber(x, out a) && TryNumber(y, out b))
            {
                var byValue = a.CompareTo(b);
                if (byValue != 0) return byValue;
            }

            return CompareRuns(x.Trim(), y.Trim());
        }

        /// <summary>The leading number of a cell ("1,204", "152.3 ft", "-4.5"),
        /// when the cell is a number with at most a short unit after it.</summary>
        public static bool TryNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var s = text.Trim();
            var end = 0;
            if (end < s.Length && (s[end] == '-' || s[end] == '+')) end++;
            var digits = 0;
            while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.' || s[end] == ','))
            {
                if (char.IsDigit(s[end])) digits++;
                end++;
            }
            if (digits == 0) return false;

            // Anything after the number must be a unit ("ft", "%", "deg"), not more
            // text: "12 OAK" is a description, not a number.
            var rest = s.Substring(end).Trim();
            if (rest.Length > 0 && !Units.Contains(rest)) return false;

            return double.TryParse(s.Substring(0, end).Replace(",", string.Empty),
                                   NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>An empty cell or a dash standing in for "no value".</summary>
        public static bool IsPlaceholder(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;
            var t = s.Trim();
            return t == "-" || t == "--" || t == "\u2014";
        }

        /// <summary>Text with digit runs compared by value, case-insensitive.</summary>
        private static int CompareRuns(string x, string y)
        {
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    var si = i; while (i < x.Length && char.IsDigit(x[i])) i++;
                    var sj = j; while (j < y.Length && char.IsDigit(y[j])) j++;

                    var a = x.Substring(si, i - si).TrimStart('0');
                    var b = y.Substring(sj, j - sj).TrimStart('0');
                    if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                    var digits = string.CompareOrdinal(a, b);
                    if (digits != 0) return digits;
                    continue;
                }

                var c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0) return c;
                i++; j++;
            }

            var byLength = (x.Length - i).CompareTo(y.Length - j);
            return byLength != 0 ? byLength : string.CompareOrdinal(x, y);
        }
    }
}
