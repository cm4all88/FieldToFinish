using System;
using System.Collections.Generic;
using System.Globalization;

namespace FieldCodes.Contours
{
    /// <summary>
    /// What FTFCONTOUR reads off a surface before and after extracting, to prove the
    /// surface came through untouched: its point and triangle counts, elevation range and
    /// mean, out-of-date flag and style. Any value that could not be read is left null and
    /// is simply not compared.
    /// </summary>
    public sealed class SurfaceFingerprint
    {
        public string Name { get; set; }
        public long? Points { get; set; }
        public long? Triangles { get; set; }
        public double? MinimumElevation { get; set; }
        public double? MaximumElevation { get; set; }
        public double? MeanElevation { get; set; }
        public bool? OutOfDate { get; set; }

        /// <summary>The surface style, by handle.</summary>
        public string Style { get; set; }

        /// <summary>How many of the values were read (for the report: "checked 6 of 7").</summary>
        public int ReadCount
        {
            get
            {
                var n = 0;
                if (Name != null) n++;
                if (Points.HasValue) n++;
                if (Triangles.HasValue) n++;
                if (MinimumElevation.HasValue) n++;
                if (MaximumElevation.HasValue) n++;
                if (MeanElevation.HasValue) n++;
                if (OutOfDate.HasValue) n++;
                if (Style != null) n++;
                return n;
            }
        }

        /// <summary>Every way <c>this</c> (after) differs from <paramref name="before"/>.
        /// Doubles are compared exactly: an untouched surface reports identical numbers.</summary>
        public IList<string> DifferencesFrom(SurfaceFingerprint before)
        {
            var differences = new List<string>();
            if (before == null) return differences;

            Compare(differences, "name", before.Name, Name);
            Compare(differences, "point count", before.Points, Points);
            Compare(differences, "triangle count", before.Triangles, Triangles);
            Compare(differences, "lowest elevation", before.MinimumElevation, MinimumElevation);
            Compare(differences, "highest elevation", before.MaximumElevation, MaximumElevation);
            Compare(differences, "mean elevation", before.MeanElevation, MeanElevation);
            if (before.OutOfDate.HasValue && OutOfDate.HasValue && before.OutOfDate.Value != OutOfDate.Value)
                differences.Add(OutOfDate.Value
                    ? "the surface became out of date"
                    : "the surface was rebuilt (it is no longer out of date)");
            Compare(differences, "style", before.Style, Style);
            return differences;
        }

        public string Describe()
        {
            var parts = new List<string>();
            if (Points.HasValue) parts.Add(Points.Value.ToString("N0", CultureInfo.InvariantCulture) + " points");
            if (Triangles.HasValue) parts.Add(Triangles.Value.ToString("N0", CultureInfo.InvariantCulture) + " triangles");
            if (MinimumElevation.HasValue && MaximumElevation.HasValue)
                parts.Add("elevations " + Number(MinimumElevation.Value) + " to " + Number(MaximumElevation.Value));
            return parts.Count == 0 ? "no statistics available" : string.Join(", ", parts.ToArray());
        }

        private static void Compare(List<string> differences, string what, string before, string after)
        {
            if (before == null || after == null) return;
            if (!string.Equals(before, after, StringComparison.Ordinal))
                differences.Add(string.Format(CultureInfo.InvariantCulture, "{0} changed from {1} to {2}", what, before, after));
        }

        private static void Compare(List<string> differences, string what, long? before, long? after)
        {
            if (!before.HasValue || !after.HasValue) return;
            if (before.Value != after.Value)
                differences.Add(string.Format(CultureInfo.InvariantCulture, "{0} changed from {1} to {2}", what, before.Value, after.Value));
        }

        private static void Compare(List<string> differences, string what, double? before, double? after)
        {
            if (!before.HasValue || !after.HasValue) return;
            if (before.Value.Equals(after.Value)) return;
            differences.Add(string.Format(CultureInfo.InvariantCulture, "{0} changed from {1} to {2}",
                what, before.Value.ToString("R", CultureInfo.InvariantCulture),
                after.Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        internal static string Number(double value)
        {
            return value.ToString("0.0###", CultureInfo.InvariantCulture);
        }
    }
}
