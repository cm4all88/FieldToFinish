using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FieldCodes.Drafting;
using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.RecordSurvey
{
    /// <summary>A course as it exists in the drawing now, read from a stamped entity.</summary>
    public sealed class CadCourse
    {
        public string Handle { get; set; }
        public string CallId { get; set; }
        public string Figure { get; set; }
        public Course Course { get; set; }
        public string Layer { get; set; }
        public string Linetype { get; set; }
        public string ObjectType { get; set; }
        public List<string> SharedWith { get; set; }
        public CadCourse() { SharedWith = new List<string>(); }
    }

    public sealed class CadLabel
    {
        public string Handle { get; set; }
        public string CallId { get; set; }
        public string Style { get; set; }
        public string Layer { get; set; }
        public bool IsCivil3D { get; set; }
        public string Kind { get; set; }
    }

    public sealed class CadMonument
    {
        public string Handle { get; set; }
        public string MonumentId { get; set; }
        public P2 Position { get; set; }
        public string Block { get; set; }
        public string Layer { get; set; }
    }

    /// <summary>Everything FTFRECORDCHECK reads back from the drawing.</summary>
    public sealed class CadState
    {
        public List<CadCourse> Courses { get; private set; }
        public List<CadLabel> Labels { get; private set; }
        public List<CadMonument> Monuments { get; private set; }
        public double UnitsPerFoot { get; set; }
        public CadState() { Courses = new List<CadCourse>(); Labels = new List<CadLabel>(); Monuments = new List<CadMonument>(); UnitsPerFoot = 1.0; }
    }

    public static class QcVerdict
    {
        public const string Match = "MATCH";
        public const string Review = "REVIEW";
        public const string Missing = "MISSING";
        public const string Error = "ERROR";
        public const string Info = "INFO";
    }

    public sealed class QcItem
    {
        /// <summary>BearingMismatch, DistanceMismatch, RadiusMismatch, ArcMismatch, DeltaMismatch, ChordMismatch, OpenBoundary, ClosureError,
        /// DuplicateGeometry, SharedBoundary, MissingLine, MissingLabel, MissingMonument, LayerMismatch, StyleMismatch, RecordVsMeasured,
        /// CurveMathematics, Unresolved, Closure, Course, Curve.</summary>
        public string Code { get; set; }
        public string Subject { get; set; }
        public string Verdict { get; set; }
        public string Message { get; set; }
        public string CallId { get; set; }
        public string Handle { get; set; }
        public string Figure { get; set; }

        public override string ToString()
        {
            return Subject + ": " + (string.IsNullOrEmpty(Message) ? Verdict : Message + (Verdict == QcVerdict.Match ? string.Empty : " — " + Verdict));
        }
    }

    public sealed class RecordQcReport
    {
        public string ProjectId { get; set; }
        public string Document { get; set; }
        public DateTime CheckedUtc { get; set; }
        public List<QcItem> Items { get; private set; }
        public RecordQcReport() { Items = new List<QcItem>(); CheckedUtc = DateTime.UtcNow; }

        public int Matches { get { return Items.Count(i => i.Verdict == QcVerdict.Match); } }
        public int Reviews { get { return Items.Count(i => i.Verdict == QcVerdict.Review); } }
        public int Missing { get { return Items.Count(i => i.Verdict == QcVerdict.Missing); } }
        public int Errors { get { return Items.Count(i => i.Verdict == QcVerdict.Error); } }
        public bool Clean { get { return Reviews == 0 && Missing == 0 && Errors == 0; } }

        public string Text()
        {
            var sb = new StringBuilder();
            sb.AppendLine("FTFRECORDCHECK " + (Document ?? string.Empty) + " (" + ProjectId + "), " + CheckedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC");
            foreach (var i in Items) sb.AppendLine(i.ToString());
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} match, {1} review, {2} missing, {3} error(s).", Matches, Reviews, Missing, Errors));
            sb.AppendLine("Nothing was adjusted. Discrepancies are for the surveyor to resolve.");
            return sb.ToString();
        }
    }

    /// <summary>
    /// FTFRECORDCHECK's engine: compares what is in the drawing with what the record says,
    /// course by course, figure by figure, and with the office standard. Flags only. A call is
    /// never changed to match the drawing, and the drawing is never changed to match the call.
    /// </summary>
    public static class RecordQc
    {
        public static RecordQcReport Evaluate(RecordSurveyProject project, CadState cad, RecordSurveySettings settings, StandardsResolution standards)
        {
            settings = settings ?? new RecordSurveySettings();
            cad = cad ?? new CadState();
            var report = new RecordQcReport { ProjectId = project.Id, Document = project.Document != null ? project.Document.Path : null };
            var upf = cad.UnitsPerFoot > 0 ? cad.UnitsPerFoot : 1.0;
            var options = new TraverseOptions
            {
                PreferMeasured = settings.PreferMeasured, UnitsPerFoot = upf, ClosureToleranceFeet = settings.ClosureToleranceFt,
                DistanceToleranceFeet = settings.DistanceToleranceFt, AngleToleranceSeconds = settings.BearingToleranceSeconds,
                RequireApproved = false, MinimumPrecision = settings.MinimumClosurePrecision
            };

            // ---- unresolved extraction
            foreach (var c in project.Calls.Where(c => c.Status == CallStatus.NeedsReview || (c.Status == CallStatus.Extracted && c.Confidence < settings.ReviewThreshold)))
                Add(report, "Unresolved", "Course " + c.Id, QcVerdict.Review, "extraction not reviewed (confidence " + c.Confidence.ToString("0.00", CultureInfo.InvariantCulture) + ")", c.Id, null, c.Figure);

            var traverses = TraverseBuilder.BuildAll(project, options);
            var byCall = cad.Courses.Where(x => !string.IsNullOrEmpty(x.CallId)).ToLookup(x => x.CallId, StringComparer.OrdinalIgnoreCase);

            foreach (var t in traverses)
            {
                foreach (var tc in t.Courses)
                {
                    var call = tc.Call;
                    var subject = (call.Kind == CallKind.Curve ? "Curve " : "Course ") + call.Id;
                    var entity = byCall[call.Id].FirstOrDefault() ?? cad.Courses.FirstOrDefault(x => x.SharedWith.Contains(call.Id));
                    if (entity == null)
                    {
                        Add(report, "MissingLine", subject, QcVerdict.Missing, "no geometry in the drawing for this course", call.Id, null, t.Figure);
                        continue;
                    }
                    if (!tc.Placed)
                    {
                        Add(report, "Course", subject, QcVerdict.Review, tc.Problem, call.Id, entity.Handle, t.Figure);
                        continue;
                    }
                    if (call.Kind == CallKind.Line) CheckLine(report, settings, tc, entity, upf, t.Figure);
                    else CheckCurve(report, settings, tc, entity, upf, t.Figure);

                    // Record vs measured on the same course: reported, never reconciled.
                    if (call.HasRecordAndMeasured && call.Measured.Complete)
                        foreach (var r in call.Records.Where(r => r.Complete))
                        {
                            var seconds = Math.Abs(CurveSolver.AngleDiff(r.AzimuthDegrees.Value, call.Measured.AzimuthDegrees.Value)) * 3600.0;
                            var feet = Math.Abs(r.DistanceFeet.Value - call.Measured.DistanceFeet.Value);
                            if (seconds > settings.RecordVsMeasuredBearingWarnSeconds || feet > settings.RecordVsMeasuredDistanceWarnFt)
                                Add(report, "RecordVsMeasured", subject, QcVerdict.Info, string.Format(CultureInfo.InvariantCulture,
                                    "record {0} {1} / measured {2} differ by {3:0}\" and {4:0.00}'", r.SourceId.Length > 0 ? r.SourceId : "R",
                                    r.Describe(settings.BearingSecondsDecimals, settings.DistanceDecimals), call.Measured.Describe(settings.BearingSecondsDecimals, settings.DistanceDecimals), seconds, feet),
                                    call.Id, entity.Handle, t.Figure);
                        }

                    // Standard: layer.
                    if (standards != null)
                    {
                        var std = standards.For(call.ObjectType);
                        if (std != null && !string.IsNullOrEmpty(std.Layer) && !string.Equals(std.Layer, entity.Layer, StringComparison.OrdinalIgnoreCase))
                            Add(report, "LayerMismatch", subject, QcVerdict.Review, "on layer " + entity.Layer + ", standard for " + call.ObjectType + " is " + std.Layer, call.Id, entity.Handle, t.Figure);
                    }
                }

                // Closure and open boundary, from the CAD geometry as it is now.
                if (t.Closure != null && t.Closure.Closed && t.Courses.All(c => c.Placed))
                {
                    var subject = (string.IsNullOrEmpty(t.Figure) ? "Figure" : t.Figure) + " closure";
                    var verdict = t.Closure.Misclosure <= settings.ClosureToleranceFt ? QcVerdict.Match : QcVerdict.Review;
                    Add(report, "Closure", subject, verdict, t.Closure.Misclosure.ToString("0.000", CultureInfo.InvariantCulture) + "'" +
                        (t.Closure.Precision.HasValue ? " (" + TraverseBuilder.PrecisionText(t.Closure) + ")" : string.Empty), null, null, t.Figure);
                    foreach (var s in t.Suggestions)
                        Add(report, "ClosureError", subject, QcVerdict.Info, "an alternative reading would change it: " + s, s.CallId, null, t.Figure);

                    var cadLoop = t.Courses.Select(c => byCall[c.Call.Id].FirstOrDefault() ?? cad.Courses.FirstOrDefault(x => x.SharedWith.Contains(c.Call.Id))).ToList();
                    if (cadLoop.All(e => e != null))
                    {
                        var gap = LargestGap(cadLoop.Select(e => e.Course).ToList(), settings.SharedLineToleranceFt * upf);
                        if (gap > settings.SharedLineToleranceFt * upf)
                            Add(report, "OpenBoundary", (string.IsNullOrEmpty(t.Figure) ? "Figure" : t.Figure), QcVerdict.Review,
                                "the drawn courses do not meet: largest gap " + (gap / upf).ToString("0.000", CultureInfo.InvariantCulture) + "'", null, null, t.Figure);
                    }
                }
            }

            // ---- shared boundaries and duplicates
            var shared = SharedLineMatcher.Match(traverses, settings.SharedLineToleranceFt * upf, settings.DistanceToleranceFt, settings.BearingToleranceSeconds);
            foreach (var s in shared.Where(x => x.IsShared))
            {
                var subject = "Shared boundary " + string.Join(" / ", s.Owners.Select(o => o.Key).Distinct().ToArray());
                if (s.Discrepancies.Count == 0) Add(report, "SharedBoundary", subject, QcVerdict.Match, null, s.Owners[0].Value, null, s.Owners[0].Key);
                else foreach (var d in s.Discrepancies) Add(report, "SharedBoundary", subject, QcVerdict.Review, d, s.Owners[0].Value, null, s.Owners[0].Key);
            }
            foreach (var n in SharedLineMatcher.NearDuplicates(traverses, settings.SharedLineToleranceFt * upf))
                Add(report, "DuplicateGeometry", "Near-coincident lines", QcVerdict.Review, n, null, null, null);
            for (var i = 0; i < cad.Courses.Count; i++)
            for (var j = i + 1; j < cad.Courses.Count; j++)
                if (SharedLineMatcher.SameCourse(cad.Courses[i].Course, cad.Courses[j].Course, settings.SharedLineToleranceFt * upf))
                    Add(report, "DuplicateGeometry", "Duplicate geometry", QcVerdict.Review, "entities " + cad.Courses[i].Handle + " and " + cad.Courses[j].Handle + " are the same course drawn twice",
                        cad.Courses[i].CallId, cad.Courses[i].Handle, cad.Courses[i].Figure);

            // ---- labels
            if (settings.LabelsEnabled)
            {
                var labelled = new HashSet<string>(cad.Labels.Where(l => !string.IsNullOrEmpty(l.CallId)).Select(l => l.CallId), StringComparer.OrdinalIgnoreCase);
                var once = new HashSet<string>(shared.Where(x => x.IsShared).SelectMany(x => x.Owners.Skip(1).Select(o => o.Value)), StringComparer.OrdinalIgnoreCase);
                foreach (var t in traverses)
                    foreach (var tc in t.Courses.Where(c => c.Placed))
                    {
                        var std = settings.FindEntity(tc.Call.ObjectType);
                        if (std != null && !std.Label) continue;
                        if (once.Contains(tc.Call.Id)) continue;
                        if (!labelled.Contains(tc.Call.Id))
                            Add(report, "MissingLabel", (tc.Call.Kind == CallKind.Curve ? "Curve " : "Course ") + tc.Call.Id, QcVerdict.Missing, "no label in the drawing", tc.Call.Id, null, t.Figure);
                    }
                if (standards != null)
                    foreach (var l in cad.Labels.Where(l => !string.IsNullOrEmpty(l.CallId)))
                    {
                        var call = project.FindCall(l.CallId);
                        if (call == null) continue;
                        var std = standards.For(call.ObjectType);
                        if (std == null) continue;
                        var expected = l.IsCivil3D ? (call.Kind == CallKind.Curve ? std.CurveLabelStyle : std.LineLabelStyle) : std.TextStyle;
                        if (!string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(l.Style) && !string.Equals(expected, l.Style, StringComparison.OrdinalIgnoreCase))
                            Add(report, "StyleMismatch", "Label " + l.CallId, QcVerdict.Review, "style " + l.Style + ", standard is " + expected, l.CallId, l.Handle, call.Figure);
                        if (!string.IsNullOrEmpty(std.LabelLayer) && !string.IsNullOrEmpty(l.Layer) && !string.Equals(std.LabelLayer, l.Layer, StringComparison.OrdinalIgnoreCase))
                            Add(report, "LayerMismatch", "Label " + l.CallId, QcVerdict.Review, "on layer " + l.Layer + ", standard is " + std.LabelLayer, l.CallId, l.Handle, call.Figure);
                    }
            }

            // ---- monuments
            if (settings.DrawMonuments)
            {
                var drawn = new HashSet<string>(cad.Monuments.Where(m => !string.IsNullOrEmpty(m.MonumentId)).Select(m => m.MonumentId), StringComparer.OrdinalIgnoreCase);
                foreach (var m in project.Monuments.Where(m => !string.IsNullOrEmpty(m.Corner) && m.ReviewStatus != CallStatus.Rejected))
                    if (!drawn.Contains(m.Id))
                        Add(report, "MissingMonument", "Monument " + m.Id, QcVerdict.Missing, m.Description + " -- not in the drawing", null, null, null);
            }

            return report;
        }

        private static void CheckLine(RecordQcReport report, RecordSurveySettings s, TraverseCourse tc, CadCourse entity, double upf, string figure)
        {
            var subject = "Course " + tc.Call.Id;
            var cadCourse = entity.Course;
            if (cadCourse == null || cadCourse.Kind != CourseKind.Line)
            {
                Add(report, "Course", subject, QcVerdict.Review, "the drawing holds " + (cadCourse == null ? "nothing" : "an arc") + " where the record calls a line", tc.Call.Id, entity.Handle, figure);
                return;
            }
            var cadLength = cadCourse.Length / upf;
            var cadAz = SurveyDirection.AzimuthFromVector(cadCourse.End.X - cadCourse.Start.X, cadCourse.End.Y - cadCourse.Start.Y);
            // Either direction of the drawn line is the same course.
            var seconds = Math.Min(Math.Abs(CurveSolver.AngleDiff(cadAz, tc.AzimuthUsed)), Math.Abs(CurveSolver.AngleDiff(cadAz, tc.AzimuthUsed + 180.0))) * 3600.0;
            var feet = Math.Abs(cadLength - tc.LengthUsedFeet);
            var problems = new List<string>();
            if (seconds > s.BearingToleranceSeconds)
                problems.Add(string.Format(CultureInfo.InvariantCulture, "CAD {0} / Record {1} ({2:0}\" apart)",
                    SurveyDirection.FormatBearing(cadAz, s.BearingSecondsDecimals, "°"), SurveyDirection.FormatBearing(tc.AzimuthUsed, s.BearingSecondsDecimals, "°"), seconds));
            if (feet > s.DistanceToleranceFt)
                problems.Add(string.Format(CultureInfo.InvariantCulture, "CAD {0} / Record {1}", cadLength.ToString("0.00", CultureInfo.InvariantCulture), tc.LengthUsedFeet.ToString("0.00", CultureInfo.InvariantCulture)));
            if (problems.Count == 0) { Add(report, "Course", subject, QcVerdict.Match, null, tc.Call.Id, entity.Handle, figure); return; }
            if (seconds > s.BearingToleranceSeconds) Add(report, "BearingMismatch", subject, QcVerdict.Review, problems[0], tc.Call.Id, entity.Handle, figure);
            if (feet > s.DistanceToleranceFt) Add(report, "DistanceMismatch", subject, QcVerdict.Review, problems[problems.Count - 1], tc.Call.Id, entity.Handle, figure);
        }

        private static void CheckCurve(RecordQcReport report, RecordSurveySettings s, TraverseCourse tc, CadCourse entity, double upf, string figure)
        {
            var subject = "Curve " + tc.Call.Id;
            var cadCourse = entity.Course;
            if (cadCourse == null || cadCourse.Kind != CourseKind.Arc)
            {
                Add(report, "Curve", subject, QcVerdict.Review, "the drawing holds " + (cadCourse == null ? "nothing" : "a line") + " where the record calls a curve", tc.Call.Id, entity.Handle, figure);
                return;
            }
            var sol = tc.CurveSolution;
            var parts = new List<string>();
            var review = false;
            var r = Math.Abs(cadCourse.Radius / upf - sol.Radius / upf);
            var l = Math.Abs(cadCourse.Length / upf - sol.ArcLength / upf);
            var d = Math.Abs(cadCourse.Sweep * 180.0 / Math.PI - sol.DeltaDegrees) * 3600.0;
            var ch = Math.Abs(cadCourse.Start.DistanceTo(cadCourse.End) / upf - sol.ChordLength / upf);
            parts.Add("Radius " + (r <= s.RadiusToleranceFt ? "MATCH" : "differs " + Ft(r)));
            parts.Add("Arc " + (l <= s.ArcToleranceFt ? "MATCH" : "differs " + Ft(l)));
            parts.Add("Delta " + (d <= s.DeltaToleranceSeconds ? "MATCH" : "differs " + d.ToString("0", CultureInfo.InvariantCulture) + "\""));
            parts.Add("Chord " + (ch <= s.ChordToleranceFt ? "MATCH" : "differs " + Ft(ch)));
            review = r > s.RadiusToleranceFt || l > s.ArcToleranceFt || d > s.DeltaToleranceSeconds || ch > s.ChordToleranceFt;
            if (r > s.RadiusToleranceFt) Add(report, "RadiusMismatch", subject, QcVerdict.Review, "radius CAD " + Ft(cadCourse.Radius / upf) + " / Record " + Ft(sol.Radius / upf), tc.Call.Id, entity.Handle, figure);
            if (l > s.ArcToleranceFt) Add(report, "ArcMismatch", subject, QcVerdict.Review, "arc CAD " + Ft(cadCourse.Length / upf) + " / Record " + Ft(sol.ArcLength / upf), tc.Call.Id, entity.Handle, figure);
            if (d > s.DeltaToleranceSeconds) Add(report, "DeltaMismatch", subject, QcVerdict.Review, "delta CAD " + SurveyDirection.FormatAzimuth(cadCourse.Sweep * 180.0 / Math.PI, 0, "°") + " / Record " + SurveyDirection.FormatAzimuth(sol.DeltaDegrees, 0, "°"), tc.Call.Id, entity.Handle, figure);
            if (ch > s.ChordToleranceFt) Add(report, "ChordMismatch", subject, QcVerdict.Review, "chord CAD " + Ft(cadCourse.Start.DistanceTo(cadCourse.End) / upf) + " / Record " + Ft(sol.ChordLength / upf), tc.Call.Id, entity.Handle, figure);
            Add(report, "Curve", subject, review ? QcVerdict.Review : QcVerdict.Match, string.Join(" / ", parts.ToArray()), tc.Call.Id, entity.Handle, figure);
            foreach (var dis in sol.Disagreements)
                Add(report, "CurveMathematics", subject, QcVerdict.Review, "stated elements disagree: " + dis, tc.Call.Id, entity.Handle, figure);
        }

        private static double LargestGap(IList<Course> loop, double tolerance)
        {
            // The drawn courses may each run either way; measure the gap between consecutive courses' nearest ends.
            var worst = 0.0;
            for (var i = 0; i < loop.Count; i++)
            {
                var a = loop[i]; var b = loop[(i + 1) % loop.Count];
                var gap = Math.Min(Math.Min(a.End.DistanceTo(b.Start), a.End.DistanceTo(b.End)), Math.Min(a.Start.DistanceTo(b.Start), a.Start.DistanceTo(b.End)));
                worst = Math.Max(worst, gap);
            }
            return worst;
        }

        private static string Ft(double feet) { return feet.ToString("0.00", CultureInfo.InvariantCulture) + "'"; }

        private static void Add(RecordQcReport report, string code, string subject, string verdict, string message, string callId, string handle, string figure)
        {
            report.Items.Add(new QcItem { Code = code, Subject = subject, Verdict = verdict, Message = message, CallId = callId, Handle = handle, Figure = figure });
        }
    }
}
