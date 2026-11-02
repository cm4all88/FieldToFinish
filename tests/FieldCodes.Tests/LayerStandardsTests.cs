using FieldCodes;
using FieldCodes.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Tests;

/// <summary>Settings > Layers, the properties half: which standard a layer gets, what is a valid
/// value, and that the section survives a settings file round trip.</summary>
public sealed class LayerStandardsTests
{
    private static LayerStandard Std(string name, string? color = null, string? linetype = null, string? weight = null,
                                     bool? plot = null, int? transparency = null, string? description = null)
        => new() { Name = name, Color = color, Linetype = linetype, LineWeight = weight, Plot = plot, Transparency = transparency, Description = description };

    // ------------------------------------------------------------ matching

    [Fact]
    public void TheLayersOwnStandardBeatsAPatternPropertyByProperty()
    {
        var standards = new[]
        {
            Std("*-TEXT-E", color: "3", weight: "0.25"),
            Std("V-CTRL-TEXT-E", color: "1")
        };
        var r = LayerStandards.Resolve(standards, "V-CTRL-TEXT-E");
        Assert.Equal("1", r.Values.Color);
        Assert.Equal("0.25", r.Values.LineWeight);
        Assert.Equal("V-CTRL-TEXT-E", r.Source(LayerProperty.Color));
        Assert.Equal("*-TEXT-E", r.Source(LayerProperty.LineWeight));
        Assert.False(r.FromPattern(LayerProperty.Color));
        Assert.True(r.FromPattern(LayerProperty.LineWeight));
        Assert.Null(r.Source(LayerProperty.Plot));
        Assert.Equal("V-CTRL-TEXT-E", LayerStandards.Find(standards, "v-ctrl-text-e")!.Name);
    }

    [Fact]
    public void AMoreSpecificPatternBeatsALooserOne()
    {
        var standards = new[] { Std("*-TEXT-E", color: "3"), Std("V-UTIL-*-TEXT-E", color: "5"), Std("V-????-*", plot: false) };
        Assert.Equal("5", LayerStandards.Resolve(standards, "V-UTIL-WATR-TEXT-E").Values.Color);
        Assert.Equal("3", LayerStandards.Resolve(standards, "V-SURF-VEGE-TEXT-E").Values.Color);
        Assert.False(LayerStandards.Resolve(standards, "V-ESMT-E").Values.Plot);
        Assert.True(LayerStandards.Resolve(standards, "C-ROAD").IsEmpty);
    }

    [Fact]
    public void NamesMatchWithoutRegardToCaseAndPatternsUseStarAndQuestionMark()
    {
        Assert.True(LayerStandards.Matches("V-SURF-VEGE-TEXT-E", "v-surf-vege-text-e"));
        Assert.True(LayerStandards.Matches("FTF-QC-*", "FTF-QC-ORIENT-INFERRED"));
        Assert.True(LayerStandards.Matches("V-ESMT-?-E", "V-ESMT-X-E"));
        Assert.False(LayerStandards.Matches("V-ESMT-E", "V-ESMT-E-DEAD"));
        Assert.False(LayerStandards.Matches("V.ESMT", "V-ESMT"));        // a dot is a dot, not "any character"
        Assert.True(LayerStandards.IsPattern("V-*"));
        Assert.False(LayerStandards.IsPattern("V-ESMT-E"));
    }

    // ------------------------------------------------------------ values

    [Theory]
    [InlineData("3", (short)3)]
    [InlineData("green", (short)3)]
    [InlineData(" 250 ", (short)250)]
    [InlineData("255", (short)255)]
    public void ALayerColourIsAnAciNumberOrName(string text, short aci)
    {
        Assert.True(LayerStandards.TryParseColor(text, out var colour, out _));
        Assert.Equal(aci, colour.Aci);
    }

    [Fact]
    public void ATrueColourIsThreeNumbers()
    {
        Assert.True(LayerStandards.TryParseColor("255,128,0", out var c, out _));
        Assert.True(c.IsTrueColor);
        Assert.Equal("255,128,0", c.ToString());
        Assert.True(LayerStandards.TryParseColor("RGB:1, 2, 3", out var d, out _));
        Assert.Equal((byte)2, d.Green);
        Assert.Equal(LayerColor.FromRgb(1, 2, 3), d);
        Assert.NotEqual(LayerColor.FromAci(1), LayerColor.FromRgb(255, 0, 0));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("256")]
    [InlineData("ByLayer")]
    [InlineData("byblock")]
    [InlineData("purple")]
    [InlineData("300")]
    [InlineData("1,2")]
    [InlineData("1,2,999")]
    [InlineData("")]
    public void ByLayerAndNonsenseAreNotLayerColours(string text)
    {
        Assert.False(LayerStandards.TryParseColor(text, out _, out var why));
        Assert.False(string.IsNullOrWhiteSpace(why));
    }

    [Theory]
    [InlineData("0.25", 25)]
    [InlineData("25", 25)]
    [InlineData("Default", -3)]
    [InlineData("2.11", 211)]
    [InlineData("0", 0)]
    [InlineData("0.09", 9)]
    [InlineData("1.06", 106)]
    public void ALineweightIsOneOfAutoCadsOwn(string text, int hundredths)
    {
        Assert.True(LayerStandards.TryParseLineWeight(text, out var w, out _));
        Assert.Equal(hundredths, w);
    }

    [Theory]
    [InlineData("0.33")]
    [InlineData("ByLayer")]
    [InlineData("ByBlock")]
    [InlineData("thick")]
    [InlineData("3.5")]
    public void AnythingElseIsNotALayerLineweight(string text)
    {
        Assert.False(LayerStandards.TryParseLineWeight(text, out _, out var why));
        Assert.Contains("lineweight", why);
    }

    [Fact]
    public void TheLineweightChoicesAreAutoCadsList()
    {
        var choices = LayerStandards.LineWeightChoices();
        Assert.Equal("Default", choices[0]);
        Assert.Contains("0.25", choices);
        Assert.Contains("2.11", choices);
        Assert.Equal(25, choices.Count);
        Assert.All(choices, c => Assert.True(LayerStandards.TryParseLineWeight(c, out _, out _)));
    }

    [Fact]
    public void AStandardSaysWhatIsWrongWithIt()
    {
        Assert.Empty(LayerStandards.Problems(Std("V-*-TEXT-E", color: "3", linetype: "HIDDEN2", weight: "0.35", plot: true, transparency: 50, description: "Text")));
        Assert.NotEmpty(LayerStandards.Problems(Std("", color: "3")));
        Assert.NotEmpty(LayerStandards.Problems(Std("A<B", color: "3")));
        Assert.NotEmpty(LayerStandards.Problems(Std("X", linetype: "   ")));              // set, but blank
        Assert.NotEmpty(LayerStandards.Problems(Std("X", linetype: "ByLayer")));
        Assert.NotEmpty(LayerStandards.Problems(Std("X", weight: "0.33")));
        Assert.NotEmpty(LayerStandards.Problems(Std("X", color: "ByLayer")));
        Assert.NotEmpty(LayerStandards.Problems(Std("X", transparency: 95)));
        Assert.Empty(LayerStandards.Problems(Std("X")));                                  // sets nothing: valid, just empty
    }

    [Fact]
    public void TheSectionRejectsInvalidAndDuplicateStandards()
    {
        var s = new FtfSettings();
        s.LayerStandards.Layers.Add(Std("V-TREE", weight: "0.33"));
        s.LayerStandards.Layers.Add(Std("V-CTRL", color: "1"));
        s.LayerStandards.Layers.Add(Std("v-ctrl", plot: false));
        var problems = new List<string>();
        s.Validate(problems);
        Assert.Contains(problems, p => p.StartsWith("Layers: standard for V-TREE") && p.Contains("lineweight"));
        Assert.Contains(problems, p => p.Contains("two standards for v-ctrl"));
        Assert.Throws<ConfigException>(() => s.Save(Path.Combine(Path.GetTempPath(), "ftf-" + Path.GetRandomFileName(), "x.json")));
    }

    [Fact]
    public void ExactOrNewRemoveAndRemoveEmptyKeepOneStandardPerLayer()
    {
        var s = new LayerStandardsSettings();
        var a = s.ExactOrNew("V-TREE");
        a.Color = "3";
        Assert.Same(a, s.ExactOrNew("v-tree"));
        s.Layers.Add(Std("V-*", plot: true));
        s.Layers.Add(Std("EMPTY"));
        s.RemoveEmpty();
        Assert.Equal(2, s.Layers.Count);
        Assert.True(s.RemoveExact("V-TREE"));
        Assert.False(s.RemoveExact("V-*-NOPE"));
        Assert.Single(s.Layers);
        Assert.True(s.Layers[0].IsPattern);
        var clone = s.Clone();
        clone.Layers[0].Plot = false;
        Assert.True(s.Layers[0].Plot);
    }

    [Fact]
    public void CopyFromDrawingCapturesEveryProperty()
    {
        var copy = LayerStandards.FromDrawing(new LayerProperties
        {
            Name = "V-TREE", Color = LayerColor.FromAci(3), Linetype = "Continuous", LineWeight = 35, Plot = false, Transparency = 20, Description = "Trees"
        })!;
        Assert.Equal("3", copy.Color);
        Assert.Equal("0.35", copy.LineWeight);
        Assert.False(copy.Plot);
        Assert.Equal(20, copy.Transparency);
        Assert.Empty(LayerStandards.Problems(copy));
    }

    // ------------------------------------------------------------ settings file

    [Fact]
    public void StandardsSurviveARoundTripAndUnsetPropertiesAreNotWritten()
    {
        var s = new FtfSettings();
        s.LayerStandards.ApplyToNewLayers = false;
        s.LayerStandards.Layers.Add(Std("V-SURF-VEGE-TEXT-E", color: "3", linetype: "Continuous", weight: "0.25", plot: true, description: "Tree labels"));
        s.LayerStandards.Layers.Add(Std("FTF-QC-*", plot: false, transparency: 50));

        var json = JsonConvert.SerializeObject(s, Formatting.Indented);
        var o = JObject.Parse(json);
        var section = (JObject)o["layerStandards"]!;
        Assert.False((bool)section["applyToNewLayers"]!);
        var qc = (JObject)section["layers"]![1]!;
        Assert.Equal("FTF-QC-*", (string)qc["name"]!);
        Assert.Null(qc["color"]);                 // unset: not written
        Assert.Equal(50, (int)qc["transparency"]!);

        var back = JsonConvert.DeserializeObject<FtfSettings>(json)!;
        back.FillMissingSections();
        Assert.False(back.LayerStandards.ApplyToNewLayers);
        Assert.Equal(2, back.LayerStandards.Layers.Count);   // replaced, not appended to defaults
        var tree = back.LayerStandards.Exact("V-SURF-VEGE-TEXT-E")!;
        Assert.Equal("3", tree.Color);
        Assert.Equal("0.25", tree.LineWeight);
        Assert.True(tree.Plot);
        Assert.Equal("Tree labels", tree.Description);
        Assert.Null(back.LayerStandards.Layers[1].Color);
        Assert.False(back.LayerStandards.Layers[1].Plot);
    }

    [Fact]
    public void AFileFromBeforeLayerStandardsLoadsWithNoneAndCreatesLayersAsBefore()
    {
        var o = JObject.Parse(JsonConvert.SerializeObject(new FtfSettings()));
        o.Remove("layerStandards");
        var dir = Path.Combine(Path.GetTempPath(), "ftf-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FtfSettings.FileName);
        File.WriteAllText(path, o.ToString());

        var loaded = FtfSettings.Load(path);
        Assert.NotNull(loaded.LayerStandards);
        Assert.Empty(loaded.LayerStandards.Layers);
        Assert.True(loaded.LayerStandards.ApplyToNewLayers);
        Assert.True(loaded.LayerStandards.For("V-SURF-VEGE-TEXT-E").IsEmpty);

        // A section with only a list keeps the default switch.
        File.WriteAllText(path, "{ \"layerStandards\": { \"layers\": [ { \"name\": \"V-TREE\", \"plot\": false } ] } }");
        var partial = FtfSettings.Load(path);
        Assert.True(partial.LayerStandards.ApplyToNewLayers);
        Assert.False(partial.LayerStandards.Exact("V-TREE")!.Plot);

        // And "layers": null is read as none.
        File.WriteAllText(path, "{ \"layerStandards\": { \"layers\": null } }");
        Assert.NotNull(FtfSettings.Load(path).LayerStandards.Layers);
    }

    [Fact]
    public void RestoreDefaultsClearsTheStandards()
    {
        var s = new FtfSettings();
        s.LayerStandards.Layers.Add(Std("X", color: "1"));
        s.LayerStandards.ApplyToNewLayers = false;
        s.RestoreDefaults();
        Assert.Empty(s.LayerStandards.Layers);
        Assert.True(s.LayerStandards.ApplyToNewLayers);
        Assert.Contains(s.Sections, sec => sec is LayerStandardsSettings);
    }
}

/// <summary>FTFLAYERUPDATE's preview: what would change, on which layers, and nothing else.</summary>
public sealed class LayerUpdatePlanTests
{
    private static LayerProperties Layer(string name, short aci = 7, string linetype = "Continuous", int weight = -3, bool plot = true,
                                         int transparency = 0, string description = "")
        => new() { Name = name, Color = LayerColor.FromAci(aci), Linetype = linetype, LineWeight = weight, Plot = plot, Transparency = transparency, Description = description };

    private static readonly List<LayerProperties> Drawing = new()
    {
        Layer("V-SURF-VEGE-TEXT-E"),
        Layer("FTF-QC-ORIENT", aci: 1),
        Layer("OTHER-LAYER"),
        Layer("XREF|V-SURF-VEGE-E"),
        Layer("V-SURF-VEGE-E", linetype: "CONTINUOUS", weight: 25)
    };

    private static readonly List<LayerStandard> Standards = new()
    {
        new() { Name = "V-SURF-VEGE-TEXT-E", Color = "3", LineWeight = "0.25", Linetype = "Continuous" },
        new() { Name = "FTF-QC-*", Plot = false },
        new() { Name = "OTHER-*", Color = "1" },
        new() { Name = "V-SURF-VEGE-E", Linetype = "Continuous", LineWeight = "0.25" },
        new() { Name = "*VEGE-E", Color = "3" }
    };

    [Fact]
    public void OnlyLayersFtfUsesThatDifferFromTheirStandardChange()
    {
        var used = new[] { "V-SURF-VEGE-TEXT-E", "FTF-QC-ORIENT", "V-MISSING", "XREF|V-SURF-VEGE-E", "V-SURF-VEGE-E" };
        var plan = LayerStandards.PlanUpdate(used, Drawing, Standards);

        Assert.DoesNotContain(plan.Changes, c => c.Layer == "OTHER-LAYER");          // FTF does not use it
        Assert.DoesNotContain(plan.Changes, c => c.Layer.Contains('|'));               // an xref's layer is its own drawing's
        Assert.Equal(3, plan.LayersCompared);                                            // V-MISSING is not in the drawing
        Assert.Equal(3, plan.LayersChanged);

        var tree = plan.Changes.Where(c => c.Layer == "V-SURF-VEGE-TEXT-E").ToList();
        Assert.Equal(new[] { LayerProperty.Color, LayerProperty.LineWeight }, tree.Select(c => c.Property).ToArray());
        Assert.Equal("7 (white)", tree[0].From);
        Assert.Equal("3 (green)", tree[0].To);
        Assert.Equal("Default", tree[1].From);
        Assert.Equal("0.25", tree[1].To);

        var qc = plan.Changes.Single(c => c.Layer == "FTF-QC-ORIENT");
        Assert.Equal(LayerProperty.Plot, qc.Property);
        Assert.Equal("Plot", qc.From);
        Assert.Equal("No plot", qc.To);
        Assert.Equal("FTF-QC-*", qc.Standard);

        // Linetype names match without regard to case; the pattern supplies the colour the layer's own standard leaves out.
        var drip = plan.Changes.Where(c => c.Layer == "V-SURF-VEGE-E").ToList();
        Assert.Single(drip);
        Assert.Equal(LayerProperty.Color, drip[0].Property);
        Assert.Equal("*VEGE-E", drip[0].Standard);
        Assert.Empty(plan.Problems);
    }

    [Fact]
    public void ALayerWithoutAStandardIsLeftAlone()
    {
        var plan = LayerStandards.PlanUpdate(new[] { "OTHER-LAYER", "V-NONE" }, new[] { Layer("V-NONE") }, Standards);
        Assert.Empty(plan.Changes);
        Assert.Equal(1, plan.LayersCompared);
        Assert.Equal(1, plan.LayersWithoutStandard);
    }

    [Fact]
    public void AnUnreadableValueIsReportedNotApplied()
    {
        var plan = LayerStandards.PlanUpdate(new[] { "V-A" }, new[] { Layer("V-A") },
                                             new[] { new LayerStandard { Name = "V-A", Color = "purple", LineWeight = "0.33", Plot = false } });
        Assert.Single(plan.Changes);
        Assert.Equal(LayerProperty.Plot, plan.Changes[0].Property);
        Assert.Equal(2, plan.Problems.Count);
    }

    [Fact]
    public void EveryPropertyIsCompared()
    {
        var now = Layer("V-A", aci: 7, linetype: "Continuous", weight: 25, plot: true, transparency: 0, description: "old");
        var standard = LayerStandards.Resolve(new[]
        {
            new LayerStandard { Name = "V-A", Color = "255,0,0", Linetype = "HIDDEN2", LineWeight = "0.25", Plot = true, Transparency = 40, Description = "new" }
        }, "V-A");
        var changes = LayerStandards.Differences(now, standard);
        Assert.Equal(new[] { LayerProperty.Color, LayerProperty.Linetype, LayerProperty.Transparency, LayerProperty.Description },
                     changes.Select(c => c.Property).ToArray());
        Assert.Equal("255,0,0", changes[0].To);

        var same = Layer("V-A", aci: 7);
        same.Color = LayerColor.FromRgb(255, 0, 0);
        Assert.DoesNotContain(LayerStandards.Differences(same, standard), c => c.Property == LayerProperty.Color);
    }

    [Fact]
    public void AColourBookLayerDiffersFromAnyStandardColour()
    {
        var now = Layer("V-A");
        now.Color = null;
        var changes = LayerStandards.Differences(now, LayerStandards.Resolve(new[] { new LayerStandard { Name = "V-A", Color = "3" } }, "V-A"));
        Assert.Single(changes);
        Assert.Equal("(colour book)", changes[0].From);
    }

    [Fact]
    public void ThePlanTakesTheCatalogLayersAfterTheMapping()
    {
        var uses = new[]
        {
            new LayerUse { Feature = "Tree", ObjectType = "Label", Layer = "V-SURF-VEGE-TEXT-E" },
            new LayerUse { Feature = "Tree", ObjectType = "Dripline", Layer = "V-SURF-VEGE-E" }
        };
        var layers = LayerCatalog.EffectiveLayers(uses, new[] { new LayerMapping { From = "V-SURF-VEGE-TEXT-E", To = "C-TREE-TEXT" } });
        Assert.Equal(new[] { "C-TREE-TEXT", "V-SURF-VEGE-E" }, layers.ToArray());
    }
}
