using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FieldCodes.Drafting;
using FieldCodes.Settings;
using Newtonsoft.Json;

namespace FieldCodes.Easements
{
    /// <summary>
    /// What a legal description needs that the drawing cannot say: the parcel, and the
    /// names of the corners and lines the description is tied to. Kept with the easement
    /// so the draft can be written again after a rebuild.
    /// </summary>
    public sealed class LegalInputs
    {
        [JsonProperty("exhibit")] public string ExhibitLabel { get; set; }

        /// <summary>Follows "THAT PORTION OF": "PARCEL 3, SNOHOMISH COUNTY SHORT PLAT NO. ...".</summary>
        [JsonProperty("parcel")] public string ParcelDescription { get; set; }

        /// <summary>"THE SOUTHEAST CORNER OF SAID PARCEL 2".</summary>
        [JsonProperty("commencement")] public string CommencementCorner { get; set; }

        /// <summary>The line the commencement tie runs along, when it runs along one.</summary>
        [JsonProperty("tieLine")] public string TieLine { get; set; }

        /// <summary>Without a Point of Commencement: how the Point of Beginning is described.</summary>
        [JsonProperty("beginning")] public string BeginningDescription { get; set; }

        /// <summary>"THE SOUTH LINE OF SAID PARCEL 2" -- the line the Point of Beginning is on.</summary>
        [JsonProperty("beginningLine")] public string BeginningLine { get; set; }

        /// <summary>"THE WEST LINE OF SAID PARCEL 2" -- the line the terminus is on.</summary>
        [JsonProperty("terminusLine")] public string TerminusLine { get; set; }

        /// <summary>"THE NORTHWEST CORNER OF SAID PARCEL 4".</summary>
        [JsonProperty("terminusCorner")] public string TerminusCorner { get; set; }

        /// <summary>For a clicked area: the name of the line a side follows, by side number (1 = first side).</summary>
        [JsonProperty("sideLines")] public Dictionary<string, string> SideLines { get; set; }

        /// <summary>Names for components B, C...: their parcel, commencement corner, beginning and followed lines.</summary>
        [JsonProperty("components")] public Dictionary<string, LegalInputs> ComponentInputs { get; set; }

        [JsonProperty("county")] public string County { get; set; }
        [JsonProperty("state")] public string State { get; set; }

        public LegalInputs()
        {
            ExhibitLabel = "EXHIBIT A";
            State = "WASHINGTON";
        }

        public LegalInputs Copy()
        {
            var copy = (LegalInputs)MemberwiseClone();
            copy.SideLines = SideLines != null ? new Dictionary<string, string>(SideLines) : null;
            copy.ComponentInputs = ComponentInputs != null ? ComponentInputs.ToDictionary(kv => kv.Key, kv => kv.Value.Copy()) : null;
            return copy;
        }
    }

    /// <summary>A written draft and what the surveyor should look at before using it.</summary>
    public sealed class LegalDraft
    {
        public string Text { get; set; }
        public List<string> Checks { get; private set; }

        /// <summary>False when the stated courses do not reproduce the CAD easement: the draft is not ready for review.</summary>
        public bool ReadyForReview { get; set; }

        public LegalDraft() { Checks = new List<string>(); ReadyForReview = true; }
    }

    /// <summary>
    /// Writes a DRAFT strip easement legal description from a built easement, in the
    /// office's pattern: parcel preamble, width about the centerline (with any temporary
    /// construction easement), commencement tie, courses, terminus and tie, the sideline
    /// clause, areas and situs. Every course comes from the stored geometry. Anything the
    /// drawing cannot know is left as a [BRACKETED] blank and listed as a check. The
    /// result is a draft for a licensed surveyor to review, never a finished description.
    /// </summary>
    public static class LegalDescriptionWriter
    {
        public const string DraftBanner = "DRAFT - FOR SURVEYOR REVIEW - NOT FOR RECORDING";
        private const string Deg = "°";

        public static LegalDraft Write(EasementRecord easement, EasementRecord temporary, LegalInputs inputs,
                                       EasementSettings settings, IEnumerable<string> surveyChanges)
        {
            if (easement == null) throw new ArgumentNullException("easement");
            inputs = inputs ?? new LegalInputs();
            var draft = new LegalDraft();
            var checks = draft.Checks;
            var changes = (surveyChanges ?? new string[0]).ToList();
            if (changes.Count > 0)
                checks.Add("The survey has changed since this easement was built (" + string.Join("; ", changes.ToArray()) +
                           "). Run FTFEASEMENTCHECK and rebuild before using this description.");

            var purpose = Upper(easement.Purpose, settings.DefaultPurpose);
            var temporaryPurpose = temporary != null ? Upper(temporary.Purpose, settings.TemporaryPurpose) : null;
            var centered = IsCentered(easement.Width);
            var lineWord = centered ? "CENTERLINE" : "LINE";
            var courses = easement.RouteCourses ?? new List<CourseData>();
            if (courses.Count == 0) checks.Add("The easement has no centerline courses stored.");

            var sb = new StringBuilder();
            sb.AppendLine(DraftBanner);
            sb.AppendLine();
            sb.AppendLine(Blank(inputs.ExhibitLabel, "[EXHIBIT]", null, null));
            sb.AppendLine(purpose + " EASEMENT" + (temporary != null ? " AND " + temporaryPurpose + " EASEMENT" : string.Empty));
            sb.AppendLine("LEGAL DESCRIPTION");
            sb.AppendLine();

            sb.AppendLine("THAT PORTION OF " + Blank(inputs.ParcelDescription, "[PARCEL DESCRIPTION]", "the parcel description", checks) +
                          ", DESCRIBED AS FOLLOWS:");
            sb.AppendLine();

            // Width about the line, and the temporary construction easement with it.
            var width = new StringBuilder();
            width.Append("A ").Append(Feet(easement.Width.Total, settings)).Append(" FOOT WIDE ")
                 .Append(temporary != null ? "PERMANENT " : string.Empty).Append(purpose).Append(" EASEMENT, ")
                 .Append(Lying(easement.Width, courses, settings, "THE FOLLOWING DESCRIBED " + lineWord, checks));
            if (temporary != null)
            {
                width.Append(", TOGETHER WITH A ").Append(Feet(temporary.Width.Total, settings)).Append(" FOOT WIDE ")
                     .Append(temporaryPurpose).Append(" EASEMENT, ")
                     .Append(Lying(temporary.Width, courses, settings, "SAID " + lineWord, checks))
                     .Append(", SAID ").Append(temporaryPurpose).Append(" EASEMENT TO TERMINATE UPON COMPLETION OF CONSTRUCTION");
            }
            sb.AppendLine(width.Append(':').ToString());
            sb.AppendLine();

            // Commencement and beginning.
            var beginningLine = easement.BeginsOn != null
                ? Blank(inputs.BeginningLine, "[LINE THE POINT OF BEGINNING IS ON]", "the name of the line the Point of Beginning is on", checks)
                : null;
            var tie = easement.TieCourses();
            if (easement.PointOfCommencement != null && tie.Count > 0)
            {
                sb.AppendLine("COMMENCING AT " + Blank(inputs.CommencementCorner, "[COMMENCEMENT CORNER]", "the name of the commencement corner", checks) + ";");
                var along = easement.CommencementAlong != null || Ties.Follows(tie)
                    ? Blank(string.IsNullOrWhiteSpace(inputs.TieLine) && SameLine(easement.CommencementAlong, easement.BeginsOn) ? inputs.BeginningLine : inputs.TieLine,
                            "[LINE THE TIE RUNS ALONG]", "the name of the line the commencement tie runs along", checks)
                    : null;
                TieBody(sb, tie, along, " TO THE POINT OF BEGINNING" + (beginningLine != null && along == null ? ", ON " + beginningLine : string.Empty), settings);
                if (Ties.Follows(tie))
                    AddOnce(checks, "The commencement tie follows the line as drawn (" + tie.Count + " courses" + (tie.Any(t => t.Course.Kind == CourseKind.Arc) ? ", with a curve" : string.Empty) + "); check its wording.");
            }
            else
            {
                sb.AppendLine("BEGINNING AT " + Blank(inputs.BeginningDescription,
                    beginningLine != null ? "[DESCRIBE THE POINT OF BEGINNING ON " + beginningLine + "]" : "[DESCRIBE THE POINT OF BEGINNING]",
                    "a description of the Point of Beginning (there is no Point of Commencement)", checks) + ";");
            }

            // Courses.
            for (var i = 0; i < courses.Count; i++)
            {
                var d = courses[i];
                var text = new StringBuilder("THENCE ");
                if (i == 0 && beginningLine != null) text.Append("DEPARTING SAID ").Append(ShortName(beginningLine)).Append(", ");
                var previous = i > 0 ? courses[i - 1].Course : null;
                text.Append(d.Course.Kind == CourseKind.Line ? Course(d, settings) : Curve(d, previous, settings));

                if (i < courses.Count - 1) { sb.AppendLine(text.Append(';').ToString()); continue; }

                if (easement.EndsOn != null)
                    text.Append(" TO ").Append(Blank(inputs.TerminusLine, "[TERMINUS LINE]", "the name of the line the terminus is on", checks)).Append(" AND");
                text.Append(" TO THE TERMINUS OF THIS ").Append(lineWord).Append(" DESCRIPTION");
                if (easement.TerminusTie != null)
                    text.Append(", TO WHICH ").Append(Blank(inputs.TerminusCorner, "[TERMINUS TIE CORNER]", "the name of the corner the terminus is tied to", checks))
                        .Append(" BEARS ").Append(Course(easement.TerminusTie, settings));
                sb.AppendLine(text.Append('.').ToString().Replace(" AND TO THE TERMINUS", " AND THE TERMINUS"));
            }
            sb.AppendLine();

            if (courses.Count > 1)
            {
                sb.AppendLine("ALL SIDELINES OF THIS EASEMENT SHALL BE SHORTENED OR LENGTHENED TO TERMINATE AT ALL ANGLE POINTS.");
                sb.AppendLine();
            }
            if (courses.Count(c => c.Course.Kind == CourseKind.Arc) > 0)
                checks.Add("The description has curves; check the curve wording and whether each is tangent.");

            Compose(sb, easement, inputs, settings, checks);
            if (temporary != null)
            {
                Compose(sb, temporary, inputs, settings, checks);
                sb.AppendLine(EasementAnnotation.LegalAreaLine(temporary.AreaSquareFeet, settings, temporaryPurpose));
            }
            sb.AppendLine(Overlapping(easement, EasementAnnotation.LegalAreaLine(easement.AreaSquareFeet, settings, temporary != null ? "PERMANENT " + purpose : purpose), settings));
            ComponentAreaCheck(easement, checks);
            sb.AppendLine();
            sb.AppendLine("SITUATE IN THE COUNTY OF " + Blank(inputs.County, "[COUNTY]", "the county", checks) +
                          ", STATE OF " + Blank(inputs.State, "[STATE]", "the state", checks) + ".");

            if (temporary != null && IsCentered(temporary.Width) != centered)
                checks.Add("The temporary construction easement is not laid out about the line the same way as the easement; check the width wording.");

            draft.Text = sb.ToString();
            return draft;
        }

        /// <summary>
        /// A portion easement: "THE WEST 10.00 FEET OF THE SOUTH 50.00 FEET OF LOT 2, ...,
        /// AS MEASURED AT RIGHT ANGLES TO THE WEST AND SOUTH LINES THEREOF."
        /// </summary>
        public static LegalDraft WritePortion(EasementRecord easement, LegalInputs inputs, EasementSettings settings, IEnumerable<string> surveyChanges)
        {
            inputs = inputs ?? new LegalInputs();
            var draft = new LegalDraft();
            var checks = draft.Checks;
            Stale(checks, surveyChanges);
            var purpose = Upper(easement.Purpose, settings.DefaultPurpose);
            var steps = easement.PortionSteps ?? new List<PortionStep>();

            var sb = Header(inputs, purpose + " EASEMENT");
            sb.AppendLine(PortionBuilder.Describe(steps, settings.DistanceDecimals) + " " +
                          Blank(inputs.ParcelDescription, "[PARCEL DESCRIPTION]", "the parcel description", checks) + ", " +
                          PortionBuilder.MeasuredClause(steps) + ".");
            sb.AppendLine();
            Compose(sb, easement, inputs, settings, checks);
            sb.AppendLine(Overlapping(easement, EasementAnnotation.LegalAreaLine(easement.AreaSquareFeet, settings, purpose), settings));
            ComponentAreaCheck(easement, checks);
            sb.AppendLine();
            Situs(sb, inputs, checks);
            draft.Text = sb.ToString();
            return draft;
        }

        /// <summary>
        /// A clicked metes and bounds area: "THAT PORTION OF ..., DESCRIBED AS FOLLOWS:
        /// COMMENCING ...; THENCE ... TO THE POINT OF BEGINNING; THENCE ...; THENCE ... TO THE
        /// POINT OF BEGINNING." Sides that follow a line read "ALONG" that line.
        /// </summary>
        public static LegalDraft WriteArea(EasementRecord area, LegalInputs inputs, EasementSettings settings, IEnumerable<string> surveyChanges)
        {
            inputs = inputs ?? new LegalInputs();
            var draft = new LegalDraft();
            var checks = draft.Checks;
            Stale(checks, surveyChanges);
            var purpose = Upper(area.Purpose, settings.AreaPurpose);
            var courses = area.RouteCourses ?? new List<CourseData>();
            var sides = area.AreaSides ?? new List<AreaSide>();

            var sb = Header(inputs, EasementAnnotation.AreaTitle(purpose, settings));
            AreaBody(sb, inputs, area.PointOfCommencement, area.TieCourses(), courses, sides, area.AnglePoints, settings, checks, string.Empty);
            if (courses.Any(c => c.Course.Kind == CourseKind.Arc))
                checks.Add("The description has curves; check the curve wording and whether each is tangent.");
            sb.AppendLine();
            Compose(sb, area, inputs, settings, checks);
            sb.AppendLine(Overlapping(area, EasementAnnotation.AreaLegalLine(area.AreaSquareFeet, settings, purpose), settings));
            ComponentAreaCheck(area, checks);
            sb.AppendLine();
            Situs(sb, inputs, checks);
            draft.Text = sb.ToString();
            return draft;
        }

        /// <summary>"THAT PORTION OF ..., DESCRIBED AS FOLLOWS:" and the courses of a clicked area, back to its beginning.</summary>
        private static void AreaBody(StringBuilder sb, LegalInputs inputs, SelectedLocation poc, IList<CourseData> tie, IList<CourseData> courses,
                                     IList<AreaSide> sides, IList<SelectedLocation> corners, EasementSettings settings, List<string> checks, string who)
        {
            sb.AppendLine("THAT PORTION OF " + Blank(inputs.ParcelDescription, "[PARCEL DESCRIPTION" + who + "]", "the parcel description" + who.ToLowerInvariant(), checks) +
                          ", DESCRIBED AS FOLLOWS:");
            sb.AppendLine();
            if (poc != null && tie != null && tie.Count > 0)
            {
                sb.AppendLine("COMMENCING AT " + Blank(inputs.CommencementCorner, "[COMMENCEMENT CORNER" + who + "]", "the name of the commencement corner" + who.ToLowerInvariant(), checks) + ";");
                var along = Ties.Follows(tie)
                    ? Blank(inputs.TieLine, "[LINE THE TIE RUNS ALONG" + who + "]", "the name of the line the commencement tie" + who.ToLowerInvariant() + " runs along", checks)
                    : null;
                TieBody(sb, tie, along, " TO THE POINT OF BEGINNING", settings);
                if (along != null)
                    AddOnce(checks, "The commencement tie" + who.ToLowerInvariant() + " follows the line as drawn (" + tie.Count + " courses" + (tie.Any(t => t.Course.Kind == CourseKind.Arc) ? ", with a curve" : string.Empty) + "); check its wording.");
            }
            else
                sb.AppendLine("BEGINNING AT " + Blank(inputs.BeginningDescription, "[DESCRIBE THE POINT OF BEGINNING" + who + "]",
                                                      "a description of the Point of Beginning" + who.ToLowerInvariant() + " (there is no Point of Commencement)", checks) + ";");

            // Courses in click order; a followed side can be several courses along one line.
            sides = sides ?? new List<AreaSide>();
            var courseSide = SideOfEachCourse(courses, corners);
            for (var i = 0; i < courses.Count; i++)
            {
                var side = courseSide.Count > i ? courseSide[i] : -1;
                var follows = side >= 0 && side < sides.Count && !string.IsNullOrWhiteSpace(sides[side].FollowHandle);
                var firstOfSide = i == 0 || courseSide.Count <= i - 1 || courseSide[i - 1] != side;
                var text = new StringBuilder("THENCE ");
                if (follows && firstOfSide)
                {
                    string name = null;
                    if (inputs.SideLines != null) inputs.SideLines.TryGetValue((side + 1).ToString(CultureInfo.InvariantCulture), out name);
                    text.Append("ALONG ").Append(Blank(name, "[LINE SIDE " + (side + 1) + " FOLLOWS" + who + "]", "the name of the line side " + (side + 1) + who.ToLowerInvariant() + " follows", checks)).Append(", ");
                }
                var previous = i > 0 ? courses[i - 1].Course : null;
                text.Append(courses[i].Course.Kind == CourseKind.Line ? Course(courses[i], settings) : Curve(courses[i], previous, settings));
                sb.AppendLine(text.Append(i == courses.Count - 1 ? " TO THE POINT OF BEGINNING." : ";").ToString());
            }
        }

        // ======================================================== composition

        /// <summary>
        /// The easement's exceptions, then each further component with its connector and its own
        /// exceptions. Nothing is decided for the surveyor: a connector not chosen, a boundary
        /// exclusion not named, or what "THEREOF" refers to, all show as blanks or checks.
        /// </summary>
        private static void Compose(StringBuilder sb, EasementRecord record, LegalInputs inputs, EasementSettings settings, List<string> checks)
        {
            Exceptions(sb, record.Exclusions, settings, checks, string.Empty);
            foreach (var c in record.Components ?? new List<EasementComponent>())
            {
                var who = " FOR COMPONENT " + c.Label;
                LegalInputs ci = null;
                if (inputs.ComponentInputs != null) inputs.ComponentInputs.TryGetValue(c.Label, out ci);
                ci = ci ?? new LegalInputs();
                sb.AppendLine(Blank(c.Connector, "[TOGETHER WITH / AND / ALSO]", "the connector before component " + c.Label + " (TOGETHER WITH, AND or ALSO)", checks));
                sb.AppendLine();
                if (c.Kind == EasementComponent.PortionKind)
                {
                    var steps = c.PortionSteps ?? new List<PortionStep>();
                    sb.AppendLine(PortionBuilder.Describe(steps, settings.DistanceDecimals) + " " +
                                  Blank(ci.ParcelDescription, "[LOT" + who + "]", "the lot of component " + c.Label, checks) + ", " +
                                  PortionBuilder.MeasuredClause(steps) + ".");
                }
                else if (c.Kind == EasementComponent.AreaKind)
                {
                    var courses = c.Courses ?? new List<CourseData>();
                    AreaBody(sb, ci, c.PointOfCommencement, c.TieCourses(), courses, c.AreaSides, c.AnglePoints, settings, checks, who);
                    if (courses.Any(x => x.Course.Kind == CourseKind.Arc))
                        AddOnce(checks, "Component " + c.Label + " has curves; check the curve wording and whether each is tangent.");
                }
                else
                {
                    sb.AppendLine(Blank(ci.ParcelDescription, "[DESCRIBE COMPONENT " + c.Label + "]", "a description of component " + c.Label + " (it was drawn from a boundary, not courses)", checks) + ".");
                }
                sb.AppendLine();
                Exceptions(sb, c.Exclusions, settings, checks, " of component " + c.Label);
            }
        }

        private static void Exceptions(StringBuilder sb, IList<Exclusion> exclusions, EasementSettings settings, List<string> checks, string whose)
        {
            if (exclusions == null) return;
            for (var i = 0; i < exclusions.Count; i++)
            {
                var x = exclusions[i];
                var n = "exclusion " + (i + 1) + whose;
                if (x.Kind == Exclusion.PortionKind)
                {
                    var calls = PortionBuilder.Describe(x.PortionSteps ?? new List<PortionStep>(), settings.DistanceDecimals);
                    if (x.Of == null)
                    {
                        sb.AppendLine("EXCEPT " + (calls.EndsWith(" OF", StringComparison.Ordinal) ? calls.Substring(0, calls.Length - 3) : calls) + " THEREOF.");
                        AddOnce(checks, "Confirm that \"THEREOF\" in " + n + " refers to the easement as described above, and how the distances are measured.");
                    }
                    else
                    {
                        sb.AppendLine("EXCEPT THAT PORTION THEREOF LYING WITHIN " + calls + " " +
                                      Blank(x.Description, "[LOT THE EXCEPTION IS MEASURED ON]", "the lot " + n + " is measured on", checks) + ".");
                    }
                }
                else
                {
                    sb.AppendLine("EXCEPT THAT PORTION THEREOF LYING WITHIN " +
                                  Blank(x.Description, "[DESCRIBE THE EXCLUDED AREA]", "a description of the area " + n + " excludes", checks) + ".");
                }
                sb.AppendLine();
                if (x.Effect == "NONE")
                    AddOnce(checks, "Exclusion " + (i + 1) + whose + " does not touch the easement as drawn; decide whether it belongs in the description.");
                else
                    AddOnce(checks, "Exclusion " + (i + 1) + whose + " removes " + x.RemovedSquareFeet.ToString("N0", CultureInfo.InvariantCulture) +
                                    " sq ft (" + (x.Effect == "HOLE" ? "an interior hole" : "cut from the outline") + "); confirm the exception wording.");
            }
        }

        /// <summary>
        /// When components overlap, the area line does not choose between the sum of the component
        /// areas and the physical area: both are offered in brackets for the surveyor to keep one.
        /// </summary>
        private static string Overlapping(EasementRecord record, string areaLine, EasementSettings settings)
        {
            if (!record.ComponentsOverlap) return areaLine;
            var format = "N" + settings.AreaSquareFeetDecimals.ToString(CultureInfo.InvariantCulture);
            var sum = record.AreaSquareFeet.ToString(format, CultureInfo.InvariantCulture);
            var choice = "[SUM OF COMPONENT AREAS " + sum + (record.PhysicalAreaSquareFeet.HasValue
                ? " / TOTAL PHYSICAL AREA " + record.PhysicalAreaSquareFeet.Value.ToString(format, CultureInfo.InvariantCulture)
                : " / TOTAL PHYSICAL AREA NOT WORKED OUT") + "]";
            return areaLine.Contains(sum) ? areaLine.Replace(sum, choice) : areaLine + " " + choice;
        }

        private static void ComponentAreaCheck(EasementRecord record, List<string> checks)
        {
            if (record.Components == null || record.Components.Count == 0) return;
            if (record.ComponentsOverlap)
                AddOnce(checks, "Components overlap: SUM OF COMPONENT AREAS " + record.AreaSquareFeet.ToString("N0", CultureInfo.InvariantCulture) + " sq ft counts the shared ground twice; " +
                                (record.PhysicalAreaSquareFeet.HasValue ? "TOTAL PHYSICAL AREA is " + record.PhysicalAreaSquareFeet.Value.ToString("N0", CultureInfo.InvariantCulture) + " sq ft" : "the physical area was not worked out") +
                                ". Keep the one the description should state -- the components are still described separately.");
            var parts = new List<string> { "A " + (record.PrimaryAreaSquareFeet ?? 0).ToString("N0", CultureInfo.InvariantCulture) + " sq ft" };
            parts.AddRange(record.Components.Select(c => c.Label + " " + c.AreaSquareFeet.ToString("N0", CultureInfo.InvariantCulture) + " sq ft"));
            AddOnce(checks, "The area stated is the total of the components (" + string.Join(", ", parts.ToArray()) +
                            "); decide whether the description states each component's area.");
        }

        /// <summary>
        /// Marks a draft whose stated courses do not reproduce the CAD easement: a second banner line
        /// and the problems first in the checks. The text itself is not altered to force agreement.
        /// </summary>
        public static void MarkNotReady(LegalDraft draft, IEnumerable<string> problems)
        {
            var list = problems.ToList();
            if (list.Count == 0) return;
            draft.ReadyForReview = false;
            var lines = draft.Text.Replace("\r\n", "\n").Split('\n').ToList();
            lines.Insert(1, "NOT READY FOR SURVEYOR REVIEW - THE STATED COURSES DO NOT REPRODUCE THE CAD EASEMENT");
            draft.Text = string.Join(Environment.NewLine, lines.ToArray());
            draft.Checks.InsertRange(0, list.Select(p => "NOT READY: " + p));
        }

        /// <summary>For each course of a clicked area, the side (0-based) it belongs to: each
        /// side starts at its corner.</summary>
        public static List<int> SideOfEachCourse(EasementRecord area)
        {
            return SideOfEachCourse(area.RouteCourses, area.AnglePoints);
        }

        public static List<int> SideOfEachCourse(IList<CourseData> courses, IList<SelectedLocation> corners)
        {
            var result = new List<int>();
            corners = corners ?? new List<SelectedLocation>();
            var side = -1;
            foreach (var c in courses ?? new List<CourseData>())
            {
                var next = side + 1;
                if (next < corners.Count && c.Course.Start.DistanceTo(corners[next].Point) < 0.01) side = next;
                result.Add(Math.Max(0, side));
            }
            return result;
        }

        private static StringBuilder Header(LegalInputs inputs, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine(DraftBanner);
            sb.AppendLine();
            sb.AppendLine(Blank(inputs.ExhibitLabel, "[EXHIBIT]", null, null));
            sb.AppendLine(title);
            sb.AppendLine("LEGAL DESCRIPTION");
            sb.AppendLine();
            return sb;
        }

        private static void Situs(StringBuilder sb, LegalInputs inputs, List<string> checks)
        {
            sb.AppendLine("SITUATE IN THE COUNTY OF " + Blank(inputs.County, "[COUNTY]", "the county", checks) +
                          ", STATE OF " + Blank(inputs.State, "[STATE]", "the state", checks) + ".");
        }

        private static void Stale(List<string> checks, IEnumerable<string> surveyChanges)
        {
            var changes = (surveyChanges ?? new string[0]).ToList();
            if (changes.Count > 0)
                checks.Add("The survey has changed since this was built (" + string.Join("; ", changes.ToArray()) +
                           "). Run FTFEASEMENTCHECK and rebuild before using this description.");
        }

        // ============================================================ wording

        /// <summary>NORTH 01°22'31" EAST 240.29 FEET.</summary>
        /// <summary>
        /// The tie from the Point of Commencement: a THENCE for each course it runs, along the named line when it
        /// follows one, and a curve as a curve. Nothing is shortened to a chord.
        /// </summary>
        private static void TieBody(StringBuilder sb, IList<CourseData> tie, string alongName, string ending, EasementSettings settings)
        {
            for (var i = 0; i < tie.Count; i++)
            {
                var text = new StringBuilder("THENCE ");
                if (alongName != null) text.Append(i == 0 ? "ALONG " + alongName + ", " : "CONTINUING ALONG SAID " + ShortName(alongName) + ", ");
                var previous = i > 0 ? tie[i - 1].Course : null;
                text.Append(tie[i].Course.Kind == CourseKind.Line ? Course(tie[i], settings) : Curve(tie[i], previous, settings));
                text.Append(i == tie.Count - 1 ? ending : string.Empty).Append(';');
                sb.AppendLine(text.ToString());
            }
        }

        public static string Course(CourseData d, EasementSettings settings)
        {
            return Bearing(d.AzimuthDegrees, settings) + " " + Feet(d.Length, settings) + " FEET";
        }

        /// <summary>NORTH 01°22'31" EAST: the quadrant letters spelled out.</summary>
        public static string Bearing(double azimuth, EasementSettings settings)
        {
            var b = SurveyDirection.FormatBearing(azimuth, settings.BearingSecondsDecimals, Deg, false);
            var ns = b[0] == 'N' ? "NORTH " : "SOUTH ";
            var ew = b[b.Length - 1] == 'E' ? " EAST" : " WEST";
            return ns + b.Substring(1, b.Length - 2) + ew;
        }

        private static string Curve(CourseData d, Course previous, EasementSettings settings)
        {
            var c = d.Course;
            var tangent = previous != null && P2.Dot(previous.EndDirection, c.StartDirection) > Math.Cos(1.0 / 3600 * Math.PI / 180);
            var turn = c.CounterClockwise ? "LEFT" : "RIGHT";
            var sb = new StringBuilder("ALONG A ");
            if (tangent)
                sb.Append("CURVE TO THE ").Append(turn).Append(", HAVING A RADIUS OF ").Append(Feet(c.Radius, settings)).Append(" FEET");
            else
            {
                var toCenter = c.Center - c.Start;
                var az = SurveyDirection.AzimuthFromVector(toCenter.X, toCenter.Y);
                sb.Append("NON-TANGENT CURVE TO THE ").Append(turn).Append(", THE RADIUS POINT OF WHICH BEARS ")
                  .Append(Bearing(az, settings)).Append(' ').Append(Feet(c.Radius, settings)).Append(" FEET");
            }
            sb.Append(", THROUGH A CENTRAL ANGLE OF ")
              .Append(SurveyDirection.FormatAzimuth(d.DeltaDegrees ?? 0, settings.BearingSecondsDecimals, Deg))
              .Append(", AN ARC DISTANCE OF ").Append(Feet(c.Length, settings)).Append(" FEET");
            return sb.ToString();
        }

        /// <summary>LYING 7.50 FEET ON EACH SIDE OF ..., or the one- and two-sided forms.</summary>
        private static string Lying(WidthSpec width, IList<CourseData> courses, EasementSettings settings, string ofWhat, List<string> checks)
        {
            if (IsCentered(width))
                return "LYING " + Feet(width.Left, settings) + " FEET ON EACH SIDE OF " + ofWhat;

            var first = courses.Count > 0 ? courses[0].AzimuthDegrees : 0;
            var leftWord = SideWord(first - 90);
            var rightWord = SideWord(first + 90);
            AddOnce(checks, "The easement is not centered; the side words (" + leftWord + ", " + rightWord +
                            ") come from the first course -- check they hold along the whole line.");
            if (width.Right <= 1e-9)
                return "LYING " + Feet(width.Left, settings) + " FEET " + leftWord + " OF AND ADJACENT TO " + ofWhat;
            if (width.Left <= 1e-9)
                return "LYING " + Feet(width.Right, settings) + " FEET " + rightWord + " OF AND ADJACENT TO " + ofWhat;
            return "LYING " + Feet(width.Left, settings) + " FEET " + leftWord + " AND " + Feet(width.Right, settings) +
                   " FEET " + rightWord + " OF " + ofWhat;
        }

        private static string SideWord(double azimuth)
        {
            var a = ((azimuth % 360) + 360) % 360;
            if (a >= 315 || a < 45) return "NORTHERLY";
            if (a < 135) return "EASTERLY";
            if (a < 225) return "SOUTHERLY";
            return "WESTERLY";
        }

        private static bool IsCentered(WidthSpec w) { return Math.Abs(w.Left - w.Right) <= 1e-9; }

        private static string Feet(double feet, EasementSettings settings)
        {
            return feet.ToString("F" + settings.DistanceDecimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        private static string Upper(string value, string fallback)
        {
            return (string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value).Trim().ToUpperInvariant();
        }

        private static string Blank(string value, string placeholder, string what, List<string> checks)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim().ToUpperInvariant();
            if (checks != null && what != null) AddOnce(checks, "Fill in " + what + ".");
            return placeholder;
        }

        private static void AddOnce(List<string> checks, string text)
        {
            if (!checks.Contains(text)) checks.Add(text);
        }

        /// <summary>"THE SOUTH LINE OF SAID PARCEL 2" becomes "SOUTH LINE", for "DEPARTING SAID SOUTH LINE".</summary>
        public static string ShortName(string line)
        {
            var name = (line ?? string.Empty).Trim();
            if (name.StartsWith("[", StringComparison.Ordinal)) return "LINE";
            if (name.StartsWith("THE ", StringComparison.OrdinalIgnoreCase)) name = name.Substring(4);
            var of = name.IndexOf(" OF ", StringComparison.OrdinalIgnoreCase);
            return (of > 0 ? name.Substring(0, of) : name).ToUpperInvariant();
        }

        private static bool SameLine(GeometrySource a, GeometrySource b)
        {
            return a != null && b != null && a.Handle == b.Handle;
        }
    }
}
