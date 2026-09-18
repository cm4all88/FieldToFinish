using FieldCodes.Geometry;

namespace FieldCodes.Tests;

public sealed class DripLineTrimmerTests
{
    private const double TwoPi = Math.PI * 2.0;
    private readonly DripLineTrimmer _t = new();

    private static Circle2d C(double x, double y, double r) => new(x, y, r);

    [Fact]
    public void IsolatedCircles_StayWhole()
    {
        var r = _t.TrimAll(new[] { C(0, 0, 10), C(100, 0, 10), C(0, 100, 10) });

        Assert.All(r, x =>
        {
            Assert.True(x.FullCircle);
            Assert.False(x.FullyHidden);
            Assert.Empty(x.Arcs);
        });
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty() => Assert.Empty(_t.TrimAll(Array.Empty<Circle2d>()));

    [Fact]
    public void TwoEqualOverlappingCircles_EachKeepThe240DegreeOuterArc()
    {
        // r=1 centres 1 apart: half-angle = acos(0.5) = 60 deg, so the arc facing the
        // neighbour spans 120 deg and is swallowed; 240 deg survives on each.
        var r = _t.TrimAll(new[] { C(0, 0, 1), C(1, 0, 1) });

        foreach (var res in r)
        {
            Assert.False(res.FullCircle);
            Assert.False(res.FullyHidden);
            var arc = Assert.Single(res.Arcs);
            Assert.Equal(TwoPi * 240.0 / 360.0, arc.Sweep, 9);
        }

        // The survivor on circle 0 points away from circle 1, i.e. midpoint near 180 deg.
        var mid0 = r[0].Arcs[0].StartAngle + r[0].Arcs[0].Sweep / 2.0;
        Assert.Equal(Math.PI, DripLineTrimmer.Normalize(mid0), 9);
    }

    [Fact]
    public void SmallCircleInsideLargeOne_IsFullyHidden()
    {
        var r = _t.TrimAll(new[] { C(0, 0, 10), C(1, 0, 2) });

        Assert.True(r[0].FullCircle);      // the big one is untouched
        Assert.True(r[1].FullyHidden);     // the small one vanishes
        Assert.Empty(r[1].Arcs);
    }

    [Fact]
    public void IdenticalCircles_KeepExactlyOne()
    {
        // Both contain each other. Without an index tie-break the drip line disappears.
        var r = _t.TrimAll(new[] { C(5, 5, 3), C(5, 5, 3) });

        Assert.True(r[0].FullCircle);
        Assert.False(r[0].FullyHidden);
        Assert.True(r[1].FullyHidden);
    }

    [Fact]
    public void ExternallyTangentCircles_ProduceNoSlivers()
    {
        // Touching at exactly one point. Naive trimming emits a zero-length arc here.
        var r = _t.TrimAll(new[] { C(0, 0, 5), C(10, 0, 5) });

        Assert.All(r, x =>
        {
            Assert.True(x.FullCircle);
            Assert.Empty(x.Arcs);
        });
    }

    [Fact]
    public void InternallyTangentCircles_ProduceNoSlivers()
    {
        var r = _t.TrimAll(new[] { C(0, 0, 10), C(5, 0, 5) });

        Assert.True(r[0].FullCircle);
        Assert.True(r[1].FullyHidden);
        Assert.Empty(r[1].Arcs);
    }

    [Fact]
    public void NoArcHasZeroOrNegativeSweep()
    {
        var circles = new List<Circle2d>();
        for (var i = 0; i < 40; i++)
            circles.Add(C(i * 3.0, (i % 7) * 2.5, 4.0 + (i % 5)));

        foreach (var res in _t.TrimAll(circles))
            Assert.All(res.Arcs, a =>
            {
                Assert.True(a.Sweep > 0, "sweep must be positive");
                Assert.True(a.Sweep <= TwoPi + 1e-9, "sweep must not exceed a full turn");
                Assert.InRange(a.StartAngle, 0.0, TwoPi);
            });
    }

    [Fact]
    public void ChainOfThreeCircles_MiddleKeepsTwoArcs()
    {
        // Middle circle is cut on both sides but not covered, so two arcs survive.
        var r = _t.TrimAll(new[] { C(0, 0, 5), C(8, 0, 5), C(16, 0, 5) });

        Assert.Equal(2, r[1].Arcs.Count);
        Assert.Single(r[0].Arcs);
        Assert.Single(r[2].Arcs);
    }

    [Fact]
    public void CircleCoveredByUnionOfNeighbours_ButInsideNoSingleOne_IsHidden()
    {
        // A small circle straddled by two big ones: inside neither alone, covered by both.
        var r = _t.TrimAll(new[] { C(-4, 0, 6), C(4, 0, 6), C(0, 0, 1.0) });

        Assert.True(r[2].FullyHidden);
        Assert.Empty(r[2].Arcs);
    }

    [Fact]
    public void ResultsAreDeterministicAndInInputOrder()
    {
        var circles = new List<Circle2d>();
        for (var i = 0; i < 60; i++)
            circles.Add(C((i * 37) % 50, (i * 17) % 50, 3 + (i % 4)));

        var a = _t.TrimAll(circles);
        var b = _t.TrimAll(circles);

        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(i, a[i].Index);
            Assert.Equal(a[i].FullCircle, b[i].FullCircle);
            Assert.Equal(a[i].FullyHidden, b[i].FullyHidden);
            Assert.Equal(a[i].Arcs.Count, b[i].Arcs.Count);
            for (var k = 0; k < a[i].Arcs.Count; k++)
            {
                Assert.Equal(a[i].Arcs[k].StartAngle, b[i].Arcs[k].StartAngle, 12);
                Assert.Equal(a[i].Arcs[k].Sweep, b[i].Arcs[k].Sweep, 12);
            }
        }
    }

    [Fact]
    public void SpatialBucketing_MatchesBruteForceOnARandomField()
    {
        // The grid is an optimisation; it must not change the answer. Compare against
        // a dense cluster where nearly everything overlaps something.
        var rng = new Random(20260814);
        var circles = new List<Circle2d>();
        for (var i = 0; i < 300; i++)
            circles.Add(C(rng.NextDouble() * 120, rng.NextDouble() * 120, 2 + rng.NextDouble() * 8));

        var viaGrid = new DripLineTrimmer { UseSpatialIndex = true }.TrimAll(circles);
        var brute = new DripLineTrimmer { UseSpatialIndex = false }.TrimAll(circles);

        Assert.Equal(brute.Count, viaGrid.Count);
        for (var i = 0; i < brute.Count; i++)
        {
            Assert.Equal(brute[i].FullyHidden, viaGrid[i].FullyHidden);
            Assert.Equal(brute[i].FullCircle, viaGrid[i].FullCircle);
            Assert.Equal(brute[i].Arcs.Count, viaGrid[i].Arcs.Count);
            for (var k = 0; k < brute[i].Arcs.Count; k++)
            {
                Assert.Equal(brute[i].Arcs[k].StartAngle, viaGrid[i].Arcs[k].StartAngle, 9);
                Assert.Equal(brute[i].Arcs[k].Sweep, viaGrid[i].Arcs[k].Sweep, 9);
            }
        }
    }

    [Fact]
    public void SpatialBucketing_IsActuallyFasterOnALargeField()
    {
        // Guards against the grid silently degenerating into all-pairs.
        var rng = new Random(7);
        var circles = new List<Circle2d>();
        for (var i = 0; i < 2000; i++)
            circles.Add(C(rng.NextDouble() * 4000, rng.NextDouble() * 4000, 2 + rng.NextDouble() * 4));

        var grid = new DripLineTrimmer { UseSpatialIndex = true };
        var pairs = new DripLineTrimmer { UseSpatialIndex = false };

        var swGrid = System.Diagnostics.Stopwatch.StartNew();
        grid.TrimAll(circles);
        swGrid.Stop();

        var swPairs = System.Diagnostics.Stopwatch.StartNew();
        pairs.TrimAll(circles);
        swPairs.Stop();

        Assert.True(swGrid.ElapsedMilliseconds * 4 < swPairs.ElapsedMilliseconds,
            $"grid {swGrid.ElapsedMilliseconds}ms vs all-pairs {swPairs.ElapsedMilliseconds}ms " +
            "- expected the grid to be several times faster");
    }
}
