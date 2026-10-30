using FieldCodes.Stamps;

namespace FieldCodes.Tests;

/// <summary>
/// Drawings stamped by the FTFCOMPARE/FTFTRIM build carry kinds 35-38, which FTFRECORD also
/// uses. On the integration branch Compare/Trim are 45-48; old stamps must still be read as
/// Compare/Trim, and FTFRECORD's own stamps must never be mistaken for them.
/// </summary>
public sealed class LegacyStampTests
{
    [Theory]
    [InlineData(35, "1A2F", "COMPARE", 45)]
    [InlineData(36, "1A2F", "COMPARE-CLOUD", 46)]
    [InlineData(37, "1A2F", "TRIM", 47)]
    [InlineData(38, "1A2F", "TRIM-CLOUD", 48)]
    public void OldCompareAndTrimStampsReadAsTheirNewKinds(short kind, string point, string tag, short expected)
    {
        Assert.Equal(expected, LegacyStamps.Remap(kind, point, tag));
    }

    [Theory]
    [InlineData(35, "REC-1A2B3C4D", "C12")]
    [InlineData(36, "REC-1A2B3C4D", "C3")]
    [InlineData(37, "REC-1A2B3C4D", "L4")]
    [InlineData(38, "REC-1A2B3C4D", "C12")]
    [InlineData(35, "REC-1A2B3C4D", "COMPARE")]   // a record project id always wins
    [InlineData(36, "1A2F", "COMPARE")]           // tag and number must agree
    [InlineData(39, "1A2F", "COMPARE-CLOUD")]
    [InlineData(45, "1A2F", "COMPARE")]
    [InlineData(22, "EAS-1", "COMPARE")]
    public void EverythingElseKeepsItsKind(short kind, string point, string tag)
    {
        Assert.Equal(kind, LegacyStamps.Remap(kind, point, tag));
    }

    [Fact]
    public void NullFieldsAreSafe()
    {
        Assert.Equal((short)35, LegacyStamps.Remap(35, null!, null!));
    }

    private static string Ownership([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "src", "FieldCodes.Cad", "Ownership.cs"));

    [Fact]
    public void TheEnumAndTheRemapAgree()
    {
        var source = Ownership();
        Assert.Contains("CompareSplit = 45,", source);
        Assert.Contains("CompareCloud = 46,", source);
        Assert.Contains("TrimSplit = 47,", source);
        Assert.Contains("TrimCloud = 48", source);
        Assert.Contains("RecordLine = 35,", source);
        Assert.Contains("LegacyStamps.Remap(", source);
    }

    [Fact]
    public void NoTwoKindsShareANumber()
    {
        var source = Ownership();
        var start = source.IndexOf("enum FtfEntityKind", StringComparison.Ordinal);
        var end = source.IndexOf("}", start, StringComparison.Ordinal);
        var numbers = System.Text.RegularExpressions.Regex.Matches(source.Substring(start, end - start), @"\b[A-Za-z]+ = (\d+)")
            .Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.True(numbers.Count > 40);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }
}
