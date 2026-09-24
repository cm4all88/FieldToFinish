// Minimal stand-ins for the AutoCAD .NET database, editor, application and runtime API.
// Compile-only: nothing here runs. Signatures follow the real AcDbMgd/AcMgd surface so that
// a misuse in the real plugin files is a compile error here too.
using System;
using System.Collections;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace Autodesk.AutoCAD.Runtime
{
    public class Exception : System.Exception
    {
        public Exception() { }
        public Exception(ErrorStatus status) { ErrorStatus = status; }
        public Exception(ErrorStatus status, string message) : base(message) { ErrorStatus = status; }
        public ErrorStatus ErrorStatus { get; }
    }

    public enum ErrorStatus { OK, NotApplicable, InvalidInput, KeyNotFound, DuplicateRecordName, NotOpenForWrite, WasErased, NullObjectId, InvalidObjectId, InvalidExtents, DegenerateGeometry, NoDatabase, NotInDatabase, RegappIdNotFound, XDataSizeExceeded, UserBreak, AlreadyInDb, IsReading, IsWriting }

    [Flags]
    public enum CommandFlags
    {
        Modal = 0, Transparent = 1, UsePickSet = 2, Redraw = 4, NoPerspective = 8, NoMultiple = 16, NoTileMode = 32,
        NoPaperSpace = 64, NoOem = 128, Undefined = 256, InProgress = 512, Defun = 1024, NoNewStack = 2048,
        NoInternalLock = 4096, DocReadLock = 8192, DocExclusiveLock = 16384, Session = 32768, Interruptible = 65536,
        NoHistory = 131072, NoUndoMarker = 262144, NoBlockEditor = 524288, NoActionRecording = 1048576, ActionMacro = 2097152
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class CommandMethodAttribute : Attribute
    {
        public CommandMethodAttribute(string globalName) { GlobalName = globalName; }
        public CommandMethodAttribute(string globalName, CommandFlags flags) { GlobalName = globalName; Flags = flags; }
        public CommandMethodAttribute(string groupName, string globalName, CommandFlags flags) { GroupName = groupName; GlobalName = globalName; Flags = flags; }
        public string GroupName { get; }
        public string GlobalName { get; }
        public CommandFlags Flags { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class CommandClassAttribute : Attribute { public CommandClassAttribute(Type type) { } }

    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class ExtensionApplicationAttribute : Attribute { public ExtensionApplicationAttribute(Type type) { } }

    public interface IExtensionApplication { void Initialize(); void Terminate(); }

    public class RXClass { public string Name { get; } public string DxfName { get; } public bool IsDerivedFrom(RXClass other) { return false; } }

    public class RXObject : IDisposable
    {
        public static RXClass GetClass(Type type) { return new RXClass(); }
        public RXClass GetRXClass() { return new RXClass(); }
        public virtual void Dispose() { }
        public bool IsDisposed { get; }
    }
}

namespace Autodesk.AutoCAD.Colors
{
    public enum ColorMethod : short { ByLayer = 192, ByBlock = 193, ByColor = 194, ByAci = 195, ByPen = 196, Foreground = 197, LayerOff = 198, LayerFrozen = 199, None = 200 }

    public sealed class Color
    {
        public static Color FromColorIndex(ColorMethod method, short index) { return new Color { ColorIndex = index, ColorMethod = method }; }
        public static Color FromRgb(byte r, byte g, byte b) { return new Color { ColorMethod = ColorMethod.ByColor }; }
        public short ColorIndex { get; private set; }
        public ColorMethod ColorMethod { get; private set; }
        public bool IsByLayer { get { return ColorMethod == ColorMethod.ByLayer; } }
        public bool IsByBlock { get { return ColorMethod == ColorMethod.ByBlock; } }
        public byte Red { get; }
        public byte Green { get; }
        public byte Blue { get; }
        public string ColorNameForDisplay { get { return ColorIndex.ToString(); } }
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    using Autodesk.AutoCAD.Runtime;

    public enum OpenMode { ForRead = 0, ForWrite = 1, ForNotify = 2 }

    public enum DxfCode
    {
        Invalid = -9999, XDictionary = -6, PReactors = -5, Operator = -4, XDataStart = -3, HeaderId = -2, FirstEntityId = -2, End = -1,
        Start = 0, Text = 1, XRefPath = 1, ShapeName = 2, BlockName = 2, AttributeTag = 2, SymbolTableName = 2, MstyleName = 2,
        SymbolTableRecordName = 2, AttributePrompt = 3, DimStyleName = 3, LinetypeProse = 3, TextFontFile = 3, Description = 3,
        DimPostString = 3, TextBigFontFile = 4, DimensionAlignmentPoint = 4, CLShapeName = 4, SymbolTableRecordComments = 4,
        Handle = 5, DimensionBlock = 5, DimBlk1 = 6, LinetypeName = 6, DimBlk2 = 7, TextStyleName = 7, LayerName = 8,
        CLShapeText = 9, XCoordinate = 10, YCoordinate = 20, ZCoordinate = 30, Elevation = 38, Thickness = 39, Real = 40,
        ViewportHeight = 40, TxtSize = 40, TxtStyleXScale = 41, ViewWidth = 41, ViewportAspect = 41, TxtStylePSize = 42,
        ViewLensLength = 42, ViewFrontClip = 43, ViewBackClip = 44, ShapeXOffset = 44, ShapeYOffset = 45, ViewHeight = 45,
        ShapeScale = 46, PixelScale = 47, LinetypeScale = 48, DashLength = 49, MlineOffset = 49, LinetypeElement = 49, Angle = 50,
        ViewportSnapAngle = 50, Visibility = 60, LayerLinetype = 61, Color = 62, HasSubentities = 66, ViewportVisibility = 67,
        ViewportActive = 68, ViewportNumber = 69, Int16 = 70, ViewMode = 71, CircleSides = 72, ViewportZoom = 73, ViewportIcon = 74,
        ViewportSnap = 75, ViewportGrid = 76, ViewportSnapStyle = 77, ViewportSnapPair = 78, RegAppFlags = 71, TxtStyleFlags = 71,
        LinetypeAlign = 72, LinetypePdc = 73, Int32 = 90, Int64 = 160, Subclass = 100, EmbeddedObjectStart = 101, ControlString = 102,
        DimVarHandle = 105, UcsOrg = 110, UcsOrientationX = 111, UcsOrientationY = 112, XReal = 140, ViewBrightness = 141,
        ViewContrast = 142, Int8 = 280, RenderMode = 281, Bool = 290, XInt16 = 370, LineWeight = 370, PlotStyleNameType = 380,
        PlotStyleNameId = 390, ExtendedInt16 = 400, LayoutName = 410, ColorRgb = 420, ColorName = 430, Alpha = 440,
        ArbitraryHandle = 320, SoftPointerId = 330, HardPointerId = 340, SoftOwnershipId = 350, HardOwnershipId = 360,
        Comment = 999, ExtendedDataAsciiString = 1000, ExtendedDataRegAppName = 1001, ExtendedDataControlString = 1002,
        ExtendedDataLayerName = 1003, ExtendedDataBinaryChunk = 1004, ExtendedDataHandle = 1005,
        ExtendedDataXCoordinate = 1010, ExtendedDataYCoordinate = 1020, ExtendedDataZCoordinate = 1030,
        ExtendedDataWorldXCoordinate = 1011, ExtendedDataWorldYCoordinate = 1021, ExtendedDataWorldZCoordinate = 1031,
        ExtendedDataWorldXDisp = 1012, ExtendedDataWorldYDisp = 1022, ExtendedDataWorldZDisp = 1032,
        ExtendedDataWorldXDir = 1013, ExtendedDataWorldYDir = 1023, ExtendedDataWorldZDir = 1033,
        ExtendedDataReal = 1040, ExtendedDataDist = 1041, ExtendedDataScale = 1042, ExtendedDataInteger16 = 1070, ExtendedDataInteger32 = 1071
    }

    public struct TypedValue
    {
        public TypedValue(int typeCode, object value) { TypeCode = (short)typeCode; Value = value; }
        public short TypeCode { get; }
        public object Value { get; }
    }

    public sealed class ResultBuffer : IDisposable, IEnumerable<TypedValue>
    {
        private readonly List<TypedValue> _values = new List<TypedValue>();
        public ResultBuffer() { }
        public ResultBuffer(params TypedValue[] values) { _values.AddRange(values); }
        public void Add(TypedValue value) { _values.Add(value); }
        public TypedValue[] AsArray() { return _values.ToArray(); }
        public IEnumerator<TypedValue> GetEnumerator() { return _values.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return _values.GetEnumerator(); }
        public void Dispose() { }
    }

    public struct Handle
    {
        public Handle(long value) { Value = value; }
        public long Value { get; }
        public override string ToString() { return Value.ToString("X"); }
    }

    public struct ObjectId
    {
        public static ObjectId Null { get { return new ObjectId(); } }
        public bool IsNull { get { return true; } }
        public bool IsValid { get { return false; } }
        public bool IsErased { get { return false; } }
        public bool IsEffectivelyErased { get { return false; } }
        public Handle Handle { get { return new Handle(0); } }
        public Database Database { get { return null; } }
        public RXClass ObjectClass { get { return new RXClass(); } }
        public DBObject GetObject(OpenMode mode) { return null; }
        public DBObject GetObject(OpenMode mode, bool openErased) { return null; }
        public static bool operator ==(ObjectId a, ObjectId b) { return true; }
        public static bool operator !=(ObjectId a, ObjectId b) { return false; }
        public override bool Equals(object obj) { return obj is ObjectId; }
        public override int GetHashCode() { return 0; }
    }

    public sealed class ObjectIdCollection : IEnumerable<ObjectId>, IEnumerable, IDisposable
    {
        public void Dispose() { }
        private readonly List<ObjectId> _ids = new List<ObjectId>();
        public ObjectIdCollection() { }
        public ObjectIdCollection(ObjectId[] ids) { _ids.AddRange(ids); }
        public int Add(ObjectId id) { _ids.Add(id); return _ids.Count - 1; }
        public void Remove(ObjectId id) { _ids.Remove(id); }
        public bool Contains(ObjectId id) { return _ids.Contains(id); }
        public void Clear() { _ids.Clear(); }
        public int Count { get { return _ids.Count; } }
        public ObjectId this[int index] { get { return _ids[index]; } set { _ids[index] = value; } }
        public IEnumerator<ObjectId> GetEnumerator() { return _ids.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return _ids.GetEnumerator(); }
    }

    public sealed class AnnotationScale : DBObject
    {
        public string Name { get; set; }
        public double DrawingUnits { get; set; }
        public double PaperUnits { get; set; }
        public double Scale { get { return DrawingUnits / PaperUnits; } }
    }

    public sealed class ObjectContextManager
    {
        public ObjectContextCollection GetContextCollection(string name) { return new ObjectContextCollection(); }
    }

    public sealed class ObjectContextCollection : IEnumerable<ObjectContext>
    {
        public ObjectContext CurrentContext { get; set; }
        public ObjectContext GetContext(string name) { return null; }
        public bool HasContext(string name) { return false; }
        public IEnumerator<ObjectContext> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public class ObjectContext { public string Name { get; } }

    public sealed class TransactionManager
    {
        public Transaction StartTransaction() { return new Transaction(); }
        public Transaction StartOpenCloseTransaction() { return new Transaction(); }
        public Transaction TopTransaction { get; }
        public int NumberOfActiveTransactions { get; }
        public void QueueForGraphicsFlush() { }
        public void FlushGraphics() { }
    }

    public sealed class Transaction : IDisposable
    {
        public DBObject GetObject(ObjectId id, OpenMode mode) { return null; }
        public DBObject GetObject(ObjectId id, OpenMode mode, bool openErased) { return null; }
        public DBObject GetObject(ObjectId id, OpenMode mode, bool openErased, bool forceOpenOnLockedLayer) { return null; }
        public void AddNewlyCreatedDBObject(DBObject obj, bool add) { }
        public void Commit() { }
        public void Abort() { }
        public TransactionManager TransactionManager { get; }
        public void Dispose() { }
    }

    public enum UnitsValue { Undefined = 0, Inches = 1, Feet = 2, Miles = 3, Millimeters = 4, Centimeters = 5, Meters = 6, Kilometers = 7, USSurveyFeet = 21, USSurveyInch = 22, USSurveyYard = 23, USSurveyMile = 24 }
    public enum LineWeight { ByLayer = -1, ByBlock = -2, ByLineWeightDefault = -3, LineWeight000 = 0, LineWeight005 = 5, LineWeight009 = 9, LineWeight013 = 13, LineWeight015 = 15, LineWeight018 = 18, LineWeight020 = 20, LineWeight025 = 25, LineWeight030 = 30, LineWeight035 = 35, LineWeight040 = 40, LineWeight050 = 50, LineWeight053 = 53, LineWeight060 = 60, LineWeight070 = 70, LineWeight080 = 80, LineWeight090 = 90, LineWeight100 = 100, LineWeight106 = 106, LineWeight120 = 120, LineWeight140 = 140, LineWeight158 = 158, LineWeight200 = 200, LineWeight211 = 211 }

    public sealed class Database : IDisposable
    {
        public Database() { }
        public Database(bool buildDefaultDrawing, bool noDocument) { }
        public string Filename { get; set; }
        public string OriginalFileName { get; }
        public AnnotationScale Cannoscale { get; set; }
        public TransactionManager TransactionManager { get; } = new TransactionManager();
        public ObjectId NamedObjectsDictionaryId { get; }
        public ObjectId BlockTableId { get; }
        public ObjectId LayerTableId { get; }
        public ObjectId TextStyleTableId { get; }
        public ObjectId LinetypeTableId { get; }
        public ObjectId RegAppTableId { get; }
        public ObjectId DimStyleTableId { get; }
        public ObjectId LayoutDictionaryId { get; }
        public ObjectId MLeaderStyleDictionaryId { get; }
        public ObjectId TableStyleDictionaryId { get; }
        public ObjectId CurrentSpaceId { get; }
        public ObjectId Clayer { get; set; }
        public ObjectId Textstyle { get; set; }
        public ObjectId Celtype { get; set; }
        public ObjectId Dimstyle { get; set; }
        public ObjectId Tablestyle { get; set; }
        public ObjectId MLeaderstyle { get; set; }
        public bool TryGetObjectId(Handle handle, out ObjectId id) { id = ObjectId.Null; return false; }
        public ObjectId GetObjectId(bool createIfNotFound, Handle handle, int reserved) { return ObjectId.Null; }
        public ObjectId ByLayerLinetype { get; }
        public ObjectId ContinuousLinetype { get; }
        public double Ltscale { get; set; }
        public double Textsize { get; set; }
        public double Dimscale { get; set; }
        public bool TileMode { get; set; }
        public UnitsValue Insunits { get; set; }
        public Extents3d Extmin { get { return new Extents3d(); } }
        public Point3d Extmax { get; }
        public ObjectContextManager ObjectContextManager { get; } = new ObjectContextManager();
        public void ReadDwgFile(string fileName, System.IO.FileShare share, bool allowCPConversion, string password) { }
        public void CloseInput(bool closeFile) { }
        public void Dispose() { }
    }

    public class DBObject : RXObject
    {
        public ObjectId ObjectId { get; }
        public ObjectId Id { get { return ObjectId; } }
        public Handle Handle { get { return new Handle(0); } }
        public Database Database { get; }
        public ObjectId OwnerId { get; }
        public ObjectId ExtensionDictionary { get; }
        public bool IsErased { get; }
        public bool IsWriteEnabled { get; }
        public bool IsReadEnabled { get; }
        public bool IsNewObject { get; }
        public ResultBuffer XData { get; set; }
        public void CreateExtensionDictionary() { }
        public void ReleaseExtensionDictionary() { }
        public void UpgradeOpen() { }
        public void DowngradeOpen() { }
        public void Erase() { }
        public void Erase(bool erasing) { }
        public void Close() { }
        public void Cancel() { }
        public ResultBuffer GetXDataForApplication(string name) { return null; }
        public void SetDatabaseDefaults() { }
        public void SetDatabaseDefaults(Database db) { }
        public DBObject Clone() { return this; }
        public DBObject DeepClone(DBObject owner, IdMapping map, bool primary) { return this; }
        public virtual ObjectId LayerId { get; set; }
    }

    public sealed class IdMapping : IDisposable { public void Dispose() { } }

    public class Entity : DBObject
    {
        public string Layer { get; set; }
        public override ObjectId LayerId { get; set; }
        public string Linetype { get; set; }
        public ObjectId LinetypeId { get; set; }
        public double LinetypeScale { get; set; }
        public Autodesk.AutoCAD.Colors.Color Color { get; set; }
        public int ColorIndex { get; set; }
        public LineWeight LineWeight { get; set; }
        public bool Visible { get; set; }
        public bool Annotative { get; set; }
        public ObjectId BlockId { get; }
        public ObjectId OwnerBlockId { get { return BlockId; } }
        public Extents3d GeometricExtents { get { return new Extents3d(); } }
        public Extents3d Bounds { get { return GeometricExtents; } }
        public Matrix3d Ecs { get { return Matrix3d.Identity; } }
        public void TransformBy(Matrix3d m) { }
        public Entity GetTransformedCopy(Matrix3d m) { return this; }
        public new Entity Clone() { return this; }
        public void Highlight() { }
        public void Unhighlight() { }
        public void RecordGraphicsModified(bool modified) { }
        public void Draw() { }
        public bool IsPlanar { get; }
        public DBObjectCollection GetOffsetCurves(double offset) { return new DBObjectCollection(); }
        public void IntersectWith(Entity other, Intersect type, Point3dCollection points, IntPtr a, IntPtr b) { }
        public void IntersectWith(Entity other, Intersect type, Plane plane, Point3dCollection points, IntPtr a, IntPtr b) { }
    }

    public enum Intersect { OnBothOperands = 0, ExtendThis = 1, ExtendArgument = 2, ExtendBoth = 3 }

    public sealed class DBObjectCollection : IEnumerable<DBObject>, IDisposable
    {
        private readonly List<DBObject> _items = new List<DBObject>();
        public int Add(DBObject o) { _items.Add(o); return _items.Count - 1; }
        public int Count { get { return _items.Count; } }
        public DBObject this[int i] { get { return _items[i]; } }
        public IEnumerator<DBObject> GetEnumerator() { return _items.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return _items.GetEnumerator(); }
        public void Dispose() { }
    }

    public class Curve : Entity
    {
        public virtual Point3d StartPoint { get; set; }
        public virtual Point3d EndPoint { get; set; }
        public double StartParam { get; }
        public double EndParam { get; }
        public bool Closed { get; set; }
        public double Area { get; }
        public Point3d GetPointAtParameter(double p) { return StartPoint; }
        public double GetParameterAtPoint(Point3d p) { return 0; }
        public double GetDistanceAtParameter(double p) { return 0; }
        public double GetParameterAtDistance(double d) { return 0; }
        public Point3d GetPointAtDist(double d) { return StartPoint; }
        public double GetDistAtPoint(Point3d p) { return 0; }
        public Point3d GetClosestPointTo(Point3d p, bool extend) { return StartPoint; }
        public Point3d GetClosestPointTo(Point3d p, Vector3d direction, bool extend) { return StartPoint; }
        public Vector3d GetFirstDerivative(Point3d p) { return Vector3d.XAxis; }
        public Vector3d GetFirstDerivative(double p) { return Vector3d.XAxis; }
        public DBObjectCollection GetSplitCurves(Point3dCollection points) { return new DBObjectCollection(); }
        public DBObjectCollection GetSplitCurves(DoubleCollection parameters) { return new DBObjectCollection(); }
        public void ReverseCurve() { }
        public Curve3dEx GetGeCurve() { return new Curve3dEx(); }
    }

    public sealed class Curve3dEx { }

    public class Line : Curve
    {
        public Line() { }
        public Line(Point3d start, Point3d end) { StartPoint = start; EndPoint = end; }
        public double Length { get { return StartPoint.DistanceTo(EndPoint); } }
        public double Angle { get { return Math.Atan2(EndPoint.Y - StartPoint.Y, EndPoint.X - StartPoint.X); } }
        public Vector3d Delta { get { return StartPoint.GetVectorTo(EndPoint); } }
        public Vector3d Normal { get; set; }
        public double Thickness { get; set; }
    }

    public class Ray : Curve { public Point3d BasePoint { get; set; } public Vector3d UnitDir { get; set; } }
    public class Xline : Curve { public Point3d BasePoint { get; set; } public Vector3d UnitDir { get; set; } }

    public class Arc : Curve
    {
        public Arc() { }
        public Arc(Point3d center, double radius, double startAngle, double endAngle) { Center = center; Radius = radius; StartAngle = startAngle; EndAngle = endAngle; }
        public Arc(Point3d center, Vector3d normal, double radius, double startAngle, double endAngle) : this(center, radius, startAngle, endAngle) { Normal = normal; }
        public Point3d Center { get; set; }
        public double Radius { get; set; }
        public double StartAngle { get; set; }
        public double EndAngle { get; set; }
        public double TotalAngle { get { return EndAngle - StartAngle; } }
        public double Length { get { return Radius * TotalAngle; } }
        public Vector3d Normal { get; set; }
        public double Thickness { get; set; }
    }

    public class Circle : Curve
    {
        public Circle() { }
        public Circle(Point3d center, Vector3d normal, double radius) { Center = center; Normal = normal; Radius = radius; }
        public Point3d Center { get; set; }
        public double Radius { get; set; }
        public Vector3d Normal { get; set; }
        public double Circumference { get { return 2 * Math.PI * Radius; } }
    }

    public enum SegmentType { Line = 0, Arc = 1, Coincident = 2, Point = 3, Empty = 4 }

    public class Polyline : Curve
    {
        public Polyline() { }
        public Polyline(int vertices) { }
        public int NumberOfVertices { get; }
        public double Elevation { get; set; }
        public Vector3d Normal { get; set; }
        public double ConstantWidth { get; set; }
        public double Length { get; }
        public bool Plinegen { get; set; }
        public bool HasBulges { get; }
        public bool HasWidth { get; }
        public bool IsOnlyLines { get; }
        public void AddVertexAt(int index, Point2d point, double bulge, double startWidth, double endWidth) { }
        public void RemoveVertexAt(int index) { }
        public void SetPointAt(int index, Point2d point) { }
        public void SetBulgeAt(int index, double bulge) { }
        public Point2d GetPoint2dAt(int index) { return Point2d.Origin; }
        public Point3d GetPoint3dAt(int index) { return Point3d.Origin; }
        public double GetBulgeAt(int index) { return 0; }
        public double GetStartWidthAt(int index) { return 0; }
        public double GetEndWidthAt(int index) { return 0; }
        public SegmentType GetSegmentType(int index) { return SegmentType.Line; }
        public LineSegment2d GetLineSegment2dAt(int index) { return new LineSegment2d(Point2d.Origin, Point2d.Origin); }
        public LineSegment3d GetLineSegmentAt(int index) { return new LineSegment3d(Point3d.Origin, Point3d.Origin); }
        public CircularArc2d GetArcSegment2dAt(int index) { return new CircularArc2d(Point2d.Origin, 1, 0, 1, Vector2d.XAxis, false); }
        public CircularArc3d GetArcSegmentAt(int index) { return new CircularArc3d(Point3d.Origin, Vector3d.ZAxis, Vector3d.XAxis, 1, 0, 1); }
        public void ConvertFrom(Entity entity, bool transferId) { }
        public void Reset(bool reuse, int vertices) { }
    }

    public class Polyline2d : Curve { }
    public class Polyline3d : Curve { }
    public class Spline : Curve { public Polyline ToPolyline() { return new Polyline(); } }
    public class Ellipse : Curve { }

    public enum TextHorizontalMode { TextLeft = 0, TextCenter = 1, TextRight = 2, TextAlign = 3, TextMid = 4, TextFit = 5 }
    public enum TextVerticalMode { TextBase = 0, TextBottom = 1, TextVerticalMid = 2, TextTop = 3 }
    public enum AttachmentPoint { TopLeft = 1, TopCenter = 2, TopRight = 3, MiddleLeft = 4, MiddleCenter = 5, MiddleRight = 6, BottomLeft = 7, BottomCenter = 8, BottomRight = 9, BaseLeft = 10, BaseCenter = 11, BaseRight = 12, BaseAlign = 13, BottomAlign = 14, MiddleAlign = 15, TopAlign = 16, BaseFit = 17, BottomFit = 18, MiddleFit = 19, TopFit = 20, BaseMid = 21, BottomMid = 22, MiddleMid = 23, TopMid = 24 }

    public class DBText : Entity
    {
        public string TextString { get; set; }
        public Point3d Position { get; set; }
        public Point3d AlignmentPoint { get; set; }
        public double Height { get; set; }
        public double Rotation { get; set; }
        public double WidthFactor { get; set; }
        public double Oblique { get; set; }
        public double Thickness { get; set; }
        public ObjectId TextStyleId { get; set; }
        public TextHorizontalMode HorizontalMode { get; set; }
        public TextVerticalMode VerticalMode { get; set; }
        public AttachmentPoint Justify { get; set; }
        public Vector3d Normal { get; set; }
        public bool IsMirroredInX { get; set; }
        public bool IsMirroredInY { get; set; }
        public bool IsDefaultAlignment { get; }
        public void AdjustAlignment(Database db) { }
    }

    public class MText : Entity
    {
        public string Contents { get; set; }
        public string Text { get; }
        public Point3d Location { get; set; }
        public double TextHeight { get; set; }
        public double Rotation { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double ActualWidth { get; }
        public double ActualHeight { get; }
        public AttachmentPoint Attachment { get; set; }
        public ObjectId TextStyleId { get; set; }
        public Vector3d Normal { get; set; }
        public Vector3d Direction { get; set; }
        public bool BackgroundFill { get; set; }
        public bool UseBackgroundColor { get; set; }
        public double BackgroundScaleFactor { get; set; }
        public double LineSpacingFactor { get; set; }
        public int LineSpacingStyle { get; set; }
        public FlowDirection FlowDirection { get; set; }
        public string ContentsRTF { get; set; }
        public void SetContentsRtf(string rtf) { }
    }

    public enum FlowDirection { LeftToRight = 1, RightToLeft = 2, TopToBottom = 3, BottomToTop = 4, ByStyle = 5 }

    public class Wipeout : Entity
    {
        public Wipeout() { }
        public void SetFrom(Point2dCollection points, Vector3d normal) { }
        public Point2dCollection GetVertices() { return new Point2dCollection(); }
        public Vector3d Orientation { get; }
        public Point3d Orientation1 { get; }
        public bool ClipBoundary { get; }
    }

    public class Hatch : Entity
    {
        public string PatternName { get; }
        public double PatternScale { get; set; }
        public double PatternAngle { get; set; }
        public void SetHatchPattern(HatchPatternType type, string name) { }
        public void AppendLoop(HatchLoopTypes type, ObjectIdCollection ids) { }
        public void EvaluateHatch(bool underestimate) { }
        public bool Associative { get; set; }
        public int NumberOfLoops { get; }
        public double Area { get; }
    }

    public enum HatchPatternType { UserDefined = 0, PreDefined = 1, CustomDefined = 2 }
    [Flags] public enum HatchLoopTypes { Default = 0, External = 1, Polyline = 2, Derived = 4, Textbox = 8, Outermost = 16, NotClosed = 32, SelfIntersecting = 64, TextIsland = 128, Duplicate = 256 }

    public sealed class AttributeCollection : IEnumerable<ObjectId>
    {
        private readonly List<ObjectId> _ids = new List<ObjectId>();
        public int Count { get { return _ids.Count; } }
        public ObjectId this[int i] { get { return _ids[i]; } }
        public ObjectId AppendAttribute(AttributeReference reference) { _ids.Add(ObjectId.Null); return ObjectId.Null; }
        public IEnumerator<ObjectId> GetEnumerator() { return _ids.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return _ids.GetEnumerator(); }
    }

    public class AttributeDefinition : DBText
    {
        public string Tag { get; set; }
        public string Prompt { get; set; }
        public bool Constant { get; set; }
        public bool Invisible { get; set; }
        public bool Verifiable { get; set; }
        public bool Preset { get; set; }
        public bool IsMTextAttributeDefinition { get; set; }
        public bool LockPositionInBlock { get; set; }
    }

    public class AttributeReference : DBText
    {
        public string Tag { get; set; }
        public bool Invisible { get; set; }
        public bool IsMTextAttribute { get; set; }
        public bool LockPositionInBlock { get; set; }
        public void SetAttributeFromBlock(AttributeDefinition definition, Matrix3d transform) { }
        public void SetAttributeFromBlock(Matrix3d transform) { }
    }

    public sealed class DynamicBlockReferenceProperty
    {
        public string PropertyName { get; }
        public object Value { get; set; }
        public bool ReadOnly { get; }
        public object[] GetAllowedValues() { return new object[0]; }
        public short PropertyTypeCode { get; }
        public string Description { get; }
        public bool Show { get; }
    }

    public sealed class DynamicBlockReferencePropertyCollection : IEnumerable<DynamicBlockReferenceProperty>
    {
        public int Count { get; }
        public DynamicBlockReferenceProperty this[int i] { get { return null; } }
        public IEnumerator<DynamicBlockReferenceProperty> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public class BlockReference : Entity
    {
        public BlockReference() { }
        public BlockReference(Point3d position, ObjectId blockTableRecord) { Position = position; BlockTableRecord = blockTableRecord; }
        public Point3d Position { get; set; }
        public double Rotation { get; set; }
        public Scale3d ScaleFactors { get; set; }
        public Vector3d Normal { get; set; }
        public ObjectId BlockTableRecord { get; set; }
        public ObjectId DynamicBlockTableRecord { get; }
        public ObjectId AnonymousBlockTableRecord { get; }
        public bool IsDynamicBlock { get; }
        public string Name { get; }
        public Matrix3d BlockTransform { get; set; }
        public AttributeCollection AttributeCollection { get; } = new AttributeCollection();
        public DynamicBlockReferencePropertyCollection DynamicBlockReferencePropertyCollection { get; } = new DynamicBlockReferencePropertyCollection();
        public void ResetBlock() { }
        public void ExplodeToOwnerSpace() { }
        public void Explode(DBObjectCollection entities) { }
    }

    public abstract class SymbolTable : DBObject, IEnumerable<ObjectId>
    {
        public bool Has(string name) { return false; }
        public bool Has(ObjectId id) { return false; }
        public ObjectId this[string name] { get { return ObjectId.Null; } }
        public ObjectId Add(SymbolTableRecord record) { return ObjectId.Null; }
        public IEnumerator<ObjectId> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public abstract class SymbolTableRecord : DBObject
    {
        public string Name { get; set; }
        public bool IsDependent { get; }
        public bool IsResolved { get; }
    }

    public sealed class BlockTable : SymbolTable
    {
        public static string ModelSpace { get { return "*Model_Space"; } }
        public static string PaperSpace { get { return "*Paper_Space"; } }
    }

    public sealed class BlockTableRecord : SymbolTableRecord, IEnumerable<ObjectId>
    {
        public static string ModelSpace { get { return "*Model_Space"; } }
        public static string PaperSpace { get { return "*Paper_Space"; } }
        public bool IsLayout { get; }
        public bool IsAnonymous { get; }
        public bool IsFromExternalReference { get; }
        public bool IsDynamicBlock { get; }
        public bool HasAttributeDefinitions { get; }
        public bool Explodable { get; set; }
        public bool IsUnloaded { get; }
        public string Comments { get; set; }
        public string PathName { get; set; }
        public Point3d Origin { get; set; }
        public UnitsValue Units { get; set; }
        public BlockScaling BlockScaling { get; set; }
        public ObjectId LayoutId { get; }
        public ObjectId DrawOrderTableId { get; }
        public ObjectId AppendEntity(Entity entity) { return ObjectId.Null; }
        public ObjectIdCollection GetBlockReferenceIds(bool directOnly, bool forceValidity) { return new ObjectIdCollection(); }
        public ObjectIdCollection GetAnonymousBlockIds() { return new ObjectIdCollection(); }
        public void AssumeOwnershipOf(ObjectIdCollection ids) { }
        public IEnumerator<ObjectId> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public enum BlockScaling { Any = 0, Uniform = 1 }

    public sealed class LayerTable : SymbolTable { public void GenerateUsageData() { } }

    public sealed class LayerTableRecord : SymbolTableRecord
    {
        public Autodesk.AutoCAD.Colors.Color Color { get; set; }
        public ObjectId LinetypeObjectId { get; set; }
        public LineWeight LineWeight { get; set; }
        public bool IsFrozen { get; set; }
        public bool IsOff { get; set; }
        public bool IsLocked { get; set; }
        public bool IsPlottable { get; set; }
        public bool IsHidden { get; set; }
        public bool IsUsed { get; }
        public string Description { get; set; }
        public double Transparency { get; set; }
        public ObjectId PlotStyleNameId { get; set; }
        public bool ViewportVisibilityDefault { get; set; }
        public void SetIsFrozen(bool frozen) { IsFrozen = frozen; }
    }

    public sealed class TextStyleTable : SymbolTable { }

    public sealed class FontDescriptor
    {
        public FontDescriptor(string typeface, bool bold, bool italic, int charset, int pitchAndFamily) { TypeFace = typeface; Bold = bold; Italic = italic; }
        public string TypeFace { get; }
        public bool Bold { get; }
        public bool Italic { get; }
    }

    public sealed class TextStyleTableRecord : SymbolTableRecord
    {
        public double TextSize { get; set; }
        public double XScale { get; set; }
        public double ObliquingAngle { get; set; }
        public double PriorSize { get; set; }
        public string FileName { get; set; }
        public string BigFontFileName { get; set; }
        public bool IsShapeFile { get; set; }
        public bool IsVertical { get; set; }
        public FontDescriptor Font { get; set; }
        public byte Flags { get; set; }
        public bool Annotative { get; set; }
    }

    public sealed class LinetypeTable : SymbolTable { }
    public sealed class LinetypeTableRecord : SymbolTableRecord { public string Comments { get; set; } public double PatternLength { get; } public int NumDashes { get; } }
    public sealed class RegAppTable : SymbolTable { }
    public sealed class RegAppTableRecord : SymbolTableRecord { }
    public sealed class DimStyleTable : SymbolTable { }
    public sealed class DimStyleTableRecord : SymbolTableRecord { public double Dimtxt { get; set; } public double Dimscale { get; set; } public ObjectId Dimtxsty { get; set; } public int Dimdec { get; set; } public string Dimpost { get; set; } public int Dimlunit { get; set; } }

    public struct DBDictionaryEntry
    {
        public DBDictionaryEntry(string key, ObjectId value) { Key = key; Value = value; }
        public string Key { get; }
        public ObjectId Value { get; }
    }

    public class DBDictionary : DBObject, IEnumerable<DBDictionaryEntry>
    {
        public bool Contains(string key) { return false; }
        public bool Contains(ObjectId id) { return false; }
        public ObjectId GetAt(string key) { return ObjectId.Null; }
        public string NameAt(ObjectId id) { return null; }
        public ObjectId SetAt(string key, DBObject value) { return ObjectId.Null; }
        public void Remove(string key) { }
        public void Remove(ObjectId id) { }
        public int Count { get; }
        public bool TreatElementsAsHard { get; set; }
        public IEnumerator<DBDictionaryEntry> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public class Xrecord : DBObject { public ResultBuffer Data { get; set; } }

    public sealed class Layout : DBObject
    {
        public string LayoutName { get; set; }
        public ObjectId BlockTableRecordId { get; }
        public int TabOrder { get; set; }
        public bool ModelType { get; }
        public ObjectIdCollection GetViewports() { return new ObjectIdCollection(); }
        public Extents2d PlotPaperSize { get { return new Extents2d(); } }
        public double StdScale { get; }
        public bool UseStandardScale { get; }
        public string CanonicalMediaName { get; }
        public string PlotConfigurationName { get; }
        public string CurrentStyleSheet { get; }
        public Extents3d Extents { get { return new Extents3d(); } }
        public Extents3d Limits { get { return new Extents3d(); } }
    }

    public sealed class LayoutManager
    {
        public static LayoutManager Current { get; } = new LayoutManager();
        public string CurrentLayout { get; set; }
        public ObjectId GetLayoutId(string name) { return ObjectId.Null; }
        public bool LayoutExists(string name) { return false; }
        public ObjectId CreateLayout(string name) { return ObjectId.Null; }
        public void CopyLayout(string from, string to) { }
        public void DeleteLayout(string name) { }
        public void RenameLayout(string from, string to) { }
    }

    public sealed class Viewport : Entity
    {
        public Point3d CenterPoint { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double CustomScale { get; set; }
        public Point2d ViewCenter { get; set; }
        public double ViewHeight { get; set; }
        public Vector3d ViewDirection { get; set; }
        public double TwistAngle { get; set; }
        public int Number { get; }
        public bool On { get; set; }
        public bool Locked { get; set; }
        public bool NonRectClipOn { get; set; }
        public ObjectId NonRectClipEntityId { get; set; }
        public StandardScaleType StandardScale { get; set; }
        public AnnotationScale AnnotationScale { get; set; }
        public void FreezeLayersInViewport(IEnumerator<ObjectId> ids) { }
        public void ThawLayersInViewport(IEnumerator<ObjectId> ids) { }
        public bool IsLayerFrozenInViewport(ObjectId id) { return false; }
        public ObjectIdCollection GetFrozenLayers() { return new ObjectIdCollection(); }
        public void UpdateDisplay() { }
        public void SetViewportScale(double s) { }
    }

    public enum StandardScaleType { CustomScale = 0, ScaleToFit = 1, StandardScale10 = 2, StandardScale100 = 3 }

    public enum CellAlignment { TopLeft = 1, TopCenter = 2, TopRight = 3, MiddleLeft = 4, MiddleCenter = 5, MiddleRight = 6, BottomLeft = 7, BottomCenter = 8, BottomRight = 9 }
    public enum RowType { UnknownRow = 0, DataRow = 1, TitleRow = 2, HeaderRow = 4 }
    public enum TableBreakOptions { None = 0, EnableBreaking = 1, RepeatTopLabels = 2, RepeatBottomLabels = 4, AllowManualPositions = 8, AllowManualHeights = 16 }
    public enum TableBreakFlowDirection { Right = 1, DownOrUp = 2, Manual = 4 }

    public sealed class Cell
    {
        public string TextString { get; set; }
        public double? TextHeight { get; set; }
        public CellAlignment? Alignment { get; set; }
        public ObjectId? TextStyleId { get; set; }
        public string Value { get; set; }
        public CellContents Contents { get; } = new CellContents();
        public bool IsMerged { get; }
    }

    public sealed class CellContents : IEnumerable<CellContent>
    {
        public int Count { get; }
        public CellContent this[int i] { get { return new CellContent(); } }
        public IEnumerator<CellContent> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public sealed class CellContent { public string TextString { get; set; } }

    public sealed class CellCollection : IEnumerable<Cell>
    {
        public Cell this[int row, int column] { get { return new Cell(); } }
        public IEnumerator<Cell> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public sealed class Row { public double Height { get; set; } public RowType Style { get; } public bool IsMerged { get; } }
    public sealed class Column { public double Width { get; set; } }
    public sealed class RowsCollection : IEnumerable<Row> { public int Count { get; } public Row this[int i] { get { return new Row(); } } public IEnumerator<Row> GetEnumerator() { yield break; } IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); } }
    public sealed class ColumnsCollection : IEnumerable<Column> { public int Count { get; } public Column this[int i] { get { return new Column(); } } public IEnumerator<Column> GetEnumerator() { yield break; } IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); } }

    public class Table : Entity
    {
        public Table() { }
        public ObjectId TableStyle { get; set; }
        public Point3d Position { get; set; }
        public Vector3d Direction { get; set; }
        public Vector3d Normal { get; set; }
        public double Rotation { get; set; }
        public int NumRows { get; }
        public int NumColumns { get; }
        public double Width { get; set; }
        public double Height { get; set; }
        public TableBreakOptions BreakEnabled { get; set; }
        public TableBreakFlowDirection BreakFlowDirection { get; set; }
        public bool IsTitleSuppressed { get; set; }
        public bool IsHeaderSuppressed { get; set; }
        public FlowDirection FlowDirection { get; set; }
        public CellCollection Cells { get; } = new CellCollection();
        public RowsCollection Rows { get; } = new RowsCollection();
        public ColumnsCollection Columns { get; } = new ColumnsCollection();
        public void SetSize(int rows, int columns) { }
        public void SetRowHeight(double height) { }
        public void SetColumnWidth(double width) { }
        public void SetRowHeight(int row, double height) { }
        public void SetColumnWidth(int column, double width) { }
        public void SetTextString(int row, int column, string text) { }
        public string TextString(int row, int column) { return string.Empty; }
        public void SetTextHeight(int row, int column, double height) { }
        public void SetTextStyle(int row, int column, ObjectId style) { }
        public void SetAlignment(int row, int column, CellAlignment alignment) { }
        public void InsertRows(int index, double height, int count) { }
        public void InsertColumns(int index, double width, int count) { }
        public void DeleteRows(int index, int count) { }
        public void DeleteColumns(int index, int count) { }
        public void MergeCells(CellRange range) { }
        public void UnmergeCells(CellRange range) { }
        public bool IsMergedCell(int row, int column) { return false; }
        public void GenerateLayout() { }
        public void RecomputeTableBlock(bool force) { }
        public void SuppressRegenTable(bool suppress) { }
        public RowType RowType(int row) { return DatabaseServices.RowType.DataRow; }
        public void SetRowType(int row, RowType type) { }
        public void SetMargin(int row, int column, CellMargins margin, double value) { }
        public CellStateFlags GetCellState(int row, int column) { return CellStateFlags.None; }
        public void SetCellState(int row, int column, CellStateFlags flags) { }
    }

    [Flags] public enum CellMargins { Top = 1, Left = 2, Bottom = 4, Right = 8, HorizontalSpacing = 16, VerticalSpacing = 32 }
    [Flags] public enum CellStateFlags { None = 0, ContentLocked = 1, ContentReadOnly = 2, Linked = 4, ContentModifiedAfterUpdate = 8, FormatLocked = 16, FormatReadOnly = 32, FormatModifiedAfterUpdate = 64 }

    public sealed class CellRange
    {
        public static CellRange Create(Table table, int topRow, int leftColumn, int bottomRow, int rightColumn) { return new CellRange(); }
        public int TopRow { get; }
        public int LeftColumn { get; }
        public int BottomRow { get; }
        public int RightColumn { get; }
    }

    public sealed class TableStyle : DBObject
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public double TextHeight(RowType type) { return 0; }
        public ObjectId TextStyle(RowType type) { return ObjectId.Null; }
        public void SetTextHeight(double height, int rowTypes) { }
        public void SetTextStyle(ObjectId style, int rowTypes) { }
        public void SetAlignment(CellAlignment alignment, int rowTypes) { }
        public bool IsTitleSuppressed { get; set; }
        public bool IsHeaderSuppressed { get; set; }
        public FlowDirection FlowDirection { get; set; }
        public double HorizontalCellMargin { get; set; }
        public double VerticalCellMargin { get; set; }
    }

    public sealed class MLeader : Entity
    {
        public ContentType ContentType { get; set; }
        public ObjectId MLeaderStyle { get; set; }
        public MText MText { get; set; }
        public ObjectId BlockContentId { get; set; }
        public int AddLeaderLine(Point3d point) { return 0; }
        public void AddFirstVertex(int leaderLine, Point3d point) { }
        public void AddLastVertex(int leaderLine, Point3d point) { }
        public void SetFirstVertex(int leaderLine, Point3d point) { }
        public void SetLastVertex(int leaderLine, Point3d point) { }
        public Point3d GetFirstVertex(int leaderLine) { return Point3d.Origin; }
        public Point3d GetLastVertex(int leaderLine) { return Point3d.Origin; }
        public int LeaderCount { get; }
        public int LeaderLineCount { get; }
        public double TextHeight { get; set; }
        public Point3d TextLocation { get; set; }
        public double Scale { get; set; }
        public double DoglegLength { get; set; }
        public double LandingGap { get; set; }
        public bool EnableDogleg { get; set; }
        public bool EnableLanding { get; set; }
        public ObjectId TextStyleId { get; set; }
        public TextAlignmentType TextAlignmentType { get; set; }
        public TextAttachmentType TextAttachmentType { get; set; }
        public void SetTextAttachmentType(TextAttachmentType type, LeaderDirectionType direction) { }
        public ObjectId ArrowSymbolId { get; set; }
        public double ArrowSize { get; set; }
        public bool ExtendLeaderToText { get; set; }
        public new void SetDatabaseDefaults(Database db) { }
    }

    public enum ContentType { NoneContent = 0, BlockContent = 1, MTextContent = 2, ToleranceContent = 3 }

    public sealed class DrawOrderTable : DBObject
    {
        public void MoveAbove(ObjectIdCollection ids, ObjectId target) { }
        public void MoveBelow(ObjectIdCollection ids, ObjectId target) { }
        public void MoveToTop(ObjectIdCollection ids) { }
        public void MoveToBottom(ObjectIdCollection ids) { }
        public ObjectIdCollection GetFullDrawOrder(int honorSortentsMask) { return new ObjectIdCollection(); }
    }

    public enum TextAlignmentType { LeftAlignment = 0, CenterAlignment = 1, RightAlignment = 2 }
    public enum TextAttachmentType { AttachmentTopOfTop = 0, AttachmentMiddleOfTop = 1, AttachmentMiddle = 2, AttachmentMiddleOfBottom = 3, AttachmentBottomOfBottom = 4, AttachmentBottomLine = 5, AttachmentBottomOfTopLine = 6, AttachmentBottomOfTop = 7, AttachmentAllLine = 8, AttachmentCenter = 9, AttachmentLinedCenter = 10 }
    public enum LeaderDirectionType { UnknownLeader = 0, LeftLeader = 1, RightLeader = 2, TopLeader = 3, BottomLeader = 4 }

    public sealed class MLeaderStyle : DBObject { public string Name { get; set; } public double TextHeight { get; set; } public ObjectId TextStyleId { get; set; } }

    public sealed class SymbolUtilityServices
    {
        public static ObjectId GetBlockModelSpaceId(Database db) { return ObjectId.Null; }
        public static ObjectId GetBlockPaperSpaceId(Database db) { return ObjectId.Null; }
        public static ObjectId GetLayerZeroId(Database db) { return ObjectId.Null; }
        public static ObjectId GetLinetypeContinuousId(Database db) { return ObjectId.Null; }
        public static ObjectId GetTextStyleStandardId(Database db) { return ObjectId.Null; }
        public static void ValidateSymbolName(string name, bool allowVerticalBar) { }
        public static bool IsBlockModelSpaceName(string name) { return name == "*Model_Space"; }
        public static bool IsBlockPaperSpaceName(string name) { return name == "*Paper_Space"; }
        public static bool IsBlockLayoutName(string name) { return false; }
    }

    public static class HostApplicationServices
    {
        public static Database WorkingDatabase { get; set; }
        public static Current Current { get; } = new Current();
    }

    public sealed class Current { public string FindFile(string name, Database db, FindFileHint hint) { return name; } }
    public enum FindFileHint { Default = 0, FontFile = 1, CompiledShapeFile = 2, TrueTypeFontFile = 3, EmbeddedImageFile = 4, XRefDrawing = 5, PatternFile = 6, ARXApplication = 7, FontMapFile = 8, UnderlayFile = 9, DataLinkFile = 10, PhotometricWebFile = 11, MaterialMapFile = 12, CloudCollaborationFile = 13 }

    public sealed class ObjectIdEnumerator { }

    public sealed class LayerFilter { }

    public sealed class Group : DBObject
    {
        public Group() { }
        public Group(string description, bool selectable) { Description = description; }
        public string Description { get; set; }
        public string Name { get; }
        public bool Selectable { get; set; }
        public void Append(ObjectId id) { }
        public void Append(ObjectIdCollection ids) { }
        public ObjectId[] GetAllEntityIds() { return new ObjectId[0]; }
        public int NumEntities { get; }
    }

    public sealed class RasterImage : Entity
    {
        public ObjectId ImageDefId { get; set; }
        public Point3d Orientation { get; set; }
        public double Rotation { get; set; }
        public bool ShowImage { get; set; }
        public bool ImageTransparency { get; set; }
        public void AssociateRasterDef(RasterImageDef def) { }
        public new void SetDatabaseDefaults(Database db) { }
    }

    public sealed class RasterImageDef : DBObject
    {
        public string SourceFileName { get; set; }
        public void Load() { }
        public static ObjectId GetImageDictionary(Database db) { return ObjectId.Null; }
        public static ObjectId CreateImageDictionary(Database db) { return ObjectId.Null; }
        public bool IsLoaded { get; }
        public Vector2d Size { get; }
    }

    public sealed class ProxyEntity : Entity { public string ApplicationDescription { get; } public string OriginalClassName { get; } public string OriginalDxfName { get; } }
}

namespace Autodesk.AutoCAD.EditorInput
{
    using Autodesk.AutoCAD.DatabaseServices;

    public enum PromptStatus { Cancel = -5002, None = 5000, Error = -5001, Keyword = -5005, OK = 5100, Modeless = 5027, Other = 5028 }

    public sealed class KeywordCollection : IEnumerable<Keyword>
    {
        private readonly List<Keyword> _keywords = new List<Keyword>();
        public void Add(string globalName) { _keywords.Add(new Keyword(globalName)); }
        public void Add(string globalName, string localName) { _keywords.Add(new Keyword(globalName)); }
        public void Add(string globalName, string localName, string displayName) { _keywords.Add(new Keyword(globalName)); }
        public void Add(string globalName, string localName, string displayName, bool visible, bool enabled) { _keywords.Add(new Keyword(globalName)); }
        public void Clear() { _keywords.Clear(); }
        public int Count { get { return _keywords.Count; } }
        public string Default { get; set; }
        public Keyword this[int i] { get { return _keywords[i]; } }
        public IEnumerator<Keyword> GetEnumerator() { return _keywords.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return _keywords.GetEnumerator(); }
    }

    public sealed class Keyword
    {
        public Keyword(string globalName) { GlobalName = globalName; }
        public string GlobalName { get; }
        public string LocalName { get; set; }
        public string DisplayName { get; set; }
        public bool Visible { get; set; }
        public bool Enabled { get; set; }
        public bool IsReadOnly { get; }
    }

    public abstract class PromptOptions
    {
        protected PromptOptions(string message) { Message = message; }
        public string Message { get; set; }
        public KeywordCollection Keywords { get; } = new KeywordCollection();
        public bool AppendKeywordsToMessage { get; set; }
        public bool IsReadOnly { get; }
    }

    public abstract class PromptEditorOptions : PromptOptions
    {
        protected PromptEditorOptions(string message) : base(message) { }
        public bool AllowArbitraryInput { get; set; }
        public bool AllowNone { get; set; }
        public bool LimitsChecked { get; set; }
        public bool UseDashedLine { get; set; }
    }

    public sealed class PromptPointOptions : PromptEditorOptions
    {
        public PromptPointOptions(string message) : base(message) { }
        public PromptPointOptions(string messageAndKeywords, string globalKeywords) : base(messageAndKeywords) { }
        public bool UseBasePoint { get; set; }
        public Point3d BasePoint { get; set; }
        public bool AllowArbitraryInput2 { get; set; }
    }

    public sealed class PromptDistanceOptions : PromptEditorOptions
    {
        public PromptDistanceOptions(string message) : base(message) { }
        public bool UseBasePoint { get; set; }
        public Point3d BasePoint { get; set; }
        public double DefaultValue { get; set; }
        public bool UseDefaultValue { get; set; }
        public bool AllowNegative { get; set; }
        public bool AllowZero { get; set; }
        public bool Only2d { get; set; }
    }

    public sealed class PromptDoubleOptions : PromptEditorOptions
    {
        public PromptDoubleOptions(string message) : base(message) { }
        public double DefaultValue { get; set; }
        public bool UseDefaultValue { get; set; }
        public bool AllowNegative { get; set; }
        public bool AllowZero { get; set; }
    }

    public sealed class PromptIntegerOptions : PromptEditorOptions
    {
        public PromptIntegerOptions(string message) : base(message) { }
        public int DefaultValue { get; set; }
        public bool UseDefaultValue { get; set; }
        public bool AllowNegative { get; set; }
        public bool AllowZero { get; set; }
        public int LowerLimit { get; set; }
        public int UpperLimit { get; set; }
    }

    public sealed class PromptAngleOptions : PromptEditorOptions
    {
        public PromptAngleOptions(string message) : base(message) { }
        public bool UseBasePoint { get; set; }
        public Point3d BasePoint { get; set; }
        public double DefaultValue { get; set; }
        public bool UseDefaultValue { get; set; }
        public bool UseAngleBase { get; set; }
    }

    public sealed class PromptStringOptions : PromptOptions
    {
        public PromptStringOptions(string message) : base(message) { }
        public bool AllowSpaces { get; set; }
        public string DefaultValue { get; set; }
        public bool UseDefaultValue { get; set; }
    }

    public sealed class PromptKeywordOptions : PromptOptions
    {
        public PromptKeywordOptions(string message) : base(message) { }
        public PromptKeywordOptions(string messageAndKeywords, string globalKeywords) : base(messageAndKeywords) { }
        public bool AllowNone { get; set; }
        public bool AllowArbitraryInput { get; set; }
    }

    public sealed class PromptEntityOptions : PromptEditorOptions
    {
        public PromptEntityOptions(string message) : base(message) { }
        public PromptEntityOptions(string messageAndKeywords, string globalKeywords) : base(messageAndKeywords) { }
        public bool AllowObjectOnLockedLayer { get; set; }
        public void SetRejectMessage(string message) { }
        public void AddAllowedClass(Type type, bool exactMatch) { }
        public void RemoveAllowedClass(Type type) { }
    }

    public sealed class PromptNestedEntityOptions : PromptEditorOptions
    {
        public PromptNestedEntityOptions(string message) : base(message) { }
        public bool UseNonInteractivePickPoint { get; set; }
        public Point3d NonInteractivePickPoint { get; set; }
        public bool AllowNone2 { get; set; }
    }

    public sealed class PromptSelectionOptions
    {
        public string MessageForAdding { get; set; }
        public string MessageForRemoval { get; set; }
        public bool SingleOnly { get; set; }
        public bool SinglePickInSpace { get; set; }
        public bool AllowDuplicates { get; set; }
        public bool AllowSubSelections { get; set; }
        public bool RejectObjectsOnLockedLayers { get; set; }
        public bool RejectObjectsFromNonCurrentSpace { get; set; }
        public bool RejectPaperspaceViewport { get; set; }
        public bool SelectEverythingInAperture { get; set; }
        public bool PrepareOptionalDetails { get; set; }
        public bool ForceSubSelections { get; set; }
        public KeywordCollection Keywords { get; } = new KeywordCollection();
        public void SetKeywords(string keywords, string globalKeywords) { }
        public event SelectionTextInputEventHandler KeywordInput;
        public event SelectionTextInputEventHandler UnknownInput;
        public void Raise() { if (KeywordInput != null) KeywordInput(this, null); if (UnknownInput != null) UnknownInput(this, null); }
    }

    public delegate void SelectionTextInputEventHandler(object sender, SelectionTextInputEventArgs e);
    public sealed class SelectionTextInputEventArgs : EventArgs { public string Input { get; } public void AddObjects(ObjectId[] ids) { } }

    public sealed class SelectionFilter
    {
        public SelectionFilter(TypedValue[] values) { }
        public TypedValue[] GetFilter() { return new TypedValue[0]; }
    }

    public class PromptResult
    {
        public PromptStatus Status { get; }
        public string StringResult { get; }
    }

    public sealed class PromptPointResult : PromptResult { public Point3d Value { get; } }
    public sealed class PromptDoubleResult : PromptResult { public double Value { get; } }
    public sealed class PromptIntegerResult : PromptResult { public int Value { get; } }
    public sealed class PromptEntityResult : PromptResult { public ObjectId ObjectId { get; } public Point3d PickedPoint { get; } }
    public sealed class PromptNestedEntityResult : PromptResult { public ObjectId ObjectId { get; } public Point3d PickedPoint { get; } public ObjectId[] GetContainers() { return new ObjectId[0]; } public Matrix3d Transform { get; } }
    public sealed class PromptSelectionResult { public PromptStatus Status { get; } public SelectionSet Value { get; } }

    public class SelectionSet : IEnumerable<SelectedObject>
    {
        public int Count { get; }
        public ObjectId[] GetObjectIds() { return new ObjectId[0]; }
        public SelectedObject this[int i] { get { return null; } }
        public static SelectionSet FromObjectIds(ObjectId[] ids) { return new SelectionSet(); }
        public IEnumerator<SelectedObject> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    public class SelectedObject { public ObjectId ObjectId { get; } public SelectionMethod SelectionMethod { get; } }
    public enum SelectionMethod { Unavailable = 0, PickPoint = 1, Window = 2, Crossing = 3, Fence = 4, SubEntity = 5, NonGraphical = 6 }

    public sealed class EditorUserInteraction : IDisposable
    {
        public void End() { }
        public void Dispose() { }
    }

    public sealed class Editor
    {
        public Autodesk.AutoCAD.ApplicationServices.Document Document { get; }
        public Matrix3d CurrentUserCoordinateSystem { get; set; }
        public void WriteMessage(string message) { }
        public void WriteMessage(string format, params object[] args) { }
        public PromptPointResult GetPoint(PromptPointOptions options) { return new PromptPointResult(); }
        public PromptPointResult GetPoint(string message) { return new PromptPointResult(); }
        public PromptDoubleResult GetDistance(PromptDistanceOptions options) { return new PromptDoubleResult(); }
        public PromptDoubleResult GetDistance(string message) { return new PromptDoubleResult(); }
        public PromptDoubleResult GetDouble(PromptDoubleOptions options) { return new PromptDoubleResult(); }
        public PromptDoubleResult GetDouble(string message) { return new PromptDoubleResult(); }
        public PromptDoubleResult GetAngle(PromptAngleOptions options) { return new PromptDoubleResult(); }
        public PromptIntegerResult GetInteger(PromptIntegerOptions options) { return new PromptIntegerResult(); }
        public PromptIntegerResult GetInteger(string message) { return new PromptIntegerResult(); }
        public PromptResult GetString(PromptStringOptions options) { return new PromptResult(); }
        public PromptResult GetString(string message) { return new PromptResult(); }
        public PromptResult GetKeywords(PromptKeywordOptions options) { return new PromptResult(); }
        public PromptResult GetKeywords(string message, params string[] keywords) { return new PromptResult(); }
        public PromptEntityResult GetEntity(PromptEntityOptions options) { return new PromptEntityResult(); }
        public PromptEntityResult GetEntity(string message) { return new PromptEntityResult(); }
        public PromptNestedEntityResult GetNestedEntity(PromptNestedEntityOptions options) { return new PromptNestedEntityResult(); }
        public PromptSelectionResult GetSelection() { return new PromptSelectionResult(); }
        public PromptSelectionResult GetSelection(SelectionFilter filter) { return new PromptSelectionResult(); }
        public PromptSelectionResult GetSelection(PromptSelectionOptions options) { return new PromptSelectionResult(); }
        public PromptSelectionResult GetSelection(PromptSelectionOptions options, SelectionFilter filter) { return new PromptSelectionResult(); }
        public PromptSelectionResult SelectAll() { return new PromptSelectionResult(); }
        public PromptSelectionResult SelectAll(SelectionFilter filter) { return new PromptSelectionResult(); }
        public PromptSelectionResult SelectImplied() { return new PromptSelectionResult(); }
        public PromptSelectionResult SelectWindow(Point3d a, Point3d b) { return new PromptSelectionResult(); }
        public PromptSelectionResult SelectCrossingWindow(Point3d a, Point3d b) { return new PromptSelectionResult(); }
        public PromptSelectionResult SelectCrossingWindow(Point3d a, Point3d b, SelectionFilter filter) { return new PromptSelectionResult(); }
        public PromptSelectionResult SelectWindow(Point3d a, Point3d b, SelectionFilter filter) { return new PromptSelectionResult(); }
        public PromptSelectionResult SelectCrossingPolygon(Point3dCollection points) { return new PromptSelectionResult(); }
        public PromptSelectionResult SelectCrossingPolygon(Point3dCollection points, SelectionFilter filter) { return new PromptSelectionResult(); }
        public PromptSelectionResult SelectWindowPolygon(Point3dCollection points) { return new PromptSelectionResult(); }
        public PromptSelectionResult SelectWindowPolygon(Point3dCollection points, SelectionFilter filter) { return new PromptSelectionResult(); }
        public void SetImpliedSelection(ObjectId[] ids) { }
        public void SetImpliedSelection(SelectionSet set) { }
        public void UpdateScreen() { }
        public void Regen() { }
        public EditorUserInteraction StartUserInteraction(System.Windows.Forms.Control control) { return new EditorUserInteraction(); }
        public EditorUserInteraction StartUserInteraction(IntPtr handle) { return new EditorUserInteraction(); }
        public Autodesk.AutoCAD.GraphicsSystem.View GetCurrentView() { return new Autodesk.AutoCAD.GraphicsSystem.View(); }
        public ViewTableRecord GetCurrentView2() { return new ViewTableRecord(); }
        public void SetCurrentView(ViewTableRecord view) { }
        public Point3d PointToWorld(System.Drawing.Point point) { return Point3d.Origin; }
        public System.Drawing.Point PointToScreen(Point3d point, int viewport) { return new System.Drawing.Point(); }
        public void DrawVector(Point3d a, Point3d b, int color, bool highlighted) { }
        public bool IsQuiescent { get; }
        public bool IsDragging { get; }
        public PromptResult Command(params object[] args) { return new PromptResult(); }
        public void Redraw() { }
        public void ApplyCurDwgLayerTableChanges() { }
        public void SwitchToModelSpace() { }
        public void SwitchToPaperSpace() { }
        public int ActiveViewportId { get { return 0; } }
    }

    public sealed class ViewTableRecord : IDisposable
    {
        public Point2d CenterPoint { get; set; }
        public double Height { get; set; }
        public double Width { get; set; }
        public Vector3d ViewDirection { get; set; }
        public Point3d Target { get; set; }
        public double ViewTwist { get; set; }
        public void Dispose() { }
    }
}

namespace Autodesk.AutoCAD.GraphicsSystem
{
    public sealed class View : IDisposable { public void Dispose() { } public void ZoomExtents(Point3d min, Point3d max) { } public void Zoom(double factor) { } }
}

namespace Autodesk.AutoCAD.ApplicationServices
{
    using Autodesk.AutoCAD.DatabaseServices;
    using Autodesk.AutoCAD.EditorInput;

    public sealed class DocumentLock : IDisposable { public void Dispose() { } }

    public sealed class Document
    {
        public Editor Editor { get; }
        public Database Database { get; }
        public string Name { get; }
        public bool IsActive { get; }
        public bool IsReadOnly { get; }
        public Window Window { get; }
        public DocumentLock LockDocument() { return new DocumentLock(); }
        public DocumentLock LockDocument(DocumentLockMode mode, string globalCommandName, string localCommandName, bool promptIfFails) { return new DocumentLock(); }
        public void SendStringToExecute(string command, bool activate, bool wrapUpInactiveDoc, bool echoCommand) { }
        public object GetLispSymbol(string name) { return null; }
        public object TransactionManager { get; }
        public string CommandInProgress { get; }
        public Autodesk.AutoCAD.DatabaseServices.TransactionManager TransactionManager2 { get; }
        public void CloseAndDiscard() { }
        public void CloseAndSave(string fileName) { }
        public event EventHandler CommandEnded;
        public void Raise() { if (CommandEnded != null) CommandEnded(this, EventArgs.Empty); }
    }

    public enum DocumentLockMode { NotLocked = 0, AutoWrite = 1, Read = 2, Write = 4, ProtectedAutoWrite = 8, ExclusiveWrite = 16 }

    public sealed class Window { public IntPtr Handle { get; } public string Text { get; set; } public void Focus() { } }

    public sealed class DocumentCollection : IEnumerable<Document>
    {
        public Document MdiActiveDocument { get; set; }
        public int Count { get; }
        public Document Open(string fileName, bool forReadOnly) { return null; }
        public Document Add(string template) { return null; }
        public IEnumerator<Document> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        public event EventHandler DocumentActivated;
        public event EventHandler DocumentCreated;
        public event EventHandler DocumentToBeDestroyed;
        public void Raise() { if (DocumentActivated != null) DocumentActivated(this, EventArgs.Empty); if (DocumentCreated != null) DocumentCreated(this, EventArgs.Empty); if (DocumentToBeDestroyed != null) DocumentToBeDestroyed(this, EventArgs.Empty); }
    }

    public static class Application
    {
        public static DocumentCollection DocumentManager { get; } = new DocumentCollection();
        public static Version Version { get { return new Version(24, 3); } }
        public static Window MainWindow { get; }
        public static System.Windows.Forms.DialogResult ShowModalDialog(System.Windows.Forms.Form form) { return System.Windows.Forms.DialogResult.OK; }
        public static System.Windows.Forms.DialogResult ShowModalDialog(IntPtr owner, System.Windows.Forms.Form form, bool persistSizeAndPosition) { return System.Windows.Forms.DialogResult.OK; }
        public static System.Windows.Forms.DialogResult ShowModalDialog(IntPtr owner, System.Windows.Forms.Form form) { return System.Windows.Forms.DialogResult.OK; }
        public static void ShowModelessDialog(System.Windows.Forms.Form form) { }
        public static void ShowModelessDialog(IntPtr owner, System.Windows.Forms.Form form, bool persistSizeAndPosition) { }
        public static bool? ShowModalWindow(System.Windows.Window window) { return true; }
        public static void ShowAlertDialog(string message) { }
        public static object GetSystemVariable(string name) { return null; }
        public static void SetSystemVariable(string name, object value) { }
        public static void UpdateScreen() { }
        public static bool IsQuiescent { get; }
        public static event EventHandler Idle;
        public static void Raise() { if (Idle != null) Idle(null, EventArgs.Empty); }
        public static Autodesk.AutoCAD.Runtime.Exception ExceptionOf() { return null; }
        public static System.Windows.Forms.DialogResult ShowModalDialog(System.Windows.Forms.Form form, bool persistSizeAndPosition) { return System.Windows.Forms.DialogResult.OK; }
    }
}

namespace System.Windows { public class Window { } }

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

    public class StyleBase : DBObject { public string Name { get; set; } public string Description { get; set; } }
    public sealed class LabelStyle : StyleBase { }
    public sealed class PointStyle : StyleBase { }
}

namespace Autodesk.Civil.DatabaseServices
{
    using Autodesk.AutoCAD.DatabaseServices;
    using Autodesk.AutoCAD.Geometry;

    public class Label : Entity
    {
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
        public double MarkerRotationAngle { get; set; }
        public bool IsMarkerRotationAngleOverridden { get; set; }
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

    public class FeatureLine : Entity
    {
        public int PointsCount { get; }
        public double Length2D { get; }
        public Point3dCollection GetPoints(FeatureLinePointType type) { return new Point3dCollection(); }
        public string StyleName { get; }
        public ObjectId StyleId { get; }
        public ObjectId SiteId { get; }
        public string Name { get; set; }
    }

    public enum FeatureLinePointType { ElevationPoint = 1, PIPoint = 2, AllPoints = 3 }

    public sealed class SurveyFigure : FeatureLine { }

    public class Alignment : Entity { public string Name { get; set; } public double Length { get; } public double StartingStation { get; } public double EndingStation { get; } public ObjectId StyleId { get; set; } }
    public class Parcel : Entity { public string Name { get; set; } public double Area { get; } public double Perimeter { get; } }
    public class TinSurface : Entity { public string Name { get; set; } public double FindElevationAtXY(double x, double y) { return 0; } }
}
