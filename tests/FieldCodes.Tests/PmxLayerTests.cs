using System.Text.RegularExpressions;
using FieldCodes;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// FTF draws to the PMX layer template (a completed PMX Civil 3D drawing: 588 layers,
/// V-&lt;GROUP&gt;-&lt;FEATURE&gt;-E with -PNTS-E / -SYMB-E / -TEXT-E companions). Earlier builds
/// labelled on names that template does not have; files saved by those builds are upgraded
/// as they load, and only the exact retired names are touched.
/// </summary>
public sealed class PmxLayerTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public PmxLayerTests(RulesFixture fx) => _fx = fx;

    /// <summary>Text, symbol, contour, vegetation and land layers present in the PMX
    /// template drawing, and the patterns FTF uses for it.</summary>
    private static readonly HashSet<string> PmxTemplate = new(StringComparer.OrdinalIgnoreCase)
    {
        "V-UTIL-POWR-TEXT-E", "V-UTIL-WATR-TEXT-E", "V-UTIL-NGAS-TEXT-E", "V-UTIL-STRM-TEXT-E",
        "V-UTIL-STRC-TEXT-E", "V-UTIL-LITG-TEXT-E", "V-CTRL-TEXT-E", "V-SURF-SIGN-TEXT-E",
        "V-SURF-MBOX-TEXT-E", "V-SURF-POST-TEXT-E", "V-SURF-VEGE-TEXT-E", "V-SURF-VEGE-E",
        "V-SURF-LAND-E", "V-SURF-LAND-TEXT-E", "V-TEXT-E", "V-TEXT-TABL",
        "V-TOPO-CONT-E", "V-TOPO-CONT-TEXT-E", "V-TOPO-SPOT-PNTS-E", "V-TOPO-SPOT-TEXT-E"
    };

    [Fact]
    public void EveryShippedLabelLayerIsInThePmxTemplate()
    {
        var cfg = _fx.Config;
        foreach (var rule in cfg.Codes.Where(c => c.Label?.Layer != null))
            Assert.True(PmxTemplate.Contains(rule.Label.Layer), rule.Id + " labels on " + rule.Label.Layer);
        foreach (var symbol in cfg.SymbolLabels)
            Assert.True(PmxTemplate.Contains(symbol.Layer), symbol.Code + " labels on " + symbol.Layer);
        foreach (var rule in cfg.Codes.Where(c => c.DripLine?.Layer != null))
            Assert.True(PmxTemplate.Contains(rule.DripLine.Layer), rule.Id + " drip on " + rule.DripLine.Layer);
        Assert.Contains(cfg.Bench.Layer, PmxTemplate);
        Assert.Contains(cfg.Bench.LabelLayer, PmxTemplate);
    }

    [Fact]
    public void TheShippedRulesFileItselfIsAlreadyPmx()
    {
        // The upgrade on load is for offices' saved copies, not a crutch for the shipped file.
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        foreach (var retired in new[] { "\"V-UTIL-POWR-TEXT\"", "\"V-SIGN-TEXT\"", "\"V-SITE-TEXT\"", "\"V-TREE-TEXT\"",
                                        "\"V-TREE-DRIP\"", "\"V-CTRL-TEXT\"", "\"V-SITE-FURN-E\"", "\"C-STRM-STRC\"", "\"V-STRC-*\"" })
            Assert.DoesNotContain(retired, text);
    }

    [Fact]
    public void DefaultSettingsAreThePmxLayers()
    {
        var s = new FtfSettings();
        Assert.Equal(PmxLayers.Protected, s.DrawOrder.ProtectedLayers);
        Assert.Equal(PmxLayers.Maskable, s.DrawOrder.MaskableLayers);
        Assert.Equal("V-TEXT-E", s.LineLabels.DefaultLabelLayer);
        Assert.Equal("", s.Tags.TagLayer);            // each tag on its feature's own text layer
        Assert.Equal("V-TEXT-TABL", s.Tags.TableLayer);
    }

    [Theory]
    [InlineData("V-UTIL-POWR-TEXT", "pole", "V-UTIL-POWR-TEXT-E")]
    [InlineData("V-UTIL-POWR-TEXT", "light-pole", "V-UTIL-LITG-TEXT-E")]
    [InlineData("V-UTIL-POWR-TEXT", "LT", "V-UTIL-LITG-TEXT-E")]
    [InlineData("V-SITE-TEXT", "mailbox", "V-SURF-MBOX-TEXT-E")]
    [InlineData("V-SITE-TEXT", "MB", "V-SURF-MBOX-TEXT-E")]
    [InlineData("V-SITE-TEXT", "bollard", "V-SURF-POST-TEXT-E")]
    [InlineData("V-SITE-TEXT", "bench", "V-SURF-LAND-TEXT-E")]
    [InlineData("V-SIGN-TEXT", "sn-sign", "V-SURF-SIGN-TEXT-E")]
    [InlineData("V-TREE-DRIP", "tree", "V-SURF-VEGE-E")]
    [InlineData("v-ctrl-text", "control", "V-CTRL-TEXT-E")]
    [InlineData("V-UTIL-POWR-TEXT-E", "pole", "V-UTIL-POWR-TEXT-E")]   // already PMX
    [InlineData("OFFICE-POLE-TEXT", "pole", "OFFICE-POLE-TEXT")]       // an office's own choice
    public void RetiredNamesMoveToPmxAndNothingElseDoes(string layer, string feature, string expected)
    {
        Assert.Equal(expected, PmxLayers.Upgrade(layer, feature));
    }

    [Fact]
    public void ARulesFileSavedByAnEarlierBuildLoadsOnPmxLayers()
    {
        // The shipped rules as they were, with one office edit to keep.
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        text = Regex.Replace(text, "\"V-UTIL-POWR-TEXT-E\"", "\"V-UTIL-POWR-TEXT\"");
        text = Regex.Replace(text, "\"V-SURF-SIGN-TEXT-E\"", "\"V-SIGN-TEXT\"");
        text = Regex.Replace(text, "\"V-SURF-(MBOX|POST)-TEXT-E\"", "\"V-SITE-TEXT\"");
        text = Regex.Replace(text, "\"V-UTIL-LITG-TEXT-E\"", "\"V-UTIL-POWR-TEXT\"");
        text = Regex.Replace(text, "\"protectedLayers\": \\[[^\\]]*\\]", "\"protectedLayers\": [ \"V-SIGN\", \"V-UTIL-*\", \"V-STRC-*\", \"C-STRM-STRC\" ]");
        text = Regex.Replace(text, "\"maskableLayers\":\\s*\\[[^\\]]*\\]", "\"maskableLayers\": [ \"V-TREE-DRIP\", \"C-TOPO-*\", \"V-ROAD-*\" ]");
        text = text.Replace("\"V-UTIL-WATR-TEXT-E\"", "\"OFFICE-WATER\"");
        var path = Path.Combine(Path.GetTempPath(), "ftf-pmx-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, text);
        try
        {
            var cfg = RulesConfig.Load(path);
            Assert.Equal("V-UTIL-POWR-TEXT-E", cfg.Codes.Single(c => c.Id == "pole").Label.Layer);
            Assert.Equal("V-UTIL-LITG-TEXT-E", cfg.Codes.Single(c => c.Id == "light-pole").Label.Layer);
            Assert.Equal("V-SURF-MBOX-TEXT-E", cfg.Codes.Single(c => c.Id == "mailbox").Label.Layer);
            Assert.Equal("V-SURF-POST-TEXT-E", cfg.Codes.Single(c => c.Id == "bollard").Label.Layer);
            Assert.Equal("V-SURF-SIGN-TEXT-E", cfg.SymbolLabels.Single(s => s.Code == "SN").Layer);
            Assert.Equal("V-UTIL-LITG-TEXT-E", cfg.SymbolLabels.Single(s => s.Code == "LT").Layer);
            Assert.Equal("V-SURF-MBOX-TEXT-E", cfg.SymbolLabels.Single(s => s.Code == "MB").Layer);
            Assert.Equal(PmxLayers.Protected, cfg.ProtectedLayers);
            Assert.Equal(PmxLayers.Maskable, cfg.MaskableLayers);
            Assert.Equal("OFFICE-WATER", cfg.Codes.Single(c => c.Id == "hydrant").Label.Layer);   // kept
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ASettingsFileSavedByAnEarlierBuildLoadsOnPmxLayers()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ftf-pmx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FtfSettings.FileName);
        File.WriteAllText(path, "{ \"version\": \"1\"," +
            " \"drawOrder\": { \"protectedLayers\": [\"V-UTIL-*\", \"V-SIGN\", \"V-STRC-*\", \"C-STRM-STRC\"]," +
            "                  \"maskableLayers\": [\"V-TREE-DRIP\", \"C-TOPO-*\", \"V-ROAD-*\", \"MY-CONTOURS\"] }," +
            " \"lineLabels\": { \"defaultLabelLayer\": \"V-LINE-TEXT\" }," +
            " \"tags\": { \"tagLayer\": \"V-TREE-TAG\", \"tableLayer\": \"V-TREE-TABL\" } }");
        try
        {
            var s = FtfSettings.Load(path);
            Assert.Equal(PmxLayers.Protected, s.DrawOrder.ProtectedLayers);           // the old default, in any order
            Assert.Equal(new[] { "V-TREE-DRIP", "C-TOPO-*", "V-ROAD-*", "MY-CONTOURS" },
                         s.DrawOrder.MaskableLayers);                                // edited by the office: kept
            Assert.Equal("V-TEXT-E", s.LineLabels.DefaultLabelLayer);
            Assert.Equal("", s.Tags.TagLayer);
            Assert.Equal("V-TEXT-TABL", s.Tags.TableLayer);
        }
        finally { Directory.Delete(dir, true); }
    }
}
