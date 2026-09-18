using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Utilities
{
    // =====================================================================
    // The storm / sewer dip data model.
    //
    // Three kinds of data live here and are NEVER mixed:
    //   1. Field observations  -- what the crew wrote down (dips, sizes, directions).
    //   2. Survey geometry     -- what the CAD point says (rim, northing, easting),
    //                             snapshotted so a later change is detectable.
    //   3. Calculations        -- derived values that always carry their inputs,
    //                             so they can be reproduced and audited.
    // A calculated value never overwrites an observation, and an inferred
    // connection never becomes an observation.
    // =====================================================================

    [JsonConverter(typeof(StringEnumConverter))]
    public enum UtilitySystem { Storm, Sanitary, Culvert, Water, Other }

    /// <summary>What the tape was held to when the dip was read.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MeasurementReference
    {
        /// <summary>The note did not say what the dip was taken to. The measurement is
        /// kept exactly; it is never used where a known reference is required.</summary>
        Unspecified,
        Invert, TopOfPipe, Springline, BottomOfStructure, WaterLevel,
        TopOfGrate, TopOfCasting, Other
    }

    /// <summary>Where a pipe's measurement reference came from.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ReferenceBasis
    {
        /// <summary>Nothing said what the dip was taken to.</summary>
        NotStated,
        /// <summary>Written on the note (INV, TOP, SPR...).</summary>
        StatedInFieldNote,
        /// <summary>Unmarked, classified by the office's documented field-note convention.</summary>
        FieldNoteConvention,
        /// <summary>Unmarked; the drafter confirmed the reference.</summary>
        ConfirmedByDrafter,
        /// <summary>The drafter entered the pipe and its reference.</summary>
        EnteredByDrafter
    }

    /// <summary>Where a structure's inside dimension came from.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum DimensionSource { Unknown, FieldObserved, UserEntry, Profile }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum PipeShape { Round, Elliptical, Arch, Box, Custom }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum FlowRole { Unknown, In, Out }

    /// <summary>Both sources are field observations -- typed notes or values the
    /// drafter entered from the field book. Neither is ever computed.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ObservationSource { FieldNote, UserEntry }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ConnectionStatus
    {
        /// <summary>Not yet examined.</summary>
        Unresolved,
        /// <summary>The search suggested it; nobody has confirmed it.</summary>
        Probable,
        /// <summary>The drafter confirmed a searched candidate.</summary>
        Confirmed,
        /// <summary>The drafter picked a structure the search did not offer.</summary>
        ManualOverride,
        /// <summary>Examined and deliberately left open.</summary>
        LeftUnresolved,
        /// <summary>The pipe runs beyond the surveyed area; there is nothing in the
        /// survey to connect it to. Not a problem and not a field question.</summary>
        OutsideSurveyLimits
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum Confidence { None, Low, Medium, High }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum DirectionKind { Unknown, Cardinal, Bearing, Azimuth }

    /// <summary>A direction as the crew recorded it, plus its azimuth.</summary>
    public sealed class ObservedDirection
    {
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("kind")] public DirectionKind Kind { get; set; }
        [JsonProperty("azimuth")] public double? AzimuthDegrees { get; set; }

        [JsonIgnore] public bool IsKnown { get { return AzimuthDegrees.HasValue; } }

        public static ObservedDirection Unknown(string text)
        {
            return new ObservedDirection { Text = text ?? "?", Kind = DirectionKind.Unknown };
        }
    }

    /// <summary>One pipe as observed at one structure. Every field is a field
    /// observation; nothing here is calculated.</summary>
    public sealed class PipeObservation
    {
        [JsonProperty("id")] public string Id { get; set; }

        /// <summary>Nominal width (span) in inches -- the diameter for round pipe.</summary>
        [JsonProperty("widthIn")] public double? WidthIn { get; set; }

        /// <summary>Nominal height (rise) in inches -- equal to width for round pipe.</summary>
        [JsonProperty("heightIn")] public double? HeightIn { get; set; }

        [JsonProperty("shape")] public PipeShape Shape { get; set; }
        [JsonProperty("material")] public string Material { get; set; }
        [JsonProperty("direction")] public ObservedDirection Direction { get; set; }

        /// <summary>The dip exactly as read, in feet below the rim. Null when the
        /// crew could not dip it.</summary>
        [JsonProperty("dip")] public double? MeasuredDip { get; set; }

        [JsonProperty("reference")] public MeasurementReference Reference { get; set; }

        /// <summary>How the reference was established. An assumption is never field evidence.</summary>
        [JsonProperty("referenceBasis")] public ReferenceBasis ReferenceBasis { get; set; }

        /// <summary>For a convention classification: which documented convention.</summary>
        [JsonProperty("referenceNote")] public string ReferenceNote { get; set; }

        /// <summary>Read only from drawings saved by the first release, where an unmarked
        /// dip was stored as Invert with this flag. Migrated to Unspecified on load.</summary>
        [JsonProperty("referenceDefaulted")] public bool LegacyReferenceDefaulted { get; set; }
        public bool ShouldSerializeLegacyReferenceDefaulted() { return false; }

        /// <summary>True when the reference still has to be confirmed.</summary>
        [JsonIgnore] public bool ReferenceUnconfirmed { get { return Reference == MeasurementReference.Unspecified; } }

        [JsonProperty("role")] public FlowRole Role { get; set; }
        [JsonProperty("conditions")] public List<string> Conditions { get; set; }
        [JsonProperty("notes")] public string Notes { get; set; }
        [JsonProperty("source")] public ObservationSource Source { get; set; }

        /// <summary>The field-note line this came from, verbatim.</summary>
        [JsonProperty("raw")] public string RawText { get; set; }

        public PipeObservation()
        {
            Id = Guid.NewGuid().ToString("N");
            Conditions = new List<string>();
            Direction = new ObservedDirection { Kind = DirectionKind.Unknown };
        }

        public bool HasCondition(string condition)
        {
            foreach (var c in Conditions)
                if (string.Equals(c, condition, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }

    /// <summary>Everything the field notes say about one structure.</summary>
    public sealed class StructureObservation
    {
        [JsonProperty("pointNumber")] public string PointNumber { get; set; }

        /// <summary>The code written on the note header ("SDMH"), if any.</summary>
        [JsonProperty("fieldCode")] public string FieldCode { get; set; }

        [JsonProperty("bottomDip")] public double? BottomDip { get; set; }
        [JsonProperty("waterDip")] public double? WaterDip { get; set; }
        [JsonProperty("conditions")] public List<string> Conditions { get; set; }
        [JsonProperty("notes")] public List<string> Notes { get; set; }
        [JsonProperty("pipes")] public List<PipeObservation> Pipes { get; set; }
        [JsonProperty("raw")] public string RawText { get; set; }

        /// <summary>Note lines that could not be read ("BOT" with no number). Kept with
        /// the observation so they reach QC and the field revisit list.</summary>
        [JsonProperty("noteProblems")] public List<string> NoteProblems { get; set; }

        /// <summary>Inside width of the structure in inches, when the note gave one ("ID 48").</summary>
        [JsonProperty("insideWidthIn")] public double? InsideWidthIn { get; set; }

        public StructureObservation()
        {
            NoteProblems = new List<string>();
            Conditions = new List<string>();
            Notes = new List<string>();
            Pipes = new List<PipeObservation>();
        }
    }

    /// <summary>The surveyed CAD point, snapshotted when the structure was last
    /// calculated. A difference from the live point makes dependants stale.</summary>
    public sealed class CadStructureSnapshot
    {
        [JsonProperty("pointNumber")] public string PointNumber { get; set; }
        [JsonProperty("northing")] public double Northing { get; set; }
        [JsonProperty("easting")] public double Easting { get; set; }
        [JsonProperty("rim")] public double Rim { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("handle")] public string Handle { get; set; }
    }

    /// <summary>One structure: its field observations, its surveyed point, and how
    /// it is classified.</summary>
    public sealed class StructureRecord
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("field")] public StructureObservation Field { get; set; }
        [JsonProperty("cad")] public CadStructureSnapshot Cad { get; set; }
        [JsonProperty("type")] public string StructureType { get; set; }
        [JsonProperty("system")] public UtilitySystem System { get; set; }

        /// <summary>Inside width typed by the drafter, inches. Separate from the field value.</summary>
        [JsonProperty("enteredInsideWidthIn")] public double? EnteredInsideWidthIn { get; set; }

        /// <summary>Inside length typed by the drafter for a rectangular structure, inches.</summary>
        [JsonProperty("enteredInsideLengthIn")] public double? EnteredInsideLengthIn { get; set; }

        public StructureRecord()
        {
            Id = Guid.NewGuid().ToString("N");
            Field = new StructureObservation();
        }

        [JsonIgnore]
        public string Label
        {
            get
            {
                var code = !string.IsNullOrEmpty(Field.FieldCode) ? Field.FieldCode : StructureType;
                return (code ?? "STRUCTURE") + " " + (Field.PointNumber ?? "?");
            }
        }
    }

    /// <summary>A calculated elevation and exactly how it was obtained.</summary>
    public sealed class CalculatedElevation
    {
        [JsonProperty("value")] public double Value { get; set; }
        [JsonProperty("rim")] public double RimUsed { get; set; }
        [JsonProperty("dip")] public double DipUsed { get; set; }
        [JsonProperty("reference")] public MeasurementReference Reference { get; set; }
        [JsonProperty("formula")] public string Formula { get; set; }
    }

    /// <summary>A link between an observed pipe and the structure at its far end.
    /// Always anchored to a field observation at the near end.</summary>
    public sealed class PipeConnection
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("fromStructure")] public string FromStructureId { get; set; }
        [JsonProperty("fromPipe")] public string FromPipeId { get; set; }
        [JsonProperty("toStructure")] public string ToStructureId { get; set; }

        /// <summary>The corresponding observed pipe at the far structure, or null
        /// when the far end was not dipped.</summary>
        [JsonProperty("toPipe")] public string ToPipeId { get; set; }

        [JsonProperty("status")] public ConnectionStatus Status { get; set; }
        [JsonProperty("confidence")] public Confidence Confidence { get; set; }
        [JsonProperty("basis")] public List<string> Basis { get; set; }
        [JsonProperty("overrideNote")] public string OverrideNote { get; set; }
        [JsonProperty("drafted")] public bool Drafted { get; set; }

        public PipeConnection()
        {
            Id = Guid.NewGuid().ToString("N");
            Basis = new List<string>();
        }

        [JsonIgnore]
        public bool IsAccepted
        {
            get
            {
                return Status == ConnectionStatus.Confirmed ||
                       Status == ConnectionStatus.ManualOverride;
            }
        }
    }

    /// <summary>A note for the field crew.</summary>
    public sealed class RevisitItem
    {
        [JsonProperty("structure")] public string StructureLabel { get; set; }
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("manual")] public bool Manual { get; set; }
    }

    /// <summary>A drafter's change to a calculated or generated value, kept so it
    /// is visible later.</summary>
    public sealed class ManualOverride
    {
        [JsonProperty("target")] public string Target { get; set; }
        [JsonProperty("what")] public string What { get; set; }
        [JsonProperty("generated")] public string Generated { get; set; }
        [JsonProperty("entered")] public string Entered { get; set; }
        [JsonProperty("utc")] public DateTime Utc { get; set; }
    }

    /// <summary>All storm/sewer dip data for one drawing.</summary>
    public sealed class UtilityProject
    {
        public const string CurrentSchema = "ftf-dips-2";

        [JsonProperty("schema")] public string Schema { get; set; }
        [JsonProperty("structures")] public List<StructureRecord> Structures { get; set; }
        [JsonProperty("connections")] public List<PipeConnection> Connections { get; set; }
        [JsonProperty("revisit")] public List<RevisitItem> ManualRevisit { get; set; }
        [JsonProperty("overrides")] public List<ManualOverride> Overrides { get; set; }

        public UtilityProject()
        {
            Schema = CurrentSchema;
            Structures = new List<StructureRecord>();
            Connections = new List<PipeConnection>();
            ManualRevisit = new List<RevisitItem>();
            Overrides = new List<ManualOverride>();
        }

        public StructureRecord Structure(string id)
        {
            foreach (var s in Structures) if (s.Id == id) return s;
            return null;
        }

        public StructureRecord StructureByPoint(string pointNumber)
        {
            foreach (var s in Structures)
                if (string.Equals(s.Field.PointNumber, pointNumber, StringComparison.OrdinalIgnoreCase))
                    return s;
            return null;
        }

        public PipeObservation Pipe(string structureId, string pipeId)
        {
            var s = Structure(structureId);
            if (s == null) return null;
            foreach (var p in s.Field.Pipes) if (p.Id == pipeId) return p;
            return null;
        }

        public PipeConnection ConnectionFor(string structureId, string pipeId)
        {
            foreach (var c in Connections)
            {
                if (c.FromStructureId == structureId && c.FromPipeId == pipeId) return c;
                if (c.ToStructureId == structureId && c.ToPipeId == pipeId) return c;
            }
            return null;
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None);
        }

        public static UtilityProject FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new UtilityProject();
            var project = JsonConvert.DeserializeObject<UtilityProject>(json) ?? new UtilityProject();

            // First-release data stored an unmarked dip as Invert plus a flag. That was an
            // assumption, so it comes back as Unspecified; everything else was stated.
            if (project.Schema != CurrentSchema)
            {
                foreach (var s in project.Structures)
                foreach (var p in s.Field.Pipes)
                {
                    if (p.LegacyReferenceDefaulted)
                    {
                        p.Reference = MeasurementReference.Unspecified;
                        p.ReferenceBasis = ReferenceBasis.NotStated;
                    }
                    else if (p.ReferenceBasis == ReferenceBasis.NotStated)
                    {
                        p.ReferenceBasis = p.Source == ObservationSource.FieldNote
                            ? ReferenceBasis.StatedInFieldNote : ReferenceBasis.EnteredByDrafter;
                    }
                    p.LegacyReferenceDefaulted = false;
                }
                project.Schema = CurrentSchema;
            }
            return project;
        }
    }

    /// <summary>Splits long JSON into Xrecord-sized strings and back.</summary>
    public static class TextChunks
    {
        public const int Size = 240;

        public static IList<string> Split(string text)
        {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(text)) return chunks;
            for (var i = 0; i < text.Length; i += Size)
                chunks.Add(text.Substring(i, Math.Min(Size, text.Length - i)));
            return chunks;
        }

        public static string Join(IEnumerable<string> chunks)
        {
            return string.Concat(chunks ?? new string[0]);
        }
    }
}
