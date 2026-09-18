using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>What the legal draft says about exclusions and components, and the closure gate that
/// keeps a draft whose stated courses do not reproduce the drawing from being presented as ready.</summary>
public sealed class LegalCompositionTests
{
    private const double Tol = EasementBuilder.DefaultTolerance;
    private static readonly EasementSettings Settings = new();
    private static P2 P(double x, double y) => new(x, y);
    private static Course L(double x0, double y0, double x1, double y1) => Course.Line(P(x0, y0), P(x1, y1));

    // Lot 100 wide by 200 deep, counter-clockwise.
    private static readonly List<Course> Lot = new() { L(0, 0, 100, 0), L(100, 0, 100, 200), L(100, 200, 0, 200), L(0, 200, 0, 0) };

    private static List<string> Lines(LegalDraft draft) =>
        draft.Text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

    /// <summary>A 150 x 50 construction area with a 20 x 20 building excluded as a hole.</summary>
    private static EasementRecord AreaWithBuilding()
    {
        var loop = new List<Course> { L(0, 50, 150, 50), L(150, 50, 150, 0), L(150, 0, 0, 0), L(0, 0, 0, 50) };
        var building = new List<Course> { L(50, 15, 70, 15), L(70, 15, 70, 35), L(70, 35, 50, 35), L(50, 35, 50, 15) };
        var region = new RegionShape(loop);
        var cut = RegionBuilder.Exclude(region, building, Tol);
        Assert.True(cut.Ok, string.Join("; ", cut.Errors));
        Assert.Equal("HOLE", cut.Effect);
        return new EasementRecord
        {
            Kind = EasementRecord.AreaKind, Purpose = "TEMPORARY CONSTRUCTION",
            RouteCourses = EasementAnnotation.Number(loop, Settings),
            BoundaryCourses = cut.Region.Outer.Select(EasementAnnotation.Describe).ToList(),
            Holes = cut.Region.Holes.Select(h => h.Select(EasementAnnotation.Describe).ToList()).ToList(),
            AreaSquareFeet = cut.Region.Area,
            AnglePoints = new List<SelectedLocation> { new() { X = 0, Y = 50 }, new() { X = 150, Y = 50 }, new() { X = 150, Y = 0 }, new() { X = 0, Y = 0 } },
            AreaSides = new List<AreaSide> { new(), new(), new(), new() },
            Exclusions = new List<Exclusion>
            {
                new() { Kind = Exclusion.BoundaryKind, Description = "THE EXISTING BUILDING FOOTPRINT", Loop = building, Effect = cut.Effect, RemovedSquareFeet = cut.Removed }
            }
        };
    }

    [Fact]
    public void AnExcludedBuildingIsExceptedAfterTheCoursesAndTheAreaIsWhatRemains()
    {
        var area = AreaWithBuilding();
        Assert.Equal(7100, area.AreaSquareFeet, 6);
        var draft = LegalDescriptionWriter.WriteArea(area, new LegalInputs { ParcelDescription = "LOT 2, TEST SHORT PLAT", BeginningDescription = "THE NORTHWEST CORNER OF SAID LOT 2", County = "SNOHOMISH" }, Settings, null);
        var lines = Lines(draft);

        var lastCourse = lines.FindIndex(l => l.EndsWith("TO THE POINT OF BEGINNING.", StringComparison.Ordinal));
        var except = lines.IndexOf("EXCEPT THAT PORTION THEREOF LYING WITHIN THE EXISTING BUILDING FOOTPRINT.");
        var areaLine = lines.FindIndex(l => l.StartsWith("SAID TEMPORARY CONSTRUCTION AREA CONTAINING 7,100 SQUARE FEET", StringComparison.Ordinal));
        Assert.True(lastCourse > 0 && except > lastCourse && areaLine > except, draft.Text);
        Assert.Contains(draft.Checks, c => c.Contains("removes 400 sq ft (an interior hole)") && c.Contains("confirm the exception wording"));
        Assert.True(draft.ReadyForReview);

        var closure = Closure.ForRecord(area, Settings);
        Assert.True(closure.Ready, string.Join("; ", closure.Problems));
        Assert.Contains(closure.Items, i => i.Name == "Hole 1" && i.Cad != null);
    }

    [Fact]
    public void AnUnnamedExclusionIsLeftAsABlankForTheSurveyor()
    {
        var area = AreaWithBuilding();
        area.Exclusions[0].Description = null;
        var draft = LegalDescriptionWriter.WriteArea(area, new LegalInputs(), Settings, null);
        Assert.Contains("EXCEPT THAT PORTION THEREOF LYING WITHIN [DESCRIBE THE EXCLUDED AREA].", draft.Text);
        Assert.Contains(draft.Checks, c => c.Contains("a description of the area exclusion 1 excludes"));
    }

    [Fact]
    public void APortionWithAThereofExceptionAndAComponentReadsInOrderAndTotalsTheArea()
    {
        var steps = new List<PortionStep> { new() { Side = "WEST", Distance = 10, Line = Lot[3] }, new() { Side = "SOUTH", Distance = 50, Line = Lot[0] } };
        var record = new EasementRecord
        {
            Kind = EasementRecord.PortionKind, Purpose = "SEWER", PortionSteps = steps,
            PrimaryAreaSquareFeet = 300, AreaSquareFeet = 300 + 1500,
            Exclusions = new List<Exclusion>
            {
                new() { Kind = Exclusion.PortionKind, PortionSteps = new List<PortionStep> { new() { Side = "NORTH", Distance = 20, Line = L(10, 50, 0, 50) } }, Effect = "CUT", RemovedSquareFeet = 200 }
            },
            Components = new List<EasementComponent>
            {
                new()
                {
                    Label = "B", Connector = "TOGETHER WITH", Kind = EasementComponent.PortionKind, AreaSquareFeet = 1500,
                    PortionSteps = new List<PortionStep> { new() { Side = "SOUTH", Distance = 15, Line = Lot[0] } }
                }
            }
        };
        var inputs = new LegalInputs
        {
            ParcelDescription = "LOT 2, TEST SHORT PLAT", County = "SNOHOMISH",
            ComponentInputs = new Dictionary<string, LegalInputs> { { "B", new LegalInputs { ParcelDescription = "LOT 3, TEST SHORT PLAT" } } }
        };
        var lines = Lines(LegalDescriptionWriter.WritePortion(record, inputs, Settings, null));
        var draft = LegalDescriptionWriter.WritePortion(record, inputs, Settings, null);

        var a = lines.IndexOf("THE WEST 10.00 FEET OF THE SOUTH 50.00 FEET OF LOT 2, TEST SHORT PLAT, AS MEASURED AT RIGHT ANGLES TO THE WEST AND SOUTH LINES THEREOF.");
        var except = lines.IndexOf("EXCEPT THE NORTH 20.00 FEET THEREOF.");
        var together = lines.IndexOf("TOGETHER WITH");
        var b = lines.FindIndex(l => l.StartsWith("THE SOUTH 15.00 FEET OF LOT 3, TEST SHORT PLAT", StringComparison.Ordinal));
        var total = lines.FindIndex(l => l.StartsWith("SAID SEWER EASEMENT CONTAINING 1,800 SQUARE FEET", StringComparison.Ordinal));
        Assert.True(a >= 0 && except > a && together > except && b > together && total > b, draft.Text);

        // Nothing is decided for the surveyor: what THEREOF means and whether each component's area is stated.
        Assert.Contains(draft.Checks, c => c.Contains("\"THEREOF\" in exclusion 1"));
        Assert.Contains(draft.Checks, c => c.Contains("total of the components (A 300 sq ft, B 1,500 sq ft)"));
    }

    [Fact]
    public void AComponentConnectorIsNeverChosenForTheSurveyor()
    {
        var record = new EasementRecord
        {
            Kind = EasementRecord.PortionKind, Purpose = "SEWER", AreaSquareFeet = 2000, PrimaryAreaSquareFeet = 1000,
            PortionSteps = new List<PortionStep> { new() { Side = "SOUTH", Distance = 10, Line = Lot[0] } },
            Components = new List<EasementComponent> { new() { Label = "B", Kind = EasementComponent.BoundaryKind, AreaSquareFeet = 1000 } }
        };
        var draft = LegalDescriptionWriter.WritePortion(record, new LegalInputs(), Settings, null);
        Assert.Contains("[TOGETHER WITH / AND / ALSO]", draft.Text);
        Assert.Contains("[DESCRIBE COMPONENT B].", draft.Text);
        Assert.Contains(draft.Checks, c => c.Contains("the connector before component B"));
    }

    [Fact]
    public void StatedCoursesThatDoNotReproduceTheDrawingMarkTheDraftNotReady()
    {
        var area = AreaWithBuilding();
        // The record says 150.50' for the north course, the drawing has 150.00'.
        area.RouteCourses[0].Length = 150.50;
        var closure = Closure.ForRecord(area, Settings);
        Assert.False(closure.Ready);
        Assert.NotEmpty(closure.Problems);

        var draft = LegalDescriptionWriter.WriteArea(area, new LegalInputs(), Settings, null);
        LegalDescriptionWriter.MarkNotReady(draft, closure.Problems);
        var lines = Lines(draft);
        Assert.False(draft.ReadyForReview);
        Assert.Equal(LegalDescriptionWriter.DraftBanner, lines[0]);
        Assert.Equal("NOT READY FOR SURVEYOR REVIEW - THE STATED COURSES DO NOT REPRODUCE THE CAD EASEMENT", lines[1]);
        Assert.StartsWith("NOT READY: ", draft.Checks[0]);
        // The wrong course is not quietly corrected to agree with the drawing.
        Assert.Contains("150.50 FEET", draft.Text);

        // A draft whose courses reproduce the drawing is left alone.
        var good = LegalDescriptionWriter.WriteArea(AreaWithBuilding(), new LegalInputs(), Settings, null);
        LegalDescriptionWriter.MarkNotReady(good, Closure.ForRecord(AreaWithBuilding(), Settings).Problems);
        Assert.True(good.ReadyForReview);
        Assert.DoesNotContain("NOT READY", good.Text);
    }
}
