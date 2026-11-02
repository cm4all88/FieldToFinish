using FieldCodes;
using FieldCodes.Settings;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Tests;

/// <summary>
/// "diagnostics": { "ignoredCodes": [...] } -- the codes someone looked at on the
/// Unknown Codes page and decided never need finishing. These tests pin the JSON
/// shape, the defaults, loading a file from before the section existed, and that
/// the parser skips ignored codes only where no rule matches.
/// </summary>
[Collection(ProfileDirectoryCollection.Name)]
public sealed class DiagnosticsSettingsTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ftf-" + Path.GetRandomFileName());
        Directory.CreateDirectory(d);
        return d;
    }

    private static RulesConfig FreshRules()
        => RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));

    // --------------------------------------------------------------- the section

    [Fact]
    public void DefaultsToNoIgnoredCodes()
    {
        var s = new FtfSettings();
        Assert.NotNull(s.Diagnostics);
        Assert.Empty(s.Diagnostics.IgnoredCodes);

        var problems = new List<string>();
        s.Diagnostics.Validate(problems);
        Assert.Empty(problems);
    }

    [Fact]
    public void IgnoreNormalisesAndRefusesDuplicatesAndJunk()
    {
        var d = new DiagnosticsSettings();

        Assert.True(d.Ignore(" xyz "));
        Assert.False(d.Ignore("XYZ"));
        Assert.False(d.Ignore("NOT A CODE"));
        Assert.False(d.Ignore(""));

        Assert.Equal(new[] { "XYZ" }, d.IgnoredCodes);
        Assert.True(d.Unignore("xyz"));
        Assert.False(d.Unignore("xyz"));
        Assert.Empty(d.IgnoredCodes);
    }

    [Theory]
    [InlineData("XYZ", true)]
    [InlineData("xyz2", true)]
    [InlineData("XYZ12", true)]
    [InlineData("XYZA", false)]
    [InlineData("XY", false)]
    [InlineData("", false)]
    public void AnEntryCoversItsSequenceNumbers(string code, bool ignored)
    {
        var d = new DiagnosticsSettings();
        d.Ignore("XYZ");
        Assert.Equal(ignored, d.IsIgnored(code));
    }

    [Fact]
    public void ValidationFlagsAnEntryThatIsNotAPlainCode()
    {
        var d = new DiagnosticsSettings { IgnoredCodes = new List<string> { "OK1", "BAD CODE", " " } };
        var problems = new List<string>();
        d.Validate(problems);

        Assert.Single(problems);
        Assert.Contains("BAD CODE", problems[0]);
        Assert.Equal(new[] { "OK1" }, d.Codes());
    }

    // -------------------------------------------------------------------- JSON

    [Fact]
    public void SavesAndLoadsTheIgnoredCodes()
    {
        var path = Path.Combine(TempDir(), FtfSettings.FileName);
        var s = new FtfSettings();
        s.Diagnostics.Ignore("XYZ");
        s.Diagnostics.Ignore("TEMP");
        s.Save(path);

        var json = JObject.Parse(File.ReadAllText(path));
        Assert.Equal(new[] { "XYZ", "TEMP" },
            json["diagnostics"]!["ignoredCodes"]!.Select(t => (string)t!).ToArray());

        var loaded = FtfSettings.Load(path);
        Assert.Equal(new[] { "XYZ", "TEMP" }, loaded.Diagnostics.IgnoredCodes);
    }

    [Fact]
    public void AnOlderFileWithoutTheSectionStillLoads()
    {
        // Written by a build that had never heard of diagnostics.
        var json = JObject.FromObject(new FtfSettings());
        json.Remove("diagnostics");
        var path = Path.Combine(TempDir(), FtfSettings.FileName);
        File.WriteAllText(path, json.ToString());

        var loaded = FtfSettings.Load(path);

        Assert.NotNull(loaded.Diagnostics);
        Assert.Empty(loaded.Diagnostics.IgnoredCodes);
        Assert.Equal(new FtfSettings().LineLabels.MinLengthFeet, loaded.LineLabels.MinLengthFeet);
    }

    [Fact]
    public void ASectionWithoutTheListLoadsEmpty()
    {
        var path = Path.Combine(TempDir(), FtfSettings.FileName);
        var json = JObject.FromObject(new FtfSettings());
        json["diagnostics"] = new JObject();
        File.WriteAllText(path, json.ToString());

        Assert.Empty(FtfSettings.Load(path).Diagnostics.Codes());
    }

    [Fact]
    public void LoadedListsReplaceTheDefaultsRatherThanAppending()
    {
        var path = Path.Combine(TempDir(), FtfSettings.FileName);
        var s = new FtfSettings();
        s.Diagnostics.Ignore("ONE");
        s.Save(path);

        var once = FtfSettings.Load(path);
        once.Save(path);
        Assert.Equal(new[] { "ONE" }, FtfSettings.Load(path).Diagnostics.IgnoredCodes);
    }

    // ------------------------------------------------------------- save target

    [Fact]
    public void ChangesGoBackToTheFileInForce_OrToYourOwnSettings()
    {
        var profileDir = TempDir();
        FtfSettings.ProfileDirectoryOverride = profileDir;
        try
        {
            var besideDrawing = Path.Combine(TempDir(), FtfSettings.FileName);
            Assert.Equal(besideDrawing, DiagnosticsSettings.SavePathFor(new SettingsResolution
            {
                Source = SettingsSource.DrawingFolder, LoadedFrom = besideDrawing
            }));

            var profile = Path.Combine(TempDir(), "WSDOT.json");
            Assert.Equal(profile, DiagnosticsSettings.SavePathFor(new SettingsResolution
            {
                Source = SettingsSource.Profile, LoadedFrom = profile, ProfileName = "WSDOT"
            }));

            // The shipped plugin copy is replaced by every deploy -- never written.
            var mine = FtfSettings.UserProfilePath();
            Assert.Equal(mine, DiagnosticsSettings.SavePathFor(new SettingsResolution
            {
                Source = SettingsSource.PluginFolder, LoadedFrom = Path.Combine(TempDir(), FtfSettings.FileName)
            }));
            Assert.Equal(mine, DiagnosticsSettings.SavePathFor(new SettingsResolution { Source = SettingsSource.Defaults }));
        }
        finally
        {
            FtfSettings.ProfileDirectoryOverride = null;
        }
    }

    // ------------------------------------------------------------------ parser

    [Fact]
    public void TheParserSkipsAnIgnoredCodeWithNoRule()
    {
        var cfg = FreshRules();
        var parser = new FieldCodeParser(cfg);
        Assert.True(parser.Parse("1", "QZ 1015").Unhandled);

        var settings = new FtfSettings();
        settings.Diagnostics.Ignore("QZ");
        settings.ApplyTo(cfg);

        var parsed = new FieldCodeParser(cfg).Parse("1", "QZ2 1015");
        Assert.True(parsed.Ignored);
        Assert.False(parsed.Unhandled);
    }

    [Fact]
    public void AnIgnoredCodeNeverOverridesARule()
    {
        var cfg = FreshRules();
        var settings = new FtfSettings();
        settings.Diagnostics.Ignore("CON");
        settings.ApplyTo(cfg);

        var parsed = new FieldCodeParser(cfg).Parse("1", "CON 18 . 25");
        Assert.False(parsed.Ignored);
        Assert.Equal("tree", parsed.RuleId);
    }

    [Fact]
    public void AnIgnoredSymbolLabelCodeKeepsItsLabel()
    {
        // PP is labelled with its code when shot bare (symbolLabels). Ignoring it on the
        // Unknown Codes page must not silently stop those labels.
        var cfg = FreshRules();
        var settings = new FtfSettings();
        settings.Diagnostics.Ignore("PP");
        settings.ApplyTo(cfg);

        var parsed = new FieldCodeParser(cfg).Parse("1", "PP");
        Assert.False(parsed.Ignored);
        Assert.True(parsed.Recognized);
    }

    [Fact]
    public void ApplyingRepeatedlyNeverStacks_AndUnignoringRestores()
    {
        var cfg = FreshRules();
        var original = cfg.LineworkPatterns.Count;
        var settings = new FtfSettings();
        settings.Diagnostics.Ignore("QZ");

        settings.ApplyTo(cfg);
        settings.ApplyTo(cfg);
        settings.ApplyTo(cfg);
        Assert.Equal(original + 1, cfg.LineworkPatterns.Count);

        settings.Diagnostics.Unignore("QZ");
        settings.ApplyTo(cfg);
        Assert.Equal(original, cfg.LineworkPatterns.Count);
        Assert.True(new FieldCodeParser(cfg).Parse("1", "QZ 1015").Unhandled);
    }

    [Fact]
    public void ARecompiledRulesObjectStartsFromItsOwnList()
    {
        var cfg = FreshRules();
        var settings = new FtfSettings();
        settings.Diagnostics.Ignore("QZ");
        settings.ApplyTo(cfg);

        cfg.Compile();                       // rebuilds the linework list from the file
        var compiled = cfg.LineworkPatterns.Count;
        settings.ApplyTo(cfg);

        Assert.Equal(compiled + 1, cfg.LineworkPatterns.Count);
        Assert.True(new FieldCodeParser(cfg).Parse("1", "QZ 1015").Ignored);
    }
}
