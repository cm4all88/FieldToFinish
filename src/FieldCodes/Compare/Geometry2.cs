using System;
using System.Collections.Generic;

namespace FieldCodes.Compare
{
    /// <summary>Plain 2D geometry over chorded polylines. Pure functions, unit tested.</summary>
    public static class Geometry2
    {
        /// <summary>Distance from p to segment ab; t is the parameter of the closest point (0..1).</summary>
        public static double DistanceToSegment(CPoint p, CPoint a, CPoint b, out double t)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len2 = dx * dx + dy * dy;
            if (len2 <= 1e-18)
            {
                t = 0;
                return p.DistanceTo(a);
            }
            t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            var cx = a.X + t * dx;
            var cy = a.Y + t * dy;
            var ex = p.X - cx;
            var ey = p.Y - cy;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>Closest point on a chorded polyline. Returns the distance; outputs the
        /// station along the polyline and the index of the segment hit.</summary>
        public static double ClosestOnPolyline(CPoint p, IList<CPoint> v, double[] stations, out double station, out int segment)
        {
            station = 0;
            segment = -1;
            if (v == null || v.Count == 0) return double.MaxValue;
            if (v.Count == 1) { segment = 0; return p.DistanceTo(v[0]); }

            var best = double.MaxValue;
            for (var i = 0; i < v.Count - 1; i++)
            {
                double t;
                var d = DistanceToSegment(p, v[i], v[i + 1], out t);
                if (d < best)
                {
                    best = d;
                    segment = i;
                    station = stations[i] + t * (stations[i + 1] - stations[i]);
                }
            }
            return best;
        }

        /// <summary>Angle in degrees (0..90) between two directions ignoring sense, so a
        /// reversed line runs "the same way" as its original.</summary>
        public static double AngleBetweenUndirected(double ax, double ay, double bx, double by)
        {
            var la = Math.Sqrt(ax * ax + ay * ay);
            var lb = Math.Sqrt(bx * bx + by * by);
            if (la < 1e-12 || lb < 1e-12) return 0;
            var cos = Math.Abs((ax * bx + ay * by) / (la * lb));
            if (cos > 1) cos = 1;
            return Math.Acos(cos) * 180.0 / Math.PI;
        }

        /// <summary>Point at a station along the polyline (clamped to its ends).</summary>
        public static CPoint PointAtStation(IList<CPoint> v, double[] stations, double s)
        {
            if (v.Count == 1) return v[0];
            if (s <= 0) return v[0];
            var last = stations[stations.Length - 1];
            if (s >= last) return v[v.Count - 1];
            var i = SegmentAt(stations, s);
            var segLen = stations[i + 1] - stations[i];
            var t = segLen <= 1e-12 ? 0 : (s - stations[i]) / segLen;
            var a = v[i];
            var b = v[i + 1];
            var z = a.HasZ && b.HasZ ? a.Z + t * (b.Z - a.Z) : double.NaN;
            return new CPoint(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y), z);
        }

        /// <summary>Unit direction of the segment containing the station.</summary>
        public static void DirectionAtStation(IList<CPoint> v, double[] stations, double s, out double dx, out double dy)
        {
            dx = 1; dy = 0;
            if (v.Count < 2) return;
            var i = SegmentAt(stations, Math.Max(0, Math.Min(s, stations[stations.Length - 1])));
            // A zero-length segment borrows the next real one.
            while (i < v.Count - 2 && stations[i + 1] - stations[i] <= 1e-12) i++;
            var a = v[i];
            var b = v[i + 1];
            dx = b.X - a.X;
            dy = b.Y - a.Y;
            var l = Math.Sqrt(dx * dx + dy * dy);
            if (l > 1e-12) { dx /= l; dy /= l; }
        }

        /// <summary>Index i with stations[i] &lt;= s &lt; stations[i+1] (binary search).</summary>
        public static int SegmentAt(double[] stations, double s)
        {
            var lo = 0;
            var hi = stations.Length - 2;
            if (hi < 0) return 0;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (stations[mid] <= s) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        /// <summary>Stations to sample: every vertex plus a regular step, sorted and deduped.</summary>
        public static List<double> SampleStations(double[] stations, double step)
        {
            var result = new List<double>();
            if (stations.Length == 0) return result;
            var length = stations[stations.Length - 1];
            if (step <= 0) step = 1.0;
            foreach (var s in stations) result.Add(s);
            for (var s = step; s < length; s += step) result.Add(s);
            result.Sort();
            var deduped = new List<double>(result.Count);
            foreach (var s in result)
                if (deduped.Count == 0 || s - deduped[deduped.Count - 1] > 1e-9) deduped.Add(s);
            return deduped;
        }

        /// <summary>Ray-casting point-in-polygon. Points on the edge count as inside.</summary>
        public static bool PointInPolygon(CPoint p, IList<CPoint> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;
            var inside = false;
            var n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                double t;
                if (DistanceToSegment(p, a, b, out t) < 1e-9) return true;
                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    var x = (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X;
                    if (p.X < x) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>Length of a chorded polyline.</summary>
        public static double Length(IList<CPoint> v)
        {
            double l = 0;
            for (var i = 1; i < v.Count; i++) l += v[i - 1].DistanceTo(v[i]);
            return l;
        }

        /// <summary>Chords an arc defined by three points: start, a point on the arc, end.
        /// Used by tests and by readers that only know a bulge-free arc.</summary>
        public static List<CPoint> ChordArc(CPoint center, double radius, double startAngle, double endAngle, bool counterClockwise, double chordTolerance)
        {
            var points = new List<CPoint>();
            var sweep = counterClockwise ? endAngle - startAngle : startAngle - endAngle;
            while (sweep < 0) sweep += 2 * Math.PI;
            if (sweep <= 1e-12) sweep = 2 * Math.PI;
            // Segment count from the sagitta: tolerance = r(1 - cos(theta/2)).
            var maxTheta = chordTolerance > 0 && chordTolerance < radius ? 2 * Math.Acos(1 - chordTolerance / radius) : Math.PI / 8;
            if (maxTheta <= 0 || double.IsNaN(maxTheta)) maxTheta = Math.PI / 8;
            var n = Math.Max(2, (int)Math.Ceiling(sweep / maxTheta));
            for (var i = 0; i <= n; i++)
            {
                var a = startAngle + (counterClockwise ? 1 : -1) * sweep * i / n;
                points.Add(new CPoint(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)));
            }
            return points;
        }

        /// <summary>Chords a polyline segment with a bulge (AutoCAD convention: tan of a
        /// quarter of the included angle, positive counter-clockwise).</summary>
        public static List<CPoint> ChordBulge(CPoint a, CPoint b, double bulge, double chordTolerance)
        {
            var points = new List<CPoint>();
            if (Math.Abs(bulge) < 1e-12)
            {
                points.Add(a);
                points.Add(b);
                return points;
            }
            var chord = a.DistanceTo(b);
            if (chord < 1e-12) { points.Add(a); return points; }
            var theta = 4 * Math.Atan(Math.Abs(bulge));             // included angle
            var radius = chord / (2 * Math.Sin(theta / 2));
            var mx = (a.X + b.X) / 2;
            var my = (a.Y + b.Y) / 2;
            var d = Math.Sqrt(Math.Max(0, radius * radius - chord * chord / 4));
            // Perpendicular to the chord; the centre sits on the left for a positive bulge
            // when the arc is less than a half circle, on the right otherwise.
            var nx = -(b.Y - a.Y) / chord;
            var ny = (b.X - a.X) / chord;
            var side = (bulge > 0) == (Math.Abs(bulge) <= 1) ? 1 : -1;
            var cx = mx + side * d * nx;
            var cy = my + side * d * ny;
            var startAngle = Math.Atan2(a.Y - cy, a.X - cx);
            var endAngle = Math.Atan2(b.Y - cy, b.X - cx);
            var arc = ChordArc(new CPoint(cx, cy), radius, startAngle, endAngle, bulge > 0, chordTolerance);
            // Keep exact ends.
            arc[0] = new CPoint(a.X, a.Y, a.Z);
            arc[arc.Count - 1] = new CPoint(b.X, b.Y, b.Z);
            return arc;
        }
    }
}
