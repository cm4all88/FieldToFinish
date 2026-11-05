using FieldCodes.Editing;
using FieldCodes.Linework;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Tests;

/// <summary>
/// Settings > Field Code Lists: the rules file's lists that say how the crew's codes are read
/// (ignoreCodes, symbolLabels, controlCodes, lineworkCodes, lineworkCommands, modifiers), read in
/// the file's order, written only where they changed (each entry keeping what the page does not
/// show), checked entry by entry, and reaching the parser, the control table and the linework
/// reader exactly as written.
/// </summary>
public sealed class FieldCodeListsEditingTests
{
    private static readonly string RulesPath = Path.Combine(AppContext.BaseDirectory, "rules.json");

    private static JObject Shipped() => JObject.Parse(File.ReadAllText(RulesPath));

    private static RuleDocument With(string key, string json)
    {
        var root = Shipped();
        root[key] = JToken.Parse(json);
        return RuleDocument.FromText(root.ToString());
    }

    /// <summary>Every top-level property other than <paramref name="changed"/> is as it was.</summary>
    private static void OnlyChanged(JObject before, RuleDocument doc, string changed)
    {
        var after = JObject.Parse(doc.Render());
        Assert.Equal(before.Properties().Select(p => p.Name), after.Properties().Select(p => p.Name));
        foreach (var p in before.Properties().Where(p => p.Name != changed))
            Assert.True(JToken.DeepEquals(p.Value, after[p.Name]), p.Name + " changed");
        Assert.False(JToken.DeepEquals(before[changed], after[changed]), changed + " did not change");
    }

    // ------------------------------------------------------------------ reading

    [Fact]
    public void TheShippedListsAreReadInTheirOrder()
    {
        var doc = RuleDocument.Load(RulesPath);
        var l = doc.GetCodeLists();

        Assert.Equal(new[] { "GS", "INFO", "INFOL", "NOTE", "ZK", "CONS" }, l.IgnoreCodes.Select(i => i.Code));
        Assert.StartsWith("Ground shot", l.IgnoreCodes[0].Reason);
        Assert.Equal("surface", l.IgnoreCodes[0].Expect);
        Assert.Equal(string.Empty, l.IgnoreCodes[1].Expect);
        Assert.Equal(string.Empty, l.IgnoreCodes[5].Reason);

        Assert.Equal(18, l.SymbolLabels.Count);
        Assert.Equal("CB", l.SymbolLabels[0].Code);
        Assert.Equal("V-UTIL-STRM-TEXT-E", l.SymbolLabels[0].Layer);
        Assert.Equal(string.Empty, l.SymbolLabels[0].Label);
        Assert.All(l.SymbolLabels, s => Assert.True(s.Enabled));

        Assert.Equal(38, l.ControlCodes.Count);
        Assert.Equal("FND", l.ControlCodes[0]);
        Assert.Equal(123, l.LineworkCodes.Count);
        Assert.Contains("FNC", l.LineworkCodes);

        Assert.True(l.LineworkCommandsInFile);
        Assert.Equal(new[] { "B" }, l.LineworkCommands.For("begin"));
        Assert.Equal(new[] { "HO", "VO", "SO" }, l.LineworkCommands.For("offset"));
        Assert.Equal("/", l.LineworkCommands.Escape);

        Assert.Equal(new[] { "cluster", "dead", "remove", "protected" }, l.Modifiers.Select(m => m.Id));
        var cluster = l.Modifiers[0];
        Assert.Equal("^CL(?<count>[0-9]+)$", cluster.Pattern);
        Assert.Equal(10, cluster.Priority);
        Assert.Equal("count", cluster.StemCountFrom);
        Assert.StartsWith("CLUSTER OF {count}", cluster.LabelFormat);
        var dead = l.Modifiers[1];
        Assert.Equal(" (DEAD)", dead.LabelSuffix);
        Assert.Equal("-DEAD", dead.LayerSuffix);
        Assert.True(dead.Flag);
        Assert.False(l.Modifiers[2].Flag);

        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void NothingChangedWritesNothing()
    {
        var doc = RuleDocument.Load(RulesPath);
        var text = doc.Render();
        doc.ApplyCodeLists(doc.GetCodeLists());
        Assert.Equal(text, doc.Render());
    }

    [Fact]
    public void TheBuiltInLineworkCommandsArePmxUniversal()
    {
        var b = EditableLineworkCommands.BuiltIn();
        Assert.Equal(14, EditableLineworkCommands.Keys.Length);
        Assert.Equal(new[] { "E" }, b.For("end"));
        Assert.Equal(new[] { "CPN" }, b.For("connectPoint"));
        Assert.Equal("/", b.Escape);
        Assert.All(EditableLineworkCommands.Keys, k =>
        {
            Assert.NotEqual(k, EditableLineworkCommands.Name(k));
            Assert.False(string.IsNullOrWhiteSpace(EditableLineworkCommands.Meaning(k)));
        });
    }

    // ------------------------------------------------------------------ editing

    [Fact]
    public void ANeverDrawnEditChangesOnlyThatListAndTheParserReadsIt()
    {
        var doc = RuleDocument.Load(RulesPath);
        var before = JObject.Parse(doc.Render());
        var l = doc.GetCodeLists();
        l.IgnoreCodes.RemoveAll(i => i.Code == "INFO");
        l.IgnoreCodes.Single(i => i.Code == "CONS").Reason = "Construction stake: nothing drafted.";
        l.IgnoreCodes.Add(new EditableIgnoreCode { Code = "TOPO", Reason = "Topo shot.", Expect = "surface" });
        l.IgnoreCodes.Add(new EditableIgnoreCode { Code = "XQ" });
        doc.ApplyCodeLists(l);

        OnlyChanged(before, doc, "ignoreCodes");
        var list = (JArray)JObject.Parse(doc.Render())["ignoreCodes"]!;
        Assert.Equal(7, list.Count);
        Assert.True(JToken.DeepEquals(before["ignoreCodes"]![0], list[0]));                   // GS untouched
        Assert.Equal("Construction stake: nothing drafted.", (string)list[4]!["reason"]!);    // was a plain "CONS"
        Assert.Equal("surface", (string)list[5]!["expect"]!);
        Assert.Equal(JTokenType.String, list[6].Type);                                        // no note: a plain code
        Assert.Empty(doc.Validate());

        var cfg = doc.CompileCandidate();
        var parser = new FieldCodeParser(cfg);
        Assert.True(parser.Parse("1", "TOPO2").Ignored);
        Assert.True(parser.Parse("1", "XQ").Ignored);
        Assert.False(parser.Parse("1", "INFO").Ignored);
        Assert.Equal("Construction stake: nothing drafted.", cfg.IgnoreNoteFor("CONS")!.Reason);
        Assert.True(cfg.IgnoreNoteFor("TOPO")!.ExpectsSurface);
    }

    [Fact]
    public void AnEntryKeepsWhatThePageDoesNotShowAndANoteClearedLeavesThePlainCode()
    {
        var root = Shipped();
        var ignore = (JArray)root["ignoreCodes"]!;
        ((JObject)ignore[0])["by"] = "office";
        ((JObject)ignore[0])["at"] = "2026-01-02T00:00:00";
        var doc = RuleDocument.FromText(root.ToString());

        var l = doc.GetCodeLists();
        l.IgnoreCodes[0].Reason = "Ground shot.";
        l.IgnoreCodes[1].Reason = string.Empty;          // INFO: nothing else on it
        // Moved to the end: an entry is matched by its code, not its place.
        var zk = l.IgnoreCodes[4];
        l.IgnoreCodes.RemoveAt(4);
        l.IgnoreCodes.Add(zk);
        doc.ApplyCodeLists(l);

        var list = (JArray)JObject.Parse(doc.Render())["ignoreCodes"]!;
        Assert.Equal("Ground shot.", (string)list[0]!["reason"]!);
        Assert.Equal("office", (string)list[0]!["by"]!);
        Assert.Equal("surface", (string)list[0]!["expect"]!);
        Assert.Equal(JTokenType.String, list[1].Type);
        Assert.Equal("INFO", (string)list[1]!);
        Assert.True(JToken.DeepEquals(ignore[4], list[5]));
        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void ACodeTypedOverKeepsItsEntrysOtherProperties()
    {
        var doc = RuleDocument.Load(RulesPath);
        var l = doc.GetCodeLists();
        l.Modifiers[0].Id = "clump";
        l.Modifiers[0].Priority = 15;
        doc.ApplyCodeLists(l);

        var cluster = (JObject)((JArray)JObject.Parse(doc.Render())["modifiers"]!)[0];
        Assert.Equal("clump", (string)cluster["id"]!);
        Assert.Equal(15, (int)cluster["priority"]!);
        Assert.NotNull(cluster["//block"]);              // the entry's comment is kept
        Assert.Equal("count", (string)cluster["stemCountFrom"]!);
        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void ASymbolLabelEditReachesTheParser()
    {
        var doc = RuleDocument.Load(RulesPath);
        var before = JObject.Parse(doc.Render());
        var l = doc.GetCodeLists();
        l.SymbolLabels.Single(s => s.Code == "CB").Label = "CATCH BASIN";
        l.SymbolLabels.Single(s => s.Code == "PP").Enabled = false;
        l.SymbolLabels.Add(new EditableSymbolLabel { Code = "MW", Layer = "V-SURF-MISC-TEXT-E" });
        doc.ApplyCodeLists(l);

        OnlyChanged(before, doc, "symbolLabels");
        var list = (JArray)JObject.Parse(doc.Render())["symbolLabels"]!;
        var pp = list.OfType<JObject>().Single(o => (string)o["code"]! == "PP");
        Assert.False((bool)pp["enabled"]!);
        var mw = (JObject)list.Last!;
        Assert.Equal(new[] { "code", "layer" }, mw.Properties().Select(p => p.Name));    // on is the default: not written
        Assert.Empty(doc.Validate());

        var parser = new FieldCodeParser(doc.CompileCandidate());
        Assert.Equal("CATCH BASIN", parser.Parse("1", "CB2").LabelText);
        Assert.True(parser.Parse("1", "PP").NoContent);
        var w = parser.Parse("1", "MW");
        Assert.Equal("MW", w.LabelText);
        Assert.Equal("V-SURF-MISC-TEXT-E", w.LabelLayer);

        // Turned back on: "enabled" is taken out again.
        l = doc.GetCodeLists();
        l.SymbolLabels.Single(s => s.Code == "PP").Enabled = true;
        doc.ApplyCodeLists(l);
        pp = ((JArray)JObject.Parse(doc.Render())["symbolLabels"]!).OfType<JObject>().Single(o => (string)o["code"]! == "PP");
        Assert.Null(pp["enabled"]);
    }

    [Fact]
    public void ControlAndLineworkCodesAreExactlyWhatThePageWrote()
    {
        var doc = RuleDocument.Load(RulesPath);
        var before = JObject.Parse(doc.Render());
        var l = doc.GetCodeLists();
        l.ControlCodes = EditableCodeLists.SplitCodes("FND\r\nCP, XBM");
        doc.ApplyCodeLists(l);
        OnlyChanged(before, doc, "controlCodes");

        before = JObject.Parse(doc.Render());
        l = doc.GetCodeLists();
        l.LineworkCodes.Add("SWALE");
        doc.ApplyCodeLists(l);
        OnlyChanged(before, doc, "lineworkCodes");
        Assert.Empty(doc.Validate());

        var cfg = doc.CompileCandidate();
        Assert.Equal(new[] { "FND", "CP", "XBM" }, cfg.ControlCodes);
        Assert.Equal(124, cfg.LineworkCodes.Count);
        Assert.True(new FieldCodeParser(cfg).Parse("1", "SWALE2 B").Ignored);
        Assert.Equal("SWALE2", LineworkCoding.Parse("SWALE2 B", cfg).Figures.Single().FigureId);
    }

    [Fact]
    public void ALineworkCommandEditChangesOnlyThatCommand()
    {
        var doc = RuleDocument.Load(RulesPath);
        var before = JObject.Parse(doc.Render());
        var l = doc.GetCodeLists();
        l.LineworkCommands.Codes["begin"] = new List<string> { "B", "BEG" };
        l.LineworkCommands.Escape = "\\";
        doc.ApplyCodeLists(l);

        OnlyChanged(before, doc, "lineworkCommands");
        var after = (JObject)JObject.Parse(doc.Render())["lineworkCommands"]!;
        var was = (JObject)before["lineworkCommands"]!;
        foreach (var p in was.Properties().Where(p => p.Name != "begin" && p.Name != "escape"))
            Assert.True(JToken.DeepEquals(p.Value, after[p.Name]), p.Name + " changed");
        Assert.Empty(doc.Validate());

        var cfg = doc.CompileCandidate();
        Assert.Equal(new[] { "B", "BEG" }, cfg.LineworkCommands.Begin);
        var shot = LineworkCoding.Parse("FNC BEG \\GATE", cfg);
        Assert.True(shot.Figures.Single().Begins);
        Assert.Equal(new[] { "GATE" }, shot.Figures.Single().Words);
    }

    [Fact]
    public void AModifierEditReachesTheLabelAndLayers()
    {
        var doc = RuleDocument.Load(RulesPath);
        var before = JObject.Parse(doc.Render());
        var l = doc.GetCodeLists();
        var dead = l.Modifiers.Single(m => m.Id == "dead");
        dead.LabelSuffix = " (DEAD TREE)";
        dead.Flag = false;
        l.Modifiers.Add(new EditableModifier { Id = "stump", Pattern = "^STUMP$", Priority = 50, LabelSuffix = " (STUMP)", LayerSuffix = "-STMP" });
        doc.ApplyCodeLists(l);

        OnlyChanged(before, doc, "modifiers");
        var list = (JArray)JObject.Parse(doc.Render())["modifiers"]!;
        Assert.Null(list[1]["flag"]);
        Assert.Equal(new[] { "id", "pattern", "priority", "labelSuffix", "layerSuffix" }, ((JObject)list[4]).Properties().Select(p => p.Name));
        Assert.Empty(doc.Validate());

        var parser = new FieldCodeParser(doc.CompileCandidate());
        var p = parser.Parse("1", "CON 18 . 25 DEAD STUMP");
        Assert.False(p.HasErrors);
        Assert.Contains("(DEAD TREE)", p.LabelText);
        Assert.Contains("(STUMP)", p.LabelText);
        Assert.EndsWith("-DEAD-STMP", p.LabelLayer);
        Assert.DoesNotContain(p.Diagnostics, d => d.Code == "FLAG");
    }

    [Fact]
    public void AFileWithoutLineworkCommandsShowsTheBuiltInSetAndTheFirstSaveWritesIt()
    {
        var root = Shipped();
        root.Remove("lineworkCommands");
        root.Remove("//lineworkCommands");
        var doc = RuleDocument.FromText(root.ToString());
        var l = doc.GetCodeLists();
        Assert.False(l.LineworkCommandsInFile);
        Assert.Equal(new[] { "B" }, l.LineworkCommands.For("begin"));
        Assert.Equal("/", l.LineworkCommands.Escape);

        doc.ApplyCodeLists(l);
        var s = (JObject)JObject.Parse(doc.Render())["lineworkCommands"]!;
        Assert.Equal(EditableLineworkCommands.Keys.Concat(new[] { "escape" }), s.Properties().Select(p => p.Name));
        Assert.Equal(new[] { "HO", "VO", "SO" }, ((JArray)s["offset"]!).Select(t => (string)t!));
        Assert.Equal("/", (string)s["escape"]!);
        Assert.Empty(doc.Validate());
        Assert.True(doc.GetCodeLists().LineworkCommandsInFile);
    }

    [Fact]
    public void ALineworkCommandsSectionNamingSomeCommandsKeepsTheBuiltInRest()
    {
        var doc = With("lineworkCommands", "{ \"begin\": [\"BG\"] }");
        var l = doc.GetCodeLists();
        Assert.True(l.LineworkCommandsInFile);
        Assert.Equal(new[] { "BG" }, l.LineworkCommands.For("begin"));
        Assert.Equal(new[] { "E" }, l.LineworkCommands.For("end"));
        Assert.Equal("/", l.LineworkCommands.Escape);
        var text = doc.Render();
        doc.ApplyCodeLists(l);
        Assert.Equal(text, doc.Render());

        l.LineworkCommands.Codes["end"] = new List<string> { "EN" };
        doc.ApplyCodeLists(l);
        var s = (JObject)JObject.Parse(doc.Render())["lineworkCommands"]!;
        Assert.Equal(new[] { "begin", "end" }, s.Properties().Select(p => p.Name));
        Assert.Equal(new[] { "EN" }, doc.CompileCandidate().LineworkCommands.End);
    }

    [Fact]
    public void AFileWithoutAListShowsItEmptyAndWritesItOnlyWhenOneIsTyped()
    {
        var root = Shipped();
        root.Remove("modifiers");
        root.Remove("controlCodes");
        var doc = RuleDocument.FromText(root.ToString());
        var l = doc.GetCodeLists();
        Assert.Empty(l.Modifiers);
        Assert.Empty(l.ControlCodes);
        var text = doc.Render();
        doc.ApplyCodeLists(l);
        Assert.Equal(text, doc.Render());

        l.ControlCodes.Add("FND");
        doc.ApplyCodeLists(l);
        Assert.Equal(new[] { "FND" }, doc.CompileCandidate().ControlCodes);
        Assert.Null(JObject.Parse(doc.Render())["modifiers"]);
    }

    // ------------------------------------------------------------------ checks

    [Theory]
    [InlineData("ignoreCodes", "[\"GS\", \"gs\"]", "Never Drawn: listed twice: GS")]
    [InlineData("ignoreCodes", "[\"G S\"]", "\"G S\" is not a plain field code")]
    [InlineData("ignoreCodes", "[{ \"code\": \"GS\", \"expect\": \"tin\" }]", "GS: what holds its points must be blank or surface, not \"tin\"")]
    [InlineData("ignoreCodes", "[{ \"code\": \"\", \"reason\": \"why\" }]", "Never Drawn, row 1: the code is blank")]
    [InlineData("ignoreCodes", "[{ \"code\": \"GS\", \"reason\": 5 }]", "GS: \"reason\" must be text")]
    [InlineData("ignoreCodes", "[{ \"code\": \"GS\", \"at\": \"soon\" }]", "GS: \"at\" must be a date")]
    [InlineData("ignoreCodes", "[\"GS\", 12]", "Never Drawn, row 2: each entry must be a code")]
    [InlineData("ignoreCodes", "{ \"code\": \"GS\" }", "ignoreCodes must be a list")]
    [InlineData("symbolLabels", "[{ \"code\": \"CB\" }]", "CB has no text layer")]
    [InlineData("symbolLabels", "[{ \"code\": \"CB\", \"layer\": \"V:TEXT\" }]", "CB: the layer \"V:TEXT\" cannot be used")]
    [InlineData("symbolLabels", "[{ \"code\": \"CB\", \"layer\": \"V-T\" }, { \"code\": \"cb\", \"layer\": \"V-T\" }]", "Labelled Symbols: listed twice: CB")]
    [InlineData("symbolLabels", "[{ \"layer\": \"V-T\" }]", "Labelled Symbols, row 1: the code is blank")]
    [InlineData("symbolLabels", "[{ \"code\": \"C B\", \"layer\": \"V-T\" }]", "\"C B\" is not one code")]
    [InlineData("symbolLabels", "[{ \"code\": \"CB\", \"layer\": \"V-T\", \"enabled\": \"no\" }]", "row 1: \"enabled\" (On) must be true or false")]
    [InlineData("symbolLabels", "[\"CB\"]", "row 1: each entry must be")]
    [InlineData("controlCodes", "\"FND\"", "Control: controlCodes must be a list")]
    [InlineData("controlCodes", "[\"FND\", \"fnd\"]", "Control: listed twice: FND")]
    [InlineData("controlCodes", "[\"F ND\"]", "\"F ND\" is not one code")]
    [InlineData("controlCodes", "[{ \"code\": \"FND\" }]", "Control, row 1: each entry must be a code")]
    [InlineData("lineworkCodes", "[\"FNC\", \"EP.1\"]", "Linework: \"EP.1\" is not a plain field code")]
    [InlineData("lineworkCodes", "[\"FNC\", \"FNC\"]", "Linework: listed twice: FNC")]
    [InlineData("lineworkCommands", "{ \"begin\": [\"B\"], \"end\": [\"B\"] }", "Line Commands: B is listed for Begin and End")]
    [InlineData("lineworkCommands", "{ \"begin\": [\"P\"] }", "P is listed for Begin and Begin curve (built in)")]
    [InlineData("lineworkCommands", "{ \"begin\": \"B\" }", "the codes for Begin must be a list")]
    [InlineData("lineworkCommands", "{ \"begin\": [\"B X\"] }", "Begin: \"B X\" is not one code")]
    [InlineData("lineworkCommands", "{ \"escape\": \"X\" }", "the escape \"X\" must be a symbol")]
    [InlineData("lineworkCommands", "{ \"escape\": 5 }", "the escape character must be text")]
    [InlineData("lineworkCommands", "[\"B\"]", "lineworkCommands must be {")]
    [InlineData("modifiers", "[{ \"id\": \"dead\", \"pattern\": \"^(DEAD$\" }]", "Modifiers: dead: the pattern \"^(DEAD$\" is not a valid pattern")]
    [InlineData("modifiers", "[{ \"id\": \"dead\" }]", "Modifiers: dead: the pattern is blank")]
    [InlineData("modifiers", "[{ \"pattern\": \"^DEAD$\" }]", "Modifiers, row 1: the name (id) is blank")]
    [InlineData("modifiers", "[{ \"id\": \"a\", \"pattern\": \"^A$\" }, { \"id\": \"A\", \"pattern\": \"^B$\" }]", "Modifiers: listed twice: a")]
    [InlineData("modifiers", "[{ \"id\": \"dead\", \"pattern\": \"^DEAD$\", \"priority\": \"high\" }]", "Modifiers, row 1: the order (priority) must be a whole number")]
    [InlineData("modifiers", "[{ \"id\": \"cl\", \"pattern\": \"^CL[0-9]+$\", \"stemCountFrom\": \"count\" }]", "cl: the stem count is read from \"count\", which is not a named part")]
    [InlineData("modifiers", "[{ \"id\": \"a\", \"pattern\": \"^A$\", \"labelFormat\": \"X\", \"priority\": 1 }, { \"id\": \"b\", \"pattern\": \"^B$\", \"labelFormat\": \"Y\", \"priority\": 1 }]", "a and b both replace the whole label at order 1")]
    [InlineData("modifiers", "[{ \"id\": \"dead\", \"pattern\": \"^DEAD$\", \"layerSuffix\": \"-DE:AD\" }]", "dead: the layer ending \"-DE:AD\" cannot be used")]
    [InlineData("modifiers", "[{ \"id\": \"dead\", \"pattern\": \"^DEAD$\", \"flag\": \"yes\" }]", "\"flag\" (Flag for review) must be true or false")]
    [InlineData("modifiers", "[{ \"id\": \"dead\", \"pattern\": \"^DEAD$\", \"labelSuffix\": 5 }]", "row 1: \"labelSuffix\" must be text")]
    [InlineData("modifiers", "{ \"id\": \"dead\" }", "modifiers must be a list")]
    public void EachMistakeIsNamedNotSaved(string key, string json, string expected)
    {
        var problems = With(key, json).Validate();
        Assert.Contains(problems, p => p.StartsWith("Field Code Lists: ") && p.Contains(expected));
    }

    [Fact]
    public void AMistakeTypedOnThePageIsNamedBeforeItIsSaved()
    {
        var doc = RuleDocument.Load(RulesPath);
        var l = doc.GetCodeLists();
        l.SymbolLabels.Add(new EditableSymbolLabel { Code = "MW" });
        l.Modifiers[1].Pattern = "^DEAD[";
        doc.ApplyCodeLists(l);
        var problems = doc.Validate();
        Assert.Contains("Field Code Lists: Labelled Symbols: MW has no text layer.", problems);
        Assert.Contains(problems, p => p.StartsWith("Field Code Lists: Modifiers: dead: the pattern \"^DEAD[\" is not a valid pattern"));
    }

    [Fact]
    public void CodesTypedInOneBoxAreSplitAndKeptInOrderAsTyped()
    {
        Assert.Equal(new[] { "FND", "fip", "XBM", "FND" }, EditableCodeLists.SplitCodes(" FND\r\nfip, XBM;\n\n FND "));
        Assert.Empty(EditableCodeLists.SplitCodes(" \r\n , "));
    }

    // ------------------------------------------------------------------ the page's preview

    [Theory]
    [InlineData("GS 12", "Never drawn (GS): Ground shot")]
    [InlineData("GS 12", "Its points are expected in a surface.")]
    [InlineData("CONS", "Never drawn (CONS): no reason is written for it.")]
    [InlineData("CB2", "Labelled symbol: labelled \"CB2\" on V-UTIL-STRM-TEXT-E.")]
    [InlineData("FND 5/8 IR", "Control: FND is a control code")]
    [InlineData("FNC1 B 3.5", "Linework: line FNC1 (code FNC, line 1): begin, value 3.5.")]
    [InlineData("CON 18 . 25 DEAD", "Modifiers: dead (DEAD).")]
    public void ThePreviewSaysHowTheListsReadADescription(string description, string expected)
    {
        var lines = CodeListPreview.Read(RulesConfig.Load(RulesPath), description);
        Assert.Contains(lines, l => l.Contains(expected));
    }

    [Fact]
    public void ThePreviewNeverCallsAControlPointUnhandledAndFollowsTheEdits()
    {
        var doc = RuleDocument.Load(RulesPath);
        Assert.DoesNotContain(CodeListPreview.Read(doc.CompileCandidate(), "FND 5/8 IR"), l => l.Contains("unhandled"));

        var l = doc.GetCodeLists();
        l.ControlCodes.Remove("FND");
        doc.ApplyCodeLists(l);
        var lines = CodeListPreview.Read(doc.CompileCandidate(), "FND 5/8 IR");
        Assert.DoesNotContain(lines, x => x.StartsWith("Control:"));
        Assert.Contains(lines, x => x.Contains("No rule reads FND yet"));
    }

    // ------------------------------------------------------------------ saving

    [Fact]
    public void SavingPutsThisPagesListsOnTheFileAsItIsNow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ftf-codelists-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "rules.json");
            File.Copy(RulesPath, path);

            var page = RuleDocument.Load(path);
            var l = page.GetCodeLists();
            l.ControlCodes.Add("CP");
            l.IgnoreCodes.Single(i => i.Code == "CONS").Reason = "Construction stake.";
            page.ApplyCodeLists(l);

            // Meanwhile the Benches page saves.
            var other = RuleDocument.Load(path);
            var b = other.GetBench();
            b.LabelLayer = "V-SURF-LAND-TEXT-B";
            other.ApplyBench(b);
            other.Save(path);

            var fresh = RuleDocument.Load(path);
            page.CopyCodeListsTo(fresh);
            Assert.Empty(fresh.Validate());
            fresh.Save(path);

            var cfg = RulesConfig.Load(path);
            Assert.Equal("CP", cfg.ControlCodes.Last());
            Assert.Equal(39, cfg.ControlCodes.Count);
            Assert.Equal("Construction stake.", cfg.IgnoreNoteFor("CONS")!.Reason);
            Assert.Equal("V-SURF-LAND-TEXT-B", cfg.Bench.LabelLayer);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
