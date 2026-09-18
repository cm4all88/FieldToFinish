using FieldCodes.Reporting;

namespace FieldCodes.Tests;

public sealed class ExceptionReportTests
{
    private static ExceptionRow Row(string desc, string diag = "ERROR [DRIP] missing") => new()
    {
        PointNumber = "101",
        Easting = 1234.5678,
        Northing = 8765.4321,
        Elevation = 42.0,
        RawDescription = desc,
        Severity = "Error",
        Diagnostics = diag
    };

    [Fact]
    public void HeaderIsWrittenEvenWithNoRows()
    {
        var csv = ExceptionReport.Render(Array.Empty<ExceptionRow>());
        Assert.StartsWith(ExceptionReport.Header, csv);
        Assert.Single(csv.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void CoordinatesUseInvariantCulture()
    {
        var csv = ExceptionReport.Render(new[] { Row("TRD 18") });
        Assert.Contains("1234.5678", csv);
        Assert.Contains("8765.4321", csv);
    }

    [Fact]
    public void DescriptionsContainingCommasAreQuoted()
    {
        var csv = ExceptionReport.Render(new[] { Row("TRD 12,14,10 24 CL3") });
        Assert.Contains("\"TRD 12,14,10 24 CL3\"", csv);
    }

    [Fact]
    public void EmbeddedQuotesAreDoubled()
    {
        var csv = ExceptionReport.Render(new[] { Row("TRD 18\" DECIDUOUS, DEAD") });
        Assert.Contains("\"TRD 18\"\" DECIDUOUS, DEAD\"", csv);
    }

    [Theory]
    [InlineData("=cmd", "'=cmd")]
    [InlineData("-DEAD", "'-DEAD")]
    [InlineData("+TREE", "'+TREE")]
    [InlineData("@ref", "'@ref")]
    public void LeadingFormulaCharactersAreNeutralisedForExcel(string input, string expected)
    {
        // A raw description starting with - or = would otherwise be evaluated as a
        // formula when someone opens the report in Excel.
        Assert.Equal(expected, ExceptionReport.Csv(input));
        Assert.Contains(expected, ExceptionReport.Render(new[] { Row(input) }));
    }

    [Theory]
    [InlineData("TRD 18 24")]
    [InlineData("SIGN STOP 135")]
    public void OrdinaryDescriptionsAreLeftAlone(string input)
        => Assert.Equal(input, ExceptionReport.Csv(input));

    [Fact]
    public void DiagnosticsWithPipesSurviveIntact()
    {
        var csv = ExceptionReport.Render(new[]
        {
            Row("TRD 18 24 ZZZ", "ERROR [MODIFIER] Unrecognised modifier token 'ZZZ'. | WARNING [FLAG] x")
        });

        Assert.Contains("Unrecognised modifier token 'ZZZ'.", csv);
        Assert.Contains("WARNING [FLAG] x", csv);
    }

    [Fact]
    public void OneLinePerRowPlusHeader()
    {
        var rows = new[] { Row("TRD 18"), Row("TRD 20"), Row("TRD 22") };
        var lines = ExceptionReport.Render(rows).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
    }

    [Theory]
    [InlineData(@"C:\jobs\1234\survey.dwg", @"C:\jobs\1234\survey.ftf-exceptions.csv")]
    [InlineData(@"C:\a b\c.dwg", @"C:\a b\c.ftf-exceptions.csv")]
    public void ReportSitsBesideTheDrawing(string dwg, string expected)
        => Assert.Equal(expected, ExceptionReport.PathFor(dwg));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsavedDrawing_HasNoReportPath(string dwg)
        => Assert.Null(ExceptionReport.PathFor(dwg));
}
