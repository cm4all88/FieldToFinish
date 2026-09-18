using FieldCodes.Easements;
using FieldCodes.Exhibits;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>Exhibit planning: scale, view rotation, paper placement, scale bar, area rows,
/// stale detection and the collision review. Uses the Parcel 3 sewer easement from the
/// office's Exhibit B (1" = 60').</summary>
public sealed class ExhibitPlannerTests
{
    private static readonly ExhibitSettings Settings = new();
    private static P2 P(double x, double y) => new(x, y);

    private static P2 Along(P2 from, double azimuthDegrees, double distance)
    {
        var a = azimuthDegrees * Math.PI / 180;
        return new P2(from.X + Math.Sin(a) * distance, from.Y + Math.Cos(a) * distance);
    }

    private static double Dms(int d, int m, int s) => d + m / 60.0 + s / 3600.0;

    [Fact]
    public void TheLargestReadableScaleThatFitsIsChosen()
    {
        // 300' x 100' in a 7" x 7" viewport with a 12% margin: 300 / (7 * 0.76) = 56.4 ft/in, so 1" = 60'.
        var fit = ExhibitPlanner.Choose(new[] { P(0, 0), P(300, 100) }, 7, 7, Settings.ScaleList(), 0.12, false, 1, null, null);
        Assert.Equal(60, fit.Scale);
        Assert.True(fit.Fits);
        Assert.Equal(0, fit.RotationDegrees);
        Assert.Equal(150, fit.ViewCenter.X, 9);
    }

    [Fact]
    public void TheExhibitBParcel3EasementFitsAt60NorthUpLikeTheSheet()
    {
        var pob = P(5000, 5000);
        var angle = Along(pob, Dms(52, 36, 35), 60.16);
        var terminus = Along(angle, Dms(1, 22, 31), 277.76);
        var poc = Along(pob, 360 - Dms(87, 55, 53), 65.10);
        var nw = Along(terminus, 360 - Dms(87, 55, 53), 111.94);
        var fit = ExhibitPlanner.Choose(new[] { pob, angle, terminus, poc, nw }, 7, 7, Settings.ScaleList(), 0.12, false, 1, null, null);
        Assert.Equal(60, fit.Scale);
    }

    [Fact]
    public void ALongDiagonalEasementReadsLargerWithTheViewTurned()
    {
        var points = new[] { P(0, 0), P(707, 707), P(714, 700), P(7, -7) };
        var north = ExhibitPlanner.Choose(points, 7, 3, Settings.ScaleList(), 0.12, false, 1, null, null);
        var turned = ExhibitPlanner.Choose(points, 7, 3, Settings.ScaleList(), 0.12, true, 1, null, null);
        Assert.True(turned.Scale < north.Scale);
        Assert.NotEqual(0, turned.RotationDegrees);
        Assert.Contains("view turned", turned.Reason);
    }

    [Fact]
    public void ACompactEasementStaysNorthUpEvenWhenRotationIsAllowed()
    {
        var fit = ExhibitPlanner.Choose(new[] { P(0, 0), P(100, 100) }, 7, 7, Settings.ScaleList(), 0.12, true, 1, null, null);
        Assert.Equal(0, fit.RotationDegrees);
    }

    [Fact]
    public void AForcedScaleThatIsTooSmallIsReportedAsNotFitting()
    {
        var fit = ExhibitPlanner.Choose(new[] { P(0, 0), P(1000, 10) }, 7, 7, Settings.ScaleList(), 0.12, false, 1, 20, null);
        Assert.Equal(20, fit.Scale);
        Assert.False(fit.Fits);
    }

    [Fact]
    public void ModelPointsLandOnTheSheetTurnedWithTheView()
    {
        var center = P(1000, 2000);
        var vp = P(4, 5);
        // North up, 1" = 20': a point 40' east of centre is 2" right.
        var east = ExhibitPlanner.ToPaper(P(1040, 2000), center, 20, 0, vp, 1);
        Assert.Equal(6, east.X, 9);
        Assert.Equal(5, east.Y, 9);
        // View turned 90 degrees: model north points to paper west, and the north arrow says so.
        var north = ExhibitPlanner.ToPaper(P(1000, 2040), center, 20, 90, vp, 1);
        Assert.Equal(2, north.X, 9);
        Assert.Equal(5, north.Y, 9);
        var arrow = ExhibitPlanner.NorthOnPaper(90);
        Assert.Equal(-1, arrow.X, 9);
    }

    [Fact]
    public void TheScaleBarIsWholeMultiplesOfTheScale()
    {
        var marks = ExhibitPlanner.ScaleBar(60, 2);
        Assert.Equal(new[] { 0.0, 30, 60, 90, 120 }, marks.Select(m => m.Key));
        Assert.Equal(2.0, marks[4].Value, 9);
    }

    [Fact]
    public void TheCombinedRowIsNotCalledALegalTotal()
    {
        var records = new List<EasementRecord>
        {
            new() { Title = "PERMANENT UTILITY EASEMENT", AreaSquareFeet = 8426 },
            new() { Title = "TEMPORARY CONSTRUCTION EASEMENT", AreaSquareFeet = 12740 }
        };
        var rows = ExhibitPlanner.AreaRows(records, Settings);
        Assert.Equal(3, rows.Count);
        Assert.Equal(21166, rows[2].SquareFeet);
        Assert.DoesNotContain("LEGAL", rows[2].Label);
        Assert.True(ExhibitPlanner.WantsAreaTable(records, Settings));
        Assert.False(ExhibitPlanner.WantsAreaTable(records.Take(1).ToList(), Settings));
    }

    [Fact]
    public void ComponentsGetTheirOwnAreaRows()
    {
        var r = new EasementRecord { Title = "SEWER EASEMENT", AreaSquareFeet = 1250, PrimaryAreaSquareFeet = 500, Components = new() { new EasementComponent { Label = "B", AreaSquareFeet = 750 } } };
        var rows = ExhibitPlanner.AreaRows(new[] { r }, Settings);
        Assert.Equal(new[] { "SEWER EASEMENT", "  COMPONENT A", "  COMPONENT B" }, rows.Select(x => x.Label));
    }

    [Fact]
    public void AnExhibitIsStaleWhenItsEasementChanges()
    {
        var r = new EasementRecord { Id = "e1", Title = "SEWER EASEMENT", AreaSquareFeet = 5069, DraftedFingerprint = "abc" };
        var exhibit = new ExhibitRecord();
        exhibit.Sources.Add(new ExhibitSource { EasementId = "e1", Title = r.Title, Fingerprint = ExhibitPlanner.SourceFingerprint(r) });
        Assert.Empty(ExhibitPlanner.StaleSources(exhibit, new[] { r }));
        r.AreaSquareFeet = 5100;
        Assert.Single(ExhibitPlanner.StaleSources(exhibit, new[] { r }));
        Assert.Contains("no longer stored", ExhibitPlanner.StaleSources(exhibit, new List<EasementRecord>())[0]);
    }

    [Fact]
    public void BlankExhibitInformationIsLeftOff()
    {
        var info = new ExhibitInfo { Title = "Exhibit B", County = "Snohomish County, Washington" };
        Assert.Equal("EXHIBIT B", info.Fill("{title}"));
        Assert.Equal(string.Empty, info.Fill("APN: {apn}"));
        Assert.Equal("SNOHOMISH COUNTY, WASHINGTON", info.Fill("{county}"));
    }

    // ================================================================ review

    [Fact]
    public void AnUglyExhibitGetsAReviewListInsteadOfPretendingToBeClean()
    {
        var viewport = new SheetRect(0.75, 2, 7.75, 9);
        var printable = new SheetRect(0.5, 0.5, 8, 10.5);
        var boxes = new List<SheetBox>
        {
            new() { Key = "LABEL:e1:3", Kind = "LABEL", Description = "Label N01°22'31\"E 277.76'", Rect = new SheetRect(7.5, 5, 8.4, 5.1) },
            new() { Key = "LINETABLE", Kind = "TABLE", Description = "Line table", Rect = new SheetRect(0.85, 8.2, 3, 9.6) },
            new() { Key = "NORTH", Kind = "SYMBOL", Description = "North arrow", Rect = new SheetRect(7.9, 10.2, 8.4, 10.9) },
            new() { Key = "AREA:e1", Kind = "AREALABEL", Description = "Title/area label", Rect = new SheetRect(3, 4, 5, 4.4) },
            new() { Key = "POINT:e1:POB", Kind = "LEADER", Description = "POINT OF BEGINNING leader", Rect = new SheetRect(1, 8.5, 2.5, 8.8) },
        };
        var lines = new List<Tuple<P2, P2>> { Tuple.Create(new P2(4, 3), new P2(4, 6)) };
        var review = ExhibitReview.Check(boxes, viewport, printable, lines, new[] { "Course L7 label does not fit at 1\" = 60'." });

        Assert.Contains(review, r => r.Message.Contains("runs outside the viewport"));
        Assert.Contains(review, r => r.Severity == "Error" && r.Message.Contains("North arrow is outside the printable area"));
        Assert.Contains(review, r => r.Message.Contains("Line table overlaps the viewport edge"));
        Assert.Contains(review, r => r.Message.Contains("overlaps POINT OF BEGINNING leader") || r.Message.Contains("POINT OF BEGINNING leader overlaps"));
        Assert.Contains(review, r => r.Message.Contains("sits on the easement lines"));
        Assert.Contains(review, r => r.Message.Contains("does not fit"));
    }

    [Fact]
    public void ACleanExhibitHasNothingToReview()
    {
        var viewport = new SheetRect(0.75, 2, 7.75, 9);
        var printable = new SheetRect(0.5, 0.5, 8, 10.5);
        var boxes = new List<SheetBox>
        {
            new() { Key = "LABEL:e1:1", Kind = "LABEL", Description = "Label", Rect = new SheetRect(2, 3, 3, 3.1) },
            new() { Key = "SCALEBAR", Kind = "SYMBOL", Description = "Scale bar", Rect = new SheetRect(5, 1.4, 7, 1.8) },
            new() { Key = "TITLE", Kind = "TITLE", Description = "Title", Rect = new SheetRect(2, 9.5, 6, 10.4) },
        };
        Assert.Empty(ExhibitReview.Check(boxes, viewport, printable, new List<Tuple<P2, P2>>(), null));
    }

    [Fact]
    public void AnAreaLabelGoesWhereNoLineOrLabelCrossesIt()
    {
        // A 5" x 1.67" construction area with a building hole in the middle of it.
        var outer = new List<P2> { P(1, 4), P(6, 4), P(6, 5.67), P(1, 5.67) };
        var hole = new List<P2> { P(3.3, 4.5), P(3.97, 4.5), P(3.97, 5.17), P(3.3, 5.17) };
        var lines = new List<Tuple<P2, P2>>();
        foreach (var loop in new[] { outer, hole })
            for (var i = 0; i < loop.Count; i++) lines.Add(Tuple.Create(loop[i], loop[(i + 1) % loop.Count]));
        var viewport = new SheetRect(0.75, 2, 7.75, 9);
        var centre = P(3.5, 4.835);

        var spot = ExhibitReview.ClearSpot(outer, new List<IList<P2>> { hole }, viewport, lines, null, 1.5, 0.3, centre);
        Assert.NotNull(spot);
        var box = new SheetRect(spot!.Value.X - 0.75, spot.Value.Y - 0.15, spot.Value.X + 0.75, spot.Value.Y + 0.15);
        Assert.DoesNotContain(lines, l => ExhibitReview.SegmentHitsRect(l.Item1, l.Item2, box));

        // A label that is already there is avoided too.
        var taken = new List<SheetRect> { box };
        var other = ExhibitReview.ClearSpot(outer, new List<IList<P2>> { hole }, viewport, lines, taken, 1.5, 0.3, centre);
        Assert.NotNull(other);
        Assert.False(new SheetRect(other!.Value.X - 0.75, other.Value.Y - 0.15, other.Value.X + 0.75, other.Value.Y + 0.15).Overlaps(box, 0));

        // Too big for the area: no place inside, so nothing is squeezed in...
        Assert.Null(ExhibitReview.ClearSpot(outer, new List<IList<P2>> { hole }, viewport, lines, null, 6, 0.3, centre));
        // ...but when a place beside the area is allowed, the label goes next to it, off its lines.
        var beside = ExhibitReview.ClearSpot(outer, new List<IList<P2>> { hole }, viewport, lines, null, 5.5, 0.3, centre, 40, 2.0);
        Assert.NotNull(beside);
        var besideBox = new SheetRect(beside!.Value.X - 2.75, beside.Value.Y - 0.15, beside.Value.X + 2.75, beside.Value.Y + 0.15);
        Assert.DoesNotContain(lines, l => ExhibitReview.SegmentHitsRect(l.Item1, l.Item2, besideBox));
        Assert.True(besideBox.MinY >= 5.67 || besideBox.MaxY <= 4);
    }

    [Fact]
    public void ATurnedLabelIsCheckedByItsOutlineNotItsBoundingBox()
    {
        // A label turned 45 degrees beside a diagonal line: its bounding box crosses the line, its outline does not.
        var line = Tuple.Create(P(1, 1), P(5, 5));
        var centre = P(3.4, 2.6);
        var quad = ExhibitReview.TurnedBox(centre, Math.PI / 4, 2.0, 0.3);
        Assert.False(ExhibitReview.SegmentHitsQuad(line.Item1, line.Item2, quad));
        var bounds = new SheetRect(quad.Min(p => p.X), quad.Min(p => p.Y), quad.Max(p => p.X), quad.Max(p => p.Y));
        Assert.True(ExhibitReview.SegmentHitsRect(line.Item1, line.Item2, bounds));

        var viewport = new SheetRect(0, 0, 8, 8);
        var review = ExhibitReview.Check(new List<SheetBox>
        {
            new() { Key = "AREA:e1", Kind = "AREALABEL", Description = "area label", Rect = bounds, Corners = quad }
        }, viewport, viewport, new List<Tuple<P2, P2>> { line }, null);
        Assert.DoesNotContain(review, r => r.Message.Contains("sits on the easement lines"));

        // The candidate on the line is passed over for the one beside it; with none clear, the least bad is used.
        var candidates = new List<Tuple<P2, double>> { Tuple.Create(P(3, 3), Math.PI / 4), Tuple.Create(centre, Math.PI / 4) };
        Assert.Equal(1, ExhibitReview.FirstClear(candidates, 2.0, 0.3, viewport, new List<Tuple<P2, P2>> { line }, null));
        var blocked = new List<SheetRect> { new(2.5, 1.5, 4.5, 3.5) };
        Assert.Null(ExhibitReview.FirstClear(candidates, 2.0, 0.3, viewport, new List<Tuple<P2, P2>> { line }, blocked));
        int conflicts;
        Assert.Equal(1, ExhibitReview.FewestConflicts(candidates, 2.0, 0.3, viewport, new List<Tuple<P2, P2>> { line }, blocked, out conflicts));
        Assert.Equal(10, conflicts);     // covering a label (10) outweighs crossing a line (1)
    }

    [Fact]
    public void TheDefaultSheetKeepsItsFurnitureOnThePrintableArea()
    {
        var s = new ExhibitSettings();
        var printable = new SheetRect(s.MarginIn, s.MarginIn, s.SheetWidthIn - s.MarginIn, s.SheetHeightIn - s.MarginIn);
        var viewport = new SheetRect(s.ViewportLeftIn, s.ViewportBottomIn, s.ViewportLeftIn + s.ViewportWidthIn, s.ViewportBottomIn + s.ViewportHeightIn);
        Assert.True(viewport.Within(printable, 0));
        // The area table (two columns) and the line table (three) fit between their corner and the margin.
        Assert.True(s.AreaTableX + (26 + 21) * s.TextHeightIn <= printable.MaxX);
        Assert.True(s.LineTableX + (9 + 12 + 17) * s.TextHeightIn <= s.AreaTableX);
        Assert.True(s.LineTableX + (8 + 10 + 10 + 14) * s.TextHeightIn <= s.AreaTableX);
        // Tables, scale bar, legend and information sit below the viewport, above the margin.
        foreach (var y in new[] { s.AreaTableY, s.LineTableY, s.ScaleBarY, s.NorthArrowY, s.LegendY, s.InfoY })
            Assert.InRange(y, printable.MinY + 0.3, viewport.MinY);
        Assert.True(s.TitleY <= printable.MaxY && s.TitleY - 4 * s.TitleTextHeightIn * 1.7 >= viewport.MaxY);
    }
}
