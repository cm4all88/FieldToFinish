using FieldCodes.Review;

namespace FieldCodes.Tests;

/// <summary>
/// Clicking a column heading sorts by it. Point numbers, lengths and counts must
/// sort as numbers; codes and figure names with sequence digits naturally.
/// </summary>
public sealed class NaturalOrderTests
{
    private static string[] Sorted(params string[] values)
        => values.OrderBy(v => v, NaturalOrder.Instance).ToArray();

    [Fact]
    public void NumbersSortByValue()
    {
        Assert.Equal(new[] { "9", "10", "100", "1,204" }, Sorted("100", "1,204", "9", "10"));
        Assert.Equal(new[] { "-4.5", "0.25", "152.3" }, Sorted("152.3", "-4.5", "0.25"));
    }

    [Fact]
    public void ShortUnitsStillSortAsNumbers()
    {
        Assert.Equal(new[] { "6.2 ft", "10 ft", "152.3 ft" }, Sorted("152.3 ft", "6.2 ft", "10 ft"));
    }

    [Fact]
    public void TextWithDigitsSortsNaturally()
    {
        Assert.Equal(new[] { "PT 9", "PT 10", "RWC2", "RWC10", "rwc11" },
            Sorted("RWC10", "PT 10", "rwc11", "RWC2", "PT 9"));
    }

    [Fact]
    public void DescriptionsAreTextNotNumbers()
    {
        // "12 OAK" is a description: compared as text, digits by value.
        Assert.False(NaturalOrder.TryNumber("12 OAK", out _));
        Assert.True(NaturalOrder.TryNumber("152.3 ft", out var v));
        Assert.Equal(152.3, v, 6);
    }

    [Fact]
    public void PlaceholdersSortLast()
    {
        Assert.Equal(new[] { "A", "B", "-", "" }, Sorted("-", "B", "", "A"));
        Assert.True(NaturalOrder.IsPlaceholder(" - "));
        Assert.False(NaturalOrder.IsPlaceholder("0"));
    }

    [Fact]
    public void ComparisonIsConsistent()
    {
        var values = new[] { "10", "9", "A10", "a9", "-", "B", "1,000", "2.5 ft" };
        foreach (var a in values)
            foreach (var b in values)
                Assert.Equal(Math.Sign(NaturalOrder.Instance.Compare(a, b)),
                             -Math.Sign(NaturalOrder.Instance.Compare(b, a)));
    }
}
