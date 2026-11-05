using FieldCodes;

namespace FieldCodes.Tests;

/// <summary>
/// The neutral TRACE configuration (config/default/rules.json) is what a firm with no
/// standards of its own gets. These tests pin that it loads, parses its example
/// features, and stays free of any one company's standards.
/// </summary>
public sealed class DefaultProfileTests
{
    private static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "default-rules.json");

    private static FieldCodeParser Parser() => new FieldCodeParser(RulesConfig.Load(DefaultPath));

    [Fact]
    public void LoadsAndCompiles()
        => Assert.NotEmpty(RulesConfig.Load(DefaultPath).Codes);

    [Fact]
    public void ContainsNoCompanySpecificStandards()
    {
        var text = File.ReadAllText(DefaultPath);
        foreach (var banned in new[] { "PMX", "Parametrix", "fdb_xdef", "f2f_xdef", @"U:\", @"C:\PMX" })
            Assert.DoesNotContain(banned, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FirmSpecificTablesStartEmpty()
    {
        var cfg = RulesConfig.Load(DefaultPath);
        Assert.Empty(cfg.LineworkCodes);
        Assert.Empty(cfg.LineFeatures);
        Assert.Empty(cfg.ControlCodes);
        Assert.Empty(cfg.SymbolLabels);
        Assert.Empty(cfg.ProtectedLayers);
    }

    [Theory]
    [InlineData("CON 18 . 25", "tree")]
    [InlineData("DEC 12 12 . 14", "tree")]
    [InlineData("PP 1234", "pole")]
    [InlineData("SIGN STOP 135", "sign")]
    public void ExampleFeaturesParse(string description, string rule)
    {
        var p = Parser().Parse("1", description);
        Assert.False(p.HasErrors);
        Assert.Equal(rule, p.RuleId);
    }

    [Fact]
    public void ModifiersStillCompose()
    {
        var p = Parser().Parse("1", "PP 1234 DEAD");
        Assert.False(p.HasErrors);
        Assert.Equal("POLE 1234 (DEAD)", p.LabelText);
    }

    [Fact]
    public void UnknownSpeciesIsReportedNotGuessed()
    {
        // The strict-parser stance survives in the neutral configuration.
        var p = Parser().Parse("1", "XYZ 18 . 25");
        Assert.False(p.Recognized && p.RuleId == "tree");
    }

    [Fact]
    public void NoRuleInsertsABlock()
    {
        foreach (var code in RulesConfig.Load(DefaultPath).Codes)
            Assert.False(code.InsertBlock, "rule '" + code.Id + "' inserts a block");
    }
}
