using FieldCodes.Reporting;

namespace FieldCodes.Tests;

public sealed class UnhandledSummaryTests
{
    private static UnhandledSummary Sample()
    {
        var s = new UnhandledSummary();
        s.Add("PP", "10001", "PP 1234");
        s.Add("PP", "10002", "PP 1235");
        s.Add("PP", "10003", "PP 1236");
        s.Add("SN", "10010", "SN");
        s.Add("EC", "10020", "EC");
        s.Add("EC", "10021", "EC");
        s.Add("EC", "10022", "EC");
        s.Add("EC", "10023", "EC");
        return s;
    }

    [Fact]
    public void CountsPointsAndDistinctCodes()
    {
        var s = Sample();
        Assert.Equal(8, s.TotalPoints);
        Assert.Equal(3, s.DistinctCodes);
    }

    [Fact]
    public void MostFrequentCodeLeads()
    {
        var ordered = Sample().ByFrequency();
        Assert.Equal("EC", ordered[0].Code);
        Assert.Equal(4, ordered[0].Count);
        Assert.Equal("PP", ordered[1].Code);
    }

    [Fact]
    public void KeepsRealDescriptions_WhichIsWhatARuleIsWrittenAgainst()
    {
        var pp = Sample().ByFrequency().Single(c => c.Code == "PP");
        Assert.Equal(new[] { "PP 1234", "PP 1235", "PP 1236" }, pp.Samples);
        Assert.Equal("10001", pp.FirstPointNumber);
    }

    [Fact]
    public void DuplicateDescriptionsAreNotRepeated()
    {
        // Four EC shots all read "EC"; one sample is all the information there is.
        var ec = Sample().ByFrequency().Single(c => c.Code == "EC");
        Assert.Single(ec.Samples);
    }

    [Fact]
    public void SamplesAreCapped()
    {
        var s = new UnhandledSummary();
        for (var i = 0; i < 50; i++) s.Add("PP", i.ToString(), "PP " + i);

        Assert.Equal(UnhandledSummary.MaxSamples, s.ByFrequency()[0].Samples.Count);
        Assert.Equal(50, s.ByFrequency()[0].Count);
    }

    [Fact]
    public void OneLineLeadsWithTheBiggest()
    {
        Assert.StartsWith("EC(4) PP(3)", Sample().OneLine(8));
    }

    [Fact]
    public void OneLineSaysHowManyItLeftOut()
    {
        Assert.Contains("and 1 more", Sample().OneLine(2));
    }

    [Fact]
    public void CsvHasAHeaderAndOneRowPerCode()
    {
        var lines = Sample().Render().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Code,Points,FirstPoint,Sample", lines[0]);
        Assert.Equal(4, lines.Length);           // header + 3 codes
    }

    [Fact]
    public void BlankCodeDoesNotCrashTheSummary()
    {
        var s = new UnhandledSummary();
        s.Add(null, "1", "");
        Assert.Equal("(blank)", s.ByFrequency()[0].Code);
    }

    [Fact]
    public void ReportSitsBesideTheDrawing()
    {
        Assert.Equal(@"C:\jobs\survey.ftf-unhandled.csv",
                     UnhandledSummary.PathFor(@"C:\jobs\survey.dwg"));
    }

    [Fact]
    public void UnsavedDrawingHasNoPath()
        => Assert.Null(UnhandledSummary.PathFor(""));
}
