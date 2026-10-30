using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Contours
{
    /// <summary>A TIN vertex: plan position and elevation.</summary>
    public struct TinPoint
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public TinPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>One TIN triangle, as the surface reports it.</summary>
    public struct TinTriangle
    {
        public readonly TinPoint A;
        public readonly TinPoint B;
        public readonly TinPoint C;

        public TinTriangle(TinPoint a, TinPoint b, TinPoint c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    /// <summary>
    /// Traces the contour at one elevation straight off TIN triangles: the exact line where
    /// the plane z = elevation cuts each triangle, joined across shared edges into continuous
    /// pieces. Every point lies on a triangle edge at the linearly interpolated position --
    /// the surface's own geometry, never an interpolation between displayed contours.
    ///
    /// FTFCONTOUR asks Civil 3D for the contour first (TinSurface.ExtractContoursAt); this is
    /// the independent fallback when that call fails, and the reference the unit tests hold
    /// contour geometry to. The triangles are only read.
    ///
    /// A vertex exactly at the elevation counts as above it. That one rule removes every
    /// degenerate case: a triangle is either cut along exactly two edges or not at all, so a
    /// contour through a vertex, along an edge, or round a flat at exactly that elevation
    /// still comes out as clean continuous lines with no double-backs.
    /// </summary>
    public static class ContourTracer
    {
        public static IList<ContourPath> Trace(IEnumerable<TinTriangle> triangles, double elevation)
        {
            var vertexIndex = new Dictionary<VertexKey, int>();
            var heights = new List<double>();
            var positions = new List<P2>();

            Func<TinPoint, int> index = p =>
            {
                var key = new VertexKey(p.X, p.Y);
                int found;
                if (vertexIndex.TryGetValue(key, out found)) return found;
                found = positions.Count;
                vertexIndex.Add(key, found);
                positions.Add(new P2(p.X, p.Y));
                heights.Add(p.Z);
                return found;
            };

            // Each cut triangle contributes one segment between two edge crossings.
            var segments = new List<Segment>();
            foreach (var t in triangles ?? Enumerable.Empty<TinTriangle>())
            {
                var ia = index(t.A);
                var ib = index(t.B);
                var ic = index(t.C);
                if (ia == ib || ib == ic || ia == ic) continue;   // degenerate triangle

                var crossings = new List<EdgeKey>(2);
                AddCrossing(ia, ib, heights, elevation, crossings);
                AddCrossing(ib, ic, heights, elevation, crossings);
                AddCrossing(ic, ia, heights, elevation, crossings);
                if (crossings.Count == 2)
                    segments.Add(new Segment(crossings[0], crossings[1]));
            }

            return Chain(segments, positions, heights, elevation);
        }

        private static void AddCrossing(int i, int j, List<double> heights, double elevation,
                                        List<EdgeKey> crossings)
        {
            var aboveI = heights[i] >= elevation;
            var aboveJ = heights[j] >= elevation;
            if (aboveI != aboveJ) crossings.Add(new EdgeKey(i, j));
        }

        /// <summary>
        /// The crossing on an edge, computed from the edge's own canonical direction so both
        /// triangles that share the edge produce bit-identical points.
        /// </summary>
        private static P2 Crossing(EdgeKey edge, List<P2> positions, List<double> heights, double elevation)
        {
            var a = positions[edge.Low];
            var b = positions[edge.High];
            var za = heights[edge.Low];
            var zb = heights[edge.High];
            if (za == elevation) return a;
            if (zb == elevation) return b;
            var t = (elevation - za) / (zb - za);
            return new P2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        private static IList<ContourPath> Chain(List<Segment> segments, List<P2> positions,
                                                List<double> heights, double elevation)
        {
            var byEdge = new Dictionary<EdgeKey, List<int>>();
            for (var s = 0; s < segments.Count; s++)
            {
                foreach (var e in new[] { segments[s].First, segments[s].Second })
                {
                    List<int> list;
                    if (!byEdge.TryGetValue(e, out list)) byEdge[e] = list = new List<int>(2);
                    list.Add(s);
                }
            }

            var used = new bool[segments.Count];
            var paths = new List<ContourPath>();

            // Open pieces first: they start where an edge has only one cut triangle -- the
            // surface's outer or hidden edge.
            var starts = byEdge.Where(kv => kv.Value.Count == 1).Select(kv => kv.Key)
                               .OrderBy(k => k.Low).ThenBy(k => k.High).ToList();
            foreach (var start in starts)
            {
                var s = byEdge[start][0];
                if (used[s]) continue;
                var edges = Walk(start, s, segments, byEdge, used);
                paths.Add(Build(edges, false, positions, heights, elevation));
            }

            // Whatever is left is closed loops.
            for (var s = 0; s < segments.Count; s++)
            {
                if (used[s]) continue;
                var start = segments[s].First;
                var edges = Walk(start, s, segments, byEdge, used);
                var loop = edges.Count > 2 && edges[edges.Count - 1].Equals(edges[0]);
                paths.Add(Build(edges, loop, positions, heights, elevation));
            }

            // Crossings at a vertex that sits exactly on the elevation repeat; drop the
            // repeats. A "loop" that collapses to two points is a ridge edge lying exactly at
            // the elevation: the surface touches the level along that line, so it is drawn as
            // the line it is.
            var result = new List<ContourPath>();
            foreach (var path in paths)
            {
                var clean = path.WithoutRepeats(0.0);
                if (clean.Closed && clean.Vertices.Count == 2)
                    clean = new ContourPath(clean.Vertices, false);
                if (clean.IsDrawable) result.Add(clean);
            }
            return result;
        }

        private static List<EdgeKey> Walk(EdgeKey start, int firstSegment, List<Segment> segments,
                                          Dictionary<EdgeKey, List<int>> byEdge, bool[] used)
        {
            var edges = new List<EdgeKey> { start };
            var current = start;
            var s = firstSegment;
            while (s >= 0 && !used[s])
            {
                used[s] = true;
                var next = segments[s].Other(current);
                edges.Add(next);
                current = next;

                s = -1;
                List<int> list;
                if (byEdge.TryGetValue(current, out list) && list.Count == 2)
                    foreach (var candidate in list)
                        if (!used[candidate]) { s = candidate; break; }
            }
            return edges;
        }

        private static ContourPath Build(List<EdgeKey> edges, bool closed, List<P2> positions,
                                         List<double> heights, double elevation)
        {
            var points = edges.Select(e => Crossing(e, positions, heights, elevation)).ToList();
            if (closed && points.Count > 1 && edges[0].Equals(edges[edges.Count - 1]))
                points.RemoveAt(points.Count - 1);   // the loop came back to its first edge
            return ContourPath.FromPoints(points, closed);
        }

        private struct VertexKey : IEquatable<VertexKey>
        {
            private readonly double _x;
            private readonly double _y;

            public VertexKey(double x, double y)
            {
                _x = x == 0 ? 0 : x;   // -0 and 0 are the same vertex
                _y = y == 0 ? 0 : y;
            }

            public bool Equals(VertexKey other) { return _x == other._x && _y == other._y; }
            public override bool Equals(object obj) { return obj is VertexKey && Equals((VertexKey)obj); }
            public override int GetHashCode() { return _x.GetHashCode() * 397 ^ _y.GetHashCode(); }
        }

        private struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly int Low;
            public readonly int High;

            public EdgeKey(int i, int j)
            {
                Low = Math.Min(i, j);
                High = Math.Max(i, j);
            }

            public bool Equals(EdgeKey other) { return Low == other.Low && High == other.High; }
            public override bool Equals(object obj) { return obj is EdgeKey && Equals((EdgeKey)obj); }
            public override int GetHashCode() { return Low * 486187739 ^ High; }
        }

        private struct Segment
        {
            public readonly EdgeKey First;
            public readonly EdgeKey Second;

            public Segment(EdgeKey first, EdgeKey second)
            {
                First = first;
                Second = second;
            }

            public EdgeKey Other(EdgeKey edge) { return edge.Equals(First) ? Second : First; }
        }
    }
}
