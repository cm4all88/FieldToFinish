using FieldCodes;
using FieldCodes.Settings;
using FieldCodes.Standards;

namespace FieldCodes.Tests;

/// <summary>Check Standards: what the settings and rules name that a drawing does not have, how
/// bad it is, and which fix is offered -- never applied by the check itself.</summary>
public sealed class StandardsCheckTests
{
    private static RulesConfig Rules()
    {
        var rules = new RulesConfig();
        rules.Codes.Add(new CodeRule
        {
            Id = "sym", Description = "Symbol", InsertBlock = true, Block = "SYM-BLK", BlockLayer = "V-SYMB-E",
            Label = new LabelRule { Layer = "V-SYMB-TEXT-E" }
        });
        rules.Codes.Add(new CodeRule { Id = "sign", InsertBlock = true, Block = "SIGN-{type}", BlockLayer = "V-SIGN-E" });  // expanded per point: not checked
        return rules;
    }

    /// <summary>A drawing that has every layer and name a default settings file and the rules above use,
    /// so each test only sees what it breaks.</summary>
    private static DrawingNames Complete(RulesConfig rules, FtfSettings settings)
    {
        var layers = LayerCatalog.EffectiveLayers(LayerCatalog.Build(rules, settings, new string[0]), settings.General.LayerMappings).ToList();
        layers.Add("0");
        return new DrawingNames
        {
            Layers = layers,
            Linetypes = new List<string> { "ByBlock", "ByLayer", "Continuous", "HIDDEN2" },
            TextStyles = new List<string> { "Standard", "PMX-Text", "Annotative", "Survey" },
            MultileaderStyles = new List<string> { "Standard", "xPMX SURV Text Arrow Anno" },
            TableStyles = new List<string> { "Standard" },
            DimensionStyles = new List<string> { "Standard" },
            LineLabelStyles = new List<string> { "Bearing over Distance" },
            CurveLabelStyles = new List<string> { "Delta over Length and Radius" },
            Blocks = new List<string> { "SYM-BLK", "SSV-SNST", "SSV-SN-STOP", "SSV-WFH", "SSV-WVL", "SSV-GV", "SSV-LTARM", "SSV-LT", "SSV-TSP", "SSV-ASTO" },
            LoadableLinetypes = new List<string> { "Continuous", "DASHED", "DASHED2", "HIDDEN", "HIDDEN2" }
        };
    }

    [Fact]
    public void ADrawingWithEverythingHasNothingToReport()
    {
        var rules = Rules();
        var settings = new FtfSettings();
        var issues = StandardsCheck.Run(rules, settings, Complete(rules, settings));
        Assert.Empty(issues);
        Assert.StartsWith("Everything", StandardsCheck.Summary(issues));
    }

    [Fact]
    public void AnnotationStylesAreCheckedForWhatTheyNameInTheDrawingOnly()
    {
        var rules = Rules();
        var settings = new FtfSettings();
        settings.Styles.Text.Add(new FieldCodes.Styles.TextStyleDef { Name = "Utility", TextStyle = "L80" });
        settings.Styles.Leaders.Add(new FieldCodes.Styles.LeaderStyleDef { Name = "Dot", ArrowBlock = "_Dot", Linetype = "ByLayer", MLeaderStyle = "Standard" });
        settings.Styles.Assignments.Add(new FieldCodes.Styles.StyleAssignment { Kind = FieldCodes.Styles.StyledKind.Label, Category = "Utilities", TextStyle = "Utility", LeaderStyle = "Dot" });

        var issues = StandardsCheck.Run(rules, settings, Complete(rules, settings));

        // The drawing text style the named style uses is missing; the assignment's names are FTF's
        // own styles, _Dot is AutoCAD's arrow and ByLayer is not a linetype: none of those is an issue.
        var only = Assert.Single(issues);
        Assert.Equal(StandardsKind.TextStyle, only.Kind);
        Assert.Equal("L80", only.Name);
    }

    [Fact]
    public void AMissingLayerIsCreatedOnFirstUseOrSentToASimilarOne()
    {
        var rules = Rules();
        var settings = new FtfSettings();
        var drawing = Complete(rules, settings);
        drawing.Layers = drawing.Layers!.Where(l => l != "V-SYMB-E" && l != "V-SYMB-TEXT-E" && l != "V-SURF-LAND-E").Concat(new[] { "V-SYMB-TEXT", "V_SURF_LAND_E" }).ToList();
        settings.LayerStandards.Layers.Add(new LayerStandard { Name = "V-SYMB-E", Color = "4" });

        var issues = StandardsCheck.Run(rules, settings, drawing);

        var created = issues.Single(i => i.Kind == StandardsKind.Layer && i.Name == "V-SYMB-E");
        Assert.Equal(Severity.Info, created.Severity);
        Assert.Equal(StandardsFix.CreateLayer, created.Fix);
        Assert.Contains("with its layer standard", created.Problem);
        Assert.Contains("Symbol (sym)", created.What);

        var similar = issues.Single(i => i.Kind == StandardsKind.Layer && i.Name == "V-SYMB-TEXT-E");
        Assert.Equal(Severity.Warning, similar.Severity);
        Assert.Equal(StandardsFix.MapLayer, similar.Fix);
        Assert.Equal(new[] { "V-SYMB-TEXT" }, similar.Candidates.ToArray());
        Assert.Equal("V-SYMB-TEXT-E", similar.ConfiguredLayer);

        var spelled = issues.Single(i => i.Kind == StandardsKind.Layer && i.Name == "V-SURF-LAND-E");
        Assert.Equal(StandardsFix.MapLayer, spelled.Fix);
        Assert.Equal("V_SURF_LAND_E", spelled.Candidates.Single());
    }

    [Fact]
    public void AMappedLayerIsCheckedUnderItsProjectName()
    {
        var rules = Rules();
        var settings = new FtfSettings();
        var drawing = Complete(rules, settings);
        settings.General.LayerMappings.Add(new LayerMapping { From = "V-SYMB-E", To = "C-SYMB" });
        Assert.Contains(StandardsCheck.Run(rules, settings, drawing), i => i.Kind == StandardsKind.Layer && i.Name == "C-SYMB" && i.ConfiguredLayer == "V-SYMB-E");
        drawing.Layers!.Add("C-SYMB");
        Assert.DoesNotContain(StandardsCheck.Run(rules, settings, drawing), i => i.Kind == StandardsKind.Layer);
    }

    [Fact]
    public void AMissingTextStyleOffersTheDrawingsStylesAndWritesTheChoiceToEverySettingThatNamedIt()
    {
        var rules = Rules();
        var settings = new FtfSettings();
        settings.Labels.TextStyle = "PMX-Txt";
        settings.Tags.TagTextStyle = "pmx-txt";
        settings.Dips.LeaderStyle = "Callout";
        var drawing = Complete(rules, settings);

        var issues = StandardsCheck.Run(rules, settings, drawing);
        var text = issues.Single(i => i.Kind == StandardsKind.TextStyle);
        Assert.Equal(Severity.Warning, text.Severity);
        Assert.Equal(StandardsFix.ChooseExisting, text.Fix);
        Assert.Equal("PMX-Text", text.Candidates[0]);                       // likeliest first
        Assert.Contains("Settings > Point Labels: Text style", text.What);
        Assert.Contains("Settings > Tags: Tag text style", text.What);
        Assert.Contains(text.Choices, c => c.StartsWith("Choose a text style"));

        // Nothing changed until the fix is chosen...
        Assert.Equal("PMX-Txt", settings.Labels.TextStyle);
        text.Setting!.Write("PMX-Text");
        Assert.Equal("PMX-Text", settings.Labels.TextStyle);
        Assert.Equal("PMX-Text", settings.Tags.TagTextStyle);

        var leader = issues.Single(i => i.Kind == StandardsKind.MultileaderStyle);
        Assert.Equal("Callout", leader.Name);
        Assert.Contains("Utility Dips", leader.What);
    }

    [Fact]
    public void AKindTheDrawingWasNotReadForIsNotChecked()
    {
        var rules = Rules();
        var settings = new FtfSettings();
        settings.Dips.LeaderStyle = "Callout";
        settings.Exhibits.TableStyle = "Nope";
        var drawing = Complete(rules, settings);
        drawing.MultileaderStyles = null;
        drawing.TableStyles = new List<string>();
        var issues = StandardsCheck.Run(rules, settings, drawing);
        Assert.DoesNotContain(issues, i => i.Kind == StandardsKind.MultileaderStyle);
        var table = issues.Single(i => i.Kind == StandardsKind.TableStyle);
        Assert.Equal(StandardsFix.None, table.Fix);                           // nothing in the drawing to choose
    }

    [Fact]
    public void ALinetypeIsLoadedFromAcadLinOrReportedWhenNothingHasIt()
    {
        var rules = Rules();
        var settings = new FtfSettings();
        settings.Drip.Linetype = "DASHED2";
        settings.LayerStandards.Layers.Add(new LayerStandard { Name = "V-SYMB-E", Linetype = "OFFICE-FENCE" });
        settings.LayerStandards.Layers.Add(new LayerStandard { Name = "V-SYMB-TEXT-E", Linetype = "office-fence" });
        var drawing = Complete(rules, settings);

        var issues = StandardsCheck.Run(rules, settings, drawing);
        var drip = issues.Single(i => i.Kind == StandardsKind.Linetype && i.Name == "DASHED2");
        Assert.Equal(Severity.Info, drip.Severity);
        Assert.Equal(StandardsFix.LoadLinetype, drip.Fix);
        Assert.Contains("Settings > Driplines", drip.What);

        var fence = issues.Single(i => i.Kind == StandardsKind.Linetype && i.Name.Equals("OFFICE-FENCE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Severity.Error, fence.Severity);
        Assert.Equal(StandardsFix.ChooseExisting, fence.Fix);
        Assert.Contains("HIDDEN2", fence.Candidates);
        fence.Setting!.Write("HIDDEN2");                                     // both standards that named it
        Assert.All(settings.LayerStandards.Layers, s => Assert.Equal("HIDDEN2", s.Linetype));

        drawing.LoadableLinetypes = null;                                    // acad.lin could not be read
        var unknown = StandardsCheck.Run(rules, settings, drawing).Single(i => i.Kind == StandardsKind.Linetype);
        Assert.Equal("DASHED2", unknown.Name);
        Assert.Equal(Severity.Warning, unknown.Severity);
        Assert.Equal(StandardsFix.LoadLinetype, unknown.Fix);
    }

    [Fact]
    public void AnInvalidOrUnusedLayerStandardIsReported()
    {
        var rules = Rules();
        var settings = new FtfSettings();
        settings.LayerStandards.Layers.Add(new LayerStandard { Name = "V-SYMB-E", LineWeight = "0.33" });
        settings.LayerStandards.Layers.Add(new LayerStandard { Name = "V-TYPO-E", Color = "3" });
        settings.LayerStandards.Layers.Add(new LayerStandard { Name = "Q-*", Plot = false });
        settings.LayerStandards.Layers.Add(new LayerStandard { Name = "V-SYMB-*", Plot = true });
        var issues = StandardsCheck.Run(rules, settings, Complete(rules, settings));

        var invalid = issues.First();
        Assert.Equal(Severity.Error, invalid.Severity);                      // errors first
        Assert.Equal(StandardsKind.LayerStandard, invalid.Kind);
        Assert.Contains("0.33", invalid.Problem);
        Assert.Contains(issues, i => i.Kind == StandardsKind.LayerStandard && i.Name == "V-TYPO-E" && i.Severity == Severity.Info);
        Assert.Contains(issues, i => i.Kind == StandardsKind.LayerStandard && i.Name == "Q-*" && i.Problem.StartsWith("Matches none"));
        Assert.DoesNotContain(issues, i => i.Name == "V-SYMB-*");
    }

    [Fact]
    public void AMissingRuleBlockIsAnErrorWithNoSubstitute()
    {
        var rules = Rules();
        var settings = new FtfSettings();
        var drawing = Complete(rules, settings);
        drawing.Blocks = drawing.Blocks!.Where(b => b != "SYM-BLK").Concat(new[] { "SYM-BLK2" }).ToList();
        var block = StandardsCheck.Run(rules, settings, drawing).Single(i => i.Kind == StandardsKind.Block && i.Name == "SYM-BLK");
        Assert.Equal(Severity.Error, block.Severity);
        Assert.Equal(StandardsFix.None, block.Fix);
        Assert.Contains("rules.json: code sym", block.What);
        Assert.Equal("SYM-BLK2", block.Candidates[0]);
        Assert.Contains("no symbol", block.Problem);
    }

    [Fact]
    public void AnOrientationBlockNoBlockMatchesIsANote()
    {
        var rules = Rules();
        var settings = new FtfSettings();
        var drawing = Complete(rules, settings);
        drawing.Blocks = new List<string> { "SYM-BLK", "SSV-SN-STOP" };
        var issues = StandardsCheck.Run(rules, settings, drawing).Where(i => i.Kind == StandardsKind.Block).ToList();
        Assert.All(issues, i => Assert.Equal(Severity.Info, i.Severity));
        Assert.DoesNotContain(issues, i => i.Name == "SSV-SN*");          // matched by SSV-SN-STOP
        Assert.Contains(issues, i => i.Name == "SSV-WFH" && i.What.Contains("WFH"));
        Assert.DoesNotContain(issues, i => i.Name.StartsWith("("));        // "(label)" is not a block
    }

    [Fact]
    public void SettingsBlocksAndCivilLabelStylesAreChecked()
    {
        var rules = Rules();
        var settings = new FtfSettings();
        settings.Exhibits.NorthArrowBlock = "NORTH-ARROW";
        settings.RecordSurvey.Entities[0].LineLabelStyle = "Bearing Distance";
        var issues = StandardsCheck.Run(rules, settings, Complete(rules, settings));
        var north = issues.Single(i => i.Kind == StandardsKind.Block && i.Name == "NORTH-ARROW");
        Assert.Equal(StandardsFix.ChooseExisting, north.Fix);
        Assert.Contains("Easement Exhibits", north.What);
        var label = issues.Single(i => i.Kind == StandardsKind.LineLabelStyle);
        Assert.Contains("Recorded Surveys: Boundary", label.What);
        Assert.Equal("Bearing over Distance", label.Candidates.Single());
    }

    [Fact]
    public void WithoutRulesTheSettingsAreStillChecked()
    {
        var settings = new FtfSettings();
        settings.Labels.TextStyle = "Missing";
        var issues = StandardsCheck.Run(null, settings, new DrawingNames { TextStyles = new List<string> { "Standard", "Survey" } });
        Assert.Single(issues);
        Assert.Equal(StandardsKind.TextStyle, issues[0].Kind);
    }

    [Theory]
    [InlineData("TextStyle", StandardsKind.TextStyle)]
    [InlineData("TagTextStyle", StandardsKind.TextStyle)]
    [InlineData("TitleTextStyle", StandardsKind.TextStyle)]
    [InlineData("LeaderStyle", StandardsKind.MultileaderStyle)]
    [InlineData("TableStyle", StandardsKind.TableStyle)]
    [InlineData("DimensionStyle", StandardsKind.DimensionStyle)]
    [InlineData("LineLabelStyle", StandardsKind.LineLabelStyle)]
    [InlineData("CurveLabelStyle", StandardsKind.CurveLabelStyle)]
    [InlineData("Linetype", StandardsKind.Linetype)]
    [InlineData("NorthArrowBlock", StandardsKind.Block)]
    public void ASettingsFieldIsCheckedByItsName(string property, StandardsKind kind)
    {
        Assert.Equal(kind, StandardsCheck.KindOf(property));
    }

    [Fact]
    public void OtherFieldsAreNotNames()
    {
        Assert.Null(StandardsCheck.KindOf("LineTypeName"));   // a Drafting Lines type, not a linetype
        Assert.Null(StandardsCheck.KindOf("TableTitle"));
        Assert.Null(StandardsCheck.KindOf("Layer"));
    }

    [Fact]
    public void ALinFileListsItsLinetypes()
    {
        var names = StandardsCheck.LinetypesIn(new[]
        {
            ";; AutoCAD Linetype Definition file",
            "*BORDER,Border __ __ . __ __ . __ __ . __ __ . __ __ .",
            "A,.5,-.25,.5,-.25,0,-.25",
            "*DASHED,Dashed __ __ __",
            "*dashed,again",
            "  *HIDDEN2 ,Hidden (.5x)",
            "*"
        });
        Assert.Equal(new[] { "BORDER", "DASHED", "HIDDEN2" }, names.ToArray());
    }

    [Fact]
    public void SimilarNamesComeFirst()
    {
        var similar = StandardsCheck.Similar("PMX Text", new[] { "Annotative", "PMX-TEXT", "PMX-Title", "Standard" });
        Assert.Equal("PMX-TEXT", similar[0]);
        Assert.Equal("PMX-Title", similar[1]);
        Assert.Equal(4, similar.Count);
    }
}

/// <summary>Settings > Layers lists every kind of object FTF draws, so each can be sent to another
/// layer and given a standard.</summary>
public sealed class LayerCatalogCoverageTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public LayerCatalogCoverageTests(RulesFixture fx) => _fx = fx;

    [Fact]
    public void EveryFamilyOfObjectIsInTheTable()
    {
        var uses = LayerCatalog.Build(_fx.Config, new FtfSettings());
        foreach (LayerFamily family in Enum.GetValues(typeof(LayerFamily)))
            Assert.True(uses.Any(u => u.Family == family), LayerCatalog.FamilyName(family) + " has no layer in the table.");
        Assert.All(Enum.GetValues(typeof(LayerFamily)).Cast<LayerFamily>(), f => Assert.False(string.IsNullOrWhiteSpace(LayerCatalog.FamilyName(f))));
    }

    [Fact]
    public void TreesHaveLabelsTagsDriplinesAndLeaders()
    {
        var uses = LayerCatalog.Build(_fx.Config, new FtfSettings());
        var tree = uses.Where(u => u.Feature == "Tree (tree)").ToList();
        Assert.Contains(tree, u => u.ObjectType == "Label" && u.Family == LayerFamily.Trees);
        Assert.Contains(tree, u => u.ObjectType == "Dripline" && u.Family == LayerFamily.Trees);
        Assert.Contains(tree, u => u.ObjectType.StartsWith("Tag, on the label layer") && u.Layer == "V-SURF-VEGE-TEXT-E" && u.Family == LayerFamily.Trees);
        Assert.Contains(tree, u => u.ObjectType == LayerCatalog.LeaderOnLabelLayer && u.Layer == "V-SURF-VEGE-TEXT-E" && u.Family == LayerFamily.Leaders);

        // Tags on a layer of their own: the per-feature tag rows give way to the Tags row.
        var s = new FtfSettings();
        s.Tags.TagLayer = "V-ANNO-TAGS";
        var named = LayerCatalog.Build(_fx.Config, s);
        Assert.Contains(named, u => u.Feature == "Tags" && u.ObjectType == "Tag" && u.Family == LayerFamily.Trees);
        Assert.Contains(named, u => u.Feature == "Tags" && u.ObjectType.StartsWith("Tag leader") && u.Layer == "V-ANNO-TAGS");
        Assert.DoesNotContain(named, u => u.ObjectType.StartsWith("Tag, on the label layer"));
    }

    [Fact]
    public void AnInsertedSymbolIsASymbolUnlessItIsATree()
    {
        var rules = new RulesConfig();
        rules.Codes.Add(new CodeRule { Id = "hydrant", Description = "Fire Hydrant", InsertBlock = true, Block = "WFH", BlockLayer = "V-UTIL-WATR-SYMB-E" });
        rules.Codes.Add(new CodeRule { Id = "tree", Description = "Tree", InsertBlock = true, Block = "TREE", BlockLayer = "V-SURF-VEGE-SYMB-E" });
        var uses = LayerCatalog.Build(rules, null);
        Assert.Equal(LayerFamily.Symbols, uses.Single(u => u.Layer == "V-UTIL-WATR-SYMB-E").Family);
        Assert.Equal(LayerFamily.Trees, uses.Single(u => u.Layer == "V-SURF-VEGE-SYMB-E").Family);
    }

    [Fact]
    public void ControlLabelsTablesAndTheLegendAreListed()
    {
        var uses = LayerCatalog.Build(_fx.Config, new FtfSettings());
        Assert.Contains(uses, u => u.Feature == "Survey Control (control)" && u.ObjectType == "Label" && u.Family == LayerFamily.SurveyControl);
        var control = uses.Single(u => u.Feature == LayerCatalog.ControlTableFeature);
        Assert.Equal(LayerSource.BuiltIn, control.Source);
        Assert.Equal(PmxLayers.TableLayer, control.Layer);
        Assert.Equal(LayerFamily.TablesAndLegends, control.Family);
        Assert.Contains(uses, u => u.Feature.StartsWith("Legend") && u.Family == LayerFamily.TablesAndLegends);
        Assert.Contains(uses, u => u.Feature == "Recorded Surveys: Found" && u.ObjectType == "Monument symbol" && u.Family == LayerFamily.SurveyControl);
        Assert.Contains(uses, u => u.Feature == "Strip Easements" && u.ObjectType == "Table" && u.Family == LayerFamily.TablesAndLegends);
        Assert.Contains(uses, u => u.Feature == "Strip Easements" && u.ObjectType == "Hatch" && u.Family == LayerFamily.EasementAndBoundary);
        Assert.Contains(uses, u => u.Feature == "Utility Dips: Storm" && u.ObjectType.StartsWith("Structure callout leader") && u.Family == LayerFamily.Leaders);
    }

    [Fact]
    public void ALineLabelGoesOnTheDrawingsTextLayerWhenItHasOne()
    {
        var plain = LayerCatalog.Build(_fx.Config, new FtfSettings());
        var stripe = plain.Single(u => u.Feature == "Line Stripe (LNSO)");
        Assert.Equal("V-TEXT-E", stripe.Layer);
        Assert.False(stripe.OnlyIfPresent);
        Assert.Equal(LayerFamily.PavementMarkings, stripe.Family);

        var withText = LayerCatalog.Build(_fx.Config, new FtfSettings(), new[] { "V-CHAN-STRP-TEXT-E", "V-SURF-FENC-TEXT-E" });
        var derived = withText.Single(u => u.Feature == "Line Stripe (LNSO)");
        Assert.Equal("V-CHAN-STRP-TEXT-E", derived.Layer);
        Assert.True(derived.OnlyIfPresent);
        Assert.Equal(LayerFamily.LineworkLabels, withText.Single(u => u.Feature == "Line Fence (FNC)").Family);
    }

    [Fact]
    public void AModifierLayerIsListedOnlyForRulesThatTakeModifiers()
    {
        var uses = LayerCatalog.Build(_fx.Config, new FtfSettings());
        var dead = uses.Single(u => u.Layer == "V-SURF-VEGE-TEXT-E-DEAD" && u.Feature == "Tree (tree)");
        Assert.Equal("dead", dead.Modifier);
        Assert.Equal(LayerFamily.Trees, dead.Family);
        Assert.Contains(uses, u => u.Layer == "V-SURF-VEGE-E-RMV" && u.Modifier == "remove");
        Assert.DoesNotContain(uses, u => u.Feature == "Survey Control (control)" && u.Modifier != null);   // its pattern has no modifiers
    }

    [Fact]
    public void LayerStandardsAreNotLayersFtfDrawsOn()
    {
        var s = new FtfSettings();
        s.LayerStandards.Layers.Add(new LayerStandard { Name = "V-ZZZZ-E", Color = "1" });
        Assert.DoesNotContain(LayerCatalog.Build(_fx.Config, s), u => u.Layer == "V-ZZZZ-E");
    }
}
