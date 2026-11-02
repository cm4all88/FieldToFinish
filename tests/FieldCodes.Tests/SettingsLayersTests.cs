using System.Security.Cryptography;
using System.Text;
using FieldCodes;
using FieldCodes.Settings;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Tests;

/// <summary>
/// One configuration system: built-in &lt; office &lt; my defaults &lt; profile &lt; project,
/// merged value by value. Saving writes a level's differences only; the office level
/// needs its PIN when one is set.
/// </summary>
[Collection(ProfileDirectoryCollection.Name)]
public sealed class SettingsLayersTests : IClassFixture<RulesFixture>, IDisposable
{
    private readonly RulesFixture _fx;
    private readonly string _office = TempDir();
    private readonly string _user = TempDir();
    private readonly string _project = TempDir();

    public SettingsLayersTests(RulesFixture fx)
    {
        _fx = fx;
        // The user level (and the profiles folder under it) lives in a temp folder, so a real
        // %APPDATA% settings file on this machine cannot take part.
        FtfSettings.ProfileDirectoryOverride = _user;
    }

    public void Dispose() => FtfSettings.ProfileDirectoryOverride = null;

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ftf-layers-" + Path.GetRandomFileName());
        Directory.CreateDirectory(d);
        return d;
    }

    private string OfficeFile => Path.Combine(_office, FtfSettings.FileName);
    private static string UserFile => FtfSettings.UserProfilePath();
    private string ProjectFile => Path.Combine(_project, FtfSettings.FileName);

    private static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private static JObject Read(string path) => JObject.Parse(File.ReadAllText(path));

    /// <summary>The settings a level file holds, without its version and admin sections.</summary>
    private static string[] SettingsIn(string path)
    {
        var json = Read(path);
        json.Remove("version");
        json.Remove("admin");
        return SettingsLayers.Leaves(json).Select(l => l.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    private SettingsResolution Resolve(string? profile = null)
        => FtfSettings.Resolve(_project, _office, _fx.Config, profile);

    private const string Office = @"{ ""version"": ""2"",
        ""labels"": { ""ringCount"": 5, ""paddingPlotted"": 0.03, ""drawMask"": false },
        ""trees"": { ""trunkDecimals"": 1 } }";

    // ------------------------------------------------------------------ merging

    [Fact]
    public void EveryLevelMergesInOrder_BuiltInOfficeMyDefaultsProfileProject()
    {
        Write(OfficeFile, Office);
        Write(UserFile, @"{ ""labels"": { ""ringCount"": 6, ""paddingPlotted"": 0.04, ""drawMask"": true } }");
        Write(FtfSettings.ProfilePath("City")!, @"{ ""labels"": { ""ringCount"": 7, ""drawMask"": false } }");
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 8 } }");

        var r = Resolve("City");
        var s = r.Settings;

        Assert.Equal(1, s.Trees.TrunkDecimals);              // office
        Assert.Equal(0.04, s.Labels.PaddingPlotted);         // my defaults over office
        Assert.False(s.Labels.DrawMask);                     // profile over my defaults
        Assert.Equal(8, s.Labels.RingCount);                 // project over everything
        Assert.Equal(0.08, s.Labels.TextHeightPlotted);      // nobody sets it: built-in

        Assert.Equal(SettingsLevel.Office, r.Provenance["trees.trunkDecimals"].From);
        Assert.Equal(SettingsLevel.User, r.Provenance["labels.paddingPlotted"].From);
        Assert.Equal(SettingsLevel.Profile, r.Provenance["labels.drawMask"].From);
        Assert.Equal(SettingsLevel.Project, r.Provenance["labels.ringCount"].From);
        Assert.Equal(SettingsLevel.BuiltIn, r.Provenance["labels.textHeightPlotted"].From);
        Assert.Equal("Profile \"City\"", r.Provenance["labels.drawMask"].FromName);

        // The resolution still names the highest file, as before.
        Assert.Equal(SettingsSource.DrawingFolder, r.Source);
        Assert.Equal(ProjectFile, r.LoadedFrom);
        Assert.Equal("City", r.ProfileName);
        Assert.Null(r.MissingProfile);

        Assert.Equal(5, r.Levels.Count);
        Assert.Equal(new[] { SettingsLevel.BuiltIn, SettingsLevel.Office, SettingsLevel.User, SettingsLevel.Profile, SettingsLevel.Project },
                     r.Levels.Select(l => l.Level));
        Assert.All(r.Levels, l => Assert.True(l.Exists));
        Assert.Equal(OfficeFile, r.PathFor(SettingsLevel.Office));
        Assert.Equal(UserFile, r.PathFor(SettingsLevel.User));
    }

    [Fact]
    public void AProjectFileWithOneValueOverridesOnlyThatValue()
    {
        Write(OfficeFile, Office);
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 9 } }");

        var r = Resolve();

        Assert.Equal(9, r.Settings.Labels.RingCount);
        Assert.Equal(0.03, r.Settings.Labels.PaddingPlotted);   // still the office's
        Assert.False(r.Settings.Labels.DrawMask);
        Assert.Equal(1, r.Settings.Trees.TrunkDecimals);
        Assert.Equal(SettingsLevel.Project, r.Provenance["labels.ringCount"].From);
        Assert.Equal(SettingsLevel.Office, r.Provenance["labels.paddingPlotted"].From);
        Assert.Equal(1, r.LevelInfo(SettingsLevel.Project)!.ValueCount);
    }

    [Fact]
    public void MyDefaultsNeverOverrideAProjectValue()
    {
        Write(UserFile, @"{ ""labels"": { ""ringCount"": 6 } }");
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 9 } }");

        var r = Resolve();
        var ring = r.Provenance["labels.ringCount"];

        Assert.Equal(9, r.Settings.Labels.RingCount);
        Assert.Equal(SettingsLevel.Project, ring.From);
        Assert.Contains(ring.Overridden, v => v.Level == SettingsLevel.User && (int)v.Value == 6);
        Assert.Contains(ring.Overridden, v => v.Level == SettingsLevel.BuiltIn && (int)v.Value == 4);
        Assert.Equal(SettingsLevel.User, ring.InheritsFrom);
        Assert.Equal(6, (int)ring.InheritedValue);
    }

    [Fact]
    public void AProfileSitsBetweenMyDefaultsAndTheProject()
    {
        Write(UserFile, @"{ ""labels"": { ""ringCount"": 6 } }");
        Write(FtfSettings.ProfilePath("WSDOT")!, @"{ ""labels"": { ""ringCount"": 7 } }");

        var withoutProject = Resolve("WSDOT");
        Assert.Equal(7, withoutProject.Settings.Labels.RingCount);
        Assert.Equal(SettingsSource.Profile, withoutProject.Source);

        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 9 } }");
        var withProject = Resolve("WSDOT");
        Assert.Equal(9, withProject.Settings.Labels.RingCount);
        Assert.Equal(new[] { SettingsLevel.Profile, SettingsLevel.User, SettingsLevel.BuiltIn },
                     withProject.Provenance["labels.ringCount"].Overridden.Select(v => v.Level));
    }

    [Fact]
    public void ListsReplace_TheyDoNotMerge()
    {
        Write(OfficeFile, @"{ ""drawOrder"": { ""protectedLayers"": [ ""A-OFFICE"", ""B-OFFICE"" ] } }");
        Write(ProjectFile, @"{ ""drawOrder"": { ""protectedLayers"": [ ""C-PROJECT"" ] } }");

        var r = Resolve();

        Assert.Equal(new[] { "C-PROJECT" }, r.Settings.DrawOrder.ProtectedLayers);
        var p = r.Provenance["drawOrder.protectedLayers"];
        Assert.Equal(SettingsLevel.Project, p.From);
        Assert.Contains(p.Overridden, v => v.Level == SettingsLevel.Office && v.ValueText == "A-OFFICE, B-OFFICE");
    }

    [Fact]
    public void ProvenanceCoversEverySetting_AndOnlySettings()
    {
        var r = Resolve();

        Assert.True(r.Provenance.Count > 100);
        Assert.DoesNotContain(r.Provenance.Keys, k => k.EndsWith(".Title", StringComparison.Ordinal) ||
                                                      k.EndsWith(".AffectedCommands", StringComparison.Ordinal) ||
                                                      k == "version");
        Assert.All(r.Provenance.Values, p => Assert.Equal(SettingsLevel.BuiltIn, p.From));
        Assert.Equal("Point Labels > Text height (plotted)", r.Provenance["labels.textHeightPlotted"].DisplayName);
    }

    [Fact]
    public void TheModelReadsItsOwnOutputBackUnchanged()
    {
        // Lists a constructor fills are replaced, not appended to, when JSON is read back.
        var builtIn = SettingsLayers.PureBuiltIn();
        Assert.True(JToken.DeepEquals(builtIn, SettingsLayers.ToJson(SettingsLayers.FromJson(builtIn))));
    }

    [Theory]
    [InlineData("labels.textHeightPlotted", "Point Labels > Text height (plotted)")]
    [InlineData("dips.doubleLineThresholdIn", "Utility Dips > Double line threshold (in)")]
    [InlineData("compare.writeCsvReport", "Survey Comparison > Write CSV report")]
    [InlineData("general.unitsPerFoot", "General > Units per foot")]
    public void SettingNamesReadLikeTheWindow(string path, string expected)
    {
        Assert.Equal(expected, SettingsLayers.Humanize(path));
    }

    // ------------------------------------------------------------------ saving

    [Fact]
    public void ASaveWritesOnlyTheDifferences_AndReadsBackTheSame()
    {
        Write(OfficeFile, Office);
        var r = Resolve();
        r.Settings.Labels.RingCount = 6;
        r.Settings.Labels.TextStyle = "PMX-ROMANS";
        var expected = SettingsLayers.ToJson(r.Settings);

        var result = SettingsLayers.SaveChanges(r, SettingsLevel.Project, r.Settings);

        Assert.Equal(new[] { "labels.ringCount", "labels.textStyle" }, SettingsIn(ProjectFile));
        Assert.Equal("2", (string?)Read(ProjectFile)["version"]);
        Assert.Equal(2, result.Changed);
        Assert.Equal(2, result.Stored);
        Assert.Empty(result.Hidden);

        var again = Resolve();
        Assert.True(JToken.DeepEquals(expected, SettingsLayers.ToJson(again.Settings)));

        // The resolution that was saved is refreshed in place.
        Assert.Equal(SettingsLevel.Project, r.Provenance["labels.ringCount"].From);
        Assert.Equal(SettingsSource.DrawingFolder, r.Source);
    }

    [Fact]
    public void OfficeChangesStillReachAProjectAfterASave()
    {
        Write(OfficeFile, Office);
        var r = Resolve();
        r.Settings.Labels.RingCount = 6;
        SettingsLayers.SaveChanges(r, SettingsLevel.Project, r.Settings);

        Write(OfficeFile, Office.Replace("\"trunkDecimals\": 1", "\"trunkDecimals\": 2"));
        var after = Resolve();

        Assert.Equal(2, after.Settings.Trees.TrunkDecimals);
        Assert.Equal(6, after.Settings.Labels.RingCount);
    }

    [Fact]
    public void SavingAValueEqualToTheInheritedOneRemovesItFromTheLevel()
    {
        Write(OfficeFile, Office);
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 9, ""textStyle"": ""PMX-ROMANS"" } }");
        var r = Resolve();
        r.Settings.Labels.RingCount = 5;     // what the office says

        var result = SettingsLayers.SaveChanges(r, SettingsLevel.Project, r.Settings);

        Assert.Equal(new[] { "labels.textStyle" }, SettingsIn(ProjectFile));
        Assert.Equal(1, result.Removed);
        Assert.Equal(SettingsLevel.Office, r.Provenance["labels.ringCount"].From);
        Assert.Equal(5, r.Settings.Labels.RingCount);
    }

    [Fact]
    public void MyDefaultsDoNotLeakIntoAProjectSave()
    {
        Write(OfficeFile, Office);
        Write(UserFile, @"{ ""labels"": { ""ringCount"": 6 } }");
        var r = Resolve();
        Assert.Equal(6, r.Settings.Labels.RingCount);
        r.Settings.Labels.TextStyle = "PMX-ROMANS";

        SettingsLayers.SaveChanges(r, SettingsLevel.Project, r.Settings);

        Assert.Equal(new[] { "labels.textStyle" }, SettingsIn(ProjectFile));
    }

    [Fact]
    public void AProjectValueEqualToMyDefaultIsKept_SoTheProjectDoesNotDependOnWhoSavedIt()
    {
        Write(OfficeFile, Office);                                         // office: 5
        Write(UserFile, @"{ ""labels"": { ""ringCount"": 9 } }");           // mine: 9
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 9 } }");        // project: 9
        var r = Resolve();
        r.Settings.Labels.TextStyle = "PMX-ROMANS";

        SettingsLayers.SaveChanges(r, SettingsLevel.Project, r.Settings);

        Assert.Equal(new[] { "labels.ringCount", "labels.textStyle" }, SettingsIn(ProjectFile));
        File.Delete(UserFile);                                             // a colleague without my defaults
        Assert.Equal(9, Resolve().Settings.Labels.RingCount);
    }

    [Fact]
    public void AChangeSavedBelowAHigherLevelIsReportedAsNotInForce()
    {
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 9 } }");
        var r = Resolve();
        r.Settings.Labels.RingCount = 7;

        var result = SettingsLayers.SaveChanges(r, SettingsLevel.User, r.Settings);

        Assert.Equal(7, (int)Read(UserFile)["labels"]!["ringCount"]!);
        var hidden = Assert.Single(result.Hidden);
        Assert.Contains("Project", hidden);
        Assert.Equal(9, r.Settings.Labels.RingCount);       // the window now shows what is in force
    }

    [Fact]
    public void SavingToTheOfficeDoesNotCopyTheProjectsOverrides()
    {
        Write(OfficeFile, Office);
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 9 } }");
        var r = Resolve();
        r.Settings.Labels.TextStyle = "PMX-ROMANS";

        SettingsLayers.SaveChanges(r, SettingsLevel.Office, r.Settings);

        var office = Read(OfficeFile);
        Assert.Equal(5, (int)office["labels"]!["ringCount"]!);
        Assert.Equal("PMX-ROMANS", (string?)office["labels"]!["textStyle"]);
        // Written whole: every setting is in the office file.
        Assert.Equal(r.Provenance.Count, SettingsIn(OfficeFile).Length);
    }

    [Fact]
    public void AnOldCompleteFileStillWorks_AndShrinksToItsDifferencesWhenSaved()
    {
        Write(OfficeFile, Office);
        var old = new FtfSettings();                 // written by an earlier build: everything, version 1
        old.Labels.RingCount = 9;
        old.Save(ProjectFile);
        Assert.Equal("1", (string?)Read(ProjectFile)["version"]);

        var r = Resolve();

        // As before: the complete file wins outright; the office file below it is not used.
        Assert.Equal(9, r.Settings.Labels.RingCount);
        Assert.Equal(0, r.Settings.Trees.TrunkDecimals);
        Assert.True(r.LevelInfo(SettingsLevel.Project)!.Complete);
        Assert.True(r.LevelInfo(SettingsLevel.Office)!.Shadowed);
        Assert.Equal(SettingsLevel.Project, r.Provenance["trees.trunkDecimals"].From);
        Assert.Contains(r.Provenance["trees.trunkDecimals"].Overridden, v => v.Level == SettingsLevel.Office && v.Shadowed);
        var before = SettingsLayers.ToJson(r.Settings);

        SettingsLayers.SaveChanges(r, SettingsLevel.Project, r.Settings);

        // Only what differs from the office standard is left: its own ring count, and the
        // built-in values it had copied where the office says otherwise.
        Assert.Equal(new[] { "labels.drawMask", "labels.paddingPlotted", "labels.ringCount", "trees.trunkDecimals" },
                     SettingsIn(ProjectFile));
        Assert.True(JToken.DeepEquals(before, SettingsLayers.ToJson(Resolve().Settings)));
        Assert.False(r.LevelInfo(SettingsLevel.Project)!.Complete);
    }

    [Fact]
    public void SaveOnResolvedSettingsWritesTheLevelsDifferences_AndAnExportIsComplete()
    {
        Write(OfficeFile, Office);
        var r = Resolve();
        r.Settings.Labels.RingCount = 6;

        r.Settings.Save(ProjectFile);
        Assert.Equal(new[] { "labels.ringCount" }, SettingsIn(ProjectFile));

        var export = Path.Combine(TempDir(), "export.json");
        r.Settings.Save(export);
        Assert.Equal("1", (string?)Read(export)["version"]);
        Assert.Equal(6, FtfSettings.Load(export).Labels.RingCount);
        Assert.Equal(1, FtfSettings.Load(export).Trees.TrunkDecimals);
    }

    [Fact]
    public void SaveAsProfileKeepsWhatDiffersFromTheOfficeAndMyDefaults()
    {
        Write(OfficeFile, Office);
        Write(UserFile, @"{ ""labels"": { ""textStyle"": ""MINE"" } }");
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 9 } }");
        var r = Resolve();

        FtfSettings.SaveProfile("Client", r.Settings);

        Assert.Equal(new[] { "labels.ringCount" }, SettingsIn(FtfSettings.ProfilePath("Client")!));
    }

    [Fact]
    public void ValuesSeededFromTheRulesFileSurviveTheFirstSave()
    {
        var rules = RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        rules.TrunkDecimals = 2;
        var r = FtfSettings.Resolve(_project, _office, rules);
        Assert.True(r.MigratedFromRules);
        Assert.Equal(2, r.Settings.Trees.TrunkDecimals);

        SettingsLayers.SaveChanges(r, SettingsLevel.User, r.Settings);

        Assert.Contains("trees.trunkDecimals", SettingsIn(UserFile));
        var after = FtfSettings.Resolve(_project, _office, null);
        Assert.False(after.MigratedFromRules);
        Assert.Equal(2, after.Settings.Trees.TrunkDecimals);
    }

    // ---------------------------------------------------------------- inheriting

    [Fact]
    public void InheritRemovesOneValueFromItsLevel()
    {
        Write(OfficeFile, Office);
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 9, ""textStyle"": ""PMX-ROMANS"" } }");
        var r = Resolve();

        Assert.True(SettingsLayers.Inherit(r, SettingsLevel.Project, "labels.ringCount"));

        Assert.Equal(new[] { "labels.textStyle" }, SettingsIn(ProjectFile));
        Assert.Equal(5, r.Settings.Labels.RingCount);
        Assert.Equal(SettingsLevel.Office, r.Provenance["labels.ringCount"].From);
        Assert.False(SettingsLayers.Inherit(r, SettingsLevel.Project, "labels.ringCount"));
        Assert.True(File.Exists(ProjectFile + ".bak"));
    }

    [Fact]
    public void InheritFromACompleteFileLetsTheLevelBelowShowThrough()
    {
        Write(OfficeFile, Office);
        var old = new FtfSettings();
        old.Labels.RingCount = 9;
        old.Save(ProjectFile);
        var r = Resolve();
        Assert.Equal(SettingsLevel.Office, r.Provenance["labels.ringCount"].InheritsFrom);

        SettingsLayers.Inherit(r, SettingsLevel.Project, "labels.ringCount");

        Assert.Equal(5, r.Settings.Labels.RingCount);
        Assert.Equal(new[] { "labels.drawMask", "labels.paddingPlotted", "trees.trunkDecimals" }, SettingsIn(ProjectFile));
    }

    [Fact]
    public void ClearingALevelRemovesAllOfItsValues()
    {
        Write(OfficeFile, Office);
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 9 } }");
        Write(FtfSettings.ProfilePath("City")!, @"{ ""labels"": { ""drawMask"": true } }");
        var r = Resolve("City");

        Assert.True(SettingsLayers.ClearLevel(r, SettingsLevel.Project));
        Assert.False(File.Exists(ProjectFile));
        Assert.True(File.Exists(ProjectFile + ".bak"));
        Assert.Equal(5, r.Settings.Labels.RingCount);

        Assert.True(SettingsLayers.ClearLevel(r, SettingsLevel.Profile));
        Assert.True(FtfSettings.ProfileExists("City"));    // still there, so the drawing still finds it
        Assert.Empty(SettingsIn(FtfSettings.ProfilePath("City")!));
        Assert.False(r.Settings.Labels.DrawMask);
        Assert.Equal("City", r.ProfileName);
    }

    // ------------------------------------------------------------- office PIN

    [Fact]
    public void ThePinIsStoredOnlyAsASaltedHash()
    {
        var admin = OfficeAuthorization.CreateAdmin("2468");

        Assert.DoesNotContain("2468", admin.ToString());
        Assert.True(OfficeAuthorization.HasPin(admin));
        Assert.True(OfficeAuthorization.Verify(admin, "2468"));
        Assert.False(OfficeAuthorization.Verify(admin, "1357"));
        Assert.False(OfficeAuthorization.Verify(admin, ""));
        Assert.NotEqual((string?)admin["salt"], (string?)OfficeAuthorization.CreateAdmin("2468")["salt"]);
    }

    [Fact]
    public void ThePinHashIsSha256OfSaltAndPin()
    {
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("abc" + "1234"))).ToLowerInvariant();
        Assert.Equal(expected, OfficeAuthorization.HashPin("abc", "1234"));
        Assert.True(OfficeAuthorization.Verify("abc", expected.ToUpperInvariant(), "1234"));
    }

    [Fact]
    public void WithNoPinThereIsNothingToCheck_AndAShortPinIsRefused()
    {
        Assert.False(OfficeAuthorization.HasPin((JObject?)null));
        Assert.True(OfficeAuthorization.Verify((JObject?)null, null));
        Assert.False(OfficeAuthorization.HasPin(OfficeFile));     // no office file at all
        Assert.NotNull(OfficeAuthorization.PinProblem("12"));
        Assert.Null(OfficeAuthorization.PinProblem("2468"));
        Assert.Throws<ArgumentException>(() => OfficeAuthorization.CreateAdmin("1"));
    }

    [Fact]
    public void TheOfficeStandardNeedsItsPin()
    {
        var office = JObject.Parse(Office);
        office["admin"] = OfficeAuthorization.CreateAdmin("2468");
        Write(OfficeFile, office.ToString());
        var r = Resolve();
        Assert.True(r.LevelInfo(SettingsLevel.Office)!.PinProtected);
        r.Settings.Labels.RingCount = 6;

        Assert.Throws<OfficeAuthorizationException>(() => SettingsLayers.SaveChanges(r, SettingsLevel.Office, r.Settings));
        Assert.Throws<OfficeAuthorizationException>(() => SettingsLayers.SaveChanges(r, SettingsLevel.Office, r.Settings, "1357"));
        Assert.Equal(5, (int)Read(OfficeFile)["labels"]!["ringCount"]!);   // untouched

        SettingsLayers.SaveChanges(r, SettingsLevel.Office, r.Settings, "2468");

        var saved = Read(OfficeFile);
        Assert.Equal(6, (int)saved["labels"]!["ringCount"]!);
        Assert.True(OfficeAuthorization.Verify((JObject?)saved["admin"], "2468"));   // the PIN survives the save
        Assert.DoesNotContain("2468", File.ReadAllText(OfficeFile));
        Assert.Throws<OfficeAuthorizationException>(() => SettingsLayers.Inherit(r, SettingsLevel.Office, "labels.ringCount"));
        Assert.Throws<OfficeAuthorizationException>(() => SettingsLayers.ClearLevel(r, SettingsLevel.Office, "0000"));
    }

    [Fact]
    public void TheFirstOfficeSaveCanSetAPin()
    {
        var r = Resolve();
        r.Settings.Labels.RingCount = 6;

        var result = SettingsLayers.SaveChanges(r, SettingsLevel.Office, r.Settings, null, "2468");

        Assert.True(result.PinSet);
        Assert.True(OfficeAuthorization.HasPin(OfficeFile));
        Assert.True(OfficeAuthorization.VerifyPin(OfficeFile, "2468"));
        r.Settings.Labels.RingCount = 7;
        Assert.Throws<OfficeAuthorizationException>(() => SettingsLayers.SaveChanges(r, SettingsLevel.Office, r.Settings));
    }

    [Fact]
    public void ProjectAndPersonalSavesNeverAskForThePin()
    {
        var office = JObject.Parse(Office);
        office["admin"] = OfficeAuthorization.CreateAdmin("2468");
        Write(OfficeFile, office.ToString());
        var r = Resolve();
        r.Settings.Labels.RingCount = 6;

        SettingsLayers.SaveChanges(r, SettingsLevel.Project, r.Settings);
        r.Settings.Labels.TextStyle = "MINE";
        SettingsLayers.SaveChanges(r, SettingsLevel.User, r.Settings);

        Assert.Equal(6, Resolve().Settings.Labels.RingCount);
        Assert.Equal("MINE", Resolve().Settings.Labels.TextStyle);
    }

    [Fact]
    public void AWritableFolderHasNoWriteProblem()
    {
        Assert.Null(OfficeAuthorization.WriteProblem(OfficeFile));
        Assert.NotNull(OfficeAuthorization.WriteProblem(Path.Combine(_office, "missing", FtfSettings.FileName)));
        Assert.NotNull(OfficeAuthorization.WriteProblem(null));
    }

    // ------------------------------------------------------------- bad files

    [Fact]
    public void ACorruptLevelFileIsReported_NotSkipped()
    {
        Write(OfficeFile, Office);
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 7 ");

        var ex = Assert.Throws<ConfigException>(() => Resolve());
        Assert.Contains(ProjectFile, ex.Message);
        Assert.Contains("Project", ex.Message);
    }

    [Fact]
    public void ALevelFileThatIsNotAnObjectOrIsEmptyIsReported()
    {
        Write(UserFile, "[ 1, 2 ]");
        Assert.Contains(UserFile, Assert.Throws<ConfigException>(() => Resolve()).Message);

        Write(UserFile, "   ");
        Assert.Contains("empty", Assert.Throws<ConfigException>(() => Resolve()).Message);
    }

    [Fact]
    public void AValueOfTheWrongKindNamesItsFile()
    {
        Write(OfficeFile, Office);
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": ""lots"" } }");

        var ex = Assert.Throws<ConfigException>(() => Resolve());
        Assert.Contains(ProjectFile, ex.Message);
    }

    // ------------------------------------------------------------------ misc

    [Fact]
    public void TheFileStampChangesWhenALevelFileChanges()
    {
        var before = SettingsLayers.FileStamp(_project, _office, null);
        Write(ProjectFile, @"{ ""labels"": { ""ringCount"": 9 } }");
        Assert.NotEqual(before, SettingsLayers.FileStamp(_project, _office, null));
    }

    [Fact]
    public void ImportingAnOverrideFileChangesOnlyItsValues()
    {
        var current = new FtfSettings();
        current.Labels.TextStyle = "KEEP";
        var file = Path.Combine(TempDir(), "overrides.json");
        Write(file, @"{ ""version"": ""2"", ""labels"": { ""ringCount"": 9 } }");

        var imported = SettingsLayers.ReadForImport(file, current);

        Assert.Equal(9, imported.Labels.RingCount);
        Assert.Equal("KEEP", imported.Labels.TextStyle);
    }
}
