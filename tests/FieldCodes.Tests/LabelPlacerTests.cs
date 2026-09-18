using FieldCodes;
using FieldCodes.Geometry;

namespace FieldCodes.Tests;

public sealed class LabelPlacerTests
{
    private static LabelPlacer NewPlacer() =>
        new() { BaseOffset = 1.0, RingStep = 1.0, RingCount = 4 };

    private static Obstacle Hard(double x0, double y0, double x1, double y1)
        => new(new Box2d(x0, y0, x1, y1), ObstacleClass.Hard);

    private static Obstacle Soft(double x0, double y0, double x1, double y1)
        => new(new Box2d(x0, y0, x1, y1), ObstacleClass.Soft);

    private static Obstacle Free(double x0, double y0, double x1, double y1)
        => new(new Box2d(x0, y0, x1, y1), ObstacleClass.Free);

    [Fact]
    public void EmptyField_TakesTheFirstCandidate_NE_OnTheBaseRing()
    {
        var p = NewPlacer().Place(0, 0, 4, 1, new List<Obstacle>());

        Assert.True(p.Placed);
        Assert.Equal(LabelDirection.NE, p.Direction);
        Assert.Equal(0, p.Ring);
        Assert.False(p.NeedsLeader);
        Assert.False(p.MasksSoftObstacles);
    }

    [Fact]
    public void FreeObstacles_AreIgnoredEntirely()
    {
        // Canopies and linework get masked, so they must not push the label around.
        var obstacles = new List<Obstacle> { Free(-100, -100, 100, 100) };
        var p = NewPlacer().Place(0, 0, 4, 1, obstacles);

        Assert.Equal(LabelDirection.NE, p.Direction);
        Assert.Equal(0, p.Ring);
        Assert.False(p.MasksSoftObstacles);
    }

    [Fact]
    public void HardObstacle_IsNeverOverlapped()
    {
        // Block NE; the placer must move to the next direction in order (E).
        var obstacles = new List<Obstacle> { Hard(0.5, 0.5, 6, 3) };
        var p = NewPlacer().Place(0, 0, 4, 1, obstacles);

        Assert.True(p.Placed);
        Assert.Equal(LabelDirection.E, p.Direction);
        Assert.All(obstacles, o => Assert.False(p.Bounds.Intersects(o.Bounds)));
    }

    [Fact]
    public void SoftObstacle_IsAvoidedWhenACleanSpotExists()
    {
        // NE is clear of hard things but covered in existing text; E is clean.
        var obstacles = new List<Obstacle> { Soft(0.5, 0.5, 6, 3) };
        var p = NewPlacer().Place(0, 0, 4, 1, obstacles);

        Assert.Equal(LabelDirection.E, p.Direction);
        Assert.False(p.MasksSoftObstacles);
        Assert.Equal(0.0, p.SoftOverlapArea);
    }

    [Fact]
    public void SoftObstacle_IsMaskedOnlyWhenForced()
    {
        // Every base-ring direction has soft text; the least-covered one wins and
        // gets masked rather than pushing the label out onto a leader.
        var obstacles = new List<Obstacle>
        {
            Soft(-100, -100, 100, 100),          // blankets everything lightly
            Hard(-0.9, -0.9, 0.9, 0.9)           // does not reach any candidate
        };

        var p = NewPlacer().Place(0, 0, 4, 1, obstacles);

        Assert.True(p.Placed);
        Assert.Equal(0, p.Ring);
        Assert.False(p.NeedsLeader);
        Assert.True(p.MasksSoftObstacles);
        Assert.True(p.SoftOverlapArea > 0);
    }

    [Fact]
    public void BaseRingFullyBlocked_MovesOutAndAsksForALeader()
    {
        // Blocks every candidate on rings 0 and 1 (offsets 1 and 2) but leaves ring 2
        // (offset 3) clear. Anything larger than the search can reach would just fail.
        var obstacles = new List<Obstacle> { Hard(-2.5, -2.5, 2.5, 2.5) };
        var p = NewPlacer().Place(0, 0, 4, 1, obstacles);

        Assert.True(p.Placed);
        Assert.True(p.Ring > 0);
        Assert.True(p.NeedsLeader);
        Assert.False(p.Bounds.Intersects(obstacles[0].Bounds));
    }

    [Fact]
    public void EverythingBlocked_ReportsFailureRatherThanGuessing()
    {
        var obstacles = new List<Obstacle> { Hard(-1000, -1000, 1000, 1000) };
        var p = NewPlacer().Place(0, 0, 4, 1, obstacles);

        Assert.False(p.Placed);
        Assert.True(p.NeedsLeader);
    }

    [Fact]
    public void DirectionOrderIsNE_E_SE_NW_W_SW_N_S()
    {
        // Asserted directly: adjacent candidate boxes overlap each other (NE and N
        // share a corner region at the same offset), so blocking one direction at a
        // time cannot isolate the next and would not actually test the order.
        Assert.Equal(
            new[]
            {
                LabelDirection.NE, LabelDirection.E, LabelDirection.SE,
                LabelDirection.NW, LabelDirection.W, LabelDirection.SW,
                LabelDirection.N,  LabelDirection.S
            },
            LabelPlacer.SearchOrder);
    }

    [Fact]
    public void WithinARing_EarliestFreeDirectionWins()
    {
        // A hard band covering everything above y = -0.9 blocks NE, E, NW, W and N.
        // SE, SW and S all survive, and SE is earliest in the search order.
        var obstacles = new List<Obstacle> { Hard(-20, -0.9, 20, 20) };
        var p = NewPlacer().Place(0, 0, 4, 1, obstacles);

        Assert.True(p.Placed);
        Assert.Equal(LabelDirection.SE, p.Direction);
        Assert.Equal(0, p.Ring);
        Assert.False(p.Bounds.Intersects(obstacles[0].Bounds));

        // Isolating a single direction is impossible by construction: at one offset the
        // SE, SW and S boxes all share x-extent, so any obstacle blocking two of them
        // blocks the third. The order itself is asserted in the test above.
    }

    [Fact]
    public void PlacedBoxKeepsTheRequestedSize()
    {
        var p = NewPlacer().Place(10, 20, 7.5, 2.25, new List<Obstacle>());
        Assert.Equal(7.5, p.Bounds.Width, 9);
        Assert.Equal(2.25, p.Bounds.Height, 9);
    }

    [Theory]
    [InlineData(LabelDirection.E)]
    [InlineData(LabelDirection.W)]
    [InlineData(LabelDirection.N)]
    [InlineData(LabelDirection.S)]
    [InlineData(LabelDirection.NE)]
    [InlineData(LabelDirection.SE)]
    [InlineData(LabelDirection.NW)]
    [InlineData(LabelDirection.SW)]
    public void EveryCandidateClearsTheAnchorByTheOffset(LabelDirection d)
    {
        var box = LabelPlacer.BoxFor(d, 0, 0, 4, 1, 1.0);

        // The anchor is never inside the label box.
        Assert.False(box.MinX < 0 && box.MaxX > 0 && box.MinY < 0 && box.MaxY > 0);
    }

    [Fact]
    public void Placement_IsDeterministic()
    {
        var obstacles = new List<Obstacle>
        {
            Hard(0.5, 0.5, 6, 3), Soft(-8, -4, -1, 2), Free(-20, -20, 20, 20)
        };

        var a = NewPlacer().Place(0, 0, 4, 1, obstacles);
        var b = NewPlacer().Place(0, 0, 4, 1, obstacles);

        Assert.Equal(a.Direction, b.Direction);
        Assert.Equal(a.Ring, b.Ring);
        Assert.Equal(a.Bounds.MinX, b.Bounds.MinX, 12);
        Assert.Equal(a.Bounds.MinY, b.Bounds.MinY, 12);
    }

    // --- box arithmetic ---

    [Fact]
    public void TouchingBoxesDoNotCountAsOverlapping()
    {
        var a = new Box2d(0, 0, 1, 1);
        var b = new Box2d(1, 0, 2, 1);      // shares an edge
        Assert.False(a.Intersects(b));
        Assert.Equal(0.0, a.OverlapArea(b));
    }

    [Fact]
    public void OverlapAreaIsCorrect()
    {
        var a = new Box2d(0, 0, 4, 2);
        var b = new Box2d(3, 1, 10, 10);
        Assert.Equal(1.0, a.OverlapArea(b), 9);   // 1 wide x 1 tall
    }
}
