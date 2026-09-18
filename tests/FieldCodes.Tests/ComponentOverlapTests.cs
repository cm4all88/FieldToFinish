using FieldCodes.Easements;
using FieldCodes.Exhibits;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>Overlapping components: the SUM OF COMPONENT AREAS keeps each component's own area,
/// the TOTAL PHYSICAL AREA counts shared ground once, and nothing decides which the legal states.</summary>
public sealed class ComponentOverlapTests
{
    private const double Tol = EasementBuilder.DefaultTolerance;
    private static P2 P(double x, double y) => new(x, y);
    private static List<Course> Box(double x0, double y0, double x1, double y1) =>
        new() { Course.Line(P(x0, y0), P(x1, y0)), Course.Line(P(x1, y0), P(x1, y1)), Course.Line(P(x1, y1), P(x0, y1)), Course.Line(P(x0, y1), P(x0, y0)) };

    [Fact]
    public void TheExampleFromTheBrief()
    {
        // A 100 x 50 = 5,000; B 60 x 50 = 3,000; they share 10 x 50 = 500.
        var a = new RegionShape(Box(0, 0, 100, 50));
        var b = new RegionShape(Box(90, 0, 150, 50));
        Assert.Equal(500, RegionBuilder.IntersectionArea(new[] { a, b }, Tol)!.Value, 6);
        Assert.Equal(7500, RegionBuilder.UnionArea(new[] { a, b }, Tol)!.Value, 6);
        Assert.Equal(8000, a.Area + b.Area, 6);
    }

    [Fact]
    public void ComponentsThatOnlyTouchShareNothing()
    {
        var a = new RegionShape(Box(0, 0, 100, 50));
        var b = new RegionShape(Box(100, 0, 150, 50));
        Assert.Equal(7500, RegionBuilder.UnionArea(new[] { a, b }, Tol)!.Value, 6);
    }

    [Fact]
    public void ThreeComponentsOverlappingAtOnePlaceAreCountedOnce()
    {
        var a = new RegionShape(Box(0, 0, 100, 100));
        var b = new RegionShape(Box(50, 0, 150, 100));
        var c = new RegionShape(Box(25, 50, 125, 150));
        // Union of the three rectangles, worked out by hand:
        // A u B = 150 x 100 = 15,000; C adds 100 x 50 above y = 100 = 5,000.
        Assert.Equal(20000, RegionBuilder.UnionArea(new[] { a, b, c }, Tol)!.Value, 6);
    }

    [Fact]
    public void AHoleIsNotCountedAsCoveredGround()
    {
        var a = new RegionShape(Box(0, 0, 100, 100));
        a.Holes.Add(Box(10, 10, 30, 30));
        var b = new RegionShape(Box(50, 0, 150, 100));
        // A = 10,000 - 400; B = 10,000; shared 50 x 100 = 5,000 (the hole is not in the shared part).
        Assert.Equal(9600 + 10000 - 5000, RegionBuilder.UnionArea(new[] { a, b }, Tol)!.Value, 6);
    }

    [Fact]
    public void AnArcComponentStaysExact()
    {
        var half = new List<Course> { Course.Line(P(0, 0), P(100, 0)), Course.Arc(P(100, 0), P(0, 0), P(50, 0), true) };
        var a = new RegionShape(half);
        var b = new RegionShape(Box(0, -20, 50, 60));
        var union = RegionBuilder.UnionArea(new[] { a, b }, Tol)!.Value;
        var quarter = Math.PI * 50 * 50 / 4;
        // Half disc (radius 50) plus the box, minus their shared quarter-ish piece: box part above y=0 inside the disc.
        var shared = RegionBuilder.IntersectionArea(new[] { a, b }, Tol)!.Value;
        Assert.Equal(a.Area + b.Area - shared, union, 6);
        Assert.InRange(shared, quarter * 0.9, 50 * 50);
    }

    [Fact]
    public void AComponentInsideAnotherWithHolesIsNotGuessed()
    {
        var a = new RegionShape(Box(0, 0, 100, 100));
        a.Holes.Add(Box(10, 10, 20, 20));
        var b = new RegionShape(Box(40, 40, 60, 60));
        b.Holes.Add(Box(45, 45, 50, 50));
        // B floats inside A: not resolved exactly here, so no number rather than a wrong one... unless the
        // smaller region is the base and nothing floats inside it.
        var union = RegionBuilder.UnionArea(new[] { a, b }, Tol);
        if (union.HasValue) Assert.Equal(a.Area, union.Value, 6);
    }

    [Fact]
    public void TheAreaTableAndLegalDraftShowBothAndChooseNeither()
    {
        var settings = new EasementSettings();
        var record = new EasementRecord
        {
            Kind = EasementRecord.PortionKind, Title = "SEWER EASEMENT", Purpose = "SEWER", AreaSquareFeet = 8000, PrimaryAreaSquareFeet = 5000,
            PhysicalAreaSquareFeet = 7500, ComponentsOverlap = true,
            PortionSteps = new List<PortionStep> { new() { Side = "SOUTH", Distance = 50, Line = Course.Line(P(0, 0), P(100, 0)) } },
            Components = new List<EasementComponent> { new() { Label = "B", Connector = "TOGETHER WITH", Kind = EasementComponent.BoundaryKind, AreaSquareFeet = 3000 } }
        };
        Assert.Equal(7500, record.DisplayAreaSquareFeet);

        var rows = ExhibitPlanner.AreaRows(new[] { record }, new ExhibitSettings());
        Assert.Contains(rows, r => r.Label.Trim() == "SUM OF COMPONENT AREAS" && r.SquareFeet == 8000);
        Assert.Contains(rows, r => r.Label.Trim() == "TOTAL PHYSICAL AREA" && r.SquareFeet == 7500);
        Assert.Contains(rows, r => r.Label.Trim() == "COMPONENT A" && r.SquareFeet == 5000);
        Assert.Contains(rows, r => r.Label.Trim() == "COMPONENT B" && r.SquareFeet == 3000);

        var draft = LegalDescriptionWriter.WritePortion(record, new LegalInputs(), settings, null);
        Assert.Contains("[SUM OF COMPONENT AREAS 8,000 / TOTAL PHYSICAL AREA 7,500]", draft.Text);
        Assert.Contains(draft.Checks, c => c.StartsWith("Components overlap", StringComparison.Ordinal));
        Assert.Contains("TOGETHER WITH", draft.Text);     // still described as separate components
    }
}
