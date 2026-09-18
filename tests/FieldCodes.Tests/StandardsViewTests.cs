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
    public void AllSixSectionsArePresent_InOrder()
    {
        Assert.Equal(
            new[] { "Layers", "Labels", "Tags", "Tables", "Drawing Order", "Feature Defaults" },
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

        Assert.Contains(layers.Items, i => i.Name == "V-TREE-TEXT" && i.Source == "Rules: tree");
        Assert.Contains(layers.Items, i => i.Name == "V-TREE-DRIP" && i.Value.StartsWith("Driplines"));
        Assert.Contains(layers.Items, i => i.Name == "V-CTRL-TEXT");
    }

    [Fact]
    public void SharedLabelLayersListEveryRuleUsingThem()
    {
        // Pole, junction box, light pole and guy anchor all label on V-UTIL-POWR-TEXT.
        var item = Section("Layers").Items.Single(i => i.Name == "V-UTIL-POWR-TEXT");

        Assert.Contains("pole", item.Value);
        Assert.Contains("junction-box", item.Value);
        Assert.Contains("light-pole", item.Value);
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

        var labels = Section("Labels", settings);

        Assert.Contains(labels.Items, i => i.Name == "Text style" && i.Value == "PMX-ROMANS");
        Assert.Contains(labels.Items, i => i.Name == "Text height (plotted)" && i.Value == "0.1");
    }

    [Fact]
    public void MissingStandardsAreMarkedPlanned_NotInvented()
    {
        var labels = Section("Labels");

        var justification = labels.Items.Single(i => i.Name == "Justification");
        Assert.Equal(StandardsViewBuilder.Planned, justification.Source);

        var rotation = labels.Items.Single(i => i.Name == "Label rotation");
        Assert.Equal(StandardsViewBuilder.Planned, rotation.Source);
    }

    [Fact]
    public void MeasuredCollisionBoxesAreABuiltInGuarantee()
    {
        var item = Section("Labels").Items.Single(i => i.Name == "Collision boxes");
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
}
