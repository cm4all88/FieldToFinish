using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FieldCodes.Linework
{
    /// <summary>
    /// Renders the linework inventory as a CSV beside the drawing and as the
    /// eight-part summary a surveyor reads on the command line: what exists, how it
    /// was identified, what it is, what is ambiguous, what is unknown, and what is
    /// FTF's own finishing output rather than survey linework.
    ///
    /// Pure formatting -- no Autodesk types, fully unit-testable.
    /// </summary>
    public static class LineworkInventoryCsv
    {
        public const string Suffix = ".ftf-linework.csv";
        public const string Header = "EntityType,Figure,TrimbleName,Layer,Feature,Codes,Source,Length,LabelLayer";

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

        public static string Render(IEnumerable<LineworkRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);

            foreach (var row in rows ?? Enumerable.Empty<LineworkRow>())
            {
                if (row == null) continue;
                sb.Append(Csv(row.EntityType)).Append(',');
                sb.Append(Csv(row.FigureName)).Append(',');
                sb.Append(Csv(row.TrimbleName)).Append(',');
                sb.Append(Csv(row.Layer)).Append(',');
                sb.Append(Csv(row.FeatureName)).Append(',');
                sb.Append(Csv(row.Codes)).Append(',');
                sb.Append(Csv(row.SourceText)).Append(',');
                sb.Append(Csv(row.LengthText)).Append(',');
                sb.Append(Csv(row.LabelLayerText));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// The command-line summary. <paramref name="ftfOwnedByKind"/> is FTF's own
        /// finishing output found among the curves (driplines, leaders) -- reported
        /// separately because it is not survey linework.
        /// </summary>
        public static string Summary(IList<LineworkRow> rows,
                                     IDictionary<string, int> ftfOwnedByKind)
        {
            rows = rows ?? new List<LineworkRow>();
            var lines = new List<string>();

            lines.Add(string.Format("{0} Civil 3D line object(s) found.", rows.Count));

            // 1-3: by Civil 3D object type.
            var byType = rows.GroupBy(r => r.EntityType ?? "?")
                             .OrderByDescending(g => g.Count());
            lines.Add("  By object type: " + string.Join(", ",
                byType.Select(g => g.Key + " " + g.Count()).ToArray()));

            // 4: identification method.
            lines.Add(string.Format(
                "  Identified by figure name: {0}, by Trimble name: {1}, by layer: {2}, " +
                "unidentified: {3}",
                rows.Count(r => r.Source == LineIdentitySource.FigureName),
                rows.Count(r => r.Source == LineIdentitySource.TrimbleName),
                rows.Count(r => r.Source == LineIdentitySource.Layer),
                rows.Count(r => r.Source == LineIdentitySource.None)));

            // 5: feature classifications with counts.
            var features = rows.Where(r => r.Source != LineIdentitySource.None)
                               .GroupBy(r => r.FeatureName + " (" + r.Codes + ")")
                               .OrderByDescending(g => g.Count());
            foreach (var g in features)
                lines.Add(string.Format("    {0,4}  {1}", g.Count(), g.Key));

            // 6: ambiguous rows -- a shared layer names several candidates.
            var ambiguous = rows.Where(r => r.Codes != null && r.Codes.IndexOf('/') >= 0).ToList();
            lines.Add(string.Format("  Ambiguous (layer shared by several codes): {0}",
                ambiguous.Count));
            foreach (var g in ambiguous.GroupBy(r => r.Layer).OrderByDescending(g => g.Count()))
                lines.Add(string.Format("    {0,4}  {1} -> {2}",
                    g.Count(), g.Key, g.First().Codes));

            // 7: unidentified, grouped by layer -- and by any surviving TrimbleName,
            // so "Top of Slope" breaklines are distinguishable from bare polylines.
            // Names on the configured ignore list are unidentified ON PURPOSE and
            // reported on their own line instead of as unknowns.
            var ignored = rows.Where(r => r.Source == LineIdentitySource.None &&
                                          r.NameIgnored).ToList();
            if (ignored.Count > 0)
                lines.Add(string.Format(
                    "  TBC names intentionally ignored (configured): {0} -- {1}",
                    ignored.Count,
                    string.Join(", ", ignored.Select(r => "\"" + r.TrimbleName + "\"")
                                             .Distinct().ToArray())));

            var unknown = rows.Where(r => r.Source == LineIdentitySource.None &&
                                          !r.NameIgnored).ToList();
            lines.Add(string.Format("  Not configured as line features: {0}", unknown.Count));
            foreach (var g in unknown.GroupBy(r => string.IsNullOrEmpty(r.TrimbleName)
                                                   ? r.Layer
                                                   : r.Layer + "  \"" + r.TrimbleName + "\"")
                                     .OrderByDescending(g => g.Count())
                                     .Take(15))
                lines.Add(string.Format("    {0,4}  on {1}", g.Count(), g.Key));
            if (unknown.GroupBy(r => r.Layer + "|" + r.TrimbleName).Count() > 15)
                lines.Add("    ... more in the CSV");

            // 8: FTF's own output among the curves.
            if (ftfOwnedByKind != null && ftfOwnedByKind.Count > 0)
                lines.Add("  FTF-owned finishing curves (not survey linework): " +
                          string.Join(", ", ftfOwnedByKind
                              .OrderByDescending(kv => kv.Value)
                              .Select(kv => kv.Key + " " + kv.Value)
                              .ToArray()));
            else
                lines.Add("  FTF-owned finishing curves: none");

            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var s = value;
            if (s[0] == '=' || s[0] == '+' || s[0] == '-' || s[0] == '@')
                s = "'" + s;

            return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }
    }
}
