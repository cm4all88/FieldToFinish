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

    // ---------------------------------------------------------------- merging passes

    [Fact]
    public void TheReadingMadeOfPlatWordsWinsTheSpotOverAnUpsideDownReadOfTheSameConfidence()
    {
        var right = new DocumentLine("INCH = 100 FEET", new PageBox(5741, 2691, 49, 515, -90), 0.63) { PassRotationDegrees = 270 };
        var upside = new DocumentLine("1334 001 =HONI", new PageBox(5741, 2691, 49, 515, 90), 0.66) { PassRotationDegrees = 90 };
        var merged = PageGeometry.Merge(new[] { upside, right });
        var kept = Assert.Single(merged);
        Assert.Equal("INCH = 100 FEET", kept.Text);
        Assert.Contains(kept.Words, w => w.Text.StartsWith("[alt]") && w.Text.Contains("HONI"));

        // With no words on either side, confidence decides as before.
        var a = new DocumentLine("N 89°42'18\" E", new PageBox(100, 100, 300, 30), 0.9) { PassRotationDegrees = 0 };
        var b = new DocumentLine("3 .81,Z7.68 N", new PageBox(100, 100, 300, 30), 0.7) { PassRotationDegrees = 180 };
        Assert.Equal("N 89°42'18\" E", Assert.Single(PageGeometry.Merge(new[] { b, a })).Text);
    }

    [Fact]
    public void ThePlatNameIsTheTallestLetteringNotAHeadingWord()
    {
        // A sideways sheet: the name is 278 px tall lettering read at the 270° pass; DEDICATION and
        // the certificates are ordinary headings.
        var p = CallExtractor.Extract(Doc(
            new DocumentLine("FLYING ACRES", new PageBox(10373, 3145, 278, 2638, -90), 0.26),
            new DocumentLine("DEDICATION", new PageBox(4500, 5200, 60, 700, -90), 0.9),
            new DocumentLine("RECORDING CERTIFICATE", new PageBox(8545, 4828, 62, 2090, -90), 0.5),
            new DocumentLine("KING COUNTY, WASHINGTON", new PageBox(9800, 3100, 120, 2500, -90), 0.8),
            new DocumentLine("SHEET 1 OF 2", new PageBox(438, 7326, 60, 950, -90), 0.5),
            new DocumentLine("N 89°42'18\" E 150.00'", new PageBox(400, 900, 500, 46), 0.9)), new ExtractionOptions());
        Assert.Equal("FLYING ACRES", p.Document.Title);
        Assert.Equal("Subdivision Plat", p.Document.SurveyType);
    }

    [Fact]
    public void TheTitleIsTheBigShortLineNotTheDescriptionParagraph()
    {
        var p = Extract(
            L("This plat of WALDHEIM ACRES Addition to King County, Washington comprises Tract 201 of Lake Morton Tracts as recorded in Volume 15 of Plats, page 22, described as follows", 400, 3000, 0.9, 0, 120),
            L("PLAT OF WALDHEIM ACRES", 1800, 300, 0.9, 0, 90));
        Assert.Equal("PLAT OF WALDHEIM ACRES", p.Document.Title);
    }

    // ---------------------------------------------------------------- sheet and recording number

    [Fact]
    public void ASheetNumberReadWithABarAndABareRecordingNumberAreStillRead()
    {
        var p = Extract(
            L("SHEET | OF 2", 438, 7326, 0.5, -90),
            L("RECORDING CERTIFICATE", 8545, 4828, 0.5, -90),
            L("6683854", 8776, 6253, 0.5, -90),
            L("N 89°42'18\" E 150.00'", 400, 900));
        Assert.Equal("1 OF 2", p.Document.Sheet);
        Assert.Equal("6683854", p.Document.RecordingNumber);
        // The recording number is not a distance and not a lot number.
        Assert.Single(p.Calls);
        Assert.DoesNotContain(p.Annotations, a => a.Text == "6683854");
    }

    // ---------------------------------------------------------------- scale

    [Fact]
    public void AScaleReadAsTwoLinesIsStillTheScale()
    {
        var p = Extract(L("SCALE", 1200, 3000), L("1 INCH = 100 FEET", 1150, 3040));
        Assert.Equal(100.0, p.Document.ScaleFeetPerInch);

        var lookAlike = Extract(L("SCALE", 1200, 3000), L("lOO FEET", 1190, 3040), L("N 89°42'18\" E 150.00'", 400, 900));
        Assert.Equal(100.0, lookAlike.Document.ScaleFeetPerInch);

        // The 1 read as a bar on the word's line, the value after a lost "=".
        var barInline = CallExtractor.Extract(Doc(
            new DocumentLine("SCALE | INCH", new PageBox(3310, 2460, 636, 64), 0.73),
            new DocumentLine("lOO FEET", new PageBox(4070, 2462, 436, 60), 0.91)), new ExtractionOptions());
        Assert.Equal(100.0, barInline.Document.ScaleFeetPerInch);

        // The 1 read as a bar, the whole statement on one (rejoined, sideways) line.
        var bar = CallExtractor.Extract(Doc(new DocumentLine("SCALE | INCH = 100 FEET", new PageBox(5735, 2346, 55, 860, -90), 0.15)), new ExtractionOptions());
        Assert.Equal(100.0, bar.Document.ScaleFeetPerInch);

        // The "=" was not read, leaving a gap on the same line: still the scale.
        var inline = CallExtractor.Extract(Doc(
            new DocumentLine("SCALE I INCH", new PageBox(2483, 1845, 477, 48), 0.53),
            new DocumentLine("lOO FEET", new PageBox(3053, 1847, 327, 45), 0.92)), new ExtractionOptions());
        Assert.Equal(100.0, inline.Document.ScaleFeetPerInch);

        // The value line must be near the word: a stray "50'" across the sheet is a distance, not the scale.
        var far = Extract(L("SCALE", 1200, 3000), L("50'", 300, 300));
        Assert.Null(far.Document.ScaleFeetPerInch);
    }

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
    public void FragmentsOfOnePrintedLineAreJoinedInReadingOrderWhicheverWayTheSheetWasScanned()
    {
        // Horizontal: a wide gap split the label into two reads on the same baseline.
        var horizontal = PageGeometry.JoinFragments(new[] { L("150.00'", 930, 900), L("N 89°42'18\" E", 700, 900) });
        var h = Assert.Single(horizontal);
        Assert.Equal("N 89°42'18\" E 150.00'", h.Text);
        Assert.Equal(700, h.Box.X);

        // Sideways sheet read at the 270° pass (rot -90): the text runs down the page, so the
        // pieces are ordered by Y and the box is thin in X.
        var a = new DocumentLine("Beginning at the N.E. Corner of said Section", new PageBox(5304, 505, 57, 1120, -90), 0.9);
        var b = new DocumentLine("18; thence S 1°22'58\"", new PageBox(5321, 1656, 55, 509, -90), 0.9);
        var c = new DocumentLine("W along the east line thereof 970'; thence N 88°", new PageBox(5331, 2169, 62, 1251, -90), 0.9);
        var sideways = PageGeometry.JoinFragments(new[] { c, a, b });
        var one = Assert.Single(sideways);
        Assert.StartsWith("Beginning at the N.E. Corner of said Section 18; thence S 1°22'58\" W along", one.Text);
        Assert.Equal(-90, one.Box.RotationDegrees);

        // Far apart along the text, or turned differently: left alone.
        Assert.Equal(2, PageGeometry.JoinFragments(new[] { L("LOT 1", 500, 900), L("LOT 2", 1500, 900) }).Count);
        Assert.Equal(2, PageGeometry.JoinFragments(new[] { L("N 89°42'18\" E", 700, 900), L("150.00'", 1000, 900, 0.9, 90) }).Count);
    }

    [Fact]
    public void ASidewaysDescriptionIsJoinedIntoAParagraphAdvancingAcrossThePage()
    {
        // rot -90 text: each printed line is a thin tall box, and the next line of the paragraph is
        // the next box to the left.
        var l1 = new DocumentLine("Beginning at the N.E. Corner of said Section 18; thence S 1°22'58\" W along", new PageBox(5304, 505, 57, 2100, -90), 0.9);
        var l2 = new DocumentLine("the east line thereof 970'; thence N 88°", new PageBox(5249, 508, 54, 1800, -90), 0.9);
        var l3 = new DocumentLine("26'42\"E along said line 634.3/' to the west line of Tract 202 of said", new PageBox(5190, 510, 56, 2050, -90), 0.9);
        var page = new DocumentPage { Number = 1, WidthPx = 6600, HeightPx = 5100, Dpi = 300 };
        page.Lines.AddRange(new[] { l3, l1, l2 });
        CallExtractor.JoinProse(page);
        var joined = Assert.Single(page.Lines);
        Assert.StartsWith("Beginning at the N.E. Corner of said Section 18; thence S 1°22'58\" W along the east line thereof 970'; thence N 88° 26'42\"E", joined.Text);

        var d = new DocumentText { DocumentPath = "sideways.pdf" };
        var p = new DocumentPage { Number = 1, WidthPx = 6600, HeightPx = 5100, Dpi = 300 };
        p.Lines.AddRange(new[] { l3, l1, l2 });
        d.Pages.Add(p);
        var project = CallExtractor.Extract(d, new ExtractionOptions());
        Assert.Equal(2, project.Calls.Count);
        Assert.Equal(180 + 1 + 22 / 60.0 + 58 / 3600.0, project.Calls[0].Records[0].AzimuthDegrees!.Value, 9);
        Assert.Equal(970.0, project.Calls[0].Records[0].DistanceFeet!.Value, 6);
        Assert.Equal(88 + 26 / 60.0 + 42 / 3600.0, project.Calls[1].Records[0].AzimuthDegrees!.Value, 9);
        Assert.Equal(634.31, project.Calls[1].Records[0].DistanceFeet!.Value, 6);
    }

    [Fact]
    public void AFiveDigitBareNumberAfterAProseBearingIsNotADistance()
    {
        // "300.92" with its point lost: left for the reviewer, never thirty thousand feet.
        var p = Extract(L("thence N 71°42'58\" E 30092 thence N 40°06'40\" E 120 to the point of beginning", 400, 900));
        Assert.Equal(2, p.Calls.Count);
        Assert.Null(p.Calls[0].Records[0].DistanceFeet);
        Assert.Equal(120.0, p.Calls[1].Records[0].DistanceFeet!.Value, 6);
    }

    [Fact]
    public void ABareNumberRightAfterTheBearingInProseIsItsDistance()
    {
        var p = Extract(L("thence N 40°06'40\" E 120 thence N 82°56'05\" E, 251.86; thence southerly along the line of Lot 24", 400, 900));
        Assert.Equal(2, p.Calls.Count);
        Assert.Equal(120.0, p.Calls[0].Records[0].DistanceFeet!.Value, 6);
        Assert.Contains(p.Calls[0].Records[0].DistanceSource!.RawText, s => true);
        Assert.Equal(251.86, p.Calls[1].Records[0].DistanceFeet!.Value, 6);
    }

    [Fact]
    public void TheOpenChainIsFoundWhicheverOrderTheLabelsWereRead()
    {
        var a = L("N 00°00'00\" E 100.00'", 600 - 154 - 40, 600 - 14, 0.97, 90);
        var b = L("N 90°00'00\" E 100.00'", 900 - 154, 300 - 14 - 40, 0.97);
        var c = L("S 00°00'00\" E 100.00'", 1200 - 154 + 40, 600 - 14, 0.97, -90);
        foreach (var order in new[] { new[] { a, b, c }, new[] { c, b, a }, new[] { b, a, c }, new[] { c, a, b } })
        {
            var p = Extract(order);
            var r = TraverseAssembler.Assemble(p, new AssemblyOptions { ScaleFeetPerInch = 50, Dpi = 300 });
            var chain = Assert.Single(r.Figures);
            Assert.Equal(3, chain.CallIds.Count);
            Assert.False(chain.Closed);
        }
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
    public void ADistanceWrittenBeforeItsBearingOnALabelBelongsToThatBearing()
    {
        var p = Extract(L("230.86 S 88°24'29\" E", 800, 900), L("160.00'", 800, 935));
        var c = Assert.Single(p.Calls);
        var r = Assert.Single(c.Records);
        Assert.Equal(180 - (88 + 24 / 60.0 + 29 / 3600.0), r.AzimuthDegrees!.Value, 9);
        Assert.Equal(230.86, r.DistanceFeet!.Value, 6);
    }

    [Fact]
    public void TallBorderScribbleIsNotThePlatName()
    {
        var p = CallExtractor.Extract(Doc(
            new DocumentLine("ey Ay Aw", new PageBox(10373, 3145, 278, 2638, -90), 0.36),
            new DocumentLine("DEDICATION", new PageBox(4500, 5200, 60, 700, -90), 0.9),
            new DocumentLine("KING COUNTY, WASHINGTON", new PageBox(9800, 3100, 120, 2500, -90), 0.8),
            new DocumentLine("SHEET 1 OF 2", new PageBox(438, 7326, 60, 950, -90), 0.5),
            new DocumentLine("N 89°42'18\" E 150.00'", new PageBox(400, 900, 500, 46), 0.9)), new ExtractionOptions());
        Assert.NotEqual("ey Ay Aw", p.Document.Title);
    }

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
