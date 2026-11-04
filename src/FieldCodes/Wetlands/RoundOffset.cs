using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Wetlands
{
    /// <summary>One continuous line of a buffer: its courses in order, and whether it closes.</summary>
    public sealed class BufferPath
    {
        public BufferPath(IEnumerable<Course> courses, bool closed)
        {
            Courses = courses.ToList();
            Closed = closed;
        }

        public List<Course> Courses { get; private set; }
        public bool Closed { get; private set; }
        public double Length { get { return Courses.Sum(c => c.Length); } }
    }

    /// <summary>
    /// Offsets of lines and arcs with round, tangent corners: the true buffer. Every point of
    /// the result is exactly the distance from the source and no point is closer; outside
    /// corners become arcs of that radius centred on the corner, and where a jagged line folds
    /// back on itself the loops an ordinary offset would leave are removed.
    ///
    /// How: every course is offset (an arc keeps its centre), every corner gets a circle of the
    /// offset radius, all of those are cut where they cross, and only the pieces exactly the
    /// distance from the source are kept (a piece that comes closer belongs to the inside of the
    /// buffer). The kept pieces are joined end to end. Arcs stay arcs; nothing is chorded.
    /// Coordinates are moved near the origin while working, so state plane values keep their
    /// precision.
    /// </summary>
    public static class RoundOffset
    {
        public const double DefaultTolerance = 1e-6;

        /// <summary>The outline of everything within <paramref name="distance"/> outside a closed
        /// loop. Usually one loop; a wetland that wraps around a pocket narrower than twice the
        /// distance gives an inner loop too.</summary>
        public static List<BufferPath> Outside(IList<Course> loop, double distance, double tolerance = DefaultTolerance)
        {
            Check(loop, distance);
            var frame = Frame.Of(loop);
            var src = frame.In(loop);
            if (Loops.SignedArea(src) < 0) src = Reversed(src);         // counter-clockwise: outside is on the right
            var minimum = distance - Slack(distance, tolerance);
            var pieces = Split(Candidates(src, true, distance, -1, tolerance), tolerance)
                .Where(p =>
                {
                    var m = p.PointAt(p.Length / 2);
                    return DistanceTo(src, m) >= minimum && !StripTrim.Inside(src, m);
                }).ToList();
            return frame.Out(Join(pieces, true, JoinTolerance(tolerance)));
        }

        /// <summary>The outline of everything inside a closed region (one or more loops, a point
        /// is inside when an odd number of them surround it) at least <paramref name="distance"/>
        /// from its edge.</summary>
        public static List<BufferPath> Inside(IList<IList<Course>> loops, double distance, double tolerance = DefaultTolerance)
        {
            if (loops == null || loops.Count == 0) throw new ArgumentException("No loops to offset.");
            foreach (var l in loops) Check(l, distance);
            var frame = Frame.Of(loops[0]);
            var src = loops.Select(l => frame.In(l)).ToList();
            // Each loop turned so the region is on its left: outer loops counter-clockwise,
            // holes clockwise. Then inward is to the left of every loop.
            for (var i = 0; i < src.Count; i++)
            {
                var probe = src[i][0].PointAt(src[i][0].Length / 2);
                var hole = src.Where((l, j) => j != i && StripTrim.Inside(l, probe)).Count() % 2 == 1;
                var ccw = Loops.SignedArea(src[i]) > 0;
                if (ccw == hole) src[i] = Reversed(src[i]);
            }
            var all = src.SelectMany(l => l).ToList();
            var minimum = distance - Slack(distance, tolerance);
            var candidates = src.SelectMany(l => Candidates(l, true, distance, 1, tolerance)).ToList();
            var pieces = Split(candidates, tolerance)
                .Where(p =>
                {
                    var m = p.PointAt(p.Length / 2);
                    return DistanceTo(all, m) >= minimum && src.Count(l => StripTrim.Inside(l, m)) % 2 == 1;
                }).ToList();
            return frame.Out(Join(pieces, true, JoinTolerance(tolerance)));
        }

        /// <summary>
        /// An open line offset to one side (+1 left of its direction, -1 right) with round
        /// outside corners and no end caps: it starts and ends square to the line's ends, as
        /// AutoCAD's OFFSET does. Pieces that would come closer than the distance to another
        /// part of the line are removed.
        /// </summary>
        public static List<BufferPath> Side(IList<Course> path, int side, double distance, double tolerance = DefaultTolerance)
        {
            Check(path, distance);
            if (side != 1 && side != -1) throw new ArgumentException("side must be +1 (left) or -1 (right)");
            var frame = Frame.Of(path);
            var src = frame.In(path);
            var minimum = distance - Slack(distance, tolerance);
            var pieces = Split(Candidates(src, false, distance, side, tolerance), tolerance)
                .Where(p =>
                {
                    var m = p.PointAt(p.Length / 2);
                    if (DistanceTo(src, m) < minimum) return false;
                    bool cap;
                    return SideOf(src, m, out cap) == side && !cap;
                }).ToList();
            var joined = Join(pieces, false, JoinTolerance(tolerance));
            // Run each the same way as the line it follows.
            var start = src[0].Start;
            var result = joined.Select(p =>
            {
                if (p.Closed) return p;
                var head = p.Courses[0].Start;
                var tail = p.Courses[p.Courses.Count - 1].End;
                return head.DistanceTo(start) <= tail.DistanceTo(start) ? p : Reverse(p);
            }).ToList();
            return frame.Out(result);
        }

        /// <summary>The shortest distance from a point to a set of courses.</summary>
        public static double DistanceTo(IEnumerable<Course> courses, P2 p)
        {
            var best = double.MaxValue;
            foreach (var c in courses)
            {
                double along;
                var d = c.Closest(p, out along).DistanceTo(p);
                if (d < best) best = d;
            }
            return best;
        }

        // ================================================================= candidates

        /// <summary>
        /// The raw offset: every course moved to the side (+1 left, -1 right), and at every corner
        /// that turns away from that side, an arc of the offset radius round the corner from one
        /// offset to the next. A curve tighter than the distance on that side has no offset; the
        /// boundary near it is the circles round its ends, which are added whole.
        /// </summary>
        private static List<Course> Candidates(IList<Course> src, bool closed, double d, int side, double tol)
        {
            var list = new List<Course>();
            var n = src.Count;
            var circled = new List<P2>();
            for (var i = 0; i < n; i++)
            {
                string why;
                var o = src[i].Offset(side * d, out why);
                if (o != null)
                {
                    if (o.Length > tol) list.Add(o);
                    continue;
                }
                foreach (var end in new[] { src[i].Start, src[i].End })
                {
                    if (circled.Any(c => c.DistanceTo(end) <= tol)) continue;
                    circled.Add(end);
                    list.AddRange(Circle(end, d));
                }
            }
            for (var i = closed ? 0 : 1; i < n; i++)
            {
                var before = src[(i - 1 + n) % n];
                var after = src[i];
                // A whole circle there already holds the corner's arc.
                if (circled.Any(c => c.DistanceTo(after.Start) <= tol)) continue;
                var incoming = before.EndDirection;
                var outgoing = after.StartDirection;
                var turn = P2.Cross(incoming, outgoing);
                var reverses = P2.Dot(incoming, outgoing) < 0 && Math.Abs(turn) < 1e-9;
                var away = reverses || (side > 0 ? turn < -1e-12 : turn > 1e-12);
                if (!away) continue;
                var corner = after.Start;
                var from = corner + incoming.LeftNormal() * (side * d);
                var to = corner + outgoing.LeftNormal() * (side * d);
                if (from.DistanceTo(to) <= tol) continue;
                // Round the outside of the turn: clockwise when offsetting left, else anticlockwise.
                list.Add(Course.Arc(from, to, corner, side < 0));
            }
            return list;
        }

        private static IEnumerable<Course> Circle(P2 centre, double radius)
        {
            var east = centre + new P2(radius, 0);
            var west = centre + new P2(-radius, 0);
            yield return Course.Arc(east, west, centre, true);
            yield return Course.Arc(west, east, centre, true);
        }

        private static List<Course> Reversed(IList<Course> loop)
        {
            var list = loop.Select(c => c.Reversed()).ToList();
            list.Reverse();
            return list;
        }

        // ================================================================= splitting

        /// <summary>Every candidate cut where another crosses it, or where another ends on it
        /// (an offset ends exactly on its corner's circle, where they only touch).</summary>
        private static List<Course> Split(List<Course> candidates, double tol)
        {
            var n = candidates.Count;
            var boxes = candidates.Select(c => Box.Of(c, tol * 10)).ToArray();
            var cuts = new List<double>[n];
            for (var i = 0; i < n; i++) cuts[i] = new List<double>();

            for (var i = 0; i < n; i++)
                for (var j = i + 1; j < n; j++)
                {
                    if (!boxes[i].Overlaps(boxes[j])) continue;
                    var a = candidates[i];
                    var b = candidates[j];
                    foreach (var p in Intersections.Bounded(a, b, tol))
                    {
                        cuts[i].Add(Along(a, p));
                        cuts[j].Add(Along(b, p));
                    }
                    foreach (var p in new[] { b.Start, b.End })
                        if (Touches(a, p, tol)) cuts[i].Add(Along(a, p));
                    foreach (var p in new[] { a.Start, a.End })
                        if (Touches(b, p, tol)) cuts[j].Add(Along(b, p));
                }

            var pieces = new List<Course>();
            var shortest = tol * 10;
            for (var i = 0; i < n; i++)
            {
                var c = candidates[i];
                var length = c.Length;
                var stations = cuts[i].Where(s => s > shortest && s < length - shortest)
                                      .Concat(new[] { 0.0, length }).OrderBy(s => s).ToList();
                var from = stations[0];
                for (var k = 1; k < stations.Count; k++)
                {
                    if (stations[k] - from <= shortest) continue;
                    pieces.Add(c.Sub(from, stations[k]));
                    from = stations[k];
                }
            }
            return pieces;
        }

        private static double Along(Course c, P2 p)
        {
            return Math.Max(0, Math.Min(c.Length, c.DistanceOf(p)));
        }

        private static bool Touches(Course c, P2 p, double tol)
        {
            double along;
            return c.Closest(p, out along).DistanceTo(p) <= tol;
        }

        // ================================================================= sides

        /// <summary>Which side of an open line a point is on, from the part of the line nearest
        /// it: +1 left, -1 right. A point nearest a corner is on its outside (the side the line
        /// turns away from). <paramref name="endCap"/> is set when the nearest part is an end of
        /// the line: that is the round cap an open offset does not have.</summary>
        private static int SideOf(IList<Course> src, P2 m, out bool endCap)
        {
            endCap = false;
            var best = -1;
            var bestDistance = double.MaxValue;
            var bestAlong = 0.0;
            var bestPoint = new P2(0, 0);
            for (var i = 0; i < src.Count; i++)
            {
                double along;
                var q = src[i].Closest(m, out along);
                var d = q.DistanceTo(m);
                if (d < bestDistance - 1e-12)
                {
                    best = i;
                    bestDistance = d;
                    bestAlong = along;
                    bestPoint = q;
                }
            }
            var c = src[best];
            const double atEnd = 1e-9;
            if (bestAlong > atEnd && bestAlong < c.Length - atEnd)
                return Math.Sign(P2.Cross(c.DirectionAt(bestAlong), m - bestPoint));

            // At a corner (or an end).
            var corner = bestAlong <= atEnd ? best : best + 1;
            if (corner == 0 || corner == src.Count)
            {
                endCap = true;
                return 0;
            }
            var incoming = src[corner - 1].EndDirection;
            var outgoing = src[corner].StartDirection;
            var turn = P2.Cross(incoming, outgoing);
            if (Math.Abs(turn) < 1e-9) return Math.Sign(P2.Cross(incoming, m - bestPoint));
            return turn > 0 ? -1 : 1;
        }

        // ================================================================= joining

        /// <summary>
        /// Joins pieces end to end into paths. An open path is walked from one of its free ends;
        /// whatever remains forms closed loops. Consecutive pieces of one line or one circle are
        /// merged back, so the polyline has a vertex only where the buffer really turns.
        /// </summary>
        private static List<BufferPath> Join(List<Course> pieces, bool expectClosed, double tol)
        {
            var unused = Distinct(pieces, tol);
            var paths = new List<BufferPath>();
            while (unused.Count > 0)
            {
                // Start at a free end if there is one, so an open path is walked whole.
                var first = -1;
                var flip = false;
                for (var i = 0; i < unused.Count && first < 0; i++)
                {
                    if (Degree(unused, unused[i].Start, tol) == 1) { first = i; flip = false; }
                    else if (Degree(unused, unused[i].End, tol) == 1) { first = i; flip = true; }
                }
                if (first < 0) first = 0;
                var path = new List<Course> { flip ? unused[first].Reversed() : unused[first] };
                unused.RemoveAt(first);

                while (true)
                {
                    var tail = path[path.Count - 1].End;
                    if (path.Count > 1 && tail.DistanceTo(path[0].Start) <= tol) break;
                    var next = -1;
                    var nextFlip = false;
                    var nearest = double.MaxValue;
                    for (var i = 0; i < unused.Count; i++)
                    {
                        var ds = unused[i].Start.DistanceTo(tail);
                        var de = unused[i].End.DistanceTo(tail);
                        if (ds <= tol && ds < nearest) { next = i; nextFlip = false; nearest = ds; }
                        if (de <= tol && de < nearest) { next = i; nextFlip = true; nearest = de; }
                    }
                    if (next < 0) break;
                    path.Add(nextFlip ? unused[next].Reversed() : unused[next]);
                    unused.RemoveAt(next);
                }

                var closed = path.Count > 1 && path[path.Count - 1].End.DistanceTo(path[0].Start) <= tol ||
                             path.Count == 1 && path[0].Kind == CourseKind.Arc && path[0].Start.DistanceTo(path[0].End) <= tol;
                paths.Add(new BufferPath(Merge(path, closed), closed));
            }
            return paths;
        }

        /// <summary>The same piece twice (two candidates on one circle) is kept once.</summary>
        private static List<Course> Distinct(List<Course> pieces, double tol)
        {
            var result = new List<Course>();
            foreach (var c in pieces)
            {
                var mid = c.PointAt(c.Length / 2);
                var twin = result.Any(o => o.Kind == c.Kind && Math.Abs(o.Length - c.Length) <= tol &&
                                           o.PointAt(o.Length / 2).DistanceTo(mid) <= tol &&
                                           (o.Start.DistanceTo(c.Start) <= tol && o.End.DistanceTo(c.End) <= tol ||
                                            o.Start.DistanceTo(c.End) <= tol && o.End.DistanceTo(c.Start) <= tol));
                if (!twin) result.Add(c);
            }
            return result;
        }

        private static int Degree(List<Course> pieces, P2 p, double tol)
        {
            var n = 0;
            foreach (var c in pieces)
            {
                if (c.Start.DistanceTo(p) <= tol) n++;
                if (c.End.DistanceTo(p) <= tol) n++;
            }
            return n;
        }

        /// <summary>Consecutive courses on one line or one circle become one course.</summary>
        internal static List<Course> Merge(List<Course> path, bool closed)
        {
            var result = new List<Course>();
            foreach (var c in path)
            {
                if (result.Count > 0)
                {
                    var merged = Combine(result[result.Count - 1], c);
                    if (merged != null) { result[result.Count - 1] = merged; continue; }
                }
                result.Add(c);
            }
            if (closed && result.Count > 2)
            {
                var wrap = Combine(result[result.Count - 1], result[0]);
                if (wrap != null)
                {
                    result[0] = wrap;
                    result.RemoveAt(result.Count - 1);
                }
            }
            return result;
        }

        private static Course Combine(Course a, Course b)
        {
            if (a.Kind != b.Kind) return null;
            if (a.Kind == CourseKind.Line)
            {
                var da = a.End - a.Start;
                var db = b.End - b.Start;
                if (P2.Dot(da, db) <= 0) return null;
                var len = da.Length * db.Length;
                if (len <= 0 || Math.Abs(P2.Cross(da, db)) > 1e-9 * len) return null;
                // The joint must lie on the combined line, not merely be parallel to it.
                var joined = Course.Line(a.Start, b.End);
                double along;
                return joined.Closest(a.End, out along).DistanceTo(a.End) <= 1e-7 ? joined : null;
            }
            if (a.CounterClockwise != b.CounterClockwise) return null;
            if (a.Center.DistanceTo(b.Center) > 1e-7 * Math.Max(1, a.Radius)) return null;
            if (Math.Abs(a.Radius - b.Radius) > 1e-7 * Math.Max(1, a.Radius)) return null;
            if (a.Sweep + b.Sweep >= 2 * Math.PI - 1e-6) return null;
            return Course.Arc(a.Start, b.End, a.Center, a.CounterClockwise);
        }

        private static BufferPath Reverse(BufferPath p)
        {
            var reversed = new List<Course>(p.Courses);
            reversed.Reverse();
            return new BufferPath(reversed.Select(c => c.Reversed()), p.Closed);
        }

        // ================================================================= small helpers

        private static void Check(IList<Course> courses, double distance)
        {
            if (courses == null || courses.Count == 0) throw new ArgumentException("Nothing to offset.");
            if (!(distance > 0)) throw new ArgumentException("The offset distance must be greater than zero.");
        }

        /// <summary>How much closer than the distance a kept piece may measure: round-off only.</summary>
        private static double Slack(double distance, double tol)
        {
            return Math.Max(tol, distance * 1e-7);
        }

        private static double JoinTolerance(double tol)
        {
            return Math.Max(tol * 100, 1e-5);
        }
    }

    /// <summary>An axis-aligned box, for skipping pairs that cannot meet.</summary>
    internal struct Box
    {
        public double MinX, MinY, MaxX, MaxY;

        public static Box Of(Course c, double pad)
        {
            if (c.Kind == CourseKind.Arc)
            {
                // The whole circle: never too small, cheap.
                return new Box
                {
                    MinX = c.Center.X - c.Radius - pad, MaxX = c.Center.X + c.Radius + pad,
                    MinY = c.Center.Y - c.Radius - pad, MaxY = c.Center.Y + c.Radius + pad
                };
            }
            return new Box
            {
                MinX = Math.Min(c.Start.X, c.End.X) - pad, MaxX = Math.Max(c.Start.X, c.End.X) + pad,
                MinY = Math.Min(c.Start.Y, c.End.Y) - pad, MaxY = Math.Max(c.Start.Y, c.End.Y) + pad
            };
        }

        public static Box Of(IEnumerable<Course> courses, double pad)
        {
            var b = new Box { MinX = double.MaxValue, MinY = double.MaxValue, MaxX = double.MinValue, MaxY = double.MinValue };
            foreach (var c in courses)
            {
                var o = Of(c, pad);
                b.MinX = Math.Min(b.MinX, o.MinX);
                b.MinY = Math.Min(b.MinY, o.MinY);
                b.MaxX = Math.Max(b.MaxX, o.MaxX);
                b.MaxY = Math.Max(b.MaxY, o.MaxY);
            }
            return b;
        }

        public bool Overlaps(Box o)
        {
            return MinX <= o.MaxX && o.MinX <= MaxX && MinY <= o.MaxY && o.MinY <= MaxY;
        }
    }

    /// <summary>Moves geometry near the origin and back, so state plane coordinates (millions
    /// of feet) do not cost the intersection arithmetic its precision.</summary>
    internal sealed class Frame
    {
        private readonly P2 _origin;

        private Frame(P2 origin) { _origin = origin; }

        public static Frame Of(IEnumerable<Course> courses)
        {
            var first = courses.FirstOrDefault();
            return new Frame(first == null ? new P2(0, 0) : first.Start);
        }

        public P2 Origin { get { return _origin; } }

        public List<Course> In(IEnumerable<Course> courses)
        {
            var by = _origin * -1;
            return courses.Select(c => Move(c, by)).ToList();
        }

        public List<BufferPath> Out(IEnumerable<BufferPath> paths)
        {
            return paths.Select(p => new BufferPath(p.Courses.Select(c => Move(c, _origin)), p.Closed)).ToList();
        }

        public Course Out(Course c) { return Move(c, _origin); }

        public P2 Out(P2 p) { return p + _origin; }

        public static Course Move(Course c, P2 by)
        {
            return c.Kind == CourseKind.Line
                ? Course.Line(c.Start + by, c.End + by)
                : Course.Arc(c.Start + by, c.End + by, c.Center + by, c.CounterClockwise);
        }
    }
}
