using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Contours
{
    /// <summary>What happened at one requested elevation.</summary>
    public enum ContourOutcome
    {
        /// <summary>Contour polylines were drawn.</summary>
        Drawn,
        /// <summary>The elevation is below the surface's lowest point.</summary>
        BelowSurface,
        /// <summary>The elevation is above the surface's highest point.</summary>
        AboveSurface,
        /// <summary>The elevation is exactly the lowest or highest point: the surface only
        /// touches it there, so there is no line to draw.</summary>
        TouchesOnly,
        /// <summary>The surface crosses the elevation, but only outside the boundary.</summary>
        OutsideBoundary,
        /// <summary>The surface returned nothing at this elevation (for example it is only
        /// crossed inside a hidden area).</summary>
        NoGeometry
    }

    /// <summary>One elevation's result, for the command-line report.</summary>
    public sealed class ContourElevationResult
    {
        public ContourElevation Elevation { get; set; }
        public ContourOutcome Outcome { get; set; }

        /// <summary>Pieces the surface gave before any boundary clip.</summary>
        public int Extracted { get; set; }

        public int Pieces { get; set; }
        public int ClosedPieces { get; set; }
        public int Vertices { get; set; }
        public double Length { get; set; }
        public int Labels { get; set; }

        /// <summary>Earlier FTF contour pieces (and their labels) at this elevation removed so
        /// the new ones do not duplicate them.</summary>
        public int Replaced { get; set; }

        /// <summary>Earlier pieces that ran out of the boundary and were trimmed back to the
        /// part outside it rather than removed.</summary>
        public int TrimmedToOutside { get; set; }

        /// <summary>True when Civil 3D's extraction failed and the contour was traced from the
        /// surface triangles instead.</summary>
        public bool Traced { get; set; }

        public string ExtractionProblem { get; set; }

        /// <summary>Largest difference between an extracted polyline's own elevation and the
        /// requested one. The drawn polyline always carries the requested elevation.</summary>
        public double? ExtractedElevationDifference { get; set; }

        /// <summary>Vertices whose surface elevation was read back for the accuracy check.</summary>
        public int Checked { get; set; }

        /// <summary>Largest |surface elevation - contour elevation| among the checked vertices.</summary>
        public double? WorstDeviation { get; set; }
    }

    /// <summary>The whole run, worded for the command line.</summary>
    public sealed class ContourRunReport
    {
        /// <summary>A checked vertex further than this from the surface is reported.</summary>
        public const double CheckTolerance = 1e-6;

        public string SurfaceName { get; set; }
        public SurfaceFingerprint Before { get; set; }
        public SurfaceFingerprint After { get; set; }
        public string Units { get; set; }
        public bool Clipped { get; set; }
        public bool Labelled { get; set; }
        public string Layer { get; set; }
        public List<ContourElevationResult> Results { get; private set; }

        public ContourRunReport()
        {
            Results = new List<ContourElevationResult>();
            Units = string.Empty;
        }

        public int TotalPieces { get { return Results.Sum(r => r.Pieces); } }

        public static ContourOutcome Classify(double elevation, double? lowest, double? highest,
                                              int extracted, int afterClip, bool clipped)
        {
            if (lowest.HasValue && elevation < lowest.Value) return ContourOutcome.BelowSurface;
            if (highest.HasValue && elevation > highest.Value) return ContourOutcome.AboveSurface;
            if (extracted == 0)
            {
                if ((lowest.HasValue && elevation == lowest.Value) ||
                    (highest.HasValue && elevation == highest.Value))
                    return ContourOutcome.TouchesOnly;
                return ContourOutcome.NoGeometry;
            }
            if (clipped && afterClip == 0) return ContourOutcome.OutsideBoundary;
            return afterClip > 0 ? ContourOutcome.Drawn : ContourOutcome.NoGeometry;
        }

        public IList<string> Lines()
        {
            var lines = new List<string>();
            var unit = string.IsNullOrEmpty(Units) ? string.Empty : " " + Units;

            lines.Add(string.Format(CultureInfo.InvariantCulture, "FTFCONTOUR: surface \"{0}\"{1}.",
                SurfaceName, Before != null ? " (" + Before.Describe() + ")" : string.Empty));

            var width = Results.Count == 0 ? 0 : Results.Max(r => r.Elevation.Text.Length);
            foreach (var r in Results)
                lines.Add("  " + r.Elevation.Text.PadRight(width) + "  " + Describe(r, unit));

            var replaced = Results.Sum(r => r.Replaced);
            var trimmed = Results.Sum(r => r.TrimmedToOutside);
            if (replaced > 0 || trimmed > 0)
            {
                var parts = new List<string>();
                if (replaced > 0)
                    parts.Add(Count(replaced, "earlier FTF contour piece") + " at the same elevation" +
                              (Clipped ? " inside the boundary" : string.Empty) + " replaced");
                if (trimmed > 0)
                    parts.Add(Count(trimmed, "earlier piece") + " crossing the boundary trimmed back to the part outside it");
                lines.Add("  No duplicates: " + string.Join("; ", parts.ToArray()) + ".");
            }

            var traced = Results.Where(r => r.Traced).ToList();
            if (traced.Count > 0)
                lines.Add("  Civil 3D's contour extraction failed at " +
                          string.Join(", ", traced.Select(r => r.Elevation.Text).ToArray()) +
                          " (" + (traced[0].ExtractionProblem ?? "no reason given") +
                          "); traced from the surface triangles instead.");

            var mismatched = Results.Where(r => r.ExtractedElevationDifference.HasValue &&
                                                r.ExtractedElevationDifference.Value > CheckTolerance).ToList();
            foreach (var r in mismatched)
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "  Note: Civil 3D returned {0} at an elevation {1} off; drawn at exactly {0}.",
                    r.Elevation.Text, Small(r.ExtractedElevationDifference.Value) + unit));

            var checkedCount = Results.Sum(r => r.Checked);
            if (checkedCount > 0)
            {
                var worst = Results.Where(r => r.WorstDeviation.HasValue).Select(r => r.WorstDeviation.Value)
                                   .DefaultIfEmpty(0).Max();
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    worst <= CheckTolerance
                        ? "  Accuracy: {0} read back from the surface; all on their contour elevation (largest difference {1})."
                        : "  WARNING: {0} read back from the surface; the largest is {1} off its contour elevation. Check the surface and the result.",
                    Count(checkedCount, "vertex", "vertices"), Small(worst) + unit));
            }

            if (Before != null && After != null)
            {
                var differences = After.DifferencesFrom(Before);
                if (differences.Count == 0)
                    lines.Add("  Surface untouched: definition, triangles, elevation range, style and build state " +
                              "read the same before and after.");
                else
                    lines.Add("  WARNING: the surface reads differently after extraction: " +
                              string.Join("; ", differences.ToArray()) + ".");
            }

            var drawn = TotalPieces;
            lines.Add(drawn == 0
                ? "  Nothing drawn."
                : string.Format(CultureInfo.InvariantCulture, "  {0} on layer {1}. One U undoes the lot.",
                                Count(drawn, "contour polyline"), Layer));
            return lines;
        }

        private string Describe(ContourElevationResult r, string unit)
        {
            switch (r.Outcome)
            {
                case ContourOutcome.BelowSurface:
                    return "none: below the surface's lowest point" + Range(r, true) + ".";
                case ContourOutcome.AboveSurface:
                    return "none: above the surface's highest point" + Range(r, false) + ".";
                case ContourOutcome.TouchesOnly:
                    return "none: the surface only touches this elevation at a single point.";
                case ContourOutcome.OutsideBoundary:
                    return string.Format(CultureInfo.InvariantCulture, "none inside the boundary ({0} outside it).",
                                         Count(r.Extracted, "piece"));
                case ContourOutcome.NoGeometry:
                    return "none: the surface has no contour at this elevation inside its visible area.";
            }

            var open = r.Pieces - r.ClosedPieces;
            var shape = r.ClosedPieces == 0 ? (r.Pieces == 1 ? "open" : "all open")
                      : open == 0 ? (r.Pieces == 1 ? "closed" : "all closed")
                      : string.Format(CultureInfo.InvariantCulture, "{0} closed, {1} open", r.ClosedPieces, open);
            var text = string.Format(CultureInfo.InvariantCulture, "{0} ({1}), {2}{3}, {4}",
                Count(r.Pieces, "contour"), shape, r.Length.ToString("0.00", CultureInfo.InvariantCulture), unit,
                Count(r.Vertices, "vertex", "vertices"));
            if (r.Labels > 0) text += ", " + Count(r.Labels, "label");
            return text + ".";
        }

        /// <summary>" (10.12)": the surface's own lowest or highest elevation, when known.</summary>
        private string Range(ContourElevationResult r, bool low)
        {
            if (Before == null) return string.Empty;
            var value = low ? Before.MinimumElevation : Before.MaximumElevation;
            return value.HasValue ? " (" + SurfaceFingerprint.Number(value.Value) + ")" : string.Empty;
        }

        private static string Small(double value)
        {
            return value.ToString(value < 0.0001 && value > 0 ? "0.0#########" : "0.0000", CultureInfo.InvariantCulture);
        }

        private static string Count(int n, string singular, string plural = null)
        {
            return n.ToString("N0", CultureInfo.InvariantCulture) + " " +
                   (n == 1 ? singular : plural ?? singular + "s");
        }
    }
}
