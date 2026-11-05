using FieldCodes;
using FieldCodes.Editing;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Tests;

public sealed class StandardsImportTests
{
    private static string DefaultJson()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "default-rules.json"));

    private static RulesConfig Compile(ImportReport r)
        => RuleDocument.FromText(r.NewJson).CompileCandidate();

    [Fact]
    public void AddsLineFeaturesAndTheyCompile()
    {
        var csv = "code,name,layer,label,placement,aliases\n" +
                  "FNC,Fence,C-FENC,CHAIN LINK FENCE,Left,Chain Link|Fence Line\n" +
                  "EOP,Edge of Pavement,C-ROAD-EDGE,EDGE OF PAVEMENT,,\n";

        var report = StandardsImport.Apply(DefaultJson(), StandardsTable.LineFeatures, csv);

        Assert.Equal(2, report.Count("Added"));
        var cfg = Compile(report);
        var fnc = cfg.LineFeatures.Single(f => f.Code == "FNC");
        Assert.Equal("C-FENC", fnc.Layer);
        Assert.Equal("Left", fnc.Placement);
        Assert.Equal(new[] { "Chain Link", "Fence Line" }, fnc.Aliases);
        Assert.Equal(2, cfg.LineFeatures.Count);
    }

    [Fact]
    public void EveryRowIsAccountedFor()
    {
        var csv = "code,layer\n" +
                  "FNC,C-FENC\n" +
                  ",C-NONE\n" +          // blank code
                  "fnc,C-OTHER\n" +      // duplicate in file (case-insensitive)
                  "WALL,C-WALL\n";

        var report = StandardsImport.Apply(DefaultJson(), StandardsTable.SymbolLabels, csv);

        Assert.Equal(4, report.Rows.Count);
        Assert.Equal(2, report.Count("Added"));
        Assert.Equal(2, report.Count("Rejected"));
        Assert.All(report.Rows, r => Assert.False(string.IsNullOrEmpty(r.Disposition)));
        Assert.Contains("Duplicate", report.Rows.Single(r => r.Line == 4).Reason);
        Assert.Contains("blank", report.Rows.Single(r => r.Line == 3).Reason);
        Assert.Contains("4 row(s)", report.Summary());
    }

    [Fact]
    public void UpdateKeepsColumnsTheCsvDoesNotSupply()
    {
        var first = StandardsImport.Apply(DefaultJson(), StandardsTable.LineFeatures,
            "code,name,layer,label\nFNC,Fence,C-FENC,FENCE\n");
        var second = StandardsImport.Apply(first.NewJson, StandardsTable.LineFeatures,
            "code,layer\nFNC,C-FENC-NEW\n");

        Assert.Equal(1, second.Count("Updated"));
        var f = Compile(second).LineFeatures.Single();
        Assert.Equal("C-FENC-NEW", f.Layer);
        Assert.Equal("FENCE", f.Label);
        Assert.Equal("Fence", f.Name);
    }

    [Fact]
    public void ReimportingTheSameFileChangesNothing()
    {
        var csv = "code,name,layer\nFNC,Fence,C-FENC\n";
        var first = StandardsImport.Apply(DefaultJson(), StandardsTable.LineFeatures, csv);
        var again = StandardsImport.Apply(first.NewJson, StandardsTable.LineFeatures, csv);

        Assert.Equal(1, again.Count("Unchanged"));
        Assert.Equal(first.NewJson, again.NewJson);
    }

    [Fact]
    public void InvalidPlacementIsRejectedWithReason()
    {
        var report = StandardsImport.Apply(DefaultJson(), StandardsTable.LineFeatures,
            "code,placement\nFNC,Sideways\n");

        var row = Assert.Single(report.Rows);
        Assert.Equal("Rejected", row.Disposition);
        Assert.Contains("Sideways", row.Reason);
        Assert.Empty(Compile(report).LineFeatures);
    }

    [Fact]
    public void CodeListsAddOnlyMissingCodes()
    {
        var report = StandardsImport.Apply(DefaultJson(), StandardsTable.LineworkCodes,
            "code\nFNC\nWALL\nfnc\n");

        Assert.Equal(2, report.Count("Added"));
        Assert.Equal(1, report.Count("Rejected"));

        var again = StandardsImport.Apply(report.NewJson, StandardsTable.LineworkCodes, "code\nWALL\n");
        Assert.Equal(1, again.Count("Unchanged"));
    }

    [Fact]
    public void QuotedFieldsAndBomAreHandled()
    {
        var csv = "\uFEFFcode,name,layer\r\n\"FNC\",\"Fence, chain link\",\"C-FENC\"\r\n";
        var report = StandardsImport.Apply(DefaultJson(), StandardsTable.LineFeatures, csv);
        Assert.Equal("Fence, chain link", Compile(report).LineFeatures.Single().Name);
    }

    [Fact]
    public void OtherPartsOfTheRulesAreUntouched()
    {
        var before = JObject.Parse(DefaultJson());
        var after = JObject.Parse(StandardsImport.Apply(DefaultJson(), StandardsTable.IgnoreCodes,
            "code\nTEMP\n").NewJson);

        Assert.True(JToken.DeepEquals(before["codes"], after["codes"]));
        Assert.True(JToken.DeepEquals(before["modifiers"], after["modifiers"]));
        Assert.Equal(before["ignoreCodes"].Count() + 1, after["ignoreCodes"].Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("name,layer\nFence,C-FENC\n")]
    public void MissingCodeColumnIsRefused(string csv)
        => Assert.Throws<ConfigException>(() =>
            StandardsImport.Apply(DefaultJson(), StandardsTable.LineFeatures, csv));

    [Fact]
    public void UnknownColumnsAreRefusedNotIgnored()
    {
        var ex = Assert.Throws<ConfigException>(() =>
            StandardsImport.Apply(DefaultJson(), StandardsTable.SymbolLabels, "code,layer,color\nCB,X,red\n"));
        Assert.Contains("color", ex.Message);
    }
}
