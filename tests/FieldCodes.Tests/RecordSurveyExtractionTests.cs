using FieldCodes.Easements;
using FieldCodes.RecordSurvey;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// From positioned OCR text to survey calls: pairing bearings with distances, record versus
/// measured, curves in place and in tables, figures, references, monuments -- and the OCR
/// ambiguity handling that offers alternatives and never guesses. The realistic cases run on
/// the synthetic Washington fixtures in Samples/.
/// </summary>
public sealed class RecordSurveyExtractionTests
{
    private static DocumentLine L(string text, double x, double y, double conf = 0.97, double rot = 0.0, double h = 28)
        => new DocumentLine(text, new PageBox(x, y, text.Length * 14, h, rot), conf);

    private static DocumentText Doc(params DocumentLine[] lines)
    {
        var d = new DocumentText { DocumentPath = "test.pdf" };
        var page = new DocumentPage { Number = 1, WidthPx = 2550, HeightPx = 3300, Dpi = 300 };
        page.Lines.AddRange(lines);
        d.Pages.Add(page);
        return d;
    }

    private static RecordSurveyProject Extract(params DocumentLine[] lines) => CallExtractor.Extract(Doc(lines), new ExtractionOptions());

    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "Samples", name);

    // ---------------------------------------------------------------- curve keys that are one letter from noise

    [Fact]
    public void ABareWholeNumberAfterAOrDeltaIsNotACurveElement()
    {
        Assert.Empty(Extract(L("a 3", 800, 900)).Calls.Where(c => c.Kind == CallKind.Curve));
        Assert.Empty(Extract(L("4:4,", 800, 900)).Calls.Where(c => c.Kind == CallKind.Curve));
        Assert.Empty(Extract(L("d= 70", 800, 900)).Calls.Where(c => c.Kind == CallKind.Curve));

        var real = Extract(L("R=250.00' A=100.00'", 800, 900));
        var c = Assert.Single(real.Calls);
        Assert.Equal(100.0, c.Curve!.ArcLength!.Value, 6);
        var angle = Extract(L("R=250.00' A 22°55'06\"", 800, 900));
        Assert.Equal(22 + 55 / 60.0 + 6 / 3600.0, Assert.Single(angle.Calls).Curve!.DeltaDegrees!.Value, 9);
    }

    // ---------------------------------------------------------------- legal description prose

    [Fact]
    public void AWrappedDescriptionLineIsJoinedSoTheCallReadsAcrossTheBreak()
    {
        var page = Doc(
            L("thence S 88°", 400, 900),
            L("26'42\"E along said line 634.3/' to the west line of Tract 202 of said", 400, 935),
            L("plat; thence N 1°22'58\" W along said west line 300 feet to the", 400, 970)).Pages[0];
        CallExtractor.JoinProse(page);
        var line = Assert.Single(page.Lines);
        Assert.StartsWith("thence S 88° 26'42\"E along", line.Text);
        Assert.Equal(400, line.Box.X);
        Assert.Equal(900, line.Box.Y);

        var p = CallExtractor.Extract(Doc(
            L("thence S 88°", 400, 900),
            L("26'42\"E along said line 634.3/' to the west line of Tract 202 of said", 400, 935),
            L("plat; thence N 1°22'58\" W along said west line 300 feet to the", 400, 970)), new ExtractionOptions());
        Assert.Equal(2, p.Calls.Count);
        Assert.All(p.Calls, c => Assert.Equal("Description", c.Figure));
        var first = p.Calls[0];
        Assert.Equal(180 - (88 + 26 / 60.0 + 42 / 3600.0), first.Records[0].AzimuthDegrees!.Value, 9);
        Assert.Equal(634.31, first.Records[0].DistanceFeet!.Value, 6);
        var second = p.Calls[1];
        Assert.Equal(360 - (1 + 22 / 60.0 + 58 / 3600.0), second.Records[0].AzimuthDegrees!.Value, 9);
        Assert.Equal(300.0, second.Records[0].DistanceFeet!.Value, 6);
    }

    [Fact]
    public void BareNumbersInProseAreNotDistancesAndOnlyTheFirstDistanceBelongsToTheCourse()
    {
        var p = Extract(L("of said Section 18; thence N 4°53'45\" E along the east line 300 feet to a point 30 feet from the corner", 400, 900));
        var c = Assert.Single(p.Calls);
        Assert.Equal("Description", c.Figure);
        var r = Assert.Single(c.Records);
        Assert.Equal(4 + 53 / 60.0 + 45 / 3600.0, r.AzimuthDegrees!.Value, 9);
        Assert.Equal(300.0, r.DistanceFeet!.Value, 6);
    }

    [Fact]
    public void StackedPlanNotesAreNotJoinedIntoAParagraph()
    {
        var page = Doc(
            L("FOUND 1/2\" REBAR WITH CAP LS 12345", 400, 900),
            L("SET 5/8\" REBAR WITH CAP LS 12345", 400, 935),
            L("N 89°42'18\" E", 900, 1200),
            L("150.00'", 900, 1235)).Pages[0];
        CallExtractor.JoinProse(page);
        Assert.Equal(4, page.Lines.Count);
    }

    // ---------------------------------------------------------------- courses

    [Fact]
    public void ABearingThatCannotBeReadStillListsTheCourseWithTheReason()
    {
        // 81 seconds is not a bearing; the course is on the page all the same, with its distance below it.
        var p = Extract(L("S 1°38'8I\" E", 800, 900), L("150.00'", 800, 935));
        var c = Assert.Single(p.Calls);
        Assert.Equal(CallStatus.NeedsReview, c.Status);
        Assert.False(c.IsBuildable);
        Assert.Contains(c.Notes, n => n.Contains("could not be read") && n.Contains("Seconds must be under 60"));
        var r = Assert.Single(c.Records);
        Assert.Null(r.AzimuthDegrees);
        Assert.Equal(150.0, r.DistanceFeet!.Value, 6);
        Assert.Equal(0.0, c.Confidence);

        var rows = new ReviewSession(p, new FieldCodes.Settings.RecordSurveySettings()).Rows();
        var row = Assert.Single(rows);
        Assert.Equal("(none)", row.Bearing);
        Assert.Equal("150.00'", row.Distance);
    }

    [Fact]
    public void ABearingOnItsOwnShowsInTheReviewWithNoDistance()
    {
        var p = Extract(L("N 89°42'18\" E", 800, 900));
        var c = Assert.Single(p.Calls);
        Assert.False(c.IsBuildable);
        var row = Assert.Single(new ReviewSession(p, new FieldCodes.Settings.RecordSurveySettings()).Rows());
        Assert.Equal("N 89°42'18\" E", row.Bearing);
        Assert.Equal("(none)", row.Distance);
    }

    [Fact]
    public void ABearingAndDistanceOnOneLineBecomeOneRecordedCall()
    {
        var p = Extract(L("N 89°42'18\" E 1320.45'", 800, 900));
        var c = Assert.Single(p.Calls);
        Assert.Equal(CallKind.Line, c.Kind);
        Assert.Equal(ValueBasis.Recorded, c.Basis);
        Assert.True(c.Measured.Empty);
        var r = Assert.Single(c.Records);
        Assert.Equal(string.Empty, r.SourceId);
        Assert.Equal(89 + 42 / 60.0 + 18 / 3600.0, r.AzimuthDegrees!.Value, 9);
        Assert.Equal(1320.45, r.DistanceFeet!.Value, 6);
        Assert.Equal(CallStatus.Extracted, c.Status);
        Assert.Equal(1, c.Source!.Page);
        Assert.Equal("N 89°42'18\" E 1320.45'", c.Source.RawText);
        Assert.Equal(800, c.Source.Box.X);
        Assert.NotNull(c.PageHint);
    }

    [Fact]
    public void AStackedBearingAndDistanceArePairedByProximityAndTheConfidenceSaysSo()
    {
        var p = Extract(L("N 89°42'18\" E", 800, 900), L("1320.45'", 830, 932));
        var c = Assert.Single(p.Calls);
        Assert.True(c.Record!.Complete);
        Assert.Equal(1320.45, c.Record.DistanceFeet!.Value, 6);
        Assert.True(c.Confidence < 0.97);
        Assert.Equal(932, c.Record.DistanceSource!.Box.Y);          // the distance points at its own line
    }

    [Fact]
    public void ADistanceTooFarAwayIsNotPairedAndTheCallIsIncomplete()
    {
        var p = Extract(L("N 89°42'18\" E", 800, 900), L("1320.45'", 800, 1500));
        var c = Assert.Single(p.Calls);
        Assert.False(c.Record!.Complete);
        Assert.Equal(CallStatus.NeedsReview, c.Status);
        Assert.Contains(c.Notes, n => n.Contains("no distance"));
        Assert.False(c.IsBuildable);
    }

    [Fact]
    public void RecordAndMeasuredOnOneLineAreOneCourse()
    {
        var p = Extract(L("N 89°42'18\" E 1320.45' (R1)  N 89°42'21\" E 1320.38' (M)", 800, 900));
        var c = Assert.Single(p.Calls);
        Assert.True(c.HasRecordAndMeasured);
        Assert.Equal("R1", c.Record!.SourceId);
        Assert.Equal(1320.45, c.Record.DistanceFeet!.Value, 6);
        Assert.Equal(1320.38, c.Measured.DistanceFeet!.Value, 6);
        Assert.Equal(89 + 42 / 60.0 + 21 / 3600.0, c.Measured.AzimuthDegrees!.Value, 9);
    }

    [Fact]
    public void RecordAndMeasuredOnAdjacentLinesAreJoined()
    {
        var p = Extract(L("R1: N 89°42'18\" E 1320.45'", 800, 900), L("M: N 89°42'21\" E 1320.38'", 800, 930));
        var c = Assert.Single(p.Calls);
        Assert.True(c.HasRecordAndMeasured);
        Assert.Equal("R1", c.Record!.SourceId);
        Assert.Contains(c.Notes, n => n.Contains("joined"));
        Assert.Contains("|", c.Source!.RawText);
    }

    [Fact]
    public void OneBearingWithTwoTaggedDistancesSharesTheBearing()
    {
        var p = Extract(L("N 89°42'18\" E 1320.45' (R) 1320.38' (M)", 800, 900));
        var c = Assert.Single(p.Calls);
        Assert.Equal(1320.45, c.Record!.DistanceFeet!.Value, 6);
        Assert.Equal("R", c.Record.SourceId);
        Assert.Equal(1320.38, c.Measured.DistanceFeet!.Value, 6);
        Assert.Equal(c.Record.AzimuthDegrees, c.Measured.AzimuthDegrees);
    }

    [Fact]
    public void ACalculatedTagIsNeverARecord()
    {
        var p = Extract(L("N 45°00'00\" E 100.00' (C)", 800, 900));
        var c = Assert.Single(p.Calls);
        Assert.Equal(ValueBasis.Calculated, c.Basis);
        Assert.Equal("C", c.Record!.SourceId);
    }

    [Fact]
    public void MultipleRecordSourcesOnOneCourseAreAllKept()
    {
        var p = Extract(L("N 89°42'18\" E 1320.45' (R1)  N 89°42'20\" E 1320.40' (R2)  N 89°42'21\" E 1320.38' (M)", 800, 900));
        var c = Assert.Single(p.Calls);
        Assert.Equal(new[] { "R1", "R2" }, c.Records.Select(r => r.SourceId).ToArray());
        Assert.True(c.Measured.Complete);
    }

    // ---------------------------------------------------------------- curves

    [Fact]
    public void InPlaceCurveDataBecomesOneCurveCallThatSolves()
    {
        var p = Extract(L("R=250.00' L=100.00' Δ=22°55'06\" CB=S 78°50'09\" E CH=99.33'", 800, 900));
        var c = Assert.Single(p.Calls);
        Assert.Equal(CallKind.Curve, c.Kind);
        Assert.Equal(250.0, c.Curve!.Radius!.Value, 6);
        Assert.Equal(100.0, c.Curve.ArcLength!.Value, 6);
        Assert.Equal(99.33, c.Curve.ChordLength!.Value, 6);
        Assert.NotNull(c.Curve.ChordAzimuthDegrees);
        Assert.Equal(new[] { "R", "L", "DELTA", "CB", "CH" }, c.Curve.StatedElements);
        Assert.Equal(CallStatus.Extracted, c.Status);
    }

    [Fact]
    public void StackedCurveDataLinesMergeIntoOneCurve()
    {
        var p = Extract(L("R=250.00'", 800, 900), L("L=100.00'", 800, 930), L("Δ=22°55'06\"", 800, 960));
        var c = Assert.Single(p.Calls);
        Assert.Equal(3, c.Curve!.StatedElements.Count);
        Assert.Contains("|", c.Source!.RawText);
    }

    [Fact]
    public void CurveElementsThatDisagreeAreFlaggedForReview()
    {
        var p = Extract(L("R=250.00' L=100.50' Δ=22°55'06\"", 800, 900));
        var c = Assert.Single(p.Calls);
        Assert.Equal(CallStatus.NeedsReview, c.Status);
        Assert.Contains(c.Notes, n => n.Contains("disagree"));
        Assert.Equal(100.50, c.Curve!.ArcLength!.Value, 6);          // kept as read
    }

    [Fact]
    public void ACurveTableRowUsesTheHeaderColumns()
    {
        var p = Extract(L("CURVE TABLE", 1700, 2000), L("CURVE   RADIUS   DELTA   LENGTH   CHORD BEARING   CHORD", 1700, 2040),
                        L("C1   250.00'   22°55'06\"   100.00'   S 78°50'09\" E   99.33'", 1700, 2080), L("C1", 1200, 1200));
        var c = Assert.Single(p.Calls);
        Assert.Equal("C1", c.Curve!.Tag);
        Assert.Equal(250.0, c.Curve.Radius!.Value, 6);
        Assert.Equal(100.0, c.Curve.ArcLength!.Value, 6);
        Assert.Equal(99.33, c.Curve.ChordLength!.Value, 6);
        Assert.Contains(c.Notes, n => n.Contains("table header"));
        Assert.Equal(1200, c.PageHint!.X);                            // the C1 tag on the plan places it
    }

    [Fact]
    public void ACurveTableRowWithoutAHeaderIsIdentifiedByTheMathematics()
    {
        var p = Extract(L("C2   99.33'   22°55'06\"   250.00'   100.00'", 1700, 2080));
        var c = Assert.Single(p.Calls);
        Assert.Equal(250.0, c.Curve!.Radius!.Value, 6);
        Assert.Equal(100.0, c.Curve.ArcLength!.Value, 6);
        Assert.Contains(c.Notes, n => n.Contains("curve equations"));
    }

    [Fact]
    public void ACurveTableRowThatCannotBeSettledIsLeftForTheReviewer()
    {
        var p = Extract(L("C3   100.00'   200.00'   300.00'", 1700, 2080));
        var c = Assert.Single(p.Calls);
        Assert.Equal(CallStatus.NeedsReview, c.Status);
        Assert.Null(c.Curve!.Radius);
        Assert.Contains(c.Notes, n => n.Contains("could not be identified"));
    }

    // ---------------------------------------------------------------- everything else

    [Fact]
    public void FiguresReferencesMonumentsAndDocumentInfoAreRead()
    {
        var p = Extract(
            L("SHORT PLAT NO. SP-2019-0042", 700, 120), L("KING COUNTY, WASHINGTON", 900, 210), L("AFN 20190815000123", 2100, 120),
            L("SCALE: 1\" = 50'", 2100, 3100), L("SHEET 2 OF 3", 2200, 3200), L("BASIS OF BEARINGS: WASHINGTON STATE PLANE, NORTH ZONE", 300, 3000),
            L("R1: AFN 9807150123, VOL. 121 OF SURVEYS, PG. 45", 300, 3060), L("LOT 1", 900, 1200), L("LOT 2, BLOCK 3", 1500, 1200), L("TRACT A", 1800, 1200),
            L("FOUND 1/2\" REBAR & CAP LS 12345", 500, 800), L("SET 5/8\" REBAR & CAP LS 54321", 1400, 800), L("N.E. 8TH STREET", 1200, 2900),
            L("JANE Q. SURVEYOR, PLS NO. 54321", 1900, 3000));
        Assert.Equal("Short Plat", p.Document.SurveyType);
        Assert.Equal("KING COUNTY", p.Document.County);
        Assert.Equal("20190815000123", p.Document.RecordingNumber);
        Assert.Equal(50.0, p.Document.ScaleFeetPerInch);
        Assert.Equal("2 OF 3", p.Document.Sheet);
        Assert.StartsWith("BASIS OF BEARINGS", p.Document.BasisOfBearing);
        Assert.Contains("PLS", p.Document.Surveyor);
        var r1 = Assert.Single(p.References);
        Assert.Equal("R1", r1.Id);
        Assert.Equal("9807150123", r1.RecordingNumber);
        Assert.Equal("121", r1.Volume);
        Assert.Equal("45", r1.Page);
        Assert.Equal("SURVEYS", r1.Kind);
        Assert.Equal(new[] { "Lot 1", "Lot 2 Block 3", "Tract A" }, p.Figures.Select(f => f.Name).ToArray());
        Assert.Equal(2, p.Monuments.Count);
        Assert.Equal(MonumentStatus.Found, p.Monuments[0].Status);
        Assert.Equal(MonumentStatus.Set, p.Monuments[1].Status);
        Assert.Contains(p.Annotations, a => a.Kind == SurveyEntityKind.StreetName);
        Assert.Empty(p.Calls);                                           // "LOT 1" is not a 1-foot course
    }

    [Fact]
    public void ADistanceWrittenInMetresIsConvertedAndNoted()
    {
        var p = Extract(L("N 45°00'00\" E 30.480 M", 800, 900));
        var c = Assert.Single(p.Calls);
        Assert.Equal(30.480 * 3937.0 / 1200.0, c.Record!.DistanceFeet!.Value, 6);
        Assert.Contains(c.Notes, n => n.Contains("metres"));
    }

    // ---------------------------------------------------------------- ambiguity

    [Fact]
    public void ASecondOcrPassThatReadDifferentlyBecomesAnAlternativeAndForcesReview()
    {
        var line = L("S 89°13'28\" W 148.52'", 900, 1900, 0.95);
        line.Words.Add(new DocumentWord("[alt] S 89°13'28\" W 148.82'", line.Box, 0.9));
        var p = Extract(line);
        var c = Assert.Single(p.Calls);
        Assert.Equal(CallStatus.NeedsReview, c.Status);
        var alt = Assert.Single(c.Alternatives);
        Assert.Equal("distance", alt.Field);
        Assert.Equal(148.82, alt.Value, 6);
        Assert.Equal("second OCR pass", alt.Reason);
        Assert.Equal(148.52, c.Record!.DistanceFeet!.Value, 6);          // the primary reading stands until a person decides
    }

    [Fact]
    public void ALowConfidenceReadingNeedsReviewAndOffersDigitAlternatives()
    {
        var p = Extract(L("S 89°13'28\" W 148.52'", 900, 1900, 0.60));
        var c = Assert.Single(p.Calls);
        Assert.Equal(CallStatus.NeedsReview, c.Status);
        Assert.True(c.Confidence < 0.85);
        Assert.Equal(2, c.Alternatives.Count);
        Assert.All(c.Alternatives, a => Assert.Contains("digit confusion", a.Reason));
    }

    [Fact]
    public void AConfidentReadingIsNotSecondGuessed()
    {
        var p = Extract(L("S 89°13'28\" W 148.52'", 900, 1900, 0.98));
        var c = Assert.Single(p.Calls);
        Assert.Equal(CallStatus.Extracted, c.Status);
        Assert.Empty(c.Alternatives);
    }

    [Fact]
    public void ThreePassOcrHitsOnOneSpotMergeToTheMostConfidentWithTheOtherAsAlternative()
    {
        var hits = new List<DocumentLine>
        {
            new DocumentLine("S 89°13'28\" W 148.52'", new PageBox(900, 1900, 300, 28), 0.8),
            new DocumentLine("S 89°13'28\" W 148.82'", new PageBox(902, 1901, 298, 27), 0.7),
            new DocumentLine("S 89°13'28\" W 148.52'", new PageBox(900, 1900, 300, 28, 90), 0.75)
        };
        var merged = PageGeometry.Merge(hits);
        var only = Assert.Single(merged);
        Assert.Equal(0.8, only.Confidence);
        Assert.Single(only.Words.Where(w => w.Text.StartsWith("[alt]")));      // only the DIFFERENT reading is kept
    }

    [Fact]
    public void BoxesFromARotatedPassMapBackOntoTheUnrotatedPage()
    {
        // A 1000 x 500 page turned 90° CCW is a 500 x 1000 canvas. A word at the canvas's top left
        // came from the page's bottom left.
        double cw, ch;
        PageGeometry.RotatedCanvas(1000, 500, 90, out cw, out ch);
        Assert.Equal(500, cw); Assert.Equal(1000, ch);
        var back = PageGeometry.Unrotate(new PageBox(0, 0, 100, 20), 90, cw, ch, 1000, 500);
        Assert.Equal(0, back.X, 6);
        Assert.Equal(400, back.Y, 6);
        Assert.Equal(20, back.Width, 6);
        Assert.Equal(100, back.Height, 6);
        Assert.Equal(90, back.RotationDegrees, 6);
        var same = PageGeometry.Unrotate(new PageBox(10, 20, 30, 40), 0, 1000, 500, 1000, 500);
        Assert.Equal(10, same.X); Assert.Equal(20, same.Y);
    }

    // ---------------------------------------------------------------- metadata

    [Fact]
    public void SourceMetadataSurvivesAProjectRoundTrip()
    {
        var p = Extract(L("N 89°42'18\" E 1320.45' (R1)  N 89°42'21\" E 1320.38' (M)", 800, 900, 0.93), L("R1: AFN 9807150123", 300, 3060));
        p.Document.RecordingNumber = "20190815000123";
        var json = p.ToJson();
        var back = RecordSurveyProject.FromJson(json);
        var c = Assert.Single(back.Calls);
        Assert.Equal(p.Id, back.Id);
        Assert.Equal("20190815000123", back.Document.RecordingNumber);
        Assert.Equal(1, c.Source!.Page);
        Assert.Equal(800, c.Source.Box.X);
        Assert.Equal(0.93, c.Source.OcrConfidence, 6);
        Assert.Equal("R1", c.Record!.SourceId);
        Assert.Equal("9807150123", back.FindReference("R1")!.RecordingNumber);
        Assert.Equal(1320.38, c.Measured.DistanceFeet!.Value, 6);
        Assert.Equal(p.CreatedUtc, back.CreatedUtc);
        Assert.Contains("\"basis\": \"Recorded\"", json);
    }

    // ---------------------------------------------------------------- fixtures

    [Fact]
    public void TheShortPlatSampleExtractsAssemblesAndClosesBothLots()
    {
        var text = DocumentText.FromJson(File.ReadAllText(Sample("short-plat-sample.ocr.json")));
        var p = CallExtractor.Extract(text, new ExtractionOptions());
        Assert.Equal("Short Plat", p.Document.SurveyType);
        Assert.Equal(50.0, p.Document.ScaleFeetPerInch);
        Assert.Equal(new[] { "Lot 1", "Lot 2" }, p.Figures.Select(f => f.Name).ToArray());
        Assert.Equal(8, p.Calls.Count);                                     // 7 lines + 1 curve from the table
        Assert.Equal(2, p.Monuments.Count);                                 // the two legend entries are not corners
        Assert.Equal(2, p.Annotations.Count(a => a.Kind == SurveyEntityKind.Legend && a.Text.Contains("REBAR") || a.Text.Contains("MONUMENT AS NOTED")));
        Assert.All(p.Calls, c => Assert.Equal(CallStatus.Extracted, c.Status));

        // Order by page position: two closed loops, the shared line copied into Lot 2.
        var session = new ReviewSession(p, new FieldCodes.Settings.RecordSurveySettings());
        var assembly = session.AutoOrder(new AssemblyOptions { ScaleFeetPerInch = 50, Dpi = 300 }, true);
        Assert.Empty(assembly.Problems);
        Assert.Equal(2, assembly.Figures.Count(f => f.Closed));
        Assert.Contains(assembly.Figures, f => f.Name == "Lot 1" && f.CallIds.Count == 4);
        Assert.Contains(assembly.Figures, f => f.Name == "Lot 2" && f.CallIds.Count == 5);
        Assert.Single(p.Calls.Where(c => c.SharedWith != null));

        session.ApproveAllAbove(0.85);
        foreach (var f in p.Figures) session.SetFigureStart(f.Name, new P2(0, 0));
        var gate = session.Gate();
        Assert.True(gate.Ready, string.Join("; ", gate.Blockers));

        var results = TraverseBuilder.BuildAll(p, session.Options(1.0, true));
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Ok, r.Figure + ": " + string.Join("; ", r.Problems)));
        Assert.All(results, r => Assert.True(r.Closure.Misclosure < 0.02, r.Figure + " misclosure " + r.Closure.Misclosure));
        Assert.Equal(18000.0, results[0].Closure.Area, 2);
        Assert.Contains(results[1].Courses, c => c.Course.Kind == CourseKind.Arc);
    }

    [Fact]
    public void TheRecordOfSurveySampleKeepsRecordAndMeasuredApartAndFlagsTheDoubtfulCall()
    {
        var text = DocumentText.FromJson(File.ReadAllText(Sample("record-of-survey-sample.ocr.json")));
        var p = CallExtractor.Extract(text, new ExtractionOptions());
        Assert.Equal("Record of Survey", p.Document.SurveyType);
        Assert.Equal("202301230456", p.Document.RecordingNumber);
        Assert.Equal(2, p.References.Count);
        Assert.Equal("30", p.FindReference("R2")!.Volume);
        Assert.Equal("PLATS", p.FindReference("R2")!.Kind);

        var stacked = p.Calls.Single(c => c.Records.Any(r => r.SourceId == "R1"));
        Assert.True(stacked.HasRecordAndMeasured);
        Assert.Equal(1320.45, stacked.Record!.DistanceFeet!.Value, 6);
        Assert.Equal(1320.38, stacked.Measured.DistanceFeet!.Value, 6);

        var inline = p.Calls.Single(c => c.Records.Any(r => r.SourceId == "R2"));
        Assert.Equal(148.52, inline.Record!.DistanceFeet!.Value, 6);
        Assert.Equal(148.82, inline.Measured.DistanceFeet!.Value, 6);

        var doubtful = p.Calls.Single(c => c.Alternatives.Any(a => a.Reason == "second OCR pass"));
        Assert.Equal(CallStatus.NeedsReview, doubtful.Status);
        Assert.Equal(148.82, doubtful.Alternatives.First(a => a.Reason == "second OCR pass").Value, 6);

        var bearingOnly = p.Calls.Single(c => c.Notes.Any(n => n.Contains("no distance")));
        Assert.Equal(CallStatus.NeedsReview, bearingOnly.Status);
        Assert.False(bearingOnly.IsBuildable);
        Assert.Equal(2, p.Monuments.Count);
    }

    [Fact]
    public void TheAssemblerRefusesToOrderWithoutAScale()
    {
        var p = Extract(L("N 89°42'18\" E 1320.45'", 800, 900));
        var a = TraverseAssembler.Assemble(p, new AssemblyOptions { ScaleFeetPerInch = 0 });
        Assert.Empty(a.Figures);
        Assert.Contains(a.Problems, x => x.Contains("scale"));
    }

    [Fact]
    public void AnOpenChainIsReportedAndNoClosingCourseIsInvented()
    {
        // Three sides of a 100' square at 1"=50' (6 px/ft), corners (600,900) (600,300) (1200,300) (1200,900):
        // each label box is centred on its course's midpoint, offset a little to the outside; no fourth label anywhere.
        var p = Extract(L("N 00°00'00\" E 100.00'", 600 - 154 - 40, 600 - 14, 0.97, 90), L("N 90°00'00\" E 100.00'", 900 - 154, 300 - 14 - 40, 0.97),
                        L("S 00°00'00\" E 100.00'", 1200 - 154 + 40, 600 - 14, 0.97, -90));
        var a = TraverseAssembler.Assemble(p, new AssemblyOptions { ScaleFeetPerInch = 50, Dpi = 300 });
        var chain = Assert.Single(a.Figures);
        Assert.False(chain.Closed);
        Assert.Equal(3, chain.CallIds.Count);
        Assert.Contains(chain.Notes, n => n.Contains("nothing is added"));
        TraverseAssembler.Apply(p, a, true);
        Assert.Equal(3, p.Calls.Count);                                     // no fourth call appeared
    }
}
