using FieldCodes;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

public sealed class FtfSettingsTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public FtfSettingsTests(RulesFixture fx) => _fx = fx;

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ftf-" + Path.GetRandomFileName());
        Directory.CreateDirectory(d);
        return d;
    }

    // ------------------------------------------------------------------ defaults

    [Fact]
    public void DefaultsAreValid()
    {
        var problems = new List<string>();
        new FtfSettings().Validate(problems);
        Assert.Empty(problems);
    }

    [Fact]
    public void EverySectionIsExposedForTheSetupWindow()
    {
        var s = new FtfSettings();
        Assert.Equal(15, s.Sections.Count);
        Assert.All(s.Sections, sec =>
        {
            Assert.False(string.IsNullOrWhiteSpace(sec.Title));
            Assert.False(string.IsNullOrWhiteSpace(sec.AffectedCommands));
        });
    }

    [Fact]
    public void CharacterWidthFactorHasNoSuccessor()
    {
        // Label boxes are measured from the real text now. If this ever comes back as
        // a setting, the measurement path has regressed to guessing.
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(new FtfSettings());
        Assert.DoesNotContain("haracterWidth", json);
    }

    // ----------------------------------------------------------------- migration

    [Fact]
    public void MigratesLegacyKeysFromRulesJson()
    {
        var s = new FtfSettings();
        s.MigrateFrom(_fx.Config);

        Assert.Equal(_fx.Config.UnitsPerFoot, s.General.UnitsPerFoot);
        Assert.Equal(_fx.Config.TrunkDecimals, s.Trees.TrunkDecimals);
        Assert.Equal(_fx.Config.MultiStemAverage, s.Trees.MultiStemAverage);
        Assert.Equal(_fx.Config.ProtectedLayers, s.DrawOrder.ProtectedLayers);
        Assert.Equal(_fx.Config.MaskableLayers, s.DrawOrder.MaskableLayers);
        Assert.Equal(_fx.Config.LabelPlacement.TextHeightPlotted, s.Labels.TextHeightPlotted);
        Assert.Equal(_fx.Config.LabelPlacement.RingCount, s.Labels.RingCount);
        Assert.Equal(_fx.Config.LabelPlacement.MovedTolerance, s.Cleanup.MovedTolerance);
    }

    [Fact]
    public void MigrationPreservesExistingDrawingBehaviour()
    {
        // The whole point: upgrading must not silently change how drawings come out.
        // The profile location is redirected because this machine may genuinely
        // have a %APPDATA% settings file from real FTF use.
        FtfSettings.ProfileDirectoryOverride = TempDir();
        try
        {
            var resolved = FtfSettings.Resolve(TempDir(), TempDir(), _fx.Config);

            Assert.Equal(SettingsSource.Defaults, resolved.Source);
            Assert.True(resolved.MigratedFromRules);
            Assert.Equal(0.08, resolved.Settings.Labels.TextHeightPlotted);
            Assert.Equal(4, resolved.Settings.Labels.RingCount);
        }
        finally { FtfSettings.ProfileDirectoryOverride = null; }
    }

    [Fact]
    public void ApplyToPutsSharedValuesBackOnTheRulesObject()
    {
        // The parser and LayerClassifier read these off RulesConfig, so settings have
        // to push them across or there would be two sources of truth.
        var cfg = RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        var s = new FtfSettings();
        s.General.UnitsPerFoot = 0.3048;
        s.Trees.TrunkDecimals = 2;
        s.Trees.MultiStemAverage = StemAverageMethod.Quadratic;
        s.DrawOrder.ProtectedLayers = new List<string> { "X-PROT" };

        s.ApplyTo(cfg);

        Assert.Equal(0.3048, cfg.UnitsPerFoot);
        Assert.Equal(2, cfg.TrunkDecimals);
        Assert.Equal(StemAverageMethod.Quadratic, cfg.MultiStemAverage);
        Assert.Equal(new[] { "X-PROT" }, cfg.ProtectedLayers);

        // and the grammar is untouched
        Assert.Equal(16, cfg.Codes.Count);         // tree, pole, sign + 13 point-feature rules

        // The never-draw list is deliberately short: ground shots, notes, and the
        // office sheet's check and confidence shots.
        // A code with no rule yet belongs in the unhandled report, not in here.
        Assert.Equal(6, cfg.IgnoreCodes.Count);
    }

    [Fact]
    public void AppliedSettingsActuallyChangeParserOutput()
    {
        var cfg = RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        var s = new FtfSettings();
        s.Trees.TrunkDecimals = 1;
        s.ApplyTo(cfg);

        var p = new FieldCodeParser(cfg).Parse("1", "CON 8 8 10 16 . 28");
        Assert.Equal("10.5", p.Fields["trunk"]);      // was "10" at 0 decimals
    }

    // ------------------------------------------------------------------ precedence

    [Fact]
    public void DrawingFolderBeatsUserProfileBeatsPluginFolder()
    {
        var dwg = TempDir();
        var plugin = TempDir();

        var order = FtfSettings.CandidatePaths(dwg, plugin);
        Assert.Equal(3, order.Count);
        Assert.StartsWith(dwg, order[0]);
        Assert.Equal(FtfSettings.UserProfilePath(), order[1]);
        Assert.StartsWith(plugin, order[2]);
    }

    [Fact]
    public void SettingsBesideTheDrawingWin()
    {
        var dwg = TempDir();
        var plugin = TempDir();

        var shipped = new FtfSettings();
        shipped.Labels.TextHeightPlotted = 0.08;
        shipped.Save(Path.Combine(plugin, FtfSettings.FileName));

        var job = new FtfSettings();
        job.Labels.TextHeightPlotted = 0.125;
        job.Save(Path.Combine(dwg, FtfSettings.FileName));

        var resolved = FtfSettings.Resolve(dwg, plugin, _fx.Config);

        Assert.Equal(SettingsSource.DrawingFolder, resolved.Source);
        Assert.Equal(0.125, resolved.Settings.Labels.TextHeightPlotted);
        Assert.False(resolved.MigratedFromRules);
    }

    [Fact]
    public void FallsBackToThePluginFolderWhenNothingElseExists()
    {
        var dwg = TempDir();
        var plugin = TempDir();

        var shipped = new FtfSettings();
        shipped.Labels.RingCount = 9;
        shipped.Save(Path.Combine(plugin, FtfSettings.FileName));

        // Redirected so a real %APPDATA% settings file cannot pre-empt the chain.
        FtfSettings.ProfileDirectoryOverride = TempDir();
        try
        {
            var resolved = FtfSettings.Resolve(dwg, plugin, _fx.Config);

            Assert.Equal(SettingsSource.PluginFolder, resolved.Source);
            Assert.Equal(9, resolved.Settings.Labels.RingCount);
        }
        finally { FtfSettings.ProfileDirectoryOverride = null; }
    }

    // ---------------------------------------------------------------- round trip

    [Fact]
    public void RoundTripsThroughDisk()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, FtfSettings.FileName);

        var s = new FtfSettings();
        s.Labels.TextStyle = "PMX-ROMANS";
        s.Labels.DrawMask = false;
        s.Labels.LeaderArrowhead = true;
        s.General.ReportLocation = ReportLocation.CustomFolder;
        s.General.ReportFolder = @"C:\reports";
        s.Drip.UnifyByDefault = false;
        s.Drip.Linetype = "DASHED";
        s.Save(path);

        var back = FtfSettings.Load(path);

        Assert.Equal("PMX-ROMANS", back.Labels.TextStyle);
        Assert.False(back.Labels.DrawMask);
        Assert.True(back.Labels.LeaderArrowhead);
        Assert.Equal(ReportLocation.CustomFolder, back.General.ReportLocation);
        Assert.Equal(@"C:\reports", back.General.ReportFolder);
        Assert.False(back.Drip.UnifyByDefault);
        Assert.Equal("DASHED", back.Drip.Linetype);
    }

    [Fact]
    public void SavingKeepsAPreviousVersionAsBak()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, FtfSettings.FileName);

        new FtfSettings().Save(path);
        new FtfSettings().Save(path);

        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public void AFileMissingWholeSectionsStillLoads()
    {
        // Written by an older build that had no drip section.
        var dir = TempDir();
        var path = Path.Combine(dir, FtfSettings.FileName);
        File.WriteAllText(path, "{ \"version\": \"1\", \"labels\": { \"ringCount\": 6 } }");

        var s = FtfSettings.Load(path);

        Assert.Equal(6, s.Labels.RingCount);
        Assert.NotNull(s.Drip);
        Assert.NotNull(s.General);
        Assert.True(s.Drip.UnifyByDefault);          // defaulted, not null
        Assert.Equal(1.0, s.General.UnitsPerFoot);
    }

    // ---------------------------------------------------------------- validation

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void InvalidTextHeightIsRefusedOnSave(double bad)
    {
        var s = new FtfSettings();
        s.Labels.TextHeightPlotted = bad;

        var path = Path.Combine(TempDir(), FtfSettings.FileName);
        var ex = Assert.Throws<ConfigException>(() => s.Save(path));

        Assert.Contains("text height", ex.Message);
        Assert.False(File.Exists(path));      // nothing written
    }

    [Fact]
    public void CustomReportFolderRequiresAFolder()
    {
        var s = new FtfSettings();
        s.General.ReportLocation = ReportLocation.CustomFolder;
        s.General.ReportFolder = "";

        var problems = new List<string>();
        s.Validate(problems);
        Assert.Contains(problems, p => p.Contains("report folder"));
    }

    [Fact]
    public void RestoreDefaultsResetsEverySection()
    {
        var s = new FtfSettings();
        s.Labels.RingCount = 99;
        s.Trees.TrunkDecimals = 5;
        s.General.UnitsPerFoot = 0.3048;

        s.RestoreDefaults();

        Assert.Equal(4, s.Labels.RingCount);
        Assert.Equal(0, s.Trees.TrunkDecimals);
        Assert.Equal(1.0, s.General.UnitsPerFoot);
    }
}
