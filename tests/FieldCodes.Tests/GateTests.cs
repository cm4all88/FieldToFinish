using FieldCodes.Easements;
using FieldCodes.Linework;
using FieldCodes.Review;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// Gate posts paired into gates and labelled with their width. Scenes are synthetic (no project
/// coordinates), X east and Y north, in feet.
/// </summary>
public sealed class GateTests
{
    private static GatePost Post(string id, double x, double y, string description = "FGP")
        => new() { Id = id, X = x, Y = y, Z = 100, Description = description, Handle = "H" + id };

    private static GateFence Fence(string id, params (double x, double y)[] points)
        => new() { Id = id, Code = "FNC", Layer = "V-SURF-FENC-E", Points = points.Select(p => new P2(p.x, p.y)).ToList() };

    private static GateResult Find(IList<GatePost> posts, IList<GateFence> fences, GateSettings? s = null)
        => GateFinder.Find(posts, fences, s ?? new GateSettings(), null, new[] { "FNC", "FCK", "FBD" });

    // ------------------------------------------------------------- the width rules

    [Theory]
    [InlineData(4.8, "GATE")]
    [InlineData(4.5, "GATE")]
    [InlineData(4.99, "GATE")]
    [InlineData(5.0, "5' GATE")]
    [InlineData(6.0, "6' GATE")]
    [InlineData(5.6, "6' GATE")]
    [InlineData(5.4, "5' GATE")]
    [InlineData(12.0, "12' GATE")]
    [InlineData(20.0, "20' GATE")]
    [InlineData(25.0, "25' GATE")]
    public void WidthsLabelByTheRules(double width, string label)
    {
        var text = GateWidths.Label(width, new GateSettings(), out var kind, out var reason);
        Assert.Equal(label, text);
        Assert.Null(kind);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData(4.2, ReviewKinds.GateNarrow)]
    [InlineData(2.0, ReviewKinds.GateNarrow)]
    [InlineData(25.5, ReviewKinds.GateWide)]
    [InlineData(40.0, ReviewKinds.GateWide)]
    public void WidthsOutOfRangeGoToReview(double width, string expectedKind)
    {
        Assert.Null(GateWidths.Label(width, new GateSettings(), out var kind, out var reason));
        Assert.Equal(expectedKind, kind);
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void ANarrowGateLabelCanBeConfigured()
    {
        var s = new GateSettings { NarrowText = "WALK GATE" };
        Assert.Equal("WALK GATE", GateWidths.Label(3.8, s, out var kind, out _));
        Assert.Null(kind);
    }

    [Fact]
    public void RoundingAndPrecisionAreSettings()
    {
        var s = new GateSettings { RoundToFeet = 0.5, Decimals = 1 };
        Assert.Equal("5.5' GATE", GateWidths.Label(5.6, s, out _, out _));
        Assert.Equal("6' GATE", GateWidths.Label(5.8, s, out _, out _));
        s = new GateSettings { RoundToFeet = 0.1, Decimals = 1, WidthFormat = "GATE ({width}')" };
        Assert.Equal("GATE (12.3')", GateWidths.Label(12.34, s, out _, out _));
    }

    [Fact]
    public void SettingsValidate()
    {
        var problems = new List<string>();
        new GateSettings().Validate(problems);
        Assert.Empty(problems);

        new GateSettings { RoundToFeet = 0.5, Decimals = 0 }.Validate(problems);
        Assert.Single(problems);
        problems.Clear();
        new GateSettings { WidthFormat = "GATE" }.Validate(problems);
        Assert.Single(problems);
        problems.Clear();
        new GateSettings { PostCodes = new List<string>() }.Validate(problems);
        Assert.Single(problems);
    }

    // ------------------------------------------------------------- pairing

    [Fact]
    public void TwoPostsSixFeetApartOnAFenceAreASixFootGate()
    {
        var fence = Fence("F1", (0, 0), (40, 0));
        var r = Find(new[] { Post("101", 10, 0), Post("102", 16, 0) }, new[] { fence });

        var gate = Assert.Single(r.Pairs);
        Assert.Equal(GateDecision.Label, gate.Decision);
        Assert.Equal("6' GATE", gate.Text);
        Assert.Equal("101-102", gate.Key);
        Assert.Equal(13, gate.MidX, 6);
        Assert.Equal(0, gate.MidY, 6);
        Assert.Same(fence, gate.Fence);
        Assert.False(gate.Verified);
        Assert.Contains("post spacing", gate.Basis);
        Assert.Empty(r.Review);
    }

    [Fact]
    public void ANarrowGateOnTheFenceIsPlainGate()
    {
        var r = Find(new[] { Post("1", 10, 0), Post("2", 14.8, 0) }, new[] { Fence("F", (0, 0), (40, 0)) });
        Assert.Equal("GATE", Assert.Single(r.Pairs).Text);
    }

    [Fact]
    public void TheLabelLinesUpWithTheOpening()
    {
        // A fence running north-east: the gate's direction follows the posts.
        var r = Find(new[] { Post("1", 10, 10), Post("2", 14, 14) }, new[] { Fence("F", (0, 0), (30, 30)) });
        var gate = Assert.Single(r.Pairs);
        Assert.Equal(Math.PI / 4, gate.Direction, 6);
        Assert.Equal(12, gate.MidX, 6);
        Assert.Equal(12, gate.MidY, 6);
        Assert.Equal("6' GATE", gate.Text);  // 5.66 ft rounds to 6
    }

    [Fact]
    public void AnOpeningBetweenTwoFenceEndsIsAGate()
    {
        // Chain link stops at post 1 and starts again at post 2: the gap is the gate.
        var r = Find(new[] { Post("1", 20, 0, "FGP FCK E"), Post("2", 32, 0, "FGP FCK B") },
                     new[] { Fence("A", (0, 0), (20, 0)), Fence("B", (32, 0), (60, 0)) });
        var gate = Assert.Single(r.Pairs);
        Assert.Equal("12' GATE", gate.Text);
        Assert.Contains("fence ends", gate.Evidence);
    }

    [Fact]
    public void AGateOnABendingFenceIsStillOneOpening()
    {
        // An irregular wood fence with a small bend between the posts.
        var fence = Fence("F", (0, 0), (10, 0), (13, 0.4), (30, 1));
        var r = Find(new[] { Post("1", 10, 0), Post("2", 16, 0.5) }, new[] { fence });
        Assert.Equal("6' GATE", Assert.Single(r.Pairs).Text);
    }

    [Fact]
    public void PostsAroundACornerAreNotOneGate()
    {
        // An L-shaped fence: posts 8 ft either side of the corner are 11.3 ft apart in a
        // straight line but 16 ft along the fence.
        var fence = Fence("F", (0, 20), (0, 0), (20, 0));
        var r = Find(new[] { Post("1", 0, 8), Post("2", 8, 0) }, new[] { fence });
        Assert.Empty(r.Pairs);
        Assert.Equal(2, r.Review.Count(i => i.Kind == ReviewKinds.GateUnpaired));
    }

    [Fact]
    public void PostsOnTwoDifferentFencesAreNotPaired()
    {
        // Two parallel fences 8 ft apart, a gate post part way along each.
        var r = Find(new[] { Post("1", 20, 0), Post("2", 20, 8) },
                     new[] { Fence("A", (0, 0), (40, 0)), Fence("B", (0, 8), (40, 8)) });
        Assert.Empty(r.Pairs);
        Assert.Equal(new[] { "gate.unpaired:1", "gate.unpaired:2" }, r.Review.Select(i => i.Id).OrderBy(x => x));
    }

    [Fact]
    public void ThreePostsInARowAreAmbiguous()
    {
        var r = Find(new[] { Post("1", 10, 0), Post("2", 16, 0), Post("3", 22.5, 0) },
                     new[] { Fence("F", (0, 0), (40, 0)) });
        Assert.Empty(r.Pairs);
        var item = Assert.Single(r.Review);
        Assert.Equal(ReviewKinds.GateAmbiguous, item.Kind);
        Assert.Equal("1, 2, 3", item.Points);
        Assert.Contains("more than one way", item.Reason);
        Assert.Equal(3, item.Handles.Count);
    }

    [Fact]
    public void APairWhoseEndCouldGoElsewhereIsAmbiguousToo()
    {
        // 1-2 is closest, but 3 is on the same fence next to 2 and would be left alone.
        var r = Find(new[] { Post("1", 10, 0), Post("2", 16, 0), Post("3", 28, 0) },
                     new[] { Fence("F", (0, 0), (40, 0)) });
        Assert.Empty(r.Pairs);
        Assert.Equal(ReviewKinds.GateAmbiguous, Assert.Single(r.Review).Kind);
    }

    [Fact]
    public void TwoSeparateGatesOnOneFenceAreBothLabelled()
    {
        var r = Find(new[] { Post("1", 10, 0), Post("2", 16, 0), Post("3", 60, 0), Post("4", 72, 0) },
                     new[] { Fence("F", (0, 0), (100, 0)) });
        Assert.Equal(new[] { "6' GATE", "12' GATE" }, r.Pairs.Select(p => p.Text));
        Assert.Empty(r.Review);
    }

    [Fact]
    public void ALonePostIsReported()
    {
        var r = Find(new[] { Post("7", 10, 0) }, new[] { Fence("F", (0, 0), (40, 0)) });
        Assert.Empty(r.Pairs);
        var item = Assert.Single(r.Review);
        Assert.Equal("gate.unpaired:7", item.Id);
        Assert.Equal("7", item.Points);
        Assert.Equal(new[] { "H7" }, item.Handles);
        Assert.True(item.Required);
        Assert.Equal("FTFGATE", item.FixCommand);
    }

    [Fact]
    public void PostsAwayFromAnyFenceAreProposedForReviewNotLabelled()
    {
        var posts = new[] { Post("1", 10, 50), Post("2", 16, 50) };
        var r = Find(posts, new[] { Fence("F", (0, 0), (40, 0)) });
        Assert.Empty(r.Pairs);
        var item = Assert.Single(r.Review);
        Assert.Equal(ReviewKinds.GateNoFence, item.Kind);
        Assert.Equal("1, 2", item.Points);

        var s = new GateSettings { PairWithoutFence = true };
        r = Find(posts, new GateFence[0], s);
        Assert.Equal("6' GATE", Assert.Single(r.Pairs).Text);
        Assert.Empty(r.Review);
    }

    [Fact]
    public void AnOutOfRangeWidthIsPairedButReviewedNotLabelled()
    {
        var r = Find(new[] { Post("1", 10, 0), Post("2", 38, 0) }, new[] { Fence("F", (0, 0), (60, 0)) },
                     new GateSettings { SearchFeet = 40 });
        var gate = Assert.Single(r.Pairs);
        Assert.Equal(GateDecision.Review, gate.Decision);
        Assert.Null(gate.Text);
        Assert.Equal(ReviewKinds.GateWide, Assert.Single(r.Review).Kind);
        Assert.Equal("gate.wide:1-2", r.Review[0].Id);

        r = Find(new[] { Post("1", 10, 0), Post("2", 13.5, 0) }, new[] { Fence("F", (0, 0), (60, 0)) });
        Assert.Equal(ReviewKinds.GateNarrow, Assert.Single(r.Review).Kind);
    }

    [Fact]
    public void AWidthTheCrewRecordedIsUsedWhenThePostsAgree()
    {
        var r = Find(new[] { Post("1", 10, 0, "FGP 12"), Post("2", 22.3, 0, "FGP FNC") }, new[] { Fence("F", (0, 0), (60, 0)) });
        var gate = Assert.Single(r.Pairs);
        Assert.Equal("12' GATE", gate.Text);
        Assert.True(gate.Verified);
        Assert.Equal(12, gate.Recorded);
        Assert.Contains("recorded by the crew", gate.Basis);
    }

    [Fact]
    public void ARecordedWidthThatDisagreesWithThePostsIsReviewed()
    {
        var r = Find(new[] { Post("1", 10, 0, "FGP 12'"), Post("2", 16, 0) }, new[] { Fence("F", (0, 0), (60, 0)) });
        var gate = Assert.Single(r.Pairs);
        Assert.Equal(GateDecision.Review, gate.Decision);
        var item = Assert.Single(r.Review);
        Assert.Equal(ReviewKinds.GateWidthDisagrees, item.Kind);
        Assert.Contains("recorded 12 ft", item.Reason);
    }

    [Theory]
    [InlineData("FGP 12", 12.0)]
    [InlineData("FGP 12'", 12.0)]
    [InlineData("FGP 12FT FNC", 12.0)]
    [InlineData("FGP FNC B 5", null)]       // 5 is the fence's height
    [InlineData("FGP 12 FNC B 5", 12.0)]
    [InlineData("FNC B 5 FGP 10", 10.0)]
    [InlineData("FGP 1", null)]             // a post number, not a width
    [InlineData("FGP", null)]
    [InlineData("FNC B 5", null)]
    public void RecordedWidthsAreReadAfterTheGateCode(string description, double? width)
    {
        Assert.Equal(width, GateFinder.RecordedWidth(description, new[] { "FGP" }, new[] { "FNC" }));
    }

    [Theory]
    [InlineData("FGP", true)]
    [InlineData("FGP FNC", true)]
    [InlineData("FNC B FGP", true)]
    [InlineData("fgp1", true)]
    [InlineData("FGPX", false)]
    [InlineData("FNC", false)]
    [InlineData("GATEPOST", false)]
    public void GatePostsAreRecognisedByCodeAndAlias(string description, bool post)
    {
        Assert.Equal(post, GateFinder.IsPost(description, new[] { "FGP" }));
        Assert.True(GateFinder.IsPost("GP B", new[] { "FGP", "GP" }));
    }

    [Fact]
    public void TheHeightOfTheFenceAtAGatePostIsTheFences()
    {
        Assert.Equal(5, LineHeights.Read("FGP FNC B 5", new[] { "FNC" }, new[] { "FGP" }, 20, out _, out _));
        Assert.Equal(5, LineHeights.Read("FNC B 5 FGP 12", new[] { "FNC" }, new[] { "FGP" }, 20, out _, out _));
        Assert.Null(LineHeights.Read("FGP 12", new[] { "FNC" }, new[] { "FGP" }, 20, out _, out _));
        Assert.Equal(3.5, LineHeights.Read("TOE1 FNC1 B 3.5", new[] { "FNC" }, new[] { "FGP" }, 20, out _, out _));
    }

    [Fact]
    public void ConfirmedPostsAreLeftToTheDrafter()
    {
        var r = GateFinder.Find(new[] { Post("1", 10, 0), Post("2", 16, 0), Post("3", 22.5, 0) },
                                new[] { Fence("F", (0, 0), (40, 0)) }, new GateSettings(), new[] { "1" });
        Assert.Equal("7' GATE", Assert.Single(r.Pairs).Text);   // 2-3 (6.5 ft) now, 1 is the drafter's
        Assert.Equal("2-3", r.Pairs[0].Key);
        Assert.Empty(r.Review);
    }

    [Fact]
    public void PostsAreNeverMovedAndTheResultIsTheSameEveryRun()
    {
        var posts = new[] { Post("102", 16, 0), Post("101", 10, 0), Post("201", 60, 0), Post("202", 72, 0) };
        var fences = new[] { Fence("F", (0, 0), (100, 0)) };
        var first = Find(posts, fences);
        var second = Find(posts.Reverse().ToArray(), fences);
        Assert.Equal(first.Pairs.Select(p => p.Key + p.Text), second.Pairs.Select(p => p.Key + p.Text));
        Assert.Equal(10, posts.Single(p => p.Id == "101").X);
        Assert.Equal(100, posts.Single(p => p.Id == "101").Z);
        Assert.Equal("101-102", GateKeys.For("102", "101"));
        Assert.Equal(new[] { "101", "102" }, GateKeys.Posts("101-102"));
    }

    [Fact]
    public void APostShotTwiceCountsOnce()
    {
        var r = Find(new[] { Post("1", 10, 0), Post("1b", 10.02, 0), Post("2", 16, 0) }, new[] { Fence("F", (0, 0), (40, 0)) });
        Assert.Equal("6' GATE", Assert.Single(r.Pairs).Text);
        Assert.Empty(r.Review);
    }

    [Fact]
    public void ADrafterPickedPairUsesTheSameRules()
    {
        var gate = GateFinder.Build(Post("9", 0, 0), Post("8", 0, 20), new GateSettings());
        Assert.Equal("8-9", gate.Key);
        Assert.Equal("20' GATE", gate.Text);
        Assert.Equal("picked by the drafter", gate.Evidence);
    }
}
