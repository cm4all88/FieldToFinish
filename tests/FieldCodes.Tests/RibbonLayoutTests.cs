using System.Text.RegularExpressions;
using FieldCodes.Commands;

namespace FieldCodes.Tests;

/// <summary>
/// The ribbon, held to its promises without Civil 3D: nothing that was on it disappears, every
/// button types a command the plugin really registers, the four tabs have the panels they should,
/// no command sits twice on one tab, no two commands share an icon, every icon is one FtfIcons
/// draws, and every button says what it does. The plugin's own source is read for the registered
/// commands and the icons, since the Civil 3D project cannot be loaded here.
/// </summary>
public sealed class RibbonLayoutTests
{
    /// <summary>
    /// Every command on the ribbon before it was reorganised into four tabs: the FTF and FTF
    /// Boundary tabs of src/FieldCodes.Cad/FtfRibbon.cs at integration b9b9263. A drafter who found
    /// a command on the ribbon must still find it there, so this list only ever grows.
    /// </summary>
    private static readonly string[] CompatibilityBaseline =
    {
        // FTF tab -- Label
        "FTFLABELALL", "FTFLABELLINE", "FTFLABELBETWEEN", "FTFLABELSTAIRS", "FTFLABELDRIVEWAY",
        "FTFBENCH", "FTFSPOT", "FTFCONTOUR", "FTFTILDE", "FTFROWDIM",
        // FTF tab -- Finish
        "FTFRUN", "FTFPOINTS", "FTFORIENT", "FTFORIENTRESET", "FTFDRIP", "FTFLABELS", "FTFTAGS",
        "FTFTABLE", "FTFORDER", "FTFCONTROL", "FTFLEGEND",
        // FTF tab -- Dips
        "FTFDIP", "FTFDIPCHECK",
        // FTF tab -- Compare
        "FTFCOMPARE", "FTFCOMPARERUN", "FTFCOMPAREAPPLY", "FTFCOMPARERESTORE", "FTFCOMPAREQC",
        "FTFCOMPARESAVE", "FTFCOMPAREBATCH", "FTFTRIM", "FTFTRIMRESTORE",
        // FTF tab -- Review
        "FTF", "FTFLINES", "FTFWHERE", "FTFSETUP", "FTFPROFILE", "FTFVERSION",
        // FTF tab -- Sheets
        "FTFSHEETPLAN", "FTFSHEETMAKE", "FTFSHEETS", "FTFKEYMAP", "FTFMATCHLINE",
        // FTF tab -- Clean up
        "FTFCLEAN",
        // FTF Boundary tab -- Record
        "FTFRECORD", "FTFRECORDCHECK", "FTFRECORDLABEL", "FTFRECORDSOURCE", "FTFRECORDREBUILD",
        // FTF Boundary tab -- Right of Way & Lots
        "FTFROW", "FTFLOTS",
        // FTF Boundary tab -- Easements
        "STRIPEASEMENT", "PORTIONEASEMENT", "CONSTRUCTIONAREA", "FTFEASEMENTEXCLUDE",
        "FTFEASEMENTCOMPONENT", "FTFEASEMENTCHECK", "FTFEASEMENTINSPECT",
        // FTF Boundary tab -- Exhibits
        "FTFEXHIBIT", "FTFEASEMENTLEGAL", "FTFEASEMENTGROUP", "FTFEXHIBITREBUILD",
    };

    /// <summary>The tab ids the two-tab ribbon used. A reload must replace them, never keep them beside the new tabs.</summary>
    private static readonly string[] OldTabIds = { "FTF_RIBBON_TAB", "FTF_BOUNDARY_RIBBON_TAB" };

    // ------------------------------------------------------------ the commands

    [Fact]
    public void TheBaselineIsTheWholeOldRibbon()
    {
        Assert.Equal(62, CompatibilityBaseline.Length);
        Assert.Equal(CompatibilityBaseline.Length, CompatibilityBaseline.Distinct().Count());
    }

    [Fact]
    public void EveryCommandThatWasOnTheRibbonStillIs()
    {
        var now = new HashSet<string>(RibbonLayout.Commands, StringComparer.OrdinalIgnoreCase);
        var gone = CompatibilityBaseline.Where(c => !now.Contains(c)).ToList();
        Assert.True(gone.Count == 0, "No longer on the ribbon: " + string.Join(", ", gone));
    }

    [Fact]
    public void EveryButtonTypesACommandThePluginRegisters()
    {
        var registered = RegisteredCommands();
        // The scan itself has to have worked, or every button would pass for the wrong reason.
        Assert.Contains("FTFRUN", registered);
        Assert.Contains("STRIPEASEMENT", registered);
        Assert.True(registered.Count > 60, "Only " + registered.Count + " registered commands were found under src/FieldCodes.Cad.");

        var unknown = RibbonLayout.AllButtons.Where(b => !registered.Contains(b.Command)).Select(b => b.ToString()).ToList();
        Assert.True(unknown.Count == 0, "Buttons whose command is not registered: " + string.Join(", ", unknown));
    }

    [Fact]
    public void CommandNamesAreWrittenExactlyAsRegistered()
    {
        // The button types its command verbatim: no stray spaces, no lower case, no alias.
        foreach (var button in RibbonLayout.AllButtons)
            Assert.Matches("^[A-Z]+$", button.Command);
    }

    // -------------------------------------------------------- tabs and panels

    [Fact]
    public void TheFourTabsHaveTheirPanels()
    {
        var expected = new[]
        {
            (RibbonLayout.FieldToFinishTabId, "FieldToFinish",
                new[] { "Process", "Points & Symbols", "Labels", "Trees", "Utility Features", "Settings", "Reports" }),
            (RibbonLayout.DraftingTabId, "Survey Drafting",
                new[] { "Elevations", "Line Labels", "Drafting", "Sheets" }),
            (RibbonLayout.BoundaryTabId, "Boundary",
                new[] { "Recorded Surveys", "Calculations & Closure", "Right of Way & Lots", "Record Comparison" }),
            (RibbonLayout.EasementsTabId, "Easements",
                new[] { "Create", "Legal Descriptions", "Exhibits", "Closure Verification" }),
        };

        Assert.Equal(expected.Select(e => e.Item2), RibbonLayout.Tabs.Select(t => t.Title));
        foreach (var (id, title, panels) in expected)
        {
            var tab = RibbonLayout.Tabs.Single(t => t.Title == title);
            Assert.Equal(id, tab.Id);
            Assert.Equal(panels, tab.Panels.Select(p => p.Title));
            Assert.All(tab.Panels, p => Assert.NotEmpty(p.Buttons));
        }
    }

    [Fact]
    public void NoCommandAppearsTwiceOnATab()
    {
        foreach (var tab in RibbonLayout.Tabs)
        {
            var twice = tab.Buttons.GroupBy(b => b.Command, StringComparer.OrdinalIgnoreCase)
                                   .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(twice.Count == 0, tab.Title + " has these twice: " + string.Join(", ", twice));
        }
    }

    [Fact]
    public void EachKindOfWorkStaysOnItsOwnTab()
    {
        // A command used by two kinds of work may sit on two tabs (Draw Lines, ROW Width, Clean
        // Drafting); these belong to exactly one.
        AssertOnlyOn(RibbonLayout.EasementsTabId, c =>
            c.Contains("EASEMENT") || c.Contains("EXHIBIT") || c == "CONSTRUCTIONAREA");
        AssertOnlyOn(RibbonLayout.BoundaryTabId, c =>
            c.StartsWith("FTFRECORD") || c.StartsWith("FTFCOMPARE") || c.StartsWith("FTFTRIM") || c == "FTFROW" || c == "FTFLOTS");
        AssertOnlyOn(RibbonLayout.FieldToFinishTabId, c =>
            c.StartsWith("FTFDIP") || c.StartsWith("FTFORIENT") ||
            new[] { "FTF", "FTFRUN", "FTFPOINTS", "FTFLABELALL", "FTFLABELS", "FTFDRIP", "FTFTAGS", "FTFTABLE", "FTFORDER", "FTFCLEAN", "FTFSETUP" }.Contains(c));
        AssertOnlyOn(RibbonLayout.DraftingTabId, c =>
            c.StartsWith("FTFSHEET") || c == "FTFKEYMAP" || c == "FTFMATCHLINE" || c == "FTFSPOT" || c == "FTFCONTOUR" ||
            c == "FTFLABELLINE" || c == "FTFLABELBETWEEN" || c == "FTFLABELDRIVEWAY" || c == "FTFLABELSTAIRS");
    }

    [Fact]
    public void TabIdsAreUniqueAndEveryOldIdIsReplacedOrRetired()
    {
        var ids = RibbonLayout.Tabs.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.StartsWith("FTF_", id));
        Assert.Empty(ids.Intersect(RibbonLayout.RetiredTabIds));
        // FtfRibbon takes every one of these off before it adds its own, so none can linger.
        foreach (var old in OldTabIds)
            Assert.True(ids.Contains(old) || RibbonLayout.RetiredTabIds.Contains(old), old + " would be left on a reloaded ribbon.");
    }

    // ------------------------------------------------------------------ icons

    [Fact]
    public void MajorCommandsHaveIconsOfTheirOwn()
    {
        Assert.All(RibbonLayout.AllButtons.Where(b => b.Size == RibbonButtonSize.Large), b => Assert.True(b.IsMajor, b + " is large, so major."));
        var shared = RibbonLayout.AllButtons.Where(b => b.IsMajor)
            .GroupBy(b => b.Icon)
            .Select(g => g.Select(b => b.Command).Distinct().ToList())
            .Where(commands => commands.Count > 1)
            .Select(commands => string.Join(" + ", commands))
            .ToList();
        Assert.True(shared.Count == 0, "Major commands sharing an icon: " + string.Join("; ", shared));
    }

    [Fact]
    public void NoTwoCommandsShareAnIconAndACommandLooksTheSameEverywhere()
    {
        // Stricter than the major rule: a step or variant gets its family's glyph with a badge of
        // its own, never the same picture as another command.
        var shared = RibbonLayout.AllButtons.GroupBy(b => b.Icon)
            .Select(g => g.Select(b => b.Command).Distinct().ToList())
            .Where(commands => commands.Count > 1)
            .Select(commands => string.Join(" + ", commands)).ToList();
        Assert.True(shared.Count == 0, "Commands sharing an icon: " + string.Join("; ", shared));

        var inconsistent = RibbonLayout.AllButtons.GroupBy(b => b.Command)
            .Where(g => g.Select(b => b.Icon).Distinct().Count() > 1 || g.Select(b => b.Tip).Distinct().Count() > 1)
            .Select(g => g.Key).ToList();
        Assert.True(inconsistent.Count == 0, "A command with a different icon or tooltip on another tab: " + string.Join(", ", inconsistent));
    }

    [Fact]
    public void EveryIconIsOneFtfIconsDraws()
    {
        var source = File.ReadAllText(Path.Combine(Repo(), "src", "FieldCodes.Cad", "FtfRibbon.cs"));
        var drawn = new HashSet<string>(
            Regex.Matches(source, @"public static ImageSource (\w+)\(int px\)").Select(m => m.Groups[1].Value));
        Assert.Contains("Run", drawn);   // the scan found the icons

        var missing = RibbonLayout.AllButtons.Where(b => !drawn.Contains(b.Icon)).Select(b => b + " -> " + b.Icon).ToList();
        Assert.True(missing.Count == 0, "Icons FtfIcons does not draw: " + string.Join(", ", missing));
    }

    // --------------------------------------------------------------- the words

    [Fact]
    public void EveryButtonSaysWhatItDoes()
    {
        foreach (var b in RibbonLayout.AllButtons)
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Label), b.Command + " has no label.");
            Assert.False(string.IsNullOrWhiteSpace(b.Tip), b + " has no tooltip.");
            Assert.NotEqual(b.Command, b.Tip);                 // the old small buttons' tooltip was just the command
            Assert.EndsWith(".", b.Tip);
            Assert.True(b.Tip.Length <= 200, b + "'s tooltip is more than a sentence or two.");
            Assert.True(b.Label.Split('\n').Length <= (b.Size == RibbonButtonSize.Large ? 2 : 1), b + "'s label has too many lines.");
            Assert.True(b.Label.Split('\n').All(line => line.Length <= 16), b + "'s label is too wide for a ribbon button.");
        }
    }

    [Fact]
    public void TheRibbonPageListsEveryTabPanelAndCommand()
    {
        var page = File.ReadAllText(Path.Combine(Repo(), "docs", "ribbon.md"));
        foreach (var tab in RibbonLayout.Tabs)
        {
            Assert.Contains(tab.Title, page);
            foreach (var panel in tab.Panels) Assert.Contains(panel.Title, page);
        }
        foreach (var command in RibbonLayout.Commands) Assert.Contains("`" + command + "`", page);
    }

    // ----------------------------------------------------------------- helpers

    private static void AssertOnlyOn(string tabId, Func<string, bool> belongs)
    {
        var strays = RibbonLayout.Tabs.Where(t => t.Id != tabId)
            .SelectMany(t => t.Buttons.Where(b => belongs(b.Command)).Select(b => b.Command + " on " + t.Title)).ToList();
        Assert.True(strays.Count == 0, "Off their tab: " + string.Join(", ", strays));
        var home = RibbonLayout.Tabs.Single(t => t.Id == tabId);
        Assert.Contains(home.Buttons, b => belongs(b.Command));
    }

    /// <summary>[CommandMethod("NAME", ...)] -- or ("GROUP", "NAME", ...), where the second string is the name.</summary>
    private static readonly Regex CommandAttribute =
        new(@"^\s*\[\s*CommandMethod\(\s*""([^""]+)""\s*(?:,\s*""([^""]+)"")?");

    /// <summary>Every command name registered by a [CommandMethod] attribute in the plugin's source.</summary>
    private static HashSet<string> RegisteredCommands()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cad = Path.Combine(Repo(), "src", "FieldCodes.Cad");
        foreach (var file in Directory.EnumerateFiles(cad, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(cad, file).Replace('\\', '/');
            if (relative.StartsWith("obj/") || relative.StartsWith("bin/")) continue;
            foreach (var line in File.ReadLines(file))
            {
                var m = CommandAttribute.Match(line);
                if (m.Success) names.Add(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value);
            }
        }
        return names;
    }

    /// <summary>The repository: the first folder above the test binaries that holds src/FieldCodes.Cad.</summary>
    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "FieldCodes.Cad"))) dir = dir.Parent;
        Assert.True(dir != null, "No folder above " + AppContext.BaseDirectory + " holds src/FieldCodes.Cad.");
        return dir!.FullName;
    }
}
