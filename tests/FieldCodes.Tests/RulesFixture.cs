using System.Globalization;
using System.Linq;
using FieldCodes;

namespace FieldCodes.Tests;

/// <summary>
/// Loads the real config/rules.json once per test class. Tests run against the
/// shipped grammar on purpose -- if a rule changes, these tests should notice.
/// </summary>
public sealed class RulesFixture
{
    public RulesConfig Config { get; }
    public FieldCodeParser Parser { get; }

    public RulesFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules.json");
        Config = RulesConfig.Load(path);
        Parser = new FieldCodeParser(Config);
    }

    public ParsedPoint Parse(string description, string pointNumber = "1")
        => Parser.Parse(pointNumber, description);
}

public static class Canon
{
    private static string N(double? d)
        => d.HasValue ? d.Value.ToString("R", CultureInfo.InvariantCulture) : "<null>";

    /// <summary>
    /// Every drawing instruction the CAD layer consumes, plus diagnostics, rendered
    /// in a stable form. Two descriptions that differ only in token order must
    /// produce byte-identical output here -- including diagnostic order.
    /// </summary>
    public static string Of(ParsedPoint p) => string.Join("\n", new[]
    {
        "recognized=" + p.Recognized,
        "rule="       + p.RuleId,
        "code="       + p.Code,
        "species="    + p.Species,
        "trunk="      + N(p.TrunkInches),
        "stems="      + string.Join(",", p.Stems.Select(s => s.ToString("R", CultureInfo.InvariantCulture))),
        "stemCount="  + p.StemCount.ToString(CultureInfo.InvariantCulture),
        "drip="       + N(p.DripRadius),
        "rotation="   + N(p.RotationDegrees),
        "block="      + p.BlockName,
        "blockLayer=" + p.BlockLayer,
        "blockScale=" + p.BlockScale.ToString("R", CultureInfo.InvariantCulture),
        "dripLayer="  + p.DripLayer,
        "dripUnify="  + p.DripUnify,
        "label="      + p.LabelText,
        "labelLayer=" + p.LabelLayer,
        "leader="     + p.Leader,
        "tagPrefix="  + p.TagPrefix,
        "modifiers="  + string.Join(",", p.Modifiers.Select(m => m.Id)),
        "diagnostics=" + string.Join(" | ", p.Diagnostics.Select(d => d.ToString())),
    });
}
