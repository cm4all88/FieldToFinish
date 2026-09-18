using FieldCodes.Sheets;

namespace FieldCodes.Tests;

/// <summary>
/// Sheet production maths: match lines where adjacent plot windows meet, and the
/// planner that lays 17x11 sheets over a site for the fewest prints. Nothing is
/// ever guessed -- diagonal neighbours and coincident windows match nothing.
/// </summary>
public sealed class SheetPlanTests
{
    private static SheetWindow Window(string name, double minX, double minY,
                                      double maxX, double maxY)
        => new SheetWindow { Name = name, MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };

    // ------------------------------------------------------------ match lines

    [Fact]
    public void SideBySideSheetsGetOneVerticalMatchLineInTheOverlap()
    {
        // Sheet 2 starts 20 ft before sheet 1 ends: the seam is centred in the
        // overlap band and spans the shared height.
        var lines = SheetMatcher.FindMatchLines(new[]
        {
            Window("SHEET 1", 0, 0, 320, 200),
            Window("SHEET 2", 300, 0, 620, 200)
        });

        var line = Assert.Single(lines);
        Assert.True(line.Vertical);
        Assert.Equal(310.0, line.X1, 9);
        Assert.Equal(0.0, line.Y1, 9);
        Assert.Equal(200.0, line.Y2, 9);
        Assert.Equal("SHEET 1", line.SideAName);   // left
        Assert.Equal("SHEET 2", line.SideBName);   // right
    }

    [Fact]
    public void StackedSheetsGetAHorizontalMatchLine()
    {
        var lines = SheetMatcher.FindMatchLines(new[]
        {
            Window("SHEET 1", 0, 190, 320, 390),
            Window("SHEET 2", 0, 0, 320, 200)
        });

        var line = Assert.Single(lines);
        Assert.False(line.Vertical);
        Assert.Equal(195.0, line.Y1, 9);
        Assert.Equal("SHEET 2", line.SideAName);   // below
        Assert.Equal("SHEET 1", line.SideBName);   // above
    }

    [Fact]
    public void AFourSheetGridGetsFourSeams_AndNoDiagonalOnes()
    {
        var lines = SheetMatcher.FindMatchLines(new[]
        {
            Window("1", 0, 190, 320, 390), Window("2", 300, 190, 620, 390),
            Window("3", 0, 0, 320, 200),   Window("4", 300, 0, 620, 200)
        });

        Assert.Equal(4, lines.Count);
        Assert.Equal(2, lines.Count(l => l.Vertical));
        Assert.Equal(2, lines.Count(l => !l.Vertical));
    }

    [Fact]
    public void ButtingSheetsWithAHairlineGapStillSeam()
    {
        var lines = SheetMatcher.FindMatchLines(new[]
        {
            Window("A", 0, 0, 320, 200),
            Window("B", 321, 0, 641, 200)      // 1 ft gap on a 320 ft sheet
        });

        Assert.Single(lines);
    }

    [Fact]
    public void FarApartOrCoincidentSheetsMatchNothing()
    {
        Assert.Empty(SheetMatcher.FindMatchLines(new[]
        {
            Window("A", 0, 0, 320, 200),
            Window("B", 1000, 0, 1320, 200)    // a road apart
        }));

        Assert.Empty(SheetMatcher.FindMatchLines(new[]
        {
            Window("A", 0, 0, 320, 200),
            Window("B", 5, 5, 325, 205)        // near-duplicate views, not a seam
        }));
    }

    // ------------------------------------------------------------ the planner

    [Fact]
    public void ASiteSmallerThanOneSheetGetsOneCentredSheet()
    {
        // 17x11 at 1"=20' with half-inch margins prints 320 x 200 ft.
        var plan = SheetPlanner.Plan(100, 100, 300, 250, 320, 200, 0.05);

        var window = Assert.Single(plan.Windows);
        Assert.Equal("SHEET 1", window.Name);
        Assert.Equal(200.0, (window.MinX + window.MaxX) / 2, 6);   // centred on site
        Assert.Equal(175.0, (window.MinY + window.MaxY) / 2, 6);
    }

    [Fact]
    public void AWideSiteLaysOutLandscapeAcross()
    {
        var plan = SheetPlanner.Plan(0, 0, 900, 180, 320, 200, 0.05);

        Assert.True(plan.Landscape);
        Assert.Equal(1, plan.Rows);
        Assert.Equal(3, plan.Columns);
    }

    [Fact]
    public void ATallNarrowSiteTurnsTheSheetsPortrait()
    {
        // 180 ft wide, 900 ft tall: portrait sheets (200 wide, 320 tall) cover it
        // in a single 3-sheet column; landscape would need more.
        var plan = SheetPlanner.Plan(0, 0, 180, 900, 320, 200, 0.05);

        Assert.False(plan.Landscape);
        Assert.Equal(3, plan.Rows);
        Assert.Equal(1, plan.Columns);
    }

    [Fact]
    public void SheetsNumberLikeAPlanSet_TopRowFirstLeftToRight()
    {
        var plan = SheetPlanner.Plan(0, 0, 600, 380, 320, 200, 0.05);

        Assert.Equal(2, plan.Rows);
        Assert.Equal(2, plan.Columns);

        var first = plan.Windows.First(w => w.Name == "SHEET 1");
        var last = plan.Windows.First(w => w.Name == "SHEET 4");
        Assert.True(first.MaxY > last.MaxY);       // sheet 1 on the top row
        Assert.True(first.MinX < last.MinX);       // and at the left
    }

    // -------------------------------------------------------------- the key map

    [Fact]
    public void TheKeymapShrinksTheGridPreservingPositionsAndAspect()
    {
        var plan = SheetPlanner.Plan(0, 0, 600, 380, 320, 200, 0.05);
        var map = KeymapBuilder.Build(plan.Windows, "SHEET 4", 2.0);

        Assert.Equal(2.0, map.Width, 9);
        Assert.Equal(4, map.Cells.Count);

        // Aspect: keymap height / width equals the covered site's ratio.
        var coverW = plan.Windows.Max(w => w.MaxX) - plan.Windows.Min(w => w.MinX);
        var coverH = plan.Windows.Max(w => w.MaxY) - plan.Windows.Min(w => w.MinY);
        Assert.Equal(coverH / coverW * 2.0, map.Height, 9);

        // Exactly one cell is the current sheet, and it reads by its number.
        var current = Assert.Single(map.Cells.Where(c => c.Current));
        Assert.Equal("SHEET 4", current.Name);
        Assert.Equal("4", current.ShortName);

        // SHEET 1 sits top-left in the map, like it does on the ground.
        var first = map.Cells.First(c => c.Name == "SHEET 1");
        var last = map.Cells.First(c => c.Name == "SHEET 4");
        Assert.True(first.Y > last.Y);
        Assert.True(first.X < last.X);
    }

    [Fact]
    public void AnUnnumberedSheetKeepsItsNameInTheKeymap()
    {
        Assert.Equal("12", KeymapBuilder.ShortName("SHEET 12"));
        Assert.Equal("OVERALL", KeymapBuilder.ShortName("OVERALL"));
    }

    [Fact]
    public void PlannedNeighboursOverlapSoTheMatchLinesHaveASeam()
    {
        var plan = SheetPlanner.Plan(0, 0, 600, 180, 320, 200, 0.05);
        var lines = SheetMatcher.FindMatchLines(plan.Windows);

        Assert.Equal(plan.Columns - 1, lines.Count);
        Assert.All(lines, l => Assert.True(l.Vertical));
    }
}
