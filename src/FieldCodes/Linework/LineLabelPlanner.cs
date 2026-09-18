using System;
using System.Collections.Generic;
using System.Globalization;

namespace FieldCodes.Linework
{
    /// <summary>
    /// A path along existing linework. The CAD layer adapts an AutoCAD Curve to this;
    /// tests use simple synthetic paths. The planner never sees the entity itself,
    /// which is what makes it structurally incapable of modifying it.
    /// </summary>
    public interface ILinePath
    {
        /// <summary>Total length, in drawing units.</summary>
        double Length { get; }

        /// <summary>Point and tangent direction (radians, CCW from east) at a
        /// distance along the path.</summary>
        void At(double distance, out double x, out double y, out double directionRadians);
    }

    /// <summary>Placement options, already converted to drawing units.</summary>
    public sealed class LineLabelOptions
    {
        /// <summary>Features shorter than this get no label.</summary>
        public double MinLength { get; set; }

        /// <summary>Spacing between repeated labels on long features.</summary>
        public double RepeatInterval { get; set; }

        /// <summary>Labels keep this far from the feature's ends.</summary>
        public double EndClearance { get; set; }

        /// <summary>Text follows the line, or stays horizontal.</summary>
        public bool AlignToLine { get; set; }

        /// <summary>Which side of the line the labels sit on, already resolved --
        /// Auto must be settled before the planner runs.</summary>
        public LineLabelSide Side { get; set; }

        /// <summary>Perpendicular offset for Left/Right placement, drawing units.</summary>
        public double SideOffset { get; set; }

        public LineLabelOptions()
        {
            MinLength = 10.0;
            RepeatInterval = 200.0;
            EndClearance = 5.0;
            AlignToLine = true;
            Side = LineLabelSide.OnLine;
            SideOffset = 1.0;
        }
    }

    /// <summary>One label the engine should place.</summary>
    public sealed class PlannedLineLabel
    {
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Final text rotation in radians, already normalised so the text
        /// can never read upside down.</summary>
        public double RotationRadians { get; set; }

        /// <summary>Distance along the path, for diagnostics.</summary>
        public double Distance { get; set; }
    }

    /// <summary>
    /// Decides where labels go along existing linework.
    ///
    /// One label at the midpoint of short features; evenly spaced labels roughly a
    /// repeat-interval apart on long ones, all inside the end clearance. Text follows
    /// the line's own direction but is flipped whenever it would read upside down --
    /// a label on a west-bound line reads the same as on an east-bound one.
    ///
    /// Pure geometry: no Autodesk types, no entity access, fully unit-tested.
    /// </summary>
    public static class LineLabelPlanner
    {
        public static IList<PlannedLineLabel> Plan(ILinePath path, LineLabelOptions options)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (options == null) options = new LineLabelOptions();

            var labels = new List<PlannedLineLabel>();

            var length = path.Length;
            if (length <= 0 || length < options.MinLength) return labels;

            var usable = length - 2.0 * options.EndClearance;

            // Clearance has eaten the whole feature: one label at the midpoint is
            // still more useful than nothing on a feature past the minimum length.
            if (usable <= 0)
            {
                labels.Add(LabelAt(path, length / 2.0, options));
                return labels;
            }

            // How many labels the run earns. Short of one interval: one, centred.
            var count = Math.Max(1, (int)Math.Round(usable / options.RepeatInterval));

            var step = usable / count;
            for (var i = 0; i < count; i++)
            {
                var distance = options.EndClearance + step * (i + 0.5);
                labels.Add(LabelAt(path, distance, options));
            }

            return labels;
        }

        private static PlannedLineLabel LabelAt(ILinePath path, double distance,
                                                LineLabelOptions options)
        {
            double x, y, direction;
            path.At(distance, out x, out y, out direction);

            return PlaceAt(x, y, direction, options.Side, options.SideOffset,
                           options.AlignToLine, distance);
        }

        /// <summary>
        /// Places one label given a point on the line and the local tangent there.
        /// This is the single placement calculation -- the bulk planner and the
        /// interactive FTFLABELLINE jig both come through here, so a preview can
        /// never disagree with a placed label.
        ///
        /// The side offset uses the RAW tangent, before any readability flip: left
        /// and right are properties of the line's own direction, and flipping the
        /// text so it reads right-side-up must never move a LEFT label to the
        /// right side. The left normal of direction d is (-sin d, cos d).
        /// </summary>
        public static PlannedLineLabel PlaceAt(double x, double y, double directionRadians,
                                               LineLabelSide side, double sideOffset,
                                               bool alignToLine, double distance = 0.0)
        {
            if (side == LineLabelSide.Left)
            {
                x += sideOffset * -Math.Sin(directionRadians);
                y += sideOffset * Math.Cos(directionRadians);
            }
            else if (side == LineLabelSide.Right)
            {
                x -= sideOffset * -Math.Sin(directionRadians);
                y -= sideOffset * Math.Cos(directionRadians);
            }

            return new PlannedLineLabel
            {
                X = x,
                Y = y,
                Distance = distance,
                RotationRadians = alignToLine
                    ? NormalizeReadable(directionRadians)
                    : 0.0
            };
        }

        /// <summary>
        /// Places one label centred BETWEEN two edges -- a driveway between its two
        /// edge-of-asphalt lines. Each input is the nearest point on one edge and
        /// the tangent there; the label sits at their midpoint, following their
        /// averaged direction. The edges may be digitized in opposite directions
        /// (TBC often does): the second tangent is flipped into agreement with the
        /// first before averaging, so the text never averages to sideways.
        /// </summary>
        public static PlannedLineLabel PlaceBetween(double ax, double ay,
                                                    double aDirectionRadians,
                                                    double bx, double by,
                                                    double bDirectionRadians,
                                                    bool alignToLine)
        {
            var cosA = Math.Cos(aDirectionRadians);
            var sinA = Math.Sin(aDirectionRadians);
            var cosB = Math.Cos(bDirectionRadians);
            var sinB = Math.Sin(bDirectionRadians);

            if (cosA * cosB + sinA * sinB < 0) { cosB = -cosB; sinB = -sinB; }

            var sumX = cosA + cosB;
            var sumY = sinA + sinB;
            var direction = Math.Abs(sumX) < 1e-12 && Math.Abs(sumY) < 1e-12
                ? aDirectionRadians                     // perpendicular edges: degenerate
                : Math.Atan2(sumY, sumX);

            return PlaceAt((ax + bx) / 2.0, (ay + by) / 2.0, direction,
                           LineLabelSide.OnLine, 0.0, alignToLine);
        }

        /// <summary>
        /// The shared direction of a family of roughly parallel lines -- stair
        /// treads, for one. Each direction is folded into agreement with the first
        /// (lines digitized opposite ways must not cancel out), then the vectors
        /// average. Returns 0 for an empty set.
        /// </summary>
        public static double AverageDirection(IList<double> directionsRadians)
        {
            if (directionsRadians == null || directionsRadians.Count == 0) return 0.0;

            var cos0 = Math.Cos(directionsRadians[0]);
            var sin0 = Math.Sin(directionsRadians[0]);
            var sumX = 0.0;
            var sumY = 0.0;

            foreach (var direction in directionsRadians)
            {
                var cos = Math.Cos(direction);
                var sin = Math.Sin(direction);
                if (cos * cos0 + sin * sin0 < 0) { cos = -cos; sin = -sin; }
                sumX += cos;
                sumY += sin;
            }

            return Math.Abs(sumX) < 1e-12 && Math.Abs(sumY) < 1e-12
                ? directionsRadians[0]
                : Math.Atan2(sumY, sumX);
        }

        /// <summary>
        /// Folds a direction into the readable half-plane: anything pointing into the
        /// left half (which would render the text upside down) is flipped 180 degrees.
        /// The result is always in (-90, 90] degrees.
        /// </summary>
        public static double NormalizeReadable(double radians)
        {
            var twoPi = Math.PI * 2.0;
            var angle = radians % twoPi;
            if (angle < 0) angle += twoPi;

            // (90, 270] degrees reads upside down; flip it.
            if (angle > Math.PI / 2.0 && angle <= Math.PI * 1.5)
                angle -= Math.PI;

            if (angle < 0) angle += twoPi;
            if (angle > Math.PI) angle -= twoPi;    // report as (-90, 90]

            return angle;
        }

        /// <summary>
        /// The human sentence the preview shows for one feature, before anything is
        /// drawn. Lengths arrive in survey feet.
        /// </summary>
        public static string Describe(string labelText, double lengthFeet,
                                      double minLengthFeet, double repeatIntervalFeet,
                                      double endClearanceFeet,
                                      LineLabelSide side = LineLabelSide.OnLine,
                                      double sideOffsetFeet = 0.0)
        {
            if (string.IsNullOrEmpty(labelText))
                return "Identified; labelling standard not configured yet";

            if (lengthFeet < minLengthFeet)
                return string.Format(CultureInfo.InvariantCulture,
                    "No label - shorter than the {0:0.#} ft minimum", minLengthFeet);

            var usable = lengthFeet - 2.0 * endClearanceFeet;
            var count = usable <= 0
                ? 1
                : Math.Max(1, (int)Math.Round(usable / repeatIntervalFeet));

            var placement = LineSideParser.Describe(side, sideOffsetFeet);

            return count == 1
                ? string.Format(CultureInfo.InvariantCulture,
                    "Label existing line \"{0}\" - 1 label at midpoint, {1}",
                    labelText, placement)
                : string.Format(CultureInfo.InvariantCulture,
                    "Label existing line \"{0}\" - {1} labels, ~{2:0} ft apart, {3}",
                    labelText, count, usable / count, placement);
        }
    }
}
