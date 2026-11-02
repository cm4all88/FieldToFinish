using FieldCodes;
using FieldCodes.Review;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// The Unknown Codes page separates real problems from setup decisions from normal
/// conditions. These tests pin the split against the shipped grammar: a blank or
/// unreadable description is an Error, data with no rule Needs Configuration, and
/// linework, never-draw, bare and user-ignored codes are Informational -- never
/// counted as unknown.
/// </summary>
public sealed class CodeDiagnosticsTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public CodeDiagnosticsTests(RulesFixture fx) => _fx = fx;

    private CodeDiagnostics Diagnostics(params string[] ignored)
        => new CodeDiagnostics(_fx.Config, ignored);

    private CodeIssue Classify(string description, params string[] ignored)
        => Diagnostics(ignored).Classify(_fx.Parse(description));

    // ------------------------------------------------------------------- errors

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ABlankDescriptionIsAnError(string? description)
    {
        var issue = Diagnostics().Classify(_fx.Parser.Parse("7", description));

        Assert.Equal(CodeIssueCategory.Error, issue.Category);
        Assert.Equal(CodeIssueKind.BlankDescription, issue.Kind);
        Assert.Equal(CodeDiagnostics.BlankCode, issue.Code);
    }

    [Fact]
    public void AnUnreadableModifierIsAParseFailure()
    {
        var issue = Classify("CON 18 . 25 WOBBLY");

        Assert.Equal(CodeIssueCategory.Error, issue.Category);
        Assert.Equal(CodeIssueKind.ParseFailure, issue.Kind);
        Assert.Equal("tree", issue.RuleId);
        Assert.Contains("WOBBLY", issue.Reason);
    }

    [Fact]
    public void AMissingRequiredValueIsInvalidData()
    {
        // The tree rule matched; the dripline radius it needs is missing.
        var issue = Classify("CON 18");

        Assert.Equal(CodeIssueCategory.Error, issue.Category);
        Assert.Equal(CodeIssueKind.InvalidData, issue.Kind);
        Assert.Equal("CON", issue.Code);
        Assert.Contains("drip", issue.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // --------------------------------------------------------- needs configuration

    [Fact]
    public void DataWithoutARuleNeedsConfiguration()
    {
        var issue = Classify("QZ 1015");

        Assert.Equal(CodeIssueCategory.NeedsConfiguration, issue.Category);
        Assert.Equal(CodeIssueKind.NoRule, issue.Kind);
        Assert.Equal("QZ", issue.Code);
        Assert.Contains("Point Features", issue.Action);
    }

    [Fact]
    public void SequenceDigitsGroupUnderTheBaseCode()
    {
        Assert.Equal("QZ", Classify("QZ2 1015").Code);
    }

    // ------------------------------------------------------------- informational

    [Fact]
    public void Civil3DLineworkIsInformational()
    {
        var issue = Classify("EC B");

        Assert.Equal(CodeIssueCategory.Informational, issue.Category);
        Assert.Equal(CodeIssueKind.Linework, issue.Kind);
    }

    [Fact]
    public void ALineFeatureCodeCarryingDataIsLinework_NotUnknown()
    {
        // A configured line feature left off the linework list: its points are still
        // Civil 3D figure points, not an unconfigured point code.
        var cfg = FreshRules();
        cfg.LineworkCodes.RemoveAll(c => string.Equals(c, "RWC", StringComparison.OrdinalIgnoreCase));
        cfg.Compile();
        var parsed = new FieldCodeParser(cfg).Parse("1", "RWC 2.5 HIGH");
        Assert.True(parsed.Unhandled);

        var issue = new CodeDiagnostics(cfg, null).Classify(parsed);

        Assert.Equal(CodeIssueCategory.Informational, issue.Category);
        Assert.Equal(CodeIssueKind.Linework, issue.Kind);
    }

    [Fact]
    public void NeverDrawCodesAreInformational()
    {
        var issue = Classify("GS");

        Assert.Equal(CodeIssueCategory.Informational, issue.Category);
        Assert.Equal(CodeIssueKind.IgnoredByRules, issue.Kind);
    }

    [Fact]
    public void BareCodesAreInformational()
    {
        var issue = Classify("SSCO");

        Assert.Equal(CodeIssueCategory.Informational, issue.Category);
        Assert.Equal(CodeIssueKind.BareCode, issue.Kind);
    }

    [Fact]
    public void ACodeIgnoredByYouIsInformational_NotUnknown()
    {
        var issue = Classify("QZ 1015", "qz");

        Assert.Equal(CodeIssueCategory.Informational, issue.Category);
        Assert.Equal(CodeIssueKind.IgnoredByUser, issue.Kind);
        Assert.Equal("QZ", issue.Code);
    }

    [Fact]
    public void IgnoringNeverHidesAConfiguredRule()
    {
        // Ignoring CON must not turn a tree that failed to parse into a quiet one.
        var issue = Classify("CON 18", "CON");
        Assert.Equal(CodeIssueCategory.Error, issue.Category);

        Assert.Equal(CodeIssueKind.Processed, Classify("CON 18 . 25", "CON").Kind);
    }

    [Fact]
    public void ProcessedPointsAreNotIssues()
    {
        var clean = Classify("CON 18 . 25");
        Assert.Equal(CodeIssueCategory.None, clean.Category);
        Assert.False(clean.HasWarnings);

        var flagged = Classify("CON 18 . 25 DEAD");
        Assert.Equal(CodeIssueCategory.None, flagged.Category);
        Assert.True(flagged.HasWarnings);
    }

    // ------------------------------------------------------------------- tallies

    private IList<ParsedPoint> Sample() => new[]
    {
        _fx.Parse("CON 18 . 25", "1"),      // processed
        _fx.Parse("CON 18 . 25 DEAD", "2"), // processed, warning
        _fx.Parse("CON 18", "3"),           // invalid value
        _fx.Parse("CON 18 . 25 WOBBLY", "4"), // unreadable
        _fx.Parser.Parse("5", ""),          // blank
        _fx.Parse("QZ 1015", "6"),          // needs configuration
        _fx.Parse("QZ 2020", "7"),          // needs configuration, same code
        _fx.Parse("XQ 5", "8"),             // ignored by you
        _fx.Parse("EC B", "9"),             // linework
        _fx.Parse("GS", "10"),              // never-draw
        _fx.Parse("SSCO", "11")             // bare code
    };

    [Fact]
    public void TheTallyPartitionsEveryPoint()
    {
        var t = Diagnostics("XQ").Tally(Sample());

        Assert.Equal(11, t.Total);
        Assert.Equal(2, t.Processed);
        Assert.Equal(1, t.ProcessedWithWarnings);
        Assert.Equal(3, t.Errors);
        Assert.Equal(1, t.BlankDescriptions);
        Assert.Equal(1, t.ParseFailures);
        Assert.Equal(1, t.InvalidData);
        Assert.Equal(2, t.NeedsConfiguration);
        Assert.Equal(1, t.UnknownCodes);
        Assert.Equal(4, t.SkippedIntentionally);
        Assert.Equal(1, t.IgnoredByUser);
        Assert.Equal(5, t.Eligible);
        Assert.Equal(t.Total, t.Eligible + t.NeedsConfiguration + t.SkippedIntentionally);
    }

    [Fact]
    public void AnIgnoredCodeIsNotCountedAsUnknown()
    {
        var before = Diagnostics().Tally(Sample());
        var after = Diagnostics("XQ", "QZ").Tally(Sample());

        Assert.Equal(2, before.UnknownCodes);
        Assert.Equal(0, after.UnknownCodes);
        Assert.Equal(0, after.NeedsConfiguration);
        Assert.Equal(3, after.IgnoredByUser);
    }

    [Fact]
    public void GroupsPutErrorsFirst_ThenConfiguration_ThenInformational()
    {
        var groups = Diagnostics("XQ").Group(Sample());

        var categories = groups.Select(g => g.Category).ToList();
        Assert.Equal(categories.OrderBy(c => (int)c), categories);
        Assert.DoesNotContain(groups, g => g.Category == CodeIssueCategory.None);

        var qz = groups.Single(g => g.Kind == CodeIssueKind.NoRule);
        Assert.Equal("QZ", qz.Code);
        Assert.Equal(2, qz.Count);
        Assert.Equal(new[] { "6", "7" }, qz.ExamplePoints);
        Assert.Equal(new[] { "QZ 1015", "QZ 2020" }, qz.Samples);

        Assert.Contains(groups, g => g.Kind == CodeIssueKind.IgnoredByUser && g.Code == "XQ");
    }

    [Fact]
    public void IssuesListErrorsWarningsAndConfiguration_WithPointsCodesAndReasons()
    {
        var issues = Diagnostics("XQ").Issues(Sample());

        Assert.Equal(new[] { "2", "3", "4", "5", "6", "7" }, issues.Select(i => i.PointNumber));
        var warning = issues.Single(i => i.PointNumber == "2");
        Assert.Equal(CodeDiagnostics.WarningSeverity, warning.Severity);
        Assert.Equal("CON", warning.Code);
        Assert.False(string.IsNullOrWhiteSpace(warning.Reason));

        Assert.Equal(CodeDiagnostics.ErrorSeverity, issues.Single(i => i.PointNumber == "3").Severity);
        Assert.Equal(CodeDiagnostics.ConfigurationSeverity, issues.Single(i => i.PointNumber == "6").Severity);
        Assert.DoesNotContain(issues, i => i.PointNumber == "8" || i.PointNumber == "9");
    }

    // -------------------------------------------------- review and parser wiring

    [Fact]
    public void TheReviewSaysAUserIgnoredCodeIsYours()
    {
        var cfg = FreshRules();
        var settings = new FtfSettings();
        settings.Diagnostics.Ignore("QZ");
        settings.ApplyTo(cfg);

        var parsed = new FieldCodeParser(cfg).Parse("1", "QZ 1015");
        var row = new FeatureReviewBuilder(cfg, settings.Diagnostics.Codes()).Build(parsed);

        Assert.True(parsed.Ignored);
        Assert.Equal(ReviewStatus.OutsideScope, row.Status);
        Assert.Contains("ignored by you", row.Reason);
        Assert.Equal(CodeIssueKind.IgnoredByUser,
            new CodeDiagnostics(cfg, settings.Diagnostics.Codes()).Classify(parsed).Kind);
    }

    private static RulesConfig FreshRules()
        => RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));
}
