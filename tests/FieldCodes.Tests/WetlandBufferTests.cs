using FieldCodes.Easements;
using FieldCodes.Wetlands;

namespace FieldCodes.Tests;

/// <summary>
/// FTFWETLANDBUFFER's geometry: the buffer is exactly the distance from the wetland everywhere
/// (round tangent outside corners, no loops where a jagged edge folds back), smoothing rounds
/// the inside corners without coming closer, an open edge is buffered to the side picked, and
/// hard surfaces between the buffer and the wetland cut it back.
/// </summary>
public sealed class WetlandBufferTests
{
    private static List<Course> Polygon(params double[] xy)
    {
        var points = new List<P2>();
        for (var i = 0; i < xy.Length; i += 2) points.Add(new P2(xy[i], xy[i + 1]));
        var courses = new List<Course>();
        for (var i = 0; i < points.Count; i++) courses.Add(Course.Line(points[i], points[(i + 1) % points.Count]));
        return courses;
    }

    private static List<Course> OpenEdge(params double[] xy)
    {
        var courses = new List<Course>();
        for (var i = 2; i < xy.Length; i += 2) courses.Add(Course.Line(new P2(xy[i - 2], xy[i - 1]), new P2(xy[i], xy[i + 1])));
        return courses;
    }

    private static WetlandBufferResult Buffer(List<Course> wetland, bool closed, double d, double r = 0, int side = 1,
                                              params HardSurfaceLine[] hard)
    {
        return WetlandBuffer.Build(new WetlandBufferRequest
        {
            Wetland = wetland, Closed = closed, Distance = d, Smoothing = r, Side = side,
            HardSurfaces = hard.Length > 0 ? hard : null
        });
    }

    private static IEnumerable<P2> Samples(BufferPath p, int perCourse = 12)
    {
        foreach (var c in p.Courses)
            for (var i = 0; i <= perCourse; i++)
                yield return c.PointAt(c.Length * i / perCourse);
    }

    /// <summary>Every point of the buffer exactly the distance from the wetland.</summary>
    private static void Exactly(WetlandBufferResult r, List<Course> wetland, double d)
    {
        Assert.True(r.Ok, string.Join("; ", r.Problems));
        foreach (var p in r.Paths)
            foreach (var s in Samples(p))
                Assert.Equal(d, RoundOffset.DistanceTo(wetland, s), 6);
    }

    private static void Continuous(BufferPath p)
    {
        for (var i = 1; i < p.Courses.Count; i++)
            Assert.True(p.Courses[i - 1].End.DistanceTo(p.Courses[i].Start) < 1e-4, "gap after course " + (i - 1));
        if (p.Closed) Assert.True(p.Courses[^1].End.DistanceTo(p.Courses[0].Start) < 1e-4, "does not close");
    }

    // ------------------------------------------------------------------ closed wetlands

    [Fact]
    public void ASquareWetlandGetsStraightSidesAndQuarterCircleCornersOfTheBufferRadius()
    {
        var wetland = Polygon(0, 0, 100, 0, 100, 100, 0, 100);
        var r = Buffer(wetland, true, 50);
        Exactly(r, wetland, 50);
        var loop = Assert.Single(r.Paths);
        Assert.True(loop.Closed);
        Continuous(loop);
        Assert.Equal(400 + 2 * Math.PI * 50, loop.Length, 6);
        Assert.Equal(4, loop.Courses.Count(c => c.Kind == CourseKind.Line));
        Assert.Equal(4, loop.Courses.Count(c => c.Kind == CourseKind.Arc));
        Assert.All(loop.Courses.Where(c => c.Kind == CourseKind.Arc), a => Assert.Equal(50, a.Radius, 9));
        Assert.Equal(50, r.ClosestApproach, 6);
    }

    [Fact]
    public void ANotchNarrowerThanTwiceTheBufferIsFilledWithNoLoops()
    {
        // A U: the notch is 20 wide and 60 deep; a 50 ft buffer bridges it.
        var wetland = Polygon(0, 0, 100, 0, 100, 100, 60, 100, 60, 40, 40, 40, 40, 100, 0, 100);
        var r = Buffer(wetland, true, 50);
        Exactly(r, wetland, 50);
        var loop = Assert.Single(r.Paths);
        Continuous(loop);
        Assert.Empty(Loops.SelfIntersections(loop.Courses, 1e-6));
        // Over the notch it dips only between the circles round the notch's two top corners
        // (to 100 + sqrt(50^2 - 10^2)); nothing of it reaches down into the notch.
        foreach (var s in Samples(loop)) Assert.False(s.X > 40 && s.X < 60 && s.Y > 0 && s.Y < 148.98, "the buffer dips into the notch at " + s);
        var overNotch = Samples(loop).Where(s => Math.Abs(s.X - 50) < 1e-6 && s.Y > 100).ToList();
        Assert.All(overNotch, s => Assert.Equal(100 + Math.Sqrt(2400), s.Y, 6));
    }

    [Fact]
    public void AJaggedEdgeGivesASmoothBufferWithFarFewerCourses()
    {
        // A wetland edge flagged in short zigzags, 2 ft teeth every 5 ft.
        var xy = new List<double>();
        for (var i = 0; i <= 40; i++) { xy.Add(i * 5); xy.Add(i % 2 == 0 ? 0 : 2); }
        xy.AddRange(new double[] { 200, 80, 0, 80 });
        var wetland = Polygon(xy.ToArray());
        var r = Buffer(wetland, true, 25);
        Exactly(r, wetland, 25);
        var loop = Assert.Single(r.Paths);
        Continuous(loop);
        Assert.Empty(Loops.SelfIntersections(loop.Courses, 1e-6));
        Assert.True(loop.Courses.Count < wetland.Count, loop.Courses.Count + " courses for a " + wetland.Count + "-course edge");
    }

    [Fact]
    public void SmoothingRoundsTheInsideCornersAndNeverComesCloser()
    {
        // An L: one inside corner. Smoothing 10 puts a radius 10 arc there.
        var wetland = Polygon(0, 0, 100, 0, 100, 40, 40, 40, 40, 100, 0, 100);
        var sharp = Buffer(wetland, true, 30);
        var smooth = Buffer(wetland, true, 30, 10);
        Assert.True(smooth.Ok, string.Join("; ", smooth.Problems));
        var loop = Assert.Single(smooth.Paths);
        Continuous(loop);
        Assert.Contains(loop.Courses, c => c.Kind == CourseKind.Arc && Math.Abs(c.Radius - 10) < 1e-6);
        foreach (var s in Samples(loop)) Assert.True(RoundOffset.DistanceTo(wetland, s) >= 30 - 1e-6);
        Assert.Equal(30, smooth.ClosestApproach, 6);
        // The sharp inside corner is a point at 30 from both edges; the rounded one cuts it short.
        Assert.True(loop.Length < sharp.Paths[0].Length);
    }

    [Fact]
    public void ACircularWetlandGetsACircularBuffer()
    {
        var c = new P2(500, 500);
        var wetland = new List<Course>
        {
            Course.Arc(c + new P2(30, 0), c + new P2(-30, 0), c, true),
            Course.Arc(c + new P2(-30, 0), c + new P2(30, 0), c, true)
        };
        var r = Buffer(wetland, true, 50);
        Exactly(r, wetland, 50);
        var loop = Assert.Single(r.Paths);
        Assert.Equal(2 * Math.PI * 80, loop.Length, 6);
        Assert.All(loop.Courses, a => Assert.Equal(80, a.Radius, 6));
    }

    [Fact]
    public void ACurveTighterThanTheBufferOnItsInsideIsBridged()
    {
        // A bite into the wetland: an arc of radius 10 curving in, far tighter than 50.
        var wetland = new List<Course>
        {
            Course.Line(new P2(0, 0), new P2(100, 0)),
            Course.Line(new P2(100, 0), new P2(100, 100)),
            Course.Line(new P2(100, 100), new P2(60, 100)),
            Course.Arc(new P2(60, 100), new P2(40, 100), new P2(50, 100), false),     // dips to y = 90
            Course.Line(new P2(40, 100), new P2(0, 100)),
            Course.Line(new P2(0, 100), new P2(0, 0))
        };
        var r = Buffer(wetland, true, 50);
        Exactly(r, wetland, 50);
        Continuous(Assert.Single(r.Paths));
    }

    [Fact]
    public void StatePlaneCoordinatesGiveTheSameBuffer()
    {
        var x = 1200000.0;
        var y = 400000.0;
        var wetland = Polygon(x, y, x + 100, y, x + 100, y + 100, x, y + 100);
        var r = Buffer(wetland, true, 50);
        Exactly(r, wetland, 50);
        Assert.Equal(400 + 2 * Math.PI * 50, Assert.Single(r.Paths).Length, 6);
    }

    [Fact]
    public void TheWetlandsDirectionDoesNotMatter()
    {
        var ccw = Polygon(0, 0, 100, 0, 100, 100, 0, 100);
        var cw = Polygon(0, 0, 0, 100, 100, 100, 100, 0);
        Assert.Equal(Buffer(ccw, true, 40).Paths[0].Length, Buffer(cw, true, 40).Paths[0].Length, 6);
    }

    // ------------------------------------------------------------------ open edges

    [Fact]
    public void AnOpenEdgeIsBufferedToTheSidePickedWithSquareEnds()
    {
        // East then south: the left side is the outside of the turn.
        var edge = OpenEdge(0, 0, 100, 0, 100, -100);
        var left = Buffer(edge, false, 10, side: 1);
        Exactly(left, edge, 10);
        var outside = Assert.Single(left.Paths);
        Assert.False(outside.Closed);
        Continuous(outside);
        Assert.Equal(new P2(0, 10).ToString(), outside.Courses[0].Start.ToString());
        Assert.Equal(new P2(110, -100).ToString(), outside.Courses[^1].End.ToString());
        Assert.Contains(outside.Courses, c => c.Kind == CourseKind.Arc && Math.Abs(c.Radius - 10) < 1e-9);
        Assert.Equal(100 + 100 + Math.PI * 10 / 2, outside.Length, 6);

        // The right side is the inside of the turn: a sharp corner at (90, -10).
        var right = Buffer(edge, false, 10, side: -1);
        Exactly(right, edge, 10);
        var inside = Assert.Single(right.Paths);
        Assert.Equal(90 + 90, inside.Length, 6);
        Assert.All(inside.Courses, c => Assert.Equal(CourseKind.Line, c.Kind));
    }

    [Fact]
    public void AnOpenEdgeWithSmoothingIsOffsetOutAndBackLikeTheLispRoutine()
    {
        var edge = OpenEdge(0, 0, 100, 0, 100, -100);
        var r = Buffer(edge, false, 10, 5, side: -1);
        Assert.True(r.Ok, string.Join("; ", r.Problems));
        var path = Assert.Single(r.Paths);
        Assert.Contains(path.Courses, c => c.Kind == CourseKind.Arc && Math.Abs(c.Radius - 5) < 1e-6);
        Assert.Equal(10, r.ClosestApproach, 6);
    }

    // ------------------------------------------------------------------ hard surfaces

    private static HardSurfaceLine Edge(string id, double x0, double y0, double x1, double y1, string layer = "V-SURF-ASPH-E")
    {
        return new HardSurfaceLine(id, layer, new[] { Course.Line(new P2(x0, y0), new P2(x1, y1)) });
    }

    [Fact]
    public void TheBufferStopsAtARoadEdgeBetweenItAndTheWetland()
    {
        var wetland = Polygon(0, 0, 100, 0, 100, 100, 0, 100);
        var road = Edge("1A2", 120, -300, 120, 400);
        var r = Buffer(wetland, true, 50, hard: road);
        Assert.True(r.Ok, string.Join("; ", r.Problems));
        var kept = Assert.Single(r.Paths);
        Assert.False(kept.Closed);
        Continuous(kept);
        foreach (var s in Samples(kept)) Assert.True(s.X <= 120 + 1e-6, "buffer runs past the road edge at " + s);
        Assert.Equal(120, kept.Courses[0].Start.X, 6);
        Assert.Equal(120, kept.Courses[^1].End.X, 6);
        var cut = Assert.Single(r.Trimmed);
        Assert.Equal("V-SURF-ASPH-E", cut.BlockedBy.Layer);
        Assert.Equal("1A2", cut.BlockedBy.Id);
        // Nothing of the wetland's own distance is lost on the near side.
        foreach (var s in Samples(kept)) Assert.Equal(50, RoundOffset.DistanceTo(wetland, s), 6);
    }

    [Fact]
    public void BothEdgesOfARoadAreHandledAndNothingInOrBeyondTheRoadIsKept()
    {
        var wetland = Polygon(0, 0, 100, 0, 100, 100, 0, 100);
        var r = Buffer(wetland, true, 50, hard: new[] { Edge("A", 110, -300, 110, 400), Edge("B", 134, -300, 134, 400) });
        var kept = Assert.Single(r.Paths);
        foreach (var s in Samples(kept)) Assert.True(s.X <= 110 + 1e-6);
        Assert.Equal(3, r.Trimmed.Count);        // in the road at the top and bottom, and beyond it
        Assert.All(r.Trimmed, t => Assert.Equal("A", t.BlockedBy.Id));
    }

    [Fact]
    public void ADrivewayAcrossTheBufferDoesNotCutItOff()
    {
        // Two driveway edges cross the buffer square to the wetland and stop short of it.
        var wetland = Polygon(0, 0, 100, 0, 100, 100, 0, 100);
        var r = Buffer(wetland, true, 50, hard: new[] { Edge("L", 40, 130, 40, 200), Edge("R", 60, 130, 60, 200) });
        Assert.Empty(r.Trimmed);
        var loop = Assert.Single(r.Paths);
        Assert.True(loop.Closed);
        Exactly(r, wetland, 50);
    }

    [Fact]
    public void ABuildingInsideTheBufferIsNotedNotTrimmed()
    {
        var wetland = Polygon(0, 0, 100, 0, 100, 100, 0, 100);
        var building = new HardSurfaceLine("B1", "V-SURF-BLDG-E", Polygon(40, 115, 60, 115, 60, 130, 40, 130));
        var r = Buffer(wetland, true, 50, hard: building);
        Assert.Empty(r.Trimmed);
        Assert.True(Assert.Single(r.Paths).Closed);
        Assert.Contains(r.Notes, n => n.Text.Contains("V-SURF-BLDG-E") && n.Text.Contains("check"));
    }

    [Fact]
    public void ABuildingTheBufferRunsThroughCutsOnlyTheStretchInsideIt()
    {
        var wetland = Polygon(0, 0, 100, 0, 100, 100, 0, 100);
        var building = new HardSurfaceLine("B2", "V-SURF-BLDG-E", Polygon(40, 140, 60, 140, 60, 160, 40, 160));
        var r = Buffer(wetland, true, 50, hard: building);
        var cut = Assert.Single(r.Trimmed);
        Assert.Equal(20, cut.Length, 6);         // y = 150 between x = 40 and 60
        var kept = Assert.Single(r.Paths);       // the rest is one line, around the far side
        Assert.False(kept.Closed);
        Assert.Equal(400 + 2 * Math.PI * 50 - 20, kept.Length, 6);
    }

    [Fact]
    public void HardSurfacesFarAwayChangeNothing()
    {
        var wetland = Polygon(0, 0, 100, 0, 100, 100, 0, 100);
        var plain = Buffer(wetland, true, 50);
        var r = Buffer(wetland, true, 50, hard: Edge("far", 500, -300, 500, 400));
        Assert.Empty(r.Trimmed);
        Assert.Empty(r.Notes);
        Assert.Equal(plain.Paths[0].Length, Assert.Single(r.Paths).Length, 9);
    }

    [Fact]
    public void AnOpenEdgeIsTrimmedTheSameWay()
    {
        var edge = OpenEdge(0, 0, 200, 0);
        var r = Buffer(edge, false, 50, side: 1, hard: Edge("road", 100, 30, 100, 300));
        // The road edge crosses the buffer at x = 100; past it the straight way back to the
        // wetland edge does not cross the road (it is square to it), so nothing is cut.
        Assert.Empty(r.Trimmed);
        // A road edge running along between them cuts all of it off.
        var along = Buffer(edge, false, 50, side: 1, hard: Edge("road", -50, 30, 250, 30));
        Assert.False(along.Ok);
        Assert.Single(along.Trimmed);
        Assert.Contains(along.Notes, n => n.Text.Contains("cut off the whole buffer"));
    }

    // ------------------------------------------------------------------ the rules file

    [Fact]
    public void TheRulesFileSaysWhereTheBufferGoesAndWhichLayersAreHardSurfaces()
    {
        var cfg = RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        var rule = cfg.WetlandBuffer;
        Assert.Equal("V-WATR-WTLD-BUFR-E", rule.Layer);
        Assert.Equal(50, rule.Distance);
        Assert.True(rule.TrimAtHardSurfaces);
        Assert.Equal(WetlandBufferRule.DefaultHardSurfaceLayers(), rule.HardSurfaceLayers);
        Assert.True(rule.IsHardSurface("v-surf-asph-e"));
        Assert.True(rule.IsHardSurface("V-SURF-GRVL-E"));
        Assert.True(rule.IsHardSurface("V-SURF-BLDG-E"));
        Assert.False(rule.IsHardSurface("V-SURF-FENC-E"));
        Assert.False(rule.IsHardSurface("V-WATR-WTLD-E"));
        Assert.False(rule.IsHardSurface("V-SURF-ASPH-TEXT-E"));

        var wild = new WetlandBufferRule { HardSurfaceLayers = new List<string> { "V-SURF-ASPH*" } };
        Assert.True(wild.IsHardSurface("V-SURF-ASPH-OVRL-E"));
        Assert.False(wild.IsHardSurface("V-SURF-CONC-E"));
        Assert.NotEmpty(new WetlandBufferRule { Distance = 0 }.Problems());
        Assert.NotEmpty(new WetlandBufferRule { Layer = " " }.Problems());
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public void ABadDistanceIsRefusedNotGuessed()
    {
        var wetland = Polygon(0, 0, 100, 0, 100, 100, 0, 100);
        Assert.False(Buffer(wetland, true, 0).Ok);
        Assert.False(Buffer(wetland, true, -5).Ok);
        Assert.False(Buffer(wetland, true, 50, -1).Ok);
        Assert.False(WetlandBuffer.Build(new WetlandBufferRequest { Distance = 10 }).Ok);
    }
}
