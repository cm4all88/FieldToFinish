using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FieldCodes.Reporting
{
    /// <summary>One field code nobody has configured a rule for yet.</summary>
    public sealed class UnhandledCode
    {
        public string Code { get; set; }
        public int Count { get; set; }

        /// <summary>A few real descriptions, which is what a rule has to be written against.</summary>
        public IList<string> Samples { get; set; }

        /// <summary>Lowest point number carrying this code, for finding one in the drawing.</summary>
        public string FirstPointNumber { get; set; }

        public UnhandledCode()
        {
            Samples = new List<string>();
        }
    }

    /// <summary>
    /// Collects the codes the tool has no rule for.
    ///
    /// Reported as a summary rather than one row per point: a survey base map carries
    /// hundreds of shots across dozens of unconfigured codes, and a per-point list of
    /// those is unreadable. A count and a few real descriptions per code is what
    /// someone actually needs to write the rule.
    ///
    /// This exists because silence is worse than noise once the tool is deployed. A
    /// surveyor who sees nothing assumes the poles were handled.
    /// </summary>
    public sealed class UnhandledSummary
    {
        /// <summary>Distinct descriptions kept per code.</summary>
        public const int MaxSamples = 5;

        private readonly Dictionary<string, UnhandledCode> _codes =
            new Dictionary<string, UnhandledCode>(StringComparer.OrdinalIgnoreCase);

        public int TotalPoints { get; private set; }
        public int DistinctCodes { get { return _codes.Count; } }

        public void Add(string code, string pointNumber, string rawDescription)
        {
            if (string.IsNullOrWhiteSpace(code)) code = "(blank)";

            TotalPoints++;

            UnhandledCode entry;
            if (!_codes.TryGetValue(code, out entry))
            {
                entry = new UnhandledCode { Code = code, FirstPointNumber = pointNumber };
                _codes[code] = entry;
            }

            entry.Count++;

            if (entry.Samples.Count < MaxSamples &&
                !string.IsNullOrWhiteSpace(rawDescription) &&
                !entry.Samples.Contains(rawDescription, StringComparer.OrdinalIgnoreCase))
            {
                entry.Samples.Add(rawDescription);
            }
        }

        /// <summary>Most frequent first, so the codes worth configuring lead.</summary>
        public IList<UnhandledCode> ByFrequency()
        {
            return _codes.Values
                .OrderByDescending(c => c.Count)
                .ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public const string Suffix = ".ftf-unhandled.csv";

        public static string PathFor(string drawingPath)
        {
            if (string.IsNullOrWhiteSpace(drawingPath)) return null;

            string dir, name;
            try
            {
                dir = Path.GetDirectoryName(drawingPath);
                name = Path.GetFileNameWithoutExtension(drawingPath);
            }
            catch (ArgumentException) { return null; }

            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return null;
            return Path.Combine(dir, name + Suffix);
        }

        public string Render()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Code,Points,FirstPoint,Sample1,Sample2,Sample3,Sample4,Sample5");

            foreach (var entry in ByFrequency())
            {
                sb.Append(Csv(entry.Code)).Append(',');
                sb.Append(entry.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(Csv(entry.FirstPointNumber));

                for (var i = 0; i < MaxSamples; i++)
                {
                    sb.Append(',');
                    sb.Append(i < entry.Samples.Count ? Csv(entry.Samples[i]) : string.Empty);
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        public void Write(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");
            File.WriteAllText(path, Render(), new UTF8Encoding(true));
        }

        /// <summary>Short command-line form: the top codes with counts.</summary>
        public string OneLine(int take)
        {
            var parts = ByFrequency().Take(take)
                .Select(c => string.Format("{0}({1})", c.Code, c.Count))
                .ToArray();

            var text = string.Join(" ", parts);
            return DistinctCodes > take
                ? text + string.Format(" and {0} more", DistinctCodes - take)
                : text;
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var s = value;
            if (s[0] == '=' || s[0] == '+' || s[0] == '-' || s[0] == '@') s = "'" + s;

            var needsQuotes = s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            return needsQuotes ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}
