using FieldCodes.Settings;

namespace FieldCodes.Tests;

public sealed class ProductionLayerResolverTests
{
    [Fact]
    public void AConfiguredLayerAlreadyInTheDrawingIsUsed()
    {
        var r = ProductionLayerResolver.Resolve("V-UTIL-STRM-E", new[] { "0", "v-util-strm-e" }, null);
        Assert.Equal(LayerDecision.Existing, r.Decision);
        Assert.Equal("v-util-strm-e", r.Layer);
    }

    [Fact]
    public void AProjectMappingWinsOverTheFtfDefault()
    {
        var mappings = new[] { new LayerMapping { From = "V-UTIL-STRM-E", To = "C-STRM-PIPE" } };
        var r = ProductionLayerResolver.Resolve("V-UTIL-STRM-E", new[] { "V-UTIL-STRM-E", "C-STRM-PIPE" }, mappings);
        Assert.Equal(LayerDecision.Mapped, r.Decision);
        Assert.Equal("C-STRM-PIPE", r.Layer);
    }

    [Fact]
    public void AMappedLayerNotYetInTheDrawingIsCreatedUnderTheProjectName()
    {
        var mappings = new[] { new LayerMapping { From = "V-UTIL-STRM-E", To = "C-STRM-PIPE" } };
        var r = ProductionLayerResolver.Resolve("V-UTIL-STRM-E", new[] { "0" }, mappings);
        Assert.Equal(LayerDecision.Create, r.Decision);
        Assert.Equal("C-STRM-PIPE", r.Layer);
    }

    [Fact]
    public void TheSameNameWithDifferentSeparatorsIsNotDuplicated()
    {
        var r = ProductionLayerResolver.Resolve("V-UTIL-STRM-TEXT-E", new[] { "V_UTIL_STRM_TEXT_E" }, null);
        Assert.Equal(LayerDecision.SameNameDifferentSpelling, r.Decision);
        Assert.Equal("V_UTIL_STRM_TEXT_E", r.Layer);
    }

    [Fact]
    public void ANearDuplicateIsRaisedForTheDrafterNotSilentlyCreated()
    {
        var r = ProductionLayerResolver.Resolve("V-UTIL-STRM-E", new[] { "0", "V-UTIL-STRM", "V-UTIL-WATR-E" }, null);
        Assert.Equal(LayerDecision.AskAboutSimilar, r.Decision);
        Assert.Equal(new[] { "V-UTIL-STRM" }, r.Similar);
    }

    [Theory]
    [InlineData("V-ESMT-PATT-E", "V-ESMT-E")]
    [InlineData("V-UTIL-STRM-TEXT-E", "V-UTIL-STRM-E")]
    [InlineData("V-ESMT-DIMS-E", "V-ESMT-E")]
    public void AModifierLayerIsNotANearDuplicateOfItsBaseLayer(string wanted, string existing)
    {
        var r = ProductionLayerResolver.Resolve(wanted, new[] { existing }, null);
        Assert.Equal(LayerDecision.Create, r.Decision);
    }

    [Fact]
    public void DifferentWordsAreNeverTreatedAsTheSameLayer()
    {
        var r = ProductionLayerResolver.Resolve("V-UTIL-SSWR-E", new[] { "V-UTIL-SANI-E", "V-UTIL-STRM-E" }, null);
        Assert.Equal(LayerDecision.Create, r.Decision);
        Assert.Empty(r.Similar);
    }
}
