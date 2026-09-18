using FieldCodes.Geometry;
using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// Spot elevation placement: the text prefers its standard perch up the label
/// angle, and dense clusters -- a wheelchair ramp's dozen shots -- pack in
/// without overlapping, deterministically.
/// </summary>
public sealed class SpotPlacerTests
{
    private const double Angle = Math.PI / 4;   // the 45-degree office standard

    [Fact]
    public void TheFirstSpotSitsOnItsStandardPerch()
    {
        var occupied = new List<SpotBox>();
        double x, y;
        SpotPlacer.Place(100, 100, Angle, 2.0, 6.0, 1.5, occupied, out x, out y);

        Assert.Equal(100 + 2.0 * Math.Cos(Angle), x, 9);
        Assert.Equal(100 + 2.0 * Math.Sin(Angle), y, 9);
        Assert.Single(occupied);
    }

    [Fact]
    public void ASecondSpotOnTheSamePointDodgesTheFirst()
    {
        var occupied = new List<SpotBox>();
        double x1, y1, x2, y2;
        SpotPlacer.Place(100, 100, Angle, 2.0, 6.0, 1.5, occupied, out x1, out y1);
        SpotPlacer.Place(100, 100, Angle, 2.0, 6.0, 1.5, occupied, out x2, out y2);

        Assert.False(Math.Abs(x1 - x2) < 1e-9 && Math.Abs(y1 - y2) < 1e-9);
        Assert.Equal(2, occupied.Count);
        Assert.False(occupied[0].Intersects(occupied[1]));
    }

    [Fact]
    public void AWheelchairRampOfCloseSpotsStaysLegible()
    {
        // Twelve shots a foot apart with 6-ft-wide labels: every placed box must
        // clear every other, whatever side each one had to take.
        var occupied = new List<SpotBox>();
        for (var i = 0; i < 12; i++)
        {
            double x, y;
            SpotPlacer.Place(100 + i * 1.0, 100, Angle, 2.0, 6.0, 1.5, occupied,
                             out x, out y);
        }

        Assert.Equal(12, occupied.Count);
        for (var a = 0; a < occupied.Count; a++)
        for (var b = a + 1; b < occupied.Count; b++)
            Assert.False(occupied[a].Intersects(occupied[b]),
                a + " overlaps " + b);
    }

    [Fact]
    public void FarApartSpotsAllTakeTheStandardPerch()
    {
        var occupied = new List<SpotBox>();
        for (var i = 0; i < 5; i++)
        {
            double x, y;
            SpotPlacer.Place(i * 100.0, 0, Angle, 2.0, 6.0, 1.5, occupied, out x, out y);
            Assert.Equal(i * 100.0 + 2.0 * Math.Cos(Angle), x, 9);
        }
    }

    [Fact]
    public void PlacementIsDeterministic()
    {
        var first = Run();
        var second = Run();
        Assert.Equal(first, second);
    }

    private static List<double> Run()
    {
        var occupied = new List<SpotBox>();
        var results = new List<double>();
        for (var i = 0; i < 8; i++)
        {
            double x, y;
            SpotPlacer.Place(50 + i * 1.5, 50, Angle, 2.0, 6.0, 1.5, occupied,
                             out x, out y);
            results.Add(x);
            results.Add(y);
        }
        return results;
    }

    // ------------------------------------------------------------- the legend

    [Fact]
    public void TheLegendListsEachIdentifiedFeatureOnce_Alphabetically()
    {
        var rows = new[]
        {
            new LineworkRow { FeatureName = "Storm Line", Layer = "V-UTIL-STRM-E",
                              Source = LineIdentitySource.Layer },
            new LineworkRow { FeatureName = "Chain Link Fence", Layer = "V-SURF-FENC-CHNL-E",
                              Source = LineIdentitySource.Layer },
            new LineworkRow { FeatureName = "Storm Line", Layer = "V-UTIL-STRM-E",
                              Source = LineIdentitySource.Layer },
            new LineworkRow { FeatureName = "-", Layer = "V-TINN-BRKL-E",
                              Source = LineIdentitySource.None }
        };

        var legend = LegendBuilder.Rows(rows);

        Assert.Equal(2, legend.Count);
        Assert.Equal("Chain Link Fence", legend[0].FeatureName);
        Assert.Equal("Storm Line", legend[1].FeatureName);
    }
}
