using System;
using System.Collections.Generic;

namespace FieldCodes.Sheets
{
    /// <summary>A vertex of a drawn break line, in model-space drawing units.</summary>
    public struct BreakPoint
    {
        public double X;
        public double Y;

        public BreakPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// The zigzag a drafter calls a lightning line: what a sheet's edge is drawn as so the
    /// reader can see the drawing carries on somewhere else.
    ///
    /// It is drawn in model space along the seam a viewport cuts, so both sheets show the same
    /// zigzag from their own side and the two halves of a run meet on the page. That means the
    /// teeth have to be sized in plotted units and converted through the viewport's scale --
    /// otherwise a 1" = 20' sheet and a 1" = 100' sheet show wildly different zigzags.
    /// </summary>
    public static class BreakLine
    {
        /// <summary>
        /// The zigzag between two points. It starts and ends exactly on them -- a match line
        /// that overshot its own corner would not meet the next sheet -- and the teeth alternate
        /// either side of the straight run between. Amplitude and period are in drawing units.
        ///
        /// A run too short for even one tooth comes back as the straight line it should be.
        /// </summary>
        public static IList<BreakPoint> Zigzag(double x1, double y1, double x2, double y2,
                                               double amplitude, double period)
        {
            var points = new List<BreakPoint> { new BreakPoint(x1, y1) };

            var dx = x2 - x1;
            var dy = y2 - y1;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0 || amplitude <= 0 || period <= 0)
            {
                points.Add(new BreakPoint(x2, y2));
                return points;
            }

            // A tooth either side per period, so the step between vertices is half of one.
            var step = period / 2.0;
            var steps = (int)Math.Round(length / step, MidpointRounding.AwayFromZero);
            if (steps < 2)
            {
                points.Add(new BreakPoint(x2, y2));
                return points;
            }

            var ux = dx / length;
            var uy = dy / length;
            var nx = -uy;
            var ny = ux;

            for (var i = 1; i < steps; i++)
            {
                var along = length * i / steps;
                var side = (i % 2 == 1) ? 1.0 : -1.0;
                points.Add(new BreakPoint(x1 + ux * along + nx * amplitude * side,
                                          y1 + uy * along + ny * amplitude * side));
            }

            points.Add(new BreakPoint(x2, y2));
            return points;
        }

        /// <summary>
        /// Where a point on the sheet lands in the survey, through a viewport.
        ///
        /// scale is model units per paper unit -- the viewport's view height divided by its
        /// height on the sheet. twist is the viewport's own rotation in radians, which a
        /// turned view needs and a north-up one leaves at zero.
        /// </summary>
        public static BreakPoint PaperToModel(double paperX, double paperY,
                                              double paperCenterX, double paperCenterY,
                                              double viewCenterX, double viewCenterY,
                                              double scale, double twistRadians)
        {
            var dx = (paperX - paperCenterX) * scale;
            var dy = (paperY - paperCenterY) * scale;
            var cos = Math.Cos(twistRadians);
            var sin = Math.Sin(twistRadians);
            return new BreakPoint(viewCenterX + dx * cos - dy * sin,
                                  viewCenterY + dx * sin + dy * cos);
        }

        /// <summary>
        /// The four corners of a viewport's edge, in model space, in order: lower left, lower
        /// right, upper right, upper left. Picking a side is then picking a pair of them.
        /// </summary>
        public static IList<BreakPoint> ViewportCornersInModel(double paperCenterX, double paperCenterY,
                                                              double paperWidth, double paperHeight,
                                                              double viewCenterX, double viewCenterY,
                                                              double scale, double twistRadians)
        {
            var halfWidth = paperWidth / 2.0;
            var halfHeight = paperHeight / 2.0;
            var corners = new[]
            {
                new[] { paperCenterX - halfWidth, paperCenterY - halfHeight },
                new[] { paperCenterX + halfWidth, paperCenterY - halfHeight },
                new[] { paperCenterX + halfWidth, paperCenterY + halfHeight },
                new[] { paperCenterX - halfWidth, paperCenterY + halfHeight }
            };

            var model = new List<BreakPoint>();
            foreach (var corner in corners)
                model.Add(PaperToModel(corner[0], corner[1], paperCenterX, paperCenterY,
                                       viewCenterX, viewCenterY, scale, twistRadians));
            return model;
        }

        /// <summary>
        /// Which side of a four-cornered shape a point is nearest, as the index of the corner
        /// the side starts at. How "pick the side the match line follows" is answered.
        /// </summary>
        public static int NearestSide(IList<BreakPoint> corners, double x, double y)
        {
            var best = 0;
            var bestDistance = double.MaxValue;
            for (var i = 0; i < corners.Count; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % corners.Count];
                var distance = DistanceToSegment(x, y, a.X, a.Y, b.X, b.Y);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            return best;
        }

        private static double DistanceToSegment(double px, double py,
                                                double x1, double y1, double x2, double y2)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0) return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));

            var t = ((px - x1) * dx + (py - y1) * dy) / lengthSquared;
            t = Math.Max(0, Math.Min(1, t));
            var cx = x1 + t * dx;
            var cy = y1 + t * dy;
            return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }
    }
}
