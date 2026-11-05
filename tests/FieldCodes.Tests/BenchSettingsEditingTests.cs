using FieldCodes.Editing;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Tests;

/// <summary>
/// Settings > Benches: FTFBENCH's section of the rules file, read with the built-in values for
/// anything it leaves out, written only where it changed, checked field by field, previewed
/// exactly as the command labels a bench, and reaching the command's rule.
/// </summary>
public sealed class BenchSettingsEditingTests
{
    private static readonly string RulesPath = Path.Combine(AppContext.BaseDirectory, "rules.json");

    private static RuleDocument WithBench(string key, string json)
    {
        var root = JObject.Parse(File.ReadAllText(RulesPath));
        ((JObject)root["bench"]!)[key] = JToken.Parse(json);
        return RuleDocument.FromText(root.ToString());
    }

    [Fact]
    public void TheShippedRulesHoldTheBuiltInValues()
    {
        var b = RuleDocument.Load(RulesPath).GetBench();
        Assert.True(b.InFile);
        Assert.Equal(new[] { "BENCH", "BNCH", "BENCHL" }, b.Codes);
        Assert.Equal("V-SURF-LAND-E", b.Layer);
        Assert.Equal("V-SURF-LAND-TEXT-E", b.LabelLayer);
        Assert.Equal("BENCH {length:0.#}'x{depth:0.#}'", b.LabelFormat);
    }

    [Fact]
    public void AFileWithoutTheSectionShowsTheBuiltInValuesAndTheFirstSaveWritesThemIn()
    {
        var root = JObject.Parse(File.ReadAllText(RulesPath));
        root.Remove("bench");
        var doc = RuleDocument.FromText(root.ToString());
        var b = doc.GetBench();
        Assert.False(b.InFile);
        Assert.Equal(EditableBench.Defaults().LabelFormat, b.LabelFormat);

        doc.ApplyBench(b);
        var s = (JObject)JObject.Parse(doc.Render())["bench"]!;
        Assert.Equal("V-SURF-LAND-E", (string)s["layer"]!);
        Assert.Equal(3, ((JArray)s["codes"]!).Count);
        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void AnEditChangesOnlyWhatChangedAndTheCommandReadsExactlyThat()
    {
        var doc = RuleDocument.Load(RulesPath);
        var before = JObject.Parse(doc.Render());
        var b = doc.GetBench();
        b.Codes = EditableBench.SplitCodes("seat, Bench  seat");
        b.LabelFormat = "BENCH {length:0.#}'x{depth:0.#}'[x{height:0.#}']";
        doc.ApplyBench(b);

        var after = JObject.Parse(doc.Render());
        var s = (JObject)after["bench"]!;
        Assert.Equal(new[] { "SEAT", "BENCH" }, ((JArray)s["codes"]!).Select(t => (string)t!));
        Assert.Equal("V-SURF-LAND-E", (string)s["layer"]!);
        foreach (var p in before.Properties().Where(p => p.Name != "bench"))
            Assert.True(JToken.DeepEquals(p.Value, after[p.Name]), p.Name + " changed");
        Assert.Empty(doc.Validate());

        // The command's rule has exactly the codes the page wrote: the built-in ones are not
        // added back in when the file is read.
        var rule = doc.CompileCandidate().Bench;
        Assert.Equal(new[] { "SEAT", "BENCH" }, rule.Codes);
        Assert.Equal(b.LabelFormat, rule.LabelFormat);
    }

    [Fact]
    public void NothingChangedWritesNothing()
    {
        var doc = RuleDocument.Load(RulesPath);
        var text = doc.Render();
        doc.ApplyBench(doc.GetBench());
        Assert.Equal(text, doc.Render());
    }

    [Theory]
    [InlineData("layer", "\"\"", "the outline layer is required")]
    [InlineData("layer", "\"V-SURF:LAND\"", "the outline layer \"V-SURF:LAND\" cannot be used")]
    [InlineData("labelLayer", "\"V-SURF<TEXT\"", "the label layer \"V-SURF<TEXT\" cannot be used")]
    [InlineData("labelLayer", "12", "labelLayer must be text")]
    [InlineData("labelFormat", "\" \"", "the label wording is required")]
    [InlineData("labelFormat", "\"BENCH {width}x{depth}\"", "uses {width}")]
    [InlineData("labelFormat", "\"BENCH {length\"", "a { without its }")]
    [InlineData("codes", "\"BENCH\"", "the codes must be a list")]
    [InlineData("codes", "[\"BENCH\", \"\"]", "a bench code is blank")]
    [InlineData("codes", "[\"BENCH END\"]", "is not one code")]
    [InlineData("codes", "[\"BENCH\", \"bench\"]", "listed twice")]
    public void EachMistakeIsNamedNotSaved(string key, string json, string expected)
    {
        var problems = WithBench(key, json).Validate();
        Assert.Contains(problems, p => p.StartsWith("Benches: ") && p.Contains(expected));
    }

    [Theory]
    [InlineData("BENCH {length:0.#}'x{depth:0.#}'", "BENCH 6'x1.5'")]
    [InlineData("BENCH {length:0.#}'x{depth:0.#}'[x{height:0.#}']", "BENCH 6'x1.5'x1.5'")]
    [InlineData("BENCH\\P{length:0.#}'x{depth:0.#}'", "BENCH / 6'x1.5'")]
    public void ThePreviewIsWhatFtfBenchWrites(string format, string expected)
    {
        Assert.Equal(expected, FieldCodes.Drafting.BenchPlanner.Preview(format));
    }

    [Fact]
    public void CodesTypedInOneBoxAreSplitCleanedAndKeptOnce()
    {
        Assert.Equal(new[] { "BENCH", "BNCH", "BENCHL" }, EditableBench.SplitCodes(" bench, BNCH;  benchl\nBench "));
        Assert.Empty(EditableBench.SplitCodes("  , ; "));
    }

    [Fact]
    public void SavingPutsThisPagesSettingsOnTheFileAsItIsNow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ftf-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "rules.json");
            File.Copy(RulesPath, path);

            var page = RuleDocument.Load(path);
            var b = page.GetBench();
            b.LabelLayer = "V-SURF-LAND-TEXT-B";
            page.ApplyBench(b);

            // Meanwhile the Wetland Buffers page saves.
            var other = RuleDocument.Load(path);
            var w = other.GetWetlandBuffer();
            w.Distance = 100;
            other.ApplyWetlandBuffer(w);
            other.Save(path);

            var fresh = RuleDocument.Load(path);
            page.CopyBenchTo(fresh);
            Assert.Empty(fresh.Validate());
            fresh.Save(path);

            var cfg = RulesConfig.Load(path);
            Assert.Equal("V-SURF-LAND-TEXT-B", cfg.Bench.LabelLayer);
            Assert.Equal(100, cfg.WetlandBuffer.Distance);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
