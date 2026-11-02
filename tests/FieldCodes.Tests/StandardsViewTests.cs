using FieldCodes;
using FieldCodes.Settings;
using FieldCodes.Standards;

namespace FieldCodes.Tests;

/// <summary>
/// The Standards page surfaces existing configuration; it must never invent a
/// setting to fill space. These tests pin what is real, what is built-in behaviour,
/// and what is honestly marked Planned.
/// </summary>
public sealed class StandardsViewTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public StandardsViewTests(RulesFixture fx) => _fx = fx;

    private IList<StandardsSection> Build(FtfSettings settings = null)
        => new StandardsViewBuilder(_fx.Config, settings ?? new FtfSettings()).Build();

    private StandardsSection Section(string title, FtfSettings settings = null)
        => Build(settings).Single(s => s.Title == title);

    // ------------------------------------------------------------------ structure

    [Fact]
    public void AllSectionsArePresent_InOrder()
    {
        Assert.Equal(
            new[]
            {
                "Layers", "Point Labels", "Line Labels", "Tags", "Tables", "Drawing Order",
                "Feature Defaults", "Field Codes"
            },
            Build().Select(s => s.Title));
    }

    [Fact]
    public void EveryItemHasANameValueAndSource()
    {
        foreach (var section in Build())
        {
            Assert.NotEmpty(section.Items);
            Assert.All(section.Items, i =>
            {
                Assert.False(string.IsNullOrWhiteSpace(i.Name));
                Assert.False(string.IsNullOrWhiteSpace(i.Value));
                Assert.False(string.IsNullOrWhiteSpace(i.Source));
            });
        }
    }

    // -------------------------------------------------------------------- layers

    [Fact]
    public void LayersComeFromTheRealRules()
    {
        var layers = Section("Layers");

        Assert.Contains(layers.Items, i => i.Name == "V-SURF-VEGE-TEXT-E" && i.Source == "Rules: tree");
        Assert.Contains(layers.Items, i => i.Name == "V-SURF-VEGE-E" && i.Value.StartsWith("Driplines"));
        Assert.Contains(layers.Items, i => i.Name == "V-CTRL-TEXT-E");
    }

    [Fact]
    public void SharedLabelLayersListEveryRuleUsingThem()
    {
        // Pole, junction box and guy anchor all label on V-UTIL-POWR-TEXT-E (light poles
        // have PMX's own illumination layer).
        var item = Section("Layers").Items.Single(i => i.Name == "V-UTIL-POWR-TEXT-E");

        Assert.Contains("pole", item.Value);
        Assert.Contains("junction-box", item.Value);
        Assert.Contains("guy-anchor", item.Value);
        Assert.DoesNotContain("light-pole", item.Value);
    }

    [Fact]
    public void ModifierSuffixesAreListed()
    {
        var item = Section("Layers").Items.Single(i => i.Name.Contains("-DEAD"));
        Assert.Contains("-RMV", item.Name);
        Assert.Contains("-PROT", item.Name);
    }

    [Fact]
    public void FeatureSymbolLayersAreDeliberatelyAbsent()
    {
        // Civil 3D's description keys own the symbols, so no symbol layer is an FTF
        // standard. The section note says so.
        Assert.Contains("description keys", Section("Layers").Note);
    }

    // -------------------------------------------------------------------- labels

    [Fact]
    public void LabelStandardsReflectTheSettings()
    {
        var settings = new FtfSettings();
        settings.Labels.TextStyle = "PMX-ROMANS";
        settings.Labels.TextHeightPlotted = 0.1;

        var labels = Section("Point Labels", settings);

        Assert.Contains(labels.Items, i => i.Name == "Text style" && i.Value == "PMX-ROMANS");
        Assert.Contains(labels.Items, i => i.Name == "Text height (plotted)" && i.Value == "0.1");
    }

    [Fact]
    public void MissingStandardsAreMarkedPlanned_NotInvented()
    {
        var labels = Section("Point Labels");

        var justification = labels.Items.Single(i => i.Name == "Justification");
        Assert.Equal(StandardsViewBuilder.Planned, justification.Source);

        var rotation = labels.Items.Single(i => i.Name == "Label rotation");
        Assert.Equal(StandardsViewBuilder.Planned, rotation.Source);
    }

    [Fact]
    public void MeasuredCollisionBoxesAreABuiltInGuarantee()
    {
        var item = Section("Point Labels").Items.Single(i => i.Name == "Collision boxes");
        Assert.Equal(StandardsViewBuilder.BuiltIn, item.Source);
        Assert.Contains("never estimated", item.Value);
    }

    // ---------------------------------------------------------------------- tags

    [Fact]
    public void TagPrefixesComeFromTheRules()
    {
        var item = Section("Tags").Items.Single(i => i.Name == "Prefixes in use");

        Assert.Contains("T (tree)", item.Value);
        Assert.Contains("P (pole)", item.Value);
        Assert.Contains("L (light-pole)", item.Value);
        Assert.Contains("S (", item.Value);      // sign and sn-sign share S
    }

    [Fact]
    public void TagPermanenceIsStatedAsBuiltIn()
    {
        var item = Section("Tags").Items.Single(i => i.Name == "Numbering");
        Assert.Equal(StandardsViewBuilder.BuiltIn, item.Source);
        Assert.Contains("leaves a gap", item.Value);
    }

    // -------------------------------------------------------------------- tables

    [Fact]
    public void TableStandardsMixSettingsAndBuiltIns()
    {
        var tables = Section("Tables");

        Assert.Contains(tables.Items, i => i.Name == "Title" && i.Value == "TREE SCHEDULE");
        Assert.Contains(tables.Items, i => i.Name == "Columns" && i.Value.Contains("Tag"));
        Assert.Contains(tables.Items,
            i => i.Name == "Row height" && i.Source == StandardsViewBuilder.BuiltIn);
    }

    // -------------------------------------------------------------- drawing order

    [Fact]
    public void BandsAreListedBottomToTop()
    {
        var names = Section("Drawing Order").Items
            .Where(i => i.Name.StartsWith("Band"))
            .Select(i => i.Name)
            .ToList();

        Assert.Equal(4, names.Count);
        Assert.Contains("maskable linework", names[0]);
        Assert.Contains("labels", names[3]);
    }

    [Fact]
    public void ProtectedAndMaskableListsComeFromSettings()
    {
        var settings = new FtfSettings();
        settings.DrawOrder.ProtectedLayers = new List<string> { "X-KEEP" };

        var item = Section("Drawing Order", settings).Items
            .Single(i => i.Name == "Protected layers");

        Assert.Equal("X-KEEP", item.Value);
        Assert.Equal(StandardsViewBuilder.Settings, item.Source);
    }

    // ------------------------------------------------------------ feature defaults

    [Fact]
    public void TheProductBoundaryLeadsTheDefaults()
    {
        var item = Section("Feature Defaults").Items.Single(i => i.Name == "Symbol ownership");
        Assert.Contains("description keys place every symbol", item.Value);
        Assert.Contains("insertBlock", item.Value);
    }

    [Fact]
    public void DefaultsTrackTheSettingsObject()
    {
        var settings = new FtfSettings();
        settings.Drip.UnifyByDefault = false;

        var item = Section("Feature Defaults", settings).Items
            .Single(i => i.Name == "Dripline trimming");

        Assert.Contains("drawn whole", item.Value);
    }

    // ------------------------------------------------- accuracy and navigation

    [Fact]
    public void EverySettingsRowNamesTheSectionThatEditsIt()
    {
        var known = new[]
        {
            StandardsViewBuilder.GeneralSection, StandardsViewBuilder.PointFeaturesSection,
            StandardsViewBuilder.TreesSection, StandardsViewBuilder.PointLabelsSection,
            StandardsViewBuilder.LineLabelsSection, StandardsViewBuilder.TagsSection,
            StandardsViewBuilder.ScheduleSection, StandardsViewBuilder.DrawOrderSection,
            StandardsViewBuilder.AdvancedSection
        };

        foreach (var item in Build().SelectMany(s => s.Items))
        {
            Assert.False(string.IsNullOrWhiteSpace(item.EditHint), item.Name);
            if (item.SettingsSection != null)
                Assert.Contains(item.SettingsSection, known);
            if (item.Source == StandardsViewBuilder.Settings && item.Name != "Codes ignored by you")
                Assert.NotNull(item.SettingsSection);
        }
    }

    [Fact]
    public void NotAvailableRowsAreNeverOperational_OrEditable()
    {
        var planned = Build().SelectMany(s => s.Items)
            .Where(i => i.Source == StandardsViewBuilder.Planned)
            .ToList();

        Assert.NotEmpty(planned);
        Assert.All(planned, i =>
        {
            Assert.False(i.Operational);
            Assert.Null(i.SettingsSection);
            Assert.Contains("Not available yet", i.EditHint);
        });
    }

    [Fact]
    public void RulesFileOnlyRowsSayWhereToEditThem()
    {
        var suffixes = Section("Drawing Order").Items.Single(i => i.Name == "Modifier suffix handling");
        Assert.Null(suffixes.SettingsSection);
        Assert.Contains("rules.json", suffixes.EditHint);

        // The suffixes listed are the ones the rules file really has.
        foreach (var suffix in _fx.Config.Modifiers.Where(m => !string.IsNullOrEmpty(m.LayerSuffix)))
            Assert.Contains(suffix.LayerSuffix, suffixes.Value);
    }

    [Fact]
    public void PerFeatureRuleValuesOpenPointFeatures()
    {
        var item = Section("Layers").Items.Single(i => i.Name == "V-SURF-VEGE-TEXT-E");
        Assert.Equal(StandardsViewBuilder.PointFeaturesSection, item.SettingsSection);
    }

    [Fact]
    public void InheritedValuesSayWhereTheyComeFrom()
    {
        var symbols = Section("Feature Defaults").Items.Single(i => i.Name == "Symbol ownership");
        Assert.Equal(StandardsViewBuilder.Civil3D, symbols.Source);
        Assert.Contains("description key", symbols.EditHint);

        var tableStyle = Section("Tables").Items.Single(i => i.Name == "Table style");
        Assert.Equal(StandardsViewBuilder.Drawing, tableStyle.Source);
    }

    [Fact]
    public void LabelMasksSayOnlyPointLabelsUseTheMaskLayer()
    {
        var mask = Section("Layers").Items.Single(i => i.Value.StartsWith("Point-label masks"));
        Assert.Contains("line-label masks sit on the line label's own layer", mask.Value);
    }

    [Fact]
    public void HandMovedToleranceIsClaimedOnlyForPointLabels()
    {
        var settings = new FtfSettings();
        settings.Cleanup.MovedTolerance = 0.5;
        var item = Section("Feature Defaults", settings).Items.Single(i => i.Name == "Hand-moved point labels");
        Assert.Contains("0.5", item.Value);
        Assert.Contains("FTFLABELS", item.Value);
        Assert.Equal(StandardsViewBuilder.AdvancedSection, item.SettingsSection);
    }

    [Fact]
    public void BareCodesMentionTheSymbolLabelException()
    {
        var item = Section("Field Codes").Items.Single(i => i.Name == "Bare codes");
        Assert.Contains("labelled with the code itself", item.Value);
        Assert.Contains("CB", item.Value);
    }

    [Fact]
    public void LineLabelSpacingIsMarkedAutomaticPassOnly()
    {
        var settings = new FtfSettings();
        settings.LineLabels.MinLengthFeet = 12;
        var lines = Section("Line Labels", settings);

        var min = lines.Items.Single(i => i.Name.StartsWith("Minimum length"));
        Assert.Contains("automatic pass only", min.Name);
        Assert.Equal("12 ft", min.Value);
        Assert.Equal(StandardsViewBuilder.LineLabelsSection, min.SettingsSection);
        Assert.Contains(lines.Items, i => i.Name == "Duplicate prevention" && i.Source == StandardsViewBuilder.BuiltIn);
    }

    [Fact]
    public void CodesYouIgnoredAreListed()
    {
        var settings = new FtfSettings();
        settings.Diagnostics.Ignore("QZ");
        var item = Section("Field Codes", settings).Items.Single(i => i.Name == "Codes ignored by you");
        Assert.Contains("QZ", item.Value);
        Assert.Contains("Unknown Codes", item.EditHint);
    }

    [Fact]
    public void BuildingTheViewChangesNothingInTheSettings()
    {
        var settings = new FtfSettings();
        var before = Newtonsoft.Json.JsonConvert.SerializeObject(settings);
        Build(settings);
        Assert.Equal(before, Newtonsoft.Json.JsonConvert.SerializeObject(settings));
    }
}
