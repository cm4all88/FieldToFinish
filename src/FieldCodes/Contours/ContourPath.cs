using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Contours
{
    /// <summary>A polyline vertex in plan: position and the bulge of the segment that leaves it
    /// (AutoCAD's convention: tan of a quarter of the included angle, positive counter-clockwise).</summary>
    public struct ContourVertex
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Bulge;

        public ContourVertex(double x, double y, double bulge = 0.0)
        {
            X = x;
            Y = y;
            Bulge = bulge;
        }

        public P2 Point { get { return new P2(X, Y); } }
    }

    /// <summary>
    /// One continuous contour piece in plan. The elevation is not stored per vertex: a
    /// contour is level by definition, and the command assigns the exact requested
    /// elevation to the polyline it draws.
    /// </summary>
    public sealed class ContourPath
    {
        public IList<ContourVertex> Vertices { get; private set; }
        public bool Closed { get; private set; }

        public ContourPath(IEnumerable<ContourVertex> vertices, bool closed)
        {
            Vertices = (vertices ?? Enumerable.Empty<ContourVertex>()).ToList().AsReadOnly();
            Closed = closed;
        }

        public static ContourPath FromPoints(IEnumerable<P2> points, bool closed)
        {
            return new ContourPath((points ?? Enumerable.Empty<P2>())
                .Select(p => new ContourVertex(p.X, p.Y)), closed);
        }

        public int SegmentCount
        {
            get
            {
                var n = Vertices.Count;
                if (n < 2) return 0;
                return Closed ? n : n - 1;
            }
        }

        public bool HasBulges
        {
            get
            {
                for (var i = 0; i < SegmentCount; i++)
                    if (Vertices[i].Bulge != 0.0) return true;
                return false;
            }
        }

        public IList<P2> Points { get { return Vertices.Select(v => v.Point).ToList(); } }

        /// <summary>Plan length, arcs measured as arcs.</summary>
        public double Length
        {
            get
            {
                var total = 0.0;
                for (var i = 0; i < SegmentCount; i++)
                {
                    var a = Vertices[i];
                    var b = Vertices[(i + 1) % Vertices.Count];
                    total += ArcMath.SegmentLength(a.Point, b.Point, a.Bulge);
                }
                return total;
            }
        }

        /// <summary>
        /// The same path with every arc replaced by chords no further than
        /// <paramref name="chordTolerance"/> from it. Straight segments and vertices are kept
        /// exactly; a path with no arcs comes back unchanged.
        /// </summary>
        public ContourPath Tessellated(double chordTolerance)
        {
            if (!HasBulges) return this;
            var points = new List<ContourVertex>();
            for (var i = 0; i < Vertices.Count; i++)
            {
                var a = Vertices[i];
                points.Add(new ContourVertex(a.X, a.Y));
                if (i >= SegmentCount) break;
                if (a.Bulge == 0.0) continue;
                var b = Vertices[(i + 1) % Vertices.Count];
                foreach (var p in ArcMath.InteriorPoints(a.Point, b.Point, a.Bulge, chordTolerance))
                    points.Add(new ContourVertex(p.X, p.Y));
            }
            return new ContourPath(points, Closed);
        }

        /// <summary>Consecutive repeated points removed (and a closing repeat on a closed path).</summary>
        public ContourPath WithoutRepeats(double tolerance)
        {
            var kept = new List<ContourVertex>();
            foreach (var v in Vertices)
            {
                if (kept.Count > 0 && Same(kept[kept.Count - 1], v, tolerance))
                {
                    // Keep the later bulge only if the earlier vertex had none (zero-length
                    // segments carry nothing worth keeping).
                    if (kept[kept.Count - 1].Bulge == 0.0 && v.Bulge != 0.0)
                        kept[kept.Count - 1] = new ContourVertex(kept[kept.Count - 1].X, kept[kept.Count - 1].Y, v.Bulge);
                    continue;
                }
                kept.Add(v);
            }
            var closed = Closed;
            if (kept.Count > 1 && Same(kept[0], kept[kept.Count - 1], tolerance))
            {
                // A path that comes back to its start is a closed loop.
                kept.RemoveAt(kept.Count - 1);
                closed = true;
            }
            return new ContourPath(kept, closed);
        }

        private static bool Same(ContourVertex a, ContourVertex b, double tolerance)
        {
            return Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;
        }

        /// <summary>True when the path has something to draw: two distinct points open, three closed.</summary>
        public bool IsDrawable
        {
            get { return Closed ? Vertices.Count >= 3 || (Vertices.Count == 2 && HasBulges) : Vertices.Count >= 2; }
        }

        /// <summary>The point and direction at a distance along the (tessellated) path.</summary>
        public static P2 PointAt(IList<P2> points, bool closed, double distance, out P2 direction)
        {
            direction = new P2(1, 0);
            if (points.Count == 0) return new P2(0, 0);
            if (points.Count == 1) return points[0];

            var segments = closed ? points.Count : points.Count - 1;
            var total = 0.0;
            for (var i = 0; i < segments; i++) total += points[i].DistanceTo(points[(i + 1) % points.Count]);
            if (total <= 0) return points[0];

            if (closed)
            {
                distance %= total;
                if (distance < 0) distance += total;
            }
            else
            {
                distance = Math.Max(0, Math.Min(total, distance));
            }

            var walked = 0.0;
            for (var i = 0; i < segments; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                var len = a.DistanceTo(b);
                if (len <= 0) continue;
                if (walked + len >= distance || i == segments - 1)
                {
                    var t = Math.Max(0, Math.Min(1, (distance - walked) / len));
                    direction = (b - a).Normalized();
                    return a + (b - a) * t;
                }
                walked += len;
            }
            return points[points.Count - 1];
        }
    }

    /// <summary>Bulge arithmetic for polyline arc segments.</summary>
    public static class ArcMath
    {
        /// <summary>Length of the segment from a to b with the given bulge.</summary>
        public static double SegmentLength(P2 a, P2 b, double bulge)
        {
            var chord = a.DistanceTo(b);
            if (bulge == 0.0 || chord == 0.0) return chord;
            var theta = 4.0 * Math.Atan(bulge);
            var radius = chord / (2.0 * Math.Sin(Math.Abs(theta) / 2.0));
            return radius * Math.Abs(theta);
        }

        /// <summary>Centre of the arc from a to b with the given (non-zero) bulge.</summary>
        public static P2 Centre(P2 a, P2 b, double bulge)
        {
            var chord = a.DistanceTo(b);
            var mid = new P2((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
            var left = (b - a).LeftNormal();
            var offset = chord / 2.0 * (1.0 - bulge * bulge) / (2.0 * bulge);
            return mid + left * offset;
        }

        /// <summary>
        /// Points strictly between a and b along the arc, spaced so no chord strays more
        /// than <paramref name="chordTolerance"/> from the arc.
        /// </summary>
        public static IEnumerable<P2> InteriorPoints(P2 a, P2 b, double bulge, double chordTolerance)
        {
            var chord = a.DistanceTo(b);
            if (bulge == 0.0 || chord == 0.0) yield break;

            var theta = 4.0 * Math.Atan(bulge);
            var centre = Centre(a, b, bulge);
            var radius = centre.DistanceTo(a);
            var tolerance = chordTolerance > 0 ? Math.Min(chordTolerance, radius) : radius * 1e-3;
            var maxStep = 2.0 * Math.Acos(Math.Max(-1.0, Math.Min(1.0, 1.0 - tolerance / radius)));
            if (!(maxStep > 0)) maxStep = Math.PI / 16;
            var count = (int)Math.Ceiling(Math.Abs(theta) / maxStep);
            count = Math.Max(1, Math.Min(1024, count));

            var start = Math.Atan2(a.Y - centre.Y, a.X - centre.X);
            for (var k = 1; k < count; k++)
            {
                var angle = start + theta * k / count;
                yield return new P2(centre.X + radius * Math.Cos(angle), centre.Y + radius * Math.Sin(angle));
            }
        }
    }
}
