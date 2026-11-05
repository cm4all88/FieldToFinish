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

    // ------------------------------------------------------------ exceptions, one grid row each

    [Fact]
    public void ExceptionsRoundTripThroughTheirGridRowsUnchanged()
    {
        var doc = Factory();
        var rows = doc.GetFeatureRequirements();
        var signs = rows.Single(r => r.Name == "Signs");
        Assert.Equal(3, signs.Exceptions!.Count);

        var grid = EditableFeatureException.FromList(signs.Exceptions);
        Assert.Equal("Address", grid[0].SignCategory);
        Assert.Equal("PointStyle", grid[0].Symbol);
        Assert.Equal("No", grid[0].Rotation);
        Assert.Equal(EditableFeatureException.Inherit, grid[0].Label);
        Assert.Equal(EditableFeatureException.Inherit, grid[0].Review);
        Assert.Equal(string.Empty, grid[0].When);
        Assert.Equal("Address / house number sign; Private sign; Other descriptive sign", EditableFeatureException.Summary(grid));

        var problems = new List<string>();
        foreach (var r in rows) r.Exceptions = EditableFeatureException.ToExceptions(EditableFeatureException.FromList(r.Exceptions), "Row", problems);
        Assert.Empty(problems);
        Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(
            Newtonsoft.Json.Linq.JArray.FromObject(FeatureStandard.Pmx().Requirements.Single(r => r.Name == "Signs").Exceptions!),
            Newtonsoft.Json.Linq.JArray.FromObject(rows.Single(r => r.Name == "Signs").Exceptions!)));
        Assert.All(rows.Where(r => r.Name != "Signs"), r => Assert.Null(r.Exceptions));

        doc.SetFeatureStandard(rows, doc.GetSignCategories());
        Assert.True(doc.FeatureStandardIsBuiltIn());                // nothing changed: nothing written
    }

    [Fact]
    public void AnExceptionAddedInTheGridIsSavedInTheSameShapeAndApplies()
    {
        var doc = Factory();
        var rows = doc.GetFeatureRequirements();
        var signs = rows.Single(r => r.Name == "Signs");
        var grid = EditableFeatureException.FromList(signs.Exceptions);
        grid[1].Review = "Yes";
        grid.Add(new EditableFeatureException
        {
            Name = " Memorial sign ", SignCategory = "", When = @"\bMEMORIAL\b", Classification = EditableFeatureException.Inherit,
            Symbol = "PointStyle", Label = "None", Rotation = "No", Review = "", Note = "A roadside memorial: not a traffic sign."
        });
        grid.Add(new EditableFeatureException { Classification = EditableFeatureException.Inherit });     // a blank row: left out
        var problems = new List<string>();
        signs.Exceptions = EditableFeatureException.ToExceptions(grid, "Row 5 (Signs)", problems);
        Assert.Empty(problems);
        doc.SetFeatureStandard(rows, doc.GetSignCategories());

        var written = (Newtonsoft.Json.Linq.JArray)Newtonsoft.Json.Linq.JObject.Parse(doc.Render())["featureStandard"]!["requirements"]![0]!["exceptions"]!;
        Assert.Equal(4, written.Count);
        var memorial = (Newtonsoft.Json.Linq.JObject)written[3];
        Assert.Equal(new[] { "label", "name", "note", "rotation", "symbol", "when" }, memorial.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal("Memorial sign", (string?)memorial["name"]);
        Assert.Equal("PointStyle", (string?)memorial["symbol"]);
        Assert.False((bool)memorial["rotation"]!);
        Assert.True((bool)written[1]["review"]!);
        Assert.Equal("Private", (string?)written[1]["signCategory"]);

        var cfg = doc.CompileCandidate();
        var memorialSign = Resolve(cfg, "SN MEMORIAL");
        Assert.Equal(LabelRequirement.None, memorialSign.Label);
        Assert.False(memorialSign.Rotation);
        Assert.Contains("Memorial sign", memorialSign.Exceptions);
        Assert.True(Resolve(cfg, "SN PRIVATE DRIVE").Review);

        // Read back into the grid: the same rows, as typed (trimmed).
        var again = EditableFeatureException.FromList(doc.GetFeatureRequirements().Single(r => r.Name == "Signs").Exceptions);
        Assert.Equal("Memorial sign", again[3].Name);
        Assert.Equal(@"\bMEMORIAL\b", again[3].When);
        Assert.Equal("Yes", again[1].Review);
    }

    [Fact]
    public void EachExceptionMistakeIsNamedByItsRow()
    {
        var grid = new List<EditableFeatureException>
        {
            new() { Name = "No condition", Symbol = "PointStyle" },
            new() { When = "(unclosed" },
            new() { Name = "Stop", SignCategory = "Stop" },
            new() { SignCategory = "Private", Classification = "Tree", Disposition = "Gone", Symbol = "Big", Label = "Loud" },
            new() { SignCategory = "Address", Surface = "Maybe", Rotation = "1", Review = "yes" },
            new() { Name = "Fine", SignCategory = "warning", Rotation = "no" }
        };
        var problems = new List<string>();
        var read = EditableFeatureException.ToExceptions(grid, "Row 5 (Signs)", problems);

        Assert.Contains("Row 5 (Signs), exception 1 (No condition): needs a sign category or a description pattern (when), to say which points it is for.", problems);
        Assert.Contains(problems, p => p.StartsWith("Row 5 (Signs), exception 2: the description pattern '(unclosed' is not a valid regular expression"));
        Assert.Contains(problems, p => p.StartsWith("Row 5 (Signs), exception 3 (Stop): sign category 'Stop' is not one of None, Regulatory, Warning"));
        Assert.DoesNotContain(problems, p => p.StartsWith("Row 5 (Signs), exception 3 (Stop): needs"));     // one problem, the real one
        Assert.Contains(problems, p => p.StartsWith("Row 5 (Signs), exception 4: class 'Tree' is not one of Feature, Surface"));
        Assert.Contains(problems, p => p.StartsWith("Row 5 (Signs), exception 4: disposition 'Gone' is not one of"));
        Assert.Contains(problems, p => p.StartsWith("Row 5 (Signs), exception 4: symbol 'Big' is not one of None, PointStyle, Specific"));
        Assert.Contains(problems, p => p.StartsWith("Row 5 (Signs), exception 4: label 'Loud' is not one of AsPlanned, None, Required, PointLabel"));
        Assert.Contains("Row 5 (Signs), exception 5: surface must be Yes, No or (inherit) (not 'Maybe').", problems);
        Assert.Contains("Row 5 (Signs), exception 5: rotate must be Yes, No or (inherit) (not '1').", problems);
        Assert.Equal(9, problems.Count);

        var fine = Assert.Single(read!);
        Assert.Equal(SignCategory.Warning, fine.SignCategory);
        Assert.False(fine.Rotation);
        Assert.Null(EditableFeatureException.ToExceptions(new List<EditableFeatureException> { new() }, "Row 1", problems));
    }
}
