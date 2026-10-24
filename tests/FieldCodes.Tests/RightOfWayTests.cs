using FieldCodes.Easements;
using FieldCodes.Row;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// FTFROW's geometry and wording, with no CAD: the opposite right-of-way line and
/// centerline offset to the road side, the dimensions square across, rounded block
/// corners, offset monuments, cross-street width checks, and the words on all of it.
/// </summary>
public class RightOfWayTests
{
    private static RowMonument Mon(double x, double y, string? pt = null, MonumentKind kind = MonumentKind.Found, string desc = "1/2\" IRON ROD")
    {
        return new RowMonument { PointNumber = pt, Location = new P2(x, y), Kind = kind, Description = desc };
    }

    private static RowJob Run(RowRoadSide side, double width, params RowMonument[] monuments)
    {
        var job = new RowJob { Mode = RowMode.Run, RoadSide = side, WidthFeet = width, RoadName = "MAIN STREET", Reference = RowReference.RowLine };
        job.Monuments.AddRange(monuments);
        return job;
    }

    private static RowJob Ring(RowMode mode, RowFace face, params RowMonument[] monuments)
    {
        var job = new RowJob { Mode = mode };
        job.Monuments.AddRange(monuments);
        foreach (var unused in monuments) job.Faces.Add(face);
        return job;
    }

    private static RowMonument[] Square(double size = 100, bool counterClockwise = true)
    {
        var pts = counterClockwise
            ? new[] { new P2(0, 0), new P2(size, 0), new P2(size, size), new P2(0, size) }
            : new[] { new P2(0, 0), new P2(0, size), new P2(size, size), new P2(size, 0) };
        return pts.Select((p, i) => Mon(p.X, p.Y, "P" + (i + 1))).ToArray();
    }

    private static bool Near(P2 a, double x, double y) { return a.DistanceTo(new P2(x, y)) < 1e-6; }

    private static IEnumerable<RowLine> Of(RowPlan plan, RowLineSource source)
    {
        return plan.Lines.Where(l => l.Source == source);
    }

    // ---------------------------------------------------------------------- run

    [Fact]
    public void ARunOffsetsTheOppositeRightOfWayLineToTheRoadSide()
    {
        var plan = RowBuilder.Build(Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(100, 0)));

        Assert.True(plan.Ok, string.Join("; ", plan.Errors));
        var opposite = Of(plan, RowLineSource.Opposite).Single().Course;
        Assert.True(Near(opposite.Start, 0, -60));
        Assert.True(Near(opposite.End, 100, -60));
        Assert.Equal(RowLayerRole.RowLine, Of(plan, RowLineSource.Opposite).Single().Role);
    }

    [Fact]
    public void TheRoadOnTheLeftOffsetsTheOtherWay()
    {
        var plan = RowBuilder.Build(Run(RowRoadSide.Left, 60, Mon(0, 0), Mon(100, 0)));
        var opposite = Of(plan, RowLineSource.Opposite).Single().Course;
        Assert.True(Near(opposite.Start, 0, 60));
        Assert.True(Near(opposite.End, 100, 60));
    }

    [Fact]
    public void TheCenterlineSitsHalfWayAcrossOnItsOwnLayerRole()
    {
        var plan = RowBuilder.Build(Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(100, 0)));
        var centre = Of(plan, RowLineSource.Centerline).Single();
        Assert.Equal(RowLayerRole.Centerline, centre.Role);
        Assert.True(Near(centre.Course.Start, 0, -30));
    }

    [Fact]
    public void TheCenterlineCanBeLeftOut()
    {
        var job = Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(100, 0));
        job.DrawCenterline = false;
        Assert.Empty(Of(RowBuilder.Build(job), RowLineSource.Centerline));
    }

    [Fact]
    public void ThePickedLineIsTheMonumentLineNotTheRightOfWayAndIsLabelled()
    {
        var plan = RowBuilder.Build(Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(100, 0)));
        var picked = Of(plan, RowLineSource.Picked).Single();
        Assert.Equal(RowLayerRole.MonumentLine, picked.Role);
        Assert.True(picked.Annotate);
        Assert.Equal(90.0, picked.Data.AzimuthDegrees, 6);
        Assert.Equal(100.0, picked.Data.Length, 6);
    }

    [Fact]
    public void TheWidthIsDimensionedSquareAcrossAtBothEnds()
    {
        var plan = RowBuilder.Build(Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(100, 0)));

        Assert.Equal(2, plan.Dimensions.Count);
        Assert.Contains(plan.Dimensions, d => Near(d.From, 0, 0) && Near(d.To, 0, -60));
        Assert.Contains(plan.Dimensions, d => Near(d.From, 100, 0) && Near(d.To, 100, -60));
        Assert.All(plan.Dimensions, d => Assert.Equal(60.0, d.MeasuredFeet, 6));
    }

    [Fact]
    public void AnAnglePointOnTheOutsideOfTheTurnJoinsTheOffsetExactly()
    {
        // East then north; the road on the right is the outside of the turn.
        var plan = RowBuilder.Build(Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(100, 0), Mon(100, 100)));

        var pieces = Of(plan, RowLineSource.Opposite).Select(l => l.Course).ToList();
        Assert.Equal(2, pieces.Count);
        Assert.True(Near(pieces[0].Start, 0, -60));
        Assert.True(Near(pieces[0].End, 160, -60));
        Assert.True(Near(pieces[1].Start, 160, -60));
        Assert.True(Near(pieces[1].End, 160, 100));
    }

    [Fact]
    public void AnAnglePointOnTheInsideOfTheTurnJoinsTheOffsetExactly()
    {
        var plan = RowBuilder.Build(Run(RowRoadSide.Left, 60, Mon(0, 0), Mon(100, 0), Mon(100, 100)));

        var pieces = Of(plan, RowLineSource.Opposite).Select(l => l.Course).ToList();
        Assert.True(Near(pieces[0].End, 40, 60));
        Assert.True(Near(pieces[1].Start, 40, 60));
        Assert.True(Near(pieces[1].End, 40, 100));
    }

    [Fact]
    public void EveryMonumentDimensionIsOnlyDrawnWhereItLandsOnTheOffsetLine()
    {
        var job = Run(RowRoadSide.Left, 60, Mon(0, 0), Mon(100, 0), Mon(100, 100));
        job.EveryMonumentDimension = true;
        var plan = RowBuilder.Build(job);

        // The angle point's square foot lies beyond the inside join on both courses.
        Assert.Equal(2, plan.Dimensions.Count);
        Assert.Contains(plan.Warnings, w => w.Contains("monument 2"));
    }

    [Fact]
    public void EveryMonumentDimensionDimensionsAnOutsideAnglePoint()
    {
        var job = Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(100, 0), Mon(100, 100));
        job.EveryMonumentDimension = true;
        var plan = RowBuilder.Build(job);

        Assert.Equal(3, plan.Dimensions.Count);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void MonumentsOnTheCenterlineGetARightOfWayLineEitherSide()
    {
        var job = new RowJob
        {
            Mode = RowMode.Run, Reference = RowReference.Centerline, RoadName = "OAK",
            CenterlineLeftFeet = 25, CenterlineRightFeet = 35
        };
        job.Monuments.Add(Mon(0, 0));
        job.Monuments.Add(Mon(100, 0));
        var plan = RowBuilder.Build(job);

        Assert.True(plan.Ok, string.Join("; ", plan.Errors));
        var sides = Of(plan, RowLineSource.Opposite).Select(l => l.Course).ToList();
        Assert.Equal(2, sides.Count);
        Assert.Contains(sides, c => Near(c.Start, 0, 25));
        Assert.Contains(sides, c => Near(c.Start, 0, -35));
        Assert.Equal(RowLayerRole.Centerline, Of(plan, RowLineSource.Centerline).Single().Role);
        Assert.Contains(plan.Dimensions, d => Near(d.From, 0, 25) && Near(d.To, 0, -35) && System.Math.Abs(d.MeasuredFeet - 60) < 1e-9);
    }

    [Fact]
    public void ADrawingNotInFeetStillGetsFeetRight()
    {
        // Drawing units are half feet: 200 units is 100 feet, and 60 feet of road is 120 units.
        var job = Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(200, 0));
        job.UnitsPerFoot = 2.0;
        var plan = RowBuilder.Build(job);

        Assert.True(Near(Of(plan, RowLineSource.Opposite).Single().Course.Start, 0, -120));
        Assert.Equal(60.0, plan.Dimensions[0].MeasuredFeet, 6);

        RowLabels.Apply(plan, new RightOfWaySettings(), new DraftingLineType(), 2.0, "°");
        Assert.Contains("100.00'", Of(plan, RowLineSource.Picked).Single().Text.Single());
    }

    // ------------------------------------------------------------------ monuments

    [Fact]
    public void AnOffsetMonumentUsesTheCornerItMarksNotWhereItSits()
    {
        var offset = Mon(0, 0, "7");
        offset.IsOffset = true;
        offset.OffsetFeet = 5;
        offset.OffsetAzimuthDegrees = 90;     // the corner is 5' east of the monument
        var plan = RowBuilder.Build(Run(RowRoadSide.Right, 60, offset, Mon(105, 0)));

        Assert.True(plan.Ok, string.Join("; ", plan.Errors));
        Assert.True(Near(plan.Corners[0], 5, 0));
        Assert.Equal(100.0, Of(plan, RowLineSource.Picked).Single().Data.Length, 6);
        Assert.True(Near(Of(plan, RowLineSource.Opposite).Single().Course.Start, 5, -60));

        var mark = plan.Monuments[0];
        Assert.True(Near(mark.Location, 0, 0));
        Assert.True(Near(mark.Corner, 5, 0));
        Assert.NotNull(mark.Tie);
        Assert.Equal(5.0, mark.Tie.Length, 6);
        Assert.Equal(90.0, mark.Tie.AzimuthDegrees, 6);
        Assert.Null(plan.Monuments[1].Tie);
    }

    [Fact]
    public void AnOffsetMonumentWithoutADistanceIsRefused()
    {
        var offset = Mon(0, 0);
        offset.IsOffset = true;
        var plan = RowBuilder.Build(Run(RowRoadSide.Right, 60, offset, Mon(100, 0)));
        Assert.False(plan.Ok);
        Assert.Contains(plan.Errors, e => e.Contains("offset distance"));
    }

    [Fact]
    public void MonumentNotesGoOnTheSideAwayFromTheRoad()
    {
        var plan = RowBuilder.Build(Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(100, 0)));
        Assert.True(plan.Monuments[0].NoteDirection.Y > 0.99);       // road is south, note is north

        plan = RowBuilder.Build(Run(RowRoadSide.Left, 60, Mon(0, 0), Mon(100, 0)));
        Assert.True(plan.Monuments[0].NoteDirection.Y < -0.99);
    }

    // ----------------------------------------------------------------- blocks

    [Fact]
    public void ABlockOffsetsEveryFrontageOutwardAndJoinsTheCorners()
    {
        var job = Ring(RowMode.Block, RowFace.Frontage("MAIN", 60), Square());
        job.DrawCenterline = false;
        var plan = RowBuilder.Build(job);

        Assert.True(plan.Ok, string.Join("; ", plan.Errors));
        var opposite = Of(plan, RowLineSource.Opposite).Select(l => l.Course).ToList();
        Assert.Equal(4, opposite.Count);
        var corners = opposite.SelectMany(c => new[] { c.Start, c.End }).ToList();
        foreach (var expected in new[] { new P2(-60, -60), new P2(160, -60), new P2(160, 160), new P2(-60, 160) })
            Assert.Contains(corners, p => p.DistanceTo(expected) < 1e-6);
    }

    [Fact]
    public void ABlockPickedClockwiseOffsetsOutwardTooBecauseOutIsNotLeftOrRight()
    {
        var job = Ring(RowMode.Block, RowFace.Frontage("MAIN", 60), Square(100, counterClockwise: false));
        job.DrawCenterline = false;
        var plan = RowBuilder.Build(job);

        var corners = Of(plan, RowLineSource.Opposite).SelectMany(l => new[] { l.Course.Start, l.Course.End }).ToList();
        Assert.Contains(corners, p => p.DistanceTo(new P2(-60, -60)) < 1e-6);
        Assert.Contains(corners, p => p.DistanceTo(new P2(160, 160)) < 1e-6);
    }

    [Fact]
    public void DifferentRoadsOnDifferentSidesOffsetByTheirOwnWidths()
    {
        var job = Ring(RowMode.Block, RowFace.Frontage("MAIN", 60), Square());
        job.Faces[1] = RowFace.Frontage("OAK", 40);       // the east side
        job.DrawCenterline = false;
        var plan = RowBuilder.Build(job);

        // Bottom (60) meets east (40) where both offsets cross: (140, -60).
        Assert.Contains(Of(plan, RowLineSource.Opposite), l => Near(l.Course.End, 140, -60));
        Assert.Contains(plan.RoadLabels, r => r.RoadName == "MAIN");
        Assert.Contains(plan.RoadLabels, r => r.RoadName == "OAK");
    }

    [Fact]
    public void ABlockSideWithNoRoadIsNotOffset()
    {
        var job = Ring(RowMode.Block, RowFace.Frontage("MAIN", 60), Square());
        job.Faces[2] = RowFace.Plain();                  // the top
        job.DrawCenterline = false;
        var plan = RowBuilder.Build(job);

        Assert.True(plan.Ok, string.Join("; ", plan.Errors));
        Assert.Equal(3, Of(plan, RowLineSource.Opposite).Count());
        Assert.DoesNotContain(Of(plan, RowLineSource.Opposite), l => System.Math.Abs(l.Course.Start.Y - 160) < 1e-6);
        Assert.Contains(Of(plan, RowLineSource.Opposite), l => Near(l.Course.Start, -60, 100) || Near(l.Course.End, -60, 100));
        Assert.Equal(4, Of(plan, RowLineSource.Picked).Count());
    }

    [Fact]
    public void ARoundedBlockCornerIsATangentArcWithTheRightData()
    {
        var job = Ring(RowMode.Block, RowFace.Plain(), Square());
        job.CornerRadiiFeet.AddRange(new[] { 25.0, 0, 0, 0 });
        var plan = RowBuilder.Build(job);

        Assert.True(plan.Ok, string.Join("; ", plan.Errors));
        var arc = Of(plan, RowLineSource.Corner).Single();
        Assert.Equal(CourseKind.Arc, arc.Course.Kind);
        Assert.Equal(25.0, arc.Data.Radius!.Value, 6);
        Assert.Equal(90.0, arc.Data.DeltaDegrees!.Value, 6);
        Assert.Equal(25.0 * System.Math.PI / 2.0, arc.Data.Length, 6);
        Assert.True(Near(arc.Course.Start, 0, 25) && Near(arc.Course.End, 25, 0));
        Assert.True(Near(arc.Course.Center, 25, 25));
        // The two sides on either side of the arc are shortened to its tangent points.
        Assert.Contains(Of(plan, RowLineSource.Picked), l => System.Math.Abs(l.Data.Length - 75.0) < 1e-6);
    }

    [Fact]
    public void ARadiusThatDoesNotFitLeavesTheCornerSharpAndSaysSo()
    {
        var job = Ring(RowMode.Block, RowFace.Plain(), Square());
        job.CornerRadiiFeet.AddRange(new[] { 60.0, 60.0, 0, 0 });
        var plan = RowBuilder.Build(job);

        Assert.Empty(Of(plan, RowLineSource.Corner));
        Assert.Contains(plan.Warnings, w => w.Contains("do not fit"));
    }

    [Fact]
    public void ARoundedCornerLabelReadsLikeACurveTable()
    {
        var job = Ring(RowMode.Block, RowFace.Plain(), Square());
        job.CornerRadiiFeet.AddRange(new[] { 25.0, 0, 0, 0 });
        var plan = RowBuilder.Build(job);
        RowLabels.Apply(plan, new RightOfWaySettings(), new DraftingLineType(), 1.0, "°");

        var text = Of(plan, RowLineSource.Corner).Single().Text;
        Assert.Equal("R=25.00'", text[0]);
        Assert.Equal("L=39.27'", text[1]);
        Assert.Equal("Δ=90°00'00\"", text[2]);
    }

    // ------------------------------------------------------------ intersections

    [Fact]
    public void AnIntersectionChecksEachCrossingAgainstTheRecordWidth()
    {
        var job = Ring(RowMode.Intersection, RowFace.Cross("MAIN", 100), Square(100));
        job.Faces[0] = RowFace.Cross("MAIN", 100);
        job.Faces[1] = RowFace.Cross("OAK", 99.5);
        job.Faces[2] = RowFace.Cross("MAIN", 100);
        job.Faces[3] = RowFace.Cross("OAK", 100);
        var plan = RowBuilder.Build(job);

        Assert.True(plan.Ok, string.Join("; ", plan.Errors));
        Assert.Equal(4, plan.Dimensions.Count);
        Assert.All(plan.Dimensions, d => Assert.True(d.IsCross));
        Assert.Single(plan.Dimensions, d => !d.WithinTolerance);
        Assert.Contains(plan.Warnings, w => w.StartsWith("OAK") && w.Contains("99.5"));
        Assert.Empty(Of(plan, RowLineSource.Opposite));
        Assert.Equal(4, plan.Lines.Count(l => l.Role == RowLayerRole.CrossLine));
    }

    [Fact]
    public void ACornerRadiusIsIgnoredWhereASideCrossesARoad()
    {
        var job = Ring(RowMode.Intersection, RowFace.Cross("MAIN", 100), Square(100));
        job.CornerRadiiFeet.AddRange(new[] { 25.0, 25.0, 25.0, 25.0 });
        var plan = RowBuilder.Build(job);

        Assert.Empty(Of(plan, RowLineSource.Corner));
        Assert.Equal(4, plan.Notes.Count(n => n.Contains("does not apply")));
    }

    [Fact]
    public void ACrossLineWithNoRecordWidthStillReportsWhatItMeasures()
    {
        var job = Ring(RowMode.Intersection, RowFace.Cross("MAIN", 0), Square(100));
        var plan = RowBuilder.Build(job);
        Assert.Empty(plan.Warnings);
        RowLabels.Apply(plan, new RightOfWaySettings(), new DraftingLineType(), 1.0, "°");
        Assert.All(plan.Dimensions, d => Assert.Equal("100' R/W", d.Text));
    }

    // ------------------------------------------------------------- steps and jogs

    [Fact]
    public void CollinearSidesWithDifferentWidthsAreJoinedByAStepAndFlagged()
    {
        var pts = new[] { Mon(0, 0), Mon(50, 0), Mon(100, 0), Mon(100, 100), Mon(0, 100) };
        var job = new RowJob { Mode = RowMode.Block, DrawCenterline = false };
        job.Monuments.AddRange(pts);
        job.Faces.Add(RowFace.Frontage("MAIN", 60));
        job.Faces.Add(RowFace.Frontage("MAIN", 40));
        job.Faces.Add(RowFace.Plain());
        job.Faces.Add(RowFace.Plain());
        job.Faces.Add(RowFace.Plain());
        var plan = RowBuilder.Build(job);

        Assert.True(plan.Ok, string.Join("; ", plan.Errors));
        Assert.Contains(plan.Warnings, w => w.Contains("different amounts"));
        Assert.Single(Of(plan, RowLineSource.Connector));
    }

    // -------------------------------------------------------------- refusals

    [Fact]
    public void BadInputIsRefusedWithAnExplanation()
    {
        Assert.Contains("at least 2", RowBuilder.Build(Run(RowRoadSide.Right, 60, Mon(0, 0))).Errors[0]);
        Assert.Contains("same corner", RowBuilder.Build(Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(0, 0))).Errors[0]);
        Assert.Contains("width", RowBuilder.Build(Run(RowRoadSide.Right, 0, Mon(0, 0), Mon(100, 0))).Errors[0]);
        Assert.Contains("doubles back", RowBuilder.Build(Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(100, 0), Mon(50, 0))).Errors[0]);
    }

    [Fact]
    public void ARingWithTheWrongNumberOfSidesOrNoWidthIsRefused()
    {
        var job = Ring(RowMode.Block, RowFace.Frontage("MAIN", 60), Square());
        job.Faces.RemoveAt(3);
        Assert.Contains(RowBuilder.Build(job).Errors, e => e.Contains("sides"));

        job = Ring(RowMode.Block, RowFace.Frontage("MAIN", 0), Square());
        Assert.Contains(RowBuilder.Build(job).Errors, e => e.Contains("right-of-way width"));
    }

    [Fact]
    public void CenterlineMonumentsNeedBothSidesOrYouAreOnARowLine()
    {
        var job = new RowJob { Mode = RowMode.Run, Reference = RowReference.Centerline, CenterlineLeftFeet = 30 };
        job.Monuments.Add(Mon(0, 0));
        job.Monuments.Add(Mon(100, 0));
        Assert.Contains(RowBuilder.Build(job).Errors, e => e.Contains("both sides"));
    }

    [Fact]
    public void ANullJobIsAnErrorNotACrash()
    {
        Assert.False(RowBuilder.Build(null).Ok);
    }

    // -------------------------------------------------------------------- words

    [Fact]
    public void MonumentAndDimensionNotesFollowTheOfficeFormats()
    {
        var settings = new RightOfWaySettings();
        var found = Mon(0, 0, "12", MonumentKind.Found, "1/2\" iron rod");
        var set = Mon(100, 0, "13", MonumentKind.Set, "5/8\" iron rod w/ cap");
        var plan = RowBuilder.Build(Run(RowRoadSide.Right, 60, found, set));
        RowLabels.Apply(plan, settings, new DraftingLineType(), 1.0, "°");

        Assert.Equal("FND 1/2\" IRON ROD", plan.Monuments[0].Text[0]);
        Assert.Equal("SET 5/8\" IRON ROD W/ CAP", plan.Monuments[1].Text[0]);
        Assert.All(plan.Dimensions, d => Assert.Equal("60' R/W", d.Text));
        Assert.Equal("MAIN STREET", plan.RoadLabels.Single().Text);
    }

    [Fact]
    public void AnOffsetMonumentNoteSaysWhichWayToTheCorner()
    {
        var offset = Mon(0, 0, "7", MonumentKind.Set, "iron rod");
        offset.IsOffset = true;
        offset.OffsetFeet = 5;
        offset.OffsetAzimuthDegrees = 45;
        var plan = RowBuilder.Build(Run(RowRoadSide.Right, 60, offset, Mon(100, 0)));
        RowLabels.Apply(plan, new RightOfWaySettings(), new DraftingLineType(), 1.0, "°");

        Assert.Equal(2, plan.Monuments[0].Text.Count);
        Assert.Equal("5.00' OFFSET N 45°00'00\" E TO CORNER", plan.Monuments[0].Text[1]);
    }

    [Fact]
    public void TheOfficeCanSetItsOwnWordingAndCase()
    {
        var settings = new RightOfWaySettings
        {
            UpperCase = false, FoundFormat = "Found {desc} ({pt})", DimensionFormat = "{width}' Right-of-Way",
            RoadNameFormat = "{name} Rd"
        };
        var plan = RowBuilder.Build(Run(RowRoadSide.Right, 66, Mon(0, 0, "12", desc: "iron pin"), Mon(100, 0, "13")));
        RowLabels.Apply(plan, settings, new DraftingLineType(), 1.0, "°");

        Assert.Equal("Found iron pin (12)", plan.Monuments[0].Text[0]);
        Assert.Equal("66' Right-of-Way", plan.Dimensions[0].Text);
        Assert.Equal("MAIN STREET Rd", plan.RoadLabels[0].Text);
    }

    [Fact]
    public void RightOfWayLinesAreAnnotatedByTheirDraftingLineType()
    {
        var plan = RowBuilder.Build(Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(100, 0)));

        var combined = new DraftingLineType { DistanceDecimals = 2, FootSymbol = true };
        RowLabels.Apply(plan, new RightOfWaySettings(), combined, 1.0, "°");
        Assert.Equal("N 90°00'00\" E  100.00'", Of(plan, RowLineSource.Picked).Single().Text.Single());

        var stacked = new DraftingLineType { Stacked = true };
        RowLabels.Apply(plan, new RightOfWaySettings(), stacked, 1.0, "°");
        Assert.Equal(2, Of(plan, RowLineSource.Picked).Single().Text.Count);

        var none = new DraftingLineType { Annotation = DraftingLineType.AnnotationNone };
        RowLabels.Apply(plan, new RightOfWaySettings(), none, 1.0, "°");
        Assert.Empty(Of(plan, RowLineSource.Picked).Single().Text);

        var azimuth = new DraftingLineType { BearingFormat = DraftingLineType.BearingFormatAzimuth };
        RowLabels.Apply(plan, new RightOfWaySettings(), azimuth, 1.0, "°");
        Assert.StartsWith("90°00'00\"", Of(plan, RowLineSource.Picked).Single().Text.Single());
    }

    [Fact]
    public void TheOppositeLineAndCenterlineAreLabelledOnlyWhenAsked()
    {
        var job = Run(RowRoadSide.Right, 60, Mon(0, 0), Mon(100, 0));
        job.LabelOppositeLine = false;
        job.LabelCenterline = true;
        var plan = RowBuilder.Build(job);
        RowLabels.Apply(plan, new RightOfWaySettings(), new DraftingLineType(), 1.0, "°");

        Assert.Empty(Of(plan, RowLineSource.Opposite).Single().Text);
        Assert.NotEmpty(Of(plan, RowLineSource.Centerline).Single().Text);
    }

    // ------------------------------------------------------------------ settings

    [Fact]
    public void TheShippedRightOfWayStandardIsValidAndLeavesLayersUnconfigured()
    {
        var settings = new RightOfWaySettings();
        var problems = new List<string>();
        settings.Validate(problems);
        Assert.Empty(problems);
        Assert.Equal(string.Empty, settings.CenterlineLayer);
        Assert.Equal(string.Empty, settings.MonumentLayer);
        Assert.Equal("Right of Way", settings.LineTypeName);
    }

    [Fact]
    public void BrokenRightOfWaySettingsAreReported()
    {
        var settings = new RightOfWaySettings { DimensionFormat = "no token", DimensionPolicy = "Sometimes", WidthToleranceFeet = -1 };
        var problems = new List<string>();
        settings.Validate(problems);
        Assert.Equal(3, problems.Count);
    }

    [Fact]
    public void RightOfWaySettingsSurviveARoundTripAndAnOldFileGetsDefaults()
    {
        var s = new FtfSettings();
        s.RightOfWay.MonumentLayer = "V-MONU";
        s.RightOfWay.DimensionPolicy = RightOfWaySettings.DimensionsEvery;
        var again = Newtonsoft.Json.JsonConvert.DeserializeObject<FtfSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(s));
        Assert.Equal("V-MONU", again.RightOfWay.MonumentLayer);
        Assert.True(again.RightOfWay.DimensionEveryMonument);

        var old = Newtonsoft.Json.JsonConvert.DeserializeObject<FtfSettings>("{\"version\":\"1\"}");
        old.FillMissingSections();
        Assert.NotNull(old.RightOfWay);
    }
}
