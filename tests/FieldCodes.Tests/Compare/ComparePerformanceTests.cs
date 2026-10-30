using System.Diagnostics;
using FieldCodes.Compare;

namespace FieldCodes.Tests;

/// <summary>Guards the spatial indexing: a large synthetic site must not go quadratic.
/// This is a regression guard, not a production performance claim -- real drawings are
/// timed in Civil 3D.</summary>
public sealed class ComparePerformanceTests
{
    [Fact]
    public void LargeDrawingComparesInSecondsNotMinutes()
    {
        var s = CF.Survey();
        var c = CF.Consultant();
        var rows = 100;
        var cols = 100;
        // Consultant: a 100 x 100 grid of 40 ft curb lines (10,000) and 5,000 structures.
        for (var i = 0; i < rows; i++)
            for (var j = 0; j < cols; j++)
            {
                var x = j * 50.0;
                var y = i * 50.0;
                c.Add(CF.Line("C" + i + "_" + j, "E-CURB", x, y, x + 40, y));
                if ((i + j) % 2 == 0) c.Add(CF.Block("B" + i + "_" + j, "E-STRM-MH", "STMH", x + 20, y + 10));
            }
        // Survey: 8,000 curb pieces covering half of each consultant line, 2,000 more
        // on streets they do not have, and 5,000 manhole shots (half of them theirs).
        var n = 0;
        for (var i = 0; i < rows; i++)
            for (var j = 0; j < cols; j++)
            {
                var x = j * 50.0;
                var y = i * 50.0;
                if (n++ < 8000) s.Add(CF.Line("S" + i + "_" + j, "V-SURF-CURB-E", x, y, x + 20, y));
                else s.Add(CF.Line("S" + i + "_" + j, "V-SURF-CURB-E", x, y + 25, x + 40, y + 25));
                if ((i + j) % 2 == 0) s.Add(CF.Cogo("P" + i + "_" + j, "SDMH " + n, x + 20.3, y + 10.2, 0));
                else if ((i + j) % 5 == 0) s.Add(CF.Cogo("P" + i + "_" + j, "SDMH " + n, x + 20, y + 10, 0));
            }

        var watch = Stopwatch.StartNew();
        var result = CF.Run(s, c);
        watch.Stop();

        var summary = result.Summary();
        Assert.Equal(8000, result.Results.Count(r => r.Kind == FeatureKind.Line && r.Status == MatchStatus.Matched));
        Assert.Equal(2000, result.Results.Count(r => r.Kind == FeatureKind.Line && r.Status == MatchStatus.Missing));
        Assert.Equal(5000, result.Results.Count(r => r.Kind == FeatureKind.Point && r.Status == MatchStatus.Matched));
        Assert.True(result.Results.Count(r => r.Kind == FeatureKind.Point && r.Status == MatchStatus.Missing) > 0);
        Assert.True(watch.Elapsed.TotalSeconds < 30, "took " + watch.Elapsed.TotalSeconds + " s for " + (s.Features.Count + c.Features.Count) + " features");
    }
}
