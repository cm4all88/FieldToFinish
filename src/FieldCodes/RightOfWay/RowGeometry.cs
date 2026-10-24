using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Row
{
    /// <summary>
    /// The plane geometry behind FTFROW: offsetting a chain of straight courses to either
    /// side with exact joins, and rounding a ring's corners with tangent arcs. Pure.
    /// </summary>
    internal static class RowGeometry
    {
        public const double Eps = 1e-9;

        /// <summary>Joins that would run out beyond this many offset distances fall back to a
        /// bevel, and the drafter is told. A near-reversal otherwise throws a spike.</summary>
        public const double MiterLimit = 8.0;

        public static double SignedArea(IList<P2> pts)
        {
            double sum = 0;
            for (var i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2.0;
        }

        public static bool TryIntersect(P2 p1, P2 d1, P2 p2, P2 d2, out P2 point)
        {
            var cross = P2.Cross(d1, d2);
            if (Math.Abs(cross) < Eps)
            {
                point = new P2(0, 0);
                return false;
            }
            var t = P2.Cross(p2 - p1, d2) / cross;
            point = p1 + d1 * t;
            return true;
        }

        /// <summary>The unit direction of travel, azimuth-free: a to b.</summary>
        public static P2 Dir(P2 a, P2 b)
        {
            return (b - a).Normalized();
        }

        // ------------------------------------------------------------------ offsets

        /// <summary>One offset course: where it starts and ends once joined to its neighbours.</summary>
        public sealed class OffsetResult
        {
            /// <summary>Per original course, the start of its offset.</summary>
            public P2[] Starts;
            public P2[] Ends;
            public P2[] Directions;
            public P2[] Normals;

            /// <summary>The corners the offset was made from, in order.</summary>
            public P2[] Corners;
            public double[] Distances;
            public bool Closed;
            public int Courses { get { return Starts.Length; } }

            /// <summary>The pieces to draw in order: each offset course, and the short connector
            /// where two joined offsets did not meet at one point.</summary>
            public List<KeyValuePair<int, Course>> Segments = new List<KeyValuePair<int, Course>>();
        }

        /// <summary>
        /// Offsets a chain of corners. <paramref name="distances"/> has one entry per course,
        /// positive to the LEFT of the direction of travel, and may differ course to course.
        /// Neighbouring offsets meet exactly where their lines cross; where they cannot (the
        /// courses are collinear but offset differently, or the corner is so sharp the join
        /// would run away) a short connector is drawn between them and a warning is added.
        /// </summary>
        public static OffsetResult OffsetChain(IList<P2> corners, bool closed, IList<double> distances,
                                               ICollection<string> warnings, string what)
        {
            var n = corners.Count;
            var courses = closed ? n : n - 1;
            var result = new OffsetResult
            {
                Closed = closed,
                Corners = corners.ToArray(),
                Starts = new P2[courses],
                Ends = new P2[courses],
                Directions = new P2[courses],
                Normals = new P2[courses],
                Distances = new double[courses]
            };

            for (var i = 0; i < courses; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % n];
                result.Directions[i] = Dir(a, b);
                result.Normals[i] = result.Directions[i].LeftNormal();
                result.Distances[i] = distances[i];
            }

            if (!closed)
            {
                result.Starts[0] = corners[0] + result.Normals[0] * result.Distances[0];
                result.Ends[courses - 1] = corners[n - 1] + result.Normals[courses - 1] * result.Distances[courses - 1];
            }

            var firstJoin = closed ? 0 : 1;
            var lastJoin = closed ? n - 1 : n - 2;
            for (var k = firstJoin; k <= lastJoin; k++)
            {
                var prev = (k - 1 + courses) % courses;
                var next = k % courses;
                var corner = corners[k];
                var onPrev = corner + result.Normals[prev] * result.Distances[prev];
                var onNext = corner + result.Normals[next] * result.Distances[next];

                P2 joined;
                var cross = P2.Cross(result.Directions[prev], result.Directions[next]);
                if (Math.Abs(cross) < 1e-9)
                {
                    if (Math.Abs(result.Distances[prev] - result.Distances[next]) < 1e-9)
                    {
                        result.Ends[prev] = onPrev;
                        result.Starts[next] = onPrev;
                        continue;
                    }
                    warnings.Add(what + ": courses " + (prev + 1) + " and " + (next + 1) +
                                 " are in line but offset by different amounts; a short step joins them.");
                    result.Ends[prev] = onPrev;
                    result.Starts[next] = onNext;
                    continue;
                }

                TryIntersect(onPrev, result.Directions[prev], onNext, result.Directions[next], out joined);
                var reach = joined.DistanceTo(corner);
                var limit = MiterLimit * Math.Max(Math.Abs(result.Distances[prev]), Math.Abs(result.Distances[next]));
                if (reach > limit)
                {
                    warnings.Add(what + ": the corner at monument " + (k + 1) +
                                 " is too sharp to join the offset lines; a bevel is drawn instead.");
                    result.Ends[prev] = onPrev;
                    result.Starts[next] = onNext;
                    continue;
                }

                result.Ends[prev] = joined;
                result.Starts[next] = joined;
            }

            for (var i = 0; i < courses; i++)
            {
                var along = P2.Dot(result.Ends[i] - result.Starts[i], result.Directions[i]);
                if (along <= Eps)
                    warnings.Add(what + ": course " + (i + 1) + " is too short for its width; its offset collapses.");
            }

            for (var i = 0; i < courses; i++)
            {
                if (P2.Dot(result.Ends[i] - result.Starts[i], result.Directions[i]) > Eps)
                    result.Segments.Add(new KeyValuePair<int, Course>(i, Course.Line(result.Starts[i], result.Ends[i])));

                var hasNext = closed || i < courses - 1;
                if (!hasNext) continue;
                var following = (i + 1) % courses;
                if (result.Ends[i].DistanceTo(result.Starts[following]) > 1e-6)
                    result.Segments.Add(new KeyValuePair<int, Course>(-1, Course.Line(result.Ends[i], result.Starts[following])));
            }

            return result;
        }

        // ------------------------------------------------------------------ corners

        /// <summary>One piece of a ring after its corners are rounded.</summary>
        public sealed class RingPiece
        {
            public Course Course;

            /// <summary>The ring side this is, or the corner an arc rounds.</summary>
            public int Index;
            public bool IsArc;
        }

        /// <summary>
        /// A closed ring of corners with tangent arcs at the corners that have a radius.
        /// A radius that does not fit between its neighbours is dropped, with a warning,
        /// and the corner stays sharp.
        /// </summary>
        public static List<RingPiece> RoundRing(IList<P2> corners, IList<double> radii, ICollection<string> warnings)
        {
            var n = corners.Count;
            var dirs = new P2[n];
            var lengths = new double[n];
            for (var i = 0; i < n; i++)
            {
                dirs[i] = Dir(corners[i], corners[(i + 1) % n]);
                lengths[i] = corners[i].DistanceTo(corners[(i + 1) % n]);
            }

            var tangent = new double[n];
            var deflection = new double[n];
            var leftTurn = new bool[n];
            for (var k = 0; k < n; k++)
            {
                var dIn = dirs[(k - 1 + n) % n];
                var dOut = dirs[k];
                var cross = P2.Cross(dIn, dOut);
                var dot = P2.Dot(dIn, dOut);
                deflection[k] = Math.Atan2(Math.Abs(cross), dot);
                leftTurn[k] = cross > 0;
                var r = radii != null && k < radii.Count ? radii[k] : 0.0;
                if (r > Eps && deflection[k] > 1e-6 && deflection[k] < Math.PI - 1e-6)
                    tangent[k] = r * Math.Tan(deflection[k] / 2.0);
            }

            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                if (tangent[i] + tangent[j] <= lengths[i] + 1e-6) continue;
                warnings.Add("The corner radii at monuments " + (i + 1) + " and " + (j + 1) +
                             " do not fit on the side between them; those corners are left sharp.");
                tangent[i] = 0;
                tangent[j] = 0;
            }

            var pieces = new List<RingPiece>();
            for (var i = 0; i < n; i++)
            {
                if (tangent[i] > 0)
                {
                    var dIn = dirs[(i - 1 + n) % n];
                    var r = radii[i];
                    var start = corners[i] - dIn * tangent[i];
                    var end = corners[i] + dirs[i] * tangent[i];
                    var center = leftTurn[i] ? start + dIn.LeftNormal() * r : start - dIn.LeftNormal() * r;
                    pieces.Add(new RingPiece { Course = Course.Arc(start, end, center, leftTurn[i]), Index = i, IsArc = true });
                }

                var lineStart = corners[i] + dirs[i] * tangent[i];
                var lineEnd = corners[(i + 1) % n] - dirs[i] * tangent[(i + 1) % n];
                if (P2.Dot(lineEnd - lineStart, dirs[i]) > Eps)
                    pieces.Add(new RingPiece { Course = Course.Line(lineStart, lineEnd), Index = i, IsArc = false });
            }
            return pieces;
        }
    }
}
