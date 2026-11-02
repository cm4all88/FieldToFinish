using FieldCodes.Linework;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// Fence heights written on the fence's shots: "FNC B 3.5" (B begins the line, 3.5 is the height in
/// feet), carried along the fence until a shot records another height, and written into the
/// fence's label.
/// </summary>
public sealed class FenceHeightTests
{
    private static double? Read(string description, out string written, out string problem)
        => LineHeights.Read(description, 20, out written, out problem);

    [Theory]
    [InlineData("FNC B 3.5", 3.5, "3.5")]
    [InlineData("FNC B 5", 5.0, "5")]
    [InlineData("FNC 6", 6.0, "6")]
    [InlineData("FNC B 4'", 4.0, "4")]
    [InlineData("FNC B 4FT", 4.0, "4")]
    [InlineData("fnc b 3.5", 3.5, "3.5")]
    [InlineData("FNC1 B 5", 5.0, "5")]
    [InlineData("FNC BEG 6", 6.0, "6")]
    [InlineData("FNC 5 E", 5.0, "5")]
    public void ReadsTheHeightAfterTheCodeAndLineCodes(string description, double feet, string written)
    {
        var h = Read(description, out var w, out var problem);
        Assert.Equal(feet, h);
        Assert.Equal(written, w);
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("FNC B")]
    [InlineData("FNC")]
    [InlineData("FNC1 B")]
    [InlineData("FNC E")]
    [InlineData("FNC R 10 20")]
    [InlineData("FNC CHAIN LINK")]
    [InlineData("")]
    public void NoHeightWhenNoneIsWritten(string description)
    {
        Assert.Null(Read(description, out _, out var problem));
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("FNC B 35")]
    [InlineData("FNC B 0.2")]
    [InlineData("FNC B 3.5 6")]
    public void ANumberThatCannotBeAHeightIsReportedNotUsed(string description)
    {
        Assert.Null(Read(description, out _, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void TheSameHeightWrittenTwiceIsFine()
    {
        Assert.Equal(6.0, Read("FNC B 6 6'", out _, out var problem));
        Assert.Null(problem);
    }

    [Fact]
    public void TheHighestHeightIsASetting()
    {
        Assert.Equal(25.0, LineHeights.Read("FNC B 25", 30, out _, out var problem));
        Assert.Null(problem);
        Assert.Null(LineHeights.Read("FNC B 25", 20, out _, out problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void TheHeightIsCarriedAlongUntilANewOneIsRecorded()
    {
        var recorded = new[]
        {
            new RecordedHeight { Station = 0, Feet = 3.5, Written = "3.5" },
            new RecordedHeight { Station = 120, Feet = 5, Written = "5" },
        };
        Assert.Equal("3.5", LineHeights.At(recorded, 0)!.Written);
        Assert.Equal("3.5", LineHeights.At(recorded, 60)!.Written);
        Assert.Equal("3.5", LineHeights.At(recorded, 119.9)!.Written);
        Assert.Equal("5", LineHeights.At(recorded, 120)!.Written);
        Assert.Equal("5", LineHeights.At(recorded, 400)!.Written);
    }

    [Fact]
    public void BeforeTheFirstRecordedHeightThereIsNone()
    {
        var recorded = new[] { new RecordedHeight { Station = 50, Feet = 6, Written = "6" } };
        Assert.Null(LineHeights.At(recorded, 10));
        Assert.Equal("FENCE", LineHeights.Compose(LineHeights.DefaultFormat, "FENCE", LineHeights.At(recorded, 10), -1));
    }

    [Fact]
    public void TheLabelCarriesTheHeight()
    {
        var h35 = new RecordedHeight { Feet = 3.5, Written = "3.5" };
        var h5 = new RecordedHeight { Feet = 5, Written = "5" };
        Assert.Equal("3.5' FENCE", LineHeights.Compose("{height}' {label}", "FENCE", h35, -1));
        Assert.Equal("5' FENCE", LineHeights.Compose("{height}' {label}", "FENCE", h5, -1));
        Assert.Equal("5.0' FENCE", LineHeights.Compose("{height}' {label}", "FENCE", h5, 1));
        Assert.Equal("4' FENCE", LineHeights.Compose("{height}' {label}", "FENCE", h35, 0));
        Assert.Equal("CHAIN LINK FENCE (5' HIGH)",
                     LineHeights.Compose("{label} ({height}' HIGH)", "CHAIN LINK FENCE", h5, -1));
    }

    [Fact]
    public void FencesReadHeightsAndOtherLinesDoNotUnlessTold()
    {
        Assert.True(LineHeights.Applies(new LineFeatureRule { Code = "FNC", Name = "Fence", Label = "FENCE" }));
        Assert.True(LineHeights.Applies(new LineFeatureRule { Code = "CLF", Name = "Chain link", Label = "CHAIN LINK FENCE" }));
        Assert.False(LineHeights.Applies(new LineFeatureRule { Code = "EP", Name = "Edge of pavement", Label = "EP" }));
        Assert.True(LineHeights.Applies(new LineFeatureRule { Code = "WALL", Label = "WALL", ReadHeight = true }));
        Assert.False(LineHeights.Applies(new LineFeatureRule { Code = "FNC", Label = "FENCE", ReadHeight = false }));
    }

    [Theory]
    [InlineData("FNC B 3.5", "FNC")]
    [InlineData("FNC1 B 5", "FNC")]
    [InlineData("  fnc12 ", "FNC")]
    [InlineData("", "")]
    public void TheCodeIsTheFirstWordWithoutItsFigureNumber(string description, string code)
    {
        Assert.Equal(code, LineHeights.CodeOf(description));
    }

    [Fact]
    public void SettingsDefaultsAndValidation()
    {
        var s = new LineLabelSettings();
        s.RestoreDefaults();
        Assert.Equal("{height}' {label}", s.HeightFormat);
        Assert.Equal(-1, s.HeightDecimals);
        Assert.Equal(20, s.HeightMaxFeet);

        s.HeightFormat = "{label}";
        Assert.NotEmpty(Problems(s));
        s.HeightFormat = "";
        Assert.Empty(Problems(s));
        s.HeightDecimals = 7;
        Assert.NotEmpty(Problems(s));
    }

    private static List<string> Problems(LineLabelSettings s)
    {
        var problems = new List<string>();
        s.Validate(problems);
        return problems;
    }

    [Fact]
    public void TheCommandsUseTheHeights()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "FieldCodes.Cad", "LineworkCommands.cs"));
        Assert.Contains("RecordedHeights(curve, candidates, noteIndex", src);              // bulk pass
        Assert.Contains("jig.TextAt = (wording, station) => LineHeights.Compose", src);   // FTFLABELLINE
        Assert.Contains("labelText = jig.CurrentText ?? jig.CurrentWording", src);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FieldToFinish.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
