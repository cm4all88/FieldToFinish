using FieldCodes;
using FieldCodes.Editing;

namespace FieldCodes.Tests;

/// <summary>
/// The configuration ownership contract: factory defaults belong to the installer,
/// office configuration belongs to the user, and no update, reload or resolve may
/// ever cross that line. Only the explicit restore operation writes factory content
/// over user configuration.
/// </summary>
public sealed class RulesStoreTests
{
    private readonly string _root;
    private readonly string _plugin;    // simulated bundle directory (factory)
    private readonly string _office;    // simulated %APPDATA%\FieldToFinish
    private readonly string _drawing;   // simulated drawing folder

    public RulesStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ftf-store-" + Path.GetRandomFileName());
        _plugin = Path.Combine(_root, "bundle");
        _office = Path.Combine(_root, "office");
        _drawing = Path.Combine(_root, "job");
        Directory.CreateDirectory(_plugin);
        Directory.CreateDirectory(_drawing);
        // _office is deliberately not created: it appears on first save.
    }

    /// <summary>A minimal valid rules file whose pole label carries a marker.</summary>
    private static string RulesWithLabel(string label)
    {
        return "{\n" +
               "  \"version\": \"1\",\n" +
               "  \"unitsPerFoot\": 1.0,\n" +
               "  \"//keep-me\": \"a documentation comment\",\n" +
               "  \"codes\": [\n" +
               "    { \"id\": \"pole\",\n" +
               "      \"match\": \"^(?<code>PP)\\\\s+(?<number>[0-9]+)$\",\n" +
               "      \"label\": { \"format\": \"" + label + "\", \"layer\": \"V-UTIL-TEXT\" } }\n" +
               "  ],\n" +
               "  \"modifiers\": []\n" +
               "}";
    }

    private void ShipFactory(string label = "FACTORY POLE {number}")
        => File.WriteAllText(Path.Combine(_plugin, "rules.json"), RulesWithLabel(label));

    private void WriteOffice(string label = "OFFICE POLE {number}")
    {
        Directory.CreateDirectory(_office);
        File.WriteAllText(Path.Combine(_office, "rules.json"), RulesWithLabel(label));
    }

    private RulesResolution Resolve()
        => RulesStore.Resolve(_drawing, _plugin, _office);

    // ------------------------------------------------------------------ precedence

    [Fact]
    public void FactoryAloneIsActive_ButEditsWouldGoToTheOffice()
    {
        ShipFactory();

        var r = Resolve();

        Assert.Equal(RulesSource.Factory, r.Source);
        Assert.Equal(Path.Combine(_plugin, "rules.json"), r.ActivePath);
        // The one invariant that protects everything: the factory file is never
        // the save target.
        Assert.Equal(Path.Combine(_office, "rules.json"), r.SaveTarget);
        Assert.NotEqual(r.ActivePath, r.SaveTarget);
    }

    [Fact]
    public void OfficeConfigurationBeatsFactory()
    {
        ShipFactory();
        WriteOffice();

        var r = Resolve();

        Assert.Equal(RulesSource.Office, r.Source);
        Assert.Equal(Path.Combine(_office, "rules.json"), r.ActivePath);
        Assert.Equal(r.ActivePath, r.SaveTarget);
    }

    [Fact]
    public void ADrawingOverrideBeatsEverything_AndIsEditedInPlace()
    {
        ShipFactory();
        WriteOffice();
        File.WriteAllText(Path.Combine(_drawing, "rules.json"), RulesWithLabel("JOB POLE {number}"));

        var r = Resolve();

        Assert.Equal(RulesSource.Drawing, r.Source);
        Assert.Equal(Path.Combine(_drawing, "rules.json"), r.ActivePath);
        Assert.Equal(r.ActivePath, r.SaveTarget);
    }

    [Fact]
    public void CandidatesListEveryLevelInPrecedenceOrder()
    {
        var candidates = RulesStore.Candidates(_drawing, _plugin, _office);

        Assert.Equal(RulesSource.Drawing, candidates[0].Value);
        Assert.Equal(RulesSource.Office, candidates[2].Value);
        Assert.Equal(RulesSource.Factory, candidates[3].Value);
    }

    // ----------------------------------------------------- updates never destroy

    [Fact]
    public void RedeployingThePluginDoesNotTouchTheOfficeConfiguration()
    {
        ShipFactory("FACTORY V1 {number}");
        WriteOffice("MY OFFICE STANDARD {number}");
        var officeBytes = File.ReadAllText(Path.Combine(_office, "rules.json"));

        // A deploy replaces the factory file wholesale -- that is all it does.
        ShipFactory("FACTORY V2 {number}");

        var r = Resolve();
        Assert.Equal(RulesSource.Office, r.Source);
        Assert.Equal(officeBytes, File.ReadAllText(Path.Combine(_office, "rules.json")));
    }

    [Fact]
    public void UserEditsSurviveAnApplicationUpdate()
    {
        ShipFactory("FACTORY V1 {number}");

        // The user edits: first save lands at the office level.
        var doc = RuleDocument.Load(Resolve().ActivePath);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "MY POLE {number}";
        doc.ApplyEdit(pole);
        doc.Save(Resolve().SaveTarget);

        // The application updates underneath them.
        ShipFactory("FACTORY V2 {number}");

        var active = Resolve();
        Assert.Equal(RulesSource.Office, active.Source);

        var parsed = new FieldCodeParser(RulesConfig.Load(active.ActivePath))
            .Parse("1", "PP 42");
        Assert.Equal("MY POLE 42", parsed.LabelText);
    }

    [Fact]
    public void TheFactoryFileItselfIsNeverModifiedByEditing()
    {
        ShipFactory();
        var factoryBytes = File.ReadAllText(Path.Combine(_plugin, "rules.json"));

        var doc = RuleDocument.Load(Resolve().ActivePath);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "EDITED {number}";
        doc.ApplyEdit(pole);
        doc.Save(Resolve().SaveTarget);

        Assert.Equal(factoryBytes, File.ReadAllText(Path.Combine(_plugin, "rules.json")));
        Assert.True(File.Exists(Path.Combine(_office, "rules.json")));
    }

    [Fact]
    public void CommentsSurviveTheFirstSaveIntoTheOfficeLevel()
    {
        ShipFactory();

        var doc = RuleDocument.Load(Resolve().ActivePath);
        var pole = doc.GetEditable("pole");
        pole.TagPrefix = "P";
        doc.ApplyEdit(pole);
        doc.Save(Resolve().SaveTarget);

        Assert.Contains("//keep-me", File.ReadAllText(Path.Combine(_office, "rules.json")));
    }

    // ------------------------------------------------------------------- restore

    [Fact]
    public void ResolvingAndReloadingNeverRestoreDefaults()
    {
        ShipFactory("FACTORY {number}");
        WriteOffice("OFFICE {number}");

        for (var i = 0; i < 3; i++) Resolve();
        RuleDocument.Load(Resolve().ActivePath);      // a reload

        Assert.Contains("OFFICE", File.ReadAllText(Path.Combine(_office, "rules.json")));
    }

    [Fact]
    public void RestoreFactoryDefaultsIsExplicit_AndKeepsThePreviousOfficeAsBak()
    {
        ShipFactory("FACTORY {number}");
        WriteOffice("OFFICE {number}");
        var r = Resolve();

        RulesStore.RestoreFactoryDefaults(r.FactoryPath, r.OfficePath);

        Assert.Contains("FACTORY", File.ReadAllText(r.OfficePath));
        Assert.Contains("OFFICE", File.ReadAllText(r.OfficePath + ".bak"));
        Assert.Equal(RulesSource.Office, Resolve().Source);
    }

    [Fact]
    public void RestoreCreatesTheOfficeLevelWhenNoneExists()
    {
        ShipFactory("FACTORY {number}");
        var r = Resolve();

        RulesStore.RestoreFactoryDefaults(r.FactoryPath, r.OfficePath);

        Assert.True(File.Exists(r.OfficePath));
        Assert.Equal(RulesSource.Office, Resolve().Source);
    }

    [Fact]
    public void ADamagedFactoryFileCanNeverDestroyTheOfficeConfiguration()
    {
        WriteOffice("OFFICE {number}");
        File.WriteAllText(Path.Combine(_plugin, "rules.json"), "{ not valid json");
        var office = Path.Combine(_office, "rules.json");
        var before = File.ReadAllText(office);

        Assert.Throws<ConfigException>(() =>
            RulesStore.RestoreFactoryDefaults(Path.Combine(_plugin, "rules.json"), office));

        Assert.Equal(before, File.ReadAllText(office));
        Assert.False(File.Exists(office + ".tmp"));
    }

    [Fact]
    public void AFactoryFileFailingEditorValidationIsAlsoRefused()
    {
        // Valid JSON, but a label with no layer -- Compile passes, editor checks fail.
        WriteOffice("OFFICE {number}");
        File.WriteAllText(Path.Combine(_plugin, "rules.json"),
            RulesWithLabel("X").Replace(", \"layer\": \"V-UTIL-TEXT\"", ""));
        var office = Path.Combine(_office, "rules.json");
        var before = File.ReadAllText(office);

        Assert.Throws<ConfigException>(() =>
            RulesStore.RestoreFactoryDefaults(Path.Combine(_plugin, "rules.json"), office));

        Assert.Equal(before, File.ReadAllText(office));
    }

    // ----------------------------------------------------- invalid never replaces

    [Fact]
    public void AnInvalidEditCanNeverReplaceTheLastValidOfficeConfiguration()
    {
        ShipFactory();
        WriteOffice("OFFICE {number}");
        var office = Path.Combine(_office, "rules.json");
        var before = File.ReadAllText(office);

        var doc = RuleDocument.Load(office);
        var pole = doc.GetEditable("pole");
        pole.Match = "^(?<code>PP[";                  // broken regex
        doc.ApplyEdit(pole);

        Assert.Throws<ConfigException>(() => doc.Save(office));
        Assert.Equal(before, File.ReadAllText(office));
    }
}
