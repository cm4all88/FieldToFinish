using FieldCodes;
using FieldCodes.Review;

namespace FieldCodes.Tests;

/// <summary>
/// The Feature Review page is a dry run: a pure function of the parse result that
/// puts words to what FTF would do. These tests pin those words -- especially that
/// they never claim FTF creates the survey geometry Civil 3D owns.
/// </summary>
public sealed class FeatureReviewTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    private readonly FeatureReviewBuilder _builder;

    public FeatureReviewTests(RulesFixture fx)
    {
        _fx = fx;
        _builder = new FeatureReviewBuilder(fx.Config);
    }

    private ReviewRow Review(string desc, string pt = "1")
        => _builder.Build(_fx.Parse(desc, pt));

    // ------------------------------------------------------------ planned actions

    [Fact]
    public void ATreePlansLabelDriplineTagAndTable()
    {
        var row = Review("CON 18 . 25");

        Assert.Equal(ReviewCategory.Tree, row.Category);
        Assert.Equal(ReviewStatus.Ok, row.Status);
        Assert.Contains("Dripline r=25 on V-TREE-DRIP", row.Actions);
        Assert.Contains("Label \"18\" CONIFER\"", row.Actions);
        Assert.Contains("Leader if needed", row.Actions);
        Assert.Contains("Tag T#", row.Actions);
        Assert.Contains("Include in table", row.Actions);
    }

    [Fact]
    public void ASignPlansMarkerRotation_NeverBlockInsertion()
    {
        var row = Review("SN 135");

        Assert.Equal(ReviewCategory.Sign, row.Category);
        Assert.Contains("Rotate existing marker to 315 deg", row.Actions);
        Assert.DoesNotContain(row.Actions, a => a.Contains("Insert block"));
        Assert.DoesNotContain(row.Actions, a => a.Contains("Create"));
    }

    [Fact]
    public void APolePlansALabelOnly_PlusTagAndTable()
    {
        var row = Review("PP 1234");

        Assert.Equal(ReviewCategory.Utility, row.Category);
        Assert.Contains("Label \"POLE 1234\"", row.Actions);
        Assert.Contains("Tag P#", row.Actions);
        Assert.DoesNotContain(row.Actions, a => a.Contains("Dripline"));
        Assert.DoesNotContain(row.Actions, a => a.Contains("Rotate"));
    }

    [Fact]
    public void AControlPointPlansItsCrossReferenceLabel()
    {
        var row = Review("XMAG AKA 1013");

        Assert.Equal(ReviewCategory.Control, row.Category);
        Assert.Contains("Label \"AKA 1013\"", row.Actions);
    }

    [Fact]
    public void ActionsComeInPipelineOrder()
    {
        // Rotation is applied during point finishing, before annotation.
        var row = Review("SN 135");
        var rotate = row.Actions.ToList().FindIndex(a => a.StartsWith("Rotate"));
        var label = row.Actions.ToList().FindIndex(a => a.StartsWith("Label"));
        Assert.True(rotate < label, "rotation should precede labelling");
    }

    // ------------------------------------------------------- the quiet categories

    [Fact]
    public void ABareCodeIsNothingToDo_NotAnError()
    {
        var row = Review("SSCO");

        Assert.Equal(ReviewStatus.NothingToDo, row.Status);
        Assert.Equal(ReviewCategory.NoData, row.Category);
        Assert.Empty(row.Actions);
        Assert.Equal("No finishing required", row.ActionsSummary);
        Assert.Contains("Civil 3D symbol stands as-is", row.Reason);
    }

    [Fact]
    public void LineworkIsOutsideScope_AndSaysWhoOwnsIt()
    {
        var row = Review("EC B");

        Assert.Equal(ReviewStatus.OutsideScope, row.Status);
        Assert.Contains("Civil 3D builds the figures", row.Reason);
    }

    [Fact]
    public void NeverDrawCodesAreIgnoredIntentionally()
    {
        var row = Review("GS");
        Assert.Equal(ReviewStatus.OutsideScope, row.Status);
        Assert.Contains("Ignored intentionally", row.Reason);
    }

    [Fact]
    public void AnUnconfiguredCodeIsNotConfigured_NotAnError()
    {
        var row = Review("QZ 1015");

        Assert.Equal(ReviewStatus.NotConfigured, row.Status);
        Assert.Equal(ReviewCategory.NotConfigured, row.Category);
        Assert.Contains("no rule is configured", row.Reason);
    }

    [Fact]
    public void AParseErrorIsAnError_WithTheParserMessage()
    {
        var row = Review("CON 18");     // missing drip radius

        Assert.Equal(ReviewStatus.Error, row.Status);
        Assert.Contains("drip", row.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AWarningSurvivesAsAWarning()
    {
        var row = Review("CON 18 . 25 DEAD");   // dead flags for review
        Assert.Equal(ReviewStatus.Warning, row.Status);
        Assert.NotEmpty(row.Actions);           // still fully finished
    }

    // -------------------------------------------------------------------- detail

    [Fact]
    public void DetailShowsFieldsModifiersAndResolvedFinishing()
    {
        var row = Review("CON 8 8 10 16 . 28 DEAD");
        var detail = FeatureReviewBuilder.DetailText(row);

        Assert.Contains("Rule: tree", detail);
        Assert.Contains("trunk=10", detail);
        Assert.Contains("Modifiers (priority order): dead", detail);
        Assert.Contains("V-TREE-TEXT-DEAD", detail);
        Assert.Contains("Dripline: radius 28", detail);
        Assert.Contains("never reissued", detail);
    }

    [Fact]
    public void DetailForRotationNamesTheBoundary()
    {
        var detail = FeatureReviewBuilder.DetailText(Review("SN 135"));
        Assert.Contains("rotates the Civil 3D marker; no block is inserted", detail);
    }

    // ------------------------------------------------------------- unknown codes

    [Fact]
    public void UnknownViewGroupsByCodeWithCountsAndReasons()
    {
        var rows = new[]
        {
            Review("QZ 1015", "10"),
            Review("QZ 2020", "11"),
            Review("SSCO", "12"),
            Review("EC B", "13"),
            Review("CON 18", "14"),        // parse error
            Review("CON 18 . 25", "15")    // fine - must not appear
        };

        var groups = CodeGrouping.BuildUnknownView(rows);

        // Errors first, then not-configured, then the quiet ones.
        Assert.Equal("CON", groups[0].Code);
        Assert.Equal(ReviewStatus.Error, groups[0].Status);

        var unk = groups.Single(g => g.Code == "QZ");
        Assert.Equal(2, unk.Count);
        Assert.Equal(new[] { "10", "11" }, unk.ExamplePoints);
        Assert.Equal(ReviewStatus.NotConfigured, unk.Status);

        Assert.Contains(groups, g => g.Code == "SSCO" && g.Status == ReviewStatus.NothingToDo);
        Assert.Contains(groups, g => g.Code == "EC" && g.Status == ReviewStatus.OutsideScope);
        Assert.DoesNotContain(groups, g => g.Example == "CON 18 . 25");
    }

    [Fact]
    public void UnknownViewCapsExamplePointsAtFive()
    {
        var rows = Enumerable.Range(1, 20).Select(i => Review("QZ " + i, i.ToString()));
        var group = CodeGrouping.BuildUnknownView(rows).Single();
        Assert.Equal(20, group.Count);
        Assert.Equal(5, group.ExamplePoints.Count);
    }

    // ---------------------------------------------------------------- dry run

    [Fact]
    public void TheWholeDemoDrawingReviewsWithoutTouchingAnything()
    {
        // Building a review is a pure function of parse results; this exercises every
        // category the real drawing produces.
        var real = new[]
        {
            "CON 18 . 25", "CON 6 8 8 12 18 18 . 30", "EC B", "TBC EP B",
            "CB", "PP", "GS", "XMAG AKA 1013", "QZ 1015"
        };

        foreach (var desc in real)
        {
            var row = Review(desc);
            Assert.NotNull(row.StatusText);
            Assert.NotNull(row.ActionsSummary);
        }
    }
}
