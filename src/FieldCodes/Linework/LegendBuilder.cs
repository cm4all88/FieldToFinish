using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Linework
{
    /// <summary>One legend entry: a feature present in the drawing and the layer
    /// whose symbology represents it.</summary>
    public struct LegendRow
    {
        public string FeatureName;
        public string Layer;
    }

    /// <summary>
    /// Derives the legend from what the drawing actually contains: every line
    /// feature the inventory identified, once each, alphabetical. A feature not
    /// present never appears -- the legend describes this drawing, not the whole
    /// office standard.
    /// </summary>
    public static class LegendBuilder
    {
        public static IList<LegendRow> Rows(IEnumerable<LineworkRow> inventory)
        {
            var rows = new List<LegendRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in inventory ?? Enumerable.Empty<LineworkRow>())
            {
                if (row == null || row.Source == LineIdentitySource.None) continue;
                if (string.IsNullOrEmpty(row.FeatureName) || row.FeatureName == "-") continue;
                if (!seen.Add(row.FeatureName)) continue;

                rows.Add(new LegendRow
                {
                    FeatureName = row.FeatureName,
                    Layer = row.Layer
                });
            }

            rows.Sort((a, b) => string.Compare(a.FeatureName, b.FeatureName,
                                               StringComparison.OrdinalIgnoreCase));
            return rows;
        }
    }
}
