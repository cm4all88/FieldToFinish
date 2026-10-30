using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Contours
{
    /// <summary>
    /// A closed limit for contouring, as a plan polygon. Arcs in the picked outline are
    /// followed to within the chord tolerance. A point on the outline counts as inside, so
    /// a contour that runs along the limit is kept by an inside clip and removed by an
    /// outside one -- never both, never neither.
    /// </summary>
    public sealed class ContourBoundary
    {
        public IList<P2> Polygon { get; private set; }

        private readonly double _minX, _minY, _maxX, _maxY;
        private readonly double _onEdge;

        private ContourBoundary(IList<P2> polygon)
        {
            Polygon = polygon;
            _minX = polygon.Min(p => p.X);
            _minY = polygon.Min(p => p.Y);
            _maxX = polygon.Max(p => p.X);
            _maxY = polygon.Max(p => p.Y);
            var size = Math.Max(1.0, Math.Max(_maxX - _minX, _maxY - _minY));
            _onEdge = size * 1e-10;
        }

        /// <summary>Builds the limit from a closed outline. Null, with the reason, when it cannot be one.</summary>
        public static ContourBoundary FromPath(ContourPath outline, double chordTolerance, out string problem)
        {
            problem = null;
            if (outline == null || outline.Vertices.Count == 0)
            {
                problem = "the boundary has no vertices";
                return null;
            }

            var path = outline.WithoutRepeats(0.0);
            if (!path.Closed)
            {
                problem = "the boundary is not closed (close the polyline, or pick one whose ends meet)";
                return null;
            }

            var points = path.Tessellated(chordTolerance).Points;
            if (points.Count < 3)
            {
                problem = "the boundary needs at least three corners";
                return null;
            }

            var area = Math.Abs(SignedArea(points));
            var span = Math.Max(points.Max(p => p.X) - points.Min(p => p.X),
                                points.Max(p => p.Y) - points.Min(p => p.Y));
            if (area <= span * span * 1e-12)
            {
                problem = "the boundary encloses no area";
                return null;
            }

            return new ContourBoundary(points.ToList().AsReadOnly());
        }

        public static ContourBoundary FromPoints(IEnumerable<P2> points, out string problem)
        {
            return FromPath(ContourPath.FromPoints(points, true), 0.0, out problem);
        }

        public double Area { get { return Math.Abs(SignedArea(Polygon)); } }

        public bool Contains(P2 p)
        {
            if (p.X < _minX - _onEdge || p.X > _maxX + _onEdge ||
                p.Y < _minY - _onEdge || p.Y > _maxY + _onEdge)
                return false;

            var inside = false;
            var n = Polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = Polygon[j];
                var b = Polygon[i];
                if (DistanceToSegment(p, a, b) <= _onEdge) return true;
                if ((b.Y > p.Y) != (a.Y > p.Y))
                {
                    var x = a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                    if (p.X < x) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>Parameters in (0, 1) where the segment p-q meets the outline.</summary>
        internal IEnumerable<double> Crossings(P2 p, P2 q)
        {
            if (Math.Max(p.X, q.X) < _minX - _onEdge || Math.Min(p.X, q.X) > _maxX + _onEdge ||
                Math.Max(p.Y, q.Y) < _minY - _onEdge || Math.Min(p.Y, q.Y) > _maxY + _onEdge)
                yield break;

            var d = q - p;
            var n = Polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = Polygon[j];
                var b = Polygon[i];
                if (Math.Max(a.X, b.X) < Math.Min(p.X, q.X) || Math.Min(a.X, b.X) > Math.Max(p.X, q.X) ||
                    Math.Max(a.Y, b.Y) < Math.Min(p.Y, q.Y) || Math.Min(a.Y, b.Y) > Math.Max(p.Y, q.Y))
                    continue;

                var e = b - a;
                var denominator = P2.Cross(d, e);
                var ap = a - p;
                if (Math.Abs(denominator) <= 1e-15 * d.Length * e.Length)
                {
                    // Parallel. If collinear, the overlap's ends are where inside/outside can change.
                    if (Math.Abs(P2.Cross(ap, d)) > 1e-12 * Math.Max(1.0, d.Length * ap.Length)) continue;
                    var dd = P2.Dot(d, d);
                    if (dd <= 0) continue;
                    foreach (var end in new[] { a, b })
                    {
                        var t = P2.Dot(end - p, d) / dd;
                        if (t > 0 && t < 1) yield return t;
                    }
                    continue;
                }

                var tp = P2.Cross(ap, e) / denominator;   // along p-q
                var ua = P2.Cross(ap, d) / denominator;   // along a-b
                if (tp > 0 && tp < 1 && ua >= -1e-12 && ua <= 1 + 1e-12) yield return tp;
            }
        }

        private static double DistanceToSegment(P2 p, P2 a, P2 b)
        {
            var ab = b - a;
            var len2 = P2.Dot(ab, ab);
            if (len2 <= 0) return p.DistanceTo(a);
            var t = Math.Max(0, Math.Min(1, P2.Dot(p - a, ab) / len2));
            return p.DistanceTo(a + ab * t);
        }

        private static double SignedArea(IList<P2> points)
        {
            var sum = 0.0;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                sum += points[j].X * points[i].Y - points[i].X * points[j].Y;
            return sum / 2.0;
        }
    }

    /// <summary>
    /// Cuts contour pieces to a closed limit. The cut points lie on the original contour
    /// segments (a straight contour segment is level, so any point on it is at the contour's
    /// elevation): clipping changes where a contour stops, never where it runs. Pieces that do
    /// not cross the limit are returned untouched, arcs and all.
    /// </summary>
    public static class ContourClipper
    {
        /// <summary>The parts of <paramref name="path"/> inside the limit (or outside it, when
        /// <paramref name="keepInside"/> is false), as separate pieces.</summary>
        public static IList<ContourPath> Clip(ContourPath path, ContourBoundary boundary,
                                              bool keepInside, double chordTolerance)
        {
            var result = new List<ContourPath>();
            if (path == null || path.Vertices.Count < 2) return result;
            if (boundary == null)
            {
                if (keepInside) result.Add(path);
                return result;
            }

            var flat = path.Tessellated(chordTolerance);
            var points = flat.Points;
            var segmentCount = flat.SegmentCount;

            // Split every segment where it meets the limit, and classify each part by its middle.
            var parts = new List<Part>();
            for (var i = 0; i < segmentCount; i++)
            {
                var p = points[i];
                var q = points[(i + 1) % points.Count];
                var cuts = boundary.Crossings(p, q).Distinct().OrderBy(t => t).ToList();
                var ts = new List<double> { 0.0 };
                ts.AddRange(cuts);
                ts.Add(1.0);
                for (var k = 0; k + 1 < ts.Count; k++)
                {
                    if (ts[k + 1] - ts[k] <= 0) continue;
                    var from = Lerp(p, q, ts[k]);
                    var to = Lerp(p, q, ts[k + 1]);
                    var middle = Lerp(p, q, (ts[k] + ts[k + 1]) / 2.0);
                    parts.Add(new Part
                    {
                        From = from,
                        To = to,
                        Keep = boundary.Contains(middle) == keepInside,
                        Split = cuts.Count > 0
                    });
                }
            }

            if (parts.Count == 0) return result;
            var anyCut = parts.Any(x => x.Split);

            if (parts.All(x => x.Keep))
            {
                // Entirely on the kept side. Return the original so arcs stay arcs -- unless the
                // path merely touched the limit, in which case the flat copy is identical anyway.
                result.Add(anyCut && path.HasBulges ? flat : path);
                return result;
            }
            if (parts.All(x => !x.Keep)) return result;

            // Walk the parts, collecting runs of kept ones.
            var pieces = new List<List<P2>>();
            List<P2> current = null;
            foreach (var part in parts)
            {
                if (!part.Keep)
                {
                    current = null;
                    continue;
                }
                if (current == null)
                {
                    current = new List<P2> { part.From };
                    pieces.Add(current);
                }
                current.Add(part.To);
            }

            // A closed contour that starts on the kept side: its last piece runs on into its first.
            if (flat.Closed && parts[0].Keep && parts[parts.Count - 1].Keep && pieces.Count > 1)
            {
                var last = pieces[pieces.Count - 1];
                var first = pieces[0];
                last.AddRange(first.Skip(1));
                pieces.RemoveAt(0);
            }

            foreach (var piece in pieces)
            {
                var clean = ContourPath.FromPoints(piece, false).WithoutRepeats(0.0);
                if (clean.Closed) clean = new ContourPath(clean.Vertices, false);
                if (clean.Vertices.Count >= 2 && clean.Length > 0) result.Add(clean);
            }
            return result;
        }

        /// <summary>Clips every piece; the order of the input is kept.</summary>
        public static IList<ContourPath> ClipAll(IEnumerable<ContourPath> paths, ContourBoundary boundary,
                                                 bool keepInside, double chordTolerance)
        {
            var result = new List<ContourPath>();
            foreach (var path in paths ?? Enumerable.Empty<ContourPath>())
                result.AddRange(Clip(path, boundary, keepInside, chordTolerance));
            return result;
        }

        private static P2 Lerp(P2 a, P2 b, double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            return new P2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        private struct Part
        {
            public P2 From;
            public P2 To;
            public bool Keep;
            public bool Split;
        }
    }
}
