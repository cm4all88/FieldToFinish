using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Drafting;
using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.RecordSurvey
{
    /// <summary>The result of one review action.</summary>
    public sealed class ReviewOutcome
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public static ReviewOutcome Success(string message = null) { return new ReviewOutcome { Ok = true, Message = message }; }
        public static ReviewOutcome Refused(string message) { return new ReviewOutcome { Ok = false, Message = message }; }
    }

    /// <summary>One row of the review table.</summary>
    public sealed class ReviewRow
    {
        public string CallId { get; set; }
        public string Course { get; set; }
        public string Figure { get; set; }
        public int Order { get; set; }
        public string Type { get; set; }
        public string Bearing { get; set; }
        public string Distance { get; set; }
        public string CurveInfo { get; set; }
        public string RecordSource { get; set; }
        public string ObjectType { get; set; }
        public double Confidence { get; set; }
        public CallStatus Status { get; set; }
        public string Flags { get; set; }
        public SourceRef Source { get; set; }
        public int Page { get { return Source != null ? Source.Page : 0; } }
    }

    /// <summary>Whether the project can be built, and why not.</summary>
    public sealed class BuildGate
    {
        public bool Ready { get; set; }
        public List<string> Blockers { get; private set; }
        public List<string> Warnings { get; private set; }
        public BuildGate() { Blockers = new List<string>(); Warnings = new List<string>(); }
    }

    /// <summary>
    /// Everything the review window does to a project, with no window: approve, reject, edit,
    /// choose an alternative, reorder, assign figures, and decide whether a build may go
    /// ahead. Edits are strictly parsed and recorded on the call, so the reconstruction can
    /// always say what came from the document and what a person changed.
    /// </summary>
    public sealed class ReviewSession
    {
        public RecordSurveyProject Project { get; private set; }
        public RecordSurveySettings Settings { get; private set; }
        public string Reviewer { get; set; }

        public ReviewSession(RecordSurveyProject project, RecordSurveySettings settings)
        {
            if (project == null) throw new ArgumentNullException("project");
            Project = project;
            Settings = settings ?? new RecordSurveySettings();
            Reviewer = Environment.UserName;
        }

        // ------------------------------------------------------------ table

        public List<ReviewRow> Rows()
        {
            return Project.Calls
                .OrderBy(c => string.IsNullOrEmpty(c.Figure) ? "~" : c.Figure, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Order == 0 ? int.MaxValue : c.Order)
                .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .Select(Row).ToList();
        }

        public ReviewRow Row(SurveyCall c)
        {
            var s = Settings;
            ValueBasis basis;
            var v = c.GeometryValue(s.PreferMeasured, out basis);
            var row = new ReviewRow
            {
                CallId = c.Id,
                Course = (string.IsNullOrEmpty(c.Figure) ? "(unassigned)" : c.Figure) + (c.Order > 0 ? " #" + c.Order : string.Empty) + " " + c.Id,
                Figure = c.Figure, Order = c.Order,
                Type = c.Kind == CallKind.Curve ? "Curve" : "Line",
                ObjectType = c.ObjectType, Confidence = c.Confidence, Status = c.Status, Source = c.Source
            };
            if (c.Kind == CallKind.Line)
            {
                // An incomplete course has no geometry value yet; show what was read so the reviewer
                // types only the missing half.
                var shown = v ?? (c.Measured != null && !c.Measured.Empty ? c.Measured : c.Records.FirstOrDefault(r => !r.Empty));
                row.Bearing = shown != null && shown.AzimuthDegrees.HasValue ? SurveyDirection.FormatBearing(shown.AzimuthDegrees.Value, s.BearingSecondsDecimals, "°", s.BearingSpaces) : "(none)";
                row.Distance = shown != null && shown.DistanceFeet.HasValue ? SurveyDirection.FormatDistance(shown.DistanceFeet.Value, s.DistanceDecimals, s.FootSymbol) : "(none)";
                row.CurveInfo = string.Empty;
            }
            else
            {
                var cs = c.Curve ?? new CurveSpec();
                row.Bearing = cs.ChordAzimuthDegrees.HasValue ? "CB " + SurveyDirection.FormatBearing(cs.ChordAzimuthDegrees.Value, s.BearingSecondsDecimals, "°", s.BearingSpaces) : string.Empty;
                row.Distance = cs.ArcLength.HasValue ? "L=" + SurveyDirection.FormatDistance(cs.ArcLength.Value, s.DistanceDecimals, s.FootSymbol) : string.Empty;
                row.CurveInfo = CurveSummary(cs, s);
            }
            row.RecordSource = RecordSourceText(c, basis);
            row.Flags = Flags(c);
            return row;
        }

        public static string CurveSummary(CurveSpec cs, RecordSurveySettings s)
        {
            var parts = new List<string>();
            if (cs.Radius.HasValue) parts.Add("R=" + SurveyDirection.FormatDistance(cs.Radius.Value, s.DistanceDecimals, s.FootSymbol));
            if (cs.DeltaDegrees.HasValue) parts.Add("Δ=" + SurveyDirection.FormatAzimuth(cs.DeltaDegrees.Value, s.BearingSecondsDecimals, "°"));
            if (cs.ArcLength.HasValue) parts.Add("L=" + SurveyDirection.FormatDistance(cs.ArcLength.Value, s.DistanceDecimals, s.FootSymbol));
            if (cs.ChordLength.HasValue) parts.Add("CH=" + SurveyDirection.FormatDistance(cs.ChordLength.Value, s.DistanceDecimals, s.FootSymbol));
            if (cs.ChordAzimuthDegrees.HasValue) parts.Add("CB=" + SurveyDirection.FormatBearing(cs.ChordAzimuthDegrees.Value, s.BearingSecondsDecimals, "°", s.BearingSpaces));
            if (cs.TangentLength.HasValue) parts.Add("T=" + SurveyDirection.FormatDistance(cs.TangentLength.Value, s.DistanceDecimals, s.FootSymbol));
            if (!string.IsNullOrEmpty(cs.Turn)) parts.Add(cs.Turn);
            if (!string.IsNullOrEmpty(cs.Tag)) parts.Insert(0, cs.Tag);
            return string.Join(" ", parts.ToArray());
        }

        private static string RecordSourceText(SurveyCall c, ValueBasis basis)
        {
            var parts = new List<string>();
            if (c.Measured != null && !c.Measured.Empty) parts.Add("M");
            foreach (var r in c.Records.Where(r => !r.Empty)) parts.Add(r.SourceId.Length > 0 ? r.SourceId : "R");
            var text = parts.Count == 0 ? "-" : string.Join(", ", parts.ToArray());
            return text + " [" + basis + "]";
        }

        private string Flags(SurveyCall c)
        {
            var flags = new List<string>();
            if (c.Confidence < Settings.ReviewThreshold) flags.Add("low confidence");
            if (c.Alternatives.Count > 0) flags.Add(c.Alternatives.Count + " alternative(s)");
            if (c.Kind == CallKind.Line)
            {
                ValueBasis b;
                if (c.GeometryValue(Settings.PreferMeasured, out b) == null) flags.Add("incomplete");
            }
            else if (c.Curve != null)
            {
                var sol = CurveSolver.Solve(c.Curve, Settings.RadiusToleranceFt, Settings.DeltaToleranceSeconds);
                if (!sol.Ok) flags.Add("curve: " + sol.Error);
                else if (sol.Disagreements.Count > 0) flags.Add("curve elements disagree");
                if (string.IsNullOrEmpty(c.Curve.Turn) && !c.Curve.ChordAzimuthDegrees.HasValue) flags.Add("turn direction unknown");
            }
            if (c.HasRecordAndMeasured) flags.Add(RecordVsMeasuredNote(c) ?? "record + measured");
            if (c.Edits.Count > 0) flags.Add("edited");
            if (c.Reversed) flags.Add("reversed in traverse");
            return string.Join("; ", flags.ToArray());
        }

        /// <summary>A note when record and measured differ beyond the warning thresholds; null when they agree.</summary>
        public string RecordVsMeasuredNote(SurveyCall c)
        {
            if (!c.HasRecordAndMeasured || c.Measured == null || !c.Measured.Complete) return null;
            foreach (var r in c.Records.Where(r => r.Complete))
            {
                var seconds = Math.Abs(CurveSolver.AngleDiff(r.AzimuthDegrees.Value, c.Measured.AzimuthDegrees.Value)) * 3600.0;
                var feet = Math.Abs(r.DistanceFeet.Value - c.Measured.DistanceFeet.Value);
                if (seconds > Settings.RecordVsMeasuredBearingWarnSeconds || feet > Settings.RecordVsMeasuredDistanceWarnFt)
                    return string.Format(CultureInfo.InvariantCulture, "record {0} vs measured differ by {1:0}\" / {2:0.00}'", r.SourceId.Length > 0 ? r.SourceId : "R", seconds, feet);
            }
            return null;
        }

        // ------------------------------------------------------------ status

        public ReviewOutcome Approve(string callId)
        {
            var c = Find(callId);
            if (c == null) return ReviewOutcome.Refused("No call " + callId + ".");
            if (c.Kind == CallKind.Line)
            {
                ValueBasis b;
                if (c.GeometryValue(Settings.PreferMeasured, out b) == null)
                    return ReviewOutcome.Refused(callId + " has no complete bearing and distance; enter the missing value before approving. Nothing is invented to fill it.");
            }
            else
            {
                var sol = CurveSolver.Solve(c.Curve ?? new CurveSpec(), Settings.RadiusToleranceFt, Settings.DeltaToleranceSeconds);
                if (!sol.Ok) return ReviewOutcome.Refused(callId + ": " + sol.Error);
            }
            c.Status = CallStatus.Approved;
            Log(c, "approved");
            return ReviewOutcome.Success();
        }

        /// <summary>Approves every call at or above the confidence that has nothing else flagged. Low ones stay for the reviewer.</summary>
        public int ApproveAllAbove(double minimumConfidence)
        {
            var n = 0;
            foreach (var c in Project.Calls.Where(c => c.Status == CallStatus.Extracted && c.Confidence >= minimumConfidence && c.Alternatives.Count == 0))
                if (Approve(c.Id).Ok) n++;
            return n;
        }

        public ReviewOutcome Reject(string callId)
        {
            var c = Find(callId);
            if (c == null) return ReviewOutcome.Refused("No call " + callId + ".");
            c.Status = CallStatus.Rejected;
            Log(c, "rejected");
            return ReviewOutcome.Success();
        }

        public ReviewOutcome Reopen(string callId)
        {
            var c = Find(callId);
            if (c == null) return ReviewOutcome.Refused("No call " + callId + ".");
            c.Status = CallStatus.NeedsReview;
            Log(c, "reopened");
            return ReviewOutcome.Success();
        }

        // ------------------------------------------------------------ edits

        /// <summary>Edits a bearing. target: "measured" or a record source id ("", "R1"). Strict parse; a refused entry changes nothing.</summary>
        public ReviewOutcome EditBearing(string callId, string target, string text)
        {
            var c = Find(callId);
            if (c == null) return ReviewOutcome.Refused("No call " + callId + ".");
            var read = SurveyCallParser.ParseBearing(text);
            if (!read.Ok) return ReviewOutcome.Refused(read.Error);
            var v = Target(c, target, true);
            var before = v.AzimuthDegrees.HasValue ? SurveyDirection.FormatBearing(v.AzimuthDegrees.Value, 0, "°") : "(none)";
            v.AzimuthDegrees = read.Value;
            v.BearingText = read.Normalized;
            v.Confidence = 1.0;
            AfterEdit(c, "bearing " + before + " -> " + read.Normalized);
            return ReviewOutcome.Success(read.Normalized);
        }

        public ReviewOutcome EditDistance(string callId, string target, string text)
        {
            var c = Find(callId);
            if (c == null) return ReviewOutcome.Refused("No call " + callId + ".");
            var read = SurveyCallParser.ParseDistance(text);
            if (!read.Ok) return ReviewOutcome.Refused(read.Error);
            var v = Target(c, target, true);
            var before = v.DistanceFeet.HasValue ? SurveyDirection.FormatDistance(v.DistanceFeet.Value, 2, true) : "(none)";
            v.DistanceFeet = read.Value;
            v.DistanceText = read.Normalized;
            v.Confidence = 1.0;
            AfterEdit(c, "distance " + before + " -> " + read.Normalized);
            return ReviewOutcome.Success(read.Normalized);
        }

        /// <summary>Edits one curve element: R, DELTA, L, CH, CB, T, TURN, or TANGENT (yes/no).</summary>
        public ReviewOutcome EditCurve(string callId, string element, string text)
        {
            var c = Find(callId);
            if (c == null) return ReviewOutcome.Refused("No call " + callId + ".");
            if (c.Kind != CallKind.Curve) return ReviewOutcome.Refused(callId + " is not a curve.");
            if (c.Curve == null) c.Curve = new CurveSpec();
            var e = (element ?? string.Empty).Trim().ToUpperInvariant();
            var cs = c.Curve;
            string change;
            switch (e)
            {
                case "R": case "L": case "CH": case "T":
                {
                    var read = SurveyCallParser.ParseDistance(text);
                    if (!read.Ok) return ReviewOutcome.Refused(read.Error);
                    if (e == "R") cs.Radius = read.Value; else if (e == "L") cs.ArcLength = read.Value; else if (e == "CH") cs.ChordLength = read.Value; else cs.TangentLength = read.Value;
                    change = e + " -> " + read.Normalized;
                    break;
                }
                case "DELTA": case "Δ": case "D":
                {
                    var read = SurveyCallParser.ParseAngle(text);
                    if (!read.Ok) return ReviewOutcome.Refused(read.Error);
                    cs.DeltaDegrees = read.Value;
                    e = "DELTA";
                    change = "Δ -> " + read.Normalized;
                    break;
                }
                case "CB":
                {
                    var read = SurveyCallParser.ParseBearing(text);
                    if (!read.Ok) return ReviewOutcome.Refused(read.Error);
                    cs.ChordAzimuthDegrees = read.Value;
                    change = "CB -> " + read.Normalized;
                    break;
                }
                case "TURN":
                {
                    var turn = CurveSolver.ParseTurn(text);
                    if (!turn.HasValue) return ReviewOutcome.Refused("Enter LEFT or RIGHT.");
                    cs.Turn = turn.Value ? "LEFT" : "RIGHT";
                    change = "turn -> " + cs.Turn;
                    e = null;
                    break;
                }
                case "TANGENT":
                {
                    var t = (text ?? string.Empty).Trim().ToUpperInvariant();
                    if (t == "YES" || t == "Y" || t == "TRUE") cs.TangentToPrevious = true;
                    else if (t == "NO" || t == "N" || t == "FALSE") cs.TangentToPrevious = false;
                    else if (t.Length == 0) cs.TangentToPrevious = null;
                    else return ReviewOutcome.Refused("Enter yes, no, or nothing (unknown).");
                    change = "tangent -> " + (cs.TangentToPrevious.HasValue ? cs.TangentToPrevious.Value.ToString() : "unknown");
                    e = null;
                    break;
                }
                default:
                    return ReviewOutcome.Refused("Unknown curve element '" + element + "'. Use R, DELTA, L, CH, CB, T, TURN or TANGENT.");
            }
            if (e != null && !cs.StatedElements.Contains(e)) cs.StatedElements.Add(e);
            AfterEdit(c, change);
            var sol = CurveSolver.Solve(cs, Settings.RadiusToleranceFt, Settings.DeltaToleranceSeconds);
            return ReviewOutcome.Success(sol.Ok ? (sol.Disagreements.Count > 0 ? "Elements disagree: " + string.Join("; ", sol.Disagreements.ToArray()) : "Curve solves from " + sol.SolvedFrom + ".") : sol.Error);
        }

        /// <summary>Takes one of the offered alternatives as the value. Recorded as an edit; the original reading stays in the notes.</summary>
        public ReviewOutcome ChooseAlternative(string callId, int alternativeIndex)
        {
            var c = Find(callId);
            if (c == null) return ReviewOutcome.Refused("No call " + callId + ".");
            if (alternativeIndex < 0 || alternativeIndex >= c.Alternatives.Count) return ReviewOutcome.Refused("No such alternative.");
            var alt = c.Alternatives[alternativeIndex];
            var applied = TraverseBuilder.Apply(c, alt);
            if (applied == null) return ReviewOutcome.Refused("That alternative cannot be applied to " + callId + ".");
            var original = Row(c);
            c.Measured = applied.Measured;
            c.Records = applied.Records;
            c.Curve = applied.Curve;
            c.Notes.Add("Original reading: " + (alt.Field == "bearing" ? original.Bearing : alt.Field == "distance" ? original.Distance : original.CurveInfo));
            c.Alternatives.RemoveAt(alternativeIndex);
            AfterEdit(c, alt.Field + " -> " + alt.Text + " (chosen alternative: " + alt.Reason + ")");
            return ReviewOutcome.Success(alt.Text);
        }

        public ReviewOutcome SetObjectType(string callId, string objectType)
        {
            var c = Find(callId);
            if (c == null) return ReviewOutcome.Refused("No call " + callId + ".");
            if (Settings.FindEntity(objectType) == null) return ReviewOutcome.Refused("\"" + objectType + "\" is not an entity standard; add it under Settings > Recorded Surveys.");
            c.ObjectType = Settings.FindEntity(objectType).Name;
            return ReviewOutcome.Success();
        }

        public ReviewOutcome SetReversed(string callId, bool reversed)
        {
            var c = Find(callId);
            if (c == null) return ReviewOutcome.Refused("No call " + callId + ".");
            c.Reversed = reversed;
            return ReviewOutcome.Success();
        }

        // ------------------------------------------------------------ figures and order

        public SurveyFigure AddFigure(string name, string kind, bool closed)
        {
            var existing = Project.Figures.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            var figure = new SurveyFigure { Name = name, Kind = kind ?? "Figure", Closed = closed };
            Project.Figures.Add(figure);
            return figure;
        }

        public ReviewOutcome Assign(string callId, string figure, int order)
        {
            var c = Find(callId);
            if (c == null) return ReviewOutcome.Refused("No call " + callId + ".");
            if (!string.IsNullOrEmpty(figure) && Project.Figures.All(f => !string.Equals(f.Name, figure, StringComparison.OrdinalIgnoreCase)))
                AddFigure(figure, figure.StartsWith("Lot", StringComparison.OrdinalIgnoreCase) ? "Lot" : "Figure", true);
            c.Figure = figure ?? string.Empty;
            c.Order = Math.Max(0, order);
            Renumber(c.Figure);
            return ReviewOutcome.Success();
        }

        public ReviewOutcome Move(string callId, int delta)
        {
            var c = Find(callId);
            if (c == null) return ReviewOutcome.Refused("No call " + callId + ".");
            var siblings = Project.CallsOf(c.Figure);
            var index = siblings.IndexOf(c);
            var target = index + delta;
            if (target < 0 || target >= siblings.Count) return ReviewOutcome.Refused("Already at the end.");
            siblings.RemoveAt(index);
            siblings.Insert(target, c);
            for (var i = 0; i < siblings.Count; i++) siblings[i].Order = i + 1;
            return ReviewOutcome.Success();
        }

        private void Renumber(string figure)
        {
            var siblings = Project.CallsOf(figure);
            for (var i = 0; i < siblings.Count; i++) siblings[i].Order = i + 1;
        }

        public ReviewOutcome SetFigureStart(string figure, P2 start)
        {
            var f = Project.Figures.FirstOrDefault(x => string.Equals(x.Name, figure, StringComparison.OrdinalIgnoreCase));
            if (f == null) return ReviewOutcome.Refused("No figure " + figure + ".");
            f.Start = start;
            return ReviewOutcome.Success();
        }

        public ReviewOutcome SetFigureClosed(string figure, bool closed)
        {
            var f = Project.Figures.FirstOrDefault(x => string.Equals(x.Name, figure, StringComparison.OrdinalIgnoreCase));
            if (f == null) return ReviewOutcome.Refused("No figure " + figure + ".");
            f.Closed = closed;
            return ReviewOutcome.Success();
        }

        /// <summary>
        /// Places every figure connected to <paramref name="firstFigure"/> through shared courses:
        /// the first starts at the picked point, and each neighbour is put where its copy of the
        /// shared course coincides with the original's built position, so adjoining lots meet
        /// at their common corners. Figures not connected to any placed one are listed; they need
        /// their own start.
        /// </summary>
        public List<string> PlaceConnected(string firstFigure, P2 start, double unitsPerFoot)
        {
            var notes = new List<string>();
            var first = Project.Figures.FirstOrDefault(f => string.Equals(f.Name, firstFigure, StringComparison.OrdinalIgnoreCase));
            if (first == null) { notes.Add("No figure " + firstFigure + "."); return notes; }
            first.Start = start;
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { first.Name };
            var options = Options(unitsPerFoot, false);
            var progressed = true;
            while (progressed)
            {
                progressed = false;
                foreach (var figure in Project.Figures.Where(f => !placed.Contains(f.Name)))
                {
                    // A course of this figure that is (or is copied by) a course of a placed figure.
                    SurveyCall mine = null, theirs = null;
                    foreach (var c in Project.CallsOf(figure.Name))
                    {
                        if (!string.IsNullOrEmpty(c.SharedWith))
                        {
                            var original = Project.FindCall(c.SharedWith);
                            if (original != null && placed.Contains(original.Figure ?? string.Empty)) { mine = c; theirs = original; break; }
                        }
                        var copy = Project.Calls.FirstOrDefault(x => x.SharedWith == c.Id && placed.Contains(x.Figure ?? string.Empty));
                        if (copy != null) { mine = c; theirs = copy; break; }
                    }
                    if (mine == null) continue;

                    var theirFigure = Project.Figures.First(f => string.Equals(f.Name, theirs.Figure, StringComparison.OrdinalIgnoreCase));
                    var theirTraverse = TraverseBuilder.Build(theirFigure, Project.CallsOf(theirFigure.Name), theirFigure.Start ?? new P2(0, 0), options);
                    var myTraverse = TraverseBuilder.Build(figure, Project.CallsOf(figure.Name), new P2(0, 0), options);
                    var theirCourse = theirTraverse.Courses.FirstOrDefault(c => c.Call.Id == theirs.Id && c.Placed);
                    var myCourse = myTraverse.Courses.FirstOrDefault(c => c.Call.Id == mine.Id && c.Placed);
                    if (theirCourse == null || myCourse == null)
                    {
                        notes.Add(figure.Name + ": shares a course with " + theirFigure.Name + " but one of them cannot be traversed yet; place it by hand.");
                        placed.Add(figure.Name);
                        progressed = true;
                        continue;
                    }
                    // The shared course runs one way in each lot: match its ends whichever way round.
                    var a = theirCourse.Course.Start; var b = theirCourse.Course.End;
                    var a2 = myCourse.Course.Start; var b2 = myCourse.Course.End;
                    var same = (a - a2) ; var flipped = (b - a2);
                    var errSame = (b - (b2 + same)).Length;
                    var errFlipped = (a - (b2 + flipped)).Length;
                    var shift = errSame <= errFlipped ? same : flipped;
                    figure.Start = new P2(shift.X, shift.Y);
                    if (Math.Min(errSame, errFlipped) > Settings.SharedLineToleranceFt * unitsPerFoot)
                        notes.Add(string.Format(CultureInfo.InvariantCulture, "{0}: placed on its shared course with {1}; the two calls for it disagree by {2:0.000}' at the far end.",
                            figure.Name, theirFigure.Name, Math.Min(errSame, errFlipped) / unitsPerFoot));
                    placed.Add(figure.Name);
                    progressed = true;
                }
            }
            foreach (var figure in Project.Figures.Where(f => !placed.Contains(f.Name)))
                notes.Add(figure.Name + " shares no course with a placed figure; pick its start point separately.");
            return notes;
        }

        /// <summary>Runs the page-position ordering and applies it where the reviewer has not ordered by hand.</summary>
        public AssemblyResult AutoOrder(AssemblyOptions options, bool overwrite)
        {
            var result = TraverseAssembler.Assemble(Project, options);
            TraverseAssembler.Apply(Project, result, overwrite);
            return result;
        }

        // ------------------------------------------------------------ gate

        /// <summary>
        /// Whether a build may proceed. Blockers: a figure with a course still needing review,
        /// or with no start point. Warnings: unassigned courses (not built), rejected courses,
        /// figures that do not close.
        /// </summary>
        public BuildGate Gate()
        {
            var gate = new BuildGate();
            var figures = Project.Figures.Where(f => Project.CallsOf(f.Name).Any(c => c.Status != CallStatus.Rejected)).ToList();
            if (figures.Count == 0) gate.Blockers.Add("No figure has any courses. Assign courses to a lot, tract or boundary in the review.");
            foreach (var f in figures)
            {
                var calls = Project.CallsOf(f.Name).Where(c => c.Status != CallStatus.Rejected).ToList();
                var pending = calls.Where(c => c.Status == CallStatus.NeedsReview).Select(c => c.Id).ToList();
                if (pending.Count > 0) gate.Blockers.Add(f.Name + ": " + pending.Count + " course(s) still need review (" + string.Join(", ", pending.ToArray()) + ").");
                var unresolved = calls.Where(c => c.Status == CallStatus.Extracted && c.Confidence < Settings.ReviewThreshold).Select(c => c.Id).ToList();
                if (unresolved.Count > 0) gate.Blockers.Add(f.Name + ": " + string.Join(", ", unresolved.ToArray()) + " under the review threshold and not approved.");
                if (!f.Start.HasValue) gate.Blockers.Add(f.Name + ": no start point. Pick where the traverse begins in the drawing.");
                if (calls.Any(c => c.Order == 0)) gate.Warnings.Add(f.Name + ": some courses are unordered; they traverse last, in id order.");
            }
            var unassigned = Project.Calls.Where(c => string.IsNullOrEmpty(c.Figure) && c.Status != CallStatus.Rejected).Select(c => c.Id).ToList();
            if (unassigned.Count > 0) gate.Warnings.Add(unassigned.Count + " course(s) are not assigned to a figure and will not be built: " + string.Join(", ", unassigned.ToArray()) + ".");
            var rejected = Project.Calls.Count(c => c.Status == CallStatus.Rejected);
            if (rejected > 0) gate.Warnings.Add(rejected + " course(s) rejected and left out.");
            gate.Ready = gate.Blockers.Count == 0;
            return gate;
        }

        /// <summary>A dry run of every figure, for the review window's closure preview.</summary>
        public List<TraverseResult> Preview(double unitsPerFoot)
        {
            return TraverseBuilder.BuildAll(Project, Options(unitsPerFoot, false));
        }

        public TraverseOptions Options(double unitsPerFoot, bool requireApproved)
        {
            return new TraverseOptions
            {
                PreferMeasured = Settings.PreferMeasured, UnitsPerFoot = unitsPerFoot, ClosureToleranceFeet = Settings.ClosureToleranceFt,
                DistanceToleranceFeet = Settings.DistanceToleranceFt, AngleToleranceSeconds = Settings.BearingToleranceSeconds,
                RequireApproved = requireApproved, MinimumPrecision = Settings.MinimumClosurePrecision
            };
        }

        // ------------------------------------------------------------ helpers

        private SurveyCall Find(string id) { return Project.FindCall(id); }

        private static CallValue Target(SurveyCall c, string target, bool create)
        {
            if (string.Equals(target, "measured", StringComparison.OrdinalIgnoreCase))
            {
                if (c.Measured == null) c.Measured = new CallValue();
                return c.Measured;
            }
            var id = target ?? string.Empty;
            var r = c.Records.FirstOrDefault(x => string.Equals(x.SourceId ?? string.Empty, id, StringComparison.OrdinalIgnoreCase)) ?? c.Records.FirstOrDefault();
            if (r == null && create) { r = new RecordValue { SourceId = id }; c.Records.Add(r); }
            return r;
        }

        private void AfterEdit(SurveyCall c, string change)
        {
            c.Basis = c.Basis == ValueBasis.Measured ? ValueBasis.Measured : ValueBasis.Entered;
            c.Confidence = 1.0;
            c.Status = CallStatus.Approved;
            Log(c, change);
        }

        private void Log(SurveyCall c, string what)
        {
            c.Edits.Add(what + " (" + Reviewer + ", " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC)");
        }
    }
}
