using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// Reading the colour and lineweight an office setting names. Written the way a
/// drafter says them; anything FTF cannot read leaves the layer alone rather than
/// becoming a guess.
/// </summary>
public sealed class LayerAppearanceTests
{
    [Theory]
    [InlineData("red", 1)]
    [InlineData("green", 3)]
    [InlineData("cyan", 4)]
    [InlineData("WHITE", 7)]
    [InlineData("3", 3)]
    [InlineData("250", 250)]
    [InlineData(" green ", 3)]
    public void ColoursAreReadByNameOrNumber(string setting, short expected)
    {
        Assert.Equal(expected, LayerAppearance.ColorIndex(setting));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("chartreuse")]
    [InlineData("257")]
    [InlineData("-1")]
    public void AColourFtfCannotReadIsNotGuessedAt(string? setting)
    {
        Assert.Null(LayerAppearance.ColorIndex(setting));
    }

    [Theory]
    [InlineData("0.40", 40)]
    [InlineData("40", 40)]
    [InlineData("0.25", 25)]
    [InlineData("0.6", 60)]
    [InlineData("2.11", 211)]
    [InlineData("ByLayer", -1)]
    [InlineData("ByBlock", -2)]
    [InlineData("Default", -3)]
    public void LineWeightsAreReadInMillimetresOrHundredths(string setting, int expected)
    {
        Assert.Equal(expected, LayerAppearance.LineWeightHundredths(setting));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("thick")]
    [InlineData("500")]
    public void ALineWeightFtfCannotReadIsNotGuessedAt(string? setting)
    {
        Assert.Null(LayerAppearance.LineWeightHundredths(setting));
    }

    [Fact]
    public void TheOfficeAnnotationConventionIsWhatANewEasementLayerGets()
    {
        // Their V-TEXT, V-TEXT-E and V-PROP-TEXT are green at 0.40; their tables cyan at 0.40.
        var es = new EasementSettings();
        Assert.Equal((short)3, LayerAppearance.ColorIndex(es.TextColor));
        Assert.Equal(40, LayerAppearance.LineWeightHundredths(es.TextLineWeight));
        Assert.Equal((short)4, LayerAppearance.ColorIndex(es.TableColor));
        Assert.Equal(40, LayerAppearance.LineWeightHundredths(es.TableLineWeight));
        Assert.Equal((short)4, LayerAppearance.ColorIndex(es.PointLabelColor));
    }
}
