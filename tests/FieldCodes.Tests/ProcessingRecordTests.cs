using FieldCodes.Editing;
using FieldCodes.Reporting;
using FieldCodes.Review;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// The last processing run is stored in the drawing as JSON; the Overview reads it
/// back to say when the drawing was last finished and how it ended. A damaged
/// record must never stop the window from opening.
/// </summary>
public sealed class ProcessingRecordTests
{
    private static ProcessingRecord Sample(bool failed = false, bool stopped = false)
    {
        var r = new ProcessingRecord
        {
            FinishedUtc = new DateTime(2026, 10, 30, 21, 32, 0, DateTimeKind.Utc),
            Source = "FTF window",
            User = "surveyor",
            StoppedEarly = stopped,
            Points = 120, Processed = 80, Errors = 3, NeedsConfiguration = 9, Skipped = 28,
            OwnedObjects = 412
        };
        r.Stages.Add(new ProcessingStage { Name = "FTFPOINTS", Succeeded = true });
        r.Stages.Add(new ProcessingStage { Name = "FTFLABELS", Succeeded = !failed, Error = failed ? "boom" : null });
        r.Stages.Add(new ProcessingStage { Name = "FTFORDER", Succeeded = true });
        return r;
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var back = ProcessingRecord.FromJson(Sample(failed: true).ToJson())!;

        Assert.Equal(new DateTime(2026, 10, 30, 21, 32, 0, DateTimeKind.Utc), back.FinishedUtc);
        Assert.Equal(DateTimeKind.Utc, back.FinishedUtc.Kind);
        Assert.Equal("FTF window", back.Source);
        Assert.Equal("surveyor", back.User);
        Assert.Equal(new[] { "FTFPOINTS", "FTFLABELS", "FTFORDER" }, back.Stages.Select(s => s.Name));
        Assert.Equal("boom", back.Stages[1].Error);
        Assert.Equal(120, back.Points);
        Assert.Equal(80, back.Processed);
        Assert.Equal(3, back.Errors);
        Assert.Equal(9, back.NeedsConfiguration);
        Assert.Equal(28, back.Skipped);
        Assert.Equal(412, back.OwnedObjects);
    }

    [Fact]
    public void TheJsonIsSmallAndNamed()
    {
        var json = Sample().ToJson();
        Assert.Contains("\"finishedUtc\":\"2026-10-30T21:32:00Z\"", json);
        Assert.Contains("\"stages\":[", json);
        Assert.DoesNotContain("\"error\"", json);     // only failed stages carry one
        Assert.DoesNotContain("Outcome", json);       // derived, never stored
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"stages\": 5}")]
    public void UnreadableTextIsNoRecord(string? json)
    {
        Assert.Null(ProcessingRecord.FromJson(json));
    }

    [Fact]
    public void AnOlderOrPartialRecordStillReads()
    {
        var r = ProcessingRecord.FromJson("{\"finishedUtc\":\"2026-01-02T03:04:05Z\"}")!;
        Assert.NotNull(r.Stages);
        Assert.Equal(ProcessingOutcome.NothingRun, r.Outcome);
        Assert.Equal(string.Empty, r.Source);
    }

    [Fact]
    public void OutcomesSayHowTheRunEnded()
    {
        Assert.Equal(ProcessingOutcome.Succeeded, Sample().Outcome);
        Assert.Equal("Succeeded - 3 stages", Sample().OutcomeText);

        Assert.Equal(ProcessingOutcome.CompletedWithFailures, Sample(failed: true).Outcome);
        Assert.Equal("Completed with failures - 1 of 3 stages failed", Sample(failed: true).OutcomeText);

        Assert.Equal(ProcessingOutcome.StoppedEarly, Sample(failed: true, stopped: true).Outcome);
        Assert.Equal(ProcessingOutcome.NothingRun, new ProcessingRecord().Outcome);
    }

    [Fact]
    public void DescribeListsStagesAndTheCountsAfterwards()
    {
        var text = Sample(failed: true).Describe();
        Assert.Contains("FAILED  FTFLABELS - boom", text);
        Assert.Contains("OK      FTFPOINTS", text);
        Assert.Contains("120 point(s), 80 processed, 3 error(s), 9 needing configuration", text);
        Assert.Contains("412 FTF object(s)", text);
    }

    // ------------------------------------------------------------ owned objects

    [Fact]
    public void OwnedObjectsAreNamedAndOrderedByCount()
    {
        var byKind = new Dictionary<string, int>
        {
            { "Mask", 380 }, { "Label", 412 }, { "Tag", 25 }, { "EasementHatch", 2 }
        };

        Assert.Equal("412 point labels, 380 label masks, 25 tags, 2 other",
            OwnedObjects.Describe(byKind, 3));
        Assert.Equal("easement hatch", OwnedObjects.NameOf("EasementHatch"));
        Assert.Equal("none", OwnedObjects.Describe(new Dictionary<string, int>(), 3));
    }

    [Fact]
    public void TheDifferenceIsPerKind_AndOnlyWhatChanged()
    {
        var before = new Dictionary<string, int> { { "Label", 10 }, { "Tag", 4 } };
        var after = new Dictionary<string, int> { { "Label", 12 }, { "Tag", 4 }, { "Mask", 12 } };

        var diff = OwnedObjects.Difference(before, after);

        Assert.Equal(2, diff["Label"]);
        Assert.Equal(12, diff["Mask"]);
        Assert.False(diff.ContainsKey("Tag"));
        Assert.Equal(-10, OwnedObjects.Difference(before, null)["Label"]);
    }

    // ------------------------------------------------------------ configuration

    [Fact]
    public void TheSettingsInForceAreNamedWithTheirFile()
    {
        var text = ConfigurationSummary.DescribeSettings(new SettingsResolution
        {
            Source = SettingsSource.Profile,
            ProfileName = "WSDOT",
            LoadedFrom = "C:\\FTF\\profiles\\WSDOT.json"
        });
        Assert.Equal("Drafting profile \"WSDOT\" - C:\\FTF\\profiles\\WSDOT.json", text);

        var missing = ConfigurationSummary.DescribeSettings(new SettingsResolution
        {
            Source = SettingsSource.Defaults,
            MissingProfile = "Client X"
        });
        Assert.StartsWith("Built-in defaults", missing);
        Assert.Contains("\"Client X\", which is not on this computer", missing);

        Assert.Equal("Office configuration - C:\\o\\rules.json",
            ConfigurationSummary.DescribeRules(new RulesResolution
            {
                Source = RulesSource.Office, ActivePath = "C:\\o\\rules.json"
            }));
    }
}
