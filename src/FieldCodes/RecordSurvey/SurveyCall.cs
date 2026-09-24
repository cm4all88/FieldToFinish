using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.RecordSurvey
{
    /// <summary>
    /// What a value IS, in the professional sense. The software must never present one
    /// kind as another: an inferred value is never a recorded call, and a calculated
    /// value is never a measurement.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ValueBasis
    {
        /// <summary>Read from the recorded document as a call.</summary>
        Recorded,
        /// <summary>A field measurement the document reports (an "(M)" value on a Record of Survey).</summary>
        Measured,
        /// <summary>Computed from other calls (a missing curve element solved from the rest).</summary>
        Calculated,
        /// <summary>Taken from the picture rather than from a written call. Never geometry on its own.</summary>
        Inferred,
        /// <summary>Typed by the reviewer.</summary>
        Entered
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum CallKind { Line, Curve }

    /// <summary>Review state of one extracted call.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum CallStatus
    {
        /// <summary>Confidence at or above the threshold and nothing flagged.</summary>
        Extracted,
        /// <summary>Below the threshold, ambiguous, or incomplete: the reviewer must decide before geometry is built.</summary>
        NeedsReview,
        /// <summary>The reviewer accepted it (as read or after editing).</summary>
        Approved,
        /// <summary>The reviewer set it aside; it is not built.</summary>
        Rejected
    }

    /// <summary>
    /// What a course is on the survey. Chooses the drafting standard (layer, label style,
    /// linetype) and the topology role. Configurable: the names must match a standard
    /// entry in the settings.
    /// </summary>
    public static class SurveyObjectType
    {
        public const string Boundary = "Boundary";
        public const string LotLine = "Lot Line";
        public const string RightOfWay = "Right of Way";
        public const string Centerline = "Centerline";
        public const string Easement = "Easement";
        public const string SectionLine = "Section Line";
        public const string QuarterSection = "Quarter Section";
        public const string TieLine = "Tie Line";
        public const string Adjoiner = "Adjoiner";
        public const string Monument = "Monument";

        public static readonly string[] All =
        {
            Boundary, LotLine, RightOfWay, Centerline, Easement, SectionLine, QuarterSection, TieLine, Adjoiner
        };
    }

    /// <summary>A bearing and distance as one source states them, with where each was read.</summary>
    public class CallValue
    {
        [JsonProperty("azimuth")] public double? AzimuthDegrees { get; set; }
        [JsonProperty("distance")] public double? DistanceFeet { get; set; }
        [JsonProperty("bearingText")] public string BearingText { get; set; }
        [JsonProperty("distanceText")] public string DistanceText { get; set; }
        [JsonProperty("bearingSource")] public SourceRef BearingSource { get; set; }
        [JsonProperty("distanceSource")] public SourceRef DistanceSource { get; set; }
        [JsonProperty("confidence")] public double Confidence { get; set; }

        public CallValue() { Confidence = 1.0; }

        [JsonIgnore] public bool Complete { get { return AzimuthDegrees.HasValue && DistanceFeet.HasValue; } }
        [JsonIgnore] public bool Empty { get { return !AzimuthDegrees.HasValue && !DistanceFeet.HasValue; } }

        public string Describe(int secondsDecimals, int distanceDecimals)
        {
            var b = AzimuthDegrees.HasValue ? Drafting.SurveyDirection.FormatBearing(AzimuthDegrees.Value, secondsDecimals, "°") : "(no bearing)";
            var d = DistanceFeet.HasValue ? Drafting.SurveyDirection.FormatDistance(DistanceFeet.Value, distanceDecimals, true) : "(no distance)";
            return b + " " + d;
        }
    }

    /// <summary>A bearing and distance from a named record source (R1, R2, P, D).</summary>
    public sealed class RecordValue : CallValue
    {
        /// <summary>"R1", "R2", "R" (unnumbered), "P" (plat), "D" (deed). Empty for the document itself.</summary>
        [JsonProperty("sourceId")] public string SourceId { get; set; }

        public RecordValue() { SourceId = string.Empty; }
    }

    /// <summary>The elements of a curve as the document states them and as reconciled.</summary>
    public sealed class CurveSpec
    {
        [JsonProperty("radius")] public double? Radius { get; set; }
        [JsonProperty("delta")] public double? DeltaDegrees { get; set; }
        [JsonProperty("arc")] public double? ArcLength { get; set; }
        [JsonProperty("chord")] public double? ChordLength { get; set; }
        [JsonProperty("chordAzimuth")] public double? ChordAzimuthDegrees { get; set; }
        [JsonProperty("tangent")] public double? TangentLength { get; set; }
        /// <summary>Bearing from the curve's start to its centre, when the document gives one.</summary>
        [JsonProperty("radialInAzimuth")] public double? RadialInAzimuthDegrees { get; set; }
        /// <summary>"LEFT" or "RIGHT" (the way the curve turns, travelling along the traverse), or null when not stated.</summary>
        [JsonProperty("turn")] public string Turn { get; set; }
        /// <summary>Tangent to the previous course; null when unknown, false when the document says NON-TANGENT.</summary>
        [JsonProperty("tangentIn")] public bool? TangentToPrevious { get; set; }
        /// <summary>Which elements were read from the document (R, DELTA, L, CH, CB, T), as opposed to solved.</summary>
        [JsonProperty("stated", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> StatedElements { get; set; }
        [JsonProperty("sources", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<SourceRef> Sources { get; set; }
        /// <summary>The curve's table tag (C3) when it came from a curve table.</summary>
        [JsonProperty("tag")] public string Tag { get; set; }

        public CurveSpec()
        {
            StatedElements = new List<string>();
            Sources = new List<SourceRef>();
        }

        public bool Stated(string element)
        {
            return StatedElements != null && StatedElements.Contains(element);
        }

        public CurveSpec Clone()
        {
            var c = (CurveSpec)MemberwiseClone();
            c.StatedElements = new List<string>(StatedElements ?? new List<string>());
            c.Sources = new List<SourceRef>(Sources ?? new List<SourceRef>());
            return c;
        }
    }

    /// <summary>
    /// One course of a recorded survey: a line or a curve, with its measured value, its
    /// record value(s), where each was read, how sure the extraction is, and its review
    /// state. This is the unit the review table shows and the geometry is built from.
    /// </summary>
    public sealed class SurveyCall
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public CallKind Kind { get; set; }
        /// <summary>The figure (lot, tract, boundary) this course belongs to. Empty until assigned.</summary>
        [JsonProperty("figure")] public string Figure { get; set; }
        /// <summary>Position around its figure, 1-based. Zero when not ordered yet.</summary>
        [JsonProperty("order")] public int Order { get; set; }
        /// <summary>The course runs the other way around the figure than the call is written (the distance
        /// is the same; the bearing is reversed when traversing). The recorded call is never altered.</summary>
        [JsonProperty("reversed")] public bool Reversed { get; set; }
        [JsonProperty("objectType")] public string ObjectType { get; set; }
        /// <summary>When this call is a second figure's copy of a course labelled once on the document (a shared lot
        /// line), the id of the original call. The geometry is built once; the copy carries the same source.</summary>
        [JsonProperty("sharedWith")] public string SharedWith { get; set; }

        [JsonProperty("measured")] public CallValue Measured { get; set; }
        [JsonProperty("records", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<RecordValue> Records { get; set; }
        [JsonProperty("basis")] public ValueBasis Basis { get; set; }
        [JsonProperty("curve")] public CurveSpec Curve { get; set; }

        [JsonProperty("confidence")] public double Confidence { get; set; }
        [JsonProperty("status")] public CallStatus Status { get; set; }
        [JsonProperty("alternatives", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<CallAlternative> Alternatives { get; set; }
        [JsonProperty("notes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Notes { get; set; }
        /// <summary>Where the call was read; the union of its value sources, for highlighting.</summary>
        [JsonProperty("source")] public SourceRef Source { get; set; }
        /// <summary>Estimated position of the course's midpoint on the page, pixels, from where its label sits. Null when unknown.</summary>
        [JsonProperty("pageHint")] public PageBox PageHint { get; set; }
        /// <summary>What the reviewer changed, if anything: "distance 148.52 -> 148.82 (Jane, 2026-09-19)".</summary>
        [JsonProperty("edits", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Edits { get; set; }

        public SurveyCall()
        {
            Kind = CallKind.Line;
            Figure = string.Empty;
            ObjectType = SurveyObjectType.Boundary;
            Measured = new CallValue();
            Records = new List<RecordValue>();
            Basis = ValueBasis.Recorded;
            Confidence = 1.0;
            Status = CallStatus.Extracted;
            Alternatives = new List<CallAlternative>();
            Notes = new List<string>();
            Edits = new List<string>();
        }

        /// <summary>The primary record value: the first one, or an empty value when there is none.</summary>
        [JsonIgnore]
        public RecordValue Record
        {
            get { return Records != null && Records.Count > 0 ? Records[0] : null; }
        }

        /// <summary>
        /// The value geometry is built from. Measured when the document gives one and the
        /// preference allows, else the record. Null when neither is complete. Never mixes a
        /// measured bearing with a record distance -- a course is one statement.
        /// </summary>
        public CallValue GeometryValue(bool preferMeasured, out ValueBasis basis)
        {
            basis = Basis;
            if (preferMeasured && Measured != null && Measured.Complete) { basis = ValueBasis.Measured; return Measured; }
            var record = Records != null ? Records.FirstOrDefault(r => r.Complete) : null;
            if (record != null) { basis = Basis == ValueBasis.Entered ? ValueBasis.Entered : ValueBasis.Recorded; return record; }
            if (Measured != null && Measured.Complete) { basis = ValueBasis.Measured; return Measured; }
            return null;
        }

        [JsonIgnore]
        public bool HasRecordAndMeasured
        {
            get { return Measured != null && !Measured.Empty && Records != null && Records.Any(r => !r.Empty); }
        }

        [JsonIgnore]
        public bool IsBuildable
        {
            get
            {
                if (Status == CallStatus.Rejected) return false;
                if (Kind == CallKind.Curve) return Curve != null;
                ValueBasis b;
                return GeometryValue(true, out b) != null;
            }
        }

        public SurveyCall Clone()
        {
            return JsonConvert.DeserializeObject<SurveyCall>(JsonConvert.SerializeObject(this));
        }

        public override string ToString()
        {
            return Id + " " + (Kind == CallKind.Curve ? "curve" : "line") + " " + Status;
        }
    }

    /// <summary>An alternative reading of one field of a call, offered to the reviewer.</summary>
    public sealed class CallAlternative
    {
        /// <summary>"distance", "bearing", "radius", "arc", "delta", "chord".</summary>
        [JsonProperty("field")] public string Field { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("value")] public double Value { get; set; }
        [JsonProperty("confidence")] public double Confidence { get; set; }
        [JsonProperty("reason")] public string Reason { get; set; }
        /// <summary>Which value the alternative applies to: "measured", or a record source id.</summary>
        [JsonProperty("target")] public string Target { get; set; }

        public override string ToString()
        {
            return Field + " " + Text + " (" + Reason + ")";
        }
    }

    /// <summary>A record source named on the document (R1 = AFN 9807150123) with what is known about it.</summary>
    public sealed class RecordReference
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("afn")] public string RecordingNumber { get; set; }
        [JsonProperty("volume")] public string Volume { get; set; }
        [JsonProperty("page")] public string Page { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("source")] public SourceRef Source { get; set; }
        [JsonProperty("confidence")] public double Confidence { get; set; }

        public RecordReference() { Confidence = 1.0; }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MonumentStatus { Found, Set, Calculated, Unknown }

    /// <summary>A monument the document describes, with its position on the page when known.</summary>
    public sealed class MonumentCall
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("status")] public MonumentStatus Status { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("source")] public SourceRef Source { get; set; }
        [JsonProperty("confidence")] public double Confidence { get; set; }
        [JsonProperty("pageHint")] public PageBox PageHint { get; set; }
        /// <summary>The corner it marks, as figure/vertex-index once tied to the reconstruction; empty until then.</summary>
        [JsonProperty("corner")] public string Corner { get; set; }
        [JsonProperty("callStatus")] public CallStatus ReviewStatus { get; set; }

        public MonumentCall() { Confidence = 1.0; Status = MonumentStatus.Unknown; Corner = string.Empty; }
    }

    /// <summary>A lot, block, tract or other figure the courses close around.</summary>
    public sealed class SurveyFigure
    {
        [JsonProperty("name")] public string Name { get; set; }
        /// <summary>"Lot", "Block", "Tract", "Boundary", "Easement", "Parcel".</summary>
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("lot")] public string Lot { get; set; }
        [JsonProperty("block")] public string Block { get; set; }
        [JsonProperty("closed")] public bool Closed { get; set; }
        /// <summary>Where the traverse starts, drawing units. Null until placed.</summary>
        [JsonProperty("start")] public Easements.P2? Start { get; set; }
        [JsonProperty("source")] public SourceRef Source { get; set; }
        [JsonProperty("pageHint")] public PageBox PageHint { get; set; }

        public SurveyFigure() { Closed = true; Kind = "Lot"; }
    }

    /// <summary>Something the document says that is not a course: a note, the basis of bearing, a street name...</summary>
    public sealed class SurveyAnnotation
    {
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("source")] public SourceRef Source { get; set; }
        [JsonProperty("confidence")] public double Confidence { get; set; }
    }

    /// <summary>What the document is: which kind of recorded survey, and its identity.</summary>
    public sealed class DocumentInfo
    {
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("title")] public string Title { get; set; }
        /// <summary>"Subdivision Plat", "Short Plat", "Record of Survey", "Boundary Line Adjustment", "Large Lot Subdivision", "Easement Exhibit", "Unknown".</summary>
        [JsonProperty("surveyType")] public string SurveyType { get; set; }
        [JsonProperty("recordingNumber")] public string RecordingNumber { get; set; }
        [JsonProperty("volumePage")] public string VolumePage { get; set; }
        [JsonProperty("county")] public string County { get; set; }
        [JsonProperty("surveyor")] public string Surveyor { get; set; }
        [JsonProperty("basisOfBearing")] public string BasisOfBearing { get; set; }
        [JsonProperty("scaleFeetPerInch")] public double? ScaleFeetPerInch { get; set; }
        [JsonProperty("sheet")] public string Sheet { get; set; }
        [JsonProperty("pages")] public int Pages { get; set; }

        public DocumentInfo() { SurveyType = "Unknown"; }
    }

    /// <summary>
    /// Everything FTFRECORD knows about one recorded document: what was read, what was
    /// extracted, how it was reviewed, and what was built. Stored in the drawing so the QC,
    /// source, label and rebuild commands find it, and exported as a file beside the drawing.
    /// </summary>
    public sealed class RecordSurveyProject
    {
        public const string Schema = "ftf-record-1";

        [JsonProperty("schema")] public string SchemaVersion { get; set; }
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("document")] public DocumentInfo Document { get; set; }
        [JsonProperty("createdUtc")] public DateTime CreatedUtc { get; set; }
        [JsonProperty("builtUtc")] public DateTime? BuiltUtc { get; set; }
        [JsonProperty("revision")] public int Revision { get; set; }
        [JsonProperty("profile")] public string Profile { get; set; }
        /// <summary>"ReconstructGeometry" or "RecreateSheet".</summary>
        [JsonProperty("mode")] public string Mode { get; set; }

        [JsonProperty("calls", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<SurveyCall> Calls { get; set; }
        [JsonProperty("figures", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<SurveyFigure> Figures { get; set; }
        [JsonProperty("references", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<RecordReference> References { get; set; }
        [JsonProperty("monuments", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<MonumentCall> Monuments { get; set; }
        [JsonProperty("annotations", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<SurveyAnnotation> Annotations { get; set; }
        /// <summary>Handles of the entities built, with the course or monument each stands for.</summary>
        [JsonProperty("built", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<BuiltEntity> Built { get; set; }
        /// <summary>The build report (closure per figure) from the last build.</summary>
        [JsonProperty("closures", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<FigureClosure> Closures { get; set; }
        [JsonProperty("warnings", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Warnings { get; set; }
        /// <summary>The review threshold in force when the calls were classified.</summary>
        [JsonProperty("reviewThreshold")] public double ReviewThreshold { get; set; }
        /// <summary>Whether geometry was built from measured values where available.</summary>
        [JsonProperty("builtFromMeasured")] public bool BuiltFromMeasured { get; set; }
        /// <summary>The OCR text the calls came from, kept so a rebuild can re-highlight sources.</summary>
        [JsonProperty("ocrPath")] public string OcrPath { get; set; }

        public RecordSurveyProject()
        {
            SchemaVersion = Schema;
            Id = "REC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
            Document = new DocumentInfo();
            CreatedUtc = DateTime.UtcNow;
            Revision = 1;
            Mode = "ReconstructGeometry";
            Calls = new List<SurveyCall>();
            Figures = new List<SurveyFigure>();
            References = new List<RecordReference>();
            Monuments = new List<MonumentCall>();
            Annotations = new List<SurveyAnnotation>();
            Built = new List<BuiltEntity>();
            Closures = new List<FigureClosure>();
            Warnings = new List<string>();
            ReviewThreshold = 0.85;
            BuiltFromMeasured = true;
        }

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.Indented); }

        public static RecordSurveyProject FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ConfigException("The record survey project is empty.");
            RecordSurveyProject p;
            try { p = JsonConvert.DeserializeObject<RecordSurveyProject>(json); }
            catch (JsonException ex) { throw new ConfigException("The record survey project is not valid JSON: " + ex.Message, ex); }
            if (p == null) throw new ConfigException("The record survey project is empty.");
            if (p.Calls == null) p.Calls = new List<SurveyCall>();
            if (p.Figures == null) p.Figures = new List<SurveyFigure>();
            if (p.References == null) p.References = new List<RecordReference>();
            if (p.Monuments == null) p.Monuments = new List<MonumentCall>();
            if (p.Annotations == null) p.Annotations = new List<SurveyAnnotation>();
            if (p.Built == null) p.Built = new List<BuiltEntity>();
            if (p.Closures == null) p.Closures = new List<FigureClosure>();
            if (p.Warnings == null) p.Warnings = new List<string>();
            if (p.Document == null) p.Document = new DocumentInfo();
            foreach (var c in p.Calls)
            {
                if (c.Records == null) c.Records = new List<RecordValue>();
                if (c.Alternatives == null) c.Alternatives = new List<CallAlternative>();
                if (c.Notes == null) c.Notes = new List<string>();
                if (c.Edits == null) c.Edits = new List<string>();
                if (c.Measured == null) c.Measured = new CallValue();
            }
            return p;
        }

        public SurveyCall FindCall(string id)
        {
            return Calls.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public RecordReference FindReference(string id)
        {
            return References.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The calls of one figure, in traverse order; unordered calls last, in id order.</summary>
        public List<SurveyCall> CallsOf(string figure)
        {
            return Calls.Where(c => string.Equals(c.Figure ?? string.Empty, figure ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(c => c.Order == 0 ? int.MaxValue : c.Order).ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                        .ToList();
        }

        public string NextCallId()
        {
            var n = 1;
            while (Calls.Any(c => c.Id == "K" + n.ToString(CultureInfo.InvariantCulture))) n++;
            return "K" + n.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>One entity FTFRECORD created and what it stands for.</summary>
    public sealed class BuiltEntity
    {
        [JsonProperty("handle")] public string Handle { get; set; }
        /// <summary>Line, Curve, Label, Mask, Monument, Table.</summary>
        [JsonProperty("role")] public string Role { get; set; }
        [JsonProperty("callId")] public string CallId { get; set; }
        [JsonProperty("figure")] public string Figure { get; set; }
        /// <summary>Other figures sharing the same geometry (a lot line built once for two lots).</summary>
        [JsonProperty("sharedWith", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> SharedWith { get; set; }
        [JsonProperty("layer")] public string Layer { get; set; }
        [JsonProperty("fingerprint")] public string Fingerprint { get; set; }

        public BuiltEntity() { SharedWith = new List<string>(); }
    }

    /// <summary>Closure of one figure as built.</summary>
    public sealed class FigureClosure
    {
        [JsonProperty("figure")] public string Figure { get; set; }
        [JsonProperty("closed")] public bool Closed { get; set; }
        [JsonProperty("misclosure")] public double Misclosure { get; set; }
        [JsonProperty("misclosureAzimuth")] public double? MisclosureAzimuth { get; set; }
        [JsonProperty("misclosureDx")] public double MisclosureDx { get; set; }
        [JsonProperty("misclosureDy")] public double MisclosureDy { get; set; }
        [JsonProperty("perimeter")] public double Perimeter { get; set; }
        [JsonProperty("precision")] public double? Precision { get; set; }
        [JsonProperty("area")] public double Area { get; set; }
        [JsonProperty("courses")] public int Courses { get; set; }
    }
}
