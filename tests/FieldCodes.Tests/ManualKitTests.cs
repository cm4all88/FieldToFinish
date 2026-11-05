using FieldCodes;

namespace FieldCodes.Tests;

/// <summary>
/// The manual Civil 3D checks promise specific outcomes for a synthetic point file. These
/// tests make sure the kit really parses that way with the neutral TRACE rules, so a
/// surprise in Civil 3D is a Civil 3D finding and not a broken kit.
/// </summary>
public sealed class ManualKitTests
{
    private static string Kit(string name) => Path.Combine(AppContext.BaseDirectory, "manual", name);

    private static List<(string Number, string Description)> Points()
        => File.ReadAllLines(Kit("trace-sample-points.csv"))
               .Where(l => l.Trim().Length > 0)
               .Select(l => l.Split(','))
               .Select(f => (f[0], string.Join(",", f.Skip(4))))
               .ToList();

    private static FieldCodeParser Parser(string rules)
        => new FieldCodeParser(RulesConfig.Load(rules));

    [Fact]
    public void KitHasTwelvePoints() => Assert.Equal(12, Points().Count);

    [Fact]
    public void EveryPointHasAKnownOutcomeWithTheNeutralRules()
    {
        var parser = Parser(Path.Combine(AppContext.BaseDirectory, "default-rules.json"));
        var parsed = Points().ToDictionary(p => p.Number, p => parser.Parse(p.Number, p.Description));

        Assert.All(new[] { "1", "2", "3", "4", "5" }, n => { Assert.Equal("tree", parsed[n].RuleId); Assert.False(parsed[n].HasErrors); });
        Assert.Equal("POLE 1234", parsed["10"].LabelText);
        Assert.Equal("POLE 1235 (DEAD)", parsed["11"].LabelText);
        Assert.Equal("sign", parsed["20"].RuleId);
        Assert.Equal("sign", parsed["21"].RuleId);
        Assert.True(parsed["30"].Ignored);                 // GS: deliberately ignored
        Assert.False(parsed["31"].Recognized);             // XYZ 12: no rule -> unhandled report
        Assert.True(parsed["32"].NoContent);               // bare PP: nothing to draw
    }

    [Fact]
    public void ManualRulesOverrideCompilesAndIdentifiesTheFence()
    {
        var cfg = RulesConfig.Load(Kit("rules.json"));
        var fence = Assert.Single(cfg.LineFeatures);
        Assert.Equal("C-FENC", fence.Layer);
        Assert.Contains("FNC", cfg.LineworkCodes);
        Assert.Equal(RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "default-rules.json")).Codes.Count, cfg.Codes.Count);
    }
}
