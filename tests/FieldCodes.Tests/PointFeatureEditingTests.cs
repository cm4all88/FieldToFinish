using FieldCodes;
using FieldCodes.Editing;

namespace FieldCodes.Tests;

/// <summary>
/// The editor workflow, end to end at the model level: every path the Point
/// Features page drives, proven without a UI. One test per requirement from the
/// editor specification.
/// </summary>
public sealed class PointFeatureEditingTests
{
    private readonly string _root;
    private readonly string _plugin;
    private readonly string _office;
    private readonly string _drawing;

    public PointFeatureEditingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ftf-editor-" + Path.GetRandomFileName());
        _plugin = Path.Combine(_root, "bundle");
        _office = Path.Combine(_root, "office");
        _drawing = Path.Combine(_root, "job");
        Directory.CreateDirectory(_plugin);
        Directory.CreateDirectory(_drawing);

        // The real shipped rules act as the factory level.
        File.Copy(Path.Combine(AppContext.BaseDirectory, "rules.json"),
                  Path.Combine(_plugin, "rules.json"));
    }

    private RulesResolution Resolve() => RulesStore.Resolve(_drawing, _plugin, _office);

    private static ParsedPoint Parse(RulesConfig cfg, string desc)
        => new FieldCodeParser(cfg).Parse("1", desc);

    // ------------------------------------------------------------------- editing

    [Fact]
    public void EditingAPowerPoleLabel()
    {
        var r = Resolve();
        var doc = RuleDocument.Load(r.ActivePath);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "UTILITY POLE {number}";
        doc.ApplyEdit(pole);
        Assert.Empty(doc.Validate());
        doc.Save(r.SaveTarget);

        var parsed = Parse(RulesConfig.Load(Resolve().ActivePath), "PP 1234");
        Assert.Equal("UTILITY POLE 1234", parsed.LabelText);
        Assert.Equal("V-UTIL-POWR-TEXT-E", parsed.LabelLayer);
    }

    [Fact]
    public void EditingASignLabel()
    {
        var r = Resolve();
        var doc = RuleDocument.Load(r.ActivePath);
        var sign = doc.GetEditable("sn-sign");
        sign.LabelFormat = "SIGN (FIELD LOCATED)";
        doc.ApplyEdit(sign);
        Assert.Empty(doc.Validate());
        doc.Save(r.SaveTarget);

        var parsed = Parse(RulesConfig.Load(Resolve().ActivePath), "SN 135");
        Assert.Equal("SIGN (FIELD LOCATED)", parsed.LabelText);
        Assert.Equal(315.0, parsed.RotationDegrees);     // rotation untouched
    }

    [Fact]
    public void DisablingAFeature()
    {
        var r = Resolve();
        var doc = RuleDocument.Load(r.ActivePath);
        var pole = doc.GetEditable("pole");
        pole.Enabled = false;
        doc.ApplyEdit(pole);
        doc.Save(r.SaveTarget);

        var cfg = RulesConfig.Load(Resolve().ActivePath);
        Assert.True(Parse(cfg, "PP 1234").Unhandled);
        Assert.Equal("tree", Parse(cfg, "CON 18 . 25").RuleId);   // trees untouched
    }

    [Fact]
    public void ApplyWithoutSaveChangesNothingOnDisk()
    {
        var r = Resolve();
        var before = File.ReadAllText(r.ActivePath);

        var doc = RuleDocument.Load(r.ActivePath);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "STAGED ONLY {number}";
        doc.ApplyEdit(pole);                              // Apply

        // The staged document sees it; no file anywhere does.
        Assert.Equal("STAGED ONLY 9",
            Parse(doc.CompileCandidate(), "PP 9").LabelText);
        Assert.Equal(before, File.ReadAllText(r.ActivePath));
        Assert.False(File.Exists(Path.Combine(_office, "rules.json")));
    }

    [Fact]
    public void CancelIsReloadingTheEditableFromTheDocument()
    {
        var doc = RuleDocument.Load(Resolve().ActivePath);

        // Fields were changed in the UI but never applied; Cancel re-reads the
        // document, which still holds the original values.
        var reloaded = doc.GetEditable("pole");
        Assert.Equal(RuleEditingTests.ShippedPoleFormat, reloaded.LabelFormat);
        Assert.Equal("P", reloaded.TagPrefix);
        Assert.True(reloaded.Enabled);
    }

    [Fact]
    public void SaveAndReloadRoundTrip()
    {
        var r = Resolve();
        var doc = RuleDocument.Load(r.ActivePath);
        var pole = doc.GetEditable("pole");
        pole.Description = "Primary Power Pole";
        pole.TagPrefix = "PW";
        doc.ApplyEdit(pole);
        doc.Save(r.SaveTarget);

        // A fresh resolve-and-load, exactly what the page's Reload does.
        var again = RuleDocument.Load(Resolve().ActivePath).GetEditable("pole");
        Assert.Equal("Primary Power Pole", again.Description);
        Assert.Equal("PW", again.TagPrefix);
    }

    // -------------------------------------------------------------- persistence

    [Fact]
    public void FirstEditCopiesFactoryToOffice()
    {
        var before = Resolve();
        Assert.Equal(RulesSource.Factory, before.Source);

        var doc = RuleDocument.Load(before.ActivePath);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "MY POLE {number}";
        doc.ApplyEdit(pole);
        doc.Save(before.SaveTarget);

        var after = Resolve();
        Assert.Equal(RulesSource.Office, after.Source);
        Assert.NotEqual(before.ActivePath, after.ActivePath);

        // Factory byte-for-byte untouched; office carries the edit AND the comments.
        var factory = File.ReadAllText(Path.Combine(_plugin, "rules.json"));
        Assert.Contains("\"POLE[ {number}][ {owner}]", factory);
        var office = File.ReadAllText(after.ActivePath);
        Assert.Contains("MY POLE", office);
        Assert.Contains("//point-features", office);
    }

    [Fact]
    public void ADrawingOverridePersistsInTheDrawingFolder()
    {
        // A job diverges: its rules live beside the drawing.
        File.Copy(Path.Combine(_plugin, "rules.json"), Path.Combine(_drawing, "rules.json"));

        var r = Resolve();
        Assert.Equal(RulesSource.Drawing, r.Source);

        var doc = RuleDocument.Load(r.ActivePath);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "JOB POLE {number}";
        doc.ApplyEdit(pole);
        doc.Save(r.SaveTarget);

        // The edit stayed with the job; office was never created, factory untouched.
        Assert.Contains("JOB POLE", File.ReadAllText(Path.Combine(_drawing, "rules.json")));
        Assert.False(File.Exists(Path.Combine(_office, "rules.json")));
        Assert.Contains("\"POLE[ {number}][ {owner}]",
            File.ReadAllText(Path.Combine(_plugin, "rules.json")));
        Assert.Equal(RulesSource.Drawing, Resolve().Source);
    }

    // ------------------------------------------------------------------ preview

    [Fact]
    public void PreviewUsesStagedChangesBeforeAnySave()
    {
        var doc = RuleDocument.Load(Resolve().ActivePath);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "STAGED POLE {number}";
        doc.ApplyEdit(pole);

        var preview = FeaturePreview.Render(
            Parse(doc.CompileCandidate(), "PP 1234"), "Power Pole");

        Assert.Contains("Label: STAGED POLE 1234", preview);
    }

    [Fact]
    public void ThePolePreviewReadsAsSpecified()
    {
        var preview = FeaturePreview.Render(
            Parse(RulesConfig.Load(Resolve().ActivePath), "PP 1234"), "Power Pole");

        Assert.StartsWith("Power Pole", preview);
        Assert.Contains("Civil 3D symbol: Existing", preview);
        Assert.Contains("FTF action: Label", preview);
        Assert.Contains("Label: POLE 1234", preview);
        Assert.Contains("Layer: V-UTIL-POWR-TEXT-E", preview);
        Assert.Contains("Tag: P#", preview);
        Assert.Contains("Rotation: None", preview);
    }

    [Fact]
    public void TheSignPreviewReadsAsSpecified()
    {
        var preview = FeaturePreview.Render(
            Parse(RulesConfig.Load(Resolve().ActivePath), "SN 135"), "Sign");

        Assert.StartsWith("Sign", preview);
        Assert.Contains("Civil 3D symbol: Existing", preview);
        Assert.Contains("FTF action: Rotate existing marker + Label", preview);
        // The PMX sign faces north at rotation zero: facing 135 is a 225 degree marker.
        Assert.Contains("Rotation: 225° (faces 135° azimuth)", preview);
        Assert.Contains("Label: SIGN", preview);
        Assert.Contains("Tag: S#", preview);
    }

    [Fact]
    public void PreviewNeverClaimsToCreateGeometry()
    {
        var cfg = RulesConfig.Load(Resolve().ActivePath);
        foreach (var desc in new[] { "PP 1234", "SN 135", "CON 18 . 25", "CB 4", "PP" })
        {
            var preview = FeaturePreview.Render(Parse(cfg, desc), null);
            Assert.DoesNotContain("create", preview, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("insert", preview, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------- every part of a code rule

    private static RuleDocument Shipped() => RuleDocument.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));

    private static string ShippedText() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules.json"));

    private static string Where(string name, string id) => RuleDocument.PointFeaturesPrefix + name + " (rule '" + id + "'): ";

    [Fact]
    public void AnEditThatChangesNothingWritesNothing()
    {
        var doc = Shipped();
        var before = doc.Render();
        foreach (var id in doc.RuleIds())
            doc.ApplyEdit(doc.GetEditable(id)!);
        Assert.Equal(before, doc.Render());
    }

    [Fact]
    public void EveryPartOfTheShippedRulesReads()
    {
        var doc = Shipped();
        var tree = doc.GetEditable("tree")!;
        Assert.Equal(20, tree.Species.Count);
        Assert.Equal(new KeyValuePair<string, string>("CON", "CONIFER"), tree.Species.Single(p => p.Key == "CON"));
        Assert.Equal("CLUSTER OF {trunk.count} STEMS, AVG {trunk}\" {species}", tree.ClusterFormat);
        Assert.Equal(new[] { "drip", "trunk" }, tree.Checks.Select(c => c.Field));
        Assert.Equal(2, tree.Checks[0].Min);
        Assert.Equal(80, tree.Checks[0].Max);
        Assert.True(tree.HasDripline);
        Assert.Equal("drip", tree.DripRadiusFrom);
        Assert.True(tree.DripUnify);
        Assert.False(tree.HasRotation);
        Assert.False(tree.HasNotes);

        var sign = doc.GetEditable("sn-sign")!;
        Assert.True(sign.HasRotation);
        Assert.Equal("rotation", sign.RotationFrom);
        Assert.Equal("Azimuth", sign.RotationMode);
        Assert.True(sign.HasNotes);
        Assert.Equal("Keep", sign.NotesUnknown);
        Assert.Equal("message", sign.NotesUnknownGroup);
        Assert.Equal("AsShot", Assert.Single(sign.NoteGroups).Order);
        Assert.Equal(" ", sign.NoteGroups[0].Joiner);

        var pole = doc.GetEditable("pole")!;
        Assert.Equal("Flag", pole.NotesUnknown);
        Assert.Equal("other", pole.NotesUnknownGroup);
        Assert.Equal("{ft}'", pole.NoteTerms[0].Text);
        Assert.Equal("height", pole.NoteTerms[0].Group);
        Assert.Empty(pole.NoteOrder);
    }

    [Fact]
    public void TheMatchPatternIsWrittenAndIsWhatTheParserUses()
    {
        var doc = Shipped();
        var mailbox = doc.GetEditable("mailbox")!;
        mailbox.Match = "^(?<code>MB|MBX)\\s+(?<number>[0-9][0-9A-Z.-]*)(?<mods>(?:\\s+[A-Z0-9-]+)*)\\s*$";
        doc.ApplyEdit(mailbox);
        Assert.Empty(doc.Validate());

        var p = Parse(doc.CompileCandidate(), "MBX 12");
        Assert.Equal("mailbox", p.RuleId);
        Assert.Equal("MAILBOX 12", p.LabelText);
        Assert.Equal(mailbox.Match, doc.GetEditable("mailbox")!.Match);
    }

    [Theory]
    [InlineData("^(?<code>MB[", "the match pattern is an invalid regex")]
    [InlineData("", "there is no match pattern")]
    [InlineData(".*", "matches any description at all")]
    [InlineData("(?<code>MB)?", "matches any description at all")]
    [InlineData("^(?<code>{SPECIES})\\s+(?<number>[0-9]+)$", "uses {SPECIES} but no species are listed")]
    public void AMatchPatternMistakeIsNamedWithItsFeature(string match, string expected)
    {
        var doc = Shipped();
        var mailbox = doc.GetEditable("mailbox")!;
        mailbox.Match = match;
        doc.ApplyEdit(mailbox);
        Assert.Contains(doc.Validate(), p => p.StartsWith(Where("Mailbox", "mailbox"), StringComparison.Ordinal) && p.Contains(expected));
    }

    [Fact]
    public void ReadPatternSaysWhatAPatternMatches()
    {
        var tree = RuleDocument.ReadPattern(Shipped().GetEditable("tree")!.Match, new[] { "CON", "FIR" });
        Assert.Null(tree.Problem);
        Assert.True(tree.UsesSpecies);
        Assert.True(tree.ReadsTrunk);
        Assert.Equal(new[] { "CON", "FIR" }, tree.Codes);
        Assert.Contains("drip", tree.Fields);

        var pole = RuleDocument.ReadPattern("^(?<code>PP)(?<mods>(?:\\s+\\S+)+)\\s*$", null);
        Assert.Equal(new[] { "PP" }, pole.Codes);
        Assert.True(pole.ReadsWords);
        Assert.False(pole.UsesSpecies);
        Assert.False(pole.ReadsTrunk);

        // Matching a blank description is not matching everything.
        Assert.Null(RuleDocument.ReadPattern("^(?<code>X)?$", null).Problem);
    }

    [Fact]
    public void SpeciesAreEditedAndTheTreePatternFollowsThem()
    {
        var doc = Shipped();
        var tree = doc.GetEditable("tree")!;
        tree.Species.RemoveAll(p => p.Key == "WIL");
        tree.Species[tree.Species.FindIndex(p => p.Key == "CON")] = new KeyValuePair<string, string>("CON", "CONIFER TREE");
        tree.Species.Add(new KeyValuePair<string, string>("SPR", "SPRUCE"));
        doc.ApplyEdit(tree);
        Assert.Empty(doc.Validate());

        var cfg = doc.CompileCandidate();
        Assert.Equal("12\" SPRUCE", Parse(cfg, "SPR 12 . 10").LabelText);
        Assert.Equal("18\" CONIFER TREE", Parse(cfg, "CON 18 . 25").LabelText);
        Assert.True(Parse(cfg, "WIL 12 . 10").Unhandled);              // no longer a tree
        var rule = cfg.Codes.Single(c => c.Id == "tree");
        Assert.Equal("ALDER", rule.Species["ALD"]);                    // the rest kept
        Assert.Equal(20, rule.Species.Count);
        Assert.Contains("SPR", RuleDocument.LiteralCodes(rule));
    }

    [Theory]
    [InlineData("RED OAK", "ROAK", "species code 'RED OAK' must be one plain code")]
    [InlineData("ROAK", "", "species ROAK has no name")]
    public void ASpeciesMistakeIsNamed(string code, string name, string expected)
    {
        var doc = Shipped();
        var tree = doc.GetEditable("tree")!;
        tree.Species.Add(new KeyValuePair<string, string>(code, name));
        doc.ApplyEdit(tree);
        Assert.Contains(doc.Validate(), p => p.StartsWith(Where("Tree", "tree"), StringComparison.Ordinal) && p.Contains(expected));
    }

    [Fact]
    public void ASpeciesListedTwiceIsRefusedBeforeAnythingIsWritten()
    {
        var doc = Shipped();
        var before = doc.Render();
        var tree = doc.GetEditable("tree")!;
        tree.Description = "Trees";
        tree.Species.Add(new KeyValuePair<string, string>("con", "CONE"));
        var ex = Assert.Throws<ConfigException>(() => doc.ApplyEdit(tree));
        Assert.Contains("species CON listed twice", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(RuleDocument.PointFeaturesPrefix, ex.Message);
        Assert.Equal(before, doc.Render());
    }

    [Fact]
    public void EmptyingTheSpeciesOfATreeIsNamed()
    {
        var doc = Shipped();
        var tree = doc.GetEditable("tree")!;
        tree.Species.Clear();
        doc.ApplyEdit(tree);
        Assert.DoesNotContain("\"ALD\"", doc.Render());
        Assert.Contains(doc.Validate(), p => p.StartsWith(Where("Tree", "tree"), StringComparison.Ordinal) && p.Contains("no species are listed"));
    }

    [Fact]
    public void TheClusterLabelIsWrittenAndUsedForTwoOrMoreStems()
    {
        var doc = Shipped();
        var tree = doc.GetEditable("tree")!;
        tree.ClusterFormat = "{trunk.count} STEM {species}";
        doc.ApplyEdit(tree);
        Assert.Empty(doc.Validate());
        var cfg = doc.CompileCandidate();
        Assert.Equal("2 STEM CONIFER", Parse(cfg, "CON 6 8 . 20").LabelText);
        Assert.Equal("6\" CONIFER", Parse(cfg, "CON 6 . 20").LabelText);

        tree = doc.GetEditable("tree")!;
        tree.ClusterFormat = "";
        doc.ApplyEdit(tree);
        Assert.DoesNotContain("clusterFormat", doc.Render());
        Assert.Equal("7\" CONIFER", Parse(doc.CompileCandidate(), "CON 6 8 . 20").LabelText);   // the label text, averaged
    }

    [Fact]
    public void AClusterLabelNamingAnUnknownFieldIsCaught()
    {
        var doc = Shipped();
        var tree = doc.GetEditable("tree")!;
        tree.ClusterFormat = "{girth} {species}";
        doc.ApplyEdit(tree);
        Assert.Contains(doc.Validate(), p => p.Contains("cluster label") && p.Contains("{girth}"));
    }

    [Fact]
    public void RangeChecksAreEditedAndRun()
    {
        var doc = Shipped();
        var tree = doc.GetEditable("tree")!;
        tree.Checks[0].Max = 50;
        tree.Checks.Add(new EditableRangeCheck { Field = "trunk.count", Max = 3, Severity = "Error", Message = "More than three stems." });
        doc.ApplyEdit(tree);
        Assert.Empty(doc.Validate());

        var cfg = doc.CompileCandidate();
        Assert.Equal(3, cfg.Codes.Single(c => c.Id == "tree").Validate.Count);       // replaced, not added to
        Assert.Contains(Parse(cfg, "CON 18 . 60").Diagnostics, d => d.Severity == Severity.Warning && d.Message.StartsWith("Drip radius is outside"));
        Assert.DoesNotContain(Parse(cfg, "CON 18 . 45").Diagnostics, d => d.Code == "RANGE");
        var many = Parse(cfg, "CON 6 8 10 12 . 20");
        Assert.True(many.HasErrors);
        Assert.Contains(many.Diagnostics, d => d.Message == "More than three stems. (got 4)");
        // The edited entry kept its own message.
        Assert.Contains("\"message\": \"Drip radius is outside the expected 2-80 ft range.\"", doc.Render());
    }

    [Fact]
    public void RemovingARangeCheckKeepsTheOthersAsTheyWere()
    {
        var doc = Shipped();
        var tree = doc.GetEditable("tree")!;
        tree.Checks.RemoveAt(0);
        doc.ApplyEdit(tree);
        var check = Assert.Single(doc.CompileCandidate().Codes.Single(c => c.Id == "tree").Validate);
        Assert.Equal("trunk", check.Field);
        Assert.Equal("Trunk diameter is outside the expected 1-120 in range.", check.Message);
        Assert.Equal(0, doc.GetEditable("tree")!.Checks[0].SourceIndex);

        tree = doc.GetEditable("tree")!;
        tree.Checks.Clear();
        doc.ApplyEdit(tree);
        Assert.Empty(doc.CompileCandidate().Codes.Single(c => c.Id == "tree").Validate);
        var written = Newtonsoft.Json.Linq.JObject.Parse(doc.Render())["codes"]!.Single(c => (string?)c["id"] == "tree");
        Assert.Null(written["validate"]);                                     // no checks: no list at all
        Assert.Empty(doc.Validate());
    }

    public static IEnumerable<object[]> RangeCheckMistakes()
    {
        yield return new object[] { "", null!, 3.0, null!, "range check 3 names no field" };
        yield return new object[] { "girth", null!, 3.0, null!, "range check 3 reads 'girth', which this feature never fills" };
        yield return new object[] { "drip", null!, null!, null!, "range check 3 (drip) has no lowest or highest value" };
        yield return new object[] { "drip", 80.0, 2.0, null!, "the lowest value 80 is above the highest 2" };
        yield return new object[] { "drip", null!, 2.0, "Severe", "range check 3: the severity must be Info, Warning or Error" };
    }

    [Theory]
    [MemberData(nameof(RangeCheckMistakes))]
    public void ARangeCheckMistakeIsNamed(string field, double? min, double? max, string? severity, string expected)
    {
        var doc = Shipped();
        var tree = doc.GetEditable("tree")!;
        tree.Checks.Add(new EditableRangeCheck { Field = field, Min = min, Max = max, Severity = severity ?? "Warning" });
        doc.ApplyEdit(tree);
        Assert.Contains(doc.Validate(), p => p.StartsWith(Where("Tree", "tree"), StringComparison.Ordinal) && p.Contains(expected));
    }

    [Fact]
    public void ARangeCheckNumberThatIsNotANumberIsNamed()
    {
        var doc = RuleDocument.FromText(ShippedText().Replace("\"min\": 2,", "\"min\": \"two\","));
        Assert.Contains(doc.Validate(), p => p.StartsWith(Where("Tree", "tree"), StringComparison.Ordinal) &&
                                             p.Contains("range check 1: its lowest value 'two' is not a number"));
    }

    [Fact]
    public void TheNotesVocabularyIsEditedAndReachesTheLabel()
    {
        var doc = Shipped();
        var pole = doc.GetEditable("pole")!;
        pole.NotesUnknown = "Refuse";
        pole.NoteTerms.Add(new EditableNoteTerm { Match = "LTE|CELL", Text = "CELL", Group = "attach" });
        pole.NoteTerms.Single(t => t.Group == "material" && t.Text == "WOOD").Text = "WD";
        pole.NoteGroups.Add(new EditableNoteGroup { Name = "attach", Order = "AsShot", Joiner = " / " });
        pole.NoteOrder.AddRange(new[] { "attach", "number" });
        doc.ApplyEdit(pole);
        Assert.Empty(doc.Validate());

        var cfg = doc.CompileCandidate();
        var p = Parse(cfg, "PP 1234 TEL LTE XFMR WOOD");
        Assert.Equal("POLE 1234 WD\\PW/ TEL / CELL / XFMR", p.LabelText);
        Assert.Equal("TEL / CELL / XFMR 1234 WD", p.Fields["notes"]);      // the {notes} order
        Assert.True(Parse(cfg, "PP 1234 FOO").HasErrors);                     // refused, not flagged
        Assert.Contains("//notes", doc.Render());
        Assert.Equal(20, cfg.Codes.Single(c => c.Id == "pole").Notes!.Terms.Count);
    }

    [Fact]
    public void ReorderingTermsChangesWhichReadsFirst()
    {
        var doc = Shipped();
        var pole = doc.GetEditable("pole")!;
        var number = pole.NoteTerms.Single(t => t.Group == "number");
        pole.NoteTerms.Remove(number);
        pole.NoteTerms.Insert(0, number);
        doc.ApplyEdit(pole);
        Assert.Empty(doc.Validate());
        var cfg = doc.CompileCandidate();
        Assert.Equal("number", cfg.Codes.Single(c => c.Id == "pole").Notes!.Terms[0].Group);
        Assert.Equal(new[] { 0, 1, 2 }, doc.GetEditable("pole")!.NoteTerms.Take(3).Select(t => t.SourceIndex));
        Assert.Equal("number", doc.GetEditable("pole")!.NoteTerms[0].Group);
    }

    [Fact]
    public void NotesAreAddedToAFeatureAndTakenOffAgain()
    {
        var doc = Shipped();
        var mailbox = doc.GetEditable("mailbox")!;
        Assert.True(Parse(doc.CompileCandidate(), "MB 12 BRS").HasErrors);     // an unknown modifier today
        mailbox.HasNotes = true;
        mailbox.NotesUnknown = "Keep";
        mailbox.NoteTerms.Add(new EditableNoteTerm { Match = "BRS|BRASS", Text = "BRASS", Group = "material" });
        mailbox.LabelFormat = "MAILBOX {number}[ {material}][ {other}]";
        doc.ApplyEdit(mailbox);
        Assert.Empty(doc.Validate());
        var cfg = doc.CompileCandidate();
        Assert.Equal("MAILBOX 12 BRASS", Parse(cfg, "MB 12 BRS").LabelText);
        Assert.Equal("MAILBOX 12 GREEN", Parse(cfg, "MB 12 GREEN").LabelText);
        Assert.Equal(UnknownNoteMode.Keep, cfg.Codes.Single(c => c.Id == "mailbox").Notes!.Unknown);

        mailbox = doc.GetEditable("mailbox")!;
        mailbox.HasNotes = false;
        mailbox.LabelFormat = "MAILBOX {number}";
        doc.ApplyEdit(mailbox);
        Assert.Null(doc.CompileCandidate().Codes.Single(c => c.Id == "mailbox").Notes);
        Assert.Equal(Shipped().Render(), doc.Render());
    }

    public static IEnumerable<object[]> NoteMistakes()
    {
        yield return new object[] { "blank term", "note term 20 has no words to match" };
        yield return new object[] { "bad regex", "note term 20 ('(LTE') is an invalid regex" };
        yield return new object[] { "matches nothing", "note term 20 ('X?') matches nothing at all" };
        yield return new object[] { "no group", "note term 20 ('LTE') names no group" };
        yield return new object[] { "spaced group", "group 'cell site' cannot be named in a label" };
        yield return new object[] { "no unknown group", "no group is named to keep them in" };
        yield return new object[] { "unused group", "note group 'colour' has settings, but no term" };
        yield return new object[] { "bad order", "note group 'attach': the order must be Vocabulary or AsShot" };
        yield return new object[] { "order typo", "the {notes} order lists 'atach', but no term" };
        yield return new object[] { "order twice", "the {notes} order lists 'owner' twice" };
        yield return new object[] { "bad unknown", "what happens to a word in no term must be Keep, Flag or Refuse" };
    }

    [Theory]
    [MemberData(nameof(NoteMistakes))]
    public void ANotesMistakeIsNamed(string mistake, string expected)
    {
        var doc = Shipped();
        var pole = doc.GetEditable("pole")!;
        switch (mistake)
        {
            case "blank term": pole.NoteTerms.Add(new EditableNoteTerm { Match = "", Text = "X", Group = "attach" }); break;
            case "bad regex": pole.NoteTerms.Add(new EditableNoteTerm { Match = "(LTE", Group = "attach" }); break;
            case "matches nothing": pole.NoteTerms.Add(new EditableNoteTerm { Match = "X?", Group = "attach" }); break;
            case "no group": pole.NoteTerms.Add(new EditableNoteTerm { Match = "LTE", Group = "" }); break;
            case "spaced group": pole.NoteTerms.Add(new EditableNoteTerm { Match = "LTE", Group = "cell site" }); break;
            case "no unknown group": pole.NotesUnknownGroup = ""; break;
            case "unused group": pole.NoteGroups.Add(new EditableNoteGroup { Name = "colour", Order = "AsShot" }); break;
            case "bad order": pole.NoteGroups.Add(new EditableNoteGroup { Name = "attach", Order = "Sorted" }); break;
            case "order typo": pole.NoteOrder.Add("atach"); break;
            case "order twice": pole.NoteOrder.AddRange(new[] { "owner", "owner" }); break;
            case "bad unknown": pole.NotesUnknown = "Maybe"; break;
        }
        doc.ApplyEdit(pole);
        Assert.Contains(doc.Validate(), p => p.StartsWith(Where("Power Pole", "pole"), StringComparison.Ordinal) && p.Contains(expected));
    }

    [Fact]
    public void TheRotationIsEditedRemovedAndAddedBack()
    {
        var doc = Shipped();
        var sign = doc.GetEditable("sn-sign")!;
        sign.RotationMode = "Cartesian";
        doc.ApplyEdit(sign);
        Assert.Empty(doc.Validate());
        var p = Parse(doc.CompileCandidate(), "SN 135 STOP");
        Assert.False(p.RotationIsAzimuth);
        Assert.Equal(135.0, p.RotationDegrees);

        sign = doc.GetEditable("sn-sign")!;
        sign.HasRotation = false;
        doc.ApplyEdit(sign);
        Assert.Null(Parse(doc.CompileCandidate(), "SN 135 STOP").RotationDegrees);

        sign = doc.GetEditable("sn-sign")!;
        sign.HasRotation = true;
        sign.RotationFrom = "rotation";
        sign.RotationMode = "Azimuth";
        doc.ApplyEdit(sign);
        Assert.Empty(doc.Validate());
        Assert.Equal(315.0, Parse(doc.CompileCandidate(), "SN 135 STOP").RotationDegrees);
    }

    [Fact]
    public void ARotationMistakeIsNamed()
    {
        var doc = Shipped();
        var hydrant = doc.GetEditable("hydrant")!;
        hydrant.HasRotation = true;
        hydrant.RotationFrom = "angle";
        doc.ApplyEdit(hydrant);
        Assert.Contains(doc.Validate(), p => p.Contains("'hydrant'") && p.Contains("rotation reads field 'angle'"));

        hydrant = doc.GetEditable("hydrant")!;
        hydrant.RotationFrom = "";
        doc.ApplyEdit(hydrant);
        Assert.Contains(doc.Validate(), p => p.StartsWith(Where("Fire Hydrant", "hydrant"), StringComparison.Ordinal) &&
                                             p.Contains("names no field to read the angle from"));

        var bad =RuleDocument.FromText(ShippedText().Replace("\"mode\": \"Azimuth\"", "\"mode\": \"Compass\""));
        Assert.Contains(bad.Validate(), p => p.StartsWith(Where("Sign", "sn-sign"), StringComparison.Ordinal) &&
                                             p.Contains("the marker angle must be Azimuth or Cartesian (not 'Compass')"));
    }

    [Fact]
    public void TheDriplineIsEditedRemovedAndAdded()
    {
        var doc = Shipped();
        var tree = doc.GetEditable("tree")!;
        tree.DripUnify = false;
        doc.ApplyEdit(tree);
        Assert.Empty(doc.Validate());
        Assert.False(Parse(doc.CompileCandidate(), "CON 18 . 25").DripUnify);
        Assert.Contains("\"unify\": false", doc.Render());

        tree = doc.GetEditable("tree")!;
        tree.HasDripline = false;
        doc.ApplyEdit(tree);
        Assert.Empty(doc.Validate());
        Assert.False(Parse(doc.CompileCandidate(), "CON 18 . 25").HasDripLine);
        Assert.DoesNotContain("dripLine", doc.Render());

        tree = doc.GetEditable("tree")!;
        tree.HasDripline = true;
        tree.DripLayer = "V-VEGE-DRIP-E";
        tree.DripRadiusFrom = "drip";
        tree.DripUnify = true;
        doc.ApplyEdit(tree);
        Assert.Empty(doc.Validate());
        var p = Parse(doc.CompileCandidate(), "CON 18 . 25");
        Assert.True(p.HasDripLine);
        Assert.Equal("V-VEGE-DRIP-E", p.DripLayer);
        Assert.True(p.DripUnify);
        Assert.Equal(25.0, p.DripRadius);
    }

    [Theory]
    [InlineData("", "drip", "names no layer for it")]
    [InlineData("V-VEGE-DRIP-E", "", "names no field to read its radius from")]
    [InlineData("V-VEGE-DRIP-E", "radius", "reads the dripline radius from 'radius', which the match pattern does not capture")]
    public void ADriplineMistakeIsNamed(string layer, string radiusFrom, string expected)
    {
        var doc = Shipped();
        var tree = doc.GetEditable("tree")!;
        tree.DripLayer = layer;
        tree.DripRadiusFrom = radiusFrom;
        doc.ApplyEdit(tree);
        Assert.Contains(doc.Validate(), p => p.StartsWith(Where("Tree", "tree"), StringComparison.Ordinal) && p.Contains(expected));
    }

    [Fact]
    public void ADriplineUnifyThatIsNotTrueOrFalseIsNamed()
    {
        var doc = RuleDocument.FromText(ShippedText().Replace("\"unify\": true", "\"unify\": \"yes\""));
        Assert.Contains(doc.Validate(), p => p.StartsWith(Where("Tree", "tree"), StringComparison.Ordinal) && p.Contains("must be true or false"));
    }

    [Fact]
    public void EveryNewPartSavesAndLoadsAsTheCommandsReadIt()
    {
        var r = Resolve();
        var doc = RuleDocument.Load(r.ActivePath);
        var tree = doc.GetEditable("tree")!;
        tree.Species.Add(new KeyValuePair<string, string>("SPR", "SPRUCE"));
        tree.ClusterFormat = "{trunk.count} STEMS {species}";
        tree.Checks[1].Severity = "Error";
        tree.DripUnify = false;
        doc.ApplyEdit(tree);
        var sign = doc.GetEditable("sn-sign")!;
        sign.RotationMode = "Cartesian";
        sign.NoteGroups[0].Joiner = "-";
        doc.ApplyEdit(sign);
        doc.Save(r.SaveTarget);

        var cfg = RulesConfig.Load(Resolve().ActivePath);
        var t = cfg.Codes.Single(c => c.Id == "tree");
        Assert.Equal("SPRUCE", t.Species["SPR"]);
        Assert.Equal("{trunk.count} STEMS {species}", t.Label.ClusterFormat);
        Assert.Equal(Severity.Error, t.Validate[1].Severity);
        Assert.False(t.DripLine.Unify);
        var s = cfg.Codes.Single(c => c.Id == "sn-sign");
        Assert.Equal(RotationMode.Cartesian, s.Rotation.Mode);
        Assert.Equal("-", s.Notes!.Groups["message"].Joiner);
        Assert.Equal("NO PARKING-ANYTIME SIGN", Parse(cfg, "SN 90 NP ANYTIME").LabelText);
    }

    // ------------------------------------------------------------------ deleting

    [Fact]
    public void DeletingAFeatureRemovesExactlyThatOne()
    {
        var doc = Shipped();
        var others = doc.RuleIds().Where(id => id != "mailbox").ToList();
        var before = Newtonsoft.Json.Linq.JObject.Parse(doc.Render());
        doc.DeleteRule("mailbox");

        Assert.Equal(others, doc.RuleIds());
        Assert.Empty(doc.Validate());
        var after = Newtonsoft.Json.Linq.JObject.Parse(doc.Render());
        before["codes"]!.Single(c => (string?)c["id"] == "mailbox").Remove();
        Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(before, after));      // nothing else in the file moved

        var cfg = doc.CompileCandidate();
        Assert.True(Parse(cfg, "MB 12").Unhandled);
        Assert.Equal("pole", Parse(cfg, "PP 1234").RuleId);
    }

    [Fact]
    public void TheLastFeatureCannotBeDeleted()
    {
        var doc = RuleDocument.FromText(@"{ ""codes"": [ { ""id"": ""x"", ""description"": ""Thing"", ""match"": ""^(?<code>X)\\s+(?<n>[0-9]+)$"" } ] }");
        var ex = Assert.Throws<ConfigException>(() => doc.DeleteRule("x"));
        Assert.Contains("Thing (rule 'x') is the only point feature", ex.Message);
        Assert.Single(doc.RuleIds());
        Assert.Throws<ConfigException>(() => Shipped().DeleteRule("no-such-rule"));
    }

    [Fact]
    public void BeforeDeletingWhatElseNamesTheFeatureIsSaid()
    {
        var doc = Shipped();
        var hydrant = doc.ReferencesTo("hydrant");
        Assert.Contains(hydrant, r => r.Contains("drafting standard") && r.Contains("Fire hydrant") && r.Contains("WFH, FH"));
        Assert.Contains(hydrant, r => r.StartsWith("Symbol rule") && r.Contains("WFH, FH"));

        var b = doc.GetBlockInsert("mailbox")!;
        b.Block = "MBOX";
        doc.ApplyBlockInsert(b);
        doc.SetInsertBlock("mailbox", true);
        Assert.Contains(doc.ReferencesTo("mailbox"), r => r.Contains("inserted block (MBOX"));
        Assert.Empty(doc.ReferencesTo("no-such-rule"));
    }

    [Fact]
    public void DeletionsAdditionsAndEditsMergeOntoTheFileAsItIsNow()
    {
        var staged = Shipped();
        staged.DeleteRule("mailbox");
        var added = staged.AddStarterRule("WMT", "V-UTIL-WATR-TEXT-E");
        var pole = staged.GetEditable("pole")!;
        pole.NotesUnknown = "Keep";
        staged.ApplyEdit(pole);

        // Meanwhile another page saved: a block on the pole, the bench layer.
        var onDisk = Shipped();
        var block = onDisk.GetBlockInsert("pole")!;
        block.Block = "POLE-BLK";
        onDisk.ApplyBlockInsert(block);
        var bench = onDisk.GetBench();
        bench.Layer = "V-BENCH";
        onDisk.ApplyBench(bench);

        foreach (var id in new[] { "mailbox", added, "pole" }) staged.CopyCodeRuleTo(onDisk, id);
        Assert.Empty(onDisk.Validate());
        var cfg = onDisk.CompileCandidate();
        Assert.DoesNotContain(cfg.Codes, c => c.Id == "mailbox");
        Assert.Equal("wmt", cfg.Codes.Last().Id);
        Assert.Equal(UnknownNoteMode.Keep, cfg.Codes.Single(c => c.Id == "pole").Notes!.Unknown);
        Assert.Equal("POLE-BLK", cfg.Codes.Single(c => c.Id == "pole").Block);     // the other page's part kept
        Assert.Equal("V-BENCH", cfg.Bench.Layer);
        Assert.Equal(staged.RuleIds(), onDisk.RuleIds());
    }

    [Fact]
    public void MergingAFeatureNobodyChangedChangesNothing()
    {
        var mine = Shipped();
        var theirs = Shipped();
        var before = theirs.Render();
        foreach (var id in mine.RuleIds()) mine.CopyCodeRuleTo(theirs, id);
        Assert.Equal(before, theirs.Render());
    }

    // ----------------------------------------------------------- the hard boundary

    [Fact]
    public void TheEntireEditingModelCannotTouchCivil3d()
    {
        // The structural guarantee behind "the editor never creates Civil 3D
        // geometry": everything it is made of lives in FieldCodes, an assembly that
        // does not reference AutoCAD or Civil 3D at all. There is no code path from
        // the editor to a drawing.
        var references = typeof(RuleDocument).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        Assert.DoesNotContain(references,
            name => name.StartsWith("Ac", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Aecc", StringComparison.OrdinalIgnoreCase));
    }
}
