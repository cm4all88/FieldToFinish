using FieldCodes.Settings;
using FieldCodes.Utilities;

namespace FieldCodes.Tests;

/// <summary>
/// What the crew actually meets in the field, and how FTF writes it down: the sizes
/// worth one click, the size nothing is made in, and the line a pipe gets on a
/// structure callout.
/// </summary>
public sealed class DipFieldRealityTests
{
    private static readonly UtilitySettings Settings = new UtilitySettings();

    [Theory]
    [InlineData("CB", UtilitySystem.Storm)]
    [InlineData("SDMH", UtilitySystem.Storm)]
    public void TwentyInchIsOneClickWhereItIsMet(string code, UtilitySystem system)
    {
        Assert.Contains(20.0, Settings.PipeChoicesFor(code, system).CommonSizes);
    }

    [Fact]
    public void NineInchIsReachableWithoutTyping()
    {
        // Not a made size, but the crew meets it, so it is on the Larger list rather than
        // needing to be typed every time.
        var sanitary = Settings.PipeChoicesFor("SSMH", UtilitySystem.Sanitary);
        Assert.Contains(9.0, sanitary.LargerSizes);
        Assert.DoesNotContain(9.0, sanitary.CommonSizes);
    }

    [Theory]
    [InlineData("DI")]
    [InlineData("DIP")]
    [InlineData("CI")]
    public void NineInchIronSaysWhatToCheck(string material)
    {
        var pipe = new QuickPipeEntry
        {
            Direction = DirectionShortcuts.For("N"), SizeIn = 9, Material = material, MeasuredDip = 4.2
        }.Create();

        var note = UtilityQc.SizeNotMade(pipe);
        Assert.NotNull(note);
        Assert.Contains("9.05", note);
        Assert.Contains("kept", note);          // the measurement is not changed
    }

    [Theory]
    [InlineData(8, "DI")]
    [InlineData(10, "DI")]
    [InlineData(9, "RCP")]                      // a 9" concrete pipe is the office's business, not FTF's
    [InlineData(9, null)]
    public void NothingIsSaidAboutAnySizeFtfCannotSpeakTo(double size, string? material)
    {
        var pipe = new QuickPipeEntry
        {
            Direction = DirectionShortcuts.For("N"), SizeIn = size, Material = material, MeasuredDip = 4.2
        }.Create();

        Assert.Null(UtilityQc.SizeNotMade(pipe));
    }

    [Fact]
    public void APipeOnACalloutReadsSizeMaterialDirectionThenTheDip()
    {
        var structure = new StructureRecord
        {
            Id = "S1",
            StructureType = "SDMH",
            Cad = new CadStructureSnapshot { Rim = 160.35, Easting = 0, Northing = 0 },
            Field = { PointNumber = "1045" }
        };
        structure.Field.Pipes.Add(new QuickPipeEntry
        {
            Direction = DirectionShortcuts.For("N"), SizeIn = 6, Material = "CONC", MeasuredDip = 6.0
        }.Create());

        var lines = UtilityLabelFormatter.StructureLabel(new UtilityProject { Structures = { structure } }, structure, Settings);

        Assert.Contains("6\" CONC (N) IE = 154.35'", lines);
    }

    [Fact]
    public void APipeRunningOutsideTheLimitsIsDrawnAsAStubWithAMark()
    {
        // Not the drawing itself -- that needs AutoCAD -- but the office's say over it.
        Assert.Equal(5.0, Settings.OutsideLimitsStubFt);
        Assert.Equal("~", Settings.OutsideLimitsMark);
    }
}
