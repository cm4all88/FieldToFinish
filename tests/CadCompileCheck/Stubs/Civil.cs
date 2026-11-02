// Stand-ins for the Civil 3D API (AeccDbMgd) the plugin uses: the Civil document, COGO points,
// styles, settings, alignments, surfaces, survey figures, feature lines, pipe networks.
//
// Kept apart from the AutoCAD stand-ins on purpose: Autodesk does not publish Civil 3D's
// assemblies, so tests/RealApiCompileCheck compiles the plugin against the REAL AutoCAD 2024
// assemblies (the AutoCAD.NET NuGet package) plus only this file. Classes deriving from an
// AutoCAD database type therefore call the constructor the real AcDbMgd declares,
// (IntPtr unmanagedObjPtr, bool autoDelete), which the AutoCAD stand-ins declare too.
using System;
using System.Collections;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace Autodesk.Civil.ApplicationServices
{
    public sealed class CivilDocument
    {
        public static CivilDocument GetCivilDocument(Autodesk.AutoCAD.DatabaseServices.Database db) { return new CivilDocument(); }
        public Autodesk.Civil.DatabaseServices.Styles.StylesRoot Styles { get; } = new Autodesk.Civil.DatabaseServices.Styles.StylesRoot();
        public Autodesk.Civil.DatabaseServices.CogoPointCollection CogoPoints { get; } = new Autodesk.Civil.DatabaseServices.CogoPointCollection();
        public Autodesk.Civil.Settings.SettingsRoot Settings { get; } = new Autodesk.Civil.Settings.SettingsRoot();
        public Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection GetSitelessAlignmentIds() { return new Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection(); }
        public Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection GetSurfaceIds() { return new Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection(); }
    }
}

namespace Autodesk.Civil
{
    public enum FeatureLinePointType { AllPoints, PIPoint, ElevationPoint }
    public enum PointMarkerDisplayType { UseCustomMarker }
}

namespace Autodesk.Civil.Settings
{
    public sealed class SettingsRoot { public Autodesk.AutoCAD.DatabaseServices.ObjectId GetSettings<T>() { return Autodesk.AutoCAD.DatabaseServices.ObjectId.Null; } }
}

namespace Autodesk.Civil.DatabaseServices.Styles
{
    using Autodesk.AutoCAD.DatabaseServices;

    public sealed class StylesRoot
    {
        public LabelStylesRoot LabelStyles { get; } = new LabelStylesRoot();
        public StyleCollectionBase PointStyles { get; } = new StyleCollectionBase();
    }

    public sealed class LabelStylesRoot
    {
        public LabelStyleCollection GeneralLineLabelStyles { get; } = new LabelStyleCollection();
        public LabelStyleCollection GeneralCurveLabelStyles { get; } = new LabelStyleCollection();
        public LabelStyleCollection GeneralNoteLabelStyles { get; } = new LabelStyleCollection();
        public PointLabelStylesRoot PointLabelStyles { get; } = new PointLabelStylesRoot();
    }

    public sealed class PointLabelStylesRoot { public LabelStyleCollection LabelStyles { get; } = new LabelStyleCollection(); }

    public class StyleCollectionBase : IEnumerable<ObjectId>
    {
        public int Count { get; }
        public ObjectId this[string name] { get { return ObjectId.Null; } }
        public bool Contains(string name) { return false; }
        public ObjectId Add(string name) { return ObjectId.Null; }
        public IEnumerator<ObjectId> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public sealed class LabelStyleCollection : StyleCollectionBase { }

    public class StyleBase : DBObject { public StyleBase() : base(IntPtr.Zero, false) { } public string Name { get; set; } public string Description { get; set; } }
    public sealed class LabelStyle : StyleBase { }

    public sealed class PointStyle : StyleBase
    {
        public Autodesk.Civil.PointMarkerDisplayType MarkerType { get; set; }
        public DisplayStyle GetDisplayStylePlan(PointDisplayStyleType component) { return new DisplayStyle(); }
        public DisplayStyle GetDisplayStyleModel(PointDisplayStyleType component) { return new DisplayStyle(); }
    }

    public enum PointDisplayStyleType { Marker = 0, Label = 1 }

    public sealed class DisplayStyle
    {
        public string Layer { get; set; }
        public bool Visible { get; set; }
    }
}

namespace Autodesk.Civil.DatabaseServices
{
    using Autodesk.AutoCAD.DatabaseServices;
    using Autodesk.AutoCAD.Geometry;

    public class Label : Entity
    {
        public Label() : base(IntPtr.Zero, false) { }
        public ObjectId StyleId { get; set; }
        public string StyleName { get; set; }
        public bool Dragged { get; set; }
        public Point3d AnchorInfo { get; }
        public void ResetLocation() { }
        public void ResetLabel() { }
        public double GetLabelTextComponentOverride(ObjectId id) { return 0; }
    }

    public sealed class GeneralSegmentLabel : Label
    {
        // The two overloads Civil 3D 2024 really has (checked against AeccDbMgd.dll): a segment label takes the line
        // and the curve style together and uses the one that fits the segment.
        public static ObjectId Create(ObjectId featureId, double ratio) { return ObjectId.Null; }
        public static ObjectId Create(ObjectId featureId, double ratio, ObjectId lineLabelStyleId, ObjectId curveLabelStyleId) { return ObjectId.Null; }
        public double Ratio { get; set; }
        public ObjectId FeatureId { get; }
    }

    public class CogoPoint : Entity
    {
        public CogoPoint() : base(IntPtr.Zero, false) { }
        public uint PointNumber { get; set; }
        public string PointName { get; set; }
        public string RawDescription { get; set; }
        public string FullDescription { get; }
        public string DescriptionFormat { get; set; }
        public Point3d Location { get; set; }
        public double Easting { get; set; }
        public double Northing { get; set; }
        public double Elevation { get; set; }
        public ObjectId StyleId { get; set; }
        public ObjectId LabelStyleId { get; set; }
        public double LabelRotation { get; set; }
        public Point3d LabelLocation { get; set; }
        public bool IsLabelPinned { get; set; }
        public bool IsLabelVisible { get; set; }
        public bool IsLabelDragged { get; set; }
        public bool IsLabelStyleOverridden { get; set; }
        public double ScaleXY { get; set; }
        public double MarkerRotation { get; set; }
        public ObjectId PointGroupId { get; }
        public string PrimaryPointGroupName { get; }
        public void ResetLabelLocation() { }
        public void ResetLabel() { }
        public bool ApplyDescriptionKeys { get; set; }
        public string AdditionalDescription { get; set; }
    }

    public sealed class CogoPointCollection : IEnumerable<ObjectId>
    {
        public int Count { get; }
        public ObjectId Add(Point3d location) { return ObjectId.Null; }
        public ObjectId Add(Point3d location, string description) { return ObjectId.Null; }
        public ObjectId Add(Point3d location, string description, bool useNextPointNumber) { return ObjectId.Null; }
        public ObjectId Add(Point3d location, string description, bool useNextPointNumber, bool applyDescriptionKeys) { return ObjectId.Null; }
        public ObjectId GetPointByPointNumber(uint number) { return ObjectId.Null; }
        public bool Contains(uint number) { return false; }
        public void Remove(ObjectId id) { }
        public IEnumerator<ObjectId> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public class FeatureLine : Curve
    {
        public FeatureLine() : base(IntPtr.Zero, false) { }
        public int PointsCount { get; }
        public double Length2D { get; }
        public Point3dCollection GetPoints(FeatureLinePointType type) { return new Point3dCollection(); }
        public string StyleName { get; }
        public ObjectId StyleId { get; }
        public ObjectId SiteId { get; }
        public string Name { get; set; }
    }

    public sealed class SurveyFigure : FeatureLine { }

    public class Alignment : Entity
    {
        public Alignment() : base(IntPtr.Zero, false) { }
        public string Name { get; set; }
        public double Length { get; }
        public double StartingStation { get; }
        public double EndingStation { get; }
        public ObjectId StyleId { get; set; }
        public AlignmentEntityCollection Entities { get; }
    }

    // Alignment geometry: entities in order, each made of sub-entities (2D: alignments are plan geometry).
    public sealed class AlignmentEntityCollection
    {
        public int Count { get; }
        public AlignmentEntity GetEntityByOrder(int index) { return null; }
    }

    public abstract class AlignmentEntity
    {
        public int SubEntityCount { get; }
        public AlignmentSubEntity this[int index] { get { return null; } }
    }

    public abstract class AlignmentSubEntity { }

    public sealed class AlignmentSubEntityLine : AlignmentSubEntity
    {
        public Point2d StartPoint { get; }
        public Point2d EndPoint { get; }
    }

    public sealed class AlignmentSubEntityArc : AlignmentSubEntity
    {
        public Point2d StartPoint { get; }
        public Point2d EndPoint { get; }
        public Point2d CenterPoint { get; }
        public bool Clockwise { get; }
    }
    public class Parcel : Entity { public Parcel() : base(IntPtr.Zero, false) { } public string Name { get; set; } public double Area { get; } public double Perimeter { get; } }
    // Surfaces, as AeccDbMgd declares the members FTFSPOT and FTFCONTOUR use. Surface derives
    // from Civil's own Entity in the real assembly; here it sits straight on AutoCAD's.
    public abstract class Surface : Entity
    {
        protected Surface() : base(IntPtr.Zero, false) { }
        public string Name { get; set; }
        public string Description { get; set; }
        public ObjectId StyleId { get; set; }
        public string StyleName { get; set; }
        public bool IsOutOfDate { get; }
        public GeneralSurfaceProperties GetGeneralProperties() { return new GeneralSurfaceProperties(); }
        public void Rebuild() { }
    }

    public sealed class GeneralSurfaceProperties
    {
        public int NumberOfPoints { get; }
        public double MinimumElevation { get; }
        public double MaximumElevation { get; }
        public double MeanElevation { get; }
    }

    public enum ContourSmoothingType { AddVertices = 1, SplineCurve = 2 }

    public class TinSurface : Surface
    {
        public static ObjectId Create(Database database, string surfaceName) { return ObjectId.Null; }
        public void AddVertices(Point3dCollection locations) { }
        public double FindElevationAtXY(double x, double y) { return 0; }
        /// <summary>ITerrainSurface.ExtractContoursAt: polylines at one elevation, added to the drawing.</summary>
        public ObjectIdCollection ExtractContoursAt(double elevation) { return new ObjectIdCollection(); }
        public ObjectIdCollection ExtractContoursAt(double elevation, ContourSmoothingType smoothingType, int smoothingFactor) { return new ObjectIdCollection(); }
        public TinSurfaceProperties GetTinProperties() { return new TinSurfaceProperties(); }
        public TinSurfaceTriangleCollection GetTriangles(bool includeInvisible) { return new TinSurfaceTriangleCollection(); }
        public TinSurfaceTriangleCollection Triangles { get { return new TinSurfaceTriangleCollection(); } }
    }

    public sealed class TinSurfaceProperties
    {
        public int NumberOfTriangles { get; }
        public double MinimumTriangleArea { get; }
        public double MaximumTriangleArea { get; }
    }

    public sealed class TinSurfaceVertex { public Point3d Location { get; } }

    public sealed class TinSurfaceTriangle
    {
        public TinSurfaceVertex Vertex1 { get; }
        public TinSurfaceVertex Vertex2 { get; }
        public TinSurfaceVertex Vertex3 { get; }
    }

    public sealed class TinSurfaceTriangleCollection : IEnumerable<TinSurfaceTriangle>
    {
        public int Count { get; }
        public IEnumerator<TinSurfaceTriangle> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
}

namespace Autodesk.Civil.DatabaseServices
{
    using Autodesk.AutoCAD.DatabaseServices;
    using Autodesk.AutoCAD.Geometry;

    public abstract class Part : Entity
    {
        protected Part() : base(IntPtr.Zero, false) { }
        public string Name { get; set; }
        public ObjectId NetworkId { get; }
        public string PartSizeName { get; }
    }

    public class Pipe : Part
    {
        public Point3d StartPoint { get; }
        public Point3d EndPoint { get; }
        public double InnerDiameterOrWidth { get; }
    }

    public class Structure : Part
    {
        public Point3d Location { get; }
        public Point3d Position { get; set; }
        public double RimElevation { get; }
    }
}
