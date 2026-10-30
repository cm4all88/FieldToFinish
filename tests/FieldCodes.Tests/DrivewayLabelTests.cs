using FieldCodes;
using FieldCodes.Linework;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// FTFLABELDRIVEWAY: the between label for a driveway is the first edge's surface type,
/// read from its layer, with DWY after it.
/// </summary>
public sealed class DrivewayLabelTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public DrivewayLabelTests(RulesFixture fx) => _fx = fx;

    [Theory]
    [InlineData("V-SURF-ASPH-E", "ASPH")]
    [InlineData("V-SURF-CONC-E", "CONC")]
    [InlineData("V-SURF-GRVL-E", "GRVL")]
    [InlineData("V-SURF-ASPH-PATC-E", "PATC")]
    [InlineData("v-surf-asph-e", "ASPH")]
    [InlineData("ASPHALT", "ASPHALT")]
    [InlineData("C-ROAD-CONC", "CONC")]
    [InlineData("0", null)]
    [InlineData("", null)]
    public void TheTypeIsTheLayersLastFieldBeforeItsStatus(string layer, string? type)
    {
        Assert.Equal(type, DrivewayWording.LayerType(layer));
    }

    [Fact]
    public void AnAsphaltEdgeLabelsAsphDwyWithTheSpelledOutWordAsTheAlternative()
    {
        var catalog = new LineworkCatalog(_fx.Config.LineFeatures, _fx.Config.IgnoreLineNames);
        var edge = catalog.FindByLayer("V-SURF-ASPH-E");
        var choices = DrivewayWording.Choices("V-SURF-ASPH-E", edge, DrivewayWording.DefaultFormat);
        Assert.Equal(new[] { "ASPH DWY", "ASPHALT DWY" }, choices);
    }

    [Fact]
    public void AConcreteEdgeLabelsConcDwyAndNotTheWalkWording()
    {
        // EC's between-wordings are CONC WALK and CW: neither spells out CONC in one word.
        var catalog = new LineworkCatalog(_fx.Config.LineFeatures, _fx.Config.IgnoreLineNames);
        var choices = DrivewayWording.Choices("V-SURF-CONC-E", catalog.FindByLayer("V-SURF-CONC-E"), DrivewayWording.DefaultFormat);
        Assert.Equal(new[] { "CONC DWY" }, choices);
    }

    [Fact]
    public void ALayerThatIsNoConfiguredFeatureStillGivesItsType()
    {
        Assert.Equal(new[] { "GRVL DWY" }, DrivewayWording.Choices("V-SURF-GRVL-E", null, null));
        Assert.Empty(DrivewayWording.Choices("0", null, "{type} DWY"));
    }

    [Fact]
    public void TheWordingFollowsTheSetting()
    {
        Assert.Equal(new[] { "ASPH DRIVEWAY" }, DrivewayWording.Choices("V-SURF-ASPH-E", null, "{type} DRIVEWAY"));
    }

    [Fact]
    public void TheSettingDefaultsToTypeDwyAndMustNameTheType()
    {
        var s = new LineLabelSettings();
        Assert.Equal("{type} DWY", s.DrivewayFormat);

        var problems = new List<string>();
        s.DrivewayFormat = "DRIVEWAY";
        s.Validate(problems);
        Assert.Contains(problems, p => p.Contains("{type}"));

        problems.Clear();
        s.DrivewayFormat = "";
        s.Validate(problems);
        Assert.Empty(problems);
        Assert.Equal("{type} DWY", s.DrivewayFormat);
    }

    [Fact]
    public void ASettingsFileWrittenBeforeTheDrivewayCommandGetsTheDefault()
    {
        var s = Newtonsoft.Json.JsonConvert.DeserializeObject<LineLabelSettings>(@"{ ""textHeightPlotted"": 0.1 }")!;
        Assert.Equal("{type} DWY", s.DrivewayFormat);
    }
}
