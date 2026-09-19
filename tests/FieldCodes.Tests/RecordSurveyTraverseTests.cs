using FieldCodes.Easements;
using FieldCodes.RecordSurvey;

namespace FieldCodes.Tests;

/// <summary>
/// Traverse and closure from written calls: exact closure, misclosure reported and never
/// adjusted, curves inside a traverse, reversed courses, shared lot lines, and the gate that
/// stops geometry being built from an unreviewed extraction.
/// </summary>
public sealed class RecordSurveyTraverseTests
{
    internal static double Dms(int d, int m, double s) => d + m / 60.0 + s / 3600.0;

    internal static SurveyCall Line(string id, double azimuth, double feet, string figure = "Lot 1", int order = 0, CallStatus status = CallStatus.Approved)
    {
        var c = new SurveyCall { Id = id, Figure = figure, Order = order, Status = status, Confidence = 1.0 };
        c.Records.Add(new RecordValue { SourceId = "", AzimuthDegrees = azimuth, DistanceFeet = feet });
        return c;
    }

    internal static SurveyCall Curve(string id, CurveSpec spec, string figure = "Lot 1", int order = 0)
    {
        return new SurveyCall { Id = id, Kind = CallKind.Curve, Curve = spec, Figure = figure, Order = order, Status = CallStatus.Approved, Confidence = 1.0 };
    }

    private static readonly double B = Dms(89, 42, 18);

    /// <summary>A 150 x 120 lot rotated to N 89°42'18" E: closes exactly.</summary>
    internal static List<SurveyCall> Rectangle(string figure = "Lot 1")
    {
        return new List<SurveyCall>
        {
            Line("K1", B, 150.0, figure, 1),
            Line("K2", 180 - (90 - B), 120.0, figure, 2),
            Line("K3", 180 + B, 150.0, figure, 3),
            Line("K4", 360 - (90 - B), 120.0, figure, 4)
        };
    }

    [Fact]
    public void ARectangleOfWrittenCallsClosesExactlyWithItsArea()
    {
        var r = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 1" }, Rectangle(), new P2(1000, 2000), new TraverseOptions());
        Assert.True(r.Ok, string.Join("; ", r.Problems));
        Assert.Equal(4, r.Courses.Count);
        Assert.True(r.Closure.Misclosure < 1e-9);
        Assert.Null(r.Closure.Precision);
        Assert.Equal(540.0, r.Closure.Perimeter, 9);
        Assert.Equal(18000.0, r.Closure.Area, 6);
        Assert.Equal("closes exactly", TraverseBuilder.PrecisionText(r.Closure));
        Assert.Equal(1000.0, r.Vertices[0].X, 9);
        Assert.Equal(5, r.Vertices.Count);
    }

    [Fact]
    public void AMisclosureIsReportedWithItsVectorAndPrecisionAndNothingIsAdjusted()
    {
        var calls = Rectangle();
        calls[2].Records[0].DistanceFeet = 150.30;          // a 0.30' bust on the south line
        var r = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 1" }, calls, new P2(0, 0), new TraverseOptions { ClosureToleranceFeet = 0.02 });
        Assert.True(r.Ok);
        Assert.Equal(0.30, r.Closure.Misclosure, 6);
        Assert.NotNull(r.Closure.MisclosureAzimuth);
        Assert.Equal(B, r.Closure.MisclosureAzimuth!.Value, 4);     // the gap runs back along the busted course's bearing
        Assert.Equal(540.30 / 0.30, r.Closure.Precision!.Value, 4);
        Assert.StartsWith("1:1,801", TraverseBuilder.PrecisionText(r.Closure));
        Assert.Contains(r.Notes, n => n.Contains("does not close") && n.Contains("nothing was adjusted"));
        // The courses are exactly what was written.
        Assert.Equal(150.30, r.Courses[2].LengthUsedFeet, 9);
        Assert.Equal(150.30, r.Courses[2].Course.Length, 9);
    }

    [Fact]
    public void AnAlternativeReadingThatWouldCloseTheFigureIsSuggestedNotApplied()
    {
        var calls = Rectangle();
        calls[2].Records[0].DistanceFeet = 150.30;
        calls[2].Alternatives.Add(new CallAlternative { Field = "distance", Text = "150.00'", Value = 150.00, Reason = "digit confusion 3/0", Target = "", Confidence = 0.4 });
        calls[2].Alternatives.Add(new CallAlternative { Field = "distance", Text = "158.30'", Value = 158.30, Reason = "digit confusion 0/8", Target = "", Confidence = 0.4 });
        var r = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 1" }, calls, new P2(0, 0), new TraverseOptions());
        Assert.Single(r.Suggestions);
        Assert.Equal("K3", r.Suggestions[0].CallId);
        Assert.Equal(150.00, r.Suggestions[0].Alternative.Value, 6);
        Assert.True(r.Suggestions[0].MisclosureWith < 1e-6);
        Assert.Equal(150.30, calls[2].Records[0].DistanceFeet!.Value, 9);     // still as read
        Assert.Equal(0.30, r.Closure.Misclosure, 6);
    }

    [Fact]
    public void ATangentCurveInsideATraverseIsBuiltFromItsElements()
    {
        // 100' east, a 90° right turn on R=50 (tangent), then 100' south... closes with a 150 x 150 figure minus the corner.
        var calls = new List<SurveyCall>
        {
            Line("K1", 90.0, 100.0, "Lot 1", 1),
            Curve("K2", new CurveSpec { Radius = 50.0, DeltaDegrees = 90.0, Turn = "RIGHT" }, "Lot 1", 2),
            Line("K3", 180.0, 100.0, "Lot 1", 3),
            Line("K4", 270.0, 150.0, "Lot 1", 4),
            Line("K5", 0.0, 150.0, "Lot 1", 5)
        };
        var r = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 1" }, calls, new P2(0, 0), new TraverseOptions());
        Assert.True(r.Ok, string.Join("; ", r.Problems));
        Assert.True(r.Closure.Misclosure < 1e-6, r.Closure.Misclosure.ToString());
        var expectedArea = 150.0 * 150.0 - (50.0 * 50.0 - Math.PI * 50.0 * 50.0 / 4.0);
        Assert.Equal(expectedArea, r.Closure.Area, 4);
        Assert.Equal(CourseKind.Arc, r.Courses[1].Course.Kind);
        Assert.Contains(r.Courses[1].Notes, n => n.Contains("tangent to the previous course"));
        Assert.Contains(r.Courses[1].Notes, n => n.Contains("Assumed tangent"));
    }

    [Fact]
    public void ACurveThatCannotBeOrientedStopsTheTraverseWithAReason()
    {
        var calls = new List<SurveyCall> { Curve("K1", new CurveSpec { Radius = 50.0, DeltaDegrees = 90.0 }, "Lot 1", 1), Line("K2", 180.0, 100.0, "Lot 1", 2) };
        var r = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 1" }, calls, new P2(0, 0), new TraverseOptions());
        Assert.False(r.Ok);
        Assert.Contains(r.Problems, p => p.Contains("cannot be placed"));
        Assert.Null(r.Courses[0].Course);
    }

    [Fact]
    public void AReversedCallTraversesBackwardsButTheRecordIsUnchanged()
    {
        var calls = Rectangle();
        calls[1].Records[0].AzimuthDegrees = 360 - (90 - B);       // written the other way round: N 00°17'42" W
        calls[1].Reversed = true;
        var r = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 1" }, calls, new P2(0, 0), new TraverseOptions());
        Assert.True(r.Closure.Misclosure < 1e-9);
        Assert.Equal(360 - (90 - B), calls[1].Records[0].AzimuthDegrees!.Value, 9);
        Assert.Contains(r.Courses[1].Notes, n => n.Contains("reversed"));
    }

    [Fact]
    public void MeasuredValuesAreUsedWhenPreferredAndRecordValuesOtherwise()
    {
        var call = Line("K1", 90.0, 100.0);
        call.Measured.AzimuthDegrees = 90.0;
        call.Measured.DistanceFeet = 100.10;
        var measured = TraverseBuilder.Build(new SurveyFigure { Name = "F", Closed = false }, new[] { call }, new P2(0, 0), new TraverseOptions { PreferMeasured = true });
        var record = TraverseBuilder.Build(new SurveyFigure { Name = "F", Closed = false }, new[] { call }, new P2(0, 0), new TraverseOptions { PreferMeasured = false });
        Assert.Equal(100.10, measured.Courses[0].LengthUsedFeet, 9);
        Assert.Equal(ValueBasis.Measured, measured.Courses[0].Basis);
        Assert.Equal(100.00, record.Courses[0].LengthUsedFeet, 9);
        Assert.Equal(ValueBasis.Recorded, record.Courses[0].Basis);
    }

    [Fact]
    public void AMeasuredBearingIsNeverMixedWithARecordDistance()
    {
        var call = Line("K1", 90.0, 100.0);
        call.Measured.AzimuthDegrees = 91.0;                 // measured bearing only, no measured distance
        ValueBasis basis;
        var v = call.GeometryValue(true, out basis);
        Assert.Equal(ValueBasis.Recorded, basis);
        Assert.Equal(90.0, v!.AzimuthDegrees!.Value, 9);
    }

    [Fact]
    public void NothingIsBuiltFromAnUnreviewedCall()
    {
        var calls = Rectangle();
        calls[1].Status = CallStatus.NeedsReview;
        var r = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 1" }, calls, new P2(0, 0), new TraverseOptions { RequireApproved = true });
        Assert.False(r.Ok);
        Assert.Empty(r.Courses);
        Assert.Contains(r.Problems, p => p.Contains("K2") && p.Contains("need review"));
    }

    [Fact]
    public void AnIncompleteCallIsAProblemNotAnInventedCourse()
    {
        var calls = Rectangle();
        calls[1].Records[0].DistanceFeet = null;          // the OCR never read a distance for this course
        var r = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 1" }, calls, new P2(0, 0), new TraverseOptions());
        Assert.False(r.Ok);
        Assert.Contains(r.Problems, p => p.Contains("K2") && p.Contains("no complete bearing and distance"));
        Assert.Null(r.Courses[1].Course);
        Assert.Null(r.Closure.Precision);
    }

    [Fact]
    public void RejectedCallsAreLeftOut()
    {
        var calls = Rectangle();
        calls.Add(Line("K9", 45.0, 10.0, "Lot 1", 5, CallStatus.Rejected));
        var r = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 1" }, calls, new P2(0, 0), new TraverseOptions());
        Assert.Equal(4, r.Courses.Count);
        Assert.True(r.Closure.Misclosure < 1e-9);
    }

    [Fact]
    public void AnOpenTraverseHasNoClosure()
    {
        var calls = new List<SurveyCall> { Line("K1", 90.0, 100.0, "Tie", 1), Line("K2", 0.0, 50.0, "Tie", 2) };
        var r = TraverseBuilder.Build(new SurveyFigure { Name = "Tie", Closed = false }, calls, new P2(0, 0), new TraverseOptions());
        Assert.True(r.Ok);
        Assert.False(r.Closure.Closed);
        Assert.Contains(r.Notes, n => n.Contains("open traverse"));
    }

    [Fact]
    public void DrawingUnitsScaleTheGeometryButNotTheReportedFeet()
    {
        var r = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 1" }, Rectangle(), new P2(0, 0), new TraverseOptions { UnitsPerFoot = 0.3048 });
        Assert.Equal(150.0 * 0.3048, r.Courses[0].Course.Length, 9);
        Assert.Equal(150.0, r.Courses[0].LengthUsedFeet, 9);
        Assert.Equal(18000.0, r.Closure.Area, 4);
    }

    // ---------------------------------------------------------------- shared lines

    [Fact]
    public void TheLineBetweenTwoLotsIsMatchedOnceAndAgreeingCallsHaveNoDiscrepancy()
    {
        var lot1 = Rectangle("Lot 1");
        var lot2 = new List<SurveyCall>
        {
            Line("K5", B, 100.0, "Lot 2", 1),
            Line("K6", 180 - (90 - B), 120.0, "Lot 2", 2),
            Line("K7", 180 + B, 100.0, "Lot 2", 3),
            Line("K8", 360 - (90 - B), 120.0, "Lot 2", 4)          // the shared line, written the other way
        };
        var r1 = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 1" }, lot1, new P2(0, 0), new TraverseOptions());
        var r2 = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 2" }, lot2, r1.Vertices[1], new TraverseOptions());
        var shared = SharedLineMatcher.Match(new[] { r1, r2 }, 0.05, 0.01, 5.0);
        Assert.Equal(7, shared.Count);
        var line = shared.Single(s => s.IsShared);
        Assert.Equal(new[] { "K2", "K8" }, line.Owners.Select(o => o.Value).ToArray());
        Assert.Empty(line.Discrepancies);
        Assert.Empty(SharedLineMatcher.NearDuplicates(new[] { r1, r2 }, 0.05));
    }

    [Fact]
    public void ASharedLineWhoseCallsDisagreeIsReported()
    {
        var lot1 = Rectangle("Lot 1");
        var lot2 = new List<SurveyCall>
        {
            Line("K5", B, 100.0, "Lot 2", 1),
            Line("K6", 180 - (90 - B), 120.0, "Lot 2", 2),
            Line("K7", 180 + B, 100.0, "Lot 2", 3),
            Line("K8", 360 - (90 - B) + 20.0 / 3600.0, 120.02, "Lot 2", 4)   // 20" and 0.02' off Lot 1's call
        };
        var r1 = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 1" }, lot1, new P2(0, 0), new TraverseOptions());
        var r2 = TraverseBuilder.Build(new SurveyFigure { Name = "Lot 2" }, lot2, r1.Vertices[1], new TraverseOptions());
        var line = SharedLineMatcher.Match(new[] { r1, r2 }, 0.05, 0.01, 5.0).Single(s => s.IsShared);
        Assert.Equal(2, line.Discrepancies.Count);
        Assert.Contains(line.Discrepancies, d => d.Contains("0.02' apart"));
        Assert.Contains(line.Discrepancies, d => d.Contains("bearings differ by 20\""));
    }

    [Fact]
    public void NearlyCoincidentLinesAreDuplicatesNotSharedLines()
    {
        var a = TraverseBuilder.Build(new SurveyFigure { Name = "A", Closed = false }, new[] { Line("K1", 90.0, 100.0, "A", 1) }, new P2(0, 0), new TraverseOptions());
        var b = TraverseBuilder.Build(new SurveyFigure { Name = "B", Closed = false }, new[] { Line("K2", 90.0, 100.0, "B", 1) }, new P2(0, 0.3), new TraverseOptions());
        Assert.Empty(SharedLineMatcher.Match(new[] { a, b }, 0.05, 0.01, 5.0).Where(s => s.IsShared));
        var notes = SharedLineMatcher.NearDuplicates(new[] { a, b }, 0.05);
        Assert.Single(notes);
        Assert.Contains("nearly coincident", notes[0]);
    }
}
