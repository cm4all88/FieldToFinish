using FieldCodes;
using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// The TBC-name alias mechanism: a feature rule can list confirmed export names
/// ("Edge of Conc" on EC), and the top-level ignoreLineNames list maps a name to
/// nothing on purpose. Both are exact matches -- trimmed, case-insensitive, never
/// substring, never fuzzy. These tests use in-memory rules because the SHIPPED
/// config deliberately contains no aliases yet: every future entry is an office
/// decision, and the mechanism must not invent one.
/// </summary>
public sealed class LineNameAliasTests
{
    private static LineworkCatalog Catalog(IEnumerable<string>? ignoreNames = null)
    {
        var features = new List<LineFeatureRule>
        {
            new LineFeatureRule
            {
                Code = "EC", Name = "Concrete Edge", Layer = "V-SURF-CONC-E",
                Label = "EDGE OF CONCRETE",
                Aliases = new List<string> { "Edge of Conc" }
            },
            new LineFeatureRule
            {
                Code = "CG", Name = "Curb & Gutter", Layer = "V-SURF-CURB-E",
                Label = "CURB"
            }
        };
        return new LineworkCatalog(features, ignoreNames);
    }

    // ----------------------------------------------------------------- aliases

    [Fact]
    public void AConfiguredAliasIdentifiesItsFeature()
    {
        var row = Catalog().Identify("Polyline", null, "V-TINN-BRKL-E-FEATURES", 40.0,
                                     "Edge of Conc");

        Assert.Equal(LineIdentitySource.TrimbleName, row.Source);
        Assert.Equal("EC", row.Codes);
        Assert.Equal("Concrete Edge", row.FeatureName);
    }

    [Fact]
    public void AliasMatchingIsCaseInsensitiveAndTrimmed()
    {
        Assert.Single(Catalog().FindByTrimbleName("  EDGE OF CONC  "));
        Assert.Single(Catalog().FindByTrimbleName("edge of conc"));
    }

    [Theory]
    [InlineData("Edge of Conc Pad")]      // superstring - no substring matching
    [InlineData("Edge of")]               // substring   - no substring matching
    [InlineData("Edge_of_Conc")]          // not the listed spelling
    public void NothingButTheExactAliasMatches(string name)
    {
        Assert.Empty(Catalog().FindByTrimbleName(name));
    }

    [Fact]
    public void AnAliasStillLosesToAFigureName()
    {
        var row = Catalog().Identify("Survey Figure", "CG1", null, 40.0, "Edge of Conc");
        Assert.Equal(LineIdentitySource.FigureName, row.Source);
        Assert.Equal("CG", row.Codes);
    }

    // ------------------------------------------------------------ ignored names

    [Fact]
    public void AnIgnoredNameIsUnidentifiedOnPurpose()
    {
        var row = Catalog(new[] { "INFO LINE" })
            .Identify("Polyline", null, "V-TINN-BRKL-E-FEATURES", 12.0, "INFO LINE");

        Assert.Equal(LineIdentitySource.None, row.Source);
        Assert.True(row.NameIgnored);
        Assert.Equal("None - TBC name intentionally ignored", row.ProposedAction);
    }

    [Fact]
    public void IgnoringANameNeverSuppressesLayerIdentification()
    {
        // The ignore list maps a NAME to nothing; an entity whose layer identifies
        // it is still identified by that layer.
        var row = Catalog(new[] { "Edge of Something" })
            .Identify("Line", null, "V-SURF-CONC-E", 12.0, "Edge of Something");

        Assert.Equal(LineIdentitySource.Layer, row.Source);
        Assert.False(row.NameIgnored);
    }

    [Fact]
    public void IgnoredMatchingIsExactCaseInsensitiveTrimmed()
    {
        var catalog = Catalog(new[] { " Info Line " });
        Assert.True(catalog.IsIgnoredName("INFO LINE"));
        Assert.False(catalog.IsIgnoredName("INFO"));
        Assert.False(catalog.IsIgnoredName("INFO LINE 2"));
    }

    [Fact]
    public void TheSummaryReportsIgnoredNamesSeparatelyFromUnknowns()
    {
        var catalog = Catalog(new[] { "INFO LINE" });
        var rows = new List<LineworkRow>
        {
            catalog.Identify("Polyline", null, "V-TINN-BRKL-E", 12.0, "INFO LINE"),
            catalog.Identify("Polyline", null, "V-TINN-BRKL-E", 12.0, "Top of Slope")
        };

        var summary = LineworkInventoryCsv.Summary(rows, null);

        Assert.Contains("TBC names intentionally ignored (configured): 1 -- \"INFO LINE\"",
            summary);
        Assert.Contains("Not configured as line features: 1", summary);
        Assert.Contains("\"Top of Slope\"", summary);
    }

    // --------------------------------------------------------- shipped defaults

    [Fact]
    public void TheShippedConfigCarriesTheMechanismButNoEntriesYet()
    {
        // Aliases and ignoreLineNames are office decisions. The factory file ships
        // both hooks EMPTY until each mapping is explicitly confirmed.
        var cfg = RulesConfig.Load(
            Path.Combine(AppContext.BaseDirectory, "rules.json"));

        Assert.NotNull(cfg.IgnoreLineNames);
        Assert.Empty(cfg.IgnoreLineNames);
        Assert.All(cfg.LineFeatures, f => Assert.Empty(f.Aliases));
    }
}
