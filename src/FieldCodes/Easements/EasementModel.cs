using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Easements
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LocationSource
    {
        CogoPoint, CadPoint, GeometryEndpoint, Manual,
        /// <summary>Worked out from other geometry -- where the easement line meets a trim line.</summary>
        Computed
    }

    /// <summary>A selected location and exactly where it came from.</summary>
    public sealed class SelectedLocation
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("source")] public LocationSource Source { get; set; }

        /// <summary>COGO point number when the source is a survey point.</summary>
        [JsonProperty("pointNumber")] public string PointNumber { get; set; }

        /// <summary>Handle of the CAD object the location was taken from.</summary>
        [JsonProperty("handle")] public string Handle { get; set; }

        [JsonIgnore] public P2 Point { get { return new P2(X, Y); } }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum WidthMode { Centered, LeftRight }

    /// <summary>
    /// Easement width. Left and right are measured from the controlling route,
    /// looking along its direction of travel. The end values exist so a tapered
    /// easement can be described; until tapering is implemented a spec whose end
    /// differs from its start is rejected rather than drawn constant.
    /// </summary>
    public sealed class WidthSpec
    {
        [JsonProperty("mode")] public WidthMode Mode { get; set; }
        [JsonProperty("left")] public double Left { get; set; }
        [JsonProperty("right")] public double Right { get; set; }
        [JsonProperty("leftEnd")] public double? LeftEnd { get; set; }
        [JsonProperty("rightEnd")] public double? RightEnd { get; set; }

        [JsonIgnore] public double Total { get { return Left + Right; } }

        [JsonIgnore]
        public bool IsTapered
        {
            get
            {
                return (LeftEnd.HasValue && Math.Abs(LeftEnd.Value - Left) > 1e-9) ||
                       (RightEnd.HasValue && Math.Abs(RightEnd.Value - Right) > 1e-9);
            }
        }

        public static WidthSpec Centered(double total)
        {
            return new WidthSpec { Mode = WidthMode.Centered, Left = total / 2.0, Right = total / 2.0 };
        }

        public static WidthSpec Sides(double left, double right)
        {
            return new WidthSpec { Mode = WidthMode.LeftRight, Left = left, Right = right };
        }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum TerminationMethod
    {
        /// <summary>Square across the route at its end.</summary>
        Perpendicular,
        /// <summary>Sidelines extended or trimmed to selected geometry.</summary>
        Boundary,
        /// <summary>Square across the route at a station.</summary>
        Station,
        /// <summary>Square across the route through a selected point.</summary>
        Point
    }

    /// <summary>How one end of the strip closes.</summary>
    public sealed class TerminationSpec
    {
        [JsonProperty("method")] public TerminationMethod Method { get; set; }

        /// <summary>What the boundary is, for the record: PROPERTY LINE, ROW, ...</summary>
        [JsonProperty("kind")] public string BoundaryKind { get; set; }

        [JsonProperty("boundary")] public List<Course> Boundary { get; set; }
        [JsonProperty("boundaryHandle")] public string BoundaryHandle { get; set; }
        [JsonProperty("station")] public double? Station { get; set; }
        [JsonProperty("point")] public SelectedLocation Point { get; set; }

        public static TerminationSpec Perpendicular()
        {
            return new TerminationSpec { Method = TerminationMethod.Perpendicular };
        }
    }

    /// <summary>One side of a clicked area: straight between its corners, or following an object.</summary>
    public sealed class AreaSide
    {
        [JsonProperty("follow")] public string FollowHandle { get; set; }
        [JsonProperty("type")] public string FollowType { get; set; }
    }

    /// <summary>A source object that controls the easement, and a fingerprint of its
    /// geometry when the easement was built.</summary>
    public sealed class GeometrySource
    {
        [JsonProperty("handle")] public string Handle { get; set; }
        [JsonProperty("type")] public string EntityType { get; set; }
        [JsonProperty("role")] public string Role { get; set; }
        [JsonProperty("fingerprint")] public string Fingerprint { get; set; }
    }

    /// <summary>A line or curve with the values a legal description needs.</summary>
    public sealed class CourseData
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("course")] public Course Course { get; set; }
        [JsonProperty("azimuth")] public double AzimuthDegrees { get; set; }
        [JsonProperty("length")] public double Length { get; set; }
        [JsonProperty("radius")] public double? Radius { get; set; }
        [JsonProperty("delta")] public double? DeltaDegrees { get; set; }
        [JsonProperty("chordAzimuth")] public double? ChordAzimuthDegrees { get; set; }
        [JsonProperty("chord")] public double? ChordLength { get; set; }
        [JsonProperty("tangent")] public double? Tangent { get; set; }
        [JsonProperty("direction")] public string TurnDirection { get; set; }
    }

    /// <summary>
    /// Everything about one strip easement, ordered so a legal description can be
    /// written from it later: commencement, tie, beginning, courses, terminus,
    /// width and area. The legal status stays "not generated" -- any future legal
    /// text needs a surveyor's review before it is used.
    /// </summary>
    public sealed class EasementRecord
    {
        [JsonProperty("schema")] public string Schema { get; set; }
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("title")] public string Title { get; set; }
        [JsonProperty("purpose")] public string Purpose { get; set; }
        [JsonProperty("createdUtc")] public DateTime CreatedUtc { get; set; }
        [JsonProperty("profile")] public string Profile { get; set; }

        [JsonProperty("poc")] public SelectedLocation PointOfCommencement { get; set; }
        [JsonProperty("tie")] public CourseData CommencementTie { get; set; }

        /// <summary>
        /// The tie from the Point of Commencement as the courses it actually runs, when it follows a line or
        /// curve (a right-of-way margin drawn as a line and an arc, say). Null or empty: the tie is the single
        /// straight <see cref="CommencementTie"/>. When set, CommencementTie is its first course.
        /// </summary>
        [JsonProperty("tieCourses")] public List<CourseData> CommencementTieCourses { get; set; }

        /// <summary>The objects a followed tie runs along, in order from the Point of Commencement.</summary>
        [JsonProperty("tieFollows")] public List<GeometrySource> CommencementTieFollows { get; set; }

        [JsonProperty("tpob")] public SelectedLocation TruePointOfBeginning { get; set; }

        [JsonProperty("routeSources")] public List<GeometrySource> RouteSources { get; set; }
        [JsonProperty("route")] public List<CourseData> RouteCourses { get; set; }
        [JsonProperty("terminus")] public P2 Terminus { get; set; }

        [JsonProperty("width")] public WidthSpec Width { get; set; }
        [JsonProperty("begin")] public TerminationSpec Begin { get; set; }
        [JsonProperty("end")] public TerminationSpec End { get; set; }
        [JsonProperty("parcel")] public GeometrySource Parcel { get; set; }

        [JsonProperty("boundary")] public List<CourseData> BoundaryCourses { get; set; }
        [JsonProperty("areaSqFt")] public double AreaSquareFeet { get; set; }
        [JsonProperty("acres")] public double Acres { get; set; }

        [JsonProperty("warnings")] public List<string> Warnings { get; set; }
        [JsonProperty("drafted")] public List<string> DraftedHandles { get; set; }

        /// <summary>Fingerprint of the drawn boundary, so a hand edit to the drafting
        /// is noticed before a rebuild would replace it.</summary>
        [JsonProperty("draftedFingerprint")] public string DraftedFingerprint { get; set; }

        /// <summary>True when the sidelines were clipped to a parent parcel.</summary>
        [JsonProperty("clipped")] public bool ClippedToParcel { get; set; }

        /// <summary>The easement line's angle points in order, when it was clicked point by point.</summary>
        [JsonProperty("anglePoints")] public List<SelectedLocation> AnglePoints { get; set; }

        /// <summary>Where the easement line started before trimming; orients an object route on a rebuild.</summary>
        [JsonProperty("routeStart")] public P2? RouteStart { get; set; }

        /// <summary>The lines the strip was trimmed to. Null for easements made before trimming existed.</summary>
        [JsonProperty("trimLines")] public List<GeometrySource> TrimLines { get; set; }

        /// <summary>A point inside each piece the drafter kept, so a rebuild keeps the same pieces.</summary>
        [JsonProperty("keep")] public List<P2> KeepPoints { get; set; }

        [JsonProperty("legalStatus")] public string LegalStatus { get; set; }

        /// <summary>Easements drawn together on one centerline share a group: the permanent
        /// easement and its temporary construction easement.</summary>
        [JsonProperty("group")] public string GroupId { get; set; }

        /// <summary>PERMANENT, or TEMPORARY for a temporary construction easement.</summary>
        [JsonProperty("role")] public string Role { get; set; }

        /// <summary>The trim line the Point of Beginning lies on, if any.</summary>
        [JsonProperty("beginsOn")] public GeometrySource BeginsOn { get; set; }

        /// <summary>The trim line the terminus lies on, if any.</summary>
        [JsonProperty("endsOn")] public GeometrySource EndsOn { get; set; }

        /// <summary>The line the tie from the Point of Commencement runs along, when both
        /// ends of the tie are on it.</summary>
        [JsonProperty("tieAlong")] public GeometrySource CommencementAlong { get; set; }

        /// <summary>The corner the terminus is tied to, and the tie from the terminus to it.</summary>
        [JsonProperty("terminusTiePoint")] public SelectedLocation TerminusTiePoint { get; set; }
        [JsonProperty("terminusTie")] public CourseData TerminusTie { get; set; }

        /// <summary>
        /// A portion easement's lot when it was built from separately drawn lot lines rather than one closed
        /// object: the lines, in order around the lot. The objects are never joined; FTF keeps its own boundary.
        /// </summary>
        [JsonProperty("lotLines")] public List<GeometrySource> LotLines { get; set; }

        /// <summary>The pattern scale an exhibit last gave each of this easement's hatches, by hatch handle. A hatch redrawn by a rebuild has a new handle and starts afresh.</summary>
        [JsonProperty("hatchScales")] public Dictionary<string, double> HatchScales { get; set; }

        /// <summary>A hatch pattern scale the drafter set by hand. FTF keeps it and does not rescale the hatch for exhibits.</summary>
        [JsonProperty("hatchScaleByHand")] public double? HatchPatternScaleByHand { get; set; }

        /// <summary>The commencement tie as courses: the followed courses, or the single straight tie.</summary>
        public IList<CourseData> TieCourses()
        {
            return Ties.Of(CommencementTie, CommencementTieCourses);
        }

        /// <summary>STRIP (null in older drawings), PORTION or AREA.</summary>
        [JsonProperty("kind")] public string Kind { get; set; }

        /// <summary>A portion easement's calls, in the order they are read.</summary>
        [JsonProperty("portion")] public List<PortionStep> PortionSteps { get; set; }

        /// <summary>A clicked area's sides: side i runs from corner i to the next corner, and
        /// follows the object with this handle when it has one.</summary>
        [JsonProperty("sides")] public List<AreaSide> AreaSides { get; set; }

        public const string StripKind = "STRIP";
        public const string PortionKind = "PORTION";
        public const string AreaKind = "AREA";

        [JsonIgnore] public bool IsPortion { get { return Kind == PortionKind; } }
        [JsonIgnore] public bool IsArea { get { return Kind == AreaKind; } }

        /// <summary>Areas excluded from the easement's own geometry (component A).</summary>
        [JsonProperty("exclusions")] public List<Exclusion> Exclusions { get; set; }

        /// <summary>Interior holes left by exclusions inside component A.</summary>
        [JsonProperty("holes")] public List<List<CourseData>> Holes { get; set; }

        /// <summary>Further areas of this same easement: components B, C...</summary>
        [JsonProperty("components")] public List<EasementComponent> Components { get; set; }

        /// <summary>Area of component A after its exclusions. AreaSquareFeet is the SUM OF COMPONENT AREAS.</summary>
        [JsonProperty("primaryAreaSqFt")] public double? PrimaryAreaSquareFeet { get; set; }

        /// <summary>
        /// TOTAL PHYSICAL AREA: the ground the components cover, an overlap counted once. Equal to the
        /// sum when nothing overlaps; null when there are no components, or when overlapping components
        /// could not be resolved exactly (then only the sum is known and that is said).
        /// </summary>
        [JsonProperty("physicalAreaSqFt")] public double? PhysicalAreaSquareFeet { get; set; }

        /// <summary>True when components were found to overlap (the sum then counts shared ground twice).</summary>
        [JsonProperty("componentsOverlap")] public bool ComponentsOverlap { get; set; }

        /// <summary>The area an exhibit shows as the easement's area: the physical area when components
        /// overlap and it is known, otherwise the sum.</summary>
        [JsonIgnore] public double DisplayAreaSquareFeet
        {
            get { return ComponentsOverlap && PhysicalAreaSquareFeet.HasValue ? PhysicalAreaSquareFeet.Value : AreaSquareFeet; }
        }

        /// <summary>Easements shown together on one exhibit (a permanent easement and its temporary one, say).</summary>
        [JsonProperty("exhibitGroup")] public string ExhibitGroup { get; set; }

        /// <summary>When the easement was last rebuilt after survey changes.</summary>
        [JsonProperty("rebuiltUtc")] public DateTime? RebuiltUtc { get; set; }

        [JsonIgnore] public bool HasComposition
        {
            get { return (Exclusions != null && Exclusions.Count > 0) || (Components != null && Components.Count > 0); }
        }

        /// <summary>Names and parcel text for the draft legal description, as last entered.</summary>
        [JsonProperty("legal")] public LegalInputs Legal { get; set; }

        [JsonIgnore] public bool IsTemporary { get { return Role == TemporaryRole; } }

        public const string PermanentRole = "PERMANENT";
        public const string TemporaryRole = "TEMPORARY";

        /// <summary>True for easements built from angle points and trim lines.</summary>
        [JsonIgnore] public bool Trimmable { get { return TrimLines != null; } }

        public EasementRecord()
        {
            Schema = "ftf-easement-2";
            Id = Guid.NewGuid().ToString("N");
            CreatedUtc = DateTime.UtcNow;
            RouteSources = new List<GeometrySource>();
            RouteCourses = new List<CourseData>();
            BoundaryCourses = new List<CourseData>();
            Warnings = new List<string>();
            DraftedHandles = new List<string>();
            LegalStatus = "NOT GENERATED - any legal description requires surveyor review";
        }

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.None); }

        public static EasementRecord FromJson(string json)
        {
            return JsonConvert.DeserializeObject<EasementRecord>(json);
        }
    }
}
