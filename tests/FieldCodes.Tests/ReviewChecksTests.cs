using FieldCodes;
using FieldCodes.Easements;
using FieldCodes.Orient;
using FieldCodes.Review;

namespace FieldCodes.Tests;

/// <summary>
/// What goes on the review list from the existing checks: real problems and things needing
/// configuration do; codes ignored on purpose, ordinary Civil 3D linework and bare codes do not.
/// </summary>
public sealed class ReviewChecksTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public ReviewChecksTests(RulesFixture fx) => _fx = fx;

    private ReviewPoint Point(string number, string description)
        => new() { Number = number, Description = description, Handle = "H" + number, X = 1, Y = 2, Parsed = _fx.Parse(description, number) };

    [Fact]
    public void UnknownCodesAreOneItemPerCodeListingThePoints()
    {
        var items = ReviewChecks.Codes(new[] { Point("1", "XYZQ 12 RED"), Point("2", "XYZQ 14"), Point("3", "XYZQ2 9") },
                                       new CodeDiagnostics(_fx.Config, null));
        var item = Assert.Single(items);
        Assert.Equal("code.unknown:XYZQ", item.Id);
        Assert.Equal("1, 2, 3", item.Points);
        Assert.Equal(3, item.Handles.Count);
        Assert.True(item.Required);
        Assert.Contains("3 point(s)", item.Reason);
    }

    [Fact]
    public void BlankAndUnreadableDescriptionsArePerPointErrors()
    {
        var items = ReviewChecks.Codes(new[] { Point("5", ""), Point("4", "CON 18 . 25 WOBBLY"), Point("3", "CON 18") },
                                       new CodeDiagnostics(_fx.Config, null));
        Assert.Contains(items, i => i.Id == "code.unreadable:5" && i.Severity == ReviewSeverity.Error);
        Assert.Contains(items, i => i.Id == "code.unreadable:4");
        Assert.Contains(items, i => i.Id == "code.invalid:3");
    }

    [Fact]
    public void IgnoredCodesLineworkAndBareCodesAreNotReviewItems()
    {
        var items = ReviewChecks.Codes(new[]
        {
            Point("1", "FNC B"),          // Civil 3D linework
            Point("2", "GS"),             // ignored by the rules
            Point("3", "CB"),             // bare code
            Point("4", "XYZQ 12"),        // ignored by the user below
            Point("5", "CON 18 . 25"),    // processed
        }, new CodeDiagnostics(_fx.Config, new[] { "XYZQ" }));
        Assert.Empty(items);
    }

    [Fact]
    public void UnrecognisedWordsAfterALineworkCodeAreFlaggedOncePerWord()
    {
        var points = new[]
        {
            Point("1", "FNC B 3.5"),
            Point("2", "FNC CL"),
            Point("3", "FNC1 CL B"),
            Point("4", "EC RMP B"),
            Point("5", "BLD B EC B BLD1 B"),
            Point("6", "FGP FNC"),
            Point("7", "FNC R 10 20"),
            Point("8", "EP XYZ"),
        };
        var items = ReviewChecks.LineworkModifiers(points, _fx.Config.LineworkCodes, new[] { "FGP" });
        Assert.Equal(new[] { "code.modifier:EP XYZ", "code.modifier:FNC CL" }, items.Select(i => i.Id));
        var cl = items.Single(i => i.Id == "code.modifier:FNC CL");
        Assert.Equal("2, 3", cl.Points);
        Assert.Contains("\"CL\" after FNC", cl.Reason);
    }

    [Fact]
    public void OrientationReviewsAndFailuresBecomeItems()
    {
        OrientationDecision D(string id, OrientKind kind, OrientationTier tier, string reason = null!) => new()
        {
            Feature = new OrientFeature { Id = id, Kind = kind, Location = new P2(10, 20) },
            Tier = tier,
            ReviewReason = reason,
            Confidence = 0.4
        };
        var arrow = D("10075", OrientKind.PavementArrow, OrientationTier.Review, "no road within 40 ft");
        var sign = D("200", OrientKind.Sign, OrientationTier.Inferred);
        var locked = D("300", OrientKind.Hydrant, OrientationTier.Relationship);
        var fine = D("400", OrientKind.Hydrant, OrientationTier.Measured);
        var result = new OrientationResult();
        foreach (var d in new[] { arrow, sign, locked, fine }) result.Decisions.Add(d);
        var outcomes = new Dictionary<OrientationDecision, OrientOutcome>
        {
            [arrow] = OrientOutcome.Review,
            [sign] = OrientOutcome.BelowThreshold,
            [locked] = OrientOutcome.Failed,
            [fine] = OrientOutcome.Rotated,
        };
        var items = ReviewChecks.Orientation(result, outcomes, new Dictionary<string, string> { ["10075"] = "AB" });
        Assert.Equal(new[] { "orient.marking:10075", "orient.review:200", "label.failed:300" }, items.Select(i => i.Id));
        Assert.Equal("no road within 40 ft", items[0].Reason);
        Assert.Equal(new[] { "AB" }, items[0].Handles);
        Assert.Equal("Pavement marking", items[0].FeatureType);
        Assert.All(items, i => Assert.Equal("Orientation", i.Source));
    }

    [Fact]
    public void AnUnreadableFenceHeightIsTiedToItsShot()
    {
        var item = ReviewChecks.FenceHeight("1044", "1F", 5, 6, "\"35\" is not a fence height");
        Assert.Equal("fence.height:1044", item.Id);
        Assert.Equal(new[] { "1F" }, item.Handles);
        Assert.True(item.Required);
        Assert.Equal("fence.height:5.00,6.00", ReviewChecks.FenceHeight(null, null, 5, 6, "x").Id);
    }
}
