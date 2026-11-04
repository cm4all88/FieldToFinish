using FieldCodes.Geometry;

namespace FieldCodes.Tests;

/// <summary>
/// Plotted annotation heights to drawing units. A scale is a ratio, and "1\" = 20'" exists both
/// as 1:20 (made for a drawing in feet) and as 1:240 (made the AutoCAD way, in inches); in a
/// drawing in feet the second would make every FTF label twelve times too big.
/// </summary>
public sealed class PlotScaleTests
{
    private const int Inches = 1, Feet = 2, Metres = 6, Unitless = 0, UsSurveyFeet = 21;

    [Fact]
    public void PlottedHeightAtOneInchToTwentyFeetInADrawingInFeet()
    {
        var r = PlotScale.Resolve("1\" = 20'", 1, 20, Feet);
        Assert.Equal(20, r.DrawingUnitsPerPlottedInch, 9);
        Assert.False(r.Corrected);
        Assert.Equal(1.6, 0.08 * r.DrawingUnitsPerPlottedInch, 9);        // 0.08 in text is 1.6 ft
    }

    /// <summary>
    /// The annotation scales a real Civil 3D survey drawing in US survey feet holds, with the
    /// ratios it stores (read from the drawing's SCALE objects). Its engineering scales are made
    /// for feet (1" = 20' is 1:20), its architectural ones the AutoCAD way in inches
    /// (1/4" = 1'-0" is 0.25:12). Taken by ratio alone, the second kind made FTF annotation
    /// twelve times too big; every one now comes to what its name says.
    /// </summary>
    [Theory]
    [InlineData("1\" = 20'", 1, 20, 20, false)]
    [InlineData("1\" = 50'", 1, 50, 50, false)]
    [InlineData("1\" = 1'", 1, 1, 1, false)]
    [InlineData("1\" = 1'-0\"", 1, 12, 1, true)]
    [InlineData("1/4\" = 1'-0\"", 0.25, 12, 4, true)]
    [InlineData("3/32\" = 1'-0\"", 0.09375, 12, 10.6667, true)]
    [InlineData("1/64\" = 1'-0\"", 0.015625, 12, 64, true)]
    [InlineData("1:20", 1, 20, 20, false)]
    [InlineData("1\" = 20'_XREF", 1, 20, 20, false)]
    [InlineData("1:1", 1, 1, 1, false)]
    public void TheScaleListOfARealSurveyDrawingInFeetComesToWhatEachScaleSays(string name, double paper, double drawing, double perInch, bool corrected)
    {
        foreach (var units in new[] { UsSurveyFeet, Feet })
        {
            var r = PlotScale.Resolve(name, paper, drawing, units);
            Assert.Equal(perInch, r.DrawingUnitsPerPlottedInch, 3);
            Assert.Equal(corrected, r.Corrected);
        }
    }

    [Fact]
    public void FtfWhatSaysWhatAHeightOrABlockScaleComesToOnPaper()
    {
        var at20 = PlotScale.Resolve("1\" = 20'", 1, 20, UsSurveyFeet);
        Assert.Contains("0.08 in plotted is 1.6 drawing units tall", PlotScale.Summary(at20));
        Assert.Contains("\"1\" = 20'\" in a drawing in US survey feet", PlotScale.Summary(at20));
        // A label at the drawing's own 1.6 ft is 0.08 in on paper; one twelve times too big shows it.
        Assert.Contains("= 0.08 in plotted", PlotScale.HeightText(1.6, at20));
        Assert.Contains("= 0.96 in plotted", PlotScale.HeightText(19.2, at20));
        Assert.Contains("(1 x the annotation scale's 20)", PlotScale.BlockScaleText(20, at20));
        Assert.Contains("(12 x the annotation scale's 20)", PlotScale.BlockScaleText(240, at20));
        var corrected = PlotScale.Resolve("1\" = 20'", 1, 240, Feet);
        Assert.Contains("is defined as 1:240", PlotScale.Summary(corrected));
        Assert.Equal("no annotation scale", PlotScale.Summary(null));
    }

    [Fact]
    public void AScaleDefinedInInchesIsReadByItsNameInADrawingInFeet()
    {
        var r = PlotScale.Resolve("1\" = 20'", 1, 240, Feet);
        Assert.Equal(20, r.DrawingUnitsPerPlottedInch, 9);
        Assert.True(r.Corrected);
        Assert.Contains("1:240", r.Basis);
    }

    [Fact]
    public void InADrawingInInchesTheSameScaleIs240UnitsPerInch()
    {
        Assert.Equal(240, PlotScale.Resolve("1\" = 20'", 1, 240, Inches).DrawingUnitsPerPlottedInch, 9);
        Assert.Equal(240, PlotScale.Resolve("1\" = 20'", 1, 20, Inches).DrawingUnitsPerPlottedInch, 9);
    }

    [Theory]
    [InlineData("1\" = 20'", 20)]
    [InlineData("1\"=20'", 20)]
    [InlineData("1 in = 20 ft", 20)]
    [InlineData("1 inch = 50 feet", 50)]
    [InlineData("1” = 30’", 30)]
    [InlineData("1/4\" = 1'-0\"", 4)]
    [InlineData("1\" = 1'-6\"", 1.5)]
    [InlineData("1-1/2\" = 1'-0\"", 2.0 / 3.0)]
    public void ImperialScaleNamesAreRead(string name, double feetPerInch)
    {
        Assert.True(PlotScale.TryReadName(name, out var read), name);
        Assert.Equal(feetPerInch, read, 9);
    }

    [Theory]
    [InlineData("1:20")]
    [InlineData("1:500")]
    [InlineData("Model")]
    [InlineData("")]
    public void OtherNamesAreNotRead(string name)
    {
        Assert.False(PlotScale.TryReadName(name, out _));
    }

    [Fact]
    public void ARatioScaleKeepsItsRatio()
    {
        Assert.Equal(20, PlotScale.Resolve("1:20", 1, 20, Feet).DrawingUnitsPerPlottedInch, 9);
        Assert.Equal(500, PlotScale.Resolve("1:500", 1, 500, Metres).DrawingUnitsPerPlottedInch, 9);
    }

    [Fact]
    public void MetricDrawingsKeepTheRatioEvenWithAnImperialName()
    {
        Assert.Equal(240, PlotScale.Resolve("1\" = 20'", 1, 240, Metres).DrawingUnitsPerPlottedInch, 9);
    }

    [Fact]
    public void SurveyFeetAndUnitlessDrawingsAreFeet()
    {
        Assert.Equal(20, PlotScale.Resolve("1\" = 20'", 1, 240, UsSurveyFeet).DrawingUnitsPerPlottedInch, 9);
        Assert.Equal(20, PlotScale.Resolve("1\" = 20'", 1, 240, Unitless).DrawingUnitsPerPlottedInch, 9);
    }

    [Fact]
    public void NoScaleIsOneToOne()
    {
        Assert.Equal(1, PlotScale.Resolve(null, 0, 0, Feet).DrawingUnitsPerPlottedInch, 9);
        Assert.Equal(1, PlotScale.Resolve("1\" = 20'", 1, 0, Feet).DrawingUnitsPerPlottedInch, 9);
    }
}
