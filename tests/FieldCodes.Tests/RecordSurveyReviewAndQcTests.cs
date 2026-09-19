using FieldCodes.Easements;
using FieldCodes.RecordSurvey;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// The review session (what the window does, without the window), the build gate, label text
/// and placement, and FTFRECORDCHECK's comparison of the drawing with the record.
/// </summary>
public sealed class RecordSurveyReviewAndQcTests
{
    private static RecordSurveyProject Project(bool close = true)
    {
        var p = new RecordSurveyProject();
        p.Figures.Add(new SurveyFigure { Name = "Lot 1", Start = new P2(0, 0) });
        p.Calls.AddRange(RecordSurveyTraverseTests.Rectangle());
        foreach (var c in p.Calls) c.Status = CallStatus.Extracted;
        return p;
    }

    private static readonly RecordSurveySettings S = new RecordSurveySettings();

    // ---------------------------------------------------------------- review

    [Fact]
    public void TheTableShowsEveryFieldTheSpecAsksFor()
    {
        var session = new ReviewSession(Project(), S);
        var rows = session.Rows();
        Assert.Equal(4, rows.Count);
        var r = rows[0];
        Assert.Equal("Lot 1 #1 K1", r.Course);
        Assert.Equal("N 89°42'18\" E", r.Bearing);
        Assert.Equal("150.00'", r.Distance);
        Assert.Equal("Line", r.Type);
        Assert.Equal("Boundary", r.ObjectType);
        Assert.Equal(1.0, r.Confidence);
        Assert.Equal(CallStatus.Extracted, r.Status);
        Assert.StartsWith("R [Recorded]", r.RecordSource);
    }

    [Fact]
    public void ApprovingAnIncompleteCallIsRefused()
    {
        var p = Project();
        p.Calls[0].Records[0].DistanceFeet = null;
        var session = new ReviewSession(p, S);
        var outcome = session.Approve("K1");
        Assert.False(outcome.Ok);
        Assert.Contains("Nothing is invented", outcome.Message);
        Assert.Equal(CallStatus.Extracted, p.Calls[0].Status);
    }

    [Fact]
    public void EditsAreStrictlyParsedRecordedAndApproveTheCall()
    {
        var p = Project();
        var session = new ReviewSession(p, S) { Reviewer = "jane" };
        Assert.False(session.EditDistance("K1", "", "abc").Ok);
        Assert.False(session.EditBearing("K1", "", "N 95°00'00\" E").Ok);
        Assert.Equal(150.0, p.Calls[0].Records[0].DistanceFeet!.Value);          // refused edits change nothing

        var ok = session.EditDistance("K1", "", "150.30'");
        Assert.True(ok.Ok);
        Assert.Equal(150.30, p.Calls[0].Records[0].DistanceFeet!.Value, 6);
        Assert.Equal(CallStatus.Approved, p.Calls[0].Status);
        Assert.Equal(ValueBasis.Entered, p.Calls[0].Basis);
        Assert.Contains(p.Calls[0].Edits, e => e.Contains("150.00' -> 150.30'") && e.Contains("jane"));
    }

    [Fact]
    public void ChoosingAnAlternativeAppliesItAndKeepsTheOriginalReadingInTheNotes()
    {
        var p = Project();
        p.Calls[2].Confidence = 0.6;
        p.Calls[2].Status = CallStatus.NeedsReview;
        p.Calls[2].Alternatives.Add(new CallAlternative { Field = "distance", Text = "158.00'", Value = 158.0, Reason = "digit confusion 5/8", Target = "" });
        var session = new ReviewSession(p, S);
        Assert.True(session.ChooseAlternative("K3", 0).Ok);
        Assert.Equal(158.0, p.Calls[2].Records[0].DistanceFeet!.Value, 6);
        Assert.Contains(p.Calls[2].Notes, n => n.Contains("Original reading: 150.00'"));
        Assert.Empty(p.Calls[2].Alternatives);
        Assert.Equal(CallStatus.Approved, p.Calls[2].Status);
        Assert.Contains(p.Calls[2].Edits, e => e.Contains("chosen alternative"));
    }

    [Fact]
    public void CurveEditsReSolveTheCurve()
    {
        var p = new RecordSurveyProject();
        p.Calls.Add(new SurveyCall { Id = "K1", Kind = CallKind.Curve, Curve = new CurveSpec { Radius = 250 }, Status = CallStatus.NeedsReview });
        var session = new ReviewSession(p, S);
        Assert.False(session.Approve("K1").Ok);
        var outcome = session.EditCurve("K1", "DELTA", "22°55'06\"");
        Assert.True(outcome.Ok);
        Assert.Contains("solves from R and Δ", outcome.Message);
        Assert.True(session.EditCurve("K1", "TURN", "right").Ok);
        Assert.Equal("RIGHT", p.Calls[0].Curve!.Turn);
        Assert.False(session.EditCurve("K1", "TURN", "sideways").Ok);
        Assert.Equal(CallStatus.Approved, p.Calls[0].Status);
    }

    [Fact]
    public void OrderingAndAssignmentAreTheReviewersToChange()
    {
        var p = Project();
        var session = new ReviewSession(p, S);
        Assert.True(session.Move("K4", -1).Ok);
        Assert.Equal(new[] { "K1", "K2", "K4", "K3" }, p.CallsOf("Lot 1").Select(c => c.Id).ToArray());
        Assert.False(session.Move("K1", -1).Ok);
        Assert.True(session.Assign("K3", "Lot 2", 1).Ok);
        Assert.Contains(p.Figures, f => f.Name == "Lot 2");
        Assert.Equal(3, p.CallsOf("Lot 1").Count);
        Assert.Equal(new[] { 1, 2, 3 }, p.CallsOf("Lot 1").Select(c => c.Order).ToArray());
        Assert.True(session.SetReversed("K3", true).Ok);
        Assert.True(session.SetObjectType("K3", "Lot Line").Ok);
        Assert.False(session.SetObjectType("K3", "Fence").Ok);
    }

    [Fact]
    public void TheGateBlocksUnreviewedAndUnplacedFiguresAndWarnsAboutTheRest()
    {
        var p = Project();
        p.Calls[1].Status = CallStatus.NeedsReview;
        p.Calls[3].Confidence = 0.5;
        p.Figures[0].Start = null;
        p.Calls.Add(RecordSurveyTraverseTests.Line("K9", 45, 10, "", 0, CallStatus.Extracted));
        var session = new ReviewSession(p, S);
        var gate = session.Gate();
        Assert.False(gate.Ready);
        Assert.Contains(gate.Blockers, b => b.Contains("K2") && b.Contains("need review"));
        Assert.Contains(gate.Blockers, b => b.Contains("K4") && b.Contains("threshold"));
        Assert.Contains(gate.Blockers, b => b.Contains("no start point"));
        Assert.Contains(gate.Warnings, w => w.Contains("K9") && w.Contains("not assigned"));

        session.Approve("K2"); session.Approve("K4");
        session.SetFigureStart("Lot 1", new P2(0, 0));
        Assert.True(session.Gate().Ready);
        Assert.Single(session.Preview(1.0));
    }

    [Fact]
    public void ApproveAllAboveLeavesTheDoubtfulOnesAlone()
    {
        var p = Project();
        p.Calls[2].Confidence = 0.7;
        p.Calls[3].Alternatives.Add(new CallAlternative { Field = "distance", Text = "120.02'", Value = 120.02, Reason = "second OCR pass", Target = "" });
        var session = new ReviewSession(p, S);
        Assert.Equal(2, session.ApproveAllAbove(0.85));
        Assert.Equal(CallStatus.Approved, p.Calls[0].Status);
        Assert.Equal(CallStatus.Extracted, p.Calls[2].Status);
        Assert.Equal(CallStatus.Extracted, p.Calls[3].Status);
    }

    [Fact]
    public void RecordVersusMeasuredDisagreementIsNotedNeverReconciled()
    {
        var p = Project();
        p.Calls[0].Measured.AzimuthDegrees = p.Calls[0].Records[0].AzimuthDegrees;
        p.Calls[0].Measured.DistanceFeet = 151.0;
        var session = new ReviewSession(p, S);
        Assert.Contains("differ by", session.RecordVsMeasuredNote(p.Calls[0]));
        Assert.Contains("differ", session.Row(p.Calls[0]).Flags);
        Assert.Equal(150.0, p.Calls[0].Records[0].DistanceFeet!.Value);
        Assert.Equal(151.0, p.Calls[0].Measured.DistanceFeet!.Value);
    }

    // ---------------------------------------------------------------- labels

    private static List<TraverseResult> Built(RecordSurveyProject p)
    {
        foreach (var c in p.Calls) if (c.Status == CallStatus.Extracted) c.Status = CallStatus.Approved;
        return TraverseBuilder.BuildAll(p, new TraverseOptions());
    }

    [Fact]
    public void LineLabelsReadTheCourseAsWrittenAndCarryRecordAndMeasuredLines()
    {
        var p = Project();
        p.Calls[0].Measured.AzimuthDegrees = p.Calls[0].Records[0].AzimuthDegrees;
        p.Calls[0].Measured.DistanceFeet = 150.05;
        p.Calls[0].Records[0].SourceId = "R1";
        p.Calls[1].Reversed = true;
        p.Calls[1].Records[0].AzimuthDegrees = 360 - (90 - RecordSurveyTraverseTests.Dms(89, 42, 18));
        var results = Built(p);
        var plan = RecordLabelPlanner.Plan(results, null, null, new LabelPlanOptions { TextHeight = 4, Offset = 2, Settings = S });
        var k1 = plan.Labels.Single(l => l.CallId == "K1");
        Assert.Equal(new[] { "N 89°42'18\" E 150.05' (M)", "N 89°42'18\" E 150.00' (R1)" }, k1.Lines);
        var k2 = plan.Labels.Single(l => l.CallId == "K2");
        Assert.Equal("N 00°17'42\" W", k2.Lines[0]);                       // as written on the record, not as traversed
        Assert.Equal("120.00'", k2.Lines[1]);
        Assert.True(Math.Abs(k1.RotationRadians) <= Math.PI / 2 + 1e-9);   // never upside down
        Assert.Equal("outside", k1.Side);
        Assert.False(k1.Conflict);
        Assert.Contains(plan.Labels, l => l.Kind == "Lot" && l.Lines[0] == "LOT 1");
    }

    [Fact]
    public void ASharedLineIsLabelledOnce()
    {
        var p = Project();
        p.Figures.Add(new SurveyFigure { Name = "Lot 2", Start = new P2(0, 0) });
        var b = RecordSurveyTraverseTests.Dms(89, 42, 18);
        p.Calls.Add(RecordSurveyTraverseTests.Line("K5", b, 100, "Lot 2", 1));
        p.Calls.Add(RecordSurveyTraverseTests.Line("K6", 180 - (90 - b), 120, "Lot 2", 2));
        p.Calls.Add(RecordSurveyTraverseTests.Line("K7", 180 + b, 100, "Lot 2", 3));
        p.Calls.Add(RecordSurveyTraverseTests.Line("K8", 360 - (90 - b), 120, "Lot 2", 4));
        var results = Built(p);
        results[1] = TraverseBuilder.Build(p.Figures[1], p.CallsOf("Lot 2"), results[0].Vertices[1], new TraverseOptions());
        var shared = SharedLineMatcher.Match(results, 0.05, 0.01, 5);
        var plan = RecordLabelPlanner.Plan(results, shared, null, new LabelPlanOptions { TextHeight = 4, Offset = 2, Settings = S });
        Assert.DoesNotContain(plan.Labels, l => l.CallId == "K8");
        Assert.Contains(plan.Labels, l => l.CallId == "K2");
        Assert.Equal(7 + 2, plan.Labels.Count);                              // 7 course labels + 2 lot labels
    }

    [Fact]
    public void LabelsAvoidMonumentsOtherCoursesAndEachOther()
    {
        var results = Built(Project());
        var start = results[0].Vertices[0];
        var mid = results[0].Courses[0].Course.PointAt(75);
        // A big "monument" right where the outside midpoint label would sit.
        var obstacles = new List<LabelObstacle> { new LabelObstacle { X = mid.X, Y = mid.Y + 8, Radius = 6, What = "monument" } };
        var plan = RecordLabelPlanner.Plan(results, null, obstacles, new LabelPlanOptions { TextHeight = 4, Offset = 2, Settings = S });
        var k1 = plan.Labels.Single(l => l.CallId == "K1");
        Assert.False(k1.Conflict);
        Assert.NotEqual(0.5, k1.AlongRatio);                                  // moved off the midpoint
        var corners = RecordLabelPlanner.Corners(k1.X, k1.Y, k1.Width, k1.Height, k1.RotationRadians);
        Assert.False(RecordLabelPlanner.InsideBox(new P2(mid.X, mid.Y + 8), corners));
        // No label box overlaps another's.
        var boxes = plan.Labels.Select(l => RecordLabelPlanner.Corners(l.X, l.Y, l.Width, l.Height, l.RotationRadians)).ToList();
        for (var i = 0; i < boxes.Count; i++)
            for (var j = i + 1; j < boxes.Count; j++)
                Assert.False(RecordLabelPlanner.BoxesOverlap(boxes[i], boxes[j]), plan.Labels[i].CallId + " overlaps " + plan.Labels[j].CallId);
    }

    [Fact]
    public void ALabelThatCannotFitAlongItsCourseGoesToTheTableInAutoMode()
    {
        var p = Project();
        p.Calls[1].Records[0].DistanceFeet = 8.0;                            // a short course
        p.Calls[2].Records[0].DistanceFeet = 150.0;
        var results = Built(p);
        var plan = RecordLabelPlanner.Plan(results, null, null, new LabelPlanOptions { TextHeight = 4, Offset = 2, Settings = S });
        var tag = plan.Labels.Single(l => l.CallId == "K2");
        Assert.True(tag.InTable);
        Assert.Equal("L1", tag.Tag);
        Assert.Single(plan.TableRows);
        Assert.Equal("K2", plan.TableRows[0].CallId);
    }

    [Fact]
    public void CurveLabelsCarryRadiusArcDeltaAndChord()
    {
        var p = new RecordSurveyProject();
        p.Figures.Add(new SurveyFigure { Name = "F", Closed = false, Start = new P2(0, 0) });
        p.Calls.Add(RecordSurveyTraverseTests.Line("K1", 90, 100, "F", 1));
        p.Calls.Add(RecordSurveyTraverseTests.Curve("K2", new CurveSpec { Radius = 250, DeltaDegrees = 22 + 55 / 60.0 + 6 / 3600.0, Turn = "RIGHT" }, "F", 2));
        var results = TraverseBuilder.BuildAll(p, new TraverseOptions());
        var plan = RecordLabelPlanner.Plan(results, null, null, new LabelPlanOptions { TextHeight = 4, Offset = 2, Settings = S });
        var curve = plan.Labels.Single(l => l.CallId == "K2");
        Assert.Equal("R=250.00'", curve.Lines[0]);
        Assert.Equal("L=100.00'", curve.Lines[1]);
        Assert.Equal("Δ=22°55'06\"", curve.Lines[2]);
        Assert.StartsWith("CH=S 78°32'27\" E 99.33'", curve.Lines[3]);        // chord = tangent-in 90° + Δ/2
    }

    // ---------------------------------------------------------------- QC

    private static CadState CadFrom(IList<TraverseResult> results, string layer = "V-PROP-BNDY-E", bool labels = true)
    {
        var cad = new CadState();
        var n = 100;
        foreach (var r in results)
            foreach (var c in r.Courses.Where(x => x.Placed))
            {
                cad.Courses.Add(new CadCourse { Handle = (n++).ToString("X"), CallId = c.Call.Id, Figure = r.Figure, Course = c.Course, Layer = layer, ObjectType = c.Call.ObjectType });
                if (labels) cad.Labels.Add(new CadLabel { Handle = (n++).ToString("X"), CallId = c.Call.Id, Layer = "V-PROP-BNDY-TEXT-E", Style = "Survey" });
            }
        return cad;
    }

    private static StandardsResolution Standards()
    {
        var inventory = new DrawingInventory { Layers = { "V-PROP-BNDY-E", "V-PROP-BNDY-TEXT-E" }, TextStyles = { "Survey" }, CurrentTextStyle = "Standard" };
        var s = new RecordSurveySettings { TextStyle = "Survey", UseCivil3DLabels = false };
        return StandardsResolver.Resolve(s, inventory, null, new[] { "Boundary" });
    }

    [Fact]
    public void ADrawingThatMatchesTheRecordReportsMatchEverywhere()
    {
        var p = Project();
        var results = Built(p);
        var report = RecordQc.Evaluate(p, CadFrom(results), S, Standards());
        Assert.True(report.Clean, report.Text());
        Assert.Equal(4, report.Items.Count(i => i.Code == "Course" && i.Verdict == QcVerdict.Match));
        Assert.Contains(report.Items, i => i.Code == "Closure" && i.Verdict == QcVerdict.Match && i.Message!.StartsWith("0.000'"));
        Assert.Contains("Course 1: MATCH".Replace("1", "K1"), report.Text());
    }

    [Fact]
    public void AChangedLineIsReportedAsCadVersusRecord()
    {
        var p = Project();
        var results = Built(p);
        var cad = CadFrom(results);
        var k3 = cad.Courses.Single(c => c.CallId == "K3");
        k3.Course = Course.Line(k3.Course.Start, k3.Course.Start + (k3.Course.End - k3.Course.Start) * (150.36 / 150.0));
        var report = RecordQc.Evaluate(p, cad, S, Standards());
        var item = report.Items.Single(i => i.Code == "DistanceMismatch");
        Assert.Equal(QcVerdict.Review, item.Verdict);
        Assert.Equal("CAD 150.36 / Record 150.00", item.Message);
        Assert.Equal("Course K3", item.Subject);
        Assert.Equal(k3.Handle, item.Handle);
        Assert.Contains("Course K3: CAD 150.36 / Record 150.00 — REVIEW", report.Text());
        Assert.DoesNotContain(report.Items, i => i.Code == "BearingMismatch");
        Assert.Equal(150.0, p.Calls[2].Records[0].DistanceFeet!.Value);      // the record was not "fixed" to match
    }

    [Fact]
    public void ADrawnLineTheOtherWayRoundStillMatches()
    {
        var p = Project();
        var results = Built(p);
        var cad = CadFrom(results);
        cad.Courses[1].Course = cad.Courses[1].Course.Reversed();
        var report = RecordQc.Evaluate(p, cad, S, Standards());
        Assert.True(report.Clean, report.Text());
    }

    [Fact]
    public void MissingLineLabelAndLayerMismatchAreReported()
    {
        var p = Project();
        var results = Built(p);
        var cad = CadFrom(results, "0", labels: false);
        cad.Courses.RemoveAt(3);
        var report = RecordQc.Evaluate(p, cad, S, Standards());
        Assert.Contains(report.Items, i => i.Code == "MissingLine" && i.Subject == "Course K4" && i.Verdict == QcVerdict.Missing);
        Assert.Equal(4, report.Items.Count(i => i.Code == "MissingLabel"));   // the missing line has no label either
        Assert.Equal(3, report.Items.Count(i => i.Code == "LayerMismatch" && i.Message!.Contains("standard for Boundary is V-PROP-BNDY-E")));
        Assert.Contains(report.Items, i => i.Code == "OpenBoundary" || i.Code == "MissingLine");
    }

    [Fact]
    public void CurveChecksReportEachElement()
    {
        var p = new RecordSurveyProject();
        p.Figures.Add(new SurveyFigure { Name = "F", Closed = false, Start = new P2(0, 0) });
        p.Calls.Add(RecordSurveyTraverseTests.Line("K1", 90, 100, "F", 1));
        p.Calls.Add(RecordSurveyTraverseTests.Curve("K2", new CurveSpec { Radius = 250, DeltaDegrees = 22 + 55 / 60.0 + 6 / 3600.0, Turn = "RIGHT" }, "F", 2));
        var results = TraverseBuilder.BuildAll(p, new TraverseOptions());
        var cad = CadFrom(results);
        var report = RecordQc.Evaluate(p, cad, S, null);
        Assert.Contains(report.Items, i => i.Subject == "Curve K2" && i.Verdict == QcVerdict.Match && i.Message == "Radius MATCH / Arc MATCH / Delta MATCH / Chord MATCH");

        // A slightly different arc in the drawing: a wider sweep on the same centre.
        var arc = cad.Courses[1].Course;
        var wider = Course.Arc(arc.Start, arc.Center + (arc.End - arc.Center).Normalized() * arc.Radius, arc.Center, arc.CounterClockwise);
        var rotated = Course.Arc(arc.Start, RotateAbout(arc.End, arc.Center, -0.0004), arc.Center, arc.CounterClockwise);
        cad.Courses[1].Course = rotated;
        report = RecordQc.Evaluate(p, cad, S, null);
        Assert.Contains(report.Items, i => i.Code == "ArcMismatch");
        Assert.Contains(report.Items, i => i.Subject == "Curve K2" && i.Message!.StartsWith("Radius MATCH / Arc differs"));
        Assert.Equal(wider.Radius, rotated.Radius, 6);
    }

    private static P2 RotateAbout(P2 p, P2 c, double radians)
    {
        var d = p - c;
        return new P2(c.X + d.X * Math.Cos(radians) - d.Y * Math.Sin(radians), c.Y + d.X * Math.Sin(radians) + d.Y * Math.Cos(radians));
    }

    [Fact]
    public void SharedBoundariesDuplicatesAndUnresolvedCallsAreReported()
    {
        var p = Project();
        p.Figures.Add(new SurveyFigure { Name = "Lot 2", Start = new P2(0, 0) });
        var b = RecordSurveyTraverseTests.Dms(89, 42, 18);
        p.Calls.Add(RecordSurveyTraverseTests.Line("K5", b, 100, "Lot 2", 1));
        p.Calls.Add(RecordSurveyTraverseTests.Line("K6", 180 - (90 - b), 120, "Lot 2", 2));
        p.Calls.Add(RecordSurveyTraverseTests.Line("K7", 180 + b, 100, "Lot 2", 3));
        p.Calls.Add(RecordSurveyTraverseTests.Line("K8", 360 - (90 - b), 120, "Lot 2", 4));
        var first = Built(p);
        p.Figures[1].Start = first[0].Vertices[1];
        var results = Built(p);
        var cad = CadFrom(results);
        // The shared line was built once: drop Lot 2's copy from the CAD state and mark the survivor shared.
        var dup = cad.Courses.Single(c => c.CallId == "K8");
        cad.Courses.Remove(dup);
        cad.Labels.RemoveAll(l => l.CallId == "K8");
        cad.Courses.Single(c => c.CallId == "K2").SharedWith.Add("K8");
        p.Calls[0].Status = CallStatus.NeedsReview;
        var report = RecordQc.Evaluate(p, cad, S, null);
        Assert.Contains(report.Items, i => i.Code == "SharedBoundary" && i.Subject == "Shared boundary Lot 1 / Lot 2" && i.Verdict == QcVerdict.Match);
        Assert.Contains(report.Items, i => i.Code == "Unresolved" && i.CallId == "K1");
        Assert.DoesNotContain(report.Items, i => i.Code == "MissingLine");
        Assert.DoesNotContain(report.Items, i => i.Code == "DuplicateGeometry");

        cad.Courses.Add(new CadCourse { Handle = "FFF", CallId = "K8", Course = dup.Course, Layer = "V-PROP-BNDY-E" });
        report = RecordQc.Evaluate(p, cad, S, null);
        Assert.Contains(report.Items, i => i.Code == "DuplicateGeometry" && i.Message!.Contains("drawn twice"));
    }

    [Fact]
    public void ClosureSuggestionsAppearInTheReportAsInformationOnly()
    {
        var p = Project();
        p.Calls[2].Records[0].DistanceFeet = 150.30;
        p.Calls[2].Alternatives.Add(new CallAlternative { Field = "distance", Text = "150.00'", Value = 150.0, Reason = "digit confusion 3/0", Target = "" });
        var results = Built(p);
        var report = RecordQc.Evaluate(p, CadFrom(results), S, null);
        Assert.Contains(report.Items, i => i.Code == "Closure" && i.Verdict == QcVerdict.Review && i.Message!.StartsWith("0.300'"));
        Assert.Contains(report.Items, i => i.Code == "ClosureError" && i.Verdict == QcVerdict.Info && i.Message!.Contains("alternative"));
        Assert.Contains("Nothing was adjusted", report.Text());
    }

    [Fact]
    public void ConnectedFiguresArePlacedFromOnePickedStart()
    {
        var text = DocumentText.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "short-plat-sample.ocr.json")));
        var p = CallExtractor.Extract(text, new ExtractionOptions());
        var session = new ReviewSession(p, S);
        session.AutoOrder(new AssemblyOptions { ScaleFeetPerInch = 50, Dpi = 300 }, true);
        session.ApproveAllAbove(0.85);
        var notes = session.PlaceConnected("Lot 1", new P2(5000, 5000), 1.0);
        Assert.Empty(notes.Where(n => n.Contains("separately")));
        Assert.All(p.Figures, f => Assert.NotNull(f.Start));
        var results = TraverseBuilder.BuildAll(p, session.Options(1.0, true));
        var shared = SharedLineMatcher.Match(results, 0.05, 0.01, 5);
        var line = Assert.Single(shared.Where(x => x.IsShared));
        Assert.Equal(2, line.Owners.Count);
        Assert.True(line.MaxEndpointDeviation < 0.02);
    }

    // ---------------------------------------------------------------- rebuild

    [Fact]
    public void ARebuildAfterAnEditProducesNewGeometryAndKeepsTheSourceTrail()
    {
        var p = Project();
        var session = new ReviewSession(p, S);
        session.ApproveAllAbove(0.0);
        var first = TraverseBuilder.BuildAll(p, session.Options(1.0, true));
        p.Built.Add(new BuiltEntity { Handle = "A1", Role = "Line", CallId = "K1", Figure = "Lot 1", Layer = "V-PROP-BNDY-E" });
        p.BuiltUtc = DateTime.UtcNow;

        var edited = session.EditDistance("K3", "", "150.30'");
        Assert.True(edited.Ok);
        p.Revision++;
        p.Built.Clear();
        var second = TraverseBuilder.BuildAll(p, session.Options(1.0, true));

        Assert.True(first[0].Closure.Misclosure < 1e-9);
        Assert.Equal(0.30, second[0].Closure.Misclosure, 6);
        Assert.Equal(2, p.Revision);
        Assert.Contains(p.Calls[2].Edits, e => e.Contains("150.30'"));
        var back = RecordSurveyProject.FromJson(p.ToJson());
        Assert.Equal(2, back.Revision);
        Assert.Contains(back.Calls[2].Edits, e => e.Contains("150.30'"));
        Assert.Equal(ValueBasis.Entered, back.Calls[2].Basis);
        Assert.Equal(ValueBasis.Recorded, back.Calls[0].Basis);
    }
}
