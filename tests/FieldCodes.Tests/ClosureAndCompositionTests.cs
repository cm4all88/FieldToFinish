using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// CAD closure vs course (legal) closure, exclusions with cuts and interior holes, portion
/// exclusions, multiple components. Stated-course checks use the office's Exhibit B for
/// 28052700104300 (N44°26'09"E 13.62', N01°22'31"E 244.44', N52°36'35"E 83.48').
/// </summary>
public sealed class ClosureAndCompositionTests
{
    private const double Tol = EasementBuilder.DefaultTolerance;
    private static readonly EasementSettings Settings = new();
    private static P2 P(double x, double y) => new(x, y);
    private static Course L(double x0, double y0, double x1, double y1) => Course.Line(P(x0, y0), P(x1, y1));
    private static List<Course> Rect(double x0, double y0, double x1, double y1) =>
        new() { L(x0, y0, x1, y0), L(x1, y0, x1, y1), L(x1, y1, x0, y1), L(x0, y1, x0, y0) };

    private static P2 Along(P2 from, double azimuthDegrees, double distance)
    {
        var a = azimuthDegrees * Math.PI / 180;
        return new P2(from.X + Math.Sin(a) * distance, from.Y + Math.Cos(a) * distance);
    }

    private static double Dms(int d, int m, int s) => d + m / 60.0 + s / 3600.0;

    // =========================================================== CAD closure

    [Fact]
    public void AnFtfPolygonClosesByConstructionAndGetsNoPrecisionRatio()
    {
        var r = Closure.Cad(Rect(0, 0, 100, 50), Tol);
        Assert.True(r.Ok);
        Assert.True(r.ClosesByConstruction);
        Assert.Equal(0, r.Gap);
        Assert.Equal(300, r.Perimeter, 9);
        Assert.Equal(5000, r.Area, 9);
    }

    [Fact]
    public void CadGapsSelfIntersectionsAndOverlapsAreReported()
    {
        var gap = Rect(0, 0, 100, 50);
        gap[3] = L(0, 50, 0, 0.5);
        Assert.Contains(Closure.Cad(gap, Tol).Problems, p => p.Contains("gap of 0.500'"));

        var bowtie = new List<Course> { L(0, 0, 100, 100), L(100, 100, 100, 0), L(100, 0, 0, 100), L(0, 100, 0, 0) };
        Assert.NotEmpty(Closure.Cad(bowtie, Tol).SelfIntersections);

        var doubled = new List<Course> { L(0, 0, 100, 0), L(100, 0, 50, 0), L(50, 0, 50, 50), L(50, 50, 0, 0) };
        Assert.True(Closure.Cad(doubled, Tol).Overlaps > 0);
    }

    // ======================================================= course closure

    [Fact]
    public void StatedExhibitCoursesReproduceTheCadCenterline()
    {
        var pob = P(5000, 5000);
        var a1 = Along(pob, Dms(44, 26, 9), 13.62);
        var a2 = Along(a1, Dms(1, 22, 31), 244.44);
        var terminus = Along(a2, Dms(52, 36, 35), 83.48);
        var courses = EasementAnnotation.Number(new[] { Course.Line(pob, a1), Course.Line(a1, a2), Course.Line(a2, terminus) }, Settings);

        var r = Closure.Courses(pob, courses, false, Settings, Settings.ReproductionToleranceFt);
        Assert.True(r.Reproduces, string.Join("; ", r.Problems));
        Assert.True(r.MaxDeviation < 0.001);
        Assert.Contains("not a closed figure", Closure.PrecisionText(r));
    }

    [Fact]
    public void RoundedStatedCoursesThatDriftAreFlaggedNotAdjusted()
    {
        // A 5,000' course at N00°00'00.4"E states as N00°00'00"E: the end lands 0.0097' off. At a
        // 0.005' tolerance that must be flagged, and the courses are not "fixed".
        var s = new EasementSettings();
        var start = P(0, 0);
        var end = Along(start, 0.4 / 3600, 5000);
        var courses = EasementAnnotation.Number(new[] { Course.Line(start, end) }, s);
        var r = Closure.Courses(start, courses, false, s, 0.005);
        Assert.False(r.Reproduces);
        Assert.Contains(r.Problems, p => p.Contains("do not reproduce"));
        Assert.Equal(0.4 / 3600, courses[0].AzimuthDegrees, 9);     // the record is untouched
    }

    [Fact]
    public void AClosedAreaTraversesBackToItsBeginningWithARealPrecision()
    {
        // A lot with an irrational diagonal: the stated (rounded) courses misclose slightly.
        var loop = new List<Course> { L(0, 0, 123.4567, 0), L(123.4567, 0, 77.7777, 88.8888), L(77.7777, 88.8888, 0, 0) };
        var courses = EasementAnnotation.Number(loop, Settings);
        var r = Closure.Courses(P(0, 0), courses, true, Settings, Settings.ReproductionToleranceFt);
        Assert.True(r.ClosedFigure);
        Assert.True(r.Misclosure > 0 && r.Misclosure < 0.02);
        Assert.NotNull(r.Precision);
        Assert.True(r.Precision > 10000);
        Assert.StartsWith("1:", Closure.PrecisionText(r));
        Assert.True(r.Reproduces);
    }

    [Fact]
    public void ARectangleOfRoundCoursesClosesExactlyAtStatedPrecision()
    {
        var courses = EasementAnnotation.Number(Rect(0, 0, 100, 50), Settings);
        var r = Closure.Courses(P(0, 0), courses, true, Settings, Settings.ReproductionToleranceFt);
        Assert.Null(r.Precision);
        Assert.Equal("closes exactly at the stated precision", Closure.PrecisionText(r));
    }

    [Fact]
    public void TangentAndNonTangentCurvesTraverseFromTheirStatedValues()
    {
        var loop = new List<Course>
        {
            L(0, 0, 100, 0),
            Course.Arc(P(100, 0), P(200, 100), P(100, 100), true),     // tangent, left
            L(200, 100, 0, 100), L(0, 100, 0, 0)
        };
        var courses = EasementAnnotation.Number(loop, Settings);
        var r = Closure.Courses(P(0, 0), courses, true, Settings, Settings.ReproductionToleranceFt);
        Assert.True(r.Reproduces, string.Join("; ", r.Problems));

        var nonTangent = EasementAnnotation.Number(new[] { Course.Arc(P(100, 0), P(200, 100), P(100, 100), true) }, Settings);
        var n = Closure.Courses(P(100, 0), nonTangent, false, Settings, Settings.ReproductionToleranceFt);
        Assert.True(n.Reproduces, string.Join("; ", n.Problems));
        Assert.Contains(n.Notes, x => x.Contains("non-tangent"));
    }

    [Fact]
    public void ACourseRecordThatDisagreesWithItsGeometryIsCaught()
    {
        var courses = EasementAnnotation.Number(Rect(0, 0, 100, 50), Settings);
        courses[1].Length = 49.5;      // a stated distance that no longer matches the CAD course
        var r = Closure.Courses(P(0, 0), courses, true, Settings, Settings.ReproductionToleranceFt);
        Assert.False(r.Reproduces);
        Assert.True(r.Misclosure > 0.4);
    }

    // =========================================================== exclusions

    [Fact]
    public void AnExclusionAcrossTheEasementCutsItsOutline()
    {
        var region = new RegionShape(Rect(0, 0, 100, 200));
        var r = RegionBuilder.Exclude(region, Rect(-10, 180, 110, 210), Tol);   // "except the north 20 feet"
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal("CUT", r.Effect);
        Assert.Equal(18000, r.Region.Area, 6);
        Assert.Equal(2000, r.Removed, 6);
        Assert.Empty(r.Region.Holes);
    }

    [Fact]
    public void AnExclusionInsideTheEasementLeavesAnInteriorHole()
    {
        var region = new RegionShape(Rect(0, 0, 100, 200));
        var r = RegionBuilder.Exclude(region, Rect(20, 50, 50, 100), Tol);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal("HOLE", r.Effect);
        Assert.Single(r.Region.Holes);
        Assert.Equal(20000 - 1500, r.Region.Area, 6);
        Assert.False(r.Region.Contains(P(30, 70)));
        Assert.True(r.Region.Contains(P(80, 70)));
    }

    [Fact]
    public void AnExclusionOfAnExistingEasementStripCutsAcross()
    {
        // "EXCEPT THAT PORTION LYING WITHIN THE EXISTING 15 FOOT UTILITY EASEMENT" running east-west.
        var region = new RegionShape(Rect(0, 0, 100, 200));
        var existing = Rect(-50, 100, 150, 115);
        var r = RegionBuilder.Exclude(region, existing, Tol);
        Assert.False(r.Ok);     // it splits the easement in two: a surveyor decision, not resolved
        Assert.Contains(r.Errors, e => e.Contains("separate parts"));
    }

    [Fact]
    public void ExclusionsThatDoNotTouchOrCoverEverythingAreReported()
    {
        var region = new RegionShape(Rect(0, 0, 100, 200));
        Assert.Contains(RegionBuilder.Exclude(region, Rect(300, 0, 400, 10), Tol).Warnings, w => w.Contains("does not touch"));
        Assert.Contains(RegionBuilder.Exclude(region, Rect(-10, -10, 110, 210), Tol).Errors, e => e.Contains("whole easement"));
    }

    [Fact]
    public void OverlappingExclusionsAreNotResolvedAutomatically()
    {
        var region = new RegionShape(Rect(0, 0, 100, 200));
        var first = RegionBuilder.Exclude(region, Rect(20, 50, 50, 100), Tol);
        var second = RegionBuilder.Exclude(first.Region, Rect(40, 60, 70, 90), Tol);
        Assert.Contains(second.Errors, e => e.Contains("overlaps an earlier exclusion"));
    }

    [Fact]
    public void APortionExclusionUsesThePortionEngine()
    {
        // The west 10 feet of the south 50 feet of a 100 x 200 lot, except the south 5 feet thereof.
        var lot = Rect(0, 0, 100, 200);
        var portion = PortionBuilder.Build(lot, new[]
        {
            new PortionStep { Side = "WEST", Distance = 10, Line = lot[3] },
            new PortionStep { Side = "SOUTH", Distance = 50, Line = lot[0] }
        }, Tol);
        var except = PortionBuilder.Build(portion.Loop, new[] { new PortionStep { Side = "SOUTH", Distance = 5, Line = lot[0] } }, Tol);
        Assert.True(except.Ok, string.Join("; ", except.Errors));

        var r = RegionBuilder.Exclude(new RegionShape(portion.Loop), except.Loop, Tol);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(450, r.Region.Area, 6);
    }

    [Fact]
    public void ArcsSurviveAnExclusion()
    {
        var region = new RegionShape(new List<Course> { L(0, 0, 100, 0), L(100, 0, 100, 100), Course.Arc(P(100, 100), P(0, 100), P(50, 100), true), L(0, 100, 0, 0) });
        var before = region.Area;
        var r = RegionBuilder.Exclude(region, Rect(-10, -10, 110, 20), Tol);
        Assert.True(r.Ok);
        Assert.Equal(before - 2000, r.Region.Area, 6);
        Assert.Contains(r.Region.Outer, c => c.Kind == CourseKind.Arc);
    }

    // =========================================================== components

    [Fact]
    public void ComponentOverlapIsMeasuredSoATotalCannotSilentlyDoubleCount()
    {
        var a = new RegionShape(Rect(0, 0, 10, 50));
        var b = new RegionShape(Rect(0, 0, 100, 15));
        Assert.Equal(150, RegionBuilder.Overlap(a, b, Tol), 6);
        Assert.Equal(0, RegionBuilder.Overlap(a, new RegionShape(Rect(10, 0, 20, 50)), Tol), 6);    // touching only
    }

    [Fact]
    public void ComponentLabelsRunBCD()
    {
        var list = new List<EasementComponent>();
        Assert.Equal("B", RegionBuilder.NextLabel(list));
        list.Add(new EasementComponent { Label = "B" });
        Assert.Equal("C", RegionBuilder.NextLabel(list));
    }
}
