using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Compare
{
    // =====================================================================
    // FTFCOMPARE data model.
    //
    // Everything the comparison reasons about is extracted from the drawings
    // into these immutable-by-convention records FIRST; the matchers never
    // touch a CAD database. The CAD layer extracts (reader) and later applies
    // the approved plan (layer manager) through its own controlled
    // transactions. Nothing here references Autodesk types, so the whole
    // engine is unit tested without a licence.
    // =====================================================================

    /// <summary>JSON settings for every comparison record: lists in a file REPLACE the
    /// constructor defaults rather than being appended to them.</summary>
    public static class CompareJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Include
        };
    }

    /// <summary>Which drawing a feature came from.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum CompareSource { Survey, Consultant }

    /// <summary>What shape of thing a feature is, which decides which matcher sees it.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum FeatureKind
    {
        /// <summary>A point feature: a block, COGO point, structure or node.</summary>
        Point,
        /// <summary>Linework: line, polyline, arc, figure, feature line, pipe.</summary>
        Line,
        /// <summary>Text, MText, multileader text, block attributes, labels.</summary>
        Annotation
    }

    /// <summary>A 2D point with an optional elevation. NaN means "no Z".</summary>
    public struct CPoint
    {
        public double X;
        public double Y;
        public double Z;

        public CPoint(double x, double y) { X = x; Y = y; Z = double.NaN; }
        public CPoint(double x, double y, double z) { X = x; Y = y; Z = z; }

        [JsonIgnore] public bool HasZ { get { return !double.IsNaN(Z); } }

        public double DistanceTo(CPoint other)
        {
            var dx = X - other.X;
            var dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, HasZ ? "({0:0.###},{1:0.###},{2:0.###})" : "({0:0.###},{1:0.###})", X, Y, Z);
        }
    }

    /// <summary>An axis-aligned 2D box.</summary>
    public struct Box2
    {
        public double MinX, MinY, MaxX, MaxY;

        public Box2(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
        }

        public static Box2 Empty { get { return new Box2(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue); } }

        [JsonIgnore] public bool IsEmpty { get { return MinX > MaxX || MinY > MaxY; } }
        [JsonIgnore] public double Width { get { return IsEmpty ? 0 : MaxX - MinX; } }
        [JsonIgnore] public double Height { get { return IsEmpty ? 0 : MaxY - MinY; } }

        public static Box2 Of(CPoint p) { return new Box2(p.X, p.Y, p.X, p.Y); }

        public static Box2 Of(IEnumerable<CPoint> points)
        {
            var b = Empty;
            foreach (var p in points) b = b.Add(p);
            return b;
        }

        public Box2 Add(CPoint p)
        {
            return new Box2(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));
        }

        public Box2 Union(Box2 other)
        {
            if (other.IsEmpty) return this;
            if (IsEmpty) return other;
            return new Box2(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));
        }

        public Box2 Expand(double by)
        {
            if (IsEmpty) return this;
            return new Box2(MinX - by, MinY - by, MaxX + by, MaxY + by);
        }

        public bool Intersects(Box2 other)
        {
            if (IsEmpty || other.IsEmpty) return false;
            return other.MinX <= MaxX && other.MaxX >= MinX && other.MinY <= MaxY && other.MaxY >= MinY;
        }

        public bool Contains(CPoint p)
        {
            return !IsEmpty && p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
        }
    }

    /// <summary>The outcome of classifying one feature against the profile.</summary>
    public sealed class FeatureClass
    {
        /// <summary>Category key from the profile (STORM-MH, CURB ...). Empty when unknown.</summary>
        public string Category { get; set; }

        /// <summary>Utility or feature system: STORM, SANITARY, WATER, POWER, COMM, GAS, SURFACE, TOPO, OTHER.</summary>
        public string System { get; set; }

        /// <summary>0..1. One agreeing source of evidence is 0.6; more agreeing sources raise it.</summary>
        public double Confidence { get; set; }

        /// <summary>Human-readable statement of what decided the class ("layer V-UTIL-STRM-E; code SDMH").</summary>
        public string Evidence { get; set; }

        /// <summary>True when sources of evidence disagreed (layer says storm, code says sanitary).
        /// The strongest wins but any match it takes part in is sent to review.</summary>
        public bool Conflict { get; set; }

        [JsonIgnore] public bool IsUnknown { get { return string.IsNullOrEmpty(Category); } }

        public static FeatureClass Unknown(string why)
        {
            return new FeatureClass { Category = string.Empty, System = "UNKNOWN", Confidence = 0, Evidence = why ?? string.Empty };
        }
    }

    /// <summary>One value read from a piece of annotation: RIM 123.45, IE N 118.2, 12", PVC, SDMH 1234.</summary>
    public sealed class AnnotationFact
    {
        /// <summary>RIM, IE, IE-N, SIZE, MAT, ID, TEXT.</summary>
        public string Key { get; set; }
        public string Text { get; set; }
        public double? Number { get; set; }

        public override string ToString() { return Key + "=" + (Number.HasValue ? Number.Value.ToString("0.##", CultureInfo.InvariantCulture) : Text); }
    }

    /// <summary>
    /// One feature extracted from a drawing. The CAD reader fills these; the engine
    /// only reads them. Geometry is in the SURVEY drawing's coordinate system and
    /// units (xref and block transforms are already applied by the reader).
    /// </summary>
    public sealed class CompareFeature
    {
        /// <summary>Unique within its dataset: owner path plus handle.</summary>
        public string Id { get; set; }
        public CompareSource Source { get; set; }
        public FeatureKind Kind { get; set; }

        /// <summary>Entity handle in the database it lives in (the host drawing or an xref).</summary>
        public string Handle { get; set; }

        /// <summary>Empty for the host model space; "XREFNAME" or "XREF>NESTED" for referenced
        /// drawings. Consultant features reached through an xref carry it so the report can
        /// say which file they came from.</summary>
        public string OwnerPath { get; set; }

        /// <summary>LINE, LWPOLYLINE, POLYLINE2D, POLYLINE3D, ARC, SPLINE, INSERT, COGOPOINT,
        /// SURVEYFIGURE, FEATURELINE, PIPE, STRUCTURE, TEXT, MTEXT, MLEADER, ATTRIBUTE, LABEL.</summary>
        public string EntityType { get; set; }

        /// <summary>The layer name without any xref prefix.</summary>
        public string Layer { get; set; }
        public string BlockName { get; set; }

        /// <summary>Raw description / point code for points, figure name for linework,
        /// the text content for annotation.</summary>
        public string Description { get; set; }

        /// <summary>Block attributes (TAG → value), COGO point number, structure name, etc.</summary>
        public Dictionary<string, string> Attributes { get; set; }

        /// <summary>Location for points and annotation (insertion / arrow point).</summary>
        public CPoint Position { get; set; }

        /// <summary>Elevation when the feature carries one that means something
        /// (a COGO point's Z, a block inserted at grade). NaN / null otherwise.</summary>
        public double? Elevation { get; set; }

        /// <summary>Linework as a chorded polyline. Curves are sampled by the reader to a
        /// small chord tolerance; the ORIGINAL entity is never altered.</summary>
        public List<CPoint> Vertices { get; set; }
        public bool Closed { get; set; }

        /// <summary>True when the original entity has curved segments (so a split must be
        /// done on the real curve, not on these chords).</summary>
        public bool HasCurves { get; set; }

        /// <summary>For annotation: the feature this text belongs to (attribute owner, label
        /// anchor, nearest structure). Set by the reader where known, else by the matcher.</summary>
        public string AssociatedFeatureId { get; set; }

        /// <summary>Parsed annotation facts.</summary>
        public List<AnnotationFact> Facts { get; set; }

        public FeatureClass Class { get; set; }

        /// <summary>Set by the reader when the feature could not be fully read (unsupported
        /// entity, unreadable block). Such a feature is reported, never silently matched or
        /// declared missing.</summary>
        public string ReadProblem { get; set; }

        /// <summary>Annotation whose text could not be read (a Civil 3D label whose content
        /// is style-driven). It proves a label exists at this place and nothing more: our
        /// annotation beside it goes to review rather than being matched or declared missing.</summary>
        public bool Opaque { get; set; }

        // ---- derived, cached ---------------------------------------------------

        private double[] _stations;
        private Box2? _bounds;
        private double _length = -1;

        /// <summary>Cumulative distance to each vertex. Points and annotation have none.</summary>
        [JsonIgnore]
        public double[] Stations
        {
            get
            {
                if (_stations != null) return _stations;
                var v = Vertices ?? new List<CPoint>();
                var s = new double[v.Count];
                for (var i = 1; i < v.Count; i++) s[i] = s[i - 1] + v[i - 1].DistanceTo(v[i]);
                _stations = s;
                return s;
            }
        }

        [JsonIgnore]
        public double Length
        {
            get
            {
                if (_length >= 0) return _length;
                var s = Stations;
                _length = s.Length == 0 ? 0 : s[s.Length - 1];
                return _length;
            }
        }

        [JsonIgnore]
        public Box2 Bounds
        {
            get
            {
                if (_bounds.HasValue) return _bounds.Value;
                var b = Kind == FeatureKind.Line && Vertices != null && Vertices.Count > 0 ? Box2.Of(Vertices) : Box2.Of(Position);
                _bounds = b;
                return b;
            }
        }

        /// <summary>Drops the cached geometry so edits during test setup are seen.</summary>
        public void InvalidateCache() { _stations = null; _bounds = null; _length = -1; }

        public string Attribute(string tag)
        {
            if (Attributes == null || string.IsNullOrEmpty(tag)) return null;
            string value;
            return Attributes.TryGetValue(tag, out value) ? value : null;
        }

        /// <summary>A short label for reports and the review window.</summary>
        public string Describe()
        {
            var what = !string.IsNullOrEmpty(BlockName) ? BlockName
                : !string.IsNullOrEmpty(Description) ? Description
                : EntityType;
            if (what != null && what.Length > 40) what = what.Substring(0, 40) + "...";
            return string.Format(CultureInfo.InvariantCulture, "{0} {1} on {2}", EntityType, what, Layer);
        }

        /// <summary>
        /// A stable fingerprint of what this feature IS -- kind, category, description and
        /// geometry rounded to a hundredth -- so a review decision made on it can be
        /// re-applied after a re-run, but only while the feature is unchanged.
        /// </summary>
        public string Fingerprint()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Kind).Append('|').Append(EntityType).Append('|').Append(Layer).Append('|')
              .Append(BlockName).Append('|').Append(Description).Append('|');
            if (Kind == FeatureKind.Line && Vertices != null)
            {
                foreach (var p in Vertices)
                    sb.Append(Round(p.X)).Append(',').Append(Round(p.Y)).Append(';');
            }
            else
            {
                sb.Append(Round(Position.X)).Append(',').Append(Round(Position.Y));
            }
            unchecked
            {
                // FNV-1a over the text: short, stable across runs and machines.
                ulong h = 14695981039346656037UL;
                foreach (var ch in sb.ToString()) { h ^= ch; h *= 1099511628211UL; }
                return h.ToString("x16");
            }
        }

        private static string Round(double v) { return Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture); }
    }

    /// <summary>Something the reader could not do that the comparison must know about.</summary>
    public sealed class DatasetIssue
    {
        public Severity Severity { get; set; }
        /// <summary>XREF-UNRESOLVED, XREF-UNLOADED, XREF-CLIPPED, UNITS, UNSUPPORTED, UNKNOWN-LAYER, READ-ERROR.</summary>
        public string Code { get; set; }
        public string Message { get; set; }
        /// <summary>True when the consultant data is incomplete because of it, so that
        /// "missing" cannot be concluded from it.</summary>
        public bool MakesIncomplete { get; set; }

        public override string ToString() { return Severity + " " + Code + ": " + Message; }
    }

    /// <summary>Everything read from one side of the comparison.</summary>
    public sealed class CompareDataset
    {
        public CompareSource Source { get; set; }
        public string Name { get; set; }
        public List<CompareFeature> Features { get; set; }
        public List<DatasetIssue> Issues { get; set; }

        /// <summary>Drawing units per foot as the reader found them (1 for feet, 0.3048 for metres).</summary>
        public double UnitsPerFoot { get; set; }

        /// <summary>Drawings whose content was read, including every xref that resolved.</summary>
        public List<string> FilesRead { get; set; }

        /// <summary>Layers seen, with entity counts -- for the unknown-layer report.</summary>
        public Dictionary<string, int> LayerCounts { get; set; }

        /// <summary>Objects the reader left out because they are not shown: on frozen, off or
        /// no-plot layers, invisible, or outside an xref clip. Counted by reason so the run
        /// says what it ignored; never used to cover anything.</summary>
        public Dictionary<string, int> HiddenCounts { get; set; }

        public CompareDataset()
        {
            Features = new List<CompareFeature>();
            Issues = new List<DatasetIssue>();
            FilesRead = new List<string>();
            LayerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            HiddenCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            UnitsPerFoot = 1.0;
        }

        public void CountHidden(string reason)
        {
            int n;
            HiddenCounts.TryGetValue(reason ?? "hidden", out n);
            HiddenCounts[reason ?? "hidden"] = n + 1;
        }

        /// <summary>True when something that should have been read was not (an unresolved
        /// xref, for instance). The engine then refuses to call anything "missing".</summary>
        [JsonIgnore]
        public bool Incomplete { get { return Issues.Any(i => i.MakesIncomplete); } }

        [JsonIgnore]
        public Box2 Bounds
        {
            get
            {
                var b = Box2.Empty;
                foreach (var f in Features) b = b.Union(f.Bounds);
                return b;
            }
        }

        public void Add(CompareFeature feature)
        {
            if (feature == null) return;
            feature.Source = Source;
            if (string.IsNullOrEmpty(feature.Id))
                feature.Id = (string.IsNullOrEmpty(feature.OwnerPath) ? string.Empty : feature.OwnerPath + "|") + feature.Handle;
            Features.Add(feature);
            var layer = feature.Layer ?? string.Empty;
            int n;
            LayerCounts.TryGetValue(layer, out n);
            LayerCounts[layer] = n + 1;
        }

        public void AddIssue(Severity severity, string code, string message, bool makesIncomplete)
        {
            Issues.Add(new DatasetIssue { Severity = severity, Code = code, Message = message, MakesIncomplete = makesIncomplete });
        }
    }
}
