using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Drafting;
using FieldCodes.Settings;

namespace FieldCodes.Easements
{
    /// <summary>
    /// How the CAD polygon itself closes. A boundary built by FTF closes by construction:
    /// every course starts exactly where the last one ended. That is reported as such --
    /// never as a precision ratio, which would be meaningless for exact geometry.
    /// </summary>
    public sealed class CadClosureReport
    {
        public double Gap { get; set; }
        public bool ClosesByConstruction { get; set; }
        public double Perimeter { get; set; }
        public double Area { get; set; }
        public int CourseCount { get; set; }
        public List<P2> SelfIntersections { get; set; }
        public int Overlaps { get; set; }
        public int ZeroLengthCourses { get; set; }
        public List<string> Problems { get; set; }
        public bool Ok { get { return Problems.Count == 0; } }

        public CadClosureReport()
        {
            SelfIntersections = new List<P2>();
            Problems = new List<string>();
        }
    }

    /// <summary>
    /// The courses as the legal description states them -- bearings and distances rounded to
    /// the profile's precision, curves by radius, central angle and arc length -- traversed
    /// independently from the Point of Beginning, and compared with the CAD geometry.
    /// </summary>
    public sealed class CourseClosureReport
    {
        /// <summary>True when the courses describe a closed figure (they should end at the start).</summary>
        public bool ClosedFigure { get; set; }
        public double Misclosure { get; set; }
        public double? MisclosureAzimuth { get; set; }
        public double Perimeter { get; set; }

        /// <summary>N in 1:N. Null when the stated courses close exactly, or the figure is open.</summary>
        public double? Precision { get; set; }

        /// <summary>The largest distance between a traversed corner and the CAD corner it stands for.</summary>
        public double MaxDeviation { get; set; }
        public int WorstCourse { get; set; }
        public bool Reproduces { get; set; }
        public double Tolerance { get; set; }
        public List<P2> Traversed { get; set; }
        public List<string> Problems { get; set; }
        public List<string> Notes { get; set; }

        public CourseClosureReport()
        {
            Traversed = new List<P2>();
            Problems = new List<string>();
            Notes = new List<string>();
        }
    }

    /// <summary>One closure check within an easement: a component boundary, a hole, the centerline, a tie.</summary>
    public sealed class ClosureItem
    {
        public string Name { get; set; }
        public CadClosureReport Cad { get; set; }
        public CourseClosureReport Courses { get; set; }
        public List<string> Problems { get; private set; }
        public List<string> Notes { get; private set; }

        public ClosureItem() { Problems = new List<string>(); Notes = new List<string>(); }
    }

    /// <summary>Every closure check for one easement record, and whether its legal courses can be presented for review.</summary>
    public sealed class RecordClosure
    {
        public List<ClosureItem> Items { get; private set; }
        public RecordClosure() { Items = new List<ClosureItem>(); }

        public IEnumerable<string> Problems { get { return Items.SelectMany(i => i.Problems.Select(p => i.Name + ": " + p)); } }
        public bool Ready { get { return !Items.Any(i => i.Problems.Count > 0); } }
    }

    public static class Closure
    {
        /// <summary>
        /// Checks an easement record the way its drawing and its legal draft both use it: the CAD
        /// boundary of each component and hole, and the stated courses -- a strip's centerline, an
        /// area's courses, each commencement tie -- traversed on their own. Portion calls are
        /// checked for distances that would round when stated.
        /// </summary>
        public static RecordClosure ForRecord(EasementRecord r, EasementSettings settings)
        {
            var result = new RecordClosure();
            var tolerance = settings.ToleranceFt;
            var reproduce = settings.ReproductionToleranceFt;

            Action<string, IList<CourseData>> cad = (name, loop) =>
            {
                if (loop == null || loop.Count == 0) return;
                var item = new ClosureItem { Name = name, Cad = Cad(loop.Select(d => d.Course).ToList(), tolerance) };
                item.Problems.AddRange(item.Cad.Problems);
                result.Items.Add(item);
            };
            Action<string, P2, IList<CourseData>, bool> stated = (name, start, courses, closed) =>
            {
                if (courses == null || courses.Count == 0) return;
                var item = new ClosureItem { Name = name, Courses = Courses(start, courses, closed, settings, reproduce) };
                item.Problems.AddRange(item.Courses.Problems);
                item.Notes.AddRange(item.Courses.Notes);
                if (closed && item.Courses.Precision.HasValue && item.Courses.Precision.Value < settings.MinimumClosurePrecision)
                    item.Problems.Add("The stated courses close at " + PrecisionText(item.Courses) + ", worse than the profile's 1:" +
                                      settings.MinimumClosurePrecision.ToString("N0", CultureInfo.InvariantCulture) + ".");
                result.Items.Add(item);
            };

            cad(r.Components != null && r.Components.Count > 0 ? "Component A boundary" : "Boundary", r.BoundaryCourses);
            if (r.Holes != null)
                for (var h = 0; h < r.Holes.Count; h++) cad("Hole " + (h + 1), r.Holes[h]);

            var route = r.RouteCourses ?? new List<CourseData>();
            if (route.Count > 0)
            {
                var start = route[0].Course.Start;
                if (r.IsArea) stated("Stated courses", start, route, true);
                else stated("Stated centerline", start, route, false);
            }
            if (r.PointOfCommencement != null && r.TieCourses().Count > 0)
                stated("Commencement tie", r.PointOfCommencement.Point, r.TieCourses(), false);
            if (r.TerminusTie != null && route.Count > 0)
                stated("Terminus tie", route[route.Count - 1].Course.End, new List<CourseData> { r.TerminusTie }, false);

            if (r.PortionSteps != null) PortionCalls(result, "Portion calls", r.PortionSteps, settings);
            foreach (var x in (r.Exclusions ?? new List<Exclusion>()).Where(x => x.PortionSteps != null))
                PortionCalls(result, "Exclusion calls", x.PortionSteps, settings);

            foreach (var c in r.Components ?? new List<EasementComponent>())
            {
                cad("Component " + c.Label + " boundary", c.BoundaryCourses);
                for (var h = 0; h < (c.Holes ?? new List<List<CourseData>>()).Count; h++) cad("Component " + c.Label + " hole " + (h + 1), c.Holes[h]);
                if (c.Kind == EasementComponent.AreaKind && c.Courses != null && c.Courses.Count > 0)
                    stated("Component " + c.Label + " stated courses", c.Courses[0].Course.Start, c.Courses, true);
                if (c.PointOfCommencement != null && c.TieCourses().Count > 0)
                    stated("Component " + c.Label + " commencement tie", c.PointOfCommencement.Point, c.TieCourses(), false);
                if (c.PortionSteps != null) PortionCalls(result, "Component " + c.Label + " portion calls", c.PortionSteps, settings);
            }
            return result;
        }

        /// <summary>A portion is described by its distances; one entered more finely than the description
        /// states it would describe a different area.</summary>
        private static void PortionCalls(RecordClosure result, string name, IList<PortionStep> steps, EasementSettings settings)
        {
            var item = new ClosureItem { Name = name };
            foreach (var s in steps)
            {
                var statedDistance = Stated(s.Distance, settings.DistanceDecimals);
                var difference = Math.Abs(statedDistance - s.Distance);
                if (difference > settings.ReproductionToleranceFt)
                    item.Problems.Add("THE " + (s.Side ?? string.Empty).ToUpperInvariant() + " " + s.Distance.ToString("0.#####", CultureInfo.InvariantCulture) +
                                      " FEET is stated as " + statedDistance.ToString("F" + settings.DistanceDecimals, CultureInfo.InvariantCulture) + " feet.");
            }
            if (item.Problems.Count == 0) item.Notes.Add("Distances are stated as entered; the portion is reproduced by its calls.");
            result.Items.Add(item);
        }
        // ================================================================== CAD

        public static CadClosureReport Cad(IList<Course> loop, double tolerance)
        {
            var r = new CadClosureReport();
            if (loop == null || loop.Count == 0) { r.Problems.Add("There is no boundary."); return r; }
            r.CourseCount = loop.Count;
            r.Gap = Loops.LargestGap(loop);
            r.ClosesByConstruction = r.Gap <= 1e-9;
            r.Perimeter = Loops.Perimeter(loop);
            r.Area = Math.Abs(Loops.SignedArea(loop));
            r.ZeroLengthCourses = loop.Count(c => c.Length <= tolerance);
            r.SelfIntersections = Loops.SelfIntersections(loop, tolerance).ToList();
            r.Overlaps = CountOverlaps(loop, tolerance);

            if (r.Gap > tolerance)
                r.Problems.Add("The boundary has a gap of " + Ft(r.Gap) + ".");
            if (r.SelfIntersections.Count > 0)
                r.Problems.Add("The boundary crosses itself at " + r.SelfIntersections.Count + " place(s), first near " + r.SelfIntersections[0] + ".");
            if (r.Overlaps > 0)
                r.Problems.Add(r.Overlaps + " course(s) run back over another course.");
            if (r.ZeroLengthCourses > 0)
                r.Problems.Add(r.ZeroLengthCourses + " course(s) have no length.");
            if (r.Area <= tolerance)
                r.Problems.Add("The boundary encloses no area.");
            return r;
        }

        /// <summary>Pairs of courses lying along each other for some length.</summary>
        private static int CountOverlaps(IList<Course> loop, double tolerance)
        {
            var count = 0;
            for (var i = 0; i < loop.Count; i++)
            for (var j = i + 1; j < loop.Count; j++)
            {
                var a = loop[i];
                var b = loop[j];
                if (a.Kind != CourseKind.Line || b.Kind != CourseKind.Line || a.Length <= tolerance || b.Length <= tolerance) continue;
                var da = a.StartDirection;
                if (Math.Abs(P2.Cross(da, b.StartDirection)) > 1e-9) continue;
                if (Math.Abs(P2.Cross(da, b.Start - a.Start)) > tolerance) continue;
                double s0 = P2.Dot(b.Start - a.Start, da), s1 = P2.Dot(b.End - a.Start, da);
                var overlap = Math.Min(a.Length, Math.Max(s0, s1)) - Math.Max(0, Math.Min(s0, s1));
                if (overlap > tolerance) count++;
            }
            return count;
        }

        // ============================================================= courses

        /// <summary>
        /// Traverses the stated courses from <paramref name="start"/>. <paramref name="closedFigure"/>
        /// says the description should return to its start (a metes and bounds area); an open
        /// description (a centerline) is compared only with the CAD corners. Each course's end is
        /// compared with the CAD course's end.
        /// </summary>
        public static CourseClosureReport Courses(P2 start, IList<CourseData> courses, bool closedFigure,
                                                  EasementSettings settings, double tolerance)
        {
            var r = new CourseClosureReport { ClosedFigure = closedFigure, Tolerance = tolerance };
            r.Traversed.Add(start);
            if (courses == null || courses.Count == 0) { r.Problems.Add("There are no stated courses to traverse."); return r; }

            var at = start;
            double? direction = null;       // azimuth, degrees, at the end of the last course as stated
            for (var i = 0; i < courses.Count; i++)
            {
                var d = courses[i];
                var c = d.Course;
                P2 end;
                if (c.Kind == CourseKind.Line)
                {
                    var az = StatedAzimuth(d.AzimuthDegrees, settings);
                    var length = Stated(d.Length, settings.DistanceDecimals);
                    end = Along(at, az, length);
                    direction = az;
                    r.Perimeter += length;
                }
                else
                {
                    // As the legal states it: radius, central angle, arc length, turn, and the
                    // tangent in from the previous course or the radial bearing when non-tangent.
                    var radius = Stated(c.Radius, settings.DistanceDecimals);
                    var delta = StatedAngle(d.DeltaDegrees ?? 0, settings);
                    var arc = Stated(c.Length, settings.DistanceDecimals);
                    var left = c.CounterClockwise;
                    double tangentIn;
                    if (direction.HasValue && IsTangent(courses[i - 1].Course, c))
                        tangentIn = direction.Value;
                    else
                    {
                        var toCenter = c.Center - c.Start;
                        var radial = StatedAzimuth(SurveyDirection.AzimuthFromVector(toCenter.X, toCenter.Y), settings);
                        tangentIn = left ? radial + 90 : radial - 90;
                        r.Notes.Add("Course " + (i + 1) + " is traversed as a non-tangent curve from its stated radial bearing.");
                    }
                    var chord = 2 * radius * Math.Sin(delta * Math.PI / 360);
                    var chordAz = left ? tangentIn - delta / 2 : tangentIn + delta / 2;
                    end = Along(at, chordAz, chord);
                    direction = left ? tangentIn - delta : tangentIn + delta;
                    r.Perimeter += arc;
                    if (Math.Abs(arc - radius * delta * Math.PI / 180) > Math.Pow(10, -settings.DistanceDecimals))
                        r.Notes.Add("Course " + (i + 1) + ": the stated arc length " + Ft(arc) + " and radius x central angle " +
                                    Ft(radius * delta * Math.PI / 180) + " disagree beyond the stated precision.");
                }

                var deviation = end.DistanceTo(c.End);
                if (deviation > r.MaxDeviation) { r.MaxDeviation = deviation; r.WorstCourse = i + 1; }
                r.Traversed.Add(end);
                at = end;
            }

            if (closedFigure)
            {
                r.Misclosure = at.DistanceTo(start);
                if (r.Misclosure > 1e-9)
                {
                    var back = start - at;
                    r.MisclosureAzimuth = SurveyDirection.AzimuthFromVector(back.X, back.Y);
                    r.Precision = r.Perimeter / r.Misclosure;
                }
            }

            r.Reproduces = r.MaxDeviation <= tolerance && (!closedFigure || r.Misclosure <= tolerance);
            if (r.MaxDeviation > tolerance)
                r.Problems.Add("The stated courses do not reproduce the CAD boundary: course " + r.WorstCourse + " ends " +
                               Ft(r.MaxDeviation) + " from the CAD corner (tolerance " + Ft(tolerance) + ").");
            if (closedFigure && r.Misclosure > tolerance)
                r.Problems.Add("The stated courses do not return to the Point of Beginning: misclosure " + Ft(r.Misclosure) + ".");
            return r;
        }

        /// <summary>Whether a curve is described as tangent to the course before it -- the same test the legal writer uses.</summary>
        public static bool IsTangent(Course previous, Course curve)
        {
            return previous != null && P2.Dot(previous.EndDirection, curve.StartDirection) > Math.Cos(1.0 / 3600 * Math.PI / 180);
        }

        public static double StatedAzimuth(double azimuth, EasementSettings settings)
        {
            var factor = 3600.0 * Math.Pow(10, Math.Max(0, settings.BearingSecondsDecimals));
            return Math.Round(azimuth * factor, MidpointRounding.AwayFromZero) / factor;
        }

        private static double StatedAngle(double degrees, EasementSettings settings) { return StatedAzimuth(degrees, settings); }

        private static double Stated(double value, int decimals)
        {
            return Math.Round(value, Math.Max(0, decimals), MidpointRounding.AwayFromZero);
        }

        private static P2 Along(P2 from, double azimuthDegrees, double distance)
        {
            var a = azimuthDegrees * Math.PI / 180;
            return new P2(from.X + Math.Sin(a) * distance, from.Y + Math.Cos(a) * distance);
        }

        public static string Ft(double feet)
        {
            return feet.ToString("0.000", CultureInfo.InvariantCulture) + "'";
        }

        /// <summary>"1:25,400", or a plain statement when there is nothing to divide by.</summary>
        public static string PrecisionText(CourseClosureReport r)
        {
            if (!r.ClosedFigure) return "not a closed figure (a centerline); compared corner by corner";
            if (!r.Precision.HasValue) return "closes exactly at the stated precision";
            return "1:" + Math.Floor(r.Precision.Value).ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
