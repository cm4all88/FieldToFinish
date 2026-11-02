using System;
using FieldCodes.Easements;
using FieldCodes.Geometry;
using FieldCodes.Orient;

namespace FieldCodes.Symbols
{
    /// <summary>
    /// The arithmetic of a symbol preview, kept out of the WinForms control so it can be tested:
    /// fitting a block into a box, turning it about its insertion point the way a marker rotation
    /// does, and where its native-zero arrow points.
    ///
    /// Block coordinates: AutoCAD's, x east, y north, the insertion point at 0,0. Screen
    /// coordinates: pixels, y down. A rotation is AutoCAD degrees, counter-clockwise.
    /// </summary>
    public sealed class PreviewFrame
    {
        private PreviewFrame(double scale, double centerX, double centerY)
        {
            Scale = scale;
            CenterX = centerX;
            CenterY = centerY;
        }

        /// <summary>Pixels per drawing unit.</summary>
        public double Scale { get; private set; }

        /// <summary>Where the insertion point lands on screen: the middle of the box.</summary>
        public double CenterX { get; private set; }
        public double CenterY { get; private set; }

        public P2 Insertion { get { return new P2(CenterX, CenterY); } }

        /// <summary>
        /// A frame that shows a circle of <paramref name="radius"/> (drawing units, about the
        /// insertion point) inside a <paramref name="width"/> x <paramref name="height"/> box with
        /// <paramref name="margin"/> pixels to spare. Fitting a circle rather than the block's box
        /// keeps the zoom fixed while the symbol turns. A zero radius (an empty block) gets a unit.
        /// </summary>
        public static PreviewFrame Fit(double radius, double width, double height, double margin)
        {
            var r = radius > 1e-12 && !double.IsInfinity(radius) ? radius : 1.0;
            var half = Math.Min(width, height) / 2.0 - Math.Max(0, margin);
            if (half < 1) half = 1;
            return new PreviewFrame(half / r, width / 2.0, height / 2.0);
        }

        /// <summary>A point of the block, turned <paramref name="rotationDegrees"/> about the
        /// insertion point, on screen.</summary>
        public P2 ToScreen(P2 block, double rotationDegrees)
        {
            var turned = Rotate(block, rotationDegrees);
            return new P2(CenterX + turned.X * Scale, CenterY - turned.Y * Scale);
        }

        /// <summary>The screen point <paramref name="length"/> drawing units from the insertion
        /// point in the AutoCAD direction <paramref name="cadDegrees"/>.</summary>
        public P2 Toward(double cadDegrees, double length)
        {
            var a = Angles.ToRadians(cadDegrees);
            return new P2(CenterX + Math.Cos(a) * length * Scale, CenterY - Math.Sin(a) * length * Scale);
        }

        /// <summary>
        /// The direction, in the BLOCK's own frame (AutoCAD degrees, [0, 360)), of a screen point
        /// seen from the insertion point while the block is drawn turned by
        /// <paramref name="rotationDegrees"/>: what a click on the preview means as a native zero.
        /// Null on the insertion point itself.
        /// </summary>
        public double? BlockAngleAt(P2 screen, double rotationDegrees)
        {
            var dx = screen.X - CenterX;
            var dy = CenterY - screen.Y;
            if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return null;
            var screenAngle = Angles.ToDegrees(Math.Atan2(dy, dx));
            return Angles.NormalizeDegrees(screenAngle - rotationDegrees);
        }

        /// <summary>A point turned counter-clockwise about the origin.</summary>
        public static P2 Rotate(P2 p, double cadDegrees)
        {
            var a = Angles.ToRadians(cadDegrees);
            var cos = Math.Cos(a);
            var sin = Math.Sin(a);
            return new P2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos);
        }
    }

    /// <summary>What a symbol preview shows for a rule and a sample facing.</summary>
    public static class SymbolPreview
    {
        /// <summary>The native-zero arrow reaches a little past the symbol.</summary>
        public static double ArrowLength(double blockRadius)
        {
            return blockRadius > 1e-12 ? blockRadius * 1.15 : 1.0;
        }

        /// <summary>The radius the preview fits: the symbol and its arrow, with a little room.</summary>
        public static double FitRadius(BlockSketch sketch)
        {
            var r = sketch != null ? sketch.Radius() : 0.0;
            return ArrowLength(r) * 1.1;
        }

        /// <summary>
        /// The marker rotation (AutoCAD degrees) that turns a symbol whose native zero is
        /// <paramref name="referenceDegrees"/> to face <paramref name="azimuth"/>, exactly as
        /// FTFPOINTS and FTFORIENT compute it (<see cref="SymbolRotation.MarkerDegrees"/>); 0 for a
        /// symbol that is never turned (period 0).
        /// </summary>
        public static double MarkerRotation(double referenceDegrees, double period, double azimuth)
        {
            var rule = new SymbolRule { ReferenceCadDegrees = referenceDegrees, Period = period };
            return SymbolRotation.MarkerDegrees(rule, azimuth) ?? 0.0;
        }

        /// <summary>Where the native-zero arrow points once the block is turned: the native zero
        /// plus the rotation, AutoCAD degrees in [0, 360).</summary>
        public static double ArrowCadDegrees(double referenceDegrees, double rotationDegrees)
        {
            return Angles.NormalizeDegrees(referenceDegrees + rotationDegrees);
        }

        /// <summary>The same arrow as a survey azimuth (clockwise from north).</summary>
        public static double ArrowAzimuth(double referenceDegrees, double rotationDegrees)
        {
            return Angles.NormalizeDegrees(90.0 - ArrowCadDegrees(referenceDegrees, rotationDegrees));
        }

        /// <summary>"Faces az 135; marker turned 225.0" -- the line under a preview.</summary>
        public static string Describe(double referenceDegrees, double period, double azimuth)
        {
            if (period <= 0)
                return "Looks the same at every rotation: never turned.";
            var marker = MarkerRotation(referenceDegrees, period, azimuth);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Facing azimuth {0:0.#}: marker rotation {1:0.#} (native zero {2:0.#}{3}).",
                azimuth, marker, referenceDegrees, period < 360 - 1e-9 ? ", repeats every " + period.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
        }
    }
}
