using System.Linq;
using FieldCodes;

namespace FieldCodes.Tests;

public sealed class ParserTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public ParserTests(RulesFixture fx) => _fx = fx;

    [Fact]
    public void RulesFile_Loads_And_Compiles()
    {
        Assert.Equal("1.0", _fx.Config.Version);
        Assert.Equal(16, _fx.Config.Codes.Count);     // tree, pole, sign + 13 point-feature rules
        Assert.Equal(4, _fx.Config.Modifiers.Count);
        Assert.All(_fx.Config.Codes, c => Assert.NotNull(c.Regex));
        Assert.All(_fx.Config.Modifiers, m => Assert.NotNull(m.Regex));
    }

    // ------------------------------------------------------------- simple tree

    [Fact]
    public void Tree_Simple_ParsesAllDrawingInstructions()
    {
        // Office format: species, diameter, '.', drip radius.
        var p = _fx.Parse("CON 18 . 25");

        Assert.True(p.Recognized);
        Assert.False(p.HasErrors);
        Assert.Equal("tree", p.RuleId);
        Assert.Equal("CON", p.Code);
        Assert.Equal("CONIFER", p.Species);

        Assert.Equal(18.0, p.TrunkInches);
        Assert.Equal(25.0, p.DripRadius);
        Assert.Equal(1, p.StemCount);

        // No block: the Civil 3D point style draws the tree symbol, so placing one
        // here would put a second symbol on top of the first.
        Assert.Null(p.BlockName);
        Assert.Null(p.BlockLayer);

        Assert.Equal("V-TREE-DRIP", p.DripLayer);
        Assert.True(p.DripUnify);
        Assert.Equal("18\" CONIFER", p.LabelText);
        Assert.Equal("V-TREE-TEXT", p.LabelLayer);
        Assert.Equal(LeaderMode.Auto, p.Leader);
        Assert.Equal("T", p.TagPrefix);
        Assert.True(p.HasDripLine);
    }

    [Theory]
    [InlineData("CON 22 . 25", "CONIFER", 22.0, 25.0)]
    [InlineData("DEC 12 . 18", "DECIDUOUS", 12.0, 18.0)]
    [InlineData("MAP 4 . 20", "MAPLE", 4.0, 20.0)]
    [InlineData("CON 6 . 8", "CONIFER", 6.0, 8.0)]
    public void Tree_RealDescriptionsFromTheBaseMap_Parse(
        string desc, string species, double trunk, double drip)
    {
        var p = _fx.Parse(desc);
        Assert.False(p.HasErrors);
        Assert.Equal(species, p.Species);
        Assert.Equal(trunk, p.TrunkInches);
        Assert.Equal(drip, p.DripRadius);
    }

    // ------------------------------------------------------------- multi-stem

    [Fact]
    public void Tree_MultiStem_AveragesStemsAndRetainsRawValues()
    {
        // Real point 10142. More than one diameter is what makes it a cluster.
        var p = _fx.Parse("CON 6 8 8 12 18 18 . 30");

        Assert.False(p.HasErrors);

        // Arithmetic mean per multiStemAverage: 70/6 = 11.667
        Assert.Equal(70.0 / 6.0, p.TrunkInches!.Value, 9);

        // Raw stems retained -- the reviewing agency may use a different formula,
        // and the average alone cannot be recomputed.
        Assert.Equal(new[] { 6.0, 8.0, 8.0, 12.0, 18.0, 18.0 }, p.Stems);
        Assert.Equal("6,8,8,12,18,18", p.Fields["trunk.stems"]);
        Assert.Equal("6", p.Fields["trunk.count"]);

        Assert.Equal(6, p.StemCount);
        Assert.Equal(30.0, p.DripRadius);

        // The cluster label needs no modifier token to announce itself.
        Assert.Equal("CLUSTER OF 6 STEMS, AVG 12\" CONIFER", p.LabelText);
    }

    [Fact]
    public void RoundedTrunkIsForLabelsOnly_ExactValueIsKeptSeparately()
    {
        // Real point 10145: 8,8,10,16 averages 10.5". The label rounds to 10" per
        // trunkDecimals, but anything geometric has to use the unrounded value.
        var p = _fx.Parse("CON 8 8 10 16 . 28");

        Assert.False(p.HasErrors);
        Assert.Equal(10.5, p.TrunkInches!.Value, 9);
        Assert.Equal("10.5", p.Fields["trunk.exact"]);
        Assert.Equal("10", p.Fields["trunk"]);          // display only
        Assert.Contains("AVG 10\"", p.LabelText);
    }

    [Fact]
    public void BlockScale_WhenABlockIsConfigured_UsesTheUnroundedAverage()
    {
        // The tree rule places no block today, but if the symbol is ever handed back
        // to the plugin, scaling must come off trunk.exact and not the rounded label.
        // 10.5" would otherwise scale as 10" -- a symbol drawn 5% small.
        var cfg = RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        var tree = cfg.Codes.Single(c => c.Id == "tree");
        tree.Block = "TREE-{species}";
        tree.BlockScaleFrom = "trunk.exact";
        tree.BlockScaleDivisor = 12;

        var p = new FieldCodeParser(cfg).Parse("10145", "CON 8 8 10 16 . 28");

        Assert.Equal("TREE-CONIFER", p.BlockName);
        Assert.Equal(10.5 / 12.0, p.BlockScale, 9);
    }

    [Fact]
    public void Tree_TwoStems_IsAlreadyACluster()
    {
        var p = _fx.Parse("DEC 12 12 . 14");

        Assert.False(p.HasErrors);
        Assert.Equal(2, p.StemCount);
        Assert.Equal(12.0, p.TrunkInches);
        Assert.Equal("CLUSTER OF 2 STEMS, AVG 12\" DECIDUOUS", p.LabelText);
    }

    [Fact]
    public void Tree_SingleStem_UsesThePlainLabel()
    {
        var p = _fx.Parse("CON 18 . 25");
        Assert.Equal(1, p.StemCount);
        Assert.DoesNotContain("CLUSTER", p.LabelText);
    }

    [Fact]
    public void Tree_MultiStem_ClusterCountDisagreeingWithStems_Warns()
    {
        var p = _fx.Parse("DEC 12 12 . 14 CL4");

        Assert.False(p.HasErrors);
        Assert.True(p.HasWarnings);
        Assert.Contains(p.Diagnostics,
            d => d.Code == "CLUSTER" && d.Severity == Severity.Warning);
    }

    // ------------------------------------------------------------- ignored codes

    [Theory]
    [InlineData("GS")]
    [InlineData("GS2")]          // trailing sequence number
    [InlineData("INFO some note")]
    public void NeverDrawCodes_AreSilent(string desc)
    {
        var p = _fx.Parse(desc);

        Assert.True(p.Ignored);
        Assert.False(p.Unhandled);
        Assert.False(p.HasErrors);
        Assert.Empty(p.Diagnostics);
    }

    [Theory]
    // Bare codes with no confirmed symbol-label standard. PP/SN/CB and friends
    // moved to the symbolLabels list ("like the code list") and now label
    // themselves -- see SymbolLabelTests.
    [InlineData("SSCO")]
    [InlineData("MW")]
    [InlineData("UVLT")]
    [InlineData("GYA")]
    [InlineData("CONC")]
    [InlineData("FMON")]
    public void BareCodes_DoNothingAndSayNothing(string desc)
    {
        // "If the note doesn't have anything then it does nothing." There is no value
        // to label and the point style already shows the feature.
        var p = _fx.Parse(desc);

        Assert.True(p.NoContent);
        Assert.False(p.Unhandled);
        Assert.False(p.HasErrors);
        Assert.Empty(p.Diagnostics);
    }

    [Theory]
    [InlineData("EC B")]
    [InlineData("EC RMP B")]
    [InlineData("TBC EP B")]
    [InlineData("BLD B EC B BLD1 B")]     // three figures on one point
    [InlineData("RWC1 B RWC2 B")]
    [InlineData("LN B ASPH")]
    public void LineworkPoints_AreSilent_TheFiguresAreBuiltElsewhere(string desc)
    {
        var p = _fx.Parse(desc);

        Assert.True(p.Ignored);
        Assert.False(p.Unhandled);
        Assert.False(p.HasErrors);
        Assert.Empty(p.Diagnostics);
    }

    [Theory]
    [InlineData("QZ 1015", "QZ")]        // on neither the office sheet nor the figure database
    [InlineData("UCO 2", "UCO")]
    public void CodesCarryingDataWithNoRule_AreReportedAsUnhandled(string desc, string code)
    {
        // Something with data that nothing is configured for is an unfinished setup,
        // and the one case genuinely worth surfacing.
        var p = _fx.Parse(desc);

        Assert.True(p.Unhandled);
        Assert.False(p.NoContent);
        Assert.False(p.HasErrors);
        Assert.Equal(code, p.Code);
        Assert.Contains(p.Diagnostics, d => d.Code == "UNHANDLED" && d.Severity == Severity.Info);
    }

    [Theory]
    [InlineData("CON 18")]                  // known species, drip missing
    [InlineData("CON 18 . 25 BOGUS")]       // known species, unknown modifier
    [InlineData("DEC 12 12 . 14 ZZZ")]
    public void MalformedKnownCodes_AreStillHardErrors(string desc)
    {
        // Strictness is preserved exactly where it matters: the tool recognised the
        // shape and the data is wrong.
        var p = _fx.Parse(desc);

        Assert.True(p.HasErrors);
        Assert.False(p.Unhandled);
    }

    [Fact]
    public void IgnoredCodeDoesNotSwallowATreeCode()
    {
        // "CON" must never be shadowed by an ignore entry.
        var p = _fx.Parse("CON 18 . 25");
        Assert.False(p.Ignored);
        Assert.True(p.Recognized);
    }

    // ------------------------------------------------- modifier order invariance

    [Fact]
    public void Modifiers_ApplyInPriorityOrder_NotTokenOrder()
    {
        var a = _fx.Parse("CON 18 . 25 CL4 DEAD");
        var b = _fx.Parse("CON 18 . 25 DEAD CL4");

        Assert.Equal(Canon.Of(a), Canon.Of(b));

        // and specifically: cluster (priority 10) before dead (priority 20)
        Assert.Equal(new[] { "cluster", "dead" }, a.Modifiers.Select(m => m.Id));
        Assert.Equal(new[] { "cluster", "dead" }, b.Modifiers.Select(m => m.Id));
    }

    [Fact]
    public void Canon_DistinguishesGenuinelyDifferentPoints()
    {
        // Negative control: guards the invariance test above from going vacuous
        // if Canon.Of ever stops capturing the fields that matter.
        Assert.NotEqual(
            Canon.Of(_fx.Parse("CON 18 . 25")),
            Canon.Of(_fx.Parse("CON 18 . 25 DEAD")));
        Assert.NotEqual(
            Canon.Of(_fx.Parse("CON 18 . 25")),
            Canon.Of(_fx.Parse("DEC 18 . 25")));
    }

    [Fact]
    public void Modifiers_ClusterAndDead_ProduceCombinedLabelAndLayers()
    {
        var p = _fx.Parse("CON 18 . 25 CL4 DEAD");

        Assert.False(p.HasErrors);

        // cluster replaces the label format; dead appends its suffix
        Assert.Equal("CLUSTER OF 4 TREES AVERAGE 18\" (DEAD)", p.LabelText);

        // No modifier reintroduces a block, or the point style's symbol would double.
        Assert.Null(p.BlockName);

        // dead contributes a layer suffix to every layer it touches
        Assert.Equal("V-TREE-TEXT-DEAD", p.LabelLayer);
        Assert.Equal("V-TREE-DRIP-DEAD", p.DripLayer);

        // dead is flagged for review
        Assert.Contains(p.Diagnostics, d => d.Code == "FLAG" && d.Severity == Severity.Warning);
    }

    // ------------------------------------------------------------------ rotation

    [Fact]
    public void Sign_Azimuth135_Becomes315DegreesAutoCadRotation()
    {
        var p = _fx.Parse("SIGN STOP 135");

        Assert.False(p.HasErrors);
        Assert.Equal("sign", p.RuleId);

        // Survey azimuth is clockwise from north; AutoCAD is counter-clockwise
        // from east. 90 - 135 = -45 -> 315.
        Assert.Equal(315.0, p.RotationDegrees);

        // The rotation is applied to the marker Civil 3D already drew; FTF places no
        // symbol of its own.
        Assert.Null(p.BlockName);
        Assert.False(p.InsertBlock);
        Assert.Equal("STOP SIGN", p.LabelText);
        Assert.Equal(LeaderMode.Always, p.Leader);
        Assert.Equal("S", p.TagPrefix);
    }

    [Theory]
    [InlineData("SIGN STOP 0", 90.0)]
    [InlineData("SIGN STOP 90", 0.0)]
    [InlineData("SIGN STOP 135", 315.0)]
    [InlineData("SIGN STOP 180", 270.0)]
    [InlineData("SIGN STOP 270", 180.0)]
    [InlineData("SIGN STOP 360", 90.0)]
    public void Sign_AzimuthConversion_IsNormalizedTo0_360(string desc, double expected)
    {
        var p = _fx.Parse(desc);
        Assert.False(p.HasErrors);
        Assert.Equal(expected, p.RotationDegrees!.Value, 9);
        Assert.InRange(p.RotationDegrees!.Value, 0.0, 360.0);
    }

    // -------------------------------------------------------------- error cases

    [Fact]
    public void UnknownSpecies_IsReportedAsUnhandled_WithItsDescription()
    {
        // The tree pattern is built from the species map, so SPR (not on the office tree list) does not match it at
        // all. It surfaces in the unhandled summary carrying its real description --
        // which is exactly what someone needs in order to add SPR to the map.
        //
        // The alternative, capturing species loosely to produce a precise SPECIES
        // error, made the tree rule swallow every other code of the same shape:
        // "PP 1234" parsed as a tree of unknown species rather than a power pole.
        var p = _fx.Parse("SPR 18 . 25");

        Assert.True(p.Unhandled);
        Assert.False(p.HasErrors);
        Assert.Equal("SPR", p.Code);
    }

    [Fact]
    public void TreeRuleDoesNotSwallowOtherCodesOfTheSameShape()
    {
        // Code-plus-number is the same shape as a single-stem tree. Every one of these
        // must land on its own rule, never on the tree rule.
        Assert.Equal("pole", _fx.Parse("PP 1234").RuleId);
        Assert.Equal("sn-sign", _fx.Parse("SN 135").RuleId);
        Assert.Equal("catch-basin", _fx.Parse("CB 4").RuleId);
        Assert.Equal("manhole", _fx.Parse("SSMH 1042").RuleId);

        // And one with no rule at all still is not a tree.
        var unk = _fx.Parse("QZ 1015");
        Assert.True(unk.Unhandled);
        Assert.NotEqual("tree", unk.RuleId);
    }

    [Fact]
    public void SpeciesPatternStaysInSyncWithTheSpeciesMap()
    {
        // {SPECIES} expands from the map, so every species in it must parse.
        var tree = _fx.Config.Codes.Single(c => c.Id == "tree");

        foreach (var species in tree.Species.Keys)
        {
            var p = _fx.Parse(species + " 12 . 20");
            Assert.False(p.Unhandled, species + " is in the map but does not parse");
            Assert.False(p.HasErrors);
        }
    }

    [Theory]
    [InlineData("CON 18 . 25 DEAD-PROTECTED")]   // the README's own example
    [InlineData("CON 18 . 25 ZZZ")]
    [InlineData("CON 18 . 25 DEAD BOGUS")]
    public void Error_UnrecognizedModifier_IsAnError_NeverIgnored(string desc)
    {
        var p = _fx.Parse(desc);

        Assert.True(p.HasErrors);
        Assert.Contains(p.Diagnostics,
            d => d.Code == "MODIFIER" && d.Severity == Severity.Error);
    }

    [Fact]
    public void Error_MissingDripRadius_IsAnError_NoFallbackValue()
    {
        var p = _fx.Parse("CON 18");

        Assert.True(p.Recognized);
        Assert.True(p.HasErrors);
        var d = Assert.Single(p.Diagnostics, x => x.Code == "DRIP");
        Assert.Equal(Severity.Error, d.Severity);

        // No fallback drip radius is invented.
        Assert.False(p.DripRadius.HasValue);
        Assert.False(p.HasDripLine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Error_BlankDescription_IsAnError(string desc)
    {
        var p = _fx.Parse(desc);
        Assert.False(p.Recognized);
        Assert.Contains(p.Diagnostics, d => d.Code == "EMPTY" && d.Severity == Severity.Error);
    }

    [Fact]
    public void UnknownCode_IsUnhandledRatherThanAnError()
    {
        // Was a hard NOMATCH error when the tool only did trees. Now that it is growing
        // to cover poles, signs and structures, an unrecognised code means "no rule
        // configured yet" far more often than it means "bad data".
        var p = _fx.Parse("WIDGET 5");

        Assert.False(p.Recognized);
        Assert.True(p.Unhandled);
        Assert.False(p.HasErrors);
        Assert.Contains(p.Diagnostics, d => d.Code == "UNHANDLED" && d.Severity == Severity.Info);
    }

    // ------------------------------------------------------------- determinism

    [Fact]
    public void ParseAll_OrdersByPointNumberNumerically()
    {
        var input = new[]
        {
            new KeyValuePair<string, string>("10", "CON 18 . 25"),
            new KeyValuePair<string, string>("2",  "CON 18 . 25"),
            new KeyValuePair<string, string>("1",  "CON 18 . 25"),
        };

        var result = _fx.Parser.ParseAll(input);

        Assert.Equal(new[] { "1", "2", "10" }, result.Select(r => r.PointNumber));
    }

    [Fact]
    public void Parse_IsPureAndRepeatable()
    {
        var a = _fx.Parse("CON 6 8 8 12 18 18 . 30");
        var b = _fx.Parse("CON 6 8 8 12 18 18 . 30");
        Assert.Equal(Canon.Of(a), Canon.Of(b));
    }
}

