using FieldCodes.Easements;

namespace FieldCodes.Tests;

/// <summary>
/// Trimming a strip easement to property lines. Areas are checked against
/// closed-form values, and every split must account for the whole strip: the
/// pieces always add back up to the untrimmed area.
/// </summary>
public sealed class StripTrimTests
{
    private const double Tol = EasementBuilder.DefaultTolerance;

    private static P2 P(double x, double y) => new P2(x, y);
    private static Course L(double x0, double y0, double x1, double y1) => Course.Line(P(x0, y0), P(x1, y1));
    private static IList<Course> Trim(params Course[] courses) => courses.ToList();

    private static List<Course> Strip(List<Course> route, WidthSpec width, IList<IList<Course>> trims)
    {
        var extended = StripTrim.ExtendEndsToTrims(route, width, trims, Tol);
        var built = EasementBuilder.Build(extended, width, null, null, Tol);
        Assert.True(built.Ok, string.Join("; ", built.Errors));
        return built.Boundary;
    }

    private static TrimResult Split(List<Course> strip, params IList<Course>[] trims)
    {
        var r = StripTrim.Split(strip, trims.ToList(), Tol);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(Math.Abs(Loops.SignedArea(strip)), r.Pieces.Sum(p => p.Area), 6);
        return r;
    }

    private static List<Course> Keep(TrimResult r, params int[] pieces)
    {
        var merged = StripTrim.Merge(r, pieces, Tol, out var failure);
        Assert.True(merged != null, failure);
        Assert.Empty(Loops.SelfIntersections(merged!, Tol));
        Assert.True(Loops.LargestGap(merged!) <= Tol);
        return merged!;
    }

    private static readonly List<Course> Straight = new() { L(0, 0, 100, 0) };

    // ============================================================ across

    [Fact]
    public void ATrimAcrossTheStripMakesTwoPiecesLargestFirst()
    {
        var strip = Strip(Straight, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(30, -50, 30, 50)));

        Assert.Equal(2, r.Pieces.Count);
        Assert.Equal(1400.0, r.Pieces[0].Area, 6);
        Assert.Equal(600.0, r.Pieces[1].Area, 6);
        Assert.True(r.TrimCuts[0]);

        var kept = Keep(r, 1);
        Assert.Equal(1400.0, Math.Abs(Loops.SignedArea(kept)), 6);
        Assert.Equal(4, kept.Count);
    }

    [Fact]
    public void TwoTrimsKeepTheStretchBetweenThem()
    {
        var strip = Strip(Straight, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(30, -50, 30, 50)), Trim(L(80, -50, 80, 50)));

        Assert.Equal(new[] { 1000.0, 600.0, 400.0 }, r.Pieces.Select(p => Math.Round(p.Area, 6)));
        var kept = Keep(r, 1);
        Assert.Equal(1000.0, Math.Abs(Loops.SignedArea(kept)), 6);
        Assert.Equal(1, r.PieceAt(P(55, 0))!.Number);
    }

    [Fact]
    public void KeepingNeighbouringPiecesJoinsTheSplitSidesBackIntoSingleCourses()
    {
        var strip = Strip(Straight, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(30, -50, 30, 50)), Trim(L(80, -50, 80, 50)));

        var kept = Keep(r, 1, 2);     // 0..80
        Assert.Equal(1600.0, Math.Abs(Loops.SignedArea(kept)), 6);
        Assert.Equal(4, kept.Count);
        Assert.Contains(kept, c => Math.Abs(c.Length - 80) < 1e-6);
    }

    [Fact]
    public void KeptPiecesThatDoNotTouchAreRefused()
    {
        var strip = Strip(Straight, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(30, -50, 30, 50)), Trim(L(80, -50, 80, 50)));

        Assert.Null(StripTrim.Merge(r, new[] { 2, 3 }, Tol, out var failure));
        Assert.Contains("do not touch", failure);
        Assert.Null(StripTrim.Merge(r, new int[0], Tol, out failure));
    }

    [Fact]
    public void TheMergedOutlineRunsTheSameWayAsTheStrip()
    {
        var strip = Strip(Straight, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(30, -50, 30, 50)));
        Assert.True(Loops.SignedArea(Keep(r, 1)) * Loops.SignedArea(strip) > 0);
    }

    // ========================================================= lengthwise

    [Fact]
    public void ALineAlongTheStripTrimsTheSideThatRunsIntoTheNextLot()
    {
        // The strip runs 10' each side; the neighbour's line is 6' left of the route.
        var strip = Strip(Straight, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(-50, 6, 150, 6)));

        Assert.Equal(2, r.Pieces.Count);
        var kept = Keep(r, 1);
        Assert.Equal(1600.0, Math.Abs(Loops.SignedArea(kept)), 6);
        Assert.Equal(4, kept.Count);
        Assert.All(kept, c => Assert.True(c.Start.Y <= 6 + 1e-9 && c.End.Y <= 6 + 1e-9));
    }

    [Fact]
    public void AnAngledLotLineTrimsPartOfTheWidth()
    {
        // Lot line from (0,10) to (100,-2): cuts the left side off on a taper.
        var strip = Strip(Straight, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(-100, 22, 200, -14)));
        var kept = Keep(r, 1);
        // Below y = 10 - 0.12x over 0..100, above y = -10: trapezoid 20 -> 8.
        Assert.Equal((20.0 + 8.0) / 2 * 100, Math.Abs(Loops.SignedArea(kept)), 6);
    }

    // ============================================================= ends

    [Fact]
    public void AnEndPointOnASkewedLotLineRunsTheStripFullyToTheLine()
    {
        // Ends at (100,0) on a 45 degree lot line: the square end would fall short on
        // one side; extended and trimmed, the strip meets the line exactly.
        var lot = Trim(L(80, -20, 120, 20));
        var trims = new List<IList<Course>> { lot };
        var strip = Strip(Straight, WidthSpec.Centered(20), trims);
        var r = Split(strip, lot);

        var kept = Keep(r, 1);
        Assert.Equal(2000.0, Math.Abs(Loops.SignedArea(kept)), 6);   // integral of (100 + y), y = -10..10
        Assert.Contains(kept, c => Math.Abs(c.Start.X - c.Start.Y - 100) < 1e-6 && Math.Abs(c.End.X - c.End.Y - 100) < 1e-6);
    }

    [Fact]
    public void BothEndsOnLotLinesExtendBothWays()
    {
        var start = Trim(L(-20, 20, 20, -20));      // x + y = 0 through (0,0)
        var end = Trim(L(80, -20, 120, 20));        // x - y = 100 through (100,0)
        var trims = new List<IList<Course>> { start, end };
        var strip = Strip(Straight, WidthSpec.Centered(20), trims);
        var r = Split(strip, start, end);

        var kept = Keep(r, 1);
        // Between x = -y and x = 100 + y: width 100 + 2y, integrated y = -10..10 = 2000.
        Assert.Equal(2000.0, Math.Abs(Loops.SignedArea(kept)), 6);
    }

    [Fact]
    public void EndsNotOnATrimLineStaySquare()
    {
        var trims = new List<IList<Course>> { Trim(L(50, -50, 50, 50)) };
        var extended = StripTrim.ExtendEndsToTrims(Straight, WidthSpec.Centered(20), trims, Tol);
        Assert.Equal(100.0, extended[0].Length, 9);
    }

    [Fact]
    public void AnOvershootingRouteIsTrimmedBackToTheLotLines()
    {
        // The sketch's right-hand easement: the route starts outside the top line,
        // crosses it, crosses the side lot line, and runs on to the bottom line. Trim
        // lines: the top line and the side line. The middle stretch is the easement.
        var route = new List<Course> { L(0, 120, 0, -120) };
        var top = Trim(L(-100, 100, 100, 80));
        var side = Trim(L(-100, -50, 100, -70));
        var strip = Strip(route, WidthSpec.Centered(10), new List<IList<Course>>());
        var r = Split(strip, top, side);

        Assert.Equal(3, r.Pieces.Count);
        var middle = r.PieceAt(P(0, 0))!;
        Assert.Equal(1, middle.Number);
        var kept = Keep(r, middle.Number);
        // Between y = 90 - 0.1x and y = -60 - 0.1x for x in -5..5: 150 x 10.
        Assert.Equal(1500.0, Math.Abs(Loops.SignedArea(kept)), 6);
    }

    // =========================================================== curves

    [Fact]
    public void ArcsStayExactThroughATrim()
    {
        // Quarter circle R=100, 20' wide: area 20 x 50π. A 45 degree line halves it.
        var route = new List<Course> { Course.Arc(P(100, 0), P(0, 100), P(0, 0), true) };
        var strip = Strip(route, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(0, 0, 200, 200)));

        Assert.Equal(2, r.Pieces.Count);
        Assert.Equal(500 * Math.PI, r.Pieces[0].Area, 6);
        Assert.Equal(500 * Math.PI, r.Pieces[1].Area, 6);
        var kept = Keep(r, 1);
        Assert.Equal(2, kept.Count(c => c.Kind == CourseKind.Arc));
    }

    [Fact]
    public void InsideIsExactNearAnArc()
    {
        var halfDisc = new List<Course> { Course.Arc(P(100, 0), P(-100, 0), P(0, 0), true), L(-100, 0, 100, 0) };
        Assert.True(StripTrim.Inside(halfDisc, P(0, 99.999)));
        Assert.False(StripTrim.Inside(halfDisc, P(0, 100.001)));
        Assert.True(StripTrim.Inside(halfDisc, P(70.71, 70.70)));
        Assert.False(StripTrim.Inside(halfDisc, P(0, -0.001)));
    }

    // ======================================================== reporting

    [Fact]
    public void ALineThatMissesTheStripIsReported()
    {
        var strip = Strip(Straight, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(0, 50, 100, 50)));
        Assert.Single(r.Pieces);
        Assert.False(r.TrimCuts[0]);
        Assert.Contains(r.Warnings, w => w.Contains("does not cross"));
    }

    [Fact]
    public void ALineThatStopsInsideTheStripIsReportedAndIgnored()
    {
        var strip = Strip(Straight, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(50, -50, 50, 0)));
        Assert.Single(r.Pieces);
        Assert.Contains(r.Warnings, w => w.Contains("stops inside"));
    }

    [Fact]
    public void ALineAlongTheStripEdgeCutsNothing()
    {
        var strip = Strip(Straight, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(-50, 10, 150, 10)));
        Assert.Single(r.Pieces);
        Assert.Equal(2000.0, r.Pieces[0].Area, 6);
    }

    [Fact]
    public void AStoredInsidePointFindsTheSamePieceAgain()
    {
        var strip = Strip(Straight, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(30, -50, 30, 50)), Trim(L(80, -50, 80, 50)));
        foreach (var piece in r.Pieces)
            Assert.Equal(piece.Number, r.PieceAt(piece.InsidePoint)!.Number);
    }

    [Fact]
    public void TheCenterlineInsideTheKeptAreaIsFound()
    {
        var strip = Strip(Straight, WidthSpec.Centered(20), new List<IList<Course>>());
        var r = Split(strip, Trim(L(30, -50, 30, 50)), Trim(L(80, -50, 80, 50)));
        var parts = StripTrim.PartsInside(Straight, Keep(r, 1), Tol);
        Assert.Single(parts);
        Assert.Single(parts[0]);
        Assert.Equal(30.0, parts[0][0].Start.X, 6);
        Assert.Equal(80.0, parts[0][0].End.X, 6);
    }
}

public sealed class StripTrimRegressionTests
{
    private const double Tol = EasementBuilder.DefaultTolerance;

    [Fact(Timeout = 10000)]
    public async Task SmokeDrawingUtilityEasement()
    {
        await Task.Run(() =>
        {
            var route = new List<Course>
            {
                Course.Line(new P2(6000, 5000), new P2(6200, 5000)),
                Course.FromBulge(new P2(6200, 5000), new P2(6300, 5100), Math.Tan(Math.PI / 8)),
                Course.Line(new P2(6300, 5100), new P2(6300, 5250)),
            };
            var trims = new List<IList<Course>> { new List<Course> { Course.Line(new P2(6010, 4950), new P2(5990, 5050)) } };
            var width = WidthSpec.Centered(20);
            var extended = StripTrim.ExtendEndsToTrims(route, width, trims, Tol);
            var built = EasementBuilder.Build(extended, width, null, null, Tol);
            Assert.True(built.Ok, string.Join("; ", built.Errors));
            var split = StripTrim.Split(built.Boundary, trims, Tol);
            Assert.True(split.Ok, string.Join("; ", split.Errors));
            var merged = StripTrim.Merge(split, new[] { 1 }, Tol, out var failure);
            Assert.True(merged != null, failure);
            Assert.Equal(20 * (350 + 50 * Math.PI), Math.Abs(Loops.SignedArea(merged!)), 4);
        });
    }
}

/// <summary>
/// The Parcel 3 sewer easement from Exhibit B (28052700104100): POB on the south line,
/// N52°36'35"E 60.16', N01°22'31"E 277.76' to the north line, a 15' permanent easement and a
/// 25' temporary construction easement on the same centerline, commencing 65.10' along the
/// south line at the SW corner of Parcel 3. The exhibit shows 5,069 SF for the permanent easement.
/// </summary>
public sealed class ExhibitEasementTests
{
    private const double Tol = EasementBuilder.DefaultTolerance;

    private static P2 Along(P2 from, double azimuthDegrees, double distance)
    {
        var a = azimuthDegrees * Math.PI / 180;
        return new P2(from.X + Math.Sin(a) * distance, from.Y + Math.Cos(a) * distance);
    }

    private static double Dms(int d, int m, int s) => d + m / 60.0 + s / 3600.0;

    private static readonly double LotLine = 180 - Dms(87, 55, 53);          // S87°55'53"E
    private static readonly P2 Pob = new P2(5000, 5000);
    private static readonly P2 Angle = Along(Pob, Dms(52, 36, 35), 60.16);
    private static readonly P2 Terminus = Along(Angle, Dms(1, 22, 31), 277.76);

    private static readonly List<Course> Route = new() { Course.Line(Pob, Angle), Course.Line(Angle, Terminus) };
    private static readonly IList<IList<Course>> Trims = new List<IList<Course>>
    {
        new List<Course> { Course.Line(Along(Pob, LotLine + 180, 200), Along(Pob, LotLine, 200)) },
        new List<Course> { Course.Line(Along(Terminus, LotLine + 180, 200), Along(Terminus, LotLine, 200)) },
    };

    private static (TrimResult Split, List<Course> Kept) Easement(double width, IEnumerable<P2>? keepPoints = null)
    {
        var w = WidthSpec.Centered(width);
        var built = EasementBuilder.Build(StripTrim.ExtendEndsToTrims(Route, w, Trims, Tol), w, null, null, Tol);
        Assert.True(built.Ok, string.Join("; ", built.Errors));
        var split = StripTrim.Split(built.Boundary, Trims, Tol);
        Assert.True(split.Ok, string.Join("; ", split.Errors));
        var keep = keepPoints == null ? new List<int> { split.PieceAt(Along(Pob, Dms(52, 36, 35), 30))!.Number }
                                      : keepPoints.Select(p => split.PieceAt(p)!.Number).Distinct().ToList();
        var kept = StripTrim.Merge(split, keep, Tol, out var failure);
        Assert.True(kept != null, failure);
        return (split, kept!);
    }

    [Fact]
    public void ThePermanentEasementMatchesTheExhibitArea()
    {
        var (_, kept) = Easement(15);
        Assert.Equal(15 * (60.16 + 277.76), Math.Abs(Loops.SignedArea(kept)), 3);
        Assert.Equal(5069, Math.Round(Math.Abs(Loops.SignedArea(kept))));
    }

    [Fact]
    public void TheTemporaryEasementKeepsWhatSurroundsTheKeptPermanentPieces()
    {
        var (split, _) = Easement(15);
        var permanentKept = split.Pieces.Where(p => p == split.PieceAt(Along(Pob, Dms(52, 36, 35), 30))).Select(p => p.InsidePoint);
        var (_, temporary) = Easement(25, permanentKept);
        Assert.Equal(25 * (60.16 + 277.76), Math.Abs(Loops.SignedArea(temporary)), 3);
    }

    [Fact]
    public void TheCenterlineRunsFromTheSouthLineToTheNorthLine()
    {
        var (_, kept) = Easement(15);
        var parts = StripTrim.PartsInside(Route, kept, Tol);
        Assert.Single(parts);
        Assert.True(parts[0][0].Start.DistanceTo(Pob) < 1e-6);
        Assert.True(parts[0][^1].End.DistanceTo(Terminus) < 1e-6);
    }

    [Fact]
    public void TheCommencementTieReadsAlongTheSouthLine()
    {
        var poc = Along(Pob, LotLine + 180, 65.10);          // SW corner of Parcel 3
        var tie = EasementAnnotation.Describe(Course.Line(poc, Pob));
        Assert.Equal(65.10, tie.Length, 6);
        Assert.Equal("S87%%d55'53\"E", EasementAnnotation.Bearing(tie.AzimuthDegrees, new FieldCodes.Settings.EasementSettings(), "%%d"));
    }
}
