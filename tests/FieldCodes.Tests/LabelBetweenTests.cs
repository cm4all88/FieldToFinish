using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// FTFLABELBETWEEN's geometry. The case that started it: a hedge drawn as ONE 3D polyline
/// up one side, across, and down the other, picked on its two long sides, must get its
/// label centred between those sides -- not refused as "the same line twice", and not
/// dragged to the middle of the whole polyline, which is on the bottom of the U. Each side
/// is the run of linework around one pick; the label sits midway between the nearest
/// point of each, in plan, whatever the lines' elevations.
/// </summary>
public sealed class LabelBetweenTests
{
    private const double Tol = 1e-9;
    private static readonly double Quarter = Math.PI / 2.0;

    private static PlanPoint P(double x, double y, double z = 0.0) => new PlanPoint(x, y, z);
    private static PlanPath Path(params PlanPoint[] points) => new PlanPath(points);

    /// <summary>The hedge: up the west side, across the top, down the east side. 10 wide, 100 long.</summary>
    private static PlanPath Hedge() => Path(P(0, 0), P(0, 100), P(10, 100), P(10, 0));

    // ------------------------------------------------------------- the U-shaped hedge

    [Fact]
    public void AUShapedHedgeIsLabelledCentredBetweenItsLongSides()
    {
        var sides = LabelBetween.OneLine(Hedge(), 0, 40, 10, 60);
        Assert.Null(sides.Problem);
        Assert.True(sides.OneLine);

        var start = LabelBetween.Start(sides, alignToLine: true);
        Assert.Equal(5.0, start.X, 9);                 // midway between the legs
        Assert.Equal(50.0, start.Y, 9);                // at the height of the picks
        Assert.Equal(Quarter, start.RotationRadians, 9);  // reading along the legs
    }

    [Fact]
    public void TheHedgeLabelIsNotAtTheMiddleOfTheWholePolyline()
    {
        // Halfway along the whole polyline (station 105 of 210) is (5, 100): the bottom of
        // the U. That is where a whole-line midpoint would put the label.
        var hedge = Hedge();
        double midX, midY;
        hedge.PointAt(hedge.Length / 2.0, out midX, out midY);
        Assert.Equal(100.0, midY, 9);

        var start = LabelBetween.Start(LabelBetween.OneLine(hedge, 0, 40, 10, 60), true);
        Assert.True(Math.Abs(start.Y - midY) > 40.0);
    }

    [Fact]
    public void TheHedgeLabelSlidesAlongBetweenTheSidesStayingCentred()
    {
        var sides = LabelBetween.OneLine(Hedge(), 0, 40, 10, 60);

        foreach (var (x, y) in new[] { (3.0, 20.0), (9.0, 75.0), (5.0, 5.0), (1.0, 90.0), (-4.0, 30.0), (14.0, 30.0) })
        {
            var plan = LabelBetween.Place(sides, x, y, true);
            Assert.Equal(5.0, plan.X, 9);
            Assert.Equal(y, plan.Y, 9);
            Assert.Equal(Quarter, plan.RotationRadians, 9);
        }
    }

    [Fact]
    public void PicksAtVeryDifferentHeightsStillSplitTheUAtItsBottom()
    {
        // One pick near the open end of the west leg, the other near the bottom of the east
        // leg: half way along the polyline between them is up the west leg, which would put
        // part of the west leg on the east side. The split goes by the bend instead.
        var sides = LabelBetween.OneLine(Hedge(), 0, 5, 10, 95);
        Assert.Null(sides.Problem);

        var start = LabelBetween.Start(sides, true);
        Assert.Equal(5.0, start.X, 9);
        Assert.Equal(50.0, start.Y, 9);

        var nearTheTop = LabelBetween.Place(sides, 2, 85, true);
        Assert.Equal(5.0, nearTheTop.X, 9);
        Assert.Equal(85.0, nearTheTop.Y, 9);
    }

    [Fact]
    public void PickingTheSidesInEitherOrderGivesTheSameLabel()
    {
        var ab = LabelBetween.Start(LabelBetween.OneLine(Hedge(), 0, 40, 10, 60), true);
        var ba = LabelBetween.Start(LabelBetween.OneLine(Hedge(), 10, 60, 0, 40), true);

        Assert.Equal(ab.X, ba.X, 9);
        Assert.Equal(ab.Y, ba.Y, 9);
        Assert.Equal(ab.RotationRadians, ba.RotationRadians, 9);
    }

    [Fact]
    public void SidesDrawnInOppositeDirectionsReadOneWayAllAlong()
    {
        // The west leg runs north, the east leg south: averaged naively they cancel. The
        // label reads along the legs at every cursor position, never sideways.
        var sides = LabelBetween.OneLine(Hedge(), 0, 40, 10, 60);
        for (var y = 2.0; y <= 92.0; y += 7.5)
        {
            var plan = LabelBetween.Place(sides, 4.0, y, true);
            Assert.Equal(Quarter, plan.RotationRadians, 9);
        }

        // An east-west U (legs running east and west) reads east, not upside down.
        var lying = Path(P(0, 0), P(100, 0), P(100, 10), P(0, 10));
        var flat = LabelBetween.Start(LabelBetween.OneLine(lying, 40, 0, 60, 10), true);
        Assert.Equal(0.0, flat.RotationRadians, 9);
        Assert.Equal(5.0, flat.Y, 9);
    }

    [Fact]
    public void AHorseshoeWithARoundedEndIsLabelledBetweenItsLegs()
    {
        // Legs 20 apart, joined by a half circle sampled into 32 chords (as a bulge is read).
        var points = new List<PlanPoint> { P(-10, 0), P(-10, 50) };
        for (var k = 1; k < 32; k++)
        {
            var theta = Math.PI - Math.PI * k / 32.0;
            points.Add(P(10 * Math.Cos(theta), 50 + 10 * Math.Sin(theta)));
        }
        points.Add(P(10, 50));
        points.Add(P(10, 0));

        var sides = LabelBetween.OneLine(new PlanPath(points), -10, 20, 10, 30);
        Assert.Null(sides.Problem);

        var start = LabelBetween.Start(sides, true);
        Assert.Equal(0.0, start.X, 9);
        Assert.Equal(25.0, start.Y, 9);
        Assert.Equal(Quarter, start.RotationRadians, 9);

        var higher = LabelBetween.Place(sides, 6, 44, true);
        Assert.Equal(0.0, higher.X, 9);
        Assert.Equal(44.0, higher.Y, 9);
    }

    [Fact]
    public void AUWithSlightlyOutOfSquareCornersStillSplitsAtItsBottom()
    {
        // Real hedges are not square: corners of 88 and 92 degrees. The middle of the bottom
        // is still the split, so each leg stays one side.
        var hedge = Path(P(0, 0), P(0, 100), P(10, 100.35), P(10.2, 0));
        var sides = LabelBetween.OneLine(hedge, 0, 30, 10.14, 30);
        Assert.Null(sides.Problem);

        var plan = LabelBetween.Place(sides, 5, 90, true);
        Assert.InRange(plan.X, 4.9, 5.2);
        Assert.InRange(plan.Y, 89.5, 90.5);
    }

    // ------------------------------------------------- irregular and nonparallel sides

    [Fact]
    public void AnIrregularBoundaryKeepsTheLabelBetweenItsSides()
    {
        // A hedge outline digitized shot by shot: wobbly legs, a peaked bottom, the east
        // leg flaring out. Picks on the two legs.
        var outline = Path(
            P(0, 0), P(1, 30), P(-1, 60), P(0, 100),     // west leg
            P(8, 104), P(16, 100),                        // the bottom, peaked
            P(20, 60), P(18, 30), P(24, 0));              // east leg, flaring
        var sides = LabelBetween.OneLine(outline, 0.5, 15, 19, 45);
        Assert.Null(sides.Problem);

        foreach (var (x, y) in new[] { (6.0, 30.0), (12.0, 60.0), (9.0, 15.0), (10.0, 80.0) })
        {
            var plan = LabelBetween.Place(sides, x, y, true);
            var west = sides.SideA.Closest(plan.X, plan.Y)!;
            var east = sides.SideB.Closest(plan.X, plan.Y)!;

            Assert.True(west.X < plan.X && plan.X < east.X, $"label at ({plan.X:0.00}, {plan.Y:0.00}) is not between the legs");
            Assert.True(west.Distance > 3.0 && east.Distance > 3.0);
            Assert.InRange(Math.Abs(plan.RotationRadians), 60 * Math.PI / 180, Quarter + 1e-9);   // along the legs
        }
    }

    [Fact]
    public void NonparallelSidesPutTheLabelMidwayBetweenTheNearestPoints()
    {
        // A vertical west edge and an east edge leaning out: the label sits at the midpoint
        // of the nearest point on each, along their averaged direction.
        var west = Path(P(0, 0), P(0, 100));
        var east = Path(P(10, 0), P(30, 100));
        var sides = LabelBetween.TwoLines(west, 0, 50, east, 20, 50);
        Assert.Null(sides.Problem);

        var plan = LabelBetween.Place(sides, 5, 50, true);

        // Nearest point on the east edge to (5, 50): t = 4900 / 10400 along it.
        var t = 4900.0 / 10400.0;
        var ex = 10 + 20 * t;
        var ey = 100 * t;
        var expected = LineLabelPlanner.PlaceBetween(0, 50, Quarter, ex, ey, Math.Atan2(100, 20), true);

        Assert.Equal(expected.X, plan.X, 9);
        Assert.Equal(expected.Y, plan.Y, 9);
        Assert.Equal(expected.RotationRadians, plan.RotationRadians, 9);
        Assert.Equal((Quarter + Math.Atan2(100, 20)) / 2.0, plan.RotationRadians, 9);
    }

    [Fact]
    public void ALineThatRunsOnPastTheUIsSplitAtTheUAndKeepsItsRunsOnTheirOwnSides()
    {
        // An edge of pavement along the road that turns up into a driveway, across its end
        // and back down to the road: one polyline. Picks on the driveway's two sides.
        var edge = Path(P(-50, 0), P(0, 0), P(0, 40), P(10, 40), P(10, 0), P(60, 0));
        var sides = LabelBetween.OneLine(edge, 0, 20, 10, 20);
        Assert.Null(sides.Problem);

        var start = LabelBetween.Start(sides, true);
        Assert.Equal(5.0, start.X, 9);
        Assert.Equal(20.0, start.Y, 9);

        var nearTheMouth = LabelBetween.Place(sides, 5, 5, true);
        Assert.Equal(5.0, nearTheMouth.X, 9);
        Assert.Equal(5.0, nearTheMouth.Y, 9);
    }

    // ----------------------------------------------- two portions of one object

    [Fact]
    public void TwoSidesOfOneClosedOutlineAreLabelledBetween()
    {
        // A walk drawn as one closed polyline, 100 x 8: picks on its two long sides.
        var walk = Path(P(0, 0), P(100, 0), P(100, 8), P(0, 8), P(0, 0));
        Assert.True(walk.Closed);

        var sides = LabelBetween.OneLine(walk, 40, 0, 60, 8);
        Assert.Null(sides.Problem);

        var start = LabelBetween.Start(sides, true);
        Assert.Equal(50.0, start.X, 9);
        Assert.Equal(4.0, start.Y, 9);
        Assert.Equal(0.0, start.RotationRadians, 9);

        foreach (var x in new[] { 3.0, 30.0, 97.0 })
        {
            var plan = LabelBetween.Place(sides, x, 1.0, true);
            Assert.Equal(x, plan.X, 9);
            Assert.Equal(4.0, plan.Y, 9);
        }
    }

    [Fact]
    public void AClosedOutlinePickedTheOtherWayRoundGivesTheSameLabel()
    {
        // The second pick before the first along the loop: the walk between them runs on
        // through the outline's first vertex.
        var walk = Path(P(0, 0), P(100, 0), P(100, 8), P(0, 8), P(0, 0));
        var sides = LabelBetween.OneLine(walk, 60, 8, 40, 0);
        Assert.Null(sides.Problem);

        var start = LabelBetween.Start(sides, true);
        Assert.Equal(50.0, start.X, 9);
        Assert.Equal(4.0, start.Y, 9);
        Assert.Equal(0.0, start.RotationRadians, 9);
        Assert.Equal(8.0, sides.SideA.YAt(0), 9);        // the first pick's side is the top
        Assert.Equal(0.0, sides.SideB.YAt(0), 9);

        var nearTheStart = LabelBetween.Place(sides, 2, 6, true);
        Assert.Equal(2.0, nearTheStart.X, 9);
        Assert.Equal(4.0, nearTheStart.Y, 9);
    }

    [Fact]
    public void TwoSegmentsOfOneOpenPolylineAreLabelledBetween()
    {
        // Two parallel segments of one polyline, joined by a short leg.
        var line = Path(P(0, 0), P(50, 0), P(50, 6), P(0, 6));
        var sides = LabelBetween.OneLine(line, 20, 0, 30, 6);
        Assert.Null(sides.Problem);

        var start = LabelBetween.Start(sides, true);
        Assert.Equal(25.0, start.X, 9);
        Assert.Equal(3.0, start.Y, 9);
        Assert.Equal(0.0, start.RotationRadians, 9);
    }

    [Fact]
    public void PicksOnTheSameSegmentAreRefused()
    {
        var sides = LabelBetween.OneLine(Hedge(), 0, 10, 0, 60);
        Assert.NotNull(sides.Problem);
        Assert.Contains("same straight run", sides.Problem);
        Assert.Null(sides.SideA);
    }

    [Fact]
    public void PicksOnOneStraightRunAcrossAVertexAreRefused()
    {
        // A straight edge with a vertex part way along it: two segments, nothing between them.
        var edge = Path(P(0, 0), P(50, 0), P(100, 0), P(100, 10), P(0, 10));
        Assert.NotNull(LabelBetween.OneLine(edge, 20, 0, 70, 0).Problem);

        // ... and the same on one side of a closed outline.
        var walk = Path(P(0, 0), P(100, 0), P(100, 8), P(0, 8), P(0, 0));
        Assert.NotNull(LabelBetween.OneLine(walk, 20, 0, 60, 0).Problem);
    }

    [Fact]
    public void ThePickTwiceInTheSamePlaceIsRefused()
    {
        Assert.NotNull(LabelBetween.OneLine(Hedge(), 10, 50, 10, 50).Problem);
    }

    // ----------------------------------------------------------- two separate objects

    [Fact]
    public void TwoSeparateEdgesStillWorkAsBefore()
    {
        // A driveway between two edge-of-asphalt lines, the far one drawn the other way.
        var near = Path(P(0, 0), P(100, 0));
        var far = Path(P(100, 10), P(0, 10));
        var sides = LabelBetween.TwoLines(near, 30, 0, far, 70, 10);
        Assert.Null(sides.Problem);
        Assert.False(sides.OneLine);

        var start = LabelBetween.Start(sides, true);
        Assert.Equal(50.0, start.X, 9);
        Assert.Equal(5.0, start.Y, 9);
        Assert.Equal(0.0, start.RotationRadians, 9);

        var moved = LabelBetween.Place(sides, 20, 2, true);
        var direct = LineLabelPlanner.PlaceBetween(20, 0, 0.0, 20, 10, Math.PI, true);
        Assert.Equal(direct.X, moved.X, 9);
        Assert.Equal(direct.Y, moved.Y, 9);
        Assert.Equal(direct.RotationRadians, moved.RotationRadians, 9);
    }

    [Fact]
    public void TwoSeparateMultiSegmentEdgesFollowTheirLocalSegments()
    {
        // Two edges that bend: the label follows the segments under the cursor.
        var south = Path(P(0, 0), P(50, 0), P(100, 50));
        var north = Path(P(0, 10), P(45.86, 10), P(95.86, 60));
        var sides = LabelBetween.TwoLines(south, 25, 0, north, 25, 10);

        var straight = LabelBetween.Place(sides, 25, 5, true);
        Assert.Equal(25.0, straight.X, 9);
        Assert.Equal(5.0, straight.Y, 9);
        Assert.Equal(0.0, straight.RotationRadians, 9);

        var bent = LabelBetween.Place(sides, 80, 33, true);
        Assert.Equal(Math.PI / 4.0, bent.RotationRadians, 6);
    }

    [Fact]
    public void HorizontalTextStaysHorizontalWhenNotAlignedToTheLine()
    {
        var plan = LabelBetween.Start(LabelBetween.OneLine(Hedge(), 0, 40, 10, 60), alignToLine: false);
        Assert.Equal(5.0, plan.X, 9);
        Assert.Equal(0.0, plan.RotationRadians, 9);
    }

    // ------------------------------------------------------------------ plan, not 3D

    [Fact]
    public void AThreeDPolylineGivesTheSameLabelAsItsPlanTrace()
    {
        // The hedge shot at its real elevations, with a vertical step at the top (two
        // vertices at one plan position): the elevation must not move the label, the step
        // has no length in plan.
        var flat = Hedge();
        var shot = Path(P(0, 0, 312.4), P(0, 50, 318.9), P(0, 100, 309.2), P(0, 100, 307.0),
                        P(10, 100, 309.8), P(10, 0, 301.5));

        Assert.Equal(flat.Length, shot.Length, 9);

        var a = LabelBetween.OneLine(flat, 0, 40, 10, 60);
        var b = LabelBetween.OneLine(shot, 0, 40, 10, 60);
        foreach (var (x, y) in new[] { (5.0, 50.0), (2.0, 12.0), (8.0, 88.0) })
        {
            var pa = LabelBetween.Place(a, x, y, true);
            var pb = LabelBetween.Place(b, x, y, true);
            Assert.Equal(pa.X, pb.X, 9);
            Assert.Equal(pa.Y, pb.Y, 9);
            Assert.Equal(pa.RotationRadians, pb.RotationRadians, 9);
        }

        var sa = LabelBetween.Start(a, true);
        var sb = LabelBetween.Start(b, true);
        Assert.Equal(sa.X, sb.X, 9);
        Assert.Equal(sa.Y, sb.Y, 9);
    }

    [Fact]
    public void TwoThreeDEdgesAtDifferentElevationsAreMeasuredInPlan()
    {
        // A cursor at Z = 0 against lines hundreds of feet up: the nearest point in 3D is
        // dragged toward wherever the line is lowest. In plan it is straight across.
        var west = Path(P(0, 0, 400), P(0, 100, 250));
        var east = Path(P(10, 0, 250), P(10, 100, 400));
        var plan = LabelBetween.Place(LabelBetween.TwoLines(west, 0, 50, east, 10, 50), 5, 70, true);

        Assert.Equal(5.0, plan.X, 9);
        Assert.Equal(70.0, plan.Y, 9);
    }

    [Fact]
    public void AVerticalLineHasNoPlanToLabelBetween()
    {
        var post = Path(P(5, 5, 100), P(5, 5, 110));
        Assert.Equal(0.0, post.Length, 9);
        Assert.NotNull(LabelBetween.OneLine(post, 5, 5, 5, 5).Problem);
        Assert.NotNull(LabelBetween.TwoLines(post, 5, 5, Hedge(), 0, 50).Problem);
    }

    // ----------------------------------------------------------------- the plan path

    [Fact]
    public void APortionOfAClosedPathRunsOnThroughItsStart()
    {
        var walk = Path(P(0, 0), P(100, 0), P(100, 8), P(0, 8), P(0, 0));
        var portion = walk.Portion(212, 104);     // from the west end, through (0,0), to the east end

        Assert.False(portion.Closed);
        Assert.Equal(0.0, portion.XAt(0), 9);
        Assert.Equal(4.0, portion.YAt(0), 9);
        Assert.Equal(100.0, portion.XAt(portion.Count - 1), 9);
        Assert.Equal(4.0, portion.YAt(portion.Count - 1), 9);
        Assert.Equal(108.0, portion.Length, 9);
    }

    [Fact]
    public void TheNearestPointIsFoundInPlanWithTheLocalDirection()
    {
        var foot = Hedge().Closest(8, 30)!;
        Assert.Equal(10.0, foot.X, 9);
        Assert.Equal(30.0, foot.Y, 9);
        Assert.Equal(2, foot.Segment);
        Assert.Equal(-Quarter, foot.DirectionRadians, 9);
        Assert.Equal(2.0, foot.Distance, 9);
        Assert.Equal(180.0, foot.Station, 9);
    }
}
