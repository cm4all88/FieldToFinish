using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// Named drafting profiles: a drawing selects one, and every FTF tool drafts to
/// it. A project file beside the drawing still overrides; a missing profile is
/// reported rather than silently replaced.
/// </summary>
public sealed class DraftingProfileTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public DraftingProfileTests(RulesFixture fx) => _fx = fx;

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ftf-prof-" + Path.GetRandomFileName());
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void ASelectedProfileDrivesTheSettings()
    {
        FtfSettings.ProfileDirectoryOverride = TempDir();
        try
        {
            var wsdot = new FtfSettings();
            wsdot.Dips.DoubleLineThresholdIn = 18;
            wsdot.Easements.DistanceDecimals = 3;
            FtfSettings.SaveProfile("WSDOT", wsdot);

            var resolved = FtfSettings.Resolve(TempDir(), TempDir(), _fx.Config, "WSDOT");

            Assert.Equal(SettingsSource.Profile, resolved.Source);
            Assert.Equal("WSDOT", resolved.ProfileName);
            Assert.Equal(18, resolved.Settings.Dips.DoubleLineThresholdIn);
            Assert.Equal(3, resolved.Settings.Easements.DistanceDecimals);
            Assert.Contains("WSDOT", FtfSettings.ListProfiles());
        }
        finally { FtfSettings.ProfileDirectoryOverride = null; }
    }

    [Fact]
    public void AProjectFileBesideTheDrawingStillWins()
    {
        FtfSettings.ProfileDirectoryOverride = TempDir();
        try
        {
            FtfSettings.SaveProfile("City Standard", new FtfSettings());
            var dwg = TempDir();
            var project = new FtfSettings();
            project.Dips.SlopeDecimals = 3;
            project.Save(Path.Combine(dwg, FtfSettings.FileName));

            var resolved = FtfSettings.Resolve(dwg, TempDir(), _fx.Config, "City Standard");

            Assert.Equal(SettingsSource.DrawingFolder, resolved.Source);
            Assert.Equal(3, resolved.Settings.Dips.SlopeDecimals);
        }
        finally { FtfSettings.ProfileDirectoryOverride = null; }
    }

    [Fact]
    public void AMissingProfileIsReported_NotSilentlySubstituted()
    {
        FtfSettings.ProfileDirectoryOverride = TempDir();
        try
        {
            var resolved = FtfSettings.Resolve(TempDir(), TempDir(), _fx.Config, "Retired Client");
            Assert.NotEqual(SettingsSource.Profile, resolved.Source);
            Assert.Equal("Retired Client", resolved.MissingProfile);
        }
        finally { FtfSettings.ProfileDirectoryOverride = null; }
    }

    [Fact]
    public void NewSectionsRoundTripAndValidate()
    {
        var path = Path.Combine(TempDir(), FtfSettings.FileName);
        var s = new FtfSettings();
        s.Dips.Standard(FieldCodes.Utilities.UtilitySystem.Sanitary).MinSlopePercent = 0.5;
        s.Easements.LabelMode = EasementLabelMode.Direct;
        s.Save(path);

        var back = FtfSettings.Load(path);
        Assert.Equal(0.5, back.Dips.Standard(FieldCodes.Utilities.UtilitySystem.Sanitary).MinSlopePercent);
        Assert.Equal(EasementLabelMode.Direct, back.Easements.LabelMode);
        Assert.Equal(new UtilitySettings().StructureCodes.Count, back.Dips.StructureCodes.Count);   // not doubled

        var problems = new List<string>();
        back.Validate(problems);
        Assert.Empty(problems);
    }
}
