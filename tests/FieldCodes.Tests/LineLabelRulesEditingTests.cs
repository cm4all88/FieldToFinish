using FieldCodes.Editing;
using FieldCodes.Linework;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Tests;

/// <summary>
/// Settings > Line Labels: the rules file's part of line labelling -- every field of every line
/// feature (with adding and removing one), the span markers (GATE), the TBC names mapped to nothing
/// and the stairs label. Read with the built-in value for anything the file leaves out, written
/// only where it changed (the rest of each entry kept), checked field by field, and reaching the
/// rules the commands read.
/// </summary>
public sealed class LineLabelRulesEditingTests
{
    private static readonly string RulesPath = Path.Combine(AppContext.BaseDirectory, "rules.json");

    private static JObject Shipped() => JObject.Parse(File.ReadAllText(RulesPath));

    private static RuleDocument With(Action<JObject> change)
    {
        var root = Shipped();
        change(root);
        return RuleDocument.FromText(root.ToString());
    }

    private static JObject Feature(JObject root, string code) =>
        ((JArray)root["lineFeatures"]!).OfType<JObject>().Single(f => (string?)f["code"] == code);

    private static EditableLineFeature Get(RuleDocument doc, string code) => doc.GetLineFeatures().Single(f => f.Code == code);

    // ------------------------------------------------------------------ what is shown

    [Fact]
    public void TheShippedRulesShowEveryFieldOfEveryLineFeature()
    {
        var features = RuleDocument.Load(RulesPath).GetLineFeatures();
        Assert.Equal(41, features.Count);
        Assert.Equal(Enumerable.Range(0, 41), features.Select(f => f.SourceIndex));

        var ep = features.Single(f => f.Code == "EP");
        Assert.Equal("Asphalt Edge", ep.Name);
        Assert.Equal("V-SURF-ASPH-E", ep.Layer);
        Assert.Equal("EDGE OF PAVEMENT", ep.Label);
        Assert.Equal(new[] { "EOP" }, ep.AltLabels);
        Assert.Equal(new[] { "ASPHALT", "ASPH" }, ep.BetweenLabels);
        Assert.Empty(ep.Aliases);
        Assert.True(ep.Enabled);
        Assert.False(ep.AlwaysLabel);
        Assert.Null(ep.ReadHeight);
        Assert.Equal("", ep.Placement);

        var fence = features.Single(f => f.Code == "FCK");
        Assert.True(fence.AlwaysLabel);
        Assert.Equal("CHAIN LINK FENCE", fence.Label);
        Assert.Equal("", features.Single(f => f.Code == "HGL").Layer);     // identified by name and shots only
    }

    [Fact]
    public void TheShippedRulesShowTheStairsLabelTheGateMarkerAndNoIgnoredNames()
    {
        var r = RuleDocument.Load(RulesPath).GetLineLabelRules();
        Assert.Equal("{count} STEPS", r.StairsLabelFormat);
        var gate = Assert.Single(r.SpanMarkers);
        Assert.Equal("FGP", gate.Token);
        Assert.Equal("GATE", gate.Label);
        Assert.True(gate.Enabled);
        Assert.Equal("GATE", gate.Shows);
        Assert.Empty(r.IgnoreLineNames);
    }

    [Fact]
    public void AFileThatSaysNothingMeansWhatTheCommandsDoWithoutItAndWritesNothing()
    {
        var builtIn = new RulesConfig();
        var d = EditableLineLabelRules.Defaults();
        Assert.Equal(builtIn.StairsLabelFormat, d.StairsLabelFormat);
        Assert.Equal(builtIn.SpanMarkers.Count, d.SpanMarkers.Count);
        Assert.Equal(builtIn.IgnoreLineNames, d.IgnoreLineNames);

        var doc = With(root =>
        {
            root.Remove("stairsLabelFormat");
            root.Remove("spanMarkers");
            root.Remove("ignoreLineNames");
        });
        var r = doc.GetLineLabelRules();
        Assert.Equal("{count} STEPS", r.StairsLabelFormat);
        Assert.Empty(r.SpanMarkers);
        var text = doc.Render();
        doc.ApplyLineLabelRules(r);
        Assert.Equal(text, doc.Render());
        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void NothingChangedWritesNothing()
    {
        var doc = RuleDocument.Load(RulesPath);
        var text = doc.Render();
        foreach (var f in doc.GetLineFeatures()) doc.ApplyLineFeature(f);
        doc.ApplyLineLabelRules(doc.GetLineLabelRules());
        Assert.Equal(text, doc.Render());
        Assert.Empty(doc.Validate());
    }

    // ------------------------------------------------------------------ line features

    [Fact]
    public void EveryFieldOfALineFeatureIsEditedAndReachesTheRulesTheCommandsRead()
    {
        var doc = RuleDocument.Load(RulesPath);
        var ec = Get(doc, "EC");
        ec.Name = "Concrete Edge (walk)";
        ec.Layer = "V-SURF-CONC-WALK-E";
        ec.Label = "EDGE OF CONC";
        ec.LabelLayer = "V-SURF-CONC-TEXT-E";
        ec.Placement = "Left";
        ec.Aliases = EditableLineFeature.SplitLines("Edge of Conc\r\n\r\n  edge of conc \nCONC EDGE");
        ec.AltLabels = new List<string> { "EOC", " CONC " };
        ec.BetweenLabels = new List<string> { "SIDEWALK", "SW" };
        ec.Enabled = false;
        ec.AlwaysLabel = true;
        ec.ReadHeight = false;
        doc.ApplyLineFeature(ec);
        Assert.Empty(doc.Validate());

        var rule = doc.CompileCandidate().LineFeatures.Single(f => f.Code == "EC");
        Assert.Equal("Concrete Edge (walk)", rule.Name);
        Assert.Equal("V-SURF-CONC-WALK-E", rule.Layer);
        Assert.Equal("EDGE OF CONC", rule.Label);
        Assert.Equal("V-SURF-CONC-TEXT-E", rule.LabelLayer);
        Assert.Equal("Left", rule.Placement);
        Assert.Equal(new[] { "Edge of Conc", "CONC EDGE" }, rule.Aliases);      // blanks and repeats dropped
        Assert.Equal(new[] { "EOC", "CONC" }, rule.AltLabels);
        Assert.Equal(new[] { "SIDEWALK", "SW" }, rule.BetweenLabels);
        Assert.False(rule.Enabled);
        Assert.True(rule.AlwaysLabel);
        Assert.False(rule.ReadHeight);

        // What the commands make of it: the TBC name identifies EC, the layer finds it, and turned
        // off it is never labelled.
        var catalog = new LineworkCatalog(doc.CompileCandidate().LineFeatures);
        Assert.Equal("EC", Assert.Single(catalog.FindByTrimbleName("edge of conc")).Code);
        Assert.Contains(catalog.FindByLayer("V-SURF-CONC-WALK-E"), f => f.Code == "EC");
        Assert.Null(LineworkCatalog.UnanimousLabel(new[] { rule }));

        // Put back: on, automatic heights, the side back to Auto.
        ec = Get(doc, "EC");
        ec.Enabled = true;
        ec.ReadHeight = null;
        ec.Placement = "Auto";
        doc.ApplyLineFeature(ec);
        var entry = Feature(JObject.Parse(doc.Render()), "EC");
        Assert.Null(entry["enabled"]);
        Assert.Null(entry["readHeight"]);
        Assert.Null(entry["placement"]);
        rule = doc.CompileCandidate().LineFeatures.Single(f => f.Code == "EC");
        Assert.Equal(new[] { "EDGE OF CONC", "EOC", "CONC" }, LineworkCatalog.LabelChoices(new[] { rule }));
        Assert.Equal(new[] { "SIDEWALK", "SW" }, LineworkCatalog.BetweenChoices(new[] { rule }));
    }

    [Fact]
    public void AnEditChangesOnlyThatFeatureAndKeepsTheRestOfItsEntry()
    {
        var doc = RuleDocument.Load(RulesPath);
        var before = JObject.Parse(doc.Render());
        var hgl = Get(doc, "HGL");
        hgl.Label = "HEDGE ROW";
        doc.ApplyLineFeature(hgl);

        var after = JObject.Parse(doc.Render());
        var entry = Feature(after, "HGL");
        Assert.Equal("HEDGE ROW", (string?)entry["label"]);
        Assert.NotNull(entry["//layer"]);                       // the entry's own note is kept
        Assert.Null(entry["layer"]);                            // and nothing is added to it
        var beforeList = (JArray)before["lineFeatures"]!;
        var afterList = (JArray)after["lineFeatures"]!;
        for (var i = 0; i < beforeList.Count; i++)
            if ((string?)beforeList[i]["code"] != "HGL")
                Assert.True(JToken.DeepEquals(beforeList[i], afterList[i]), (string?)beforeList[i]["code"] + " changed");
        foreach (var p in before.Properties().Where(p => p.Name != "lineFeatures"))
            Assert.True(JToken.DeepEquals(p.Value, after[p.Name]), p.Name + " changed");
    }

    [Fact]
    public void ACodeCanBeChangedAndTheEntryKeepsEverythingElse()
    {
        var doc = RuleDocument.Load(RulesPath);
        var fnc = Get(doc, "FNC");
        fnc.Code = "FN";
        doc.ApplyLineFeature(fnc);
        Assert.Empty(doc.Validate());
        var rule = doc.CompileCandidate().LineFeatures.Single(f => f.Code == "FN");
        Assert.Equal("FENCE", rule.Label);
        Assert.True(rule.AlwaysLabel);
        Assert.DoesNotContain(doc.CompileCandidate().LineFeatures, f => f.Code == "FNC");
        Assert.Equal("FN", new LineworkCatalog(doc.CompileCandidate().LineFeatures).FindByFigureName("FN3")!.Code);
    }

    [Fact]
    public void ALineFeatureIsAddedAtTheEndWithItsCodeInCapitals()
    {
        var doc = RuleDocument.Load(RulesPath);
        var added = doc.AddLineFeature(new EditableLineFeature
        {
            Code = " rwg ", Name = "Gabion Wall", Layer = "V-SURF-WALL-GABN-E", Label = "GABION WALL",
            Aliases = new List<string> { "Gabion" }
        });
        Assert.Equal("RWG", added.Code);
        Assert.Equal(41, added.SourceIndex);
        Assert.True(added.Enabled);
        Assert.Empty(doc.Validate());

        var entry = (JObject)((JArray)JObject.Parse(doc.Render())["lineFeatures"]!).Last!;
        Assert.Equal(new[] { "code", "name", "layer", "label", "aliases" }, entry.Properties().Select(p => p.Name));

        var catalog = new LineworkCatalog(doc.CompileCandidate().LineFeatures);
        Assert.Equal("GABION WALL", catalog.FindByFigureName("RWG2")!.Label);
        Assert.Equal("RWG", Assert.Single(catalog.FindByTrimbleName("GABION")).Code);
    }

    [Theory]
    [InlineData("fnc", "already a line feature with the code FNC")]
    [InlineData("  ", "needs a code")]
    [InlineData("RW G", "is not one code")]
    [InlineData("RWG2", "ends in a number")]
    public void ALineFeatureWhoseCodeCannotWorkIsNotAdded(string code, string expected)
    {
        var doc = RuleDocument.Load(RulesPath);
        var text = doc.Render();
        var ex = Assert.Throws<ConfigException>(() => doc.AddLineFeature(new EditableLineFeature { Code = code }));
        Assert.Contains(expected, ex.Message);
        Assert.Equal(text, doc.Render());
    }

    [Fact]
    public void AFileWithoutLineFeaturesGetsItsFirstOne()
    {
        var doc = With(root => root.Remove("lineFeatures"));
        Assert.Empty(doc.GetLineFeatures());
        doc.AddLineFeature(new EditableLineFeature { Code = "FNC", Label = "FENCE" });
        Assert.Equal("FENCE", Assert.Single(doc.CompileCandidate().LineFeatures).Label);
    }

    [Fact]
    public void ALineFeatureIsRemovedAndTheOthersAreKept()
    {
        var doc = RuleDocument.Load(RulesPath);
        var before = doc.GetLineFeatures();
        Assert.True(doc.RemoveLineFeature(Get(doc, "EP")));
        Assert.False(doc.RemoveLineFeature(new EditableLineFeature { Code = "NOPE" }));
        Assert.Empty(doc.Validate());

        var after = doc.CompileCandidate().LineFeatures;
        Assert.Equal(before.Where(f => f.Code != "EP").Select(f => f.Code), after.Select(f => f.Code));
        Assert.Equal(new[] { "CONC WALK", "CW" }, after.Single(f => f.Code == "EC").BetweenLabels);
        Assert.Null(new LineworkCatalog(after).FindByFigureName("EP1"));
    }

    [Fact]
    public void TwoFeaturesWithOneCodeAreToldApartByTheirPlace()
    {
        // A hand-edited file with a code twice: each entry is still edited on its own (and the
        // check says to give each its own code).
        var doc = With(root => ((JArray)root["lineFeatures"]!).Add(JObject.Parse("{ \"code\": \"CU\", \"name\": \"Curb (second)\" }")));
        var second = doc.GetLineFeatures().Last();
        second.Code = "CUB";
        doc.ApplyLineFeature(second);
        Assert.Empty(doc.Validate());
        var features = doc.CompileCandidate().LineFeatures;
        Assert.Equal("Curb", features.Single(f => f.Code == "CU").Name);
        Assert.Equal("Curb (second)", features.Single(f => f.Code == "CUB").Name);
    }

    [Theory]
    [InlineData("FHW", "code", "\"FNC\"", "the code FNC is given to 2 line features")]
    [InlineData("FHW", "code", "\"\"", "line feature 9: a line feature needs a code")]
    [InlineData("FHW", "code", "\"FN C\"", "\"FN C\" is not one code")]
    [InlineData("FHW", "code", "\"FHW2\"", "ends in a number")]
    [InlineData("FHW", "layer", "\"V-SURF:FENC\"", "the layer it is identified on, \"V-SURF:FENC\", cannot be used")]
    [InlineData("FHW", "labelLayer", "\"V-SURF<TEXT\"", "the label layer \"V-SURF<TEXT\" cannot be used")]
    [InlineData("FHW", "placement", "\"Upside\"", "the side must be Left, Right or OnLine")]
    [InlineData("FHW", "label", "12", "label must be text")]
    [InlineData("FHW", "aliases", "\"Hog Wire\"", "aliases must be a list")]
    [InlineData("FHW", "altLabels", "[\"HOG WIRE\", 3]", "every entry of altLabels must be text")]
    [InlineData("FHW", "enabled", "\"no\"", "enabled must be true or false")]
    [InlineData("FHW", "alwaysLabel", "1", "alwaysLabel must be true or false")]
    [InlineData("FHW", "readHeight", "\"auto\"", "readHeight must be true, false or left out")]
    public void EachLineFeatureMistakeIsNamed(string code, string key, string json, string expected)
    {
        var doc = With(root => Feature(root, code)[key] = JToken.Parse(json));
        var problems = doc.Validate();
        Assert.Contains(problems, p => p.StartsWith("Line Labels: ") && p.Contains(expected));
        Assert.Throws<ConfigException>(() => doc.Save(Path.Combine(Path.GetTempPath(), "ftf-never-" + Guid.NewGuid().ToString("N") + ".json")));
    }

    [Fact]
    public void AMistakeTypedOnThePageIsPutRightThere()
    {
        // A value of the wrong kind in the file is rewritten by the next edit of that feature.
        var doc = With(root => Feature(root, "FHW")["aliases"] = "Hog Wire");
        var fhw = Get(doc, "FHW");
        Assert.Equal(new[] { "Hog Wire" }, fhw.Aliases);
        doc.ApplyLineFeature(fhw);
        Assert.Empty(doc.Validate());
        Assert.Equal(new[] { "Hog Wire" }, doc.CompileCandidate().LineFeatures.Single(f => f.Code == "FHW").Aliases);
    }

    // ------------------------------------------------------------------ span markers

    [Fact]
    public void SpanMarkersAreEditedAddedAndRemovedAndKeepTheirOtherProperties()
    {
        var doc = With(root => ((JObject)((JArray)root["spanMarkers"]!)[0])["//why"] = "office code sheet");
        var r = doc.GetLineLabelRules();
        r.SpanMarkers[0].Label = "GATE OPENING";
        r.SpanMarkers.Add(new EditableSpanMarker { Token = "DWY", Label = "" });
        r.SpanMarkers.Add(new EditableSpanMarker { Token = "XING", Label = "CROSSING", Enabled = false });
        doc.ApplyLineLabelRules(r);
        Assert.Empty(doc.Validate());

        var markers = doc.CompileCandidate().SpanMarkers;
        Assert.Equal(new[] { "FGP", "DWY", "XING" }, markers.Select(m => m.Token));
        Assert.Equal("GATE OPENING", markers[0].Label);
        Assert.Null(markers[1].Label);                          // the label says the word itself
        Assert.False(markers[2].Enabled);
        var written = (JArray)JObject.Parse(doc.Render())["spanMarkers"]!;
        Assert.Equal("office code sheet", (string?)written[0]["//why"]);
        Assert.Null(written[1]["enabled"]);

        // Removing the first keeps the others as they were.
        r = doc.GetLineLabelRules();
        r.SpanMarkers.RemoveAt(0);
        doc.ApplyLineLabelRules(r);
        Assert.Equal(new[] { "DWY", "XING" }, doc.CompileCandidate().SpanMarkers.Select(m => m.Token));
        Assert.Null(((JArray)JObject.Parse(doc.Render())["spanMarkers"]!)[0]["//why"]);

        r = doc.GetLineLabelRules();
        r.SpanMarkers.Clear();
        doc.ApplyLineLabelRules(r);
        Assert.Empty(doc.CompileCandidate().SpanMarkers);
    }

    [Theory]
    [InlineData("[{ \"token\": \"\" }]", "span marker 1 has no note word")]
    [InlineData("[{ \"token\": \"GATE POST\" }]", "\"GATE POST\" is not one word")]
    [InlineData("[{ \"token\": \"FGP\" }, { \"token\": \"fgp\", \"label\": \"G\" }]", "span marker FGP is listed 2 times")]
    [InlineData("[{ \"token\": \"FGP\", \"enabled\": \"yes\" }]", "span marker FGP: enabled must be true or false")]
    [InlineData("[{ \"token\": \"FGP\", \"label\": [\"GATE\"] }]", "span marker FGP: label must be text")]
    [InlineData("[\"FGP\"]", "span marker 1 is not an entry")]
    [InlineData("{ \"token\": \"FGP\" }", "the span markers (spanMarkers) must be a list")]
    public void EachSpanMarkerMistakeIsNamed(string json, string expected)
    {
        var problems = With(root => root["spanMarkers"] = JToken.Parse(json)).Validate();
        Assert.Contains(problems, p => p.StartsWith("Line Labels: ") && p.Contains(expected));
    }

    // ------------------------------------------------------------------ ignored TBC names

    [Fact]
    public void IgnoredNamesAreTypedOnePerLineAndTheInventoryReadsThem()
    {
        var doc = RuleDocument.Load(RulesPath);
        var r = doc.GetLineLabelRules();
        r.IgnoreLineNames = EditableLineFeature.SplitLines("INFO LINE\r\n\r\n  Survey Note \ninfo line\n");
        doc.ApplyLineLabelRules(r);
        Assert.Empty(doc.Validate());

        var cfg = doc.CompileCandidate();
        Assert.Equal(new[] { "INFO LINE", "Survey Note" }, cfg.IgnoreLineNames);
        var catalog = new LineworkCatalog(cfg.LineFeatures, cfg.IgnoreLineNames);
        Assert.True(catalog.Identify("Polyline", null, "0", 10, "survey note").NameIgnored);
    }

    [Theory]
    [InlineData("Edge of Conc", "one of EC's other names")]
    [InlineData("concrete edge", "EC's feature name")]
    [InlineData("EDGE OF CONCRETE", "EC's label text")]
    [InlineData("EC2", "read as the code EC")]
    public void AnIgnoredNameALineFeatureAlsoClaimsIsNamed(string name, string expected)
    {
        var doc = With(root =>
        {
            Feature(root, "EC")["aliases"] = new JArray("Edge of Conc");
            root["ignoreLineNames"] = new JArray(name);
        });
        var problems = doc.Validate();
        Assert.Contains(problems, p => p.StartsWith("Line Labels: ") && p.Contains(expected) && p.Contains("ignoring it does nothing"));
    }

    [Theory]
    [InlineData("\"INFO LINE\"", "the ignored TBC names (ignoreLineNames) must be a list")]
    [InlineData("[\"INFO LINE\", 4]", "every ignored TBC name must be text")]
    public void AnIgnoredNameListThatCannotBeReadIsNamed(string json, string expected)
    {
        var problems = With(root => root["ignoreLineNames"] = JToken.Parse(json)).Validate();
        Assert.Contains(problems, p => p.StartsWith("Line Labels: ") && p.Contains(expected));
    }

    // ------------------------------------------------------------------ stairs

    [Theory]
    [InlineData("{count} STEPS", 5, "5 STEPS")]
    [InlineData("STAIRS ({count})", 12, "STAIRS (12)")]
    [InlineData("STAIRS", 3, "STAIRS")]
    [InlineData(null, 7, "7 STEPS")]
    public void TheStairsPreviewIsWhatFtfLabelStairsWrites(string? format, int count, string expected)
    {
        Assert.Equal(expected, LineLabelPlanner.StairsText(format, count));
    }

    [Fact]
    public void FtfLabelStairsWritesThroughThePreviewsOwnFunction()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "FieldCodes.Cad", "LineworkCommands.cs"));
        Assert.Contains("var label = LineLabelPlanner.StairsText(cfg.StairsLabelFormat, count);", src);
    }

    [Fact]
    public void TheStairsLabelIsEditedAndReachesTheCommand()
    {
        var doc = RuleDocument.Load(RulesPath);
        var r = doc.GetLineLabelRules();
        r.StairsLabelFormat = "{count} RISERS";
        doc.ApplyLineLabelRules(r);
        Assert.Empty(doc.Validate());
        Assert.Equal("4 RISERS", LineLabelPlanner.StairsText(doc.CompileCandidate().StairsLabelFormat, 4));

        // A file without it gets it written the first time it differs from the built-in wording.
        doc = With(root => root.Remove("stairsLabelFormat"));
        r = doc.GetLineLabelRules();
        r.StairsLabelFormat = "STAIRS";
        doc.ApplyLineLabelRules(r);
        Assert.Equal("STAIRS", (string?)JObject.Parse(doc.Render())["stairsLabelFormat"]);
    }

    [Theory]
    [InlineData("\"  \"", "the stairs label is empty")]
    [InlineData("\"{steps} STEPS\"", "uses {steps}; it can use {count}")]
    [InlineData("\"{COUNT} STEPS\"", "uses {COUNT}; it can use {count}")]
    [InlineData("\"{count STEPS\"", "a { without its }")]
    [InlineData("5", "the stairs label (stairsLabelFormat) must be text")]
    public void EachStairsMistakeIsNamed(string json, string expected)
    {
        var problems = With(root => root["stairsLabelFormat"] = JToken.Parse(json)).Validate();
        Assert.Contains(problems, p => p.StartsWith("Line Labels: ") && p.Contains(expected));
    }

    // ------------------------------------------------------------------ saving

    [Fact]
    public void SavingPutsThisPagesRulesOnTheFileAsItIsNow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ftf-linelabels-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "rules.json");
            File.Copy(RulesPath, path);

            var page = RuleDocument.Load(path);
            var ep = Get(page, "EP");
            ep.AltLabels.Add("EDGE PVMT");
            page.ApplyLineFeature(ep);
            page.RemoveLineFeature(Get(page, "LN"));
            page.AddLineFeature(new EditableLineFeature { Code = "RWG", Label = "GABION WALL" });
            var r = page.GetLineLabelRules();
            r.StairsLabelFormat = "{count} STAIRS";
            r.IgnoreLineNames.Add("INFO LINE");
            r.SpanMarkers[0].Enabled = false;
            page.ApplyLineLabelRules(r);

            // Meanwhile the Benches page saves.
            var other = RuleDocument.Load(path);
            var b = other.GetBench();
            b.LabelLayer = "V-SURF-LAND-TEXT-B";
            other.ApplyBench(b);
            other.Save(path);

            var fresh = RuleDocument.Load(path);
            page.CopyLineLabelRulesTo(fresh);
            Assert.Empty(fresh.Validate());
            fresh.Save(path);

            var cfg = RulesConfig.Load(path);
            Assert.Equal("V-SURF-LAND-TEXT-B", cfg.Bench.LabelLayer);
            Assert.Equal(new[] { "EOP", "EDGE PVMT" }, cfg.LineFeatures.Single(f => f.Code == "EP").AltLabels);
            Assert.DoesNotContain(cfg.LineFeatures, f => f.Code == "LN");
            Assert.Equal("GABION WALL", cfg.LineFeatures.Single(f => f.Code == "RWG").Label);
            Assert.Equal("{count} STAIRS", cfg.StairsLabelFormat);
            Assert.Equal(new[] { "INFO LINE" }, cfg.IgnoreLineNames);
            Assert.False(Assert.Single(cfg.SpanMarkers).Enabled);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void CopyingWhatDidNotChangeWritesNothing()
    {
        var page = RuleDocument.Load(RulesPath);
        var fresh = RuleDocument.Load(RulesPath);
        var text = fresh.Render();
        page.CopyLineLabelRulesTo(fresh);
        Assert.Equal(text, fresh.Render());
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FieldToFinish.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
