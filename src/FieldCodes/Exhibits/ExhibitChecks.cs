using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FieldCodes.Easements;

namespace FieldCodes.Exhibits
{
    /// <summary>A place a width dimension truly shows the easement's width.</summary>
    public sealed class WidthSpot
    {
        public double Station;
        public P2 At;
        public P2 Left;
        public P2 Right;
        public P2 Direction;
    }

    /// <summary>
    /// Where a strip's width can be dimensioned honestly: at right angles to the centerline, with both ends on
    /// sidelines that run parallel to the centerline there, clear of corners, bends, tapers and trim lines, and
    /// with the whole dimension inside the easement. Nothing is placed where it would mislead.
    /// </summary>
    public static class WidthDimensions
    {
        private static readonly double ParallelSine = Math.Sin(0.5 * Math.PI / 180);

        public static List<WidthSpot> Spots(IList<Course> centerline, IList<Course> boundary, double left, double right, double tolerance, IEnumerable<double> fractions)
        {
            var spots = new List<WidthSpot>();
            if (centerline == null || centerline.Count == 0 || boundary == null || boundary.Count < 2 || left + right <= tolerance) return spots;
            var length = EasementBuilder.RouteLength(centerline);
            var width = left + right;
            foreach (var fraction in fractions)
            {
                var station = length * fraction;
                if (NearBend(centerline, station, Math.Max(left, right), tolerance)) continue;
                P2 direction;
                var at = EasementBuilder.PointAtStation(centerline, station, out direction);
                var normal = direction.LeftNormal();
                var l = at + normal * left;
                var r = at - normal * right;
                if (!OnParallelSideline(boundary, l, direction, width, tolerance) || !OnParallelSideline(boundary, r, direction, width, tolerance)) continue;
                if (!InsideBetween(boundary, r, l, tolerance)) continue;
                spots.Add(new WidthSpot { Station = station, At = at, Left = l, Right = r, Direction = direction });
            }
            return spots;
        }

        /// <summary>True when the station is within <paramref name="clear"/> of a turn in the centerline.</summary>
        private static bool NearBend(IList<Course> centerline, double station, double clear, double tolerance)
        {
            var along = 0.0;
            for (var i = 0; i < centerline.Count; i++)
            {
                along += centerline[i].Length;
                if (i == centerline.Count - 1) break;
                var turn = Math.Abs(P2.Cross(centerline[i].EndDirection, centerline[i + 1].StartDirection));
                if (turn > ParallelSine && Math.Abs(station - along) < clear + tolerance) return true;
            }
            return false;
        }

        /// <summary>The point is on a boundary course parallel to the centerline there, not near that course's ends.</summary>
        private static bool OnParallelSideline(IList<Course> boundary, P2 p, P2 direction, double width, double tolerance)
        {
            foreach (var c in boundary)
            {
                double along;
                var foot = c.Closest(p, out along);
                if (foot.DistanceTo(p) > tolerance * 10) continue;
                var d = c.DirectionAt(Math.Max(0, Math.Min(c.Length, along)));
                if (Math.Abs(P2.Cross(d, direction)) > ParallelSine) continue;
                var margin = Math.Min(width * 0.5, c.Length * 0.25);
                if (along < margin || along > c.Length - margin) continue;
                return true;
            }
            return false;
        }

        /// <summary>The dimension line runs inside the easement: no boundary crossing between its ends.</summary>
        private static bool InsideBetween(IList<Course> boundary, P2 a, P2 b, double tolerance)
        {
            var segment = Course.Line(a, b);
            foreach (var c in boundary)
                foreach (var hit in Intersections.Bounded(segment, c, tolerance))
                    if (hit.DistanceTo(a) > tolerance * 10 && hit.DistanceTo(b) > tolerance * 10) return false;
            return StripTrim.Inside(boundary, (a + b) * 0.5);
        }
    }

    /// <summary>
    /// Checks a north arrow turned by a dynamic block property: the parts of the block that moved when the property
    /// was set must have turned about the property's origin by the view's turn. Measured from the block geometry.
    /// </summary>
    public static class NorthArrowCheck
    {
        /// <summary>
        /// The turn, degrees counter-clockwise, of the points that moved between <paramref name="before"/> and
        /// <paramref name="after"/> (same points, same order) about <paramref name="origin"/>; null when nothing moved
        /// or the moved points did not all turn alike (the property did something other than turn the arrow).
        /// </summary>
        public static double? MeasuredTurn(IList<P2> before, IList<P2> after, P2 origin, double tolerance = 1e-6)
        {
            if (before == null || after == null || before.Count != after.Count) return null;
            var turns = new List<double>();
            for (var i = 0; i < before.Count; i++)
            {
                if (before[i].DistanceTo(after[i]) <= tolerance) continue;
                var a = before[i] - origin;
                var b = after[i] - origin;
                if (a.Length <= tolerance || b.Length <= tolerance) return null;
                if (Math.Abs(a.Length - b.Length) > Math.Max(tolerance, a.Length * 1e-4)) return null;      // moved, not turned
                turns.Add(Normalize((Math.Atan2(b.Y, b.X) - Math.Atan2(a.Y, a.X)) * 180 / Math.PI));
            }
            if (turns.Count == 0) return null;
            var first = turns[0];
            if (turns.Any(t => Math.Abs(Normalize(t - first)) > 0.05)) return null;
            return first;
        }

        /// <summary>
        /// As above without knowing where the block turns about: the turn of the moved parts relative to each other
        /// (a rigid turn keeps their shape). Null when fewer than two distinct points moved, or they did not turn alike.
        /// </summary>
        public static double? MeasuredTurn(IList<P2> before, IList<P2> after, double tolerance = 1e-6)
        {
            if (before == null || after == null || before.Count != after.Count) return null;
            var moved = Enumerable.Range(0, before.Count).Where(i => before[i].DistanceTo(after[i]) > tolerance).ToList();
            if (moved.Count < 2) return null;
            var anchor = moved[0];
            var turns = new List<double>();
            foreach (var i in moved.Skip(1))
            {
                var a = before[i] - before[anchor];
                var b = after[i] - after[anchor];
                if (a.Length <= tolerance * 10) continue;
                if (Math.Abs(a.Length - b.Length) > Math.Max(tolerance * 10, a.Length * 1e-4)) return null;     // stretched, not turned
                turns.Add(Normalize((Math.Atan2(b.Y, b.X) - Math.Atan2(a.Y, a.X)) * 180 / Math.PI));
            }
            if (turns.Count == 0) return null;
            var first = turns[0];
            if (turns.Any(t => Math.Abs(Normalize(t - first)) > 0.05)) return null;
            return first;
        }

        /// <summary>True when the measured turn matches the wanted turn within a tenth of a degree.</summary>
        public static bool Matches(double? measured, double wantedDegrees)
        {
            return measured.HasValue && Math.Abs(Normalize(measured.Value - wantedDegrees)) <= 0.1;
        }

        public static double Normalize(double degrees)
        {
            var d = degrees % 360;
            if (d > 180) d -= 360;
            if (d <= -180) d += 360;
            return d;
        }
    }

    /// <summary>What an exhibit build does with an easement hatch's pattern scale.</summary>
    public enum HatchAction { Leave, Set, KeepHand, RecordHand }

    /// <summary>
    /// Easement hatch scale for an exhibit: the pattern scale that prints the hatch lines the profile's distance apart
    /// at the exhibit scale. A scale the drafter set by hand is noticed, recorded and kept.
    /// </summary>
    public static class HatchScaling
    {
        /// <summary>The pattern scale giving <paramref name="wantedSpacingIn"/> printed, from the hatch's current scale and line spacing.</summary>
        public static double PatternScaleFor(double patternScaleNow, double spacingNowUnits, double wantedSpacingIn, double unitsPerPaperInch)
        {
            if (patternScaleNow <= 0 || spacingNowUnits <= 0 || wantedSpacingIn <= 0 || unitsPerPaperInch <= 0) return patternScaleNow;
            return patternScaleNow * (wantedSpacingIn * unitsPerPaperInch / spacingNowUnits);
        }

        /// <summary>
        /// Decides for one hatch: a scale set by hand earlier is kept (and put back if the easement was redrawn);
        /// a scale that differs from the one FTF last set was changed by hand and is recorded; otherwise the
        /// hatch gets the scale for this exhibit.
        /// </summary>
        public static HatchAction Decide(double current, double? lastSetByFtf, double? byHand, double target)
        {
            if (byHand.HasValue) return Same(current, byHand.Value) ? HatchAction.Leave : HatchAction.KeepHand;
            if (lastSetByFtf.HasValue && !Same(current, lastSetByFtf.Value)) return HatchAction.RecordHand;
            return Same(current, target) ? HatchAction.Leave : HatchAction.Set;
        }

        public static bool Same(double a, double b)
        {
            return Math.Abs(a - b) <= Math.Max(Math.Abs(a), Math.Abs(b)) * 1e-6 + 1e-12;
        }
    }

    /// <summary>One FTFEXHIBITQA finding.</summary>
    public sealed class QaLine
    {
        public string Severity;     // Error, Warning (REVIEW) or empty (INFO)
        public string Category;
        public string Item;
        public string Value;
    }

    /// <summary>
    /// The FTFEXHIBITQA summary, grouped so it is plain whether a finding touches survey content (geometry, source
    /// data, legal reproduction) or is drafting and sheet cleanup. READY FOR SURVEYOR REVIEW only ever means the
    /// exhibit can go to the surveyor; FTF approves nothing.
    /// </summary>
    public static class ExhibitQaReport
    {
        public const string Geometry = "GEOMETRY", SourceData = "SOURCE DATA", LegalReproduction = "LEGAL REPRODUCTION",
                            Drafting = "DRAFTING", SheetPlot = "SHEET / PLOT", ManualReview = "MANUAL REVIEW";

        public static readonly string[] Categories = { Geometry, SourceData, LegalReproduction, Drafting, SheetPlot, ManualReview };
        public static readonly string[] SurveyContent = { Geometry, SourceData, LegalReproduction };

        public const string Error = "Error", Review = "Warning";

        public static string Verdict(IEnumerable<QaLine> lines)
        {
            return lines.Any(l => l.Severity == Error) ? "NOT READY FOR SURVEYOR REVIEW" : "READY FOR SURVEYOR REVIEW";
        }

        public static string Counts(IList<QaLine> lines)
        {
            return lines.Count(l => l.Severity == Error) + " Errors, " + lines.Count(l => l.Severity == Review) + " Review Items, " +
                   lines.Count(l => string.IsNullOrEmpty(l.Severity)) + " Informational Items";
        }

        public static string Format(string title, IList<QaLine> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine();
            sb.AppendLine(lines.Count(l => l.Severity == Error) + " Errors");
            sb.AppendLine(lines.Count(l => l.Severity == Review) + " Review Items");
            sb.AppendLine(lines.Count(l => string.IsNullOrEmpty(l.Severity)) + " Informational Items");
            sb.AppendLine();
            sb.AppendLine(Verdict(lines));
            sb.AppendLine("(The surveyor makes the final determination; FTF does not approve exhibits.)");
            sb.AppendLine();
            var content = lines.Where(l => SurveyContent.Contains(l.Category)).ToList();
            var drafting = lines.Where(l => !SurveyContent.Contains(l.Category)).ToList();
            sb.AppendLine("Survey content (geometry, source data, legal reproduction): " + Short(content));
            sb.AppendLine("Drafting, sheet and manual review: " + Short(drafting));
            foreach (var category in Categories)
            {
                var group = lines.Where(l => l.Category == category).ToList();
                sb.AppendLine();
                sb.AppendLine(category + " -- " + Short(group));
                foreach (var severity in new[] { Error, Review, string.Empty })
                    foreach (var l in group.Where(q => (q.Severity ?? string.Empty) == severity))
                        sb.AppendLine("  " + (severity == Error ? "ERROR " : severity == Review ? "REVIEW" : "INFO  ") + " " + l.Item + ": " + l.Value);
            }
            return sb.ToString();
        }

        private static string Short(IList<QaLine> lines)
        {
            return lines.Count(l => l.Severity == Error).ToString(CultureInfo.InvariantCulture) + " errors, " +
                   lines.Count(l => l.Severity == Review).ToString(CultureInfo.InvariantCulture) + " review";
        }
    }
}
