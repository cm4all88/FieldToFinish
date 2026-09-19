using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Drafting;
using FieldCodes.Easements;

namespace FieldCodes.RecordSurvey
{
    public sealed class TraverseOptions
    {
        /// <summary>Build from measured values where the document gives them; otherwise the record.</summary>
        public bool PreferMeasured { get; set; }
        public double UnitsPerFoot { get; set; }
        /// <summary>Misclosure at or under this, in feet, counts as closed.</summary>
        public double ClosureToleranceFeet { get; set; }
        public double DistanceToleranceFeet { get; set; }
        public double AngleToleranceSeconds { get; set; }
        /// <summary>Calls still needing review stop the figure from being built.</summary>
        public bool RequireApproved { get; set; }
        /// <summary>1:N under which a closed figure is reported as poor.</summary>
        public double MinimumPrecision { get; set; }
        /// <summary>Try each call's alternative readings against the closure and list the ones that would close it.</summary>
        public bool SuggestAlternatives { get; set; }

        public TraverseOptions()
        {
            PreferMeasured = true;
            UnitsPerFoot = 1.0;
            ClosureToleranceFeet = 0.02;
            DistanceToleranceFeet = 0.01;
            AngleToleranceSeconds = 5.0;
            RequireApproved = true;
            MinimumPrecision = 10000.0;
            SuggestAlternatives = true;
        }
    }

    /// <summary>One course as traversed: the call, the geometry, and what was used to get it.</summary>
    public sealed class TraverseCourse
    {
        public SurveyCall Call { get; set; }
        public Course Course { get; set; }
        public ValueBasis Basis { get; set; }
        public double AzimuthUsed { get; set; }
        public double LengthUsedFeet { get; set; }
        public CurveSolution CurveSolution { get; set; }
        public PlacedCurve Placement { get; set; }
        public List<string> Notes { get; private set; }
        public string Problem { get; set; }
        public bool Placed { get { return Course != null && Problem == null; } }

        public TraverseCourse() { Notes = new List<string>(); }
    }

    /// <summary>An alternative reading that would change the closure -- shown, never applied.</summary>
    public sealed class ClosureSuggestion
    {
        public string CallId { get; set; }
        public CallAlternative Alternative { get; set; }
        public double MisclosureWith { get; set; }
        public override string ToString()
        {
            return CallId + ": " + Alternative.Field + " " + Alternative.Text + " (" + Alternative.Reason + ") would give a misclosure of " +
                   MisclosureWith.ToString("0.000", CultureInfo.InvariantCulture) + "'";
        }
    }

    public sealed class TraverseResult
    {
        public string Figure { get; set; }
        public P2 Start { get; set; }
        public List<TraverseCourse> Courses { get; private set; }
        public List<P2> Vertices { get; private set; }
        public FigureClosure Closure { get; set; }
        public List<string> Problems { get; private set; }
        public List<string> Notes { get; private set; }
        public List<ClosureSuggestion> Suggestions { get; private set; }
        /// <summary>True when every course was placed. Closure quality is reported separately, never a failure.</summary>
        public bool Ok { get { return Problems.Count == 0 && Courses.Count > 0 && Courses.All(c => c.Placed); } }

        public TraverseResult()
        {
            Courses = new List<TraverseCourse>();
            Vertices = new List<P2>();
            Problems = new List<string>();
            Notes = new List<string>();
            Suggestions = new List<ClosureSuggestion>();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060")]
        public IList<Course> Loop { get { return Courses.Where(c => c.Course != null).Select(c => c.Course).ToList(); } }
    }

    /// <summary>
    /// Traverses the calls of a figure from a start point and reports how they close. This is
    /// where survey mathematics replaces tracing: every course comes from a written bearing and
    /// distance (or curve elements), never from the picture. Nothing is adjusted to force
    /// closure -- the misclosure is reported and the surveyor decides.
    /// </summary>
    public static class TraverseBuilder
    {
        public static TraverseResult Build(SurveyFigure figure, IList<SurveyCall> orderedCalls, P2 start, TraverseOptions options)
        {
            options = options ?? new TraverseOptions();
            var result = new TraverseResult { Figure = figure != null ? figure.Name : string.Empty, Start = start };
            var calls = (orderedCalls ?? new List<SurveyCall>()).Where(c => c != null && c.Status != CallStatus.Rejected).ToList();
            if (calls.Count == 0) { result.Problems.Add("The figure has no courses."); return result; }

            var unresolved = calls.Where(c => c.Status == CallStatus.NeedsReview).Select(c => c.Id).ToList();
            if (unresolved.Count > 0 && options.RequireApproved)
            {
                result.Problems.Add("Course(s) " + string.Join(", ", unresolved.ToArray()) + " still need review; nothing is built from an unresolved extraction.");
                return result;
            }

            var at = start;
            result.Vertices.Add(at);
            double? direction = null;
            var perimeter = 0.0;
            foreach (var call in calls)
            {
                var tc = new TraverseCourse { Call = call };
                result.Courses.Add(tc);
                if (call.Kind == CallKind.Line)
                {
                    ValueBasis basis;
                    var v = call.GeometryValue(options.PreferMeasured, out basis);
                    if (v == null) { tc.Problem = "Course " + call.Id + " has no complete bearing and distance."; result.Problems.Add(tc.Problem); continue; }
                    tc.Basis = basis;
                    var az = v.AzimuthDegrees.Value;
                    if (call.Reversed) { az = Geometry.Angles.NormalizeDegrees(az + 180.0); tc.Notes.Add("Traversed against the written direction (bearing reversed for the traverse only)."); }
                    var feet = v.DistanceFeet.Value;
                    tc.AzimuthUsed = az;
                    tc.LengthUsedFeet = feet;
                    var end = Along(at, az, feet * options.UnitsPerFoot);
                    tc.Course = Course.Line(at, end);
                    perimeter += feet;
                    direction = az;
                    at = end;
                    if (basis == ValueBasis.Measured && call.Records.Any(r => r.Complete)) tc.Notes.Add("Built from the measured value; record value(s) kept as attributes.");
                }
                else
                {
                    if (call.Curve == null) { tc.Problem = "Curve " + call.Id + " has no curve data."; result.Problems.Add(tc.Problem); continue; }
                    var spec = Scaled(call.Curve, options.UnitsPerFoot);
                    tc.CurveSolution = CurveSolver.Solve(spec, options.DistanceToleranceFeet * options.UnitsPerFoot, options.AngleToleranceSeconds);
                    if (!tc.CurveSolution.Ok) { tc.Problem = "Curve " + call.Id + ": " + tc.CurveSolution.Error; result.Problems.Add(tc.Problem); continue; }
                    foreach (var d in tc.CurveSolution.Disagreements) tc.Notes.Add("Curve " + call.Id + ": " + d + " -- built from " + tc.CurveSolution.SolvedFrom + ".");
                    tc.Placement = CurveSolver.Place(tc.CurveSolution, spec, at, direction, call.Reversed);
                    if (!tc.Placement.Ok) { tc.Problem = "Curve " + call.Id + ": " + tc.Placement.Error; result.Problems.Add(tc.Problem); continue; }
                    tc.Notes.AddRange(tc.Placement.Notes);
                    tc.Notes.Add("Placed by " + tc.Placement.Method + ".");
                    tc.Basis = call.Basis == ValueBasis.Entered ? ValueBasis.Entered : ValueBasis.Recorded;
                    tc.Course = tc.Placement.Course;
                    tc.AzimuthUsed = tc.Placement.ChordAzimuthDegrees;
                    tc.LengthUsedFeet = tc.CurveSolution.ArcLength / options.UnitsPerFoot;
                    perimeter += tc.LengthUsedFeet;
                    direction = tc.Placement.TangentOutAzimuthDegrees;
                    at = tc.Course.End;
                }
                result.Vertices.Add(at);
            }

            var closed = figure == null || figure.Closed;
            var closure = new FigureClosure { Figure = result.Figure, Closed = closed, Perimeter = perimeter, Courses = result.Courses.Count(c => c.Placed) };
            result.Closure = closure;
            if (!result.Courses.All(c => c.Placed)) return result;

            if (closed)
            {
                var gap = start - at;
                closure.MisclosureDx = gap.X / options.UnitsPerFoot;
                closure.MisclosureDy = gap.Y / options.UnitsPerFoot;
                closure.Misclosure = gap.Length / options.UnitsPerFoot;
                if (closure.Misclosure > 1e-9)
                {
                    closure.MisclosureAzimuth = SurveyDirection.AzimuthFromVector(gap.X, gap.Y);
                    closure.Precision = perimeter / closure.Misclosure;
                }
                // Area of the figure with the gap closed by a straight line, as a check value.
                var loop = result.Loop.ToList();
                if (closure.Misclosure > 1e-9) loop.Add(Course.Line(at, start));
                closure.Area = Math.Abs(Loops.SignedArea(loop)) / (options.UnitsPerFoot * options.UnitsPerFoot);

                if (closure.Misclosure > options.ClosureToleranceFeet)
                {
                    result.Notes.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0} does not close: misclosure {1:0.000}' at {2}, precision {3}. The calls are drawn as written; nothing was adjusted.",
                        Name(result.Figure), closure.Misclosure,
                        closure.MisclosureAzimuth.HasValue ? SurveyDirection.FormatBearing(closure.MisclosureAzimuth.Value, 0, "°") : "-",
                        PrecisionText(closure)));
                    if (options.SuggestAlternatives) result.Suggestions.AddRange(Suggest(figure, calls, start, options, closure.Misclosure));
                }
                else if (closure.Precision.HasValue && closure.Precision.Value < options.MinimumPrecision)
                {
                    result.Notes.Add(Name(result.Figure) + " closes at " + PrecisionText(closure) + ", under the 1:" +
                                     options.MinimumPrecision.ToString("N0", CultureInfo.InvariantCulture) + " standard.");
                }
            }
            else
            {
                var loop = result.Loop.ToList();
                closure.Area = 0;
                closure.Misclosure = 0;
                result.Notes.Add(Name(result.Figure) + " is an open traverse of " + loop.Count + " course(s); no closure applies.");
            }
            return result;
        }

        /// <summary>Which single alternative readings would close the figure within tolerance. Offered only.</summary>
        private static IEnumerable<ClosureSuggestion> Suggest(SurveyFigure figure, IList<SurveyCall> calls, P2 start,
                                                              TraverseOptions options, double currentMisclosure)
        {
            var suggestions = new List<ClosureSuggestion>();
            var quiet = new TraverseOptions
            {
                PreferMeasured = options.PreferMeasured, UnitsPerFoot = options.UnitsPerFoot,
                ClosureToleranceFeet = options.ClosureToleranceFeet, DistanceToleranceFeet = options.DistanceToleranceFeet,
                AngleToleranceSeconds = options.AngleToleranceSeconds, RequireApproved = false, MinimumPrecision = 0,
                SuggestAlternatives = false          // a trial traverse must not go looking for its own alternatives
            };
            foreach (var call in calls)
            {
                foreach (var alt in call.Alternatives ?? new List<CallAlternative>())
                {
                    var trial = calls.Select(c => ReferenceEquals(c, call) ? Apply(c, alt) : c).ToList();
                    if (trial.Any(c => c == null)) continue;
                    var r = Build(figure, trial, start, quiet);
                    if (r.Closure == null || !r.Courses.All(c => c.Placed)) continue;
                    if (r.Closure.Misclosure <= options.ClosureToleranceFeet || r.Closure.Misclosure < currentMisclosure * 0.1)
                        suggestions.Add(new ClosureSuggestion { CallId = call.Id, Alternative = alt, MisclosureWith = r.Closure.Misclosure });
                }
            }
            return suggestions.OrderBy(s => s.MisclosureWith);
        }

        /// <summary>A copy of the call with one alternative substituted, for a trial traverse.</summary>
        internal static SurveyCall Apply(SurveyCall call, CallAlternative alt)
        {
            var copy = call.Clone();
            CallValue target = null;
            if (string.Equals(alt.Target, "measured", StringComparison.OrdinalIgnoreCase)) target = copy.Measured;
            else target = copy.Records.FirstOrDefault(r => string.Equals(r.SourceId ?? string.Empty, alt.Target ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                          ?? copy.Records.FirstOrDefault();
            switch ((alt.Field ?? string.Empty).ToLowerInvariant())
            {
                case "distance": if (target == null) return null; target.DistanceFeet = alt.Value; break;
                case "bearing": if (target == null) return null; target.AzimuthDegrees = alt.Value; break;
                case "radius": if (copy.Curve == null) return null; copy.Curve.Radius = alt.Value; break;
                case "arc": if (copy.Curve == null) return null; copy.Curve.ArcLength = alt.Value; break;
                case "delta": if (copy.Curve == null) return null; copy.Curve.DeltaDegrees = alt.Value; break;
                case "chord": if (copy.Curve == null) return null; copy.Curve.ChordLength = alt.Value; break;
                default: return null;
            }
            return copy;
        }

        private static CurveSpec Scaled(CurveSpec spec, double unitsPerFoot)
        {
            if (Math.Abs(unitsPerFoot - 1.0) < 1e-12) return spec;
            var s = spec.Clone();
            if (s.Radius.HasValue) s.Radius *= unitsPerFoot;
            if (s.ArcLength.HasValue) s.ArcLength *= unitsPerFoot;
            if (s.ChordLength.HasValue) s.ChordLength *= unitsPerFoot;
            if (s.TangentLength.HasValue) s.TangentLength *= unitsPerFoot;
            return s;
        }

        internal static P2 Along(P2 from, double azimuthDegrees, double distance)
        {
            var a = azimuthDegrees * Math.PI / 180.0;
            return new P2(from.X + Math.Sin(a) * distance, from.Y + Math.Cos(a) * distance);
        }

        private static string Name(string figure) { return string.IsNullOrEmpty(figure) ? "The figure" : figure; }

        public static string PrecisionText(FigureClosure c)
        {
            if (c == null || !c.Closed) return "open";
            if (!c.Precision.HasValue) return "closes exactly";
            return "1:" + Math.Floor(c.Precision.Value).ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>The whole project: every figure with a start point, in figure order.</summary>
        public static List<TraverseResult> BuildAll(RecordSurveyProject project, TraverseOptions options)
        {
            var results = new List<TraverseResult>();
            foreach (var figure in project.Figures)
            {
                var calls = project.CallsOf(figure.Name);
                if (calls.Count == 0) continue;
                var start = figure.Start ?? new P2(0, 0);
                results.Add(Build(figure, calls, start, options));
            }
            return results;
        }
    }

    /// <summary>One piece of geometry two or more figures share.</summary>
    public sealed class SharedLine
    {
        public Course Geometry { get; set; }
        /// <summary>figure/callId pairs, first is the one whose geometry is kept.</summary>
        public List<KeyValuePair<string, string>> Owners { get; private set; }
        public double MaxEndpointDeviation { get; set; }
        public List<string> Discrepancies { get; private set; }

        public SharedLine() { Owners = new List<KeyValuePair<string, string>>(); Discrepancies = new List<string>(); }
        public bool IsShared { get { return Owners.Count > 1; } }
    }

    /// <summary>
    /// Finds the courses that two figures have in common -- the line between Lot 7 and Lot 8 --
    /// so it is drawn once, and reports when the two lots' calls for it disagree. Courses are
    /// the same when their endpoints coincide (either way round) within the tolerance.
    /// </summary>
    public static class SharedLineMatcher
    {
        public static List<SharedLine> Match(IList<TraverseResult> figures, double tolerance, double distanceTolerance, double angleToleranceSeconds)
        {
            var shared = new List<SharedLine>();
            foreach (var f in figures ?? new List<TraverseResult>())
            {
                foreach (var tc in f.Courses.Where(c => c.Placed))
                {
                    var existing = shared.FirstOrDefault(s => SameCourse(s.Geometry, tc.Course, tolerance));
                    if (existing == null)
                    {
                        var line = new SharedLine { Geometry = tc.Course };
                        line.Owners.Add(new KeyValuePair<string, string>(f.Figure, tc.Call.Id));
                        shared.Add(line);
                        continue;
                    }
                    existing.Owners.Add(new KeyValuePair<string, string>(f.Figure, tc.Call.Id));
                    var deviation = Math.Max(
                        Math.Min(existing.Geometry.Start.DistanceTo(tc.Course.Start), existing.Geometry.Start.DistanceTo(tc.Course.End)),
                        Math.Min(existing.Geometry.End.DistanceTo(tc.Course.End), existing.Geometry.End.DistanceTo(tc.Course.Start)));
                    existing.MaxEndpointDeviation = Math.Max(existing.MaxEndpointDeviation, deviation);

                    // The calls behind a shared line must agree: same length, reverse bearings.
                    var first = figures.SelectMany(x => x.Courses).First(c => c.Call.Id == existing.Owners[0].Value && c.Placed);
                    var lengthDiff = Math.Abs(first.LengthUsedFeet - tc.LengthUsedFeet);
                    if (lengthDiff > distanceTolerance)
                        existing.Discrepancies.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} calls {2:0.00}', {3} {4} calls {5:0.00}' ({6:0.00}' apart)",
                            existing.Owners[0].Key, existing.Owners[0].Value, first.LengthUsedFeet, f.Figure, tc.Call.Id, tc.LengthUsedFeet, lengthDiff));
                    if (first.Call.Kind == CallKind.Line && tc.Call.Kind == CallKind.Line)
                    {
                        var sameWay = existing.Geometry.Start.DistanceTo(tc.Course.Start) <= tolerance;
                        var expected = sameWay ? first.AzimuthUsed : Geometry.Angles.NormalizeDegrees(first.AzimuthUsed + 180.0);
                        var seconds = Math.Abs(CurveSolver.AngleDiff(expected, tc.AzimuthUsed)) * 3600.0;
                        if (seconds > angleToleranceSeconds)
                            existing.Discrepancies.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} and {2} {3} bearings differ by {4:0}\"",
                                existing.Owners[0].Key, existing.Owners[0].Value, f.Figure, tc.Call.Id, seconds));
                    }
                }
            }
            return shared;
        }

        public static bool SameCourse(Course a, Course b, double tolerance)
        {
            if (a == null || b == null || a.Kind != b.Kind) return false;
            var forward = a.Start.DistanceTo(b.Start) <= tolerance && a.End.DistanceTo(b.End) <= tolerance;
            var backward = a.Start.DistanceTo(b.End) <= tolerance && a.End.DistanceTo(b.Start) <= tolerance;
            if (!forward && !backward) return false;
            if (a.Kind == CourseKind.Arc) return a.Center.DistanceTo(b.Center) <= tolerance * 10;
            return true;
        }

        /// <summary>Near-duplicates that are NOT the same course: parallel, overlapping, a hair apart. Reported for QC.</summary>
        public static List<string> NearDuplicates(IList<TraverseResult> figures, double tolerance)
        {
            var notes = new List<string>();
            var all = (figures ?? new List<TraverseResult>()).SelectMany(f => f.Courses.Where(c => c.Placed).Select(c => new { f.Figure, c })).ToList();
            for (var i = 0; i < all.Count; i++)
            for (var j = i + 1; j < all.Count; j++)
            {
                var a = all[i].c.Course;
                var b = all[j].c.Course;
                if (a.Kind != CourseKind.Line || b.Kind != CourseKind.Line) continue;
                if (SameCourse(a, b, tolerance)) continue;
                var da = a.StartDirection;
                if (Math.Abs(P2.Cross(da, b.StartDirection)) > 1e-4) continue;
                var offset = Math.Abs(P2.Cross(da, b.Start - a.Start));
                if (offset > tolerance * 50) continue;
                double s0 = P2.Dot(b.Start - a.Start, da), s1 = P2.Dot(b.End - a.Start, da);
                var overlap = Math.Min(a.Length, Math.Max(s0, s1)) - Math.Max(0, Math.Min(s0, s1));
                if (overlap > tolerance)
                    notes.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} and {2} {3} run along each other {4:0.000}' apart for {5:0.00}' -- nearly coincident, not shared",
                        all[i].Figure, all[i].c.Call.Id, all[j].Figure, all[j].c.Call.Id, offset, overlap));
            }
            return notes;
        }
    }
}
