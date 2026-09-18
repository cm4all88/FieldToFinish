using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// The maths behind FTFLABELLINE, the interactive click-to-place workflow: the
/// cursor picks the spot along the line and which side the label sits on; the
/// perpendicular distance always comes from the configured standard. FromCursor
/// and PlaceAt are the same pure functions the jig calls, so these tests pin the
/// preview and the committed label alike.
/// </summary>
public sealed class InteractiveLineLabelTests
{
    // -------------------------------------------------------- cursor -> side

    [Fact]
    public void CursorNorthOfAnEastboundLineChoosesLeft()
    {
        Assert.Equal(LineLabelSide.Left,
            LineSideParser.FromCursor(0.0, dx: 0.0, dy: 3.0, onLineBand: 0.5));
    }

    [Fact]
    public void CursorSouthOfAnEastboundLineChoosesRight()
    {
        Assert.Equal(LineLabelSide.Right,
            LineSideParser.FromCursor(0.0, dx: 0.0, dy: -3.0, onLineBand: 0.5));
    }

    [Fact]
    public void TheSideIsLineRelative_NotScreenRelative()
    {
        // North of a WESTBOUND line is the line's own right -- but the label still
        // lands where the cursor is (north), because Right of westbound IS north.
        Assert.Equal(LineLabelSide.Right,
            LineSideParser.FromCursor(Math.PI, dx: 0.0, dy: 3.0, onLineBand: 0.5));
    }

    [Fact]
    public void InsideTheOnLineBandTheLabelSnapsOntoTheLine()
    {
        Assert.Equal(LineLabelSide.OnLine,
            LineSideParser.FromCursor(0.0, dx: 0.1, dy: 0.3, onLineBand: 0.5));
        Assert.Equal(LineLabelSide.OnLine,
            LineSideParser.FromCursor(0.0, dx: 0.0, dy: 0.0, onLineBand: 0.0));
    }

    [Fact]
    public void JustOutsideTheBandTheCursorSideWins()
    {
        Assert.Equal(LineLabelSide.Left,
            LineSideParser.FromCursor(0.0, dx: 0.0, dy: 0.51, onLineBand: 0.5));
    }

    // ---------------------------------------------------------- PlaceAt maths

    [Fact]
    public void CursorAboveAnEastboundLineLandsTheLabelExactlyWhereItPoints()
    {
        // Cursor at (50, 3) over an eastbound line along Y=0: side Left, offset 1.0
        // from the standard -> label at (50, 1), reading east.
        var side = LineSideParser.FromCursor(0.0, 0.0, 3.0, 0.5);
        var plan = LineLabelPlanner.PlaceAt(50.0, 0.0, 0.0, side, 1.0, true);

        Assert.Equal(50.0, plan.X, 9);
        Assert.Equal(1.0, plan.Y, 9);
        Assert.Equal(0.0, plan.RotationRadians, 9);
    }

    [Fact]
    public void ReadabilityFlippingNeverMovesTheLabelOffTheCursorSide()
    {
        // Cursor north of a WESTBOUND line. The text flips to read east, but the
        // label must stay north -- on the side the user pointed at.
        var side = LineSideParser.FromCursor(Math.PI, 0.0, 3.0, 0.5);
        var plan = LineLabelPlanner.PlaceAt(50.0, 0.0, Math.PI, side, 1.0, true);

        Assert.Equal(LineLabelSide.Right, side);          // line-relative right
        Assert.Equal(1.0, plan.Y, 9);                     // geometrically north
        Assert.Equal(0.0, plan.RotationRadians, 9);       // flipped to read east
    }

    [Fact]
    public void OnLinePlacementAppliesNoOffset()
    {
        var plan = LineLabelPlanner.PlaceAt(50.0, 7.0, Math.PI / 4,
                                            LineLabelSide.OnLine, 1.0, true);
        Assert.Equal(50.0, plan.X, 9);
        Assert.Equal(7.0, plan.Y, 9);
    }

    [Fact]
    public void HorizontalModeStillRespectsTheSide()
    {
        var plan = LineLabelPlanner.PlaceAt(0.0, 0.0, 0.0, LineLabelSide.Right,
                                            2.0, alignToLine: false);
        Assert.Equal(-2.0, plan.Y, 9);
        Assert.Equal(0.0, plan.RotationRadians, 9);
    }

    [Fact]
    public void TheBulkPlannerAndPlaceAtAreTheSameCalculation()
    {
        // A 100 ft eastbound line planned by the bulk engine yields exactly what
        // PlaceAt computes for its midpoint: one calculation, two entry points.
        var path = new StraightEastPath(100.0);
        var options = new LineLabelOptions
        {
            MinLength = 10, RepeatInterval = 200, EndClearance = 5,
            Side = LineLabelSide.Left, SideOffset = 1.5
        };

        var planned = LineLabelPlanner.Plan(path, options).Single();
        var direct = LineLabelPlanner.PlaceAt(50.0, 0.0, 0.0, LineLabelSide.Left,
                                              1.5, true, 50.0);

        Assert.Equal(direct.X, planned.X, 9);
        Assert.Equal(direct.Y, planned.Y, 9);
        Assert.Equal(direct.RotationRadians, planned.RotationRadians, 9);
    }

    // -------------------------------------------------- between two edges

    [Fact]
    public void ADrivewayLabelSitsMidwayBetweenItsTwoEdges()
    {
        // Two east-running EOA edges at y=0 and y=10: the label sits on the
        // midline, reading along the edges.
        var plan = LineLabelPlanner.PlaceBetween(50, 0, 0.0, 50, 10, 0.0, true);

        Assert.Equal(50.0, plan.X, 9);
        Assert.Equal(5.0, plan.Y, 9);
        Assert.Equal(0.0, plan.RotationRadians, 9);
    }

    [Fact]
    public void OppositelyDigitizedEdgesStillAverageAlongTheDriveway()
    {
        // TBC often draws the far edge the other way. Averaging east with west
        // naively gives garbage; the flip makes it read east like the near edge.
        var plan = LineLabelPlanner.PlaceBetween(50, 0, 0.0, 50, 10, Math.PI, true);

        Assert.Equal(5.0, plan.Y, 9);
        Assert.Equal(0.0, plan.RotationRadians, 9);
    }

    [Fact]
    public void ConvergingEdgesSplitTheAngle()
    {
        // Edges at +10 and -10 degrees: the label follows the centreline, 0.
        var plan = LineLabelPlanner.PlaceBetween(
            0, 0, 10 * Math.PI / 180, 0, 6, -10 * Math.PI / 180, true);

        Assert.Equal(0.0, plan.RotationRadians, 9);
        Assert.Equal(3.0, plan.Y, 9);
    }

    private sealed class StraightEastPath : ILinePath
    {
        public StraightEastPath(double length) { Length = length; }
        public double Length { get; }
        public void At(double d, out double x, out double y, out double dir)
        {
            x = d; y = 0; dir = 0;
        }
    }
}
