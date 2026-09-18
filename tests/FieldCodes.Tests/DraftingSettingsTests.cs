using FieldCodes.Settings;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// The FTFDRAWLINE catalog: the shipped types, the deliberate absence of guessed
/// layers, and the settings-file round trip -- including files written before the
/// section existed.
/// </summary>
public sealed class DraftingSettingsTests
{
    [Fact]
    public void TheShippedCatalogHasTheCadastralTypes()
    {
        var drafting = new DraftingSettings();

        var expected = new[]
        {
            "Boundary", "Right of Way", "Centerline", "Section Line",
            "Quarter Section", "Easement", "Lot Line", "Property Line"
        };

        Assert.Equal(expected.Length, drafting.LineTypes.Count);
        foreach (var name in expected)
            Assert.NotNull(drafting.FindType(name));
    }

    [Fact]
    public void ShippedTypesNameNoLayers()
    {
        // A plausible-looking guessed layer would be worse than an explicit "not
        // configured": the command refuses an unconfigured type with an
        // explanation, and the office sets the real layer once, from the drawing.
        var drafting = new DraftingSettings();
        Assert.All(drafting.LineTypes, t => Assert.Equal(string.Empty, t.Layer));
    }

    [Fact]
    public void ShippedDefaultsAreValid()
    {
        var problems = new List<string>();
        new DraftingSettings().Validate(problems);
        Assert.Empty(problems);
    }

    [Fact]
    public void FindTypeIsCaseInsensitive()
    {
        var drafting = new DraftingSettings();
        Assert.NotNull(drafting.FindType("boundary"));
        Assert.Null(drafting.FindType("Alignment"));
        Assert.Null(drafting.FindType(null));
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public void DuplicateTypeNamesAreReported()
    {
        var drafting = new DraftingSettings();
        drafting.LineTypes.Add(new DraftingLineType { Name = "boundary" });

        var problems = new List<string>();
        drafting.Validate(problems);
        Assert.Contains(problems, p => p.Contains("more than once"));
    }

    [Theory]
    [InlineData("Sideways")]        // not a placement
    [InlineData("")]
    public void BadPlacementIsReported(string placement)
    {
        var drafting = new DraftingSettings();
        drafting.LineTypes[0].Placement = placement;

        var problems = new List<string>();
        drafting.Validate(problems);
        Assert.Contains(problems, p => p.Contains("placement"));
    }

    [Fact]
    public void BadNumbersAreReported()
    {
        var drafting = new DraftingSettings();
        drafting.LineTypes[0].TextHeightPlotted = 0;
        drafting.LineTypes[1].OffsetPlotted = -1;
        drafting.LineTypes[2].BearingSecondsDecimals = 9;
        drafting.LineTypes[3].DistanceDecimals = -1;

        var problems = new List<string>();
        drafting.Validate(problems);
        Assert.Equal(4, problems.Count);
    }

    [Fact]
    public void BadAnnotationKindIsReported()
    {
        var drafting = new DraftingSettings();
        drafting.LineTypes[0].Annotation = "CurveTable";

        var problems = new List<string>();
        drafting.Validate(problems);
        Assert.Contains(problems, p => p.Contains("annotation"));
    }

    [Theory]
    [InlineData("None")]
    [InlineData("BearingDistance")]
    [InlineData("BearingOnly")]
    [InlineData("DistanceOnly")]
    [InlineData("bearingdistance")]     // case-insensitive
    public void EveryAnnotationKindIsAccepted(string kind)
    {
        var drafting = new DraftingSettings();
        drafting.LineTypes[0].Annotation = kind;

        var problems = new List<string>();
        drafting.Validate(problems);
        Assert.Empty(problems);
    }

    [Fact]
    public void FeatureTextAnnotationNeedsItsText()
    {
        // The label is what would be drawn; it is never derived from the type
        // name, so an enabled FeatureText type with no text is a config problem.
        var drafting = new DraftingSettings();
        drafting.LineTypes[0].Annotation = DraftingLineType.AnnotationFeatureText;

        var problems = new List<string>();
        drafting.Validate(problems);
        Assert.Contains(problems, p => p.Contains("feature text"));

        drafting.LineTypes[0].FeatureText = "SECTION LINE";
        problems.Clear();
        drafting.Validate(problems);
        Assert.Empty(problems);
    }

    [Fact]
    public void ADisabledFeatureTextTypeMayStayUnconfigured()
    {
        var drafting = new DraftingSettings();
        drafting.LineTypes[0].Annotation = DraftingLineType.AnnotationFeatureText;
        drafting.LineTypes[0].Enabled = false;

        var problems = new List<string>();
        drafting.Validate(problems);
        Assert.Empty(problems);
    }

    [Fact]
    public void BadBearingFormatIsReported()
    {
        var drafting = new DraftingSettings();
        drafting.LineTypes[0].BearingFormat = "Gradians";

        var problems = new List<string>();
        drafting.Validate(problems);
        Assert.Contains(problems, p => p.Contains("bearing format"));
    }

    [Fact]
    public void AMissingBearingFormatMeansQuadrant()
    {
        // Settings files written before the field existed have no bearingFormat
        // key; they must keep formatting quadrant bearings, and validate clean.
        var type = new DraftingLineType { Name = "Boundary", BearingFormat = null };
        Assert.False(type.WantsAzimuthFormat);

        var drafting = new DraftingSettings();
        drafting.LineTypes[0].BearingFormat = null;
        var problems = new List<string>();
        drafting.Validate(problems);
        Assert.Empty(problems);
    }

    [Fact]
    public void AnnotationKindNormalisesCase()
    {
        var type = new DraftingLineType { Annotation = "featuretext" };
        Assert.Equal(DraftingLineType.AnnotationFeatureText, type.AnnotationKind);
        Assert.True(type.WantsAnnotation);

        type.Annotation = "none";
        Assert.False(type.WantsAnnotation);

        type.Annotation = "garbage";
        Assert.Null(type.AnnotationKind);
        Assert.False(type.WantsAnnotation);
    }

    // ----------------------------------------------------------------- round trip

    [Fact]
    public void SettingsRoundTripThroughJson()
    {
        var settings = new FtfSettings();
        var boundary = settings.Drafting.FindType("Boundary");
        boundary.Layer = "V-PROP-BNDY";
        boundary.Stacked = true;
        boundary.BearingSecondsDecimals = 1;
        boundary.BearingFormat = DraftingLineType.BearingFormatAzimuth;
        boundary.FeatureText = "BOUNDARY";

        var json = JsonConvert.SerializeObject(settings);
        var loaded = JsonConvert.DeserializeObject<FtfSettings>(json);
        Assert.NotNull(loaded);
        loaded.FillMissingSections();

        var reloaded = loaded.Drafting.FindType("Boundary");
        Assert.Equal("V-PROP-BNDY", reloaded.Layer);
        Assert.True(reloaded.Stacked);
        Assert.Equal(1, reloaded.BearingSecondsDecimals);
        Assert.True(reloaded.WantsAzimuthFormat);
        Assert.Equal("BOUNDARY", reloaded.FeatureText);
        Assert.Equal(8, loaded.Drafting.LineTypes.Count);
    }

    [Fact]
    public void AFileFromBeforeThisSectionExistedGetsTheCatalog()
    {
        // An older ftf-settings.json has no "drafting" key at all. Loading it must
        // seed the shipped catalog rather than leave a null section at draw time.
        var loaded = JsonConvert.DeserializeObject<FtfSettings>("{\"version\":\"1\"}");
        Assert.NotNull(loaded);
        loaded.FillMissingSections();

        Assert.NotNull(loaded.Drafting);
        Assert.Equal(8, loaded.Drafting.LineTypes.Count);
        Assert.NotNull(loaded.Drafting.FindType("Boundary"));
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var original = new DraftingLineType { Name = "Boundary", Layer = "A" };
        var clone = original.Clone();
        clone.Layer = "B";
        Assert.Equal("A", original.Layer);
    }
}
