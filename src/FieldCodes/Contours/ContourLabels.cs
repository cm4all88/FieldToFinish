using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Contours
{
    /// <summary>Where one elevation label goes: centred on the contour, along it, readable.</summary>
    public struct ContourLabelSpot
    {
        public readonly P2 At;

        /// <summary>Radians, always in (-pi/2, pi/2] so the text never reads upside down.</summary>
        public readonly double Rotation;

        public ContourLabelSpot(P2 at, double rotation)
        {
            At = at;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// Spaces elevation labels along a contour piece. One label sits at the middle of a
    /// piece; a long piece gets one per <c>spacing</c>, evenly spread. Each label is slid
    /// along the contour (within its share of the length) to the straightest stretch, so
    /// text does not sit across a sharp bend. A piece too short to carry the text clearly
    /// is left unlabelled.
    /// </summary>
    public static class ContourLabelPlanner
    {
        public static IList<ContourLabelSpot> Plan(ContourPath path, double labelWidth, double spacing,
                                                   double chordTolerance)
        {
            var spots = new List<ContourLabelSpot>();
            if (path == null || labelWidth <= 0) return spots;

            var flat = path.Tessellated(chordTolerance);
            var points = flat.Points;
            if (points.Count < 2) return spots;

            var length = flat.Length;
            if (length < labelWidth * 2.0) return spots;

            var count = spacing > 0 ? Math.Max(1, (int)Math.Floor(length / spacing)) : 1;
            var share = length / count;
            var half = labelWidth / 2.0;

            for (var k = 0; k < count; k++)
            {
                var centre = (k + 0.5) * share;

                // Look either side of the even spot for the straightest stretch, preferring
                // the even spot when it is already straight enough.
                var best = centre;
                var bestStraightness = Straightness(points, flat.Closed, centre, half, length);
                if (bestStraightness < 0.98)
                {
                    var reach = Math.Max(0, share / 2.0 - half);
                    const int Tries = 12;
                    for (var i = 1; i <= Tries; i++)
                    {
                        foreach (var sign in new[] { 1.0, -1.0 })
                        {
                            var s = centre + sign * reach * i / Tries;
                            if (!flat.Closed && (s - half < 0 || s + half > length)) continue;
                            var straightness = Straightness(points, flat.Closed, s, half, length);
                            if (straightness > bestStraightness + 1e-9)
                            {
                                best = s;
                                bestStraightness = straightness;
                            }
                        }
                    }
                }

                P2 ignored;
                var at = ContourPath.PointAt(points, flat.Closed, best, out ignored);
                P2 before, after;
                var a = ContourPath.PointAt(points, flat.Closed, Clamp(best - half, flat.Closed, length), out before);
                var b = ContourPath.PointAt(points, flat.Closed, Clamp(best + half, flat.Closed, length), out after);
                var direction = b - a;
                if (direction.Length <= 0) direction = after;
                spots.Add(new ContourLabelSpot(at, Readable(Math.Atan2(direction.Y, direction.X))));
            }
            return spots;
        }

        /// <summary>Chord over arc length across the label's width: 1 on a straight run.</summary>
        private static double Straightness(IList<P2> points, bool closed, double s, double half, double length)
        {
            P2 d;
            var a = ContourPath.PointAt(points, closed, Clamp(s - half, closed, length), out d);
            var b = ContourPath.PointAt(points, closed, Clamp(s + half, closed, length), out d);
            var along = closed ? 2 * half : Clamp(s + half, false, length) - Clamp(s - half, false, length);
            return along <= 0 ? 0 : a.DistanceTo(b) / along;
        }

        private static double Clamp(double s, bool closed, double length)
        {
            return closed ? s : Math.Max(0, Math.Min(length, s));
        }

        /// <summary>The same direction turned, if need be, so text along it reads left to right.</summary>
        public static double Readable(double angle)
        {
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle <= -Math.PI) angle += 2 * Math.PI;
            if (angle > Math.PI / 2 + 1e-12) angle -= Math.PI;
            else if (angle <= -Math.PI / 2 + 1e-12) angle += Math.PI;
            return angle;
        }
    }
}
