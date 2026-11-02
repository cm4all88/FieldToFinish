using FieldCodes.Easements;
using FieldCodes.Orient;
using FieldCodes.Symbols;

namespace FieldCodes.Tests;

/// <summary>
/// The arithmetic behind the symbol preview (FieldCodes.Cad.Ui.BlockPreview): a block definition
/// reduced to polylines with its base point at 0,0, fitted into a box, turned about the insertion
/// point as a marker rotation turns it, and the native-zero arrow landing on the facing asked for.
/// </summary>
public sealed class BlockPreviewMathTests
{
    private const double Tol = 1e-9;

    private static void Near(P2 expected, P2 actual, double tol = 1e-6)
    {
        Assert.True(expected.DistanceTo(actual) < tol, "expected " + expected + " got " + actual);
    }

    // ------------------------------------------------------------------ fitting

    [Fact]
    public void FitsTheCircleAboutTheInsertionPointIntoTheShorterSide()
    {
        var frame = PreviewFrame.Fit(10, 200, 100, 10);
        Assert.Equal(4.0, frame.Scale, 9);           // (100 / 2 - 10) / 10
        Assert.Equal(100, frame.CenterX, 9);
        Assert.Equal(50, frame.CenterY, 9);
    }

    [Fact]
    public void AnEmptyBlockStillGetsAUsableFrame()
    {
        var frame = PreviewFrame.Fit(0, 100, 100, 10);
        Assert.Equal(40, frame.Scale, 9);
        Assert.True(PreviewFrame.Fit(5, 10, 10, 40).Scale > 0);      // a margin bigger than the box
    }

    [Fact]
    public void TheFitIncludesTheArrowSoTurningNeverRescales()
    {
        var sketch = new BlockSketch("B");
        sketch.AddLine(new P2(-2, 0), new P2(4, 3));                  // furthest point 5 from the origin
        Assert.Equal(5, sketch.Radius(), 9);
        Assert.Equal(5 * 1.15, SymbolPreview.ArrowLength(sketch.Radius()), 9);
        Assert.Equal(5 * 1.15 * 1.1, SymbolPreview.FitRadius(sketch), 9);
        Assert.Equal(1.0, SymbolPreview.ArrowLength(0), 9);
    }

    // ------------------------------------------------------------------ turning about the insertion point

    [Fact]
    public void ScreenYRunsDownAndTheBlockTurnsCounterClockwise()
    {
        var frame = PreviewFrame.Fit(10, 200, 100, 10);
        Near(new P2(140, 50), frame.ToScreen(new P2(10, 0), 0));     // east: right
        Near(new P2(100, 10), frame.ToScreen(new P2(10, 0), 90));    // turned 90: up the screen
        Near(new P2(60, 50), frame.ToScreen(new P2(10, 0), 180));
        Near(new P2(100, 90), frame.ToScreen(new P2(0, -10), 0));    // south: down
    }

    [Fact]
    public void TheInsertionPointNeverMoves()
    {
        var frame = PreviewFrame.Fit(3, 300, 200, 12);
        foreach (var r in new[] { 0.0, 33, 90, 225, 359.5 })
            Near(frame.Insertion, frame.ToScreen(new P2(0, 0), r));
    }

    [Fact]
    public void TurningKeepsDistancesFromTheInsertionPoint()
    {
        var p = new P2(3, -4);
        foreach (var r in new[] { 0.0, 15, 90, 200, 271 })
            Assert.Equal(5, PreviewFrame.Rotate(p, r).Length, 9);
        Near(new P2(4, 3), PreviewFrame.Rotate(p, 90));
    }

    // ------------------------------------------------------------------ the native zero arrow

    [Fact]
    public void APmxSignTurnedToFaceAnAzimuthHasItsZeroArrowOnThatAzimuth()
    {
        // SSV-SN: the face points north (90) at rotation 0. To face 135 the marker turns 225.
        var marker = SymbolPreview.MarkerRotation(90, 360, 135);
        Assert.Equal(225, marker, 9);
        Assert.Equal(315, SymbolPreview.ArrowCadDegrees(90, marker), 9);
        Assert.Equal(135, SymbolPreview.ArrowAzimuth(90, marker), 9);

        // The same as FTFPOINTS / FTFORIENT compute it.
        var sign = OrientationStandard.Pmx().SymbolFor("SN")!;
        Assert.Equal(SymbolRotation.MarkerDegrees(sign, 135)!.Value, marker, 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(100)]
    [InlineData(271.5)]
    public void ADirectionalSymbolsArrowAlwaysLandsOnTheFacing(double azimuth)
    {
        foreach (var zero in new[] { 0.0, 90, 137.5, 360 })
        {
            var marker = SymbolPreview.MarkerRotation(zero, 360, azimuth);
            Assert.Equal(0, SymbolRotation.Difference(azimuth, SymbolPreview.ArrowAzimuth(zero, marker)), 6);
        }
    }

    [Fact]
    public void AnAxisLandsOnTheFacingOrItsReverse()
    {
        // A PMX valve: pipe axis north-south (90), repeats every 180.
        var marker = SymbolPreview.MarkerRotation(90, 180, 100);
        Assert.InRange(marker, 0, 180);
        Assert.Equal(0, SymbolRotation.Difference(100, SymbolPreview.ArrowAzimuth(90, marker), 180), 6);
    }

    [Fact]
    public void ASymbolThatLooksTheSameEveryWayIsNeverTurned()
    {
        Assert.Equal(0, SymbolPreview.MarkerRotation(0, 0, 135));
        Assert.Contains("never turned", SymbolPreview.Describe(0, 0, 135));
        Assert.Contains("marker rotation 225", SymbolPreview.Describe(90, 360, 135));
    }

    [Fact]
    public void AClickOnThePreviewIsADirectionInTheBlocksOwnFrame()
    {
        var frame = PreviewFrame.Fit(10, 200, 200, 0);
        Assert.Equal(90, frame.BlockAngleAt(new P2(100, 20), 0)!.Value, 9);      // above: north
        Assert.Equal(0, frame.BlockAngleAt(new P2(180, 100), 0)!.Value, 9);      // right: east
        // Drawn turned 90, a click to the right is the block's own south (270).
        Assert.Equal(270, frame.BlockAngleAt(new P2(180, 100), 90)!.Value, 9);
        Assert.Null(frame.BlockAngleAt(frame.Insertion, 0));

        // Round trip: the direction picked is where the zero arrow is then drawn.
        var picked = frame.BlockAngleAt(new P2(130, 40), 225)!.Value;
        var tip = frame.Toward(SymbolPreview.ArrowCadDegrees(picked, 225), 10);
        var dx = tip.X - frame.CenterX;
        var dy = tip.Y - frame.CenterY;
        Assert.Equal(Math.Atan2(-60, 30), Math.Atan2(dy, dx), 9);
    }

    // ------------------------------------------------------------------ the sketch

    [Fact]
    public void APositiveBulgeIsACounterClockwiseArc()
    {
        // A half circle from (0,0) to (2,0), counter-clockwise: round the bottom of (1,0).
        var points = BlockSketch.BulgePoints(new P2(0, 0), new P2(2, 0), 1.0, 48);
        Assert.Equal(new P2(2, 0).X, points[points.Count - 1].X, 12);
        Assert.All(points, p => Assert.Equal(1.0, p.DistanceTo(new P2(1, 0)), 9));
        Assert.Contains(points, p => p.DistanceTo(new P2(1, -1)) < 1e-9);
        Assert.All(points, p => Assert.True(p.Y <= Tol));

        var clockwise = BlockSketch.BulgePoints(new P2(0, 0), new P2(2, 0), -1.0, 48);
        Assert.All(clockwise, p => Assert.True(p.Y >= -Tol));
    }

    [Fact]
    public void AQuarterBulgeHasTheRightCentre()
    {
        // 90 degrees counter-clockwise round the origin from (1,0) to (0,1): bulge tan(22.5).
        var points = BlockSketch.BulgePoints(new P2(1, 0), new P2(0, 1), Math.Tan(Math.PI / 8), 48);
        Assert.All(points, p => Assert.Equal(1.0, p.Length, 9));
        Assert.Equal(12, points.Count);                       // a quarter of 48 chords: 11 between, then the end point
    }

    [Fact]
    public void AStraightSegmentIsJustItsEnd()
    {
        var points = BlockSketch.BulgePoints(new P2(0, 0), new P2(3, 4), 0, 48);
        Near(new P2(3, 4), Assert.Single(points));
    }

    [Fact]
    public void AClosedPolylineUsesTheLastBulgeOnTheClosingSegment()
    {
        var sketch = new BlockSketch("B");
        sketch.AddPolyline(new[] { new P2(0, 0), new P2(2, 0) }, new[] { 0.0, 1.0 }, true);
        var path = Assert.Single(sketch.Paths);
        Assert.True(path.Closed);
        // The closing segment (2,0) -> (0,0) bulges counter-clockwise: over the top of (1,0).
        Assert.Contains(path.Points, p => p.DistanceTo(new P2(1, 1)) < 1e-9);
        Assert.Equal(2.0, sketch.Radius(), 9);
    }

    [Fact]
    public void AnArcRunsCounterClockwiseFromStartToEndAndWraps()
    {
        var sketch = new BlockSketch("B");
        sketch.AddArc(new P2(0, 0), 2, 0, Math.PI / 2);
        var quarter = sketch.Paths[0].Points;
        Near(new P2(2, 0), quarter[0]);
        Near(new P2(0, 2), quarter[quarter.Count - 1]);

        sketch.AddArc(new P2(0, 0), 1, 3 * Math.PI / 2, Math.PI / 2);   // through east, not west
        Assert.Contains(sketch.Paths[1].Points, p => p.DistanceTo(new P2(1, 0)) < 1e-9);
        Assert.DoesNotContain(sketch.Paths[1].Points, p => p.X < -1e-9);

        sketch.AddCircle(new P2(5, 0), 1);
        Assert.True(sketch.Paths[2].Closed);
        Assert.Equal(6, sketch.Radius(), 9);
    }

    [Fact]
    public void ANestedBlockIsPlacedByItsReference()
    {
        var inner = new BlockSketch("ARROW");
        inner.AddLine(new P2(0, 0), new P2(1, 0));
        inner.Skip("Text");

        var outer = new BlockSketch("SIGN");
        outer.AddNested(inner, new P2(5, 5), Math.PI / 2, 2, 2);
        var line = outer.Paths[0].Points;
        Near(new P2(5, 5), line[0]);
        Near(new P2(5, 7), line[1]);
        Assert.True(outer.Simplified);                         // what the inner block skipped, the outer did too
        Assert.Equal("1 text", outer.SkippedSummary());

        var mirrored = new BlockSketch("M");
        mirrored.AddNested(inner, new P2(0, 0), 0, -1, 1);
        Near(new P2(-1, 0), mirrored.Paths[0].Points[1]);
    }

    [Fact]
    public void TheBlocksBasePointBecomesTheInsertionPoint()
    {
        var sketch = new BlockSketch("B");
        sketch.AddLine(new P2(10, 10), new P2(11, 10));
        sketch.MoveBasePoint(new P2(10, 10));
        Near(new P2(0, 0), sketch.Paths[0].Points[0]);
        Near(new P2(1, 0), sketch.Paths[0].Points[1]);
    }

    [Fact]
    public void WhatCannotBeDrawnIsCountedAndAttributeTagsAreKept()
    {
        var sketch = new BlockSketch("B");
        Assert.False(sketch.Simplified);
        Assert.True(sketch.IsEmpty);
        sketch.Skip("Text");
        sketch.Skip("text");
        sketch.Skip("Hatch");
        sketch.AddAttributeTag("NUM");
        sketch.AddAttributeTag("num");
        sketch.AddAttributeTag(" ");
        Assert.True(sketch.Simplified);
        Assert.Equal("2 text, 1 hatch", sketch.SkippedSummary());
        Assert.Equal(new[] { "NUM" }, sketch.AttributeTags);
    }
}
