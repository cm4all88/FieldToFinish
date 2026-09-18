using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Easements
{
    /// <summary>A plan point, easting X and northing Y.</summary>
    public struct P2
    {
        public readonly double X;
        public readonly double Y;

        [JsonConstructor]
        public P2(double x, double y) { X = x; Y = y; }

        public static P2 operator +(P2 a, P2 b) { return new P2(a.X + b.X, a.Y + b.Y); }
        public static P2 operator -(P2 a, P2 b) { return new P2(a.X - b.X, a.Y - b.Y); }
        public static P2 operator *(P2 a, double k) { return new P2(a.X * k, a.Y * k); }

        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }
        public double DistanceTo(P2 o) { return (this - o).Length; }
        public P2 Normalized() { var l = Length; return l < 1e-15 ? new P2(0, 0) : new P2(X / l, Y / l); }

        /// <summary>The unit normal to the LEFT of this direction.</summary>
        public P2 LeftNormal() { var n = Normalized(); return new P2(-n.Y, n.X); }

        public static double Dot(P2 a, P2 b) { return a.X * b.X + a.Y * b.Y; }
        public static double Cross(P2 a, P2 b) { return a.X * b.Y - a.Y * b.X; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###})", X, Y);
        }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum CourseKind { Line, Arc }

    /// <summary>
    /// One course of survey geometry: a straight line or a circular arc, kept
    /// exact. Arcs stay arcs through offsetting, trimming and area -- nothing is
    /// chorded into short lines.
    /// </summary>
    public sealed class Course
    {
        [JsonProperty("kind")] public CourseKind Kind { get; private set; }
        [JsonProperty("start")] public P2 Start { get; private set; }
        [JsonProperty("end")] public P2 End { get; private set; }
        [JsonProperty("center")] public P2 Center { get; private set; }
        [JsonProperty("radius")] public double Radius { get; private set; }

        /// <summary>For an arc: true when it turns counter-clockwise (to the left).</summary>
        [JsonProperty("ccw")] public bool CounterClockwise { get; private set; }

        [JsonConstructor]
        private Course() { }

        public static Course Line(P2 start, P2 end)
        {
            return new Course { Kind = CourseKind.Line, Start = start, End = end };
        }

        public static Course Arc(P2 start, P2 end, P2 center, bool counterClockwise)
        {
            return new Course
            {
                Kind = CourseKind.Arc, Start = start, End = end, Center = center,
                Radius = (start - center).Length, CounterClockwise = counterClockwise
            };
        }

        /// <summary>An arc from a polyline bulge (tan of a quarter of the sweep;
        /// positive turns left).</summary>
        public static Course FromBulge(P2 start, P2 end, double bulge)
        {
            if (Math.Abs(bulge) < 1e-12) return Line(start, end);

            var chord = end - start;
            var c = chord.Length;
            var sweep = 4.0 * Math.Atan(Math.Abs(bulge));
            var radius = c / (2.0 * Math.Sin(sweep / 2.0));
            var sagittaSide = Math.Sqrt(Math.Max(0, radius * radius - c * c / 4.0));
            var mid = (start + end) * 0.5;
            var left = chord.LeftNormal();

            // Bulge > 0 turns left: the center is on the left of the chord for a
            // minor arc and on the right for a major one.
            var toCenter = sweep <= Math.PI ? sagittaSide : -sagittaSide;
            var center = bulge > 0 ? mid + left * toCenter : mid - left * toCenter;
            return Arc(start, end, center, bulge > 0);
        }

        // --------------------------------------------------------------- angles

        [JsonIgnore] public double StartAngle { get { return Math.Atan2(Start.Y - Center.Y, Start.X - Center.X); } }
        [JsonIgnore] public double EndAngle { get { return Math.Atan2(End.Y - Center.Y, End.X - Center.X); } }

        /// <summary>The arc's sweep, 0 to 2π, in its own turning direction.</summary>
        [JsonIgnore]
        public double Sweep
        {
            get
            {
                if (Kind == CourseKind.Line) return 0;
                var s = CounterClockwise ? EndAngle - StartAngle : StartAngle - EndAngle;
                s %= 2 * Math.PI;
                if (s < 0) s += 2 * Math.PI;
                if (s < 1e-12 && Start.DistanceTo(End) < 1e-9) s = 2 * Math.PI;
                return s;
            }
        }

        [JsonIgnore]
        public double Length
        {
            get { return Kind == CourseKind.Line ? Start.DistanceTo(End) : Radius * Sweep; }
        }

        /// <summary>The unit direction of travel at the start.</summary>
        [JsonIgnore]
        public P2 StartDirection
        {
            get
            {
                if (Kind == CourseKind.Line) return (End - Start).Normalized();
                var radial = (Start - Center).Normalized();
                return CounterClockwise ? new P2(-radial.Y, radial.X) : new P2(radial.Y, -radial.X);
            }
        }

        [JsonIgnore]
        public P2 EndDirection
        {
            get
            {
                if (Kind == CourseKind.Line) return (End - Start).Normalized();
                var radial = (End - Center).Normalized();
                return CounterClockwise ? new P2(-radial.Y, radial.X) : new P2(radial.Y, -radial.X);
            }
        }

        public P2 PointAt(double distance)
        {
            if (Kind == CourseKind.Line)
            {
                var len = Length;
                return len < 1e-12 ? Start : Start + (End - Start) * (distance / len);
            }
            var angle = StartAngle + (CounterClockwise ? 1 : -1) * distance / Radius;
            return new P2(Center.X + Radius * Math.Cos(angle), Center.Y + Radius * Math.Sin(angle));
        }

        public P2 DirectionAt(double distance)
        {
            if (Kind == CourseKind.Line) return StartDirection;
            var p = PointAt(distance);
            var radial = (p - Center).Normalized();
            return CounterClockwise ? new P2(-radial.Y, radial.X) : new P2(radial.Y, -radial.X);
        }

        public Course Reversed()
        {
            return Kind == CourseKind.Line ? Line(End, Start) : Arc(End, Start, Center, !CounterClockwise);
        }

        public Course WithEnds(P2 start, P2 end)
        {
            return Kind == CourseKind.Line ? Line(start, end) : Arc(start, end, Center, CounterClockwise);
        }

        /// <summary>The part of this course between two distances along it.</summary>
        public Course Sub(double from, double to)
        {
            return WithEnds(PointAt(from), PointAt(to));
        }

        /// <summary>
        /// This course moved sideways: positive to the left of travel. An arc keeps its
        /// center; its radius shrinks on the inside of the turn. Returns null and a
        /// reason when the offset turns the arc inside out -- a curve tighter than
        /// the width.
        /// </summary>
        public Course Offset(double left, out string failure)
        {
            failure = null;
            if (Math.Abs(left) < 1e-12) return this;

            if (Kind == CourseKind.Line)
            {
                var n = (End - Start).LeftNormal() * left;
                return Line(Start + n, End + n);
            }

            // Counter-clockwise: the center is on the left, so moving left shrinks.
            var newRadius = CounterClockwise ? Radius - left : Radius + left;
            if (newRadius <= 1e-6)
            {
                failure = string.Format(CultureInfo.InvariantCulture,
                    "curve radius {0:0.00}' is too tight for a {1:0.00}' offset on its inside",
                    Radius, Math.Abs(left));
                return null;
            }

            var k = newRadius / Radius;
            return Arc(Center + (Start - Center) * k, Center + (End - Center) * k, Center, CounterClockwise);
        }

        /// <summary>Distance along the course of a point known to lie on its carrier,
        /// measured from the start; negative before the start, beyond Length after.</summary>
        public double DistanceOf(P2 p)
        {
            if (Kind == CourseKind.Line)
            {
                var d = End - Start;
                var len = d.Length;
                return len < 1e-12 ? 0 : P2.Dot(p - Start, d) / len;
            }

            var a = Math.Atan2(p.Y - Center.Y, p.X - Center.X);
            var delta = CounterClockwise ? a - StartAngle : StartAngle - a;
            delta %= 2 * Math.PI;
            if (delta < 0) delta += 2 * Math.PI;

            // Past the end but closer to the start going backward: report negative.
            var sweep = Sweep;
            if (delta > sweep && delta - sweep > 2 * Math.PI - delta) delta -= 2 * Math.PI;
            return delta * Radius;
        }

        /// <summary>The nearest point on the course (not its carrier) to p.</summary>
        public P2 Closest(P2 p, out double distanceAlong)
        {
            var along = DistanceOf(p);
            if (Kind == CourseKind.Line)
            {
                distanceAlong = Math.Max(0, Math.Min(Length, along));
                return PointAt(distanceAlong);
            }

            if (along >= 0 && along <= Length)
            {
                distanceAlong = along;
                return PointAt(along);
            }
            distanceAlong = p.DistanceTo(Start) <= p.DistanceTo(End) ? 0 : Length;
            return distanceAlong == 0 ? Start : End;
        }

        public override string ToString()
        {
            return Kind == CourseKind.Line
                ? "Line " + Start + " -> " + End
                : string.Format(CultureInfo.InvariantCulture, "Arc {0} -> {1} R={2:0.###} {3}", Start, End, Radius,
                                CounterClockwise ? "CCW" : "CW");
        }
    }

    /// <summary>Intersections between course carriers (infinite lines, full circles)
    /// and helpers to keep only those on the bounded courses.</summary>
    public static class Intersections
    {
        public const double Tolerance = 1e-6;

        public static IList<P2> Carriers(Course a, Course b)
        {
            if (a.Kind == CourseKind.Line && b.Kind == CourseKind.Line) return LineLine(a.Start, a.End, b.Start, b.End);
            if (a.Kind == CourseKind.Line) return LineCircle(a.Start, a.End, b.Center, b.Radius);
            if (b.Kind == CourseKind.Line) return LineCircle(b.Start, b.End, a.Center, a.Radius);
            return CircleCircle(a.Center, a.Radius, b.Center, b.Radius);
        }

        /// <summary>Intersections lying on both bounded courses.</summary>
        public static IList<P2> Bounded(Course a, Course b, double tolerance)
        {
            return Carriers(a, b).Where(p => OnCourse(a, p, tolerance) && OnCourse(b, p, tolerance)).ToList();
        }

        public static bool OnCourse(Course c, P2 p, double tolerance)
        {
            var along = c.DistanceOf(p);
            return along >= -tolerance && along <= c.Length + tolerance;
        }

        public static IList<P2> LineLine(P2 a0, P2 a1, P2 b0, P2 b1)
        {
            var r = a1 - a0;
            var s = b1 - b0;
            var denom = P2.Cross(r, s);
            if (Math.Abs(denom) < 1e-12 * Math.Max(1, r.Length * s.Length)) return new P2[0];
            var t = P2.Cross(b0 - a0, s) / denom;
            return new[] { a0 + r * t };
        }

        public static IList<P2> LineCircle(P2 a0, P2 a1, P2 center, double radius)
        {
            var d = a1 - a0;
            var f = a0 - center;
            var a = P2.Dot(d, d);
            if (a < 1e-24) return new P2[0];
            var b = 2 * P2.Dot(f, d);
            var c = P2.Dot(f, f) - radius * radius;
            var disc = b * b - 4 * a * c;
            if (disc < -1e-9 * Math.Max(1, radius * radius)) return new P2[0];
            if (disc < 0) disc = 0;
            var sq = Math.Sqrt(disc);
            var t1 = (-b - sq) / (2 * a);
            var t2 = (-b + sq) / (2 * a);
            return Math.Abs(t1 - t2) < 1e-12 ? new[] { a0 + d * t1 } : new[] { a0 + d * t1, a0 + d * t2 };
        }

        public static IList<P2> CircleCircle(P2 c0, double r0, P2 c1, double r1)
        {
            var d = c0.DistanceTo(c1);
            if (d < 1e-12 || d > r0 + r1 + 1e-9 || d < Math.Abs(r0 - r1) - 1e-9) return new P2[0];
            var a = (r0 * r0 - r1 * r1 + d * d) / (2 * d);
            var h = Math.Sqrt(Math.Max(0, r0 * r0 - a * a));
            var dir = (c1 - c0) * (1 / d);
            var mid = c0 + dir * a;
            var perp = new P2(-dir.Y, dir.X);
            return h < 1e-12 ? new[] { mid } : new[] { mid + perp * h, mid - perp * h };
        }
    }

    /// <summary>Area, closure and chaining over a closed loop of courses.</summary>
    public static class Loops
    {
        /// <summary>Signed area by Green's theorem, exact for arcs: positive when the
        /// loop runs counter-clockwise.</summary>
        public static double SignedArea(IList<Course> loop)
        {
            if (loop == null || loop.Count == 0) return 0;
            // Worked about a point on the loop, not the drawing origin: at state plane coordinates (a million
            // feet and more) a joint that meets to within a few hundred-thousandths of a foot would otherwise
            // add whole square feet (the gap times the distance from the origin).
            var o = loop[0].Start;
            var twice = 0.0;
            foreach (var c in loop)
            {
                if (c.Kind == CourseKind.Line)
                {
                    twice += (c.Start.X - o.X) * (c.End.Y - o.Y) - (c.End.X - o.X) * (c.Start.Y - o.Y);
                    continue;
                }

                var a = c.StartAngle;
                var sweep = c.Sweep;
                var b = c.CounterClockwise ? a + sweep : a - sweep;
                var r = c.Radius;
                twice += r * r * (b - a) +
                         (c.Center.X - o.X) * r * (Math.Sin(b) - Math.Sin(a)) -
                         (c.Center.Y - o.Y) * r * (Math.Cos(b) - Math.Cos(a));
            }
            return twice / 2.0;
        }

        public static double Perimeter(IList<Course> loop)
        {
            return loop.Sum(c => c.Length);
        }

        /// <summary>The largest distance between one course's end and the next one's
        /// start, including the wrap back to the first.</summary>
        public static double LargestGap(IList<Course> loop)
        {
            if (loop.Count == 0) return 0;
            var worst = 0.0;
            for (var i = 0; i < loop.Count; i++)
                worst = Math.Max(worst, loop[i].End.DistanceTo(loop[(i + 1) % loop.Count].Start));
            return worst;
        }

        /// <summary>
        /// Every place the loop crosses itself, excluding the shared corners of
        /// neighbouring courses.
        /// </summary>
        public static IList<P2> SelfIntersections(IList<Course> loop, double tolerance)
        {
            var hits = new List<P2>();
            var n = loop.Count;
            for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                var adjacentForward = j == i + 1;
                var adjacentWrap = i == 0 && j == n - 1;
                foreach (var p in Intersections.Bounded(loop[i], loop[j], tolerance))
                {
                    if (adjacentForward && p.DistanceTo(loop[i].End) < tolerance * 10) continue;
                    if (adjacentWrap && p.DistanceTo(loop[i].Start) < tolerance * 10) continue;
                    if (n == 2 && (p.DistanceTo(loop[i].Start) < tolerance * 10 || p.DistanceTo(loop[i].End) < tolerance * 10))
                        continue;
                    hits.Add(p);
                }
            }
            return hits;
        }

        /// <summary>Orders loose curves (e.g. an exploded region) into closed loops,
        /// flipping courses as needed. Curves that do not close are returned as an
        /// open remainder.</summary>
        public static IList<IList<Course>> Chain(IEnumerable<Course> courses, double tolerance,
                                                 out IList<Course> unchained)
        {
            var pool = courses.Where(c => c.Length > tolerance).ToList();
            var loops = new List<IList<Course>>();

            while (pool.Count > 0)
            {
                var loop = new List<Course> { pool[0] };
                pool.RemoveAt(0);
                var progressed = true;

                while (progressed && loop[loop.Count - 1].End.DistanceTo(loop[0].Start) > tolerance)
                {
                    progressed = false;
                    var tail = loop[loop.Count - 1].End;
                    for (var i = 0; i < pool.Count; i++)
                    {
                        if (pool[i].Start.DistanceTo(tail) <= tolerance) { loop.Add(pool[i]); pool.RemoveAt(i); progressed = true; break; }
                        if (pool[i].End.DistanceTo(tail) <= tolerance) { loop.Add(pool[i].Reversed()); pool.RemoveAt(i); progressed = true; break; }
                    }
                }

                if (loop[loop.Count - 1].End.DistanceTo(loop[0].Start) <= tolerance)
                    loops.Add(loop);
                else
                {
                    pool.InsertRange(0, loop.Skip(1));
                    unchained = new List<Course> { loop[0] }.Concat(pool).ToList();
                    return loops;
                }
            }

            unchained = new List<Course>();
            return loops;
        }
    }
}
