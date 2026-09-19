// Minimal stand-ins for the AutoCAD .NET geometry types. Compile-only: nothing here runs.
using System;

namespace Autodesk.AutoCAD.Geometry
{
    public struct Tolerance
    {
        public Tolerance(double equalPoint, double equalVector) { EqualPoint = equalPoint; EqualVector = equalVector; }
        public double EqualPoint { get; }
        public double EqualVector { get; }
        public static Tolerance Global { get { return new Tolerance(1e-10, 1e-10); } }
    }

    public struct Point2d
    {
        public Point2d(double x, double y) { X = x; Y = y; }
        public double X { get; }
        public double Y { get; }
        public static Point2d Origin { get { return new Point2d(0, 0); } }
        public double GetDistanceTo(Point2d other) { return Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y)); }
        public Vector2d GetVectorTo(Point2d other) { return new Vector2d(other.X - X, other.Y - Y); }
        public Point2d Add(Vector2d v) { return new Point2d(X + v.X, Y + v.Y); }
        public Point2d Subtract(Vector2d v) { return new Point2d(X - v.X, Y - v.Y); }
        public Vector2d GetAsVector() { return new Vector2d(X, Y); }
        public bool IsEqualTo(Point2d other) { return X == other.X && Y == other.Y; }
        public bool IsEqualTo(Point2d other, Tolerance tol) { return GetDistanceTo(other) <= tol.EqualPoint; }
        public static Point2d operator +(Point2d p, Vector2d v) { return p.Add(v); }
        public static Point2d operator -(Point2d p, Vector2d v) { return p.Subtract(v); }
        public static Vector2d operator -(Point2d a, Point2d b) { return new Vector2d(a.X - b.X, a.Y - b.Y); }
    }

    public struct Vector2d
    {
        public Vector2d(double x, double y) { X = x; Y = y; }
        public double X { get; }
        public double Y { get; }
        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }
        public double Angle { get { return Math.Atan2(Y, X); } }
        public static Vector2d XAxis { get { return new Vector2d(1, 0); } }
        public static Vector2d YAxis { get { return new Vector2d(0, 1); } }
        public Vector2d GetNormal() { var l = Length; return new Vector2d(X / l, Y / l); }
        public Vector2d MultiplyBy(double s) { return new Vector2d(X * s, Y * s); }
        public Vector2d Negate() { return new Vector2d(-X, -Y); }
        public Vector2d GetPerpendicularVector() { return new Vector2d(-Y, X); }
        public double DotProduct(Vector2d v) { return X * v.X + Y * v.Y; }
        public double GetAngleTo(Vector2d v) { return Math.Acos(DotProduct(v) / (Length * v.Length)); }
        public Vector2d RotateBy(double angle) { var c = Math.Cos(angle); var s = Math.Sin(angle); return new Vector2d(X * c - Y * s, X * s + Y * c); }
        public bool IsZeroLength() { return X == 0 && Y == 0; }
        public static Vector2d operator *(Vector2d v, double s) { return v.MultiplyBy(s); }
        public static Vector2d operator *(double s, Vector2d v) { return v.MultiplyBy(s); }
        public static Vector2d operator /(Vector2d v, double s) { return v.MultiplyBy(1 / s); }
        public static Vector2d operator +(Vector2d a, Vector2d b) { return new Vector2d(a.X + b.X, a.Y + b.Y); }
        public static Vector2d operator -(Vector2d a, Vector2d b) { return new Vector2d(a.X - b.X, a.Y - b.Y); }
        public static Vector2d operator -(Vector2d a) { return a.Negate(); }
    }

    public struct Point3d
    {
        public Point3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public static Point3d Origin { get { return new Point3d(0, 0, 0); } }
        public double DistanceTo(Point3d other) { return Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y) + (Z - other.Z) * (Z - other.Z)); }
        public Vector3d GetVectorTo(Point3d other) { return new Vector3d(other.X - X, other.Y - Y, other.Z - Z); }
        public Point3d Add(Vector3d v) { return new Point3d(X + v.X, Y + v.Y, Z + v.Z); }
        public Point3d Subtract(Vector3d v) { return new Point3d(X - v.X, Y - v.Y, Z - v.Z); }
        public Vector3d GetAsVector() { return new Vector3d(X, Y, Z); }
        public Point3d TransformBy(Matrix3d m) { return m.Apply(this); }
        public Point2d Convert2d(Plane plane) { return new Point2d(X, Y); }
        public bool IsEqualTo(Point3d other) { return X == other.X && Y == other.Y && Z == other.Z; }
        public bool IsEqualTo(Point3d other, Tolerance tol) { return DistanceTo(other) <= tol.EqualPoint; }
        public static Point3d operator +(Point3d p, Vector3d v) { return p.Add(v); }
        public static Point3d operator -(Point3d p, Vector3d v) { return p.Subtract(v); }
        public static Vector3d operator -(Point3d a, Point3d b) { return new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public override string ToString() { return "(" + X + "," + Y + "," + Z + ")"; }
    }

    public struct Vector3d
    {
        public Vector3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public static Vector3d XAxis { get { return new Vector3d(1, 0, 0); } }
        public static Vector3d YAxis { get { return new Vector3d(0, 1, 0); } }
        public static Vector3d ZAxis { get { return new Vector3d(0, 0, 1); } }
        public Vector3d GetNormal() { var l = Length; return new Vector3d(X / l, Y / l, Z / l); }
        public Vector3d MultiplyBy(double s) { return new Vector3d(X * s, Y * s, Z * s); }
        public Vector3d DivideBy(double s) { return new Vector3d(X / s, Y / s, Z / s); }
        public Vector3d Negate() { return new Vector3d(-X, -Y, -Z); }
        public Vector3d Add(Vector3d v) { return new Vector3d(X + v.X, Y + v.Y, Z + v.Z); }
        public Vector3d Subtract(Vector3d v) { return new Vector3d(X - v.X, Y - v.Y, Z - v.Z); }
        public double DotProduct(Vector3d v) { return X * v.X + Y * v.Y + Z * v.Z; }
        public Vector3d CrossProduct(Vector3d v) { return new Vector3d(Y * v.Z - Z * v.Y, Z * v.X - X * v.Z, X * v.Y - Y * v.X); }
        public double GetAngleTo(Vector3d v) { return Math.Acos(DotProduct(v) / (Length * v.Length)); }
        public double GetAngleTo(Vector3d v, Vector3d reference) { return GetAngleTo(v); }
        public Vector3d GetPerpendicularVector() { return new Vector3d(-Y, X, 0); }
        public Vector3d RotateBy(double angle, Vector3d axis) { var c = Math.Cos(angle); var s = Math.Sin(angle); return new Vector3d(X * c - Y * s, X * s + Y * c, Z); }
        public Vector3d TransformBy(Matrix3d m) { return this; }
        public bool IsZeroLength() { return X == 0 && Y == 0 && Z == 0; }
        public bool IsParallelTo(Vector3d v) { return CrossProduct(v).IsZeroLength(); }
        public Vector2d Convert2d(Plane plane) { return new Vector2d(X, Y); }
        public static Vector3d operator *(Vector3d v, double s) { return v.MultiplyBy(s); }
        public static Vector3d operator *(double s, Vector3d v) { return v.MultiplyBy(s); }
        public static Vector3d operator /(Vector3d v, double s) { return v.DivideBy(s); }
        public static Vector3d operator +(Vector3d a, Vector3d b) { return a.Add(b); }
        public static Vector3d operator -(Vector3d a, Vector3d b) { return a.Subtract(b); }
        public static Vector3d operator -(Vector3d a) { return a.Negate(); }
    }

    public struct Scale3d
    {
        public Scale3d(double s) { X = s; Y = s; Z = s; }
        public Scale3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    public struct Matrix3d
    {
        private readonly Vector3d _shift;
        private Matrix3d(Vector3d shift) { _shift = shift; }
        public static Matrix3d Identity { get { return new Matrix3d(new Vector3d(0, 0, 0)); } }
        public static Matrix3d Displacement(Vector3d v) { return new Matrix3d(v); }
        public static Matrix3d Rotation(double angle, Vector3d axis, Point3d center) { return Identity; }
        public static Matrix3d Scaling(double scale, Point3d center) { return Identity; }
        public static Matrix3d Mirroring(Plane plane) { return Identity; }
        public static Matrix3d AlignCoordinateSystem(Point3d fo, Vector3d fx, Vector3d fy, Vector3d fz, Point3d to, Vector3d tx, Vector3d ty, Vector3d tz) { return Identity; }
        public Matrix3d PreMultiplyBy(Matrix3d m) { return this; }
        public Matrix3d PostMultiplyBy(Matrix3d m) { return this; }
        public Matrix3d Inverse() { return this; }
        public Vector3d Translation { get { return _shift; } }
        public Point3d Apply(Point3d p) { return p.Add(_shift); }
        public static Matrix3d operator *(Matrix3d a, Matrix3d b) { return a; }
    }

    public sealed class Plane
    {
        public Plane() { }
        public Plane(Point3d origin, Vector3d normal) { }
        public Vector3d Normal { get { return Vector3d.ZAxis; } }
    }

    public struct Extents3d
    {
        public Extents3d(Point3d min, Point3d max) { MinPoint = min; MaxPoint = max; }
        public Point3d MinPoint { get; private set; }
        public Point3d MaxPoint { get; private set; }
        public void AddPoint(Point3d p)
        {
            MinPoint = new Point3d(Math.Min(MinPoint.X, p.X), Math.Min(MinPoint.Y, p.Y), Math.Min(MinPoint.Z, p.Z));
            MaxPoint = new Point3d(Math.Max(MaxPoint.X, p.X), Math.Max(MaxPoint.Y, p.Y), Math.Max(MaxPoint.Z, p.Z));
        }
        public void AddExtents(Extents3d e) { AddPoint(e.MinPoint); AddPoint(e.MaxPoint); }
        public void TransformBy(Matrix3d m) { }
    }

    public struct Extents2d
    {
        public Extents2d(Point2d min, Point2d max) { MinPoint = min; MaxPoint = max; }
        public Point2d MinPoint { get; }
        public Point2d MaxPoint { get; }
    }

    public sealed class Point2dCollection : System.Collections.Generic.List<Point2d>
    {
        public Point2dCollection() { }
        public Point2dCollection(Point2d[] points) { AddRange(points); }
    }

    public sealed class Point3dCollection : System.Collections.Generic.List<Point3d>
    {
        public Point3dCollection() { }
        public Point3dCollection(Point3d[] points) { AddRange(points); }
    }

    public sealed class DoubleCollection : System.Collections.Generic.List<double>
    {
        public DoubleCollection() { }
        public DoubleCollection(double[] values) { AddRange(values); }
    }

    public sealed class LineSegment2d
    {
        public LineSegment2d(Point2d a, Point2d b) { StartPoint = a; EndPoint = b; }
        public Point2d StartPoint { get; }
        public Point2d EndPoint { get; }
        public double Length { get { return StartPoint.GetDistanceTo(EndPoint); } }
        public Vector2d Direction { get { return StartPoint.GetVectorTo(EndPoint).GetNormal(); } }
    }

    public sealed class LineSegment3d
    {
        public LineSegment3d(Point3d a, Point3d b) { StartPoint = a; EndPoint = b; }
        public Point3d StartPoint { get; }
        public Point3d EndPoint { get; }
        public double Length { get { return StartPoint.DistanceTo(EndPoint); } }
        public Vector3d Direction { get { return StartPoint.GetVectorTo(EndPoint).GetNormal(); } }
    }

    public sealed class CircularArc2d
    {
        public CircularArc2d(Point2d center, double radius, double start, double end, Vector2d reference, bool clockwise) { Center = center; Radius = radius; StartAngle = start; EndAngle = end; }
        public Point2d Center { get; }
        public double Radius { get; }
        public double StartAngle { get; }
        public double EndAngle { get; }
        public Point2d StartPoint { get { return Center; } }
        public Point2d EndPoint { get { return Center; } }
        public bool IsClockWise { get { return false; } }
    }

    public sealed class CircularArc3d
    {
        public CircularArc3d(Point3d center, Vector3d normal, Vector3d reference, double radius, double start, double end) { Center = center; Radius = radius; StartAngle = start; EndAngle = end; Normal = normal; }
        public Point3d Center { get; }
        public double Radius { get; }
        public double StartAngle { get; }
        public double EndAngle { get; }
        public Vector3d Normal { get; }
        public Point3d StartPoint { get { return Center; } }
        public Point3d EndPoint { get { return Center; } }
    }
}
