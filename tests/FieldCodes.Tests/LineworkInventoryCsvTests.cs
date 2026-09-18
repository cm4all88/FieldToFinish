using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// The inventory report's formatting: the CSV a reviewer opens and the eight-part
/// summary the surveyor reads. Pure formatting over identification rows.
/// </summary>
public sealed class LineworkInventoryCsvTests : IClassFixture<RulesFixture>
{
    private readonly LineworkCatalog _catalog;

    public LineworkInventoryCsvTests(RulesFixture fx)
        => _catalog = new LineworkCatalog(fx.Config.LineFeatures);

    private List<LineworkRow> SampleRows()
    {
        return new List<LineworkRow>
        {
            _catalog.Identify("Survey Figure", "RWC1", "V-SURF-WALL-CONC-E", 42.5),
            _catalog.Identify("Survey Figure", "RWC2", "V-SURF-WALL-CONC-E", 18.0),
            _catalog.Identify("Polyline", null, "V-SURF-FENC-CHNL-E", 88.5),
            _catalog.Identify("Polyline", null, "V-SURF-FENC-E", 12.0),   // ambiguous
            _catalog.Identify("Line", null, "0", 5.0)                      // unidentified
        };
    }

    [Fact]
    public void CsvHasHeaderAndOneRowPerObject()
    {
        var lines = LineworkInventoryCsv.Render(SampleRows())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(LineworkInventoryCsv.Header, lines[0].TrimEnd());
        Assert.Equal(6, lines.Length);
        Assert.Contains("Survey Figure,RWC1,,V-SURF-WALL-CONC-E,Concrete Wall,RWC,figure name,42.5",
                        lines[1]);
    }

    [Fact]
    public void SummaryAnswersEveryInventoryQuestion()
    {
        var owned = new Dictionary<string, int> { { "DripArc", 25 }, { "Leader", 3 } };
        var summary = LineworkInventoryCsv.Summary(SampleRows(), owned);

        Assert.Contains("5 Civil 3D line object(s)", summary);                       // total
        Assert.Contains("Survey Figure 2", summary);                                 // by type
        Assert.Contains("Polyline 2", summary);
        Assert.Contains("by figure name: 2, by Trimble name: 0, by layer: 2, unidentified: 1",
            summary); // method
        Assert.Contains("Concrete Wall (RWC)", summary);                             // features
        Assert.Contains("Chain Link Fence (FCK)", summary);
        Assert.Contains("Ambiguous (layer shared by several codes): 1", summary);    // ambiguity
        Assert.Contains("V-SURF-FENC-E -> FNC/FHW", summary);
        Assert.Contains("Not configured as line features: 1", summary);              // unknown
        Assert.Contains("DripArc 25", summary);                                      // FTF-owned
        Assert.Contains("not survey linework", summary);
    }

    [Fact]
    public void NoFtfOwnedCurvesSaysSo()
    {
        var summary = LineworkInventoryCsv.Summary(SampleRows(), new Dictionary<string, int>());
        Assert.Contains("FTF-owned finishing curves: none", summary);
    }

    [Fact]
    public void ReportSitsBesideTheDrawing()
    {
        Assert.Equal(@"C:\jobs\survey.ftf-linework.csv",
                     LineworkInventoryCsv.PathFor(@"C:\jobs\survey.dwg"));
        Assert.Null(LineworkInventoryCsv.PathFor(""));
    }

    [Fact]
    public void LayersWithCommasAreQuoted()
    {
        var row = _catalog.Identify("Polyline", null, "ODD,LAYER", 1.0);
        Assert.Contains("\"ODD,LAYER\"", LineworkInventoryCsv.Render(new[] { row }));
    }
}
