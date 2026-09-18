using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace FieldCodes.Easements
{
    /// <summary>
    /// One call of a portion description: "the SOUTH 50.00 feet", measured at right angles
    /// from a straight line of the lot.
    /// </summary>
    public sealed class PortionStep
    {
        /// <summary>NORTH, EAST, SOUTH or WEST: what the line is called.</summary>
        [JsonProperty("side")] public string Side { get; set; }
        [JsonProperty("distance")] public double Distance { get; set; }

        /// <summary>The straight lot line the distance is measured from.</summary>
        [JsonProperty("line")] public Course Line { get; set; }

        /// <summary>The object the line came from, and which of its courses.</summary>
        [JsonProperty("handle")] public string Handle { get; set; }
        [JsonProperty("segment")] public int Segment { get; set; }
    }

    public sealed class PortionResult
    {
        public List<Course> Loop { get; set; }
        public double Area { get; set; }
        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }
        public bool Ok { get { return Errors.Count == 0; } }

        public PortionResult()
        {
            Loop = new List<Course>();
            Errors = new List<string>();
            Warnings = new List<string>();
        }
    }

    /// <summary>
    /// Portion easements: "the west 10 feet of the south 50 feet of Lot 2". Each call keeps
    /// the part of the lot lying within its distance of a lot line, measured at right
    /// angles to that line; the calls together keep what lies within all of them. Exact
    /// for curved lots: the lot is cut with StripTrim, so arcs stay arcs.
    /// </summary>
    public static class PortionBuilder
    {
        public static PortionResult Build(IList<Course> lot, IList<PortionStep> steps, double tolerance)
        {
            var result = new PortionResult();
            if (lot == null || lot.Count == 0) { result.Errors.Add("No lot boundary."); return result; }
            if (Loops.LargestGap(lot) > tolerance) { result.Errors.Add("The lot boundary does not close."); return result; }
            if (steps == null || steps.Count == 0) { result.Errors.Add("No lot line and distance was given."); return result; }

            var region = lot.ToList();
            foreach (var step in steps)
            {
                var name = "the " + Word(step.Side) + " " + step.Distance.ToString("0.00", CultureInfo.InvariantCulture) + " feet";
                if (step.Line == null || step.Line.Kind != CourseKind.Line || step.Line.Length <= tolerance)
                {
                    result.Errors.Add("For " + name + ", pick a straight lot line; a portion measured from a curve is not supported.");
                    return result;
                }
                if (step.Distance <= tolerance) { result.Errors.Add("For " + name + ", the distance must be greater than zero."); return result; }

                // Which side of the line the lot is on.
                var inward = Inward(region, step.Line, tolerance);
                if (inward == 0)
                {
                    result.Errors.Add("For " + name + ", the lot is not on one side of the picked line -- pick the line along the lot's edge.");
                    return result;
                }

                // Cut the region with the parallel line at the distance, and keep what lies between.
                var along = step.Line.StartDirection;
                var normal = along.LeftNormal() * inward;
                var size = Extent(region) * 3 + step.Distance;
                var mid = step.Line.PointAt(step.Line.Length / 2) + normal * step.Distance;
                var cut = new List<Course> { Course.Line(mid - along * size, mid + along * size) };
                var split = StripTrim.Split(region, new List<IList<Course>> { cut }, tolerance);
                if (!split.Ok) { result.Errors.AddRange(split.Errors); return result; }

                var keep = new List<int>();
                var beyond = false;
                foreach (var piece in split.Pieces)
                {
                    var d = P2.Dot(piece.InsidePoint - step.Line.Start, normal);
                    if (d < step.Distance) keep.Add(piece.Number);
                    if (d < -tolerance) beyond = true;
                }
                if (beyond)
                    result.Warnings.Add("Part of the lot lies beyond " + Word(step.Side) + " line extended; it is included in " + name + " -- check the description.");
                if (!split.TrimCuts[0])
                    result.Warnings.Add("The lot is not " + step.Distance.ToString("0.00", CultureInfo.InvariantCulture) + " feet deep from its " + Word(step.Side) +
                                        " line, so " + name + " is the whole of what remains.");
                if (keep.Count == 0) { result.Errors.Add("Nothing of the lot lies within " + name + "."); return result; }

                string failure;
                var merged = split.Pieces.Count == 1 ? split.Pieces[0].Loop : StripTrim.Merge(split, keep, tolerance, out failure);
                if (merged == null)
                {
                    result.Errors.Add(name + " falls in separate pieces of the lot; describe each piece on its own.");
                    return result;
                }
                region = merged;
            }

            if (Loops.SignedArea(region) * Loops.SignedArea(lot) < 0) region = EasementBuilder.Reverse(region);
            result.Loop = region;
            result.Area = Math.Abs(Loops.SignedArea(region));
            if (result.Area <= tolerance) result.Errors.Add("The portion has no area.");
            return result;
        }

        /// <summary>+1 when the region lies to the left of the line, -1 to the right, 0 when it straddles it.</summary>
        private static int Inward(IList<Course> region, Course line, double tolerance)
        {
            var dir = line.StartDirection;
            double left = 0, right = 0;
            foreach (var c in region)
                for (var k = 0; k <= 4; k++)
                {
                    var side = P2.Cross(dir, c.PointAt(c.Length * k / 4) - line.Start);
                    if (side > tolerance) left = Math.Max(left, side);
                    if (side < -tolerance) right = Math.Max(right, -side);
                }
            if (left > tolerance && right <= tolerance) return 1;
            if (right > tolerance && left <= tolerance) return -1;
            // A lot that crosses the line extended: go with the side holding most of it.
            if (left > right * 4) return 1;
            if (right > left * 4) return -1;
            return 0;
        }

        private static double Extent(IList<Course> loop)
        {
            var points = loop.SelectMany(c => new[] { c.Start, c.End, c.PointAt(c.Length / 2) }).ToList();
            return Math.Sqrt(Math.Pow(points.Max(p => p.X) - points.Min(p => p.X), 2) + Math.Pow(points.Max(p => p.Y) - points.Min(p => p.Y), 2));
        }

        /// <summary>What a lot line would normally be called: the compass side its outward
        /// face looks toward.</summary>
        public static string SideOf(IList<Course> lot, Course line, double tolerance)
        {
            if (line == null || line.Kind != CourseKind.Line) return "SOUTH";
            var inward = Inward(lot, line, tolerance);
            var outward = line.StartDirection.LeftNormal() * (inward == 0 ? 1 : -inward);
            if (Math.Abs(outward.Y) >= Math.Abs(outward.X)) return outward.Y >= 0 ? "NORTH" : "SOUTH";
            return outward.X >= 0 ? "EAST" : "WEST";
        }

        /// <summary>"THE WEST 10.00 FEET OF THE SOUTH 50.00 FEET OF", in the order given.</summary>
        public static string Describe(IList<PortionStep> steps, int decimals)
        {
            var format = "F" + decimals.ToString(CultureInfo.InvariantCulture);
            return string.Concat(steps.Select(s => "THE " + Word(s.Side).ToUpperInvariant() + " " +
                                                     s.Distance.ToString(format, CultureInfo.InvariantCulture) + " FEET OF ").ToArray()).TrimEnd();
        }

        /// <summary>"AS MEASURED AT RIGHT ANGLES TO THE WEST AND SOUTH LINES THEREOF".</summary>
        public static string MeasuredClause(IList<PortionStep> steps)
        {
            var sides = steps.Select(s => Word(s.Side).ToUpperInvariant()).Distinct().ToList();
            var names = sides.Count == 1 ? sides[0] + " LINE"
                : string.Join(", ", sides.Take(sides.Count - 1).ToArray()) + " AND " + sides[sides.Count - 1] + " LINES";
            return "AS MEASURED AT RIGHT ANGLES TO THE " + names + " THEREOF";
        }

        private static string Word(string side)
        {
            return string.IsNullOrWhiteSpace(side) ? "[SIDE]" : side.Trim().ToLowerInvariant();
        }
    }

    /// <summary>
    /// Metes and bounds areas clicked corner by corner. A side can follow an existing line
    /// or curve between two corners; it keeps that line's exact courses.
    /// </summary>
    public static class AreaPath
    {
        /// <summary>
        /// The courses of a path between two points on it, in order from <paramref name="from"/>
        /// to <paramref name="to"/>. On a closed path the shorter way round is taken and a note
        /// is returned. Null, with a reason, when a point is not on the path.
        /// </summary>
        public static List<Course> Between(IList<Course> path, P2 from, P2 to, double tolerance, out string problem)
        {
            problem = null;
            var a = EasementBuilder.NearestStation(path, from);
            var b = EasementBuilder.NearestStation(path, to);
            if (a.Item2 > tolerance || b.Item2 > tolerance)
            {
                problem = "The corner " + (a.Item2 > tolerance ? "before" : "after") + " the followed line is not on that line (" +
                          Math.Max(a.Item2, b.Item2).ToString("0.000", CultureInfo.InvariantCulture) + "' off).";
                return null;
            }
            var length = EasementBuilder.RouteLength(path);
            var closed = path[path.Count - 1].End.DistanceTo(path[0].Start) <= tolerance;
            if (Math.Abs(a.Item1 - b.Item1) <= tolerance) { problem = "The line is followed between the same two points."; return null; }

            List<Course> forward;
            if (a.Item1 < b.Item1) forward = EasementBuilder.SubRoute(path, a.Item1, b.Item1);
            else forward = EasementBuilder.Reverse(EasementBuilder.SubRoute(path, b.Item1, a.Item1));
            if (!closed) return Snap(forward, from, to);

            // Round the other way on a closed boundary.
            var around = a.Item1 < b.Item1
                ? EasementBuilder.Reverse(EasementBuilder.SubRoute(path, 0, a.Item1)).Concat(EasementBuilder.Reverse(EasementBuilder.SubRoute(path, b.Item1, length))).ToList()
                : EasementBuilder.SubRoute(path, a.Item1, length).Concat(EasementBuilder.SubRoute(path, 0, b.Item1)).ToList();
            var shorter = EasementBuilder.RouteLength(around) < EasementBuilder.RouteLength(forward) ? around : forward;
            return Snap(shorter, from, to);
        }

        private static List<Course> Snap(List<Course> courses, P2 from, P2 to)
        {
            courses = courses.Where(c => c.Length > 1e-9).ToList();
            if (courses.Count == 0) return courses;
            courses[0] = courses[0].WithEnds(from, courses[0].End);
            courses[courses.Count - 1] = courses[courses.Count - 1].WithEnds(courses[courses.Count - 1].Start, to);
            return courses;
        }

        /// <summary>Problems that stop a clicked area being drawn.</summary>
        public static List<string> Check(IList<Course> loop, double tolerance)
        {
            var problems = new List<string>();
            if (loop.Count < 2) { problems.Add("An area needs at least three corners."); return problems; }
            if (Loops.LargestGap(loop) > tolerance) problems.Add("The area does not close.");
            var crossings = Loops.SelfIntersections(loop, tolerance);
            if (crossings.Count > 0)
                problems.Add("The sides cross each other near " + crossings[0] + " -- click the corners in order around the area.");
            if (Math.Abs(Loops.SignedArea(loop)) <= tolerance) problems.Add("The area has no size.");
            return problems;
        }
    }
}
