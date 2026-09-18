using FieldCodes;
using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// The linework identification contract: existing Civil 3D figures and polylines
/// are identified from their figure name or layer against the office Figure Prefix
/// Database mapping, and nothing about the inventory ever proposes recreating
/// survey geometry.
/// </summary>
public sealed class LineworkReviewTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    private readonly LineworkCatalog _catalog;

    public LineworkReviewTests(RulesFixture fx)
    {
        _fx = fx;
        _catalog = new LineworkCatalog(fx.Config.LineFeatures);
    }

    // ------------------------------------------------------------------- catalog

    [Fact]
    public void TheCatalogLoadsFromTheRulesFile()
    {
        Assert.True(_catalog.Count >= 30, "expected the office line-feature catalog");
    }

    [Theory]
    [InlineData("RWC", "Concrete Wall")]
    [InlineData("RWRK", "Rock Wall")]
    [InlineData("FCK", "Chain Link Fence")]
    [InlineData("GRL", "Guardrail")]
    [InlineData("BLD", "Building")]
    [InlineData("LNDY", "Double Yellow Stripe")]
    [InlineData("CG", "Curb & Gutter Flowline")]
    [InlineData("TBC", "Top Back of Curb")]
    [InlineData("EP", "Asphalt Edge")]
    [InlineData("EC", "Concrete Edge")]
    public void EveryRequestedFamilyIsInTheCatalog(string code, string name)
    {
        var feature = _catalog.Features.Single(
            f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(name, feature.Name);
        Assert.StartsWith("V-", feature.Layer);
    }

    [Fact]
    public void EveryLineFeatureCodeIsAlsoALineworkCode()
    {
        // Consistency between the two halves of the model: a code identified as a
        // line feature must also be a code whose POINTS are silenced -- otherwise
        // the shots that created the figure would land in the unknown report.
        foreach (var feature in _catalog.Features)
        {
            var p = _fx.Parse(feature.Code + " B");
            Assert.True(p.Ignored,
                feature.Code + " points should be handled as linework");
        }
    }

    // -------------------------------------------------------------- identification

    [Theory]
    [InlineData("RWC3", "RWC", "Concrete Wall")]      // third wall string
    [InlineData("FCK1", "FCK", "Chain Link Fence")]
    [InlineData("GRL2", "GRL", "Guardrail")]
    [InlineData("EC12", "EC", "Concrete Edge")]
    [InlineData("LNDY", "LNDY", "Double Yellow Stripe")]  // no suffix at all
    public void ASurveyFigureIsIdentifiedByItsName(string figure, string code, string name)
    {
        var row = _catalog.Identify("SurveyFigure", figure, "SOME-OTHER-LAYER", 120.0);

        Assert.Equal(LineIdentitySource.FigureName, row.Source);
        Assert.Equal(code, row.Codes);
        Assert.Equal(name, row.FeatureName);
    }

    [Fact]
    public void APlainPolylineIsIdentifiedByItsLayer()
    {
        var row = _catalog.Identify("Polyline", null, "V-SURF-FENC-CHNL-E", 88.5);

        Assert.Equal(LineIdentitySource.Layer, row.Source);
        Assert.Equal("FCK", row.Codes);
        Assert.Equal("Chain Link Fence", row.FeatureName);
        Assert.Equal("88.5", row.LengthText);
    }

    [Fact]
    public void TheFigureNameBeatsTheLayer()
    {
        // A wall figure that happens to sit on the fence layer is still a wall:
        // the figure name is the survey's own identity for the line.
        var row = _catalog.Identify("SurveyFigure", "RWC1", "V-SURF-FENC-CHNL-E", 10.0);

        Assert.Equal(LineIdentitySource.FigureName, row.Source);
        Assert.Equal("Concrete Wall", row.FeatureName);
    }

    [Fact]
    public void ASharedLayerReportsEveryCandidateInsteadOfGuessing()
    {
        // FNC and FHW both live on V-SURF-FENC-E. With only the layer to go on,
        // the honest answer is both.
        var row = _catalog.Identify("Polyline", null, "V-SURF-FENC-E", 50.0);

        Assert.Equal(LineIdentitySource.Layer, row.Source);
        Assert.Contains("FNC", row.Codes);
        Assert.Contains("FHW", row.Codes);
        Assert.Contains("Fence", row.FeatureName);
    }

    [Fact]
    public void IdentificationIsCaseInsensitive()
    {
        Assert.Equal("Concrete Wall",
            _catalog.Identify("Polyline", "rwc2", null, null).FeatureName);
        Assert.Equal("Guardrail",
            _catalog.Identify("Polyline", null, "v-surf-gral-e", null).FeatureName);
    }

    [Fact]
    public void UnrecognisedLineworkIsReportedAsSuch_NotForced()
    {
        var row = _catalog.Identify("Line", null, "0", 5.0);

        Assert.Equal(LineIdentitySource.None, row.Source);
        Assert.Equal("-", row.FeatureName);
        Assert.Contains("not a configured line feature", row.ProposedAction);
    }

    [Theory]
    [InlineData("RWC3", "RWC")]
    [InlineData("EC12", "EC")]
    [InlineData("LNDY", "LNDY")]
    [InlineData("123", null)]        // digits only is not a code
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TrailingDigitsStripToTheSourceCode(string figure, string expected)
        => Assert.Equal(expected, LineworkCatalog.StripTrailingDigits(figure));

    // ------------------------------------------------------------ product boundary

    [Fact]
    public void ProposedActionsFinishTheExistingLine_NeverRecreateIt()
    {
        var rows = new[]
        {
            _catalog.Identify("SurveyFigure", "RWC1", null, 10.0),
            _catalog.Identify("Polyline", null, "V-SURF-GRAL-E", 20.0),
            _catalog.Identify("Line", null, "0", 1.0)
        };

        foreach (var row in rows)
        {
            Assert.DoesNotContain("create", row.ProposedAction, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("draw", row.ProposedAction, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recreate", row.ProposedAction, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("existing line", rows[0].ProposedAction);
    }

    [Fact]
    public void NoLabellingStandardIsClaimedYet()
    {
        // The standard is deliberately unconfigured; every proposal says so rather
        // than inventing wording that would end up on a plan.
        var row = _catalog.Identify("SurveyFigure", "GRL1", null, 30.0);
        Assert.Contains("standard not configured yet", row.ProposedAction);
    }

    [Fact]
    public void TheIdentificationModelCannotTouchCivil3d()
    {
        // Same structural guarantee as the rule editor: identification lives in
        // FieldCodes, which references no Autodesk assembly at all.
        var references = typeof(LineworkCatalog).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        Assert.DoesNotContain(references,
            name => name.StartsWith("Ac", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Aec", StringComparison.OrdinalIgnoreCase));
    }
}
