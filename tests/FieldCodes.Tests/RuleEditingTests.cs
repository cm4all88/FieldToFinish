using FieldCodes;
using FieldCodes.Editing;

namespace FieldCodes.Tests;

/// <summary>
/// The rule editor's contract: it may only ever change what it explicitly exposes.
/// Comments, unknown properties, unknown rules and untouched siblings must survive
/// every round trip byte-for-byte in structure, and a save can never leave a partial
/// file behind.
/// </summary>
public sealed class RuleEditingTests
{
    private static readonly string RulesPath =
        Path.Combine(AppContext.BaseDirectory, "rules.json");

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ftf-edit-" + Path.GetRandomFileName());
        Directory.CreateDirectory(d);
        return d;
    }

    private static RuleDocument Doc() => RuleDocument.Load(RulesPath);

    // ------------------------------------------------------------------ reading

    [Fact]
    public void ThePoleReadsExactlyAsTheSpecificationDescribes()
    {
        var pole = Doc().GetEditable("pole");

        Assert.Equal("pole", pole.Id);
        Assert.True(pole.Enabled);
        Assert.Equal("POLE {number}", pole.LabelFormat);
        Assert.Equal("V-UTIL-POWR-TEXT", pole.LabelLayer);
        Assert.Equal("P", pole.TagPrefix);
        Assert.Equal("None", pole.RotationSummary);
        Assert.Contains("Civil 3D", pole.SymbolSummary);
        Assert.False(pole.HasDripline);
        Assert.True(pole.InTable);
    }

    [Fact]
    public void EveryShippedRuleIsReadable()
    {
        var doc = Doc();
        foreach (var id in doc.RuleIds())
        {
            var rule = doc.GetEditable(id);
            Assert.NotNull(rule);
            Assert.True(rule.Enabled);
        }
        Assert.Equal(16, doc.RuleIds().Count);
    }

    // -------------------------------------------------------------- round trips

    [Fact]
    public void AnUntouchedDocumentRendersItsCommentsAndEverythingElse()
    {
        var rendered = Doc().Render();

        Assert.Contains("//point-features", rendered);
        Assert.Contains("//sn-sign", rendered);
        Assert.Contains("//lineworkControlCodes", rendered);
        Assert.Contains("clusterFormat", rendered);
        Assert.Contains("stemCountFrom", rendered);
    }

    [Fact]
    public void EditingOneFieldLeavesEverySiblingAlone()
    {
        var doc = Doc();
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "UTILITY POLE {number}";
        doc.ApplyEdit(pole);

        var rendered = doc.Render();

        // The one intended change...
        Assert.Contains("UTILITY POLE {number}", rendered);
        Assert.DoesNotContain("\"POLE {number}\"", rendered);

        // ...and nothing else. Comments, other rules, tree grammar all intact.
        Assert.Contains("//sn-sign", rendered);
        Assert.Contains("CLUSTER OF {trunk.count} STEMS", rendered);
        Assert.Contains("\"tagPrefix\": \"T\"", rendered);
        Assert.Contains("XMAG|XHT|XNL|FMAG", rendered);
    }

    [Fact]
    public void UnknownPropertiesAndUnknownRulesSurviveEditing()
    {
        // A future build added things this build has never heard of.
        var json = File.ReadAllText(RulesPath)
            .Replace("\"id\": \"pole\",",
                     "\"id\": \"pole\", \"futureProperty\": { \"nested\": [1, 2, 3] },")
            .Replace("  \"codes\": [",
                     "  \"futureTopLevel\": \"keep me\",\n  \"codes\": [\n" +
                     "    { \"id\": \"future-rule\", \"match\": \"^(?<code>FUT)\\\\s+(?<x>[0-9]+)$\", " +
                     "\"newMechanism\": true },");

        var doc = RuleDocument.FromText(json);
        var pole = doc.GetEditable("pole");
        pole.TagPrefix = "PW";
        doc.ApplyEdit(pole);

        var rendered = doc.Render();

        Assert.Contains("futureProperty", rendered);
        Assert.Contains("futureTopLevel", rendered);
        Assert.Contains("future-rule", rendered);
        Assert.Contains("newMechanism", rendered);
        Assert.Contains("\"PW\"", rendered);
    }

    [Fact]
    public void DisablingWritesTheFlag_EnablingRemovesIt()
    {
        var doc = Doc();
        var pole = doc.GetEditable("pole");

        pole.Enabled = false;
        doc.ApplyEdit(pole);
        Assert.Contains("\"enabled\": false", doc.Render());

        pole.Enabled = true;
        doc.ApplyEdit(pole);
        Assert.DoesNotContain("\"enabled\"", doc.Render());
    }

    [Fact]
    public void EmptyingTheLabelOfALabelOnlyRuleRemovesTheNode_ButClusterFormatKeepsIt()
    {
        var doc = Doc();

        // The mailbox label carries nothing else: emptying it removes the node.
        var mailbox = doc.GetEditable("mailbox");
        mailbox.LabelFormat = "";
        mailbox.LabelLayer = "";
        doc.ApplyEdit(mailbox);

        // The tree label carries clusterFormat: it must survive even with format gone.
        var tree = doc.GetEditable("tree");
        tree.LabelFormat = "";
        doc.ApplyEdit(tree);

        var rendered = doc.Render();
        Assert.Contains("clusterFormat", rendered);

        var cfg = Newtonsoft.Json.JsonConvert.DeserializeObject<RulesConfig>(rendered)!;
        Assert.Null(cfg.Codes.Single(c => c.Id == "mailbox").Label);
        Assert.NotNull(cfg.Codes.Single(c => c.Id == "tree").Label);
    }

    // ------------------------------------------------------------ enabled behaviour

    [Fact]
    public void ADisabledRuleStopsMatching_ItsCodesFallThrough()
    {
        var doc = Doc();
        var pole = doc.GetEditable("pole");
        pole.Enabled = false;
        doc.ApplyEdit(pole);

        var parser = new FieldCodeParser(doc.CompileCandidate());

        var p = parser.Parse("1", "PP 1234");
        Assert.True(p.Unhandled);              // data with no (enabled) rule
        Assert.Equal("PP", parser.Parse("1", "PP").LabelText);   // symbolLabels, not the rule

        // and nothing else was disturbed
        Assert.Equal("tree", parser.Parse("1", "CON 18 . 25").RuleId);
    }

    [Fact]
    public void AbsentEnabledMeansEnabled()
    {
        Assert.DoesNotContain("\"enabled\"", File.ReadAllText(RulesPath));
        Assert.All(RulesConfig.Load(RulesPath).Codes, c => Assert.True(c.Enabled));
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public void TheShippedRulesValidateClean()
    {
        Assert.Empty(Doc().Validate());
    }

    [Fact]
    public void AnInvalidRegexIsCaught()
    {
        var doc = Doc();
        var pole = doc.GetEditable("pole");
        pole.Match = "^(?<code>PP[)\\s+";
        doc.ApplyEdit(pole);

        Assert.Contains(doc.Validate(), p => p.Contains("invalid regex"));
    }

    [Fact]
    public void AnUnknownPlaceholderIsCaught()
    {
        var doc = Doc();
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "POLE {serial}";
        doc.ApplyEdit(pole);

        Assert.Contains(doc.Validate(), p => p.Contains("{serial}"));
    }

    [Fact]
    public void ALabelWithoutALayerIsCaught()
    {
        var doc = Doc();
        var pole = doc.GetEditable("pole");
        pole.LabelLayer = "";
        doc.ApplyEdit(pole);

        Assert.Contains(doc.Validate(), p => p.Contains("needs a layer"));
    }

    [Fact]
    public void AnInvalidLayerNameIsCaught()
    {
        var doc = Doc();
        var pole = doc.GetEditable("pole");
        pole.LabelLayer = "V-UTIL<POWR>";
        doc.ApplyEdit(pole);

        Assert.Contains(doc.Validate(), p => p.Contains("does not allow"));
    }

    [Fact]
    public void ADigitInATagPrefixIsCaught()
    {
        var doc = Doc();
        var pole = doc.GetEditable("pole");
        pole.TagPrefix = "P1";
        doc.ApplyEdit(pole);

        Assert.Contains(doc.Validate(), p => p.Contains("merge with the tag number"));
    }

    [Fact]
    public void TwoRulesClaimingTheSameCodeAreCaught()
    {
        var doc = Doc();
        var mailbox = doc.GetEditable("mailbox");
        mailbox.Match = "^(?<code>PP|MB)\\s+(?<number>[0-9]+)$";
        doc.ApplyEdit(mailbox);

        var problem = doc.Validate().Single(p => p.Contains("'PP'"));
        Assert.Contains("pole", problem);
        Assert.Contains("mailbox", problem);
    }

    [Fact]
    public void DisablingOneClaimantResolvesTheConflict()
    {
        var doc = Doc();
        var mailbox = doc.GetEditable("mailbox");
        mailbox.Match = "^(?<code>PP|MB)\\s+(?<number>[0-9]+)$";
        doc.ApplyEdit(mailbox);

        var pole = doc.GetEditable("pole");
        pole.Enabled = false;
        doc.ApplyEdit(pole);

        Assert.DoesNotContain(doc.Validate(), p => p.Contains("claimed by more than one"));
    }

    // -------------------------------------------------------------- atomic saving

    [Fact]
    public void SaveWritesAtomically_WithOriginalAndRollingBackups()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "rules.json");
        File.Copy(RulesPath, path);

        // First save: the pristine original is preserved permanently.
        var doc = RuleDocument.Load(path);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "UTILITY POLE {number}";
        doc.ApplyEdit(pole);
        doc.Save(path);

        Assert.True(File.Exists(path + RuleDocument.OriginalBackupSuffix));
        Assert.Contains("POLE {number}",
            File.ReadAllText(path + RuleDocument.OriginalBackupSuffix));
        Assert.Contains("UTILITY POLE", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));

        // Second save: original untouched, .bak holds the previous version.
        var doc2 = RuleDocument.Load(path);
        var pole2 = doc2.GetEditable("pole");
        pole2.TagPrefix = "PW";
        doc2.ApplyEdit(pole2);
        doc2.Save(path);

        Assert.DoesNotContain("UTILITY POLE",
            File.ReadAllText(path + RuleDocument.OriginalBackupSuffix));
        Assert.Contains("UTILITY POLE", File.ReadAllText(path + ".bak"));
        Assert.Contains("\"PW\"", File.ReadAllText(path));
    }

    [Fact]
    public void AnInvalidDocumentIsNeverWritten()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "rules.json");
        File.Copy(RulesPath, path);
        var before = File.ReadAllText(path);

        var doc = RuleDocument.Load(path);
        var pole = doc.GetEditable("pole");
        pole.Match = "^(?<code>PP[)";
        doc.ApplyEdit(pole);

        Assert.Throws<ConfigException>(() => doc.Save(path));

        Assert.Equal(before, File.ReadAllText(path));            // untouched
        Assert.False(File.Exists(path + RuleDocument.OriginalBackupSuffix));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void CancellingIsJustNotSaving()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "rules.json");
        File.Copy(RulesPath, path);
        var before = File.ReadAllText(path);

        var doc = RuleDocument.Load(path);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "SOMETHING ELSE";
        doc.ApplyEdit(pole);
        // no Save -- the document is discarded

        Assert.Equal(before, File.ReadAllText(path));
        var fresh = RuleDocument.Load(path).GetEditable("pole");
        Assert.Equal("POLE {number}", fresh.LabelFormat);
    }

    [Fact]
    public void ASavedEditIsWhatTheParserThenUses()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "rules.json");
        File.Copy(RulesPath, path);

        var doc = RuleDocument.Load(path);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "UTILITY POLE {number}";
        doc.ApplyEdit(pole);
        doc.Save(path);

        var parsed = new FieldCodeParser(RulesConfig.Load(path)).Parse("1", "PP 1234");
        Assert.Equal("UTILITY POLE 1234", parsed.LabelText);

        // The tree demonstration is untouched by an edit to the pole.
        var tree = new FieldCodeParser(RulesConfig.Load(path)).Parse("1", "CON 18 . 25");
        Assert.Equal("18\" CONIFER", tree.LabelText);
    }
}
