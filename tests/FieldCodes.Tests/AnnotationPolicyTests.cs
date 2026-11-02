using FieldCodes;
using FieldCodes.Annotation;
using FieldCodes.Settings;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// Label All and the label/tag stages: which family a point is in, which families a run
/// covers, trees annotated one way only, dips kept out of label text, points marked no
/// label, hand labels respected, and the preview counts a run would produce.
/// </summary>
public sealed class AnnotationPolicyTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public AnnotationPolicyTests(RulesFixture fx) => _fx = fx;

    private static HashSet<AnnotationCategory> All() => new(AnnotationPolicy.All());

    [Theory]
    [InlineData("PP 1234", AnnotationCategory.Utilities)]
    [InlineData("SSMH 12", AnnotationCategory.StormSewer)]
    [InlineData("CB 4", AnnotationCategory.StormSewer)]
    [InlineData("SN 135 STOP", AnnotationCategory.Signs)]
    [InlineData("XMAG AKA 5", AnnotationCategory.Control)]
    [InlineData("WFH 7", AnnotationCategory.Utilities)]
    [InlineData("MB 12", AnnotationCategory.OtherPoints)]
    [InlineData("CON 18 . 25", AnnotationCategory.Trees)]
    public void EveryPointBelongsToOneFamily(string description, AnnotationCategory expected)
    {
        Assert.Equal(expected, AnnotationPolicy.CategoryOf(_fx.Parse(description), _fx.Config));
    }

    [Fact]
    public void ARuleCanNameItsOwnFamily()
    {
        var p = _fx.Parse("MB 12");
        var rule = _fx.Config.Codes.First(r => r.Id == p.RuleId);
        var was = rule.Category;
        try
        {
            rule.Category = "Utilities";
            Assert.Equal(AnnotationCategory.Utilities, AnnotationPolicy.CategoryOf(p, _fx.Config));
        }
        finally { rule.Category = was; }
    }

    // ------------------------------------------------------------ trees: one annotation only

    [Theory]
    [InlineData(TreeAnnotation.TagOnly, false, true)]
    [InlineData(TreeAnnotation.DescriptionOnly, true, false)]
    [InlineData(TreeAnnotation.None, false, false)]
    public void ATreeIsTaggedOrLabelledNeverBoth(TreeAnnotation mode, bool label, bool tag)
    {
        var p = _fx.Parse("CON 18 . 25");
        Assert.False(string.IsNullOrEmpty(p.LabelText));
        Assert.False(string.IsNullOrEmpty(p.TagPrefix));
        var s = new AnnotationSettings { TreeAnnotation = mode };
        Assert.Equal(label, AnnotationPolicy.Label(p, s, All(), _fx.Config).Annotate);
        Assert.Equal(tag, AnnotationPolicy.Tag(p, s, All(), _fx.Config).Annotate);
    }

    [Fact]
    public void TheDefaultIsTheDescriptionAndNoSchedule()
    {
        var s = new AnnotationSettings();
        Assert.Equal(TreeAnnotation.DescriptionOnly, s.TreeAnnotation);
        Assert.False(s.ScheduleTrees);
        Assert.DoesNotContain(AnnotationCategory.StormSewer, s.Categories);
    }

    [Fact]
    public void NonTreeTagsAreUnaffectedByTheTreeChoice()
    {
        var pole = _fx.Parse("PP 1234");
        foreach (TreeAnnotation mode in Enum.GetValues(typeof(TreeAnnotation)))
            Assert.True(AnnotationPolicy.Tag(pole, new AnnotationSettings { TreeAnnotation = mode }, All(), _fx.Config).Annotate);
    }

    // ------------------------------------------------------------ families and exclusions

    [Fact]
    public void AFamilyNotSelectedIsLeftAlone()
    {
        var mh = _fx.Parse("SSMH 12");
        var s = new AnnotationSettings();
        var d = AnnotationPolicy.Label(mh, s, new HashSet<AnnotationCategory>(s.Categories), _fx.Config);
        Assert.False(d.Annotate);
        Assert.Contains("not selected", d.Reason);
        Assert.True(AnnotationPolicy.Label(mh, s, All(), _fx.Config).Annotate);
    }

    [Theory]
    [InlineData("12 RCP N 6.41", true)]
    [InlineData("8\" PVC NE 6.25", true)]
    [InlineData("MD 6.25", true)]
    [InlineData("BOT 7.82", true)]
    [InlineData("WL 6.9", true)]
    [InlineData("INV 119.18", true)]
    [InlineData("24X36 BOX CONC E 5.10", true)]
    [InlineData("W FACE 438383-159717", false)]
    [InlineData("6.0 WOOD WIRE", false)]
    [InlineData("TIES E", false)]
    [InlineData("AKA 1000 & 3001", false)]
    [InlineData("12\" N72E", false)]
    [InlineData("", false)]
    public void DipMeasurementsAreRecognisedInNotes(string note, bool isDip)
    {
        Assert.Equal(isDip, AnnotationPolicy.IsDepthNote(note));
        Assert.Equal(!isDip, AnnotationPolicy.MayUseNote(note, new AnnotationSettings()));
        Assert.True(AnnotationPolicy.MayUseNote(note, new AnnotationSettings { ExcludeDepthNotes = false }));
    }

    [Fact]
    public void APointMarkedNoLabelIsNotLabelledOrTagged()
    {
        var p = _fx.Parse("PP 1234");
        p.Note = "NOLABEL";
        var s = new AnnotationSettings();
        Assert.False(AnnotationPolicy.Label(p, s, All(), _fx.Config).Annotate);
        Assert.False(AnnotationPolicy.Tag(p, s, All(), _fx.Config).Annotate);
        Assert.Equal("marked no label", AnnotationPolicy.Label(p, s, All(), _fx.Config).Reason);
    }

    // ------------------------------------------------------------ the preview

    [Fact]
    public void ThePreviewCountsWhatTheRunWillDo()
    {
        var s = new AnnotationSettings();
        var scope = new HashSet<AnnotationCategory>(s.Categories);
        var c = new List<AnnotationCandidate>
        {
            new() { Parsed = _fx.Parse("PP 1", "1") },                                   // create
            new() { Parsed = _fx.Parse("PP 2", "2"), HasFtfLabel = true },               // update
            new() { Parsed = _fx.Parse("PP 3", "3"), HasFtfLabel = true, LabelMovedByHand = true }, // kept by hand
            new() { Parsed = _fx.Parse("PP 4", "4"), ManualLabelNearby = true },         // hand label already there
            new() { Parsed = _fx.Parse("SSMH 5", "5") },                                 // family off
            new() { Parsed = _fx.Parse("CON 18 . 25", "6") },                           // tree, description
            new() { Parsed = _fx.Parse("GS", "7") }                                     // nothing to do: not counted
        };
        var plan = AnnotationPlan.Build(c, s, scope, scope, includeTags: true, _fx.Config);
        Assert.Equal(2, plan.Count(PlanKind.Label, PlanOutcome.Create));
        Assert.Equal(1, plan.Count(PlanKind.Label, PlanOutcome.Update));
        Assert.Equal(3, plan.Count(PlanKind.Label, PlanOutcome.Skip));
        Assert.Contains(plan.Items, i => i.PointNumber == "3" && i.Reason == "moved by hand: kept");
        Assert.Contains(plan.Items, i => i.PointNumber == "4" && i.Reason == "already labelled by hand");
        // Tags: four poles tagged, the tree is labelled so not tagged.
        Assert.Equal(4, plan.Count(PlanKind.Tag, PlanOutcome.Create));
        Assert.Contains(plan.Items, i => i.PointNumber == "6" && i.Kind == PlanKind.Tag && i.Outcome == PlanOutcome.Skip);
        Assert.Contains(plan.Summary(true), l => l.StartsWith("Labels: 2 to create, 1 to update, 3 skipped"));
    }

    [Fact]
    public void AnErrorIsFlaggedNotDrawn()
    {
        var p = _fx.Parse("CON 18 . 25");
        p.Add(Severity.Error, "TEST", "trunk is not a number");
        var plan = AnnotationPlan.Build(new[] { new AnnotationCandidate { Parsed = p } }, new AnnotationSettings(), All(), All(), true, _fx.Config);
        Assert.Equal(1, plan.Count(PlanKind.Label, PlanOutcome.Flag));
        Assert.Equal(0, plan.Count(PlanKind.Tag, PlanOutcome.Create));
    }

    // ------------------------------------------------------------ hand labels

    [Theory]
    [InlineData("18in Fir", true)]
    [InlineData("{\\fArial|b0;18\" FIR}", true)]
    [InlineData("18\" fir", true)]
    [InlineData("FIR", true)]                // says less, but it is this tree's label
    [InlineData("TO REMAIN", false)]
    [InlineData("24\" CEDAR", false)]
    public void AHandLabelSayingTheSameThingIsRecognised(string handText, bool same)
    {
        var index = new ManualAnnotationIndex(new[] { new ExistingAnnotation { Text = handText, X = 102, Y = 99 } }, 5);
        Assert.Equal(same, index.HasMatch("18\" FIR", 100, 100, 8));
        Assert.False(index.HasMatch("18\" FIR", 200, 200, 8));     // too far away
    }

    // ------------------------------------------------------------ presets

    [Fact]
    public void StandardTopographicLeavesOutSchedulesTagsAndDips()
    {
        var plan = ProcessPresets.Build(ProcessPreset.StandardTopographic, new AnnotationSettings());
        Assert.Equal(new[] { "FTFPOINTS", "FTFDRIP", "FTFLINELABELS", "FTFLABELS", "FTFORDER" }, plan.Stages);
        Assert.DoesNotContain(AnnotationCategory.StormSewer, plan.LabelFamilies);
        Assert.False(plan.Runs(ProcessStage.Tags));
        Assert.False(plan.Runs(ProcessStage.Table));
    }

    [Fact]
    public void StandardTopographicTagsTreesOnlyWhenTreesAreTagged()
    {
        var plan = ProcessPresets.Build(ProcessPreset.StandardTopographic, new AnnotationSettings { TreeAnnotation = TreeAnnotation.TagOnly });
        Assert.True(plan.Runs(ProcessStage.Tags));
        Assert.Equal(new[] { AnnotationCategory.Trees }, plan.TagFamilies);
    }

    [Fact]
    public void TreesAndVegetationTouchesOnlyTrees()
    {
        var plan = ProcessPresets.Build(ProcessPreset.TreesAndVegetation, new AnnotationSettings { ScheduleTrees = true });
        Assert.Equal(new[] { "FTFDRIP", "FTFLABELS", "FTFTABLE", "FTFORDER" }, plan.Stages);
        Assert.Equal(new[] { AnnotationCategory.Trees }, plan.LabelFamilies);
    }

    [Fact]
    public void AnnotationsOnlyNeverRedrawsDriplinesOrTurnsSymbols()
    {
        var plan = ProcessPresets.Build(ProcessPreset.AnnotationsOnly, new AnnotationSettings());
        Assert.False(plan.Runs(ProcessStage.Drip));
        Assert.False(plan.Runs(ProcessStage.Points));
        Assert.False(plan.Runs(ProcessStage.Orient));
        Assert.Equal("FTFORDER", plan.Stages.Last());
    }

    [Fact]
    public void ACustomSelectionIsPutInOrderWithDrawOrderLast()
    {
        var plan = ProcessPresets.Build(ProcessPreset.Custom, new AnnotationSettings(),
            new[] { "FTFTAGS", "ftflabels", "FTFPOINTS", "NOPE" }, new[] { AnnotationCategory.Utilities });
        Assert.Equal(new[] { "FTFPOINTS", "FTFLABELS", "FTFTAGS", "FTFORDER" }, plan.Stages);
        Assert.Contains(plan.Adjustments, a => a.Contains("Draw order added"));
        Assert.Contains(plan.Adjustments, a => a.Contains("NOPE"));
        Assert.Equal(new[] { AnnotationCategory.Utilities }, plan.LabelFamilies);
    }

    [Fact]
    public void PointFinishingAloneDoesNotTouchDrawOrder()
    {
        var plan = ProcessPresets.Build(ProcessPreset.Custom, new AnnotationSettings(), new[] { "FTFPOINTS" });
        Assert.Equal(new[] { "FTFPOINTS" }, plan.Stages);
    }

    [Fact]
    public void LineFeaturesOffDropsTheLinePass()
    {
        var s = new AnnotationSettings();
        s.Categories.Remove(AnnotationCategory.Lines);
        var plan = ProcessPresets.Build(ProcessPreset.StandardTopographic, s);
        Assert.False(plan.Runs(ProcessStage.LineLabels));
    }

    [Fact]
    public void ARunScopeAppliesOnlyWhileItLasts()
    {
        var s = new AnnotationSettings();
        var plan = ProcessPresets.Build(ProcessPreset.TreesAndVegetation, s);
        using (AnnotationRun.Use(plan))
            Assert.Equal(new[] { AnnotationCategory.Trees }, AnnotationRun.LabelScope(s));
        Assert.Contains(AnnotationCategory.Utilities, AnnotationRun.LabelScope(s));
    }

    // ------------------------------------------------------------ settings round trip

    [Fact]
    public void AnOlderSettingsFileGetsTheDefaults()
    {
        var s = JsonConvert.DeserializeObject<FtfSettings>("{ \"general\": { \"unitsPerFoot\": 1.0 } }")!;
        s.FillMissingSections();
        Assert.Equal(TreeAnnotation.DescriptionOnly, s.Annotation.TreeAnnotation);
        Assert.Equal(AnnotationSettings.DefaultCategories(), s.Annotation.Categories);
    }

    [Fact]
    public void LabelAllSettingsSurviveSaveAndLoad()
    {
        var s = new FtfSettings();
        s.Annotation.TreeAnnotation = TreeAnnotation.TagOnly;
        s.Annotation.ScheduleTrees = true;
        s.Annotation.Categories = new List<AnnotationCategory> { AnnotationCategory.Trees, AnnotationCategory.StormSewer };
        var json = JsonConvert.SerializeObject(s);
        Assert.Contains("\"treeAnnotation\":\"TagOnly\"", json);
        var back = JsonConvert.DeserializeObject<FtfSettings>(json)!;
        back.FillMissingSections();
        Assert.Equal(TreeAnnotation.TagOnly, back.Annotation.TreeAnnotation);
        Assert.True(back.Annotation.ScheduleTrees);
        Assert.Equal(new[] { AnnotationCategory.Trees, AnnotationCategory.StormSewer }, back.Annotation.Categories);
    }
}

/// <summary>Settings > Layers: every layer FTF uses, and the project mapping every command applies.</summary>
public sealed class LayerCatalogTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public LayerCatalogTests(RulesFixture fx) => _fx = fx;

    [Fact]
    public void TheCatalogListsRuleAndSettingsLayers()
    {
        var uses = LayerCatalog.Build(_fx.Config, new FtfSettings());
        Assert.Contains(uses, u => u.ObjectType == "Label" && u.Layer == "V-SURF-VEGE-TEXT-E" && u.Source == LayerSource.Rules);
        Assert.Contains(uses, u => u.ObjectType == "Dripline" && u.Source == LayerSource.Rules);
        Assert.Contains(uses, u => u.Feature == "Schedule" && u.ObjectType == "Table" && u.Source == LayerSource.Settings && u.SettingsSection == "Schedule");
        Assert.Contains(uses, u => u.Feature == "Spot Elevations" && u.ObjectType == "Marker");
        Assert.Contains(uses, u => u.Feature == "Utility Dips: Storm" && u.ObjectType == "Pipe");

        // Tags and masks follow each feature's label layer until the office names one.
        var s = new FtfSettings();
        s.Tags.TagLayer = "V-ANNO-TAGS";
        Assert.Contains(LayerCatalog.Build(_fx.Config, s), u => u.Feature == "Tags" && u.ObjectType == "Tag" && u.Layer == "V-ANNO-TAGS");
        Assert.Contains(uses, u => u.Feature == "Contours");
        Assert.Contains(uses, u => u.Feature.StartsWith("Symbol orientation"));
        Assert.All(uses, u => Assert.False(string.IsNullOrWhiteSpace(u.Layer)));
    }

    [Fact]
    public void AMappingSendsTheLayerToTheProjectsAndNowhereElse()
    {
        var map = new LayerMap(new[]
        {
            new LayerMapping { From = "V-UTIL-POWR-TEXT-E", To = "C-POWR-TEXT" },
            new LayerMapping { From = "C-POWR-TEXT", To = "LOOP" }            // never chained
        });
        Assert.Equal("C-POWR-TEXT", map.Apply("v-util-powr-text-e"));
        Assert.Equal("V-SURF-VEGE-E", map.Apply("V-SURF-VEGE-E"));
        Assert.Equal("LOOP", map.Apply("C-POWR-TEXT"));
    }

    [Fact]
    public void SettingAMappingReplacesOrClearsJustThatLayer()
    {
        var list = new List<LayerMapping> { new() { From = "A", To = "B" }, new() { From = "C", To = "D" } };
        LayerMap.Set(list, "a", "E");
        Assert.Equal(new[] { "C", "A" }, list.Select(m => m.From));
        Assert.Equal("E", list.Last().To);
        LayerMap.Set(list, "C", "");
        Assert.Single(list);
        LayerMap.Set(list, "A", "A");      // mapped to itself: no mapping
        Assert.Empty(list);
    }

    [Theory]
    [InlineData("V-UTIL-POWR-TEXT-E", true)]
    [InlineData("", false)]
    [InlineData("BAD:NAME", false)]
    [InlineData(" LEAD", false)]
    public void LayerNamesAreChecked(string name, bool ok)
    {
        Assert.Equal(ok, LayerMap.IsValidName(name, out _));
    }

    [Fact]
    public void StatusSaysWhetherTheLayerWillBeCreated()
    {
        var drawing = new[] { "V-SURF-VEGE-E", "C-POWR-TEXT", "V_UTIL_WATR_TEXT_E" };
        var mappings = new[] { new LayerMapping { From = "V-UTIL-POWR-TEXT-E", To = "C-POWR-TEXT" } };
        Assert.Equal(LayerStatus.Exists, LayerCatalog.StatusOf("V-SURF-VEGE-E", drawing, mappings));
        Assert.Equal(LayerStatus.MappedExists, LayerCatalog.StatusOf("V-UTIL-POWR-TEXT-E", drawing, mappings));
        Assert.Equal(LayerStatus.SpelledDifferently, LayerCatalog.StatusOf("V-UTIL-WATR-TEXT-E", drawing, mappings));
        Assert.Equal(LayerStatus.WillBeCreated, LayerCatalog.StatusOf("Z-NEW", drawing, mappings));
    }
}

/// <summary>The schedule: trees in or out on their own switch, independent of tags.</summary>
public sealed class ScheduleSelectionTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public ScheduleSelectionTests(RulesFixture fx) => _fx = fx;

    private List<ParsedPoint> Points() => new()
    {
        _fx.Parse("CON 18 . 25", "101"), _fx.Parse("CON 12 . 20", "102"), _fx.Parse("PP 1234", "201")
    };

    [Fact]
    public void TreesAreOutOfTheScheduleUnlessItIsOn()
    {
        var tags = new[]
        {
            new FieldCodes.Tagging.TagAssignment { PointNumber = "101", Prefix = "T", Number = 1 },
            new FieldCodes.Tagging.TagAssignment { PointNumber = "201", Prefix = "P", Number = 1 }
        };
        var off = ScheduleSelection.Rows(tags, Points(), new AnnotationSettings { ScheduleTrees = false }, _fx.Config);
        Assert.Equal(new[] { "201" }, off.Select(r => r.PointNumber));

        var on = ScheduleSelection.Rows(tags, Points(), new AnnotationSettings { ScheduleTrees = true, TreeAnnotation = TreeAnnotation.TagOnly }, _fx.Config);
        Assert.Contains(on, r => r.PointNumber == "101" && r.Text == "T1");
    }

    [Fact]
    public void UntaggedTreesAreListedByPointNumber()
    {
        var rows = ScheduleSelection.Rows(new FieldCodes.Tagging.TagAssignment[0], Points(),
            new AnnotationSettings { ScheduleTrees = true, TreeAnnotation = TreeAnnotation.DescriptionOnly }, _fx.Config);
        Assert.Equal(new[] { "101", "102" }, rows.Select(r => r.PointNumber).OrderBy(x => x));
        Assert.All(rows, r => Assert.True(r.Untagged && r.Text == ""));
        var model = FieldCodes.Tagging.TagTable.Build(rows, Points(), null, "TREE SCHEDULE", 0);
        Assert.Equal(2, model.RowCount);
    }
}

/// <summary>A copy someone makes of FTF output is theirs: FTF never re-places or erases it.</summary>
public sealed class StampIdentityTests
{
    [Theory]
    [InlineData("2A3F", "2A3F", false)]   // the entity FTF stamped
    [InlineData("2A3F", "2a3f", false)]
    [InlineData("2A3F", "2B10", true)]    // COPY / paste / ARRAY: new handle, same XData
    [InlineData("", "2B10", false)]       // stamped by an earlier build: still FTF's
    [InlineData(null, "2B10", false)]
    public void ACopyIsTheUsers(string stamped, string actual, bool copy)
    {
        Assert.Equal(copy, FieldCodes.Stamps.StampIdentity.IsCopy(stamped, actual));
    }
}

/// <summary>Point Features editor: a rule's Label All family is edited in rules.json and validated.</summary>
public sealed class RuleCategoryEditingTests
{
    private const string Rules = @"{
  // office comment kept
  ""codes"": [ { ""id"": ""mb"", ""description"": ""Mailbox"", ""match"": ""^(?<code>MB)\\s+(?<number>[0-9]+)$"",
                ""label"": { ""format"": ""MAILBOX {number}"", ""layer"": ""V-SURF-MBOX-TEXT-E"" }, ""unknownThing"": 1 } ] }";

    [Fact]
    public void TheCategoryRoundTripsAndKeepsEverythingElse()
    {
        var doc = FieldCodes.Editing.RuleDocument.FromText(Rules);
        var edit = doc.GetEditable("mb")!;
        Assert.Equal("", edit.Category);
        edit.Category = "Utilities";
        doc.ApplyEdit(edit);
        Assert.Empty(doc.Validate());
        var text = doc.Render();
        Assert.Contains("\"category\": \"Utilities\"", text);
        Assert.Contains("unknownThing", text);
        var cfg = doc.CompileCandidate();
        Assert.Equal("Utilities", cfg.Codes[0].Category);
        Assert.Equal(AnnotationCategory.Utilities,
            AnnotationPolicy.CategoryOf(new FieldCodeParser(cfg).Parse("1", "MB 4"), cfg));
    }

    [Fact]
    public void AnUnknownCategoryIsRefused()
    {
        var doc = FieldCodes.Editing.RuleDocument.FromText(Rules);
        var edit = doc.GetEditable("mb")!;
        edit.Category = "Lines";
        doc.ApplyEdit(edit);
        Assert.Contains(doc.Validate(), p => p.Contains("not a point family"));
    }
}

/// <summary>Line Labels page: a line feature's label text, layer and side are edited in rules.json.</summary>
public sealed class LineFeatureEditingTests
{
    private const string Rules = @"{
  ""codes"": [ { ""id"": ""x"", ""match"": ""^X$"" } ],
  ""lineFeatures"": [ { ""code"": ""FNC"", ""name"": ""Fence"", ""layer"": ""V-SURF-FENC-E"", ""alwaysLabel"": true },
                      { ""code"": ""EP"", ""name"": ""Edge of pavement"", ""layer"": ""V-SURF-ASPH-E"", ""label"": ""EP"" } ] }";

    [Fact]
    public void ALineFeaturesLabelIsEditedAndTheRestKept()
    {
        var doc = FieldCodes.Editing.RuleDocument.FromText(Rules);
        var fence = doc.GetLineFeatures().First(f => f.Code == "FNC");
        Assert.Equal("", fence.Label);       // identified, not labelled yet
        fence.Label = "CHAIN LINK FENCE";
        fence.LabelLayer = "V-SURF-FENC-TEXT-E";
        fence.Placement = "Left";
        doc.ApplyLineFeature(fence);
        Assert.Empty(doc.Validate());
        var cfg = doc.CompileCandidate();
        var f = cfg.LineFeatures.First(x => x.Code == "FNC");
        Assert.Equal("CHAIN LINK FENCE", f.Label);
        Assert.Equal("Left", f.Placement);
        Assert.Contains("alwaysLabel", doc.Render());

        fence.Placement = "Auto";
        doc.ApplyLineFeature(fence);
        Assert.Null(doc.CompileCandidate().LineFeatures.First(x => x.Code == "FNC").Placement);
    }

    [Fact]
    public void ABadSideIsRefused()
    {
        var doc = FieldCodes.Editing.RuleDocument.FromText(Rules);
        var ep = doc.GetLineFeatures().First(f => f.Code == "EP");
        ep.Placement = "Upside";
        doc.ApplyLineFeature(ep);
        Assert.Contains(doc.Validate(), p => p.Contains("side must be"));
    }
}
