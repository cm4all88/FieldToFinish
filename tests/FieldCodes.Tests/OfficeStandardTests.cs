using FieldCodes.Easements;
using FieldCodes.Exhibits;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>Office exhibit standards: viewport layer rules, title block attribute mapping, sheet blocks,
/// table columns, and the office profiles built from delivered exhibits.</summary>
public sealed class OfficeStandardTests
{
    private static string Repo([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    [Fact]
    public void ViewportLayerRulesMatchFirstRuleWithWildcards()
    {
        var x = new ExhibitSettings
        {
            ViewportLayerRules = "V-CTRL-PMX_-PNTS-E=Show; *-PNTS-E=Hide; V-SURF-*=Relevant; V-ESMT-*-TEXT=User; bogus=Maybe",
            FreezeInViewport = "V-ESMT-TEXT-E",
            ExistingEasementLayers = "V-ESMT-SEWER-LINE"
        };
        var rules = x.LayerRules();
        Assert.Equal(ExhibitSettings.Show, ExhibitSettings.ActionFor(rules, "V-CTRL-PMX_-PNTS-E"));   // first match wins over *-PNTS-E
        Assert.Equal(ExhibitSettings.Hide, ExhibitSettings.ActionFor(rules, "v-util-powr-pnts-e"));    // case does not matter
        Assert.Equal(ExhibitSettings.Relevant, ExhibitSettings.ActionFor(rules, "V-SURF-BLDG-E"));
        Assert.Equal(ExhibitSettings.User, ExhibitSettings.ActionFor(rules, "V-ESMT-SEWER-TEXT"));
        Assert.Equal(ExhibitSettings.Hide, ExhibitSettings.ActionFor(rules, "V-ESMT-TEXT-E"));         // the older freeze list still works
        Assert.Equal(ExhibitSettings.Show, ExhibitSettings.ActionFor(rules, "V-ESMT-SEWER-LINE"));     // and the existing easement layers
        Assert.Null(ExhibitSettings.ActionFor(rules, "V-PROP-BNDY-E"));                                 // no rule: left as it is
        Assert.Null(ExhibitSettings.ActionFor(rules, "bogus"));                                         // an unknown action is ignored
        Assert.True(ExhibitSettings.Wildcard("C-BNDY-LIMT*", "C-BNDY-LIMT-PATT-CULVERT"));
        Assert.False(ExhibitSettings.Wildcard("V-TEXT-ESMT", "V-TEXT-ESMT-DETAIL"));
    }

    [Fact]
    public void SheetBlocksCarryLayerDynamicValuesAndScale()
    {
        var x = new ExhibitSettings { SheetBlocks = "PMXLogo@1.054,1.973@G-BORD-LOGO@Visibility1=Puyallup,scale=0.68; WA-stamp-here@6.512,2.06; broken@x,y" };
        var blocks = x.SheetBlockList();
        Assert.Equal(2, blocks.Count);
        Assert.Equal("PMXLogo", blocks[0].Name);
        Assert.Equal(1.054, blocks[0].X, 9);
        Assert.Equal("G-BORD-LOGO", blocks[0].Layer);
        Assert.Equal("Puyallup", blocks[0].Properties["visibility1"]);
        Assert.Equal(0.68, blocks[0].Scale, 9);
        Assert.False(blocks[0].Properties.ContainsKey("scale"));
        Assert.Null(blocks[1].Layer);
        Assert.Equal(1, blocks[1].Scale);
    }

    [Fact]
    public void TitleBlockAttributesMapByTagAndSheetNumbersSplit()
    {
        var x = new ExhibitSettings { TitleBlockAttributes = "JobNo={projectNumber}; SheetNo={sheetNo}; SheetOf={sheetOf}; DrawnBy={preparedBy}" };
        var map = x.AttributeMap();
        Assert.Equal(new[] { "JobNo", "SheetNo", "SheetOf", "DrawnBy" }, map.Select(m => m.Key));
        var info = new ExhibitInfo { Sheet = "2 OF 3", ProjectNumber = "2169171001", PreparedBy = "cmm" };
        Assert.Equal("2", info.Fill("{sheetNo}"));
        Assert.Equal("3", info.Fill("{sheetOf}"));
        Assert.Equal("2169171001", info.Fill(map[0].Value));
        Assert.Equal("CMM", info.Fill("{preparedBy}"));
        Assert.Equal(string.Empty, info.Fill("{checkedBy}"));                                           // blank stays blank, not "{checkedBy}"
        Assert.Equal("1", new ExhibitInfo { Sheet = "1 OF 1" }.Fill("{sheetOf}"));
    }

    [Fact]
    public void TableColumnsFollowTheProfile()
    {
        var columns = ExhibitSettings.Columns("LINE NO.={id}|LENGTH={distance}|DIRECTION={bearing}");
        Assert.Equal(new[] { "LINE NO.", "LENGTH", "DIRECTION" }, columns.Select(c => c.Key));
        Assert.Equal(new[] { "{id}", "{distance}", "{bearing}" }, columns.Select(c => c.Value));
        Assert.Empty(ExhibitSettings.Columns("no columns here"));
    }

    [Fact]
    public void TheReviewIsQuietAboutWhatOfficeSheetsDoOnPurpose()
    {
        var viewport = new SheetRect(1.07, 1.922, 7.46, 8.918);
        var printable = new SheetRect(1.0, 1.0, 7.5, 10.0);
        var boxes = new List<SheetBox>
        {
            // The office title sits right on the (no-plot) viewport's top edge; the logo and scale bar in its bottom band.
            new() { Key = "TITLE", Kind = "TITLE", Description = "The title", Rect = new SheetRect(1.01, 8.93, 7.51, 9.86) },
            new() { Key = "SCALEBAR", Kind = "SYMBOL", Description = "The scale bar", Rect = new SheetRect(4.31, 1.22, 5.46, 2.0) },
            // A full-sheet office title block.
            new() { Key = "BORDER", Kind = "TITLEBLOCK", Description = "The title block", Rect = new SheetRect(0, 0, 8.5, 11) },
            // A label outside the viewport is reported once, not also as outside the printable area.
            new() { Key = "LABEL:x:1", Kind = "LABEL", Description = "Label", Rect = new SheetRect(8.0, 5.0, 8.4, 5.1) },
        };
        var office = ExhibitReview.Check(boxes, viewport, printable, new List<Tuple<P2, P2>>(), null, viewportFramePlots: false);
        Assert.DoesNotContain(office, r => r.Message.Contains("viewport edge"));
        Assert.DoesNotContain(office, r => r.ItemKey == "BORDER");
        Assert.Single(office, r => r.ItemKey == "LABEL:x:1");
        Assert.Contains(office, r => r.ItemKey == "LABEL:x:1" && r.Message.Contains("runs outside the viewport"));

        // A plotted viewport frame keeps the edge warnings.
        var plotted = ExhibitReview.Check(boxes, viewport, printable, new List<Tuple<P2, P2>>(), null, viewportFramePlots: true);
        Assert.Contains(plotted, r => r.ItemKey == "SCALEBAR" && r.Message.Contains("viewport edge"));
    }

    [Fact]
    public void TheOfficeProfilesLoadValidateAndLeaveTheGenericDefaultsAlone()
    {
        foreach (var name in new[] { "PMX SURVEY EXHIBIT", "PMX SURVEY EXHIBIT TABLES" })
        {
            var path = Path.Combine(Repo(), "config", "profiles", name + ".json");
            Assert.True(File.Exists(path), path);
            var s = FtfSettings.Load(path);
            var problems = new List<string>();
            s.Exhibits.Validate(problems);
            Assert.Empty(problems);
            Assert.Equal("PMX Survey BW.ctb", s.Exhibits.PlotStyleTable);
            Assert.Equal("ANSI_full_bleed_A_(8.50_x_11.00_Inches)", s.Exhibits.MediaName);
            Assert.Equal("G-ScalebarFig", s.Exhibits.ScaleBarBlock);
            Assert.Equal("1\" = 60'", s.Exhibits.ScaleBarVisibility.Replace("{scale}", "60"));
            Assert.Equal(1.42857, s.Exhibits.TitleFirstLineScale, 5);
            Assert.Contains(s.Exhibits.SheetBlockList(), b => b.Name == "PMXLogo" && b.Properties["Visibility1"] == "Puyallup");
            // Context layers are never hidden by the office rules.
            var rules = s.Exhibits.LayerRules();
            foreach (var context in new[] { "V-PROP-BNDY-E", "V-PROP-RWAY-E", "V-PROP-SECT-E", "V-CTRL-MONU-SYMB-E", "V-PROP-LOTL-E" })
                Assert.NotEqual(ExhibitSettings.Hide, ExhibitSettings.ActionFor(rules, context));
            Assert.Equal(ExhibitSettings.Hide, ExhibitSettings.ActionFor(rules, "V-TOPO-CONT-TEXT"));
            Assert.Equal(ExhibitSettings.Show, ExhibitSettings.ActionFor(rules, "V-CTRL-PMX_-PNTS-E"));
        }

        // The generic profile is not the office's.
        var generic = new ExhibitSettings();
        Assert.Equal(string.Empty, generic.PlotStyleTable);
        Assert.Equal(string.Empty, generic.ScaleBarBlock);
        Assert.Equal(string.Empty, generic.SheetBlocks);
        Assert.Equal(1, generic.TitleFirstLineScale);
        Assert.True(generic.AreaLabelTitle);
        Assert.True(generic.StripLabelAlong);
        Assert.Equal("NoLeader", generic.NarrowStripLabel);
        Assert.True(new EasementSettings().AskTableLocation);
    }
}
