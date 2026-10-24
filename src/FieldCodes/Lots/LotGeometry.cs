using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Easements;
using FieldCodes.Row;

namespace FieldCodes.Lots
{
    /// <summary>A polygon corner whose following edge carries a tag saying where that edge came
    /// from: a side of the block, or a line the lots were cut by.</summary>
    internal struct TagVertex
    {
        public readonly P2 P;
        public readonly int Tag;
        public TagVertex(P2 p, int tag) { P = p; Tag = tag; }
    }

    /// <summary>
    /// Polygon work for dividing a block: clipping a polygon by a line while remembering which
    /// line made each new edge, so a lot's edges can be told apart (front, side, rear) by where
    /// they came from instead of guessed at from the geometry afterwards. Pure.
    /// </summary>
    internal static class LotGeometry
    {
        public const double Eps = 1e-9;

        public static List<TagVertex> FromRing(IList<P2> corners)
        {
            var poly = new List<TagVertex>();
            for (var i = 0; i < corners.Count; i++) poly.Add(new TagVertex(corners[i], i));
            return poly;
        }

        /// <summary>Keeps the part of the polygon where <c>Dot(x - origin, normal) &gt;= offset</c>.
        /// The edge cut along that line is tagged <paramref name="tag"/>; the rest keep their tags.</summary>
        public static List<TagVertex> Clip(IList<TagVertex> polygon, P2 origin, P2 normal, double offset, int tag)
        {
            var result = new List<TagVertex>();
            var n = polygon.Count;
            for (var i = 0; i < n; i++)
            {
                var current = polygon[i];
                var next = polygon[(i + 1) % n];
                var dc = P2.Dot(current.P - origin, normal) - offset;
                var dn = P2.Dot(next.P - origin, normal) - offset;
                var currentIn = dc >= -Eps;
                var nextIn = dn >= -Eps;

                if (currentIn)
                {
                    result.Add(current);
                    if (!nextIn) result.Add(new TagVertex(Cross(current.P, next.P, dc, dn), tag));
                }
                else if (nextIn)
                {
                    result.Add(new TagVertex(Cross(current.P, next.P, dc, dn), current.Tag));
                }
            }
            return Clean(result);
        }

        private static P2 Cross(P2 a, P2 b, double da, double db)
        {
            var t = da / (da - db);
            return a + (b - a) * t;
        }

        /// <summary>Drops repeated points and points that sit in the middle of one straight
        /// edge, so a clipped lot has exactly its corners.</summary>
        public static List<TagVertex> Clean(IList<TagVertex> polygon)
        {
            var poly = polygon.ToList();
            var changed = true;
            while (changed && poly.Count >= 3)
            {
                changed = false;
                for (var i = 0; i < poly.Count; i++)
                {
                    var next = poly[(i + 1) % poly.Count];
                    if (poly[i].P.DistanceTo(next.P) < 1e-7)
                    {
                        poly.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
                if (changed) continue;

                for (var i = 0; i < poly.Count; i++)
                {
                    var prev = poly[(i - 1 + poly.Count) % poly.Count];
                    var here = poly[i];
                    var next = poly[(i + 1) % poly.Count];
                    var a = here.P - prev.P;
                    var b = next.P - here.P;
                    if (prev.Tag == here.Tag && Math.Abs(P2.Cross(a.Normalized(), b.Normalized())) < 1e-9 && P2.Dot(a, b) > 0)
                    {
                        poly.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
            return poly.Count < 3 ? new List<TagVertex>() : poly;
        }

        public static List<P2> Points(IList<TagVertex> polygon)
        {
            return polygon.Select(v => v.P).ToList();
        }

        public static double Area(IList<P2> polygon)
        {
            return Math.Abs(RowGeometry.SignedArea(polygon));
        }

        /// <summary>The centre of the polygon's area.</summary>
        public static P2 Centroid(IList<P2> polygon)
        {
            double a = 0, cx = 0, cy = 0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var p = polygon[i];
                var q = polygon[(i + 1) % polygon.Count];
                var cross = p.X * q.Y - q.X * p.Y;
                a += cross;
                cx += (p.X + q.X) * cross;
                cy += (p.Y + q.Y) * cross;
            }
            if (Math.Abs(a) < Eps)
            {
                var sum = new P2(0, 0);
                foreach (var p in polygon) sum = sum + p;
                return sum * (1.0 / Math.Max(1, polygon.Count));
            }
            return new P2(cx / (3.0 * a), cy / (3.0 * a));
        }

        /// <summary>Distance along a ray to the furthest place it crosses the polygon's boundary,
        /// or 0 when it crosses nowhere. For a convex block that is where it leaves.</summary>
        public static double RayExit(P2 origin, P2 direction, IList<P2> polygon)
        {
            double best = 0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                var edge = b - a;
                var cross = P2.Cross(direction, edge);
                if (Math.Abs(cross) < Eps) continue;
                var t = P2.Cross(a - origin, edge) / cross;
                var s = P2.Cross(a - origin, direction) / cross;
                if (t > 1e-9 && s >= -1e-9 && s <= 1 + 1e-9 && t > best) best = t;
            }
            return best;
        }

        /// <summary>True when every turn of the polygon is the same way.</summary>
        public static bool IsConvex(IList<P2> polygon)
        {
            double sign = 0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                var c = polygon[(i + 2) % polygon.Count];
                var cross = P2.Cross(b - a, c - b);
                if (Math.Abs(cross) < 1e-9) continue;
                if (sign == 0) sign = Math.Sign(cross);
                else if (Math.Sign(cross) != sign) return false;
            }
            return true;
        }

        public static double DistanceToSegment(P2 p, P2 a, P2 b)
        {
            var ab = b - a;
            var len2 = P2.Dot(ab, ab);
            if (len2 < Eps) return p.DistanceTo(a);
            var t = Math.Max(0.0, Math.Min(1.0, P2.Dot(p - a, ab) / len2));
            return p.DistanceTo(a + ab * t);
        }
    }
}
