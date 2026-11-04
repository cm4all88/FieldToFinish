using System.Text.RegularExpressions;
using FieldCodes.Easements;
using FieldCodes.Orient;
using FieldCodes.Review;
using FieldCodes.Stamps;

namespace FieldCodes.Tests;

/// <summary>
/// What a drafter sees on the review list for orientation and labels, and that every object FTF
/// makes can be traced to the command that made it (FTFWHAT).
/// </summary>
public sealed class OrientationReviewTests
{
    private static string Repo([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    private static (OrientationResult, Dictionary<OrientationDecision, OrientOutcome>) Sample()
    {
        var std = OrientationStandard.Pmx();
        var sign = new OrientFeature { Id = "101", Code = "SN", Kind = OrientKind.Sign, Location = new P2(0, -20), Description = "SN" };
        var light = new OrientFeature { Id = "102", Code = "LTARM", Kind = OrientKind.StreetLight, Location = new P2(50, -20), Description = "LTARM" };
        var valve = new OrientFeature { Id = "103", Code = "WVL", Kind = OrientKind.WaterValve, Location = new P2(80, -6), Description = "WVL" };
        var result = new OrientationResult();
        var rotated = new OrientationDecision { Feature = sign, Symbol = std.SymbolFor("SN"), Tier = OrientationTier.Alignment, Confidence = 0.7, Azimuth = 270,
                                                Reference = "centre line CL1 (20.0 ft away)", ReferenceDistance = 20 };
        var review = new OrientationDecision { Feature = light, Symbol = std.SymbolFor("LTARM"), Tier = OrientationTier.Review, Azimuth = 0,
                                               ReviewReason = "no arm direction recorded", Reference = "centre line CL1 (20.0 ft away)" };
        var low = new OrientationDecision { Feature = valve, Symbol = std.SymbolFor("WVL"), Tier = OrientationTier.Inferred, Confidence = 0.55, Azimuth = 90 };
        result.Decisions.Add(rotated);
        result.Decisions.Add(review);
        result.Decisions.Add(low);
        result.Connections.Add(new InferredConnection { Kind = "hydrant lateral (inferred)", From = new P2(80, -6), To = new P2(80, -22), Confidence = 0.55,
                                                        FeatureIds = new List<string> { "103", "104" } });
        var outcomes = new Dictionary<OrientationDecision, OrientOutcome>
        {
            { rotated, OrientOutcome.Rotated }, { review, OrientOutcome.Review }, { low, OrientOutcome.BelowThreshold }
        };
        return (result, outcomes);
    }

    [Fact]
    public void AnOrientationReviewItemOpensItsPointsDecision()
    {
        var (result, outcomes) = Sample();
        var handles = new Dictionary<string, string> { { "101", "A1" }, { "102", "A2" }, { "103", "A3" }, { "104", "A4" } };
        var items = ReviewChecks.Orientation(result, outcomes, handles);

        Assert.DoesNotContain(items, i => i.Points == "101");                // turned: nothing to review
        var light = items.Single(i => i.Points == "102");
        Assert.Equal(ReviewKinds.OrientReview, light.Kind);
        Assert.Equal("FTFORIENTPOINT 102", light.FixCommand);                 // Fix: why, Preview, Apply, Override, Recheck
        Assert.Contains("reference: centre line CL1", light.Reason);
        Assert.Contains("A2", light.Handles);
        var valve = items.Single(i => i.Points == "103");
        Assert.Equal("FTFORIENTPOINT 103", valve.FixCommand);
        Assert.Contains("below the minimum", valve.Reason);
    }

    [Fact]
    public void AnInferredConnectionIsAReviewItemNeverADeliverable()
    {
        var (result, outcomes) = Sample();
        var handles = new Dictionary<string, string> { { "103", "A3" }, { "104", "A4" } };
        var item = ReviewChecks.Orientation(result, outcomes, handles).Single(i => i.Kind == ReviewKinds.InferredConnection);
        Assert.Equal("103 104", item.Points);
        Assert.Contains("A3", item.Handles);
        Assert.Contains("A4", item.Handles);
        Assert.Contains("not verified", item.Reason);
        Assert.False(item.Required);
        Assert.Equal(ReviewSeverity.Info, item.Severity);
        Assert.Equal(80, item.X!.Value, 6);
        Assert.Equal(-14, item.Y!.Value, 6);
        var kind = ReviewKinds.All.Single(k => k.Id == ReviewKinds.InferredConnection);
        Assert.Contains("never drawn as utility linework", kind.Action);
    }

    [Fact]
    public void ALabelWithNoClearPositionIsOnTheReviewList()
    {
        var item = ReviewChecks.LabelNotPlaced("57", "2F3", 10, 20, "PP 12");
        Assert.Equal(ReviewKinds.LabelFailed, item.Kind);
        Assert.Equal(ReviewChecks.PointLabelsSource, item.Source);
        Assert.Equal("57", item.Points);
        Assert.Contains("2F3", item.Handles);
        Assert.Contains("left out rather than laid over them", item.Reason);
    }

    [Fact]
    public void EveryKindOfObjectFtfMakesNamesTheCommandThatMadeIt()
    {
        var source = File.ReadAllText(Path.Combine(Repo(), "src", "FieldCodes.Cad", "Ownership.cs"));
        var body = Regex.Match(source, @"public enum FtfEntityKind\s*:\s*short\s*\{(?<body>[\s\S]*?)\n    \}").Groups["body"].Value;
        var names = Regex.Matches(body, @"(?m)^\s*(?<name>[A-Za-z]+)\s*=\s*\d+").Cast<Match>().Select(m => m.Groups["name"].Value).ToList();
        Assert.True(names.Count > 50, "the stamp kinds were not found in Ownership.cs");
        foreach (var name in names) Assert.True(OwnerNames.Knows(name), "FTFWHAT cannot say what made a " + name);
        Assert.Contains("FTFORIENT", OwnerNames.For("OrientQc"));
        Assert.Contains("not plotted", OwnerNames.For("OrientQc"));
    }
}
