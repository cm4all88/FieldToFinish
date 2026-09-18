using FieldCodes;
using FieldCodes.Tagging;

namespace FieldCodes.Tests;

public sealed class TaggingTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public TaggingTests(RulesFixture fx) => _fx = fx;

    private IList<ParsedPoint> Points(params (string Pt, string Desc)[] input)
        => input.Select(i => _fx.Parse(i.Desc, i.Pt)).ToList();

    private static Dictionary<string, string> Existing(params (string Pt, string Tag)[] pairs)
        => pairs.ToDictionary(p => p.Pt, p => p.Tag, StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ assignment

    [Fact]
    public void FirstRunNumbersSequentiallyFromOne()
    {
        var tags = new TagAssigner().Assign(
            Points(("10", "CON 18 . 25"), ("20", "DEC 12 . 18"), ("30", "MAP 4 6 . 20")),
            null);

        Assert.Equal(new[] { "T1", "T2", "T3" }, tags.Select(t => t.Text));
        Assert.All(tags, t => Assert.True(t.IsNew));
    }

    [Fact]
    public void PrefixComesFromTheRule()
    {
        var tags = new TagAssigner().Assign(
            Points(("10", "CON 18 . 25"), ("20", "SIGN STOP 135")),
            null);

        Assert.Equal("T1", tags.Single(t => t.PointNumber == "10").Text);
        Assert.Equal("S1", tags.Single(t => t.PointNumber == "20").Text);
    }

    [Fact]
    public void EachPrefixNumbersIndependently()
    {
        var tags = new TagAssigner().Assign(
            Points(("10", "CON 18 . 25"), ("20", "SIGN STOP 90"),
                   ("30", "DEC 12 . 18"), ("40", "SIGN YIELD 45")),
            null);

        Assert.Equal(new[] { "T1", "S1", "T2", "S2" }, tags.Select(t => t.Text));
    }

    // ---------------------------------------------------------------- re-run rules

    [Fact]
    public void ARerunKeepsEveryExistingNumber()
    {
        // The point of the whole exercise: a tag on an issued plan must not move.
        var points = Points(("10", "CON 18 . 25"), ("20", "DEC 12 . 18"), ("30", "MAP 4 . 20"));
        var existing = Existing(("10", "T1"), ("20", "T2"), ("30", "T3"));

        var tags = new TagAssigner().Assign(points, existing);

        Assert.Equal(new[] { "T1", "T2", "T3" }, tags.Select(t => t.Text));
        Assert.All(tags, t => Assert.False(t.IsNew));
    }

    [Fact]
    public void ANewTreeContinuesPastTheHighestNumber()
    {
        var points = Points(("10", "CON 18 . 25"), ("15", "DEC 12 . 18"), ("20", "MAP 4 . 20"));
        var existing = Existing(("10", "T1"), ("20", "T2"));

        var tags = new TagAssigner().Assign(points, existing);

        Assert.Equal("T1", tags.Single(t => t.PointNumber == "10").Text);
        Assert.Equal("T2", tags.Single(t => t.PointNumber == "20").Text);
        Assert.Equal("T3", tags.Single(t => t.PointNumber == "15").Text);   // not T2
        Assert.True(tags.Single(t => t.PointNumber == "15").IsNew);
    }

    [Fact]
    public void ADeletedTreeLeavesAGapRatherThanShufflingEveryTagAfterIt()
    {
        // Point 20 is gone. T2 must not be handed to a different tree, and T3 must
        // stay on the tree that already carries it.
        var points = Points(("10", "CON 18 . 25"), ("30", "MAP 4 . 20"), ("40", "DEC 8 . 12"));
        var existing = Existing(("10", "T1"), ("20", "T2"), ("30", "T3"));

        var tags = new TagAssigner().Assign(points, existing);

        Assert.Equal("T1", tags.Single(t => t.PointNumber == "10").Text);
        Assert.Equal("T3", tags.Single(t => t.PointNumber == "30").Text);
        Assert.Equal("T4", tags.Single(t => t.PointNumber == "40").Text);
        Assert.DoesNotContain(tags, t => t.Text == "T2");
    }

    [Fact]
    public void RepeatedRunsAreStable()
    {
        var points = Points(("10", "CON 18 . 25"), ("20", "DEC 12 . 18"));
        var assigner = new TagAssigner();

        var first = assigner.Assign(points, null);
        var carried = first.ToDictionary(t => t.PointNumber, t => t.Text);
        var second = assigner.Assign(points, carried);
        var third = assigner.Assign(points, second.ToDictionary(t => t.PointNumber, t => t.Text));

        Assert.Equal(first.Select(t => t.Text), third.Select(t => t.Text));
        Assert.All(third, t => Assert.False(t.IsNew));
    }

    [Fact]
    public void AChangedPrefixReassigns()
    {
        // The rule changed and this point is now a sign, so its old T-number no longer
        // applies and it takes the next free S-number.
        var points = Points(("10", "SIGN STOP 90"));
        var tags = new TagAssigner().Assign(points, Existing(("10", "T7")));

        Assert.Equal("S1", tags.Single().Text);
        Assert.True(tags.Single().IsNew);
    }

    // -------------------------------------------------------------- what gets a tag

    [Fact]
    public void ErroredAndUnhandledPointsGetNoTag()
    {
        var points = Points(
            ("10", "CON 18 . 25"),      // fine
            ("20", "CON 18"),           // error: no drip radius
            ("30", "PP"),               // unhandled
            ("40", "GS"));              // never drawn

        var tags = new TagAssigner().Assign(points, null);

        Assert.Single(tags);
        Assert.Equal("10", tags[0].PointNumber);
    }

    [Fact]
    public void StartNumberIsHonoured()
    {
        var tags = new TagAssigner { StartNumber = 100 }
            .Assign(Points(("10", "CON 18 . 25")), null);

        Assert.Equal("T100", tags.Single().Text);
    }

    [Theory]
    [InlineData("T12", "T", 12)]
    [InlineData("S3", "S", 3)]
    [InlineData("TREE-7", "TREE-", 7)]
    [InlineData("42", "", 42)]
    public void TagTextSplitsIntoPrefixAndNumber(string text, string prefix, int number)
    {
        string p;
        int n;
        Assert.True(TagAssigner.TrySplit(text, out p, out n));
        Assert.Equal(prefix, p);
        Assert.Equal(number, n);
    }

    [Theory]
    [InlineData("")]
    [InlineData("T")]
    [InlineData("T1A")]
    public void MalformedTagTextIsRejected(string text)
    {
        string p;
        int n;
        Assert.False(TagAssigner.TrySplit(text, out p, out n));
    }

    // --------------------------------------------------------------------- table

    [Fact]
    public void TableRowsAreInTagOrderNotPointOrder()
    {
        var points = Points(("30", "CON 18 . 25"), ("10", "DEC 12 . 18"), ("20", "MAP 4 . 20"));
        var tags = new TagAssigner().Assign(points, null);

        var table = TagTable.Build(tags, points, TagTable.DefaultColumns(), "TREE SCHEDULE", 0);

        Assert.Equal(new[] { "T1", "T2", "T3" }, table.Rows.Select(r => r[0]));
    }

    [Fact]
    public void TableCarriesTheValuesAnAgencyReviews()
    {
        var points = Points(("10", "CON 8 8 10 16 . 28"));
        var tags = new TagAssigner().Assign(points, null);

        var table = TagTable.Build(tags, points,
            new List<TagTableColumn>
            {
                TagTableColumn.Tag, TagTableColumn.PointNumber, TagTableColumn.Species,
                TagTableColumn.Size, TagTableColumn.StemCount, TagTableColumn.Stems,
                TagTableColumn.DripRadius
            },
            "TREE SCHEDULE", 1);

        var row = table.Rows.Single();
        Assert.Equal("T1", row[0]);
        Assert.Equal("10", row[1]);
        Assert.Equal("CONIFER", row[2]);
        Assert.Equal("10.5", row[3]);                 // unrounded average, 1 decimal
        Assert.Equal("4", row[4]);
        Assert.Equal("8, 8, 10, 16", row[5]);         // raw stems preserved
        Assert.Equal("28", row[6]);
    }

    [Fact]
    public void SingleStemTreesLeaveTheClusterColumnsBlank()
    {
        var points = Points(("10", "CON 18 . 25"));
        var tags = new TagAssigner().Assign(points, null);

        var table = TagTable.Build(tags, points,
            new List<TagTableColumn> { TagTableColumn.StemCount, TagTableColumn.Stems },
            "T", 0);

        Assert.Equal(new[] { "", "" }, table.Rows.Single());
    }

    [Fact]
    public void NotesColumnSurfacesModifiers()
    {
        var points = Points(("10", "CON 18 . 25 DEAD"));
        var tags = new TagAssigner().Assign(points, null);

        var table = TagTable.Build(tags, points,
            new List<TagTableColumn> { TagTableColumn.Tag, TagTableColumn.Notes }, "T", 0);

        Assert.Contains("DEAD", table.Rows.Single()[1]);
    }

    [Fact]
    public void HeadersMatchTheChosenColumns()
    {
        var table = TagTable.Build(new List<TagAssignment>(), new List<ParsedPoint>(),
            new List<TagTableColumn> { TagTableColumn.Tag, TagTableColumn.DripRadius },
            "TREE SCHEDULE", 0);

        Assert.Equal(new[] { "TAG", "DRIP (FT)" }, table.Headers);
        Assert.Equal("TREE SCHEDULE", table.Title);
        Assert.Empty(table.Rows);
    }
}
