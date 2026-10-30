using System.Text.RegularExpressions;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// The first run of FTFLABELALL on a real drawing: the leader arrows were specks and the label
/// text read as sitting behind its mask. The arrow size setting existed but was never applied;
/// draw order was right in the drawing but banded FTF text by layer and was not regenerated.
/// The CAD side has no test host here, so those are held to the source.
/// </summary>
public sealed class LabelEverythingFixTests
{
    private static string Repo([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Repo() }.Concat(parts).ToArray()));

    // ----------------------------------------------------------------- the arrow

    [Fact]
    public void ASettingsFileFromBeforeArrowsWereSizedLoadsWithThemSized()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ftf-arrow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FtfSettings.FileName);
        File.WriteAllText(path, "{ \"version\": \"1\", \"labels\": { \"textHeightPlotted\": 0.08, " +
                                "\"leaderArrowhead\": false, \"leaderArrowSizePlotted\": 0.06 } }");
        try
        {
            var s = FtfSettings.Load(path);
            Assert.True(s.Labels.SizeLeaderArrows);         // the old key never did anything
            Assert.Equal(0.06, s.Labels.LeaderArrowSizePlotted);
            Assert.Equal(string.Empty, s.Labels.NoteSource);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("PMX SURVEY EXHIBIT.json")]
    [InlineData("PMX SURVEY EXHIBIT TABLES.json")]
    public void TheShippedProfilesSizeArrows(string profile)
    {
        var text = Read("config", "profiles", profile);
        Assert.Contains("\"sizeLeaderArrows\": true", text);
        Assert.DoesNotContain("leaderArrowhead", text);
    }

    [Fact]
    public void PointLabelAndTagLeadersAreSizedAtPlotScale()
    {
        Assert.Contains("CadUtil.SizeArrow(leadered, lp, scale);", Read("src", "FieldCodes.Cad", "LabelCommands.cs"));
        Assert.Contains("CadUtil.SizeArrow(leadered, settings.Labels, scale);", Read("src", "FieldCodes.Cad", "TagCommands.cs"));

        var util = Read("src", "FieldCodes.Cad", "CadUtil.cs");
        Assert.Contains("var size = labels.LeaderArrowSizePlotted * drawingUnitsPerPlotted;", util);
        Assert.Contains("leader.ArrowSize = overall > 0 ? size / overall : size;", util);
    }

    // ----------------------------------------------------------------- the mask

    [Fact]
    public void FtfTextIsBandedOnTopWhateverItsLayer()
    {
        var order = Read("src", "FieldCodes.Cad", "DrawOrderCommands.cs");
        Assert.Matches(@"if \(entity is DBText \|\| entity is MText \|\| entity is MLeader\)\s*return DrawOrderBand\.Label;", order);
        // Read in draw order, so a mask placed under its label stays under it within a band.
        Assert.Contains("foreach (ObjectId id in sortEnts.GetFullDrawOrder(0))", order);
        Assert.DoesNotContain("foreach (ObjectId id in ms)", order);
    }

    [Fact]
    public void TheScreenIsRegeneratedAfterMasksAreOrdered()
    {
        var session = Read("src", "FieldCodes.Cad", "FtfSession.cs");
        Assert.Matches(@"RunInTransaction\(commandName, db, ed, body\);\s*if \(LastRunError == null\) RegenIfPending\(ed\);", session);
        Assert.Contains("ed.Regen();", session);

        foreach (var file in new[] { "TextMask.cs", "LabelCommands.cs", "DrawOrderCommands.cs" })
            Assert.Contains("FtfSession.RegenWhenDone();", Read("src", "FieldCodes.Cad", file));

        // FTFLABELALL regenerates once, after all three stages.
        var all = Read("src", "FieldCodes.Cad", "LabelAllCommand.cs");
        var batch = all.IndexOf("using (FtfSession.Batch())", StringComparison.Ordinal);
        var regen = all.IndexOf("FtfSession.RegenIfPending(ed);", StringComparison.Ordinal);
        Assert.True(batch >= 0 && regen > batch);
    }

    // ----------------------------------------------------------------- the note

    [Fact]
    public void EveryCommandThatParsesSurveyPointsReadsTheirNotes()
    {
        foreach (var file in new[] { "LabelCommands.cs", "TagCommands.cs", "TreeCommands.cs", "DripCommands.cs",
                                     "FtfStatusService.cs", "FtfReviewService.cs" })
        {
            var code = Read("src", "FieldCodes.Cad", file);
            Assert.Contains("CogoNotes.For(db, tr, settings)", code);
            Assert.DoesNotMatch(@"parser\.Parse\([^;]*cogo\.RawDescription\)", code);
        }
    }
}
