using FieldCodes.Editing;
using FieldCodes.Standards;

namespace FieldCodes.Tests;

/// <summary>
/// Settings > Point Features > Drafting Standard edits "featureStandard" in the rules file through
/// RuleDocument: every entry in force is shown, and only what differs from the built-in PMX
/// standard is written back, so an untouched office file gains nothing and a later fix to the
/// built-in standard still reaches it.
/// </summary>
public sealed class RuleDocumentFeatureStandardTests
{
    private static RuleDocument Factory() => RuleDocument.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));

    private static EffectiveRequirement Resolve(RulesConfig cfg, string description)
        => FeatureRequirements.For(cfg, new FieldCodeParser(cfg).Parse("1", description), description);

    [Fact]
    public void TheFactoryRulesUseTheBuiltInStandardAndSavingItUnchangedAddsNothing()
    {
        var doc = Factory();
        Assert.True(doc.FeatureStandardIsBuiltIn());
        var rows = doc.GetFeatureRequirements();
        Assert.Equal(FeatureStandard.Pmx().Requirements.Select(r => r.Key), rows.Select(r => r.Key));
        Assert.All(rows, r => Assert.True(RuleDocument.SameAsBuiltIn(r), r.Key));
        Assert.Contains(rows, r => r.Key == "GS" && r.Label == LabelRequirement.None && r.Surface == true);
        Assert.Contains(rows, r => r.Key == "ZK" && r.Disposition == IntendedDisposition.ControlCheck);
        Assert.Contains(rows, r => r.Key == "default");

        doc.SetFeatureStandard(rows, doc.GetSignCategories());
        Assert.Null(Newtonsoft.Json.Linq.JObject.Parse(doc.Render())["featureStandard"]);
        Assert.True(doc.FeatureStandardIsBuiltIn());
        // The factory file's own note on the section stays.
        Assert.Contains("\"//featureStandard\"", doc.Render());
    }

    [Fact]
    public void OneChangedEntryIsAllThatIsWrittenAndTheRestStayBuiltIn()
    {
        var doc = Factory();
        var rows = doc.GetFeatureRequirements();
        rows.Single(r => r.Key == "WFH, FH").Label = LabelRequirement.Required;
        doc.SetFeatureStandard(rows, doc.GetSignCategories());

        var text = doc.Render();
        Assert.Contains("\"//featureStandard\"", text);
        var section = Newtonsoft.Json.Linq.JObject.Parse(text)["featureStandard"]!;
        var written = section["requirements"]!.ToObject<List<FeatureRequirement>>()!;
        Assert.Equal("WFH, FH", Assert.Single(written).Key);
        Assert.Null(section["signCategories"]);

        var cfg = doc.CompileCandidate();
        Assert.Equal(LabelRequirement.Required, Resolve(cfg, "WFH 12").Label);
        Assert.Equal(SymbolRequirement.Specific, Resolve(cfg, "WFH 12").Symbol);          // the rest of its entry as it was
        Assert.Equal(IntendedDisposition.SurfaceOnly, Resolve(cfg, "GS").Disposition);    // other entries built in

        // Read back: the file's version is shown in the built-in's place, and putting it back removes the section.
        var again = doc.GetFeatureRequirements();
        Assert.Equal(rows.Count, again.Count);
        var hydrant = again.Single(r => r.Key == "WFH, FH");
        Assert.False(RuleDocument.SameAsBuiltIn(hydrant));
        hydrant.Label = LabelRequirement.None;
        doc.SetFeatureStandard(again, doc.GetSignCategories());
        Assert.Null(Newtonsoft.Json.Linq.JObject.Parse(doc.Render())["featureStandard"]);
    }

    [Fact]
    public void ABuiltInEntryIsSwitchedOffAndAnOfficeEntryIsLookedAtFirst()
    {
        var doc = Factory();
        var rows = doc.GetFeatureRequirements().ToList();
        rows.Single(r => r.Key.StartsWith("SN*")).Enabled = false;
        rows.Insert(0, new FeatureRequirement { Name = "Water meter", Codes = new List<string> { "wmt" }, Symbol = SymbolRequirement.Specific, SymbolStyle = "PMX WATER METER" });
        doc.SetFeatureStandard(rows, doc.GetSignCategories());

        var cfg = doc.CompileCandidate();
        var meter = Resolve(cfg, "WMT");
        Assert.True(meter.Specific);
        Assert.Equal(SymbolRequirement.Specific, meter.Symbol);
        Assert.Equal("PMX WATER METER", meter.SymbolStyle);
        // With the sign entry off, a house number sign is no longer named by the standard.
        Assert.False(Resolve(cfg, "HOUSE NUMBER 4735 SIGN").Specific);

        var shown = doc.GetFeatureRequirements();
        Assert.Equal("WMT", shown[0].Key);                                   // office entries first, as they are looked at
        Assert.Null(RuleDocument.BuiltInRequirement(shown[0].Key));
        Assert.False(shown.Single(r => r.Key.StartsWith("SN*")).Enabled);
    }

    [Fact]
    public void SignCategoryWordsAreEditedTheSameWay()
    {
        var doc = Factory();
        var signs = doc.GetSignCategories();
        Assert.Equal(FeatureStandard.Pmx().SignCategories.Select(c => c.Category), signs.Select(c => c.Category));
        signs.Single(c => c.Category == SignCategory.Address).Words.Add("lot number");
        doc.SetFeatureStandard(doc.GetFeatureRequirements(), signs);

        var section = Newtonsoft.Json.Linq.JObject.Parse(doc.Render())["featureStandard"]!;
        var written = section["signCategories"]!.ToObject<List<SignCategoryRule>>()!;
        Assert.Equal(SignCategory.Address, Assert.Single(written).Category);
        Assert.Contains("LOT NUMBER", written[0].Words);
        var cfg = doc.CompileCandidate();
        Assert.Equal(SignCategory.Address, cfg.FeatureStandard.SignCategoryOf("SN", "SN LOT NUMBER 12", null));
        Assert.Equal(SignCategory.Regulatory, cfg.FeatureStandard.SignCategoryOf("SN", "SN STOP", null));
    }

    [Fact]
    public void AStandardThatCannotLoadIsRefusedAndNothingChanges()
    {
        var doc = Factory();
        var before = doc.Render();
        var rows = doc.GetFeatureRequirements().ToList();
        rows.Add(new FeatureRequirement { Name = "Broken", Match = "(unclosed", Symbol = SymbolRequirement.PointStyle });
        var ex = Assert.Throws<ConfigException>(() => doc.SetFeatureStandard(rows, doc.GetSignCategories()));
        Assert.Contains("was not changed", ex.Message);
        Assert.Equal(before, doc.Render());

        rows = doc.GetFeatureRequirements().ToList();
        rows.Add(new FeatureRequirement { Name = "Hydrant again", Codes = new List<string> { "WFH", "FH" } });
        ex = Assert.Throws<ConfigException>(() => doc.SetFeatureStandard(rows, doc.GetSignCategories()));
        Assert.Contains("two entries for WFH, FH", ex.Message);
        Assert.Equal(before, doc.Render());
    }

    [Fact]
    public void TheSectionsOwnCommentsAreKept()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules.json")).TrimEnd();
        text = text.Substring(0, text.Length - 1) + ",\n  \"featureStandard\": { \"//\": \"office note\", \"requirements\": [ { \"codes\": [\"GS\"], \"review\": true } ] }\n}\n";
        var doc = RuleDocument.FromText(text);
        Assert.False(doc.FeatureStandardIsBuiltIn());
        var gs = doc.GetFeatureRequirements().Single(r => r.Key == "GS");
        Assert.True(gs.Review);
        Assert.Null(gs.Label);                             // the file's entry replaces the built-in one whole
        gs.Note = "every ground shot checked this year";
        doc.SetFeatureStandard(doc.GetFeatureRequirements().Select(r => r.Key == "GS" ? gs : r), doc.GetSignCategories());
        var section = Newtonsoft.Json.Linq.JObject.Parse(doc.Render())["featureStandard"]!;
        Assert.Equal("office note", (string?)section["//"]);
        Assert.Equal("every ground shot checked this year", (string?)section["requirements"]![0]!["note"]);
    }
}
