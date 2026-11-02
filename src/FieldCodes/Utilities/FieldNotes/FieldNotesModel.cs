using System;
using System.Collections.Generic;
using System.Globalization;
using FieldCodes.Settings;
using Newtonsoft.Json;

namespace FieldCodes.Utilities.FieldNotes
{
    // =====================================================================
    // What goes into a dip field-notes record, before anything is laid out.
    //
    // Everything here is copied or calculated from the Dip Builder's own data
    // through the library's own functions (DipElevations, SlopeCalculator,
    // UtilityQc...). Nothing is made up: a value nobody recorded is null here
    // and prints as "not recorded" -- it is never filled with a default.
    // =====================================================================

    /// <summary>
    /// The facts at the top of every page that the Dip Builder does not hold: the project, the day the dips were
    /// taken and who took them. The drafter types them from the field book when saving; they are kept in the
    /// drawing so the next save starts from them. Anything left blank prints as "not recorded".
    /// </summary>
    public sealed class FieldNotesHeader
    {
        [JsonProperty("projectName")] public string ProjectName { get; set; }
        [JsonProperty("projectNumber")] public string ProjectNumber { get; set; }

        /// <summary>The date of observation as yyyy-MM-dd, or empty when not recorded.</summary>
        [JsonProperty("observationDate")] public string ObservationDateText { get; set; }

        /// <summary>Crew or observer, as written in the field book ("J. SMITH / R. LEE").</summary>
        [JsonProperty("crew")] public string Crew { get; set; }

        /// <summary>The date of observation, or null when it was not recorded.</summary>
        [JsonIgnore]
        public DateTime? ObservationDate
        {
            get
            {
                DateTime d;
                if (!string.IsNullOrWhiteSpace(ObservationDateText) &&
                    DateTime.TryParseExact(ObservationDateText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out d))
                    return d.Date;
                return null;
            }
            set
            {
                ObservationDateText = value.HasValue
                    ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null;
            }
        }

        public FieldNotesHeader Copy() { return (FieldNotesHeader)MemberwiseClone(); }

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.None); }

        /// <summary>Reads a stored header; anything unreadable gives an empty one rather than an error.</summary>
        public static FieldNotesHeader FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new FieldNotesHeader();
            try { return JsonConvert.DeserializeObject<FieldNotesHeader>(json) ?? new FieldNotesHeader(); }
            catch (JsonException) { return new FieldNotesHeader(); }
        }

        /// <summary>The project as it identifies a file: the number, else the name, else null.</summary>
        [JsonIgnore]
        public string ProjectIdentifier
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ProjectNumber)) return ProjectNumber.Trim();
                if (!string.IsNullOrWhiteSpace(ProjectName)) return ProjectName.Trim();
                return null;
            }
        }
    }

    /// <summary>Whether one structure's dip observation is complete enough to issue, and if not, what is missing.</summary>
    public sealed class ObservationCompleteness
    {
        public bool IsComplete { get { return Missing.Count == 0; } }

        /// <summary>Plain-language reasons the observation is not complete. Empty when it is.</summary>
        public IList<string> Missing { get; private set; }

        public ObservationCompleteness() { Missing = new List<string>(); }
    }

    /// <summary>
    /// The Dip Builder has no "complete" flag on a structure, so completeness is read from the observation itself,
    /// and only ever reported -- nothing here marks or changes an observation. An observation is complete when:
    /// the structure has its surveyed point (the rim every elevation is calculated from), and that point has not
    /// moved since the elevations were calculated; at least one pipe is recorded; every pipe has a measure down,
    /// or a recorded reason it could not be dipped (UNABLE TO DIP, BLOCKED, SUBMERGED...); every measure down says
    /// what it was taken to (or the drafter has confirmed it); and no line of the typed note was unreadable.
    /// A missing size, material or direction does not make it incomplete: those print as "not recorded".
    /// </summary>
    public static class DipObservationStatus
    {
        public static ObservationCompleteness Check(UtilityProject project, StructureRecord structure,
                                                    ICollection<string> staleStructureIds)
        {
            var result = new ObservationCompleteness();
            if (structure == null) { result.Missing.Add("structure not found"); return result; }

            if (structure.Cad == null)
                result.Missing.Add("no surveyed point (rim) in the drawing, so no elevation can be calculated");
            else if (staleStructureIds != null && staleStructureIds.Contains(structure.Id))
                result.Missing.Add("the surveyed point has changed since the elevations were calculated -- " +
                                   "rebuild it (Review tab) first");

            foreach (var problem in structure.Field.NoteProblems ?? new List<string>())
                result.Missing.Add("unreadable field note line (" + problem + ")");

            if (structure.Field.Pipes.Count == 0)
                result.Missing.Add("no pipes recorded");

            for (var i = 0; i < structure.Field.Pipes.Count; i++)
            {
                var pipe = structure.Field.Pipes[i];
                var name = FieldNotesReportBuilder.PipeKey(i) + " " + ConnectionFinder.Describe(pipe);
                if (!pipe.MeasuredDip.HasValue && (pipe.Conditions == null || pipe.Conditions.Count == 0))
                    result.Missing.Add(name + ": no measure down (MD) and no reason it was not dipped");
                if (pipe.MeasuredDip.HasValue && pipe.ReferenceUnconfirmed)
                    result.Missing.Add(name + ": the dip does not say what it was measured to -- confirm the reference");
            }
            return result;
        }
    }

    /// <summary>Where a pipe direction drawn on the sketch came from.</summary>
    public enum SketchDirectionBasis
    {
        /// <summary>Recorded at this structure by the crew (cardinal, bearing or azimuth).</summary>
        Observed,
        /// <summary>Copied from the connected pipe's other end when this end was entered; not observed here.</summary>
        CarriedOver,
        /// <summary>Not recorded; taken from the surveyed line to the structure the pipe was connected to.</summary>
        FromConnection
    }

    /// <summary>What a pipe's calculated elevation is of.</summary>
    public enum ElevationKind { None, Invert, TopOfPipe, Springline, Other }

    /// <summary>One pipe line of the record.</summary>
    public sealed class PipeNotes
    {
        /// <summary>P1, P2... in the order the pipes were entered.</summary>
        public string Key { get; set; }
        public string PipeId { get; set; }
        public FlowRole Role { get; set; }

        /// <summary>8", 24"x36", or null when not recorded.</summary>
        public string Size { get; set; }
        public string Shape { get; set; }
        public string Material { get; set; }
        /// <summary>The direction exactly as recorded (NE, N45E, AZ215), or null.</summary>
        public string Direction { get; set; }

        /// <summary>Values carried over from the connected pipe ("size", "material", "direction"), not observed here.</summary>
        public IList<string> CarriedOver { get; private set; }
        public string CarriedOverFrom { get; set; }

        /// <summary>The measure down exactly as recorded, or null when the pipe was not dipped.</summary>
        public double? MeasuredDip { get; set; }
        public string MeasuredDipText { get; set; }

        public MeasurementReference Reference { get; set; }
        public string ReferenceWords { get; set; }
        public string ReferenceBasisWords { get; set; }

        /// <summary>The library's own calculation (DipElevations.Pipe), or null.</summary>
        public CalculatedElevation Elevation { get; set; }
        public ElevationKind ElevationKind { get; set; }

        /// <summary>INV, TOP, SPR or EL.</summary>
        public string ElevationLabel { get; set; }
        public string ElevationText { get; set; }

        /// <summary>Why no invert is stated, when the dip was not taken to the invert.</summary>
        public string InvertNote { get; set; }

        public string ConnectsTo { get; set; }
        /// <summary>The other structure's label, for the sketch.</summary>
        public string ConnectsToShort { get; set; }
        public string Slope { get; set; }

        public IList<string> Conditions { get; private set; }
        public string Notes { get; set; }
        public string RawText { get; set; }

        public PipeNotes()
        {
            CarriedOver = new List<string>();
            Conditions = new List<string>();
        }
    }

    /// <summary>One pipe on the plan sketch.</summary>
    public sealed class SketchPipe
    {
        public string Key { get; set; }
        public double AzimuthDegrees { get; set; }
        public SketchDirectionBasis Basis { get; set; }
        public FlowRole Role { get; set; }
        public string Caption { get; set; }
        public string ElevationLabel { get; set; }
        public string ElevationText { get; set; }
        public string ConnectsTo { get; set; }

        /// <summary>True when the direction drawn was not observed at this structure.</summary>
        public bool Inferred { get { return Basis != SketchDirectionBasis.Observed; } }
    }

    /// <summary>The plan sketch of one structure: its outline and its pipes, north up, not to scale.</summary>
    public sealed class StructureSketch
    {
        public StructureShape Shape { get; set; }
        public string Code { get; set; }
        public IList<SketchPipe> Pipes { get; private set; }

        /// <summary>Pipes with no direction at all: listed, never drawn in a made-up direction.</summary>
        public IList<string> NotDrawn { get; private set; }

        /// <summary>A north arrow is drawn whenever any pipe is drawn in a direction.</summary>
        public bool HasNorthArrow { get { return Pipes.Count > 0; } }

        public StructureSketch()
        {
            Pipes = new List<SketchPipe>();
            NotDrawn = new List<string>();
        }
    }

    /// <summary>A warning or qualification printed with a structure.</summary>
    public sealed class FieldNotesWarning
    {
        public Severity Severity { get; set; }
        public string Text { get; set; }
    }

    /// <summary>One structure's dip observation, ready to lay out.</summary>
    public sealed class StructureNotes
    {
        public string StructureId { get; set; }
        public string Label { get; set; }
        public string PointNumber { get; set; }
        public string Code { get; set; }
        public string FieldCode { get; set; }
        public bool TypeSetByDrafter { get; set; }
        public string TypeName { get; set; }
        public UtilitySystem System { get; set; }
        public StructureShape Shape { get; set; }
        public string Description { get; set; }

        public double? Rim { get; set; }
        public string RimText { get; set; }
        public double? Northing { get; set; }
        public double? Easting { get; set; }

        public string InsideSize { get; set; }
        public string InsideSizeSource { get; set; }

        public string BottomDipText { get; set; }
        public CalculatedElevation Bottom { get; set; }
        public string BottomText { get; set; }
        public string WaterDipText { get; set; }
        public CalculatedElevation Water { get; set; }
        public string WaterText { get; set; }

        public IList<string> Conditions { get; private set; }
        public IList<string> Notes { get; private set; }
        public IList<string> RawLines { get; private set; }
        public IList<PipeNotes> Pipes { get; private set; }
        public IList<FieldNotesWarning> Warnings { get; private set; }
        public StructureSketch Sketch { get; set; }

        public StructureNotes()
        {
            Conditions = new List<string>();
            Notes = new List<string>();
            RawLines = new List<string>();
            Pipes = new List<PipeNotes>();
            Warnings = new List<FieldNotesWarning>();
        }
    }

    /// <summary>A structure left out of the record, and why.</summary>
    public sealed class ExcludedObservation
    {
        public string StructureId { get; set; }
        public string Label { get; set; }
        public IList<string> Reasons { get; private set; }

        public ExcludedObservation() { Reasons = new List<string>(); }
    }

    /// <summary>Everything one field-notes PDF says, before it is laid out on pages.</summary>
    public sealed class FieldNotesReport
    {
        public FieldNotesHeader Header { get; set; }
        public string DrawingName { get; set; }
        public int ElevationDecimals { get; set; }
        public IList<StructureNotes> Structures { get; private set; }
        public IList<ExcludedObservation> Incomplete { get; private set; }

        /// <summary>The office convention text, when any included pipe's reference came from it.</summary>
        public string ConventionNote { get; set; }

        public FieldNotesReport()
        {
            Header = new FieldNotesHeader();
            Structures = new List<StructureNotes>();
            Incomplete = new List<ExcludedObservation>();
            ElevationDecimals = 2;
        }
    }
}
