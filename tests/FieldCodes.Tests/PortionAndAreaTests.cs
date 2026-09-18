using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>Portion easements (the west 10 feet of the south 50 feet) and clicked
/// metes and bounds areas, with their draft legal descriptions.</summary>
public sealed class PortionAndAreaTests
{
    private const double Tol = EasementBuilder.DefaultTolerance;
    private static readonly EasementSettings Settings = new();
    private static P2 P(double x, double y) => new(x, y);
    private static Course L(double x0, double y0, double x1, double y1) => Course.Line(P(x0, y0), P(x1, y1));

    // Lot 100 wide (x 0..100) by 200 deep (y 0..200), counter-clockwise.
    private static readonly List<Course> Lot = new() { L(0, 0, 100, 0), L(100, 0, 100, 200), L(100, 200, 0, 200), L(0, 200, 0, 0) };

    private static PortionStep Step(string side, double distance, Course line) => new() { Side = side, Distance = distance, Line = line };

    [Fact]
    public void TheSouth50FeetOfARectangularLot()
    {
        var r = PortionBuilder.Build(Lot, new[] { Step("SOUTH", 50, Lot[0]) }, Tol);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(5000, r.Area, 6);
        Assert.All(r.Loop, c => Assert.True(c.Start.Y <= 50 + 1e-9));
    }

    [Fact]
    public void TheWest10FeetOfTheSouth50Feet()
    {
        var r = PortionBuilder.Build(Lot, new[] { Step("WEST", 10, Lot[3]), Step("SOUTH", 50, Lot[0]) }, Tol);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(500, r.Area, 6);
        Assert.All(r.Loop, c => Assert.True(c.Start.X <= 10 + 1e-9 && c.Start.Y <= 50 + 1e-9));
        Assert.Equal("THE WEST 10.00 FEET OF THE SOUTH 50.00 FEET OF", PortionBuilder.Describe(new[] { Step("WEST", 10, Lot[3]), Step("SOUTH", 50, Lot[0]) }, 2));
        Assert.Equal("AS MEASURED AT RIGHT ANGLES TO THE WEST AND SOUTH LINES THEREOF",
                     PortionBuilder.MeasuredClause(new[] { Step("WEST", 10, Lot[3]), Step("SOUTH", 50, Lot[0]) }));
    }

    [Fact]
    public void DistancesAreMeasuredAtRightAnglesToASkewedLine()
    {
        // South line rises 1 in 10: the south 20 feet is a band parallel to it, 20' wide at right angles.
        var lot = new List<Course> { L(0, 0, 100, 10), L(100, 10, 100, 200), L(100, 200, 0, 200), L(0, 200, 0, 0) };
        var r = PortionBuilder.Build(lot, new[] { Step("SOUTH", 20, lot[0]) }, Tol);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        // Parallelogram between the south line and a parallel 20' away, over x = 0..100:
        // vertical height 20 * sqrt(1.01), times 100.
        Assert.Equal(100 * 20 * Math.Sqrt(1.01), r.Area, 6);
    }

    [Fact]
    public void ACurvedLotKeepsItsArc()
    {
        // The north side is a half circle; the south 50 feet stays straight, the north 20 feet is curved.
        var lot = new List<Course> { L(0, 0, 100, 0), L(100, 0, 100, 100), Course.Arc(P(100, 100), P(0, 100), P(50, 100), true), L(0, 100, 0, 0) };
        var r = PortionBuilder.Build(lot, new[] { Step("SOUTH", 50, lot[0]) }, Tol);
        Assert.True(r.Ok);
        Assert.Equal(5000, r.Area, 6);
        var west = PortionBuilder.Build(lot, new[] { Step("WEST", 10, lot[3]) }, Tol);
        Assert.True(west.Ok);
        Assert.Contains(west.Loop, c => c.Kind == CourseKind.Arc);
    }

    [Fact]
    public void AShallowLotIsReportedWhenTheDistanceIsDeeperThanTheLot()
    {
        var r = PortionBuilder.Build(Lot, new[] { Step("SOUTH", 250, Lot[0]) }, Tol);
        Assert.True(r.Ok);
        Assert.Equal(20000, r.Area, 6);
        Assert.Contains(r.Warnings, w => w.Contains("not 250.00 feet deep"));
    }

    [Fact]
    public void ACurvedLineCannotBeMeasuredFrom()
    {
        var r = PortionBuilder.Build(Lot, new[] { Step("NORTH", 10, Course.Arc(P(100, 200), P(0, 200), P(50, 200), true)) }, Tol);
        Assert.False(r.Ok);
    }

    [Fact]
    public void TheSideNameComesFromWhichWayTheLineFaces()
    {
        Assert.Equal("SOUTH", PortionBuilder.SideOf(Lot, Lot[0], Tol));
        Assert.Equal("EAST", PortionBuilder.SideOf(Lot, Lot[1], Tol));
        Assert.Equal("NORTH", PortionBuilder.SideOf(Lot, Lot[2], Tol));
        Assert.Equal("WEST", PortionBuilder.SideOf(Lot, Lot[3], Tol));
    }

    // ================================================================ areas

    [Fact]
    public void FollowingAClosedLotTakesTheShorterWayRound()
    {
        var path = AreaPath.Between(Lot, P(50, 200), P(100, 150), Tol, out var problem);
        Assert.Null(problem);
        Assert.Equal(100, EasementBuilder.RouteLength(path!), 6);     // 50 east along the north line, 50 south along the east line
        Assert.True(path![0].Start.DistanceTo(P(50, 200)) < 1e-9 && path[^1].End.DistanceTo(P(100, 150)) < 1e-9);
    }

    [Fact]
    public void FollowingAnOpenLineGoesEitherDirection()
    {
        var line = new List<Course> { L(0, 0, 100, 0), L(100, 0, 200, 0) };
        var back = AreaPath.Between(line, P(150, 0), P(20, 0), Tol, out _);
        Assert.Equal(130, EasementBuilder.RouteLength(back!), 6);
        Assert.Equal(2, back!.Count);
        Assert.Null(AreaPath.Between(line, P(150, 5), P(20, 0), Tol, out var problem));
        Assert.Contains("not on that line", problem);
    }

    [Fact]
    public void CrossedSidesAreRefused()
    {
        var bowtie = new List<Course> { L(0, 0, 100, 100), L(100, 100, 100, 0), L(100, 0, 0, 100), L(0, 100, 0, 0) };
        Assert.Contains(AreaPath.Check(bowtie, Tol), p => p.Contains("cross"));
        Assert.Empty(AreaPath.Check(Lot, Tol));
    }

    [Fact]
    public void AreaLegalReadsCourseByCourseBackToTheBeginning()
    {
        var corners = new[] { P(50, 200), P(100, 150), P(50, 150) };
        var followed = AreaPath.Between(Lot, corners[0], corners[1], Tol, out _)!;
        var loop = followed.Concat(new[] { Course.Line(corners[1], corners[2]), Course.Line(corners[2], corners[0]) }).ToList();
        var area = new EasementRecord
        {
            Kind = EasementRecord.AreaKind, Purpose = "TEMPORARY CONSTRUCTION",
            RouteCourses = EasementAnnotation.Number(loop, Settings), AreaSquareFeet = Math.Abs(Loops.SignedArea(loop)),
            AnglePoints = corners.Select(c => new SelectedLocation { X = c.X, Y = c.Y }).ToList(),
            AreaSides = new List<AreaSide> { new() { FollowHandle = "2F", FollowType = "Polyline" }, new(), new() },
            PointOfCommencement = new SelectedLocation { X = 0, Y = 200 },
            CommencementTie = EasementAnnotation.Describe(Course.Line(P(0, 200), corners[0])),
        };
        var inputs = new LegalInputs
        {
            ParcelDescription = "LOT 2, TEST SHORT PLAT", CommencementCorner = "THE NORTHWEST CORNER OF SAID LOT 2", County = "SNOHOMISH",
            SideLines = new Dictionary<string, string> { { "1", "THE NORTH AND EAST LINES OF SAID LOT 2" } }
        };
        var draft = LegalDescriptionWriter.WriteArea(area, inputs, Settings, null);
        var lines = draft.Text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        Assert.Equal(new[]
        {
            LegalDescriptionWriter.DraftBanner, "EXHIBIT A", "TEMPORARY CONSTRUCTION AREA", "LEGAL DESCRIPTION",
            "THAT PORTION OF LOT 2, TEST SHORT PLAT, DESCRIBED AS FOLLOWS:",
            "COMMENCING AT THE NORTHWEST CORNER OF SAID LOT 2;",
            "THENCE NORTH 90°00'00\" EAST 50.00 FEET TO THE POINT OF BEGINNING;",
            "THENCE ALONG THE NORTH AND EAST LINES OF SAID LOT 2, NORTH 90°00'00\" EAST 50.00 FEET;",
            "THENCE SOUTH 00°00'00\" EAST 50.00 FEET;",
            "THENCE NORTH 90°00'00\" WEST 50.00 FEET;",
            "THENCE NORTH 00°00'00\" EAST 50.00 FEET TO THE POINT OF BEGINNING.",
            "SAID TEMPORARY CONSTRUCTION AREA CONTAINING 2,500 SQUARE FEET, MORE OR LESS.",
            "SITUATE IN THE COUNTY OF SNOHOMISH, STATE OF WASHINGTON.",
        }, lines);
        Assert.Empty(draft.Checks);
        Assert.Equal(new[] { 0, 0, 1, 2 }, LegalDescriptionWriter.SideOfEachCourse(area));
    }

    [Fact]
    public void PortionLegalNamesTheCallsAndHowTheyAreMeasured()
    {
        var steps = new List<PortionStep> { Step("WEST", 10, Lot[3]), Step("SOUTH", 50, Lot[0]) };
        var record = new EasementRecord { Kind = EasementRecord.PortionKind, Purpose = "SEWER", PortionSteps = steps, AreaSquareFeet = 500 };
        var draft = LegalDescriptionWriter.WritePortion(record, new LegalInputs { ParcelDescription = "LOT 2, TEST SHORT PLAT", County = "SNOHOMISH" }, Settings, null);
        Assert.Contains("THE WEST 10.00 FEET OF THE SOUTH 50.00 FEET OF LOT 2, TEST SHORT PLAT, AS MEASURED AT RIGHT ANGLES TO THE WEST AND SOUTH LINES THEREOF.", draft.Text);
        Assert.Contains("SAID SEWER EASEMENT CONTAINING 500 SQUARE FEET, MORE OR LESS.", draft.Text);
        Assert.Contains("SEWER EASEMENT\r\nLEGAL DESCRIPTION", draft.Text.Replace("\n", "\r\n").Replace("\r\r", "\r"));
    }
}
