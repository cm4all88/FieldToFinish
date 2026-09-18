using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Easements
{
    /// <summary>The route and its source order, ready to offset.</summary>
    public sealed class RouteResult
    {
        public List<Course> Courses { get; private set; }
        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }

        /// <summary>Distance from the chosen TPOB to the route, feet.</summary>
        public double TpobOffset { get; set; }

        public RouteResult()
        {
            Courses = new List<Course>();
            Errors = new List<string>();
            Warnings = new List<string>();
        }

        public bool Ok { get { return Errors.Count == 0; } }
    }

    public sealed class EasementBuildResult
    {
        public List<Course> Centerline { get; set; }
        public List<Course> LeftSideline { get; set; }
        public List<Course> RightSideline { get; set; }
        public List<Course> BeginEdge { get; set; }
        public List<Course> EndEdge { get; set; }

        /// <summary>The closed boundary: left sideline, end edge, right sideline back,
        /// begin edge. Starts at the left sideline's first point.</summary>
        public List<Course> Boundary { get; set; }

        public double Area { get; set; }
        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }

        public EasementBuildResult()
        {
            Centerline = new List<Course>();
            LeftSideline = new List<Course>();
            RightSideline = new List<Course>();
            BeginEdge = new List<Course>();
            EndEdge = new List<Course>();
            Boundary = new List<Course>();
            Errors = new List<string>();
            Warnings = new List<string>();
        }

        public bool Ok { get { return Errors.Count == 0; } }
    }

    /// <summary>
    /// Builds a strip easement from its controlling route. Pure geometry: the CAD
    /// layer turns selected objects into courses, and turns the result back into
    /// entities. Every failure is reported -- nothing invalid is ever returned as
    /// if it were good.
    /// </summary>
    public static class EasementBuilder
    {
        public const double DefaultTolerance = 0.005;

        // ================================================================= route

        /// <summary>
        /// Puts the selected pieces end to end starting at the True Point of
        /// Beginning. Each piece is flipped as needed to continue from the last; a
        /// gap between pieces is an error, never bridged. When the TPOB falls part
        /// way along the route, the route starts there.
        /// </summary>
        public static RouteResult OrderRoute(IList<IList<Course>> pieces, P2 tpob, double tolerance)
        {
            var result = new RouteResult();
            var flat = new List<Course>();

            for (var p = 0; p < pieces.Count; p++)
            {
                var piece = pieces[p].Where(c => c.Length > tolerance).ToList();
                if (piece.Count == 0) continue;

                if (flat.Count == 0)
                {
                    // The first piece runs toward the second when there is one; a lone
                    // piece runs away from the TPOB.
                    var next = p + 1 < pieces.Count
                        ? pieces[p + 1].Where(c => c.Length > tolerance).ToList()
                        : new List<Course>();
                    if (next.Count > 0)
                    {
                        var tail = piece[piece.Count - 1].End;
                        var head = piece[0].Start;
                        var nearTail = Math.Min(tail.DistanceTo(next[0].Start), tail.DistanceTo(next[next.Count - 1].End));
                        var nearHead = Math.Min(head.DistanceTo(next[0].Start), head.DistanceTo(next[next.Count - 1].End));
                        if (nearHead < nearTail) piece = Reverse(piece);
                    }
                    else if (NearestStation(piece, tpob).Item1 > RouteLength(piece) / 2.0)
                    {
                        piece = Reverse(piece);
                    }
                    flat.AddRange(piece);
                    continue;
                }

                var end = flat[flat.Count - 1].End;
                if (piece[0].Start.DistanceTo(end) <= tolerance)
                    flat.AddRange(piece);
                else if (piece[piece.Count - 1].End.DistanceTo(end) <= tolerance)
                    flat.AddRange(Reverse(piece));
                else
                {
                    var gap = Math.Min(piece[0].Start.DistanceTo(end), piece[piece.Count - 1].End.DistanceTo(end));
                    result.Errors.Add(string.Format(CultureInfo.InvariantCulture,
                        "Route piece {0} does not connect to the previous piece (gap {1:0.000}'). Gaps are not bridged.",
                        p + 1, gap));
                    return result;
                }
            }

            if (flat.Count == 0)
            {
                result.Errors.Add("No controlling route was selected.");
                return result;
            }

            // Snap exact continuity so joins do not see tiny gaps.
            for (var i = 1; i < flat.Count; i++)
                if (flat[i].Start.DistanceTo(flat[i - 1].End) > 0)
                    flat[i] = flat[i].WithEnds(flat[i - 1].End, flat[i].End);

            var nearest = NearestStation(flat, tpob);
            result.TpobOffset = nearest.Item2;
            if (nearest.Item2 > tolerance)
            {
                result.Errors.Add(string.Format(CultureInfo.InvariantCulture,
                    "The True Point of Beginning is {0:0.000}' off the controlling route. Select a TPOB on the route.",
                    nearest.Item2));
                return result;
            }

            var courses = nearest.Item1 > tolerance ? SubRoute(flat, nearest.Item1, RouteLength(flat)) : flat;
            if (nearest.Item1 > tolerance)
                result.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "The route begins {0:0.00}' before the TPOB; the easement starts at the TPOB.", nearest.Item1));

            result.Courses.AddRange(courses);
            return result;
        }

        public static List<Course> Reverse(IList<Course> courses)
        {
            return courses.Reverse().Select(c => c.Reversed()).ToList();
        }

        public static double RouteLength(IList<Course> route)
        {
            return route.Sum(c => c.Length);
        }

        /// <summary>(station along the route, perpendicular distance) of the nearest
        /// point on the route to p.</summary>
        public static Tuple<double, double> NearestStation(IList<Course> route, P2 p)
        {
            var bestStation = 0.0;
            var bestDistance = double.MaxValue;
            var start = 0.0;
            foreach (var c in route)
            {
                double along;
                var q = c.Closest(p, out along);
                var d = q.DistanceTo(p);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestStation = start + along;
                }
                start += c.Length;
            }
            return Tuple.Create(bestStation, bestDistance);
        }

        public static P2 PointAtStation(IList<Course> route, double station, out P2 direction)
        {
            var start = 0.0;
            foreach (var c in route)
            {
                if (station <= start + c.Length + 1e-9)
                {
                    var along = Math.Max(0, station - start);
                    direction = c.DirectionAt(along);
                    return c.PointAt(along);
                }
                start += c.Length;
            }
            var last = route[route.Count - 1];
            direction = last.EndDirection;
            return last.End;
        }

        /// <summary>The part of a route between two stations.</summary>
        public static List<Course> SubRoute(IList<Course> route, double from, double to)
        {
            var result = new List<Course>();
            var start = 0.0;
            foreach (var c in route)
            {
                var end = start + c.Length;
                var a = Math.Max(from, start);
                var b = Math.Min(to, end);
                if (b - a > 1e-9) result.Add(c.Sub(a - start, b - start));
                start = end;
            }
            return result;
        }

        // ================================================================= build

        public static EasementBuildResult Build(IList<Course> route, WidthSpec width,
                                                TerminationSpec begin, TerminationSpec end,
                                                double tolerance)
        {
            var result = new EasementBuildResult();
            begin = begin ?? TerminationSpec.Perpendicular();
            end = end ?? TerminationSpec.Perpendicular();

            if (route == null || route.Count == 0) { result.Errors.Add("No controlling route."); return result; }
            if (width == null) { result.Errors.Add("No width."); return result; }
            if (width.Left < 0 || width.Right < 0 || double.IsNaN(width.Left) || double.IsNaN(width.Right))
            { result.Errors.Add("Widths cannot be negative."); return result; }
            if (width.Total <= tolerance) { result.Errors.Add("The total easement width must be greater than zero."); return result; }
            if (width.IsTapered)
            {
                result.Errors.Add("Variable-width (tapered) easements are described by the data model but not drafted yet; enter a constant width.");
                return result;
            }

            // Stations and points shorten the route before it is offset.
            var working = route.ToList();
            var length = RouteLength(working);
            var beginStation = StationFor(begin, working, tolerance, result, "beginning");
            var endStation = StationFor(end, working, tolerance, result, "ending");
            if (!result.Ok) return result;

            var from = beginStation ?? 0.0;
            var to = endStation ?? length;
            if (to - from <= tolerance)
            {
                result.Errors.Add("The ending termination is at or before the beginning termination.");
                return result;
            }
            if (from > tolerance || to < length - tolerance) working = SubRoute(working, from, to);
            result.Centerline = working;

            var left = OffsetRoute(working, width.Left, "left", tolerance, result);
            var right = OffsetRoute(working, -width.Right, "right", tolerance, result);
            if (!result.Ok) return result;

            // Boundary terminations extend or trim each sideline to the geometry.
            BoundaryHit leftBegin = null, rightBegin = null, leftEnd = null, rightEnd = null;
            if (begin.Method == TerminationMethod.Boundary)
            {
                leftBegin = CutToBoundary(ref left, begin.Boundary, true, tolerance, result, "left sideline at the beginning");
                rightBegin = CutToBoundary(ref right, begin.Boundary, true, tolerance, result, "right sideline at the beginning");
            }
            if (end.Method == TerminationMethod.Boundary)
            {
                leftEnd = CutToBoundary(ref left, end.Boundary, false, tolerance, result, "left sideline at the end");
                rightEnd = CutToBoundary(ref right, end.Boundary, false, tolerance, result, "right sideline at the end");
            }
            if (!result.Ok) return result;

            result.LeftSideline = left;
            result.RightSideline = right;

            result.EndEdge = leftEnd != null && rightEnd != null
                ? BoundaryPath(end.Boundary, leftEnd, rightEnd, tolerance)
                : new List<Course> { Course.Line(left[left.Count - 1].End, right[right.Count - 1].End) };
            result.BeginEdge = leftBegin != null && rightBegin != null
                ? BoundaryPath(begin.Boundary, rightBegin, leftBegin, tolerance)
                : new List<Course> { Course.Line(right[0].Start, left[0].Start) };

            var loop = new List<Course>();
            loop.AddRange(left);
            loop.AddRange(result.EndEdge.Where(c => c.Length > tolerance));
            loop.AddRange(Reverse(right));
            loop.AddRange(result.BeginEdge.Where(c => c.Length > tolerance));
            result.Boundary = loop;

            Validate(result, working, width, tolerance);
            return result;
        }

        private static double? StationFor(TerminationSpec spec, IList<Course> route, double tolerance,
                                          EasementBuildResult result, string which)
        {
            if (spec.Method == TerminationMethod.Station)
            {
                if (!spec.Station.HasValue || spec.Station.Value < 0 || spec.Station.Value > RouteLength(route) + tolerance)
                {
                    result.Errors.Add("The " + which + " station is off the route.");
                    return null;
                }
                return spec.Station.Value;
            }
            if (spec.Method == TerminationMethod.Point)
            {
                if (spec.Point == null) { result.Errors.Add("No " + which + " termination point."); return null; }
                return NearestStation(route, spec.Point.Point).Item1;
            }
            if (spec.Method == TerminationMethod.Boundary && (spec.Boundary == null || spec.Boundary.Count == 0))
                result.Errors.Add("No " + which + " boundary geometry was selected.");
            return null;
        }

        // ================================================================ offset

        public static List<Course> OffsetRoute(IList<Course> route, double left, string side,
                                               double tolerance, EasementBuildResult result)
        {
            var offs = new List<Course>();
            for (var i = 0; i < route.Count; i++)
            {
                string failure;
                var o = route[i].Offset(left, out failure);
                if (o == null)
                {
                    result.Errors.Add(string.Format(CultureInfo.InvariantCulture,
                        "Cannot offset the {0} sideline along course {1}: {2}.", side, i + 1, failure));
                    return offs;
                }
                offs.Add(o);
            }

            for (var i = 1; i < offs.Count; i++)
            {
                var prev = offs[i - 1];
                var next = offs[i];
                if (prev.End.DistanceTo(next.Start) <= tolerance)
                {
                    offs[i] = next.WithEnds(prev.End, next.End);
                    continue;
                }

                // An angle point: extend or trim the two courses to where they meet,
                // taking the meeting point nearest the route's own corner.
                var corner = route[i].Start;
                var candidates = Intersections.Carriers(prev, next);
                if (candidates.Count == 0)
                {
                    result.Errors.Add(string.Format(CultureInfo.InvariantCulture,
                        "The {0} sideline cannot be joined at the angle point between courses {1} and {2}.",
                        side, i, i + 1));
                    return offs;
                }
                var meet = candidates.OrderBy(p => p.DistanceTo(corner)).First();

                var trimmedPrev = prev.WithEnds(prev.Start, meet);
                var trimmedNext = next.WithEnds(meet, next.End);
                if (Collapsed(prev, trimmedPrev, tolerance) || Collapsed(next, trimmedNext, tolerance))
                {
                    result.Errors.Add(string.Format(CultureInfo.InvariantCulture,
                        "The {0} sideline collapses at the angle point between courses {1} and {2} -- a course is shorter than the offset allows.",
                        side, i, i + 1));
                    return offs;
                }
                offs[i - 1] = trimmedPrev;
                offs[i] = trimmedNext;
            }

            return offs;
        }

        private static bool Collapsed(Course original, Course trimmed, double tolerance)
        {
            if (trimmed.Length <= tolerance) return true;
            if (original.Kind == CourseKind.Line)
                return P2.Dot(trimmed.End - trimmed.Start, original.End - original.Start) <= 0;
            return trimmed.Sweep > original.Sweep + Math.PI / 2;
        }

        // ============================================================= boundary

        private sealed class BoundaryHit
        {
            public P2 Point;
            public int BoundaryIndex;
            public double BoundaryAlong;
        }

        private static BoundaryHit CutToBoundary(ref List<Course> sideline, IList<Course> boundary,
                                                 bool atStart, double tolerance,
                                                 EasementBuildResult result, string what)
        {
            if (sideline.Count == 0 || boundary == null) return null;

            var total = RouteLength(sideline);
            BoundaryHit best = null;
            var bestStation = 0.0;
            var bestScore = double.MaxValue;

            var starts = new double[sideline.Count];
            var acc = 0.0;
            for (var k = 0; k < sideline.Count; k++) { starts[k] = acc; acc += sideline[k].Length; }

            for (var b = 0; b < boundary.Count; b++)
            {
                for (var k = 0; k < sideline.Count; k++)
                {
                    var course = sideline[k];
                    var allowBefore = atStart && k == 0;
                    var allowAfter = !atStart && k == sideline.Count - 1;

                    foreach (var p in Intersections.Carriers(course, boundary[b]))
                    {
                        if (!Intersections.OnCourse(boundary[b], p, tolerance)) continue;
                        var along = course.DistanceOf(p);
                        var inside = along >= -tolerance && along <= course.Length + tolerance;
                        var before = allowBefore && along < 0;
                        var after = allowAfter && along > course.Length;
                        if (!inside && !before && !after) continue;

                        // Extensions of arcs are limited to a quarter turn -- a boundary
                        // reached only by wrapping the circle is not a real extension.
                        if (course.Kind == CourseKind.Arc && (before || after) &&
                            Math.Abs(before ? along : along - course.Length) > course.Radius * Math.PI / 2)
                            continue;

                        var station = starts[k] + along;
                        var score = atStart ? Math.Abs(station) : Math.Abs(total - station);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestStation = station;
                            best = new BoundaryHit { Point = p, BoundaryIndex = b, BoundaryAlong = boundary[b].DistanceOf(p) };
                        }
                    }
                }
            }

            if (best == null)
            {
                result.Errors.Add("The " + what + " never reaches the selected termination geometry.");
                return null;
            }

            if (atStart)
            {
                if (bestStation < 0)
                    sideline[0] = sideline[0].WithEnds(best.Point, sideline[0].End);
                else
                {
                    sideline = SubRoute(sideline, bestStation, total);
                    if (sideline.Count > 0) sideline[0] = sideline[0].WithEnds(best.Point, sideline[0].End);
                }
            }
            else
            {
                if (bestStation > total)
                {
                    var last = sideline.Count - 1;
                    sideline[last] = sideline[last].WithEnds(sideline[last].Start, best.Point);
                }
                else
                {
                    sideline = SubRoute(sideline, 0, bestStation);
                    if (sideline.Count > 0)
                    {
                        var last = sideline.Count - 1;
                        sideline[last] = sideline[last].WithEnds(sideline[last].Start, best.Point);
                    }
                }
            }

            if (sideline.Count == 0 || RouteLength(sideline) <= tolerance)
                result.Errors.Add("The " + what + " has no length left after terminating at the boundary.");
            return best;
        }

        /// <summary>The boundary geometry between two hits, following its vertices.
        /// On a closed boundary the shorter way round is used.</summary>
        private static List<Course> BoundaryPath(IList<Course> boundary, BoundaryHit from, BoundaryHit to,
                                                 double tolerance)
        {
            var forward = PathForward(boundary, from, to);
            var closed = boundary.Count > 1 && boundary[boundary.Count - 1].End.DistanceTo(boundary[0].Start) <= tolerance;
            if (!closed) return forward;

            var backward = Reverse(PathForward(boundary, to, from));
            return RouteLength(backward) < RouteLength(forward) ? backward : forward;
        }

        private static List<Course> PathForward(IList<Course> boundary, BoundaryHit a, BoundaryHit b)
        {
            var path = new List<Course>();
            if (a.BoundaryIndex == b.BoundaryIndex)
            {
                var c = boundary[a.BoundaryIndex];
                if (a.BoundaryAlong <= b.BoundaryAlong) path.Add(c.Sub(a.BoundaryAlong, b.BoundaryAlong).WithEnds(a.Point, b.Point));
                else path.Add(c.Sub(b.BoundaryAlong, a.BoundaryAlong).WithEnds(b.Point, a.Point).Reversed());
                return path;
            }

            var closed = boundary[boundary.Count - 1].End.DistanceTo(boundary[0].Start) < 1e-6;
            if (a.BoundaryIndex < b.BoundaryIndex || closed)
            {
                var i = a.BoundaryIndex;
                path.Add(boundary[i].WithEnds(a.Point, boundary[i].End));
                i = (i + 1) % boundary.Count;
                while (i != b.BoundaryIndex)
                {
                    path.Add(boundary[i]);
                    i = (i + 1) % boundary.Count;
                }
                path.Add(boundary[i].WithEnds(boundary[i].Start, b.Point));
                return path;
            }

            return Reverse(PathForward(boundary, b, a));
        }

        // ============================================================ validation

        private static void Validate(EasementBuildResult result, IList<Course> route, WidthSpec width,
                                     double tolerance)
        {
            var loop = result.Boundary;

            var gap = Loops.LargestGap(loop);
            if (gap > tolerance)
                result.Errors.Add(string.Format(CultureInfo.InvariantCulture,
                    "The easement boundary does not close (gap {0:0.000}').", gap));

            var crossings = Loops.SelfIntersections(loop, tolerance);
            if (crossings.Count > 0)
                result.Errors.Add(string.Format(CultureInfo.InvariantCulture,
                    "The easement boundary crosses itself at {0} place(s), first near {1} -- the offset is invalid for this geometry.",
                    crossings.Count, crossings[0]));

            var signed = Loops.SignedArea(loop);
            result.Area = Math.Abs(signed);
            if (result.Area <= tolerance)
                result.Errors.Add("The easement has no area.");

            // A strip should average roughly its width; much less means something was
            // trimmed away or folded over.
            var routeLength = RouteLength(route);
            if (routeLength > tolerance && result.Area > tolerance)
            {
                var averageWidth = result.Area / routeLength;
                if (averageWidth < width.Total * 0.5)
                    result.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "Average width {0:0.00}' is well under the {1:0.00}' specified -- check for slivers or a termination that cuts across the strip.",
                        averageWidth, width.Total));
                if (averageWidth > width.Total * 3.0)
                    result.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "Average width {0:0.00}' is far over the {1:0.00}' specified -- check the termination geometry.",
                        averageWidth, width.Total));
            }

            foreach (var c in loop.Where(c => c.Length < tolerance * 10))
                result.Warnings.Add("A very short boundary course (" + c.Length.ToString("0.000", CultureInfo.InvariantCulture) + "') -- possible sliver.");
        }
    }
}
