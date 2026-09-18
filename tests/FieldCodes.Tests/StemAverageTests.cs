using FieldCodes;

namespace FieldCodes.Tests;

/// <summary>
/// The multi-stem formula is the number that goes to the reviewing agency, and the
/// README flags it as needing confirmation against the local ordinance. These pin
/// down what the code actually computes for the README's own worked example.
/// </summary>
public sealed class StemAverageTests
{
    private static readonly double[] Readme = { 12.0, 14.0, 10.0 };

    [Fact]
    public void Arithmetic_IsThePlainMean()
        => Assert.Equal(12.0, FieldCodeParser.Average(Readme, StemAverageMethod.Arithmetic), 9);

    [Fact]
    public void Quadratic_IsRootSumOfSquares()
    {
        // sqrt(12^2 + 14^2 + 10^2) = sqrt(440)
        Assert.Equal(Math.Sqrt(440.0), FieldCodeParser.Average(Readme, StemAverageMethod.Quadratic), 9);
    }

    [Fact]
    public void LargestPlusHalf_IsLargestPlusHalfOfEveryOtherStem()
    {
        // 14 + 0.5*(12 + 10) = 25
        Assert.Equal(25.0, FieldCodeParser.Average(Readme, StemAverageMethod.LargestPlusHalf), 9);
    }

    [Fact]
    public void Sum_AddsThemAll()
        => Assert.Equal(36.0, FieldCodeParser.Average(Readme, StemAverageMethod.Sum), 9);

    [Theory]
    [InlineData(StemAverageMethod.Arithmetic)]
    [InlineData(StemAverageMethod.Quadratic)]
    [InlineData(StemAverageMethod.LargestPlusHalf)]
    [InlineData(StemAverageMethod.Sum)]
    public void SingleStem_IsAlwaysItself(StemAverageMethod method)
        => Assert.Equal(18.0, FieldCodeParser.Average(new[] { 18.0 }, method), 9);
}
