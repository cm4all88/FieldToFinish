using System;

namespace FieldCodes.Geometry
{
    /// <summary>
    /// Every angle conversion in one tested place.
    ///
    /// Three conventions meet in this tool and mixing them is silent rather than loud:
    /// survey azimuth runs clockwise from north in degrees, AutoCAD rotation runs
    /// counter-clockwise from east in degrees, and the AutoCAD API takes radians.
    /// A sign rotated by the wrong one still draws, just facing the wrong way.
    /// </summary>
    public static class Angles
    {
        private const double TwoPi = Math.PI * 2.0;

        public static double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        public static double ToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        /// <summary>Folds degrees into [0, 360).</summary>
        public static double NormalizeDegrees(double degrees)
        {
            degrees %= 360.0;
            return degrees < 0 ? degrees + 360.0 : degrees;
        }

        /// <summary>Folds radians into [0, 2*pi).</summary>
        public static double NormalizeRadians(double radians)
        {
            radians %= TwoPi;
            return radians < 0 ? radians + TwoPi : radians;
        }

        /// <summary>
        /// Survey azimuth (clockwise from north) to AutoCAD rotation (counter-clockwise
        /// from east), both in degrees. A stop sign shot at 135 faces 315.
        /// </summary>
        public static double AzimuthToCadDegrees(double azimuthDegrees)
        {
            return NormalizeDegrees(90.0 - azimuthDegrees);
        }

        /// <summary>
        /// The value the AutoCAD API wants for a rotation expressed in AutoCAD degrees.
        /// This is the only place a CAD rotation should be converted.
        /// </summary>
        public static double CadDegreesToApiRadians(double cadDegrees)
        {
            return NormalizeRadians(ToRadians(cadDegrees));
        }
    }
}
