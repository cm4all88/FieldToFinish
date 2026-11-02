using FieldCodes;
using FieldCodes.Easements;
using FieldCodes.Editing;
using FieldCodes.Orient;
using FieldCodes.Symbols;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// How FTFPOINTS places a block for a rule that opted in (insertBlock): scale, insertion point,
/// rotation method and attribute text. Every setting is optional and its absence is exactly what
/// FTF did before it existed; the opt-in itself is never written as a side effect of editing.
/// </summary>
public sealed class BlockInsertSettingsTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public BlockInsertSettingsTests(RulesFixture fx) => _fx = fx;

    private static readonly string RulesPath = Path.Combine(AppContext.BaseDirectory, "rules.json");

    private static RulesConfig Fresh() => RulesConfig.Load(RulesPath);

    private static RuleDocument Shipped() => RuleDocument.Load(RulesPath);

    // ------------------------------------------------------------------ CodeRule defaults

    [Fact]
    public void ANewRuleHasTodaysBehaviourForEveryBlockSetting()
    {
        var rule = new CodeRule();
        Assert.False(rule.InsertBlock);
        Assert.Equal(1.0, rule.BlockScale);
        Assert.Equal(1.0, rule.BlockScaleDivisor);
        Assert.Equal(BlockPlacement.AtPoint, rule.BlockInsertion);
        Assert.Equal(0.0, rule.BlockOffsetX);
        Assert.Equal(0.0, rule.BlockOffsetY);
        Assert.Equal(BlockRotationMethod.Field, rule.BlockRotation);
        Assert.NotNull(rule.BlockAttributes);
        Assert.Empty(rule.BlockAttributes);
    }

    [Fact]
    public void EveryShippedRuleLeavesTheNewSettingsAtTheirDefaults()
    {
        foreach (var rule in _fx.Config.Codes)
        {
            Assert.Equal(1.0, rule.BlockScale);
            Assert.Equal(BlockPlacement.AtPoint, rule.BlockInsertion);
            Assert.Equal(BlockRotationMethod.Field, rule.BlockRotation);
            Assert.Empty(rule.BlockAttributes);
        }
    }

    [Fact]
    public void TheNewSettingsReadFromRulesJson()
    {
        var rule = JsonConvert.DeserializeObject<CodeRule>(
            "{ \"id\": \"x\", \"match\": \"^X$\", \"insertBlock\": true, \"block\": \"B\", \"blockScale\": 2.5, " +
            "\"blockInsertion\": \"OffsetRotated\", \"blockOffsetX\": 1.5, \"blockOffsetY\": -2, \"blockRotation\": \"Marker\", " +
            "\"blockAttributes\": { \"NUM\": \"{point}\", \"elev\": \"{elevation:0.00}\" } }")!;
        Assert.Equal(2.5, rule.BlockScale);
        Assert.Equal(BlockPlacement.OffsetRotated, rule.BlockInsertion);
        Assert.Equal(1.5, rule.BlockOffsetX);
        Assert.Equal(-2, rule.BlockOffsetY);
        Assert.Equal(BlockRotationMethod.Marker, rule.BlockRotation);
        Assert.Equal("{point}", rule.BlockAttributes["num"]);       // tags match whatever their case
        Assert.Equal("{elevation:0.00}", rule.BlockAttributes["ELEV"]);
    }

    // ------------------------------------------------------------------ placement decisions

    private static ParsedPoint Sign(RulesConfig cfg, string description = "SIGN STOP 135")
        => new FieldCodeParser(cfg).Parse("7", description);

    [Fact]
    public void WithNoSettingsTheBlockIsPlacedExactlyAsBefore()
    {
        var cfg = Fresh();
        var sign = cfg.Codes.Single(c => c.Id == "sign");
        sign.Block = "SSV-SN";
        var p = Sign(cfg);
        Assert.True(p.RotationDegrees.HasValue);

        // The old PlaceBlock: the standard's zero only for its own block, scale from the parser.
        var symbol = cfg.Orientation.SymbolFor(p.Code);
        var old = symbol != null && symbol.DrawnWith(p.BlockName) ? p.MarkerRotationDegrees ?? p.RotationDegrees!.Value : p.RotationDegrees!.Value;

        Assert.Equal(old, BlockInsertion.RotationDegrees(sign, p, cfg.Orientation, p.BlockName, 12.0));
        Assert.Equal(p.BlockScale, BlockInsertion.Scale(sign, p));
        var at = BlockInsertion.Position(sign, new P2(100, 200), 37);
        Assert.Equal(100, at.X);
        Assert.Equal(200, at.Y);
    }

    [Fact]
    public void TheStandardsZeroIsTakenOffOnlyForItsOwnBlock()
    {
        var cfg = Fresh();
        var sign = cfg.Codes.Single(c => c.Id == "sign");
        var p = Sign(cfg, "SIGN STOP 135");
        // SIGN is not a code the PMX standard knows, so the azimuth's AutoCAD angle is kept as is.
        Assert.Equal(315, BlockInsertion.RotationDegrees(sign, p, cfg.Orientation, "ANY-BLOCK", null)!.Value, 6);

        var sn = cfg.Codes.Single(c => c.Id == "sn-sign");
        var q = new FieldCodeParser(cfg).Parse("8", "SN 135 STOP");
        Assert.Equal(225, BlockInsertion.RotationDegrees(sn, q, cfg.Orientation, "SSV-SN", null)!.Value, 6);    // PMX sign, zero 90
        Assert.Equal(315, BlockInsertion.RotationDegrees(sn, q, cfg.Orientation, "OTHER", null)!.Value, 6);     // another block
    }

    [Fact]
    public void RotationMethodsNoneAndMarker()
    {
        var cfg = Fresh();
        var sign = cfg.Codes.Single(c => c.Id == "sign");
        var p = Sign(cfg);

        sign.BlockRotation = BlockRotationMethod.None;
        Assert.Null(BlockInsertion.RotationDegrees(sign, p, cfg.Orientation, "B", 99));

        sign.BlockRotation = BlockRotationMethod.Marker;
        Assert.Equal(99, BlockInsertion.RotationDegrees(sign, p, cfg.Orientation, "B", 99)!.Value, 9);
        Assert.Equal(10, BlockInsertion.RotationDegrees(sign, p, cfg.Orientation, "B", 370)!.Value, 9);
        Assert.Null(BlockInsertion.RotationDegrees(sign, p, cfg.Orientation, "B", null));

        // Field with no rotation in the description: left at 0, as before.
        sign.BlockRotation = BlockRotationMethod.Field;
        var bare = new ParsedPoint { Code = "SIGN" };
        Assert.Null(BlockInsertion.RotationDegrees(sign, bare, cfg.Orientation, "B", 45));
    }

    [Fact]
    public void FixedScaleAppliesUnlessTheScaleComesFromAField()
    {
        var cfg = Fresh();
        var tree = cfg.Codes.Single(c => c.Id == "tree");
        tree.Block = "TREE-{species}";
        var p = new FieldCodeParser(cfg).Parse("1", "CON 18 . 25");
        Assert.Equal(1.0, BlockInsertion.Scale(tree, p));

        tree.BlockScale = 2.5;
        Assert.Equal(2.5, BlockInsertion.Scale(tree, p));

        tree.BlockScaleFrom = "trunk.exact";
        tree.BlockScaleDivisor = 12;
        p = new FieldCodeParser(cfg).Parse("1", "CON 18 . 25");
        Assert.Equal(1.5, BlockInsertion.Scale(tree, p), 9);

        tree.BlockScaleFrom = null;
        tree.BlockScale = 0;                                   // nonsense never reaches AutoCAD
        Assert.Equal(1.0, BlockInsertion.Scale(tree, p));
    }

    [Fact]
    public void OffsetsEastNorthOrTurningWithTheBlock()
    {
        var rule = new CodeRule { BlockOffsetX = 2, BlockOffsetY = 0 };
        var point = new P2(100, 100);

        Assert.Equal(100, BlockInsertion.Position(rule, point, 90).X);   // AtPoint ignores the offset

        rule.BlockInsertion = BlockPlacement.Offset;
        var east = BlockInsertion.Position(rule, point, 90);
        Assert.Equal(102, east.X, 9);
        Assert.Equal(100, east.Y, 9);

        rule.BlockInsertion = BlockPlacement.OffsetRotated;
        var turned = BlockInsertion.Position(rule, point, 90);
        Assert.Equal(100, turned.X, 9);
        Assert.Equal(102, turned.Y, 9);
    }

    [Fact]
    public void TheSettingsReadAsASentence()
    {
        var rule = new CodeRule { BlockScaleFrom = "trunk.exact", BlockScaleDivisor = 12, BlockRotation = BlockRotationMethod.Marker };
        var text = BlockInsertion.Describe(rule);
        Assert.Contains("trunk.exact / 12", text);
        Assert.Contains("marker", text);
        Assert.Contains("on the point", text);
    }

    // ------------------------------------------------------------------ attribute templates

    private static ParsedPoint Hydrant(RulesFixture fx) => fx.Parse("WFH 12", "4001");

    [Fact]
    public void ATemplateUsesTheRulesGroupsAndThePointsOwnValues()
    {
        var p = Hydrant(_fx);
        var values = AttributeTemplate.Values(p, 5000.126, 10000.5, 101.254);
        Assert.Equal("HYD 12", AttributeTemplate.Format("HYD {number}", values));      // the pattern's group
        Assert.Equal("4001", AttributeTemplate.Format("{point}", values));             // the point number
        Assert.Equal("101.25", AttributeTemplate.Format("{elevation:0.00}", values));
        Assert.Equal("N 10000.50 E 5000.13", AttributeTemplate.Format("N {northing:0.00} E {easting:0.00}", values));
        Assert.Equal("WFH", AttributeTemplate.Format("{code}", values));
        Assert.Equal("hydrant", AttributeTemplate.Format("{rule}", values));
        Assert.Equal("WFH 12", AttributeTemplate.Format("{raw}", values));
        Assert.Equal("HYDRANT 12", AttributeTemplate.Format("{label}", values));
    }

    [Fact]
    public void OptionalPartsUnknownNamesAndLinesFollowTheLabelRules()
    {
        var values = AttributeTemplate.Values(Hydrant(_fx), 1, 2, double.NaN);
        Assert.Equal("12", AttributeTemplate.Format("{number}[ EL {elevation:0.0}]", values));      // no elevation: part dropped
        Assert.Equal("X", AttributeTemplate.Format("X{nosuch}", values));
        Assert.Equal("12 WFH", AttributeTemplate.Format("{number}\\P{code}", values));              // one line
        Assert.Equal(string.Empty, AttributeTemplate.Format(string.Empty, values));

        var sentinel = AttributeTemplate.Values(Hydrant(_fx), 1, 2, -1e300);                       // Civil 3D's "no elevation"
        Assert.False(sentinel.ContainsKey("elevation"));
    }

    [Fact]
    public void AGroupOfThePatternWinsOverABuiltInOfTheSameName()
    {
        var p = new ParsedPoint { PointNumber = "9", RuleId = "r" };
        p.Fields["point"] = "PT-A";
        Assert.Equal("PT-A", AttributeTemplate.Format("{point}", AttributeTemplate.Values(p, 0, 0, 0)));
    }

    [Fact]
    public void AFieldAzimuthIsAvailableAsAnAzimuth()
    {
        var p = _fx.Parse("SN 135 STOP");
        Assert.Equal("135", AttributeTemplate.Format("{azimuth:0}", AttributeTemplate.Values(p, 0, 0, 0)));
    }

    [Fact]
    public void PlaceholdersAndTagsAreCheckedAsTyped()
    {
        Assert.Equal(new[] { "point", "elevation" }, AttributeTemplate.Placeholders("{point} [{elevation:0.0}] {point}"));
        Assert.True(AttributeTemplate.IsValidTag("PT_NO"));
        Assert.False(AttributeTemplate.IsValidTag("PT NO"));
        Assert.False(AttributeTemplate.IsValidTag(" "));
    }

    // ------------------------------------------------------------------ RuleDocument editing

    [Fact]
    public void AnUntouchedRuleReadsAsTheDefaults()
    {
        var e = Shipped().GetBlockInsert("tree")!;
        Assert.False(e.InsertBlock);
        Assert.Equal(string.Empty, e.Block);
        Assert.Equal(1.0, e.BlockScale);
        Assert.Equal(1.0, e.BlockScaleDivisor);
        Assert.Equal(BlockPlacement.AtPoint, e.Placement);
        Assert.Equal(BlockRotationMethod.Field, e.Rotation);
        Assert.Empty(e.Attributes);
        Assert.Equal("rotation", Shipped().GetBlockInsert("sign")!.FieldRotation);
    }

    [Fact]
    public void ApplyingTheDefaultsChangesNotOneCharacter()
    {
        var doc = Shipped();
        var before = doc.Render();
        foreach (var id in doc.RuleIds()) doc.ApplyBlockInsert(doc.GetBlockInsert(id)!);
        Assert.Equal(before, doc.Render());
        Assert.Empty(doc.BlockRuleIds());
    }

    [Fact]
    public void EditingWritesTheBlockSettingsButNeverTheOptIn()
    {
        var doc = Shipped();
        var e = doc.GetBlockInsert("hydrant")!;
        e.Block = "OFC-HYD";
        e.BlockLayer = "V-UTIL-WATR-SYMB";
        e.BlockScale = 2;
        e.Placement = BlockPlacement.Offset;
        e.OffsetX = 1.5;
        e.Rotation = BlockRotationMethod.Marker;
        e.Attributes.Add(new KeyValuePair<string, string>("NUM", "{number}"));
        e.InsertBlock = true;                                   // not how the opt-in is made
        doc.ApplyBlockInsert(e);

        Assert.Empty(doc.Validate());
        var rule = doc.CompileCandidate().Codes.Single(c => c.Id == "hydrant");
        Assert.False(rule.InsertBlock);
        Assert.Equal("OFC-HYD", rule.Block);
        Assert.Equal("V-UTIL-WATR-SYMB", rule.BlockLayer);
        Assert.Equal(2, rule.BlockScale);
        Assert.Equal(BlockPlacement.Offset, rule.BlockInsertion);
        Assert.Equal(1.5, rule.BlockOffsetX);
        Assert.Equal(BlockRotationMethod.Marker, rule.BlockRotation);
        Assert.Equal("{number}", rule.BlockAttributes["NUM"]);
        Assert.DoesNotContain("blockOffsetY", doc.Render());    // zero is absence
        Assert.Equal(new[] { "hydrant" }, doc.BlockRuleIds());

        doc.SetInsertBlock("hydrant", true);                    // the confirmed opt-in
        Assert.True(doc.CompileCandidate().Codes.Single(c => c.Id == "hydrant").InsertBlock);
        Assert.True(new FieldCodeParser(doc.CompileCandidate()).Parse("1", "WFH 12").InsertBlock);
        doc.SetInsertBlock("hydrant", false);
        Assert.DoesNotContain("insertBlock", doc.Render());
    }

    [Fact]
    public void ScaleFromAFieldReplacesTheFixedScaleAndBack()
    {
        var doc = Shipped();
        var e = doc.GetBlockInsert("tree")!;
        e.Block = "TREE-{species}";
        e.BlockScale = 2.5;
        doc.ApplyBlockInsert(e);
        Assert.Contains("\"blockScale\": 2.5", doc.Render());

        e.BlockScaleFrom = "trunk.exact";
        e.BlockScaleDivisor = 12;
        doc.ApplyBlockInsert(e);
        var rendered = doc.Render();
        Assert.DoesNotContain("\"blockScale\"", rendered);
        Assert.Contains("\"blockScaleFrom\": \"trunk.exact\"", rendered);
        Assert.Empty(doc.Validate());

        e.BlockScaleFrom = string.Empty;
        e.BlockScale = 1;
        doc.ApplyBlockInsert(e);
        rendered = doc.Render();
        Assert.DoesNotContain("\"blockScale", rendered);          // no key (the comment names them)
        Assert.Contains("//block", rendered);                   // the rule's own comment stays
    }

    [Fact]
    public void TakingTheBlockOffKeepsTheRulesComments()
    {
        var doc = Shipped();
        var e = doc.GetBlockInsert("tree")!;
        e.Block = "TREE-{species}";
        e.Attributes.Add(new KeyValuePair<string, string>("SIZE", "{trunk}\""));
        doc.ApplyBlockInsert(e);
        doc.SetInsertBlock("tree", true);
        doc.RemoveBlockInsert("tree");
        Assert.Equal(Shipped().Render(), doc.Render());
    }

    [Fact]
    public void AttributeCommentsSurviveAnEdit()
    {
        var doc = RuleDocument.FromText(
            "{ \"codes\": [ { \"id\": \"x\", \"match\": \"^(?<code>X)\\\\s+(?<number>[0-9]+)$\", \"block\": \"B\", " +
            "\"blockAttributes\": { \"//why\": \"office tags\", \"NUM\": \"{number}\" } } ] }");
        var e = doc.GetBlockInsert("x")!;
        Assert.Equal("NUM", Assert.Single(e.Attributes).Key);
        e.Attributes.Add(new KeyValuePair<string, string>("PT", "{point}"));
        doc.ApplyBlockInsert(e);
        var rendered = doc.Render();
        Assert.Contains("\"//why\": \"office tags\"", rendered);
        Assert.Contains("\"PT\": \"{point}\"", rendered);
        Assert.Empty(doc.Validate());
    }

    [Theory]
    [InlineData("\"insertBlock\": true", "names no block")]
    [InlineData("\"block\": \"B\", \"blockScale\": 0", "greater than zero")]
    [InlineData("\"block\": \"B\", \"blockScaleFrom\": \"size\"", "block scale reads field 'size'")]
    [InlineData("\"block\": \"B\", \"blockScaleFrom\": \"number\", \"blockScaleDivisor\": 0", "divisor cannot be zero")]
    [InlineData("\"block\": \"B\", \"blockLayer\": \"A|B\"", "block layer")]
    [InlineData("\"block\": \"B\", \"blockAttributes\": { \"NUM\": \"{nosuch}\" }", "attribute NUM uses {nosuch}")]
    [InlineData("\"block\": \"B\", \"blockAttributes\": { \"PT NO\": \"{point}\" }", "attribute tag 'PT NO'")]
    [InlineData("\"block\": \"B\", \"blockAttributes\": { \"EL\": \"{elevation:Q}\" }", "number format")]
    public void InvalidBlockSettingsAreRefused(string settings, string expected)
    {
        var doc = RuleDocument.FromText(
            "{ \"codes\": [ { \"id\": \"x\", \"match\": \"^(?<code>X)\\\\s+(?<number>[0-9]+)$\", " + settings + " } ] }");
        Assert.Contains(doc.Validate(), p => p.Contains(expected));
    }

    [Fact]
    public void BuiltInAttributeNamesAreValid()
    {
        var doc = RuleDocument.FromText(
            "{ \"codes\": [ { \"id\": \"x\", \"match\": \"^(?<code>X)\\\\s+(?<number>[0-9]+)$\", \"block\": \"B\", " +
            "\"blockAttributes\": { \"A\": \"{point} {elevation:0.00} {elev} {northing} {easting} {raw} {note} {label} {rule} {azimuth} {code} {number}\" } } ] }");
        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void TheBlockSettingsMergeOntoTheFileAsItIsNow()
    {
        var staged = Shipped();
        var e = staged.GetBlockInsert("hydrant")!;
        e.Block = "OFC-HYD";
        staged.ApplyBlockInsert(e);
        staged.SetInsertBlock("hydrant", true);

        var onDisk = Shipped();
        var pole = onDisk.GetEditable("pole");
        pole.TagPrefix = "PL";
        onDisk.ApplyEdit(pole);

        staged.CopyBlockInsertTo(onDisk, "hydrant");
        var cfg = onDisk.CompileCandidate();
        Assert.True(cfg.Codes.Single(c => c.Id == "hydrant").InsertBlock);
        Assert.Equal("OFC-HYD", cfg.Codes.Single(c => c.Id == "hydrant").Block);
        Assert.Equal("PL", cfg.Codes.Single(c => c.Id == "pole").TagPrefix);

        // A block taken off in the editor comes off the file too.
        staged.RemoveBlockInsert("hydrant");
        staged.CopyBlockInsertTo(onDisk, "hydrant");
        Assert.DoesNotContain("OFC-HYD", onDisk.Render());
        Assert.DoesNotContain("insertBlock", onDisk.Render());
    }
}
