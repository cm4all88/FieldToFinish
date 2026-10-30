using System.Globalization;
using System.Text.RegularExpressions;
using FieldCodes.Contours;
using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// FTFCONTOUR: contour polylines at an exact elevation off a TIN surface, without touching
/// the surface. Civil 3D supplies the contour inside the product; what is held here is
/// everything around it -- reading the elevations exactly, the independent triangle tracer
/// the command falls back on (and that defines what "the contour" is), clipping to a
/// boundary without moving the line, label placement, and the before/after proof that the
/// surface is unchanged.
/// </summary>
public sealed class ContourTests
{
    // ================================================================== elevations

    [Fact]
    public void OneElevationIsReadExactlyAndKeepsItsTypedText()
    {
        var r = ContourElevations.Parse("12.60");
        Assert.True(r.Ok);
        var e = Assert.Single(r.Elevations);
        Assert.Equal(12.6, e.Value);                       // the double 12.6, bit for bit
        Assert.Equal("12.60", e.Text);                     // the label says what was typed
    }

    [Theory]
    [InlineData("12.6, 13.0, 13.5")]
    [InlineData("12.6,13.0,13.5")]
    [InlineData("12.6 13.0 13.5")]
    [InlineData("12.6; 13.0; 13.5")]
    [InlineData(" 12.6 ,  13.0 ,13.5 , ")]
    public void SeveralElevationsInOneGo(string input)
    {
        var r = ContourElevations.Parse(input);
        Assert.True(r.Ok, string.Join(" | ", r.Problems));
        Assert.Equal(new[] { 12.6, 13.0, 13.5 }, r.Elevations.Select(e => e.Value));
        Assert.Equal(new[] { "12.6", "13.0", "13.5" }, r.Elevations.Select(e => e.Text));
    }

    [Fact]
    public void ARunLandsOnEveryStepWithoutDrift()
    {
        var r = ContourElevations.Parse("10 to 11 by 0.25");
        Assert.True(r.Ok);
        Assert.Equal(new[] { "10.00", "10.25", "10.50", "10.75", "11.00" }, r.Elevations.Select(e => e.Text));
        Assert.Equal(new[] { 10.0, 10.25, 10.5, 10.75, 11.0 }, r.Elevations.Select(e => e.Value));

        // 0.1 is not exact in binary; each value is still exactly the double its text names.
        var tenths = ContourElevations.Parse("12 to 13 by 0.1");
        Assert.Equal(11, tenths.Elevations.Count);
        foreach (var e in tenths.Elevations)
            Assert.Equal(double.Parse(e.Text, CultureInfo.InvariantCulture), e.Value);
        Assert.Equal(12.3, tenths.Elevations[3].Value);    // not 12.299999999999999
        Assert.Equal("13.0", tenths.Elevations[10].Text);
    }

    [Fact]
    public void ARunCanCountDownAndMixWithAList()
    {
        var r = ContourElevations.Parse("14 to 10 by 2, 12.6");
        Assert.True(r.Ok);
        Assert.Equal(new[] { 14.0, 12.0, 10.0, 12.6 }, r.Elevations.Select(e => e.Value));
    }

    [Fact]
    public void TheSameElevationTwiceIsDrawnOnce()
    {
        var r = ContourElevations.Parse("13, 13.0, 13, 12.6");
        Assert.True(r.Ok);
        Assert.Equal(new[] { 13.0, 12.6 }, r.Elevations.Select(e => e.Value));
        Assert.Equal(2, r.Notes.Count);
        Assert.Contains(r.Notes, n => n.Contains("13 and 13.0 are the same elevation"));
    }

    [Theory]
    [InlineData("12.123456789012", 12.123456789012)]
    [InlineData("-2.5", -2.5)]
    [InlineData(".5", 0.5)]
    [InlineData("+3", 3.0)]
    [InlineData("1234.5678", 1234.5678)]
    public void NothingIsRounded(string input, double expected)
    {
        var e = Assert.Single(ContourElevations.Parse(input).Elevations);
        Assert.Equal(expected, e.Value);
    }

    [Fact]
    public void TypedTextIsTidiedNotReformatted()
    {
        Assert.Equal("0.5", ContourElevations.Parse(".5").Elevations[0].Text);
        Assert.Equal("3", ContourElevations.Parse("+3").Elevations[0].Text);
        Assert.Equal("12", ContourElevations.Parse("12.").Elevations[0].Text);
        Assert.Equal("0", ContourElevations.Parse("-0").Elevations[0].Text);
        Assert.Equal("100.500", ContourElevations.Parse("100.500").Elevations[0].Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [InlineData("abc")]
    [InlineData("12.6, abc")]
    [InlineData("12.6.1")]
    [InlineData("1e3")]
    [InlineData("10 to 14")]
    [InlineData("10 to 14 by 0")]
    [InlineData("0 to 1000 by 0.01")]
    public void InputThatCannotBeReadDrawsNothing(string input)
    {
        var r = ContourElevations.Parse(input);
        Assert.False(r.Ok);
        Assert.NotEmpty(r.Problems);
    }

    [Fact]
    public void TheKeyFindsTheSameElevationHoweverItWasTyped()
    {
        var a = ContourElevations.Parse("12.60").Elevations[0];
        var b = ContourElevations.Parse("12.6").Elevations[0];
        Assert.Equal(a.Key, b.Key);
        Assert.Equal(ContourKeys.Owner("2A3F", a.Value), ContourKeys.Owner("2A3F", b.Value));
        Assert.NotEqual(ContourKeys.Owner("2A3F", 12.6), ContourKeys.Owner("2A3F", 12.6000001));
        Assert.NotEqual(ContourKeys.Owner("2A3F", 12.6), ContourKeys.Owner("2A40", 12.6));

        Assert.True(ContourKeys.TryRead(ContourKeys.Owner("2A3F", -0.1), out var handle, out var z));
        Assert.Equal("2A3F", handle);
        Assert.Equal(-0.1, z);
        Assert.False(ContourKeys.TryRead("not a key", out _, out _));
    }

    // ================================================================== the tracer

    /// <summary>A regular-grid TIN: each cell split into two triangles, z from the function.</summary>
    private static List<TinTriangle> Grid(int nx, int ny, double step, Func<double, double, double> z,
                                          Func<int, int, bool>? skipCell = null, double x0 = 0, double y0 = 0)
    {
        TinPoint P(int i, int j)
        {
            var x = x0 + i * step;
            var y = y0 + j * step;
            return new TinPoint(x, y, z(x, y));
        }

        var triangles = new List<TinTriangle>();
        for (var i = 0; i < nx; i++)
        for (var j = 0; j < ny; j++)
        {
            if (skipCell != null && skipCell(i, j)) continue;
            triangles.Add(new TinTriangle(P(i, j), P(i + 1, j), P(i + 1, j + 1)));
            triangles.Add(new TinTriangle(P(i, j), P(i + 1, j + 1), P(i, j + 1)));
        }
        return triangles;
    }

    /// <summary>True when the point lies on an edge of some triangle exactly where the edge,
    /// interpolated linearly, is at the elevation -- the definition of a TIN contour point.</summary>
    private static bool OnTheSurfaceAt(P2 p, IEnumerable<TinTriangle> triangles, double elevation)
    {
        foreach (var t in triangles)
        foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
        {
            var ab = new P2(b.X - a.X, b.Y - a.Y);
            var len2 = P2.Dot(ab, ab);
            if (len2 <= 0) continue;
            var s = P2.Dot(new P2(p.X - a.X, p.Y - a.Y), ab) / len2;
            if (s < -1e-12 || s > 1 + 1e-12) continue;
            var foot = new P2(a.X + ab.X * s, a.Y + ab.Y * s);
            if (foot.DistanceTo(p) > 1e-9) continue;
            var zAt = a.Z + (b.Z - a.Z) * s;
            if (Math.Abs(zAt - elevation) <= 1e-9) return true;
        }
        return false;
    }

    [Fact]
    public void APlaneGivesOneStraightOpenContourExactlyAtTheElevation()
    {
        var tin = Grid(20, 10, 1.0, (x, y) => x);           // z = x: the 12.6 contour is x = 12.6
        var paths = ContourTracer.Trace(tin, 12.6);

        var path = Assert.Single(paths);
        Assert.False(path.Closed);
        Assert.All(path.Vertices, v => Assert.Equal(12.6, v.X, 12));
        var ys = path.Vertices.Select(v => v.Y).ToList();
        Assert.Equal(0.0, ys.Min(), 12);                     // runs edge to edge of the surface
        Assert.Equal(10.0, ys.Max(), 12);
        Assert.Equal(10.0, path.Length, 9);
    }

    [Fact]
    public void ContourPointsAreTheTrianglesOwnIntersectionNotASmoothedGuess()
    {
        // A curved surface sampled at the grid: the TIN is piecewise linear, and every contour
        // vertex must sit on a triangle edge exactly where that edge reaches the elevation.
        var tin = Grid(12, 12, 1.0, (x, y) => 0.15 * x * x + 0.05 * y * y + 0.3 * x * y);
        foreach (var elevation in new[] { 3.3, 12.6, 20.05 })
        {
            var paths = ContourTracer.Trace(tin, elevation);
            Assert.NotEmpty(paths);
            foreach (var path in paths)
                Assert.All(path.Points, p => Assert.True(OnTheSurfaceAt(p, tin, elevation),
                    $"{p} is not on the surface at {elevation}"));
        }
    }

    [Fact]
    public void AHillGivesAClosedLoopAroundItsTop()
    {
        var tin = Grid(20, 20, 1.0, (x, y) => 20 - Math.Sqrt((x - 10) * (x - 10) + (y - 10) * (y - 10)));
        var path = Assert.Single(ContourTracer.Trace(tin, 15.0));
        Assert.True(path.Closed);
        var boundary = ContourBoundary.FromPoints(path.Points, out var problem);
        Assert.Null(problem);
        Assert.True(boundary!.Contains(new P2(10, 10)));
        Assert.False(boundary.Contains(new P2(2, 2)));
        Assert.All(path.Points, p => Assert.True(OnTheSurfaceAt(p, tin, 15.0)));
    }

    [Fact]
    public void TwoHillsGiveTwoDisconnectedContours()
    {
        double Hill(double x, double y, double cx, double cy) =>
            12 - Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
        var tin = Grid(30, 12, 1.0, (x, y) => Math.Max(Hill(x, y, 7, 6), Hill(x, y, 23, 6)));

        var paths = ContourTracer.Trace(tin, 9.0);
        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.True(p.Closed));
        var centres = paths.Select(p => p.Points.Average(q => q.X)).OrderBy(x => x).ToList();
        Assert.Equal(7, centres[0], 0);
        Assert.Equal(23, centres[1], 0);
    }

    [Fact]
    public void AHiddenGapSplitsAContourIntoSeparateOpenPieces()
    {
        // Cells 4..5 in y are hidden (a hide boundary): the x = 12.6 line breaks around them.
        var tin = Grid(20, 10, 1.0, (x, y) => x, (i, j) => j == 4 || j == 5);
        var paths = ContourTracer.Trace(tin, 12.6);
        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.False(p.Closed));
        Assert.Equal(8.0, paths.Sum(p => p.Length), 9);
    }

    [Fact]
    public void AContourThroughVerticesExactlyAtTheElevationStaysOneCleanLine()
    {
        var tin = Grid(20, 10, 1.0, (x, y) => x);           // x = 12 runs through every grid vertex
        var path = Assert.Single(ContourTracer.Trace(tin, 12.0));
        Assert.False(path.Closed);
        Assert.All(path.Vertices, v => Assert.Equal(12.0, v.X, 12));
        Assert.Equal(10.0, path.Length, 9);
        for (var i = 1; i < path.Vertices.Count; i++)        // no repeated or doubled-back points
            Assert.True(path.Vertices[i].Y > path.Vertices[i - 1].Y || path.Vertices[i].Y < path.Vertices[i - 1].Y);
        var ys = path.Vertices.Select(v => v.Y).ToList();
        Assert.True(ys.SequenceEqual(ys.OrderBy(y => y)) || ys.SequenceEqual(ys.OrderByDescending(y => y)));
    }

    [Fact]
    public void AFlatAtExactlyTheElevationIsOutlinedOnce()
    {
        // A plateau at exactly 10.0 in the middle of a slope down to the edges.
        var tin = Grid(10, 10, 1.0, (x, y) => Math.Min(10.0, 10.0 - (Math.Max(Math.Abs(x - 5), Math.Abs(y - 5)) - 2)));
        var path = Assert.Single(ContourTracer.Trace(tin, 10.0));
        Assert.True(path.Closed);
        // The outline of the 4x4 flat (corners 3,3 .. 7,7): 16 long, never doubled.
        Assert.Equal(16.0, path.Length, 9);
    }

    [Fact]
    public void ElevationsOffTheSurfaceGiveNothing()
    {
        var tin = Grid(10, 10, 1.0, (x, y) => x);
        Assert.Empty(ContourTracer.Trace(tin, -0.5));
        Assert.Empty(ContourTracer.Trace(tin, 10.5));
        Assert.Empty(ContourTracer.Trace(Array.Empty<TinTriangle>(), 1.0));

        // A single peak vertex exactly at the elevation is a point, not a line.
        var cone = Grid(10, 10, 1.0, (x, y) => 10 - Math.Abs(x - 5) - Math.Abs(y - 5));
        Assert.Empty(ContourTracer.Trace(cone, 10.0));
    }

    [Fact]
    public void TracingNeverChangesTheTriangles()
    {
        var tin = Grid(15, 15, 1.0, (x, y) => Math.Sin(x / 3) * 4 + y * 0.5 + 10);
        var before = tin.Select(t => (t.A, t.B, t.C)).ToList();
        var copy = tin.ToArray();

        foreach (var elevation in new[] { 9.0, 12.6, 13.0, 13.5 }) ContourTracer.Trace(tin, elevation);

        Assert.Equal(before, tin.Select(t => (t.A, t.B, t.C)).ToList());
        Assert.Equal(copy, tin.ToArray());
    }

    [Fact]
    public void TheSameTrianglesInAnyOrderGiveTheSameContour()
    {
        var tin = Grid(12, 12, 1.0, (x, y) => 20 - Math.Sqrt((x - 6) * (x - 6) + (y - 6) * (y - 6)));
        var shuffled = tin.OrderBy(t => (t.A.X * 7919 + t.A.Y * 104729 + t.B.X) % 13).ToList();
        var a = ContourTracer.Trace(tin, 16.5).Single();
        var b = ContourTracer.Trace(shuffled, 16.5).Single();
        Assert.Equal(a.Length, b.Length, 9);
        Assert.Equal(a.Vertices.Count, b.Vertices.Count);
        Assert.Equal(a.Points.Select(p => (Math.Round(p.X, 9), Math.Round(p.Y, 9))).OrderBy(p => p),
                     b.Points.Select(p => (Math.Round(p.X, 9), Math.Round(p.Y, 9))).OrderBy(p => p));
    }

    // ================================================================== clipping

    private static ContourBoundary Square(double x0, double y0, double x1, double y1)
    {
        var b = ContourBoundary.FromPoints(new[] { new P2(x0, y0), new P2(x1, y0), new P2(x1, y1), new P2(x0, y1) },
                                           out var problem);
        Assert.Null(problem);
        return b!;
    }

    [Fact]
    public void AContourIsCutWhereItLeavesTheBoundaryAndNowhereElse()
    {
        var contour = ContourPath.FromPoints(new[] { new P2(-5, 3), new P2(2, 3), new P2(15, 3) }, false);
        var pieces = ContourClipper.Clip(contour, Square(0, 0, 10, 10), true, 0.001);

        var piece = Assert.Single(pieces);
        Assert.Equal(new P2(0, 3), piece.Vertices[0].Point);
        Assert.Equal(new P2(2, 3), piece.Vertices[1].Point);   // the interior vertex is kept as it was
        Assert.Equal(new P2(10, 3), piece.Vertices[2].Point);
        Assert.Equal(10.0, piece.Length, 12);
    }

    [Fact]
    public void ClippedEndsLieOnTheOriginalContour()
    {
        var contour = ContourPath.FromPoints(new[] { new P2(-3.3, -1.7), new P2(4.1, 6.2), new P2(13.9, 2.2) }, false);
        var piece = Assert.Single(ContourClipper.Clip(contour, Square(0, 0, 10, 10), true, 0.001));
        foreach (var end in new[] { piece.Vertices[0].Point, piece.Vertices[piece.Vertices.Count - 1].Point })
        {
            var onFirst = Math.Abs(P2.Cross(new P2(4.1, 6.2) - new P2(-3.3, -1.7), end - new P2(-3.3, -1.7)));
            var onSecond = Math.Abs(P2.Cross(new P2(13.9, 2.2) - new P2(4.1, 6.2), end - new P2(4.1, 6.2)));
            Assert.True(Math.Min(onFirst, onSecond) < 1e-9);
        }
    }

    [Fact]
    public void AConcaveBoundaryCanMakeOneContourIntoSeveralPieces()
    {
        // A U-shaped limit: a contour across the open top of the U crosses both arms.
        var u = ContourBoundary.FromPoints(new[]
        {
            new P2(0, 0), new P2(10, 0), new P2(10, 10), new P2(7, 10), new P2(7, 3),
            new P2(3, 3), new P2(3, 10), new P2(0, 10)
        }, out var problem);
        Assert.Null(problem);

        var contour = ContourPath.FromPoints(new[] { new P2(-1, 6), new P2(11, 6) }, false);
        var inside = ContourClipper.Clip(contour, u!, true, 0.001);
        Assert.Equal(2, inside.Count);
        Assert.Equal(6.0, inside.Sum(p => p.Length), 12);

        var outside = ContourClipper.Clip(contour, u!, false, 0.001);
        Assert.Equal(3, outside.Count);
        Assert.Equal(12.0, inside.Sum(p => p.Length) + outside.Sum(p => p.Length), 12);
    }

    [Fact]
    public void AClosedLoopThatStartsInsideComesBackAsOneUnbrokenPiece()
    {
        // A square loop starting at (2,2), inside; its right side pokes out past x = 10.
        var loop = ContourPath.FromPoints(new[] { new P2(2, 2), new P2(14, 2), new P2(14, 8), new P2(2, 8) }, true);
        var piece = Assert.Single(ContourClipper.Clip(loop, Square(0, 0, 10, 10), true, 0.001));
        Assert.False(piece.Closed);
        Assert.Equal(8 + 6 + 8, piece.Length, 12);
        Assert.Equal(new P2(10, 8), piece.Vertices[0].Point);
        Assert.Equal(new P2(10, 2), piece.Vertices[piece.Vertices.Count - 1].Point);
    }

    [Fact]
    public void AContourWhollyInsideIsReturnedUntouchedArcsAndAll()
    {
        var circle = new ContourPath(new[] { new ContourVertex(4, 5, 1.0), new ContourVertex(6, 5, 1.0) }, true);
        var pieces = ContourClipper.Clip(circle, Square(0, 0, 10, 10), true, 0.001);
        Assert.Same(circle, Assert.Single(pieces));
        Assert.Empty(ContourClipper.Clip(circle, Square(0, 0, 10, 10), false, 0.001));
        Assert.Empty(ContourClipper.Clip(circle, Square(20, 20, 30, 30), true, 0.001));
    }

    [Fact]
    public void NoBoundaryKeepsEverything()
    {
        var c = ContourPath.FromPoints(new[] { new P2(0, 0), new P2(5, 5) }, false);
        Assert.Same(c, Assert.Single(ContourClipper.Clip(c, null!, true, 0.001)));
    }

    [Fact]
    public void ACircularBoundaryIsFollowedToTheChordTolerance()
    {
        var circle = new ContourPath(new[] { new ContourVertex(-5, 0, 1.0), new ContourVertex(5, 0, 1.0) }, true);
        var limit = ContourBoundary.FromPath(circle, 0.0005, out var problem);
        Assert.Null(problem);

        var line = ContourPath.FromPoints(new[] { new P2(-10, 1), new P2(10, 1) }, false);
        var piece = Assert.Single(ContourClipper.Clip(line, limit!, true, 0.0005));
        var halfChord = Math.Sqrt(25 - 1);
        Assert.Equal(-halfChord, piece.Vertices[0].X, 2);
        Assert.Equal(halfChord, piece.Vertices[piece.Vertices.Count - 1].X, 2);
        Assert.All(piece.Vertices, v => Assert.Equal(1.0, v.Y, 12));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABoundaryMustBeClosedAndEncloseSomething(bool flat)
    {
        string? problem;
        if (flat)
            Assert.Null(ContourBoundary.FromPoints(new[] { new P2(0, 0), new P2(5, 0), new P2(10, 0) }, out problem));
        else
            Assert.Null(ContourBoundary.FromPath(
                ContourPath.FromPoints(new[] { new P2(0, 0), new P2(5, 0), new P2(5, 5) }, false), 0.01, out problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void InsideAndOutsideClipsShareTheContourExactly()
    {
        var tin = Grid(20, 20, 1.0, (x, y) => 20 - Math.Sqrt((x - 10) * (x - 10) + (y - 10) * (y - 10)));
        var loop = Assert.Single(ContourTracer.Trace(tin, 14.0));
        var limit = Square(3, 3, 11, 17);
        var inside = ContourClipper.Clip(loop, limit, true, 0.001);
        var outside = ContourClipper.Clip(loop, limit, false, 0.001);
        Assert.NotEmpty(inside);
        Assert.NotEmpty(outside);
        Assert.Equal(loop.Length, inside.Sum(p => p.Length) + outside.Sum(p => p.Length), 9);
        Assert.All(inside.SelectMany(p => p.Points), p => Assert.True(limit.Contains(p)));
    }

    // ================================================================== labels

    [Fact]
    public void LongContoursGetALabelPerSpacingAndReadLeftToRight()
    {
        var c = ContourPath.FromPoints(new[] { new P2(100, 0), new P2(0, 0) }, false);   // drawn right to left
        var spots = ContourLabelPlanner.Plan(c, 4.0, 30.0, 0.01);
        Assert.Equal(3, spots.Count);
        Assert.All(spots, s => Assert.Equal(0.0, s.Rotation, 12));                        // not upside down
        Assert.Equal(new[] { 100 - 100.0 / 6, 50, 100.0 / 6 }, spots.Select(s => s.At.X).ToArray(),
                     new ToleranceComparer(1e-9));
    }

    [Fact]
    public void OneLabelWhenSpacingIsOff_NoneWhenThePieceIsTooShort()
    {
        var c = ContourPath.FromPoints(new[] { new P2(0, 0), new P2(0, 50) }, false);
        var spot = Assert.Single(ContourLabelPlanner.Plan(c, 4.0, 0.0, 0.01));
        Assert.Equal(Math.PI / 2, spot.Rotation, 12);
        Assert.Equal(25.0, spot.At.Y, 9);

        var stub = ContourPath.FromPoints(new[] { new P2(0, 0), new P2(7, 0) }, false);
        Assert.Empty(ContourLabelPlanner.Plan(stub, 4.0, 0.0, 0.01));
    }

    [Fact]
    public void ALabelSlidesOffASharpBendOntoAStraightStretch()
    {
        // An L whose corner is exactly where the single label would go.
        var c = ContourPath.FromPoints(new[] { new P2(0, 0), new P2(20, 0), new P2(20, 20) }, false);
        var spot = Assert.Single(ContourLabelPlanner.Plan(c, 4.0, 0.0, 0.01));
        Assert.True(spot.At.DistanceTo(new P2(20, 0)) >= 2.0 - 1e-9);
        var r = spot.Rotation;
        Assert.True(Math.Abs(r) < 1e-9 || Math.Abs(r - Math.PI / 2) < 1e-9, $"rotation {r}");
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(Math.PI, 0.0)]
    [InlineData(-Math.PI / 2, Math.PI / 2)]
    [InlineData(3 * Math.PI / 4, -Math.PI / 4)]
    [InlineData(-3 * Math.PI / 4, Math.PI / 4)]
    public void LabelsAreNeverUpsideDown(double angle, double expected)
    {
        Assert.Equal(expected, ContourLabelPlanner.Readable(angle), 12);
    }

    private sealed class ToleranceComparer : IEqualityComparer<double>
    {
        private readonly double _tolerance;
        public ToleranceComparer(double tolerance) { _tolerance = tolerance; }
        public bool Equals(double a, double b) => Math.Abs(a - b) <= _tolerance;
        public int GetHashCode(double value) => 0;
    }

    // ================================================================== paths

    [Fact]
    public void BulgeArithmeticMatchesTheArc()
    {
        // A semicircle of radius 5 from (-5,0) to (5,0), counter-clockwise (bulge 1).
        Assert.Equal(Math.PI * 5, ArcMath.SegmentLength(new P2(-5, 0), new P2(5, 0), 1.0), 12);
        var centre = ArcMath.Centre(new P2(-5, 0), new P2(5, 0), 1.0);
        Assert.Equal(0.0, centre.X, 12);
        Assert.Equal(0.0, centre.Y, 12);

        // Counter-clockwise from (-5,0) to (5,0) goes underneath (through y < 0).
        var mid = ArcMath.InteriorPoints(new P2(-5, 0), new P2(5, 0), 1.0, 0.001).ToList();
        Assert.All(mid, p => Assert.Equal(5.0, p.DistanceTo(centre), 9));
        Assert.All(mid, p => Assert.True(p.Y < 0));

        var quarter = ArcMath.Centre(new P2(1, 0), new P2(0, 1), Math.Tan(Math.PI / 8));
        Assert.Equal(0.0, quarter.X, 12);
        Assert.Equal(0.0, quarter.Y, 12);
    }

    [Fact]
    public void TessellationStaysWithinTheChordTolerance()
    {
        var circle = new ContourPath(new[] { new ContourVertex(0, -10, 1.0), new ContourVertex(0, 10, 1.0) }, true);
        var flat = circle.Tessellated(0.001);
        Assert.False(flat.HasBulges);
        Assert.True(flat.Closed);
        var pts = flat.Points;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var mid = new P2((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            Assert.True(10.0 - mid.Length <= 0.001 + 1e-12);   // sagitta within tolerance
            Assert.Equal(10.0, a.Length, 9);
        }
        Assert.Equal(2 * Math.PI * 10, circle.Length, 9);
    }

    [Fact]
    public void APathThatReturnsToItsStartIsALoop()
    {
        var p = ContourPath.FromPoints(new[] { new P2(0, 0), new P2(1, 0), new P2(1, 0), new P2(1, 1), new P2(0, 0) }, false)
                           .WithoutRepeats(0.0);
        Assert.True(p.Closed);
        Assert.Equal(3, p.Vertices.Count);
    }

    // ================================================================== proof the surface is untouched

    private static SurfaceFingerprint Fingerprint() => new()
    {
        Name = "EG", Points = 1234, Triangles = 2401, MinimumElevation = 10.12, MaximumElevation = 18.4,
        MeanElevation = 13.377, OutOfDate = false, Style = "1F2"
    };

    [Fact]
    public void AnIdenticalSurfaceReportsNoDifferences()
    {
        Assert.Empty(Fingerprint().DifferencesFrom(Fingerprint()));
        Assert.Equal(8, Fingerprint().ReadCount);
    }

    [Fact]
    public void AnyChangeToTheSurfaceIsCaught()
    {
        var before = Fingerprint();

        var moreTriangles = Fingerprint(); moreTriangles.Triangles = 2405;
        Assert.Contains("triangle count changed from 2401 to 2405", moreTriangles.DifferencesFrom(before));

        var nudged = Fingerprint(); nudged.MeanElevation = Math.BitIncrement(13.377);
        Assert.Single(nudged.DifferencesFrom(before));        // compared exactly, not to display precision

        var rebuilt = Fingerprint(); rebuilt.OutOfDate = true;
        Assert.Contains("the surface became out of date", rebuilt.DifferencesFrom(before));

        var restyled = Fingerprint(); restyled.Style = "2A0";
        Assert.Single(restyled.DifferencesFrom(before));
    }

    [Fact]
    public void ValuesThatCouldNotBeReadAreNotCompared()
    {
        var before = Fingerprint();
        var after = Fingerprint();
        after.Triangles = null;
        after.MeanElevation = null;
        Assert.Empty(after.DifferencesFrom(before));
        Assert.Equal(6, after.ReadCount);
    }

    // ================================================================== the report

    [Theory]
    [InlineData(9.0, 3, 3, false, ContourOutcome.BelowSurface)]
    [InlineData(19.0, 0, 0, false, ContourOutcome.AboveSurface)]
    [InlineData(18.4, 0, 0, false, ContourOutcome.TouchesOnly)]
    [InlineData(12.6, 0, 0, false, ContourOutcome.NoGeometry)]
    [InlineData(12.6, 3, 0, true, ContourOutcome.OutsideBoundary)]
    [InlineData(12.6, 3, 2, true, ContourOutcome.Drawn)]
    [InlineData(12.6, 3, 3, false, ContourOutcome.Drawn)]
    public void EveryElevationGetsAnOutcome(double z, int extracted, int afterClip, bool clipped, ContourOutcome expected)
    {
        Assert.Equal(expected, ContourRunReport.Classify(z, 10.12, 18.4, extracted, afterClip, clipped));
    }

    [Fact]
    public void TheReportSaysWhatWasDrawnAndThatTheSurfaceIsUntouched()
    {
        var report = new ContourRunReport
        {
            SurfaceName = "EG", Before = Fingerprint(), After = Fingerprint(), Units = "ft",
            Layer = "V-TOPO-CONT-E"
        };
        report.Results.Add(new ContourElevationResult
        {
            Elevation = new ContourElevation(12.6, "12.60"), Outcome = ContourOutcome.Drawn, Extracted = 3,
            Pieces = 3, ClosedPieces = 1, Vertices = 386, Length = 412.371, Labels = 3,
            Checked = 12, WorstDeviation = 0.0
        });
        report.Results.Add(new ContourElevationResult
        {
            Elevation = new ContourElevation(19.0, "19.0"), Outcome = ContourOutcome.AboveSurface
        });

        var text = string.Join("\n", report.Lines());
        Assert.Contains("surface \"EG\" (1,234 points, 2,401 triangles, elevations 10.12 to 18.4)", text);
        Assert.Contains("12.60  3 contours (1 closed, 2 open), 412.37 ft, 386 vertices, 3 labels.", text);
        Assert.Contains("19.0   none: above the surface's highest point (18.4).", text);
        Assert.Contains("Surface untouched", text);
        Assert.Contains("3 contour polylines on layer V-TOPO-CONT-E. One U undoes the lot.", text);
        Assert.Contains("Accuracy: 12 vertices read back", text);
        Assert.DoesNotContain("WARNING", text);
    }

    [Fact]
    public void TheReportWarnsWhenTheSurfaceReadsDifferentlyOrAVertexIsOff()
    {
        var after = Fingerprint();
        after.Points = 1235;
        var report = new ContourRunReport { SurfaceName = "EG", Before = Fingerprint(), After = after, Layer = "L" };
        report.Results.Add(new ContourElevationResult
        {
            Elevation = new ContourElevation(12.6, "12.6"), Outcome = ContourOutcome.Drawn, Pieces = 1, Extracted = 1,
            Vertices = 2, Length = 10, Checked = 2, WorstDeviation = 0.02
        });
        var text = string.Join("\n", report.Lines());
        Assert.Contains("WARNING: the surface reads differently after extraction: point count changed from 1234 to 1235", text);
        Assert.Contains("WARNING: 2 vertices read back from the surface; the largest is 0.0200 off", text);
    }

    // ================================================================== settings

    [Fact]
    public void ContourSettingsDefaultToTheTopoLayersAndAreValid()
    {
        var c = new FtfSettings().Contours;
        Assert.Equal("V-TOPO-CONT-E", c.Layer);
        Assert.Equal("V-TOPO-CONT-TEXT-E", c.LabelLayer);
        Assert.False(c.LabelByDefault);
        Assert.Equal(-1, c.LabelDecimals);
        var problems = new List<string>();
        c.Validate(problems);
        Assert.Empty(problems);
    }

    [Fact]
    public void LabelsSayTheElevationAsTypedUnlessDecimalsAreSet()
    {
        var c = new ContourSettings();
        Assert.Equal("12.60", c.LabelText(12.6, "12.60"));
        Assert.Equal("12.6", c.LabelText(12.6, "12.6"));
        c.LabelDecimals = 2;
        Assert.Equal("12.60", c.LabelText(12.6, "12.6"));
        c.LabelDecimals = 0;
        Assert.Equal("13", c.LabelText(12.6, "12.6"));
    }

    [Fact]
    public void BadContourSettingsAreRefused()
    {
        var c = new ContourSettings { Layer = " ", LabelTextPlotted = 0, LabelSpacingPlotted = -1, LabelDecimals = 9 };
        var problems = new List<string>();
        c.Validate(problems);
        Assert.Equal(4, problems.Count);
    }

    [Fact]
    public void ASettingsFileFromBeforeContoursGetsTheDefaults()
    {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(new FtfSettings());
        var old = Newtonsoft.Json.Linq.JObject.Parse(json);
        old.Remove("contours");
        var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<FtfSettings>(old.ToString())!;
        loaded.FillMissingSections();
        Assert.NotNull(loaded.Contours);
        Assert.Equal("V-TOPO-CONT-E", loaded.Contours.Layer);

        var custom = new FtfSettings();
        custom.Contours.Layer = "C-TOPO-MINR";
        custom.Contours.LabelSpacingPlotted = 0;
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<FtfSettings>(
            Newtonsoft.Json.JsonConvert.SerializeObject(custom))!;
        Assert.Equal("C-TOPO-MINR", back.Contours.Layer);
        Assert.Equal(0.0, back.Contours.LabelSpacingPlotted);
    }

    // ================================================================== the command never edits the surface

    private static string CommandSource([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..",
                                                       "src", "FieldCodes.Cad", "ContourCommands.cs")));

    /// <summary>Strips comments so a sentence such as "never rebuilds" cannot hide or fake a call.</summary>
    private static string Code(string source) =>
        Regex.Replace(Regex.Replace(source, @"//[^\n]*", string.Empty), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    // Every surface-editing member of the Civil 3D surface API FTFCONTOUR could reach, as a
    // call or assignment. FTF's own polylines legitimately use AddVertexAt; that is not here.
    [Theory]
    [InlineData(@"\.Rebuild\s*\(")]
    [InlineData(@"\bBreaklinesDefinition\b")]
    [InlineData(@"\bBoundariesDefinition\b")]
    [InlineData(@"\bPointsDefinition\b")]
    [InlineData(@"\bContoursDefinition\b")]
    [InlineData(@"\bDrawingObjectsDefinition\b")]
    [InlineData(@"\bDEMFilesDefinition\b")]
    [InlineData(@"\bPointGroupsDefinition\b")]
    [InlineData(@"\bAddContours\b")]
    [InlineData(@"\bAddVertex\s*\(")]
    [InlineData(@"\bAddVertices\b")]
    [InlineData(@"\bDeleteVertex(?:ices)?\b")]
    [InlineData(@"\bDeleteVertices\b")]
    [InlineData(@"\bMoveVertex\b")]
    [InlineData(@"\bMoveVertices\b")]
    [InlineData(@"\bSwapEdge\b")]
    [InlineData(@"\bAddLine\b")]
    [InlineData(@"\bDeleteLine\b")]
    [InlineData(@"\bSmoothSurface")]
    [InlineData(@"\bRaiseSurface\b")]
    [InlineData(@"\bPasteSurface\b")]
    [InlineData(@"\bSimplifySurface\b")]
    [InlineData(@"\bSetVertexElevation\b")]
    [InlineData(@"\.StyleId\s*=(?!=)")]
    [InlineData(@"\bContourInterval\b")]
    [InlineData(@"\bMinorInterval\b")]
    [InlineData(@"\bMajorInterval\b")]
    public void FtfContourNeverCallsAnythingThatEditsTheSurface(string forbidden)
    {
        Assert.DoesNotMatch(forbidden, Code(CommandSource()));
    }

    [Fact]
    public void TheSurfaceIsOnlyEverOpenedForRead()
    {
        var code = Code(CommandSource());
        Assert.Contains("(TinSurface)tr.GetObject(surfaceId, OpenMode.ForRead)", code);
        Assert.DoesNotMatch(@"surface\w*\.UpgradeOpen", code);
        Assert.DoesNotMatch(@"surface\w*,\s*OpenMode\.ForWrite", code);
        Assert.DoesNotMatch(@"(?i)surfaceId\s*,\s*OpenMode\.ForWrite", code);
    }

    [Fact]
    public void ExtractionIsTheSupportedCallWithoutSmoothingAndItsPolylinesAreRemoved()
    {
        var code = Code(CommandSource());
        Assert.Contains("surface.ExtractContoursAt(elevation)", code);              // no smoothing overload
        Assert.DoesNotContain("ContourSmoothingType", code);
        Assert.Contains("entity.Erase();", code);                                    // each extracted polyline
        Assert.Contains("EraseTemporaries(tr, temporaries);", code);                 // swept before commit
        Assert.Contains("EraseLeftovers(db, temporaries)", code);                    // and after a failed run
        Assert.Contains("pl.Elevation = elevation.Value;", code);                    // the exact typed value
        Assert.Contains("after.DifferencesFrom(before)", code);                      // the before/after proof
    }
}
