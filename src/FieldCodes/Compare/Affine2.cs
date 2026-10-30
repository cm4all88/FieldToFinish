using System;

namespace FieldCodes.Compare
{
    /// <summary>
    /// A planar affine transform (rotation, uniform or non-uniform scale, translation)
    /// with a separate Z scale and offset, as an xref or block insertion applies to its
    /// contents. The CAD reader builds one from each insertion's matrix and composes them
    /// down the nesting; the geometry stored for comparison is always in the survey
    /// drawing's coordinates. Kept CAD-free so the arithmetic is unit tested.
    /// </summary>
    public sealed class Affine2
    {
        public double M11, M12, M21, M22, Tx, Ty, Sz, Tz;

        public Affine2() { M11 = 1; M22 = 1; Sz = 1; }

        public static Affine2 Identity { get { return new Affine2(); } }

        public static Affine2 Translation(double tx, double ty) { return new Affine2 { Tx = tx, Ty = ty }; }

        /// <summary>Rotation (radians, counter-clockwise) then uniform scale then translation.</summary>
        public static Affine2 Insertion(double tx, double ty, double rotation, double scale)
        {
            var c = Math.Cos(rotation) * scale;
            var s = Math.Sin(rotation) * scale;
            return new Affine2 { M11 = c, M12 = -s, M21 = s, M22 = c, Tx = tx, Ty = ty, Sz = scale };
        }

        public static Affine2 Insertion(double tx, double ty, double tz, double rotation, double sx, double sy, double sz)
        {
            var c = Math.Cos(rotation);
            var s = Math.Sin(rotation);
            return new Affine2 { M11 = c * sx, M12 = -s * sy, M21 = s * sx, M22 = c * sy, Tx = tx, Ty = ty, Tz = tz, Sz = sz };
        }

        public CPoint Apply(CPoint p)
        {
            var x = M11 * p.X + M12 * p.Y + Tx;
            var y = M21 * p.X + M22 * p.Y + Ty;
            return new CPoint(x, y, p.HasZ ? p.Z * Sz + Tz : double.NaN);
        }

        /// <summary>The transform that applies <paramref name="inner"/> first, then this one.</summary>
        public Affine2 Then(Affine2 inner)
        {
            if (inner == null) return this;
            return new Affine2
            {
                M11 = M11 * inner.M11 + M12 * inner.M21,
                M12 = M11 * inner.M12 + M12 * inner.M22,
                M21 = M21 * inner.M11 + M22 * inner.M21,
                M22 = M21 * inner.M12 + M22 * inner.M22,
                Tx = M11 * inner.Tx + M12 * inner.Ty + Tx,
                Ty = M21 * inner.Tx + M22 * inner.Ty + Ty,
                Sz = Sz * inner.Sz,
                Tz = Sz * inner.Tz + Tz
            };
        }

        /// <summary>Planar scale factor (square root of the determinant's magnitude).</summary>
        public double Scale { get { return Math.Sqrt(Math.Abs(M11 * M22 - M12 * M21)); } }

        public bool IsUniform
        {
            get
            {
                var sx = Math.Sqrt(M11 * M11 + M21 * M21);
                var sy = Math.Sqrt(M12 * M12 + M22 * M22);
                return Math.Abs(sx - sy) < 1e-9 * Math.Max(1, sx);
            }
        }

        public bool IsIdentity
        {
            get
            {
                return Math.Abs(M11 - 1) < 1e-12 && Math.Abs(M22 - 1) < 1e-12 && Math.Abs(M12) < 1e-12 && Math.Abs(M21) < 1e-12 &&
                       Math.Abs(Tx) < 1e-12 && Math.Abs(Ty) < 1e-12 && Math.Abs(Sz - 1) < 1e-12 && Math.Abs(Tz) < 1e-12;
            }
        }

        public double RotationDegrees { get { return Math.Atan2(M21, M11) * 180.0 / Math.PI; } }

        public override string ToString()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "rotate {0:0.##}°, scale {1:0.####}, move ({2:0.##}, {3:0.##})", RotationDegrees, Scale, Tx, Ty);
        }
    }
}
