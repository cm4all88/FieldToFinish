using FieldCodes.Commands;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// Settings > Commands' list and the My Commands ribbon panel, built from the plugin's registered
/// commands (read from its source here; by reflection in Civil 3D) and its Settings page
/// registrations. A new command or page must appear with no change to either.
/// </summary>
public sealed class CommandCatalogTests
{
    private static IList<CommandEntry> Catalog(IDictionary<string, string>? descriptions = null) =>
        CommandCatalog.Build(CadSource.CommandGroups(), CadSource.Registrations(), descriptions);

    [Fact]
    public void EveryRegisteredCommandIsListedOnceWithRibbonCommandsFirst()
    {
        var catalog = Catalog();
        var listed = catalog.SelectMany(e => new[] { e.Command }.Concat(e.Aliases)).ToList();

        Assert.Equal(listed.Count, listed.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Empty(CadSource.RegisteredCommands().Where(c => !listed.Contains(c, StringComparer.OrdinalIgnoreCase)));
        Assert.Empty(RibbonLayout.Commands.Where(c => !listed.Contains(c, StringComparer.OrdinalIgnoreCase)));

        // Ribbon order first (Process Drawing leads), then the rest alphabetically.
        Assert.Equal("FTFRUN", catalog[0].Command);
        var offRibbon = catalog.SkipWhile(e => e.OnRibbon).ToList();
        Assert.All(offRibbon, e => Assert.False(e.OnRibbon));
        Assert.Equal(offRibbon.Select(e => e.Command).OrderBy(c => c, StringComparer.Ordinal), offRibbon.Select(e => e.Command));
    }

    [Fact]
    public void ARibbonCommandCarriesItsButtonTipPlaceAndSettings()
    {
        var labels = Catalog().Single(e => e.Command == "FTFLABELS");

        Assert.Equal("Point Labels", labels.Title);
        Assert.Equal(RibbonLayout.AllButtons.First(b => b.Command == "FTFLABELS").Tip, labels.Description);
        Assert.Equal(new[] { "FieldToFinish > Labels" }, labels.RibbonPlaces);
        Assert.Contains("Point Labels", labels.SettingsPages);
        Assert.Contains("Label All", labels.SettingsPages);
        Assert.Equal(5, labels.StageNumber);
        Assert.False(labels.CanFollowWorkflow);

        // On two tabs: both places.
        Assert.Equal(new[] { "Survey Drafting > Drafting", "Boundary > Calculations & Closure" },
                     Catalog().Single(e => e.Command == "FTFDRAWLINE").RibbonPlaces);
    }

    [Fact]
    public void AliasesFollowTheCommandOnTheRibbon()
    {
        var catalog = Catalog();
        Assert.Equal(new[] { "FTFL" }, catalog.Single(e => e.Command == "FTFLABELLINE").Aliases);
        Assert.Equal(new[] { "FTFTREES" }, catalog.Single(e => e.Command == "FTFPOINTS").Aliases);
        Assert.Equal(new[] { "FTFSETTINGS", "FTFCONFIG", "FTFOPTIONS" }, catalog.Single(e => e.Command == "FTFSETUP").Aliases);
        Assert.DoesNotContain(catalog, e => e.Command == "FTFL");
    }

    [Fact]
    public void AStageOffTheRibbonIsDescribedByThePipeline()
    {
        var entry = Catalog(new Dictionary<string, string> { { "FTFLINELABELS", "Linework Labels: the bulk line pass." } })
            .Single(e => e.Command == "FTFLINELABELS");
        Assert.False(entry.OnRibbon);
        Assert.Equal("Linework Labels: the bulk line pass.", entry.Description);
        Assert.Equal(4, entry.StageNumber);
        Assert.Contains("Line Labels", entry.SettingsPages);
    }

    [Fact]
    public void WorkflowFollowUpsAreMarked()
    {
        var catalog = Catalog();
        Assert.All(SettingsRegistry.WorkflowFollowUps, c => Assert.True(catalog.Single(e => e.Command == c).CanFollowWorkflow));
        Assert.False(catalog.Single(e => e.Command == "FTFLEGEND").CanFollowWorkflow);
    }

    [Fact]
    public void ANewCommandAndItsPageNeedNoChangeHere()
    {
        var pages = new List<SettingsPageInfo>(CadSource.Registrations())
        {
            SettingsRegistry.FromAttribute(new SettingsPageAttribute
            {
                Group = "Survey Drafting", Label = "Cross Sections", Order = 60, Commands = "FTFXSECTION"
            }, "CrossSectionsPage")
        };
        var registered = CadSource.CommandGroups().Concat(new[] { new List<string> { "FTFXSECTION", "FTFXS" } });

        var entry = CommandCatalog.Build(registered, pages).Single(e => e.Command == "FTFXSECTION");

        Assert.Equal(new[] { "FTFXS" }, entry.Aliases);
        Assert.Equal(new[] { "Cross Sections" }, entry.SettingsPages);
        Assert.False(entry.OnRibbon);
    }

    [Fact]
    public void PagesEveryCommandFollowsAreNamedOnce()
    {
        Assert.Equal(new[] { "General", "Layers", "Profiles" }, CommandCatalog.PagesForEveryCommand(CadSource.Registrations()));
        Assert.DoesNotContain("General", Catalog().Single(e => e.Command == "FTFTAGS").SettingsPages);
    }

    [Fact]
    public void SearchLooksAtEverythingShown()
    {
        var tags = Catalog().Single(e => e.Command == "FTFTAGS");
        Assert.True(tags.Matches("ftftags"));
        Assert.True(tags.Matches("tree tags"));          // its button
        Assert.True(tags.Matches("same number"));        // its tip
        Assert.True(tags.Matches("> trees"));            // its ribbon place
        Assert.True(tags.Matches("label all"));          // a settings page that changes it
        Assert.True(tags.Matches(""));
        Assert.False(tags.Matches("easement"));
    }

    // ------------------------------------------------------------- my commands

    [Fact]
    public void MyCommandsCopiesTheRibbonButtonAndDescribesTheRest()
    {
        var panel = UserRibbonPanel.Build(new[] { "ftflabels", "FTFLINELABELS", "FTFLABELS", "not a command", "FTFNEW" },
            new Dictionary<string, string> { { "FTFLINELABELS", "Labels every recognised line." } });

        Assert.NotNull(panel);
        Assert.Equal("My Commands", panel!.Title);
        Assert.Equal(new[] { "FTFLABELS", "FTFLINELABELS", "FTFNEW" }, panel.Buttons.Select(b => b.Command));

        var labels = panel.Buttons[0];
        Assert.Equal("Point Labels", labels.Label);
        Assert.Equal("Labels", labels.Icon);
        Assert.Equal(RibbonButtonSize.Small, labels.Size);

        Assert.Equal("FTFLINELABELS", panel.Buttons[1].Label);
        Assert.Null(panel.Buttons[1].Icon);
        Assert.Equal("Labels every recognised line.", panel.Buttons[1].Tip);
        Assert.Equal("Runs FTFNEW, as if typed at the command line.", panel.Buttons[2].Tip);

        // A large button's two-line label goes on one line in a small button.
        Assert.Equal("Process Drawing", UserRibbonPanel.Build(new[] { "FTFRUN" })!.Buttons[0].Label);
    }

    [Fact]
    public void NoCommandsMeansNoPanelAndTheOfficeRibbonNeverHasOne()
    {
        Assert.Null(UserRibbonPanel.Build(null!));
        Assert.Null(UserRibbonPanel.Build(new[] { " ", "lower case?" }));
        Assert.DoesNotContain(RibbonLayout.Tabs.SelectMany(t => t.Panels), p => p.Title == UserRibbonPanel.Title);
    }
}
