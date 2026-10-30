// Stand-ins for the rest of the API surface the whole plugin uses: the ribbon (AdWindows),
// pipe networks, xref spatial filters, the progress meter, regions and Ge curves. Shapes follow
// the real AdWindows / AcDbMgd / AcMgd / AeccDbMgd declarations for the members used.
using System;
using System.Collections;
using System.Collections.Generic;

namespace Autodesk.Windows
{
    public enum RibbonItemSize { Standard = 0, Large = 1 }

    public class RibbonItem
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public bool ShowText { get; set; }
        public bool ShowImage { get; set; }
        public System.Windows.Media.ImageSource Image { get; set; }
        public System.Windows.Media.ImageSource LargeImage { get; set; }
        public object ToolTip { get; set; }
        public RibbonItemSize Size { get; set; }
        public System.Windows.Controls.Orientation Orientation { get; set; }
    }

    public class RibbonCommandItem : RibbonItem
    {
        public object CommandParameter { get; set; }
        public System.Windows.Input.ICommand CommandHandler { get; set; }
    }

    public class RibbonButton : RibbonCommandItem { }
    public class RibbonRowBreak : RibbonItem { }

    public class RibbonItemCollection : List<RibbonItem> { }

    public class RibbonRowPanel : RibbonItem
    {
        public RibbonItemCollection Items { get; } = new RibbonItemCollection();
    }

    public class RibbonPanelSource
    {
        public string Title { get; set; }
        public RibbonItemCollection Items { get; } = new RibbonItemCollection();
    }

    public class RibbonPanel
    {
        public RibbonPanelSource Source { get; set; }
    }

    public class RibbonTab
    {
        public string Title { get; set; }
        public string Id { get; set; }
        public List<RibbonPanel> Panels { get; } = new List<RibbonPanel>();
    }

    public class RibbonControl
    {
        public List<RibbonTab> Tabs { get; } = new List<RibbonTab>();
        public RibbonTab FindTab(string id) { return null; }
    }

    public class RibbonItemEventArgs : EventArgs
    {
        public RibbonItem Item { get; }
    }

    public static class ComponentManager
    {
        public static RibbonControl Ribbon { get; }
        public static event EventHandler<RibbonItemEventArgs> ItemInitialized;
        public static void Raise() { if (ItemInitialized != null) ItemInitialized(null, null); }
    }
}

namespace Autodesk.AutoCAD.Runtime
{
    public sealed class ProgressMeter : IDisposable
    {
        public void Start() { }
        public void Start(string displayString) { }
        public void SetLimit(int max) { }
        public void MeterProgress() { }
        public void Stop() { }
        public void Dispose() { }
    }
}

namespace Autodesk.AutoCAD.DatabaseServices.Filters
{
    using Autodesk.AutoCAD.DatabaseServices;

    public abstract class Filter : DBObject { }

    public sealed class SpatialFilter : Filter
    {
        public SpatialFilterDefinition Definition { get; set; }
        public Autodesk.AutoCAD.Geometry.Matrix3d ClipSpaceToWorldCoordinateSystemTransform { get; }
        public Autodesk.AutoCAD.Geometry.Matrix3d OriginalInverseBlockTransform { get; }
        public bool Inverted { get; }
    }

    public struct SpatialFilterDefinition
    {
        public bool Enabled { get; }
        public double Elevation { get; }
        public Autodesk.AutoCAD.Geometry.Vector3d Normal { get; }
        public Autodesk.AutoCAD.Geometry.Point2dCollection GetPoints() { return new Autodesk.AutoCAD.Geometry.Point2dCollection(); }
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    public enum BooleanOperationType { BoolUnite = 0, BoolIntersect = 1, BoolSubtract = 2 }

    public class Region : Entity
    {
        public static DBObjectCollection CreateFromCurves(DBObjectCollection curveSegments) { return new DBObjectCollection(); }
        public double Area { get; }
        public double Perimeter { get; }
        public bool IsNull { get; }
        public void BooleanOperation(BooleanOperationType operation, Region otherRegion) { }
    }
}

namespace Autodesk.AutoCAD.Geometry
{
    public abstract class Entity3d { }

    public abstract class Curve3d : Entity3d
    {
        public Point3d StartPoint { get; }
        public Point3d EndPoint { get; }
    }

    public class CompositeCurve3d : Curve3d
    {
        public Curve3d[] GetCurves() { return new Curve3d[0]; }
    }
}

namespace Autodesk.Civil.DatabaseServices
{
    using Autodesk.AutoCAD.DatabaseServices;
    using Autodesk.AutoCAD.Geometry;

    public abstract class Part : Entity
    {
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
