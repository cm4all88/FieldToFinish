using FieldCodes.Easements;
using FieldCodes.RecordSurvey;

namespace FieldCodes.Tests;

/// <summary>
/// Curve mathematics: any two elements define the curve, the rest are calculated, every stated
/// element is checked, and placement never guesses a side.
/// </summary>
public sealed class RecordSurveyCurveTests
{
    private const double Delta = 22 + 55 / 60.0 + 6 / 3600.0;
    private const double Radius = 250.0;
    private static readonly double Arc = Radius * Delta * Math.PI / 180.0;
    private static readonly double Chord = 2 * Radius * Math.Sin(Delta * Math.PI / 360.0);
    private static readonly double Tangent = Radius * Math.Tan(Delta * Math.PI / 360.0);

    public static IEnumerable<object[]> Pairs()
    {
        yield return new object[] { "R+D", new CurveSpec { Radius = Radius, DeltaDegrees = Delta } };
        yield return new object[] { "R+L", new CurveSpec { Radius = Radius, ArcLength = Arc } };
        yield return new object[] { "R+CH", new CurveSpec { Radius = Radius, ChordLength = Chord } };
        yield return new object[] { "R+T", new CurveSpec { Radius = Radius, TangentLength = Tangent } };
        yield return new object[] { "D+L", new CurveSpec { DeltaDegrees = Delta, ArcLength = Arc } };
        yield return new object[] { "D+CH", new CurveSpec { DeltaDegrees = Delta, ChordLength = Chord } };
        yield return new object[] { "D+T", new CurveSpec { DeltaDegrees = Delta, TangentLength = Tangent } };
        yield return new object[] { "L+CH", new CurveSpec { ArcLength = Arc, ChordLength = Chord } };
        yield return new object[] { "L+T", new CurveSpec { ArcLength = Arc, TangentLength = Tangent } };
        yield return new object[] { "CH+T", new CurveSpec { ChordLength = Chord, TangentLength = Tangent } };
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void AnyTwoElementsReconcileToTheSameCurve(string name, CurveSpec spec)
    {
        var s = CurveSolver.Solve(spec, 0.01, 5.0);
        Assert.True(s.Ok, name + ": " + s.Error);
        Assert.Equal(Radius, s.Radius, 4);
        Assert.Equal(Delta, s.DeltaDegrees, 6);
        Assert.Equal(Arc, s.ArcLength, 4);
        Assert.Equal(Chord, s.ChordLength, 4);
        Assert.Equal(Tangent, s.TangentLength, 4);
        Assert.Empty(s.Disagreements);
        Assert.Equal(3, s.Calculated.Count(c => c.Length <= 2));      // the three elements not stated
    }

    [Fact]
    public void OverSpecifiedCurvesAreCheckedNotAveraged()
    {
        var spec = new CurveSpec { Radius = Radius, DeltaDegrees = Delta, ArcLength = Arc + 0.05 };
        var s = CurveSolver.Solve(spec, 0.02, 5.0);
        Assert.True(s.Ok);
        Assert.Equal("R and Δ", s.SolvedFrom);
        Assert.Equal(Arc, s.ArcLength, 6);                     // the solution is R and Δ's, untouched by the stated arc
        Assert.Single(s.Disagreements);
        Assert.Contains("arc length", s.Disagreements[0]);
        Assert.Equal(0.05, s.MaxDistanceDisagreement, 6);
    }

    [Fact]
    public void OneElementIsRefused()
    {
        var s = CurveSolver.Solve(new CurveSpec { Radius = 250 }, 0.01, 5.0);
        Assert.False(s.Ok);
        Assert.Contains("two of", s.Error);
    }

    [Fact]
    public void AChordLongerThanTheDiameterIsRefused()
    {
        var s = CurveSolver.Solve(new CurveSpec { Radius = 50, ChordLength = 120 }, 0.01, 5.0);
        Assert.False(s.Ok);
        Assert.Contains("diameter", s.Error);
    }

    // ---------------------------------------------------------------- placement

    [Fact]
    public void ATangentCurveTurningRightIsPlacedFromThePreviousCourse()
    {
        var spec = new CurveSpec { Radius = Radius, DeltaDegrees = Delta, Turn = "RIGHT" };
        var s = CurveSolver.Solve(spec, 0.01, 5.0);
        var p = CurveSolver.Place(s, spec, new P2(0, 0), 90.0, false);     // coming in due east
        Assert.True(p.Ok, p.Error);
        Assert.Equal(90.0 + Delta / 2, p.ChordAzimuthDegrees, 6);
        Assert.Equal(90.0 + Delta, p.TangentOutAzimuthDegrees, 6);
        Assert.False(p.TurnsLeft);
        Assert.Equal(Radius, p.Course.Radius, 6);
        Assert.Equal(Chord, p.Course.Start.DistanceTo(p.Course.End), 6);
        Assert.Equal(Arc, p.Course.Length, 6);
        // Centre lies to the right of due east travel: south of the start.
        Assert.Equal(-Radius, p.Course.Center.Y, 6);
        Assert.Equal(0.0, p.Course.Center.X, 6);
    }

    [Fact]
    public void AChordBearingPlacesTheCurveAndSettlesTheTurnFromThePreviousCourse()
    {
        var spec = new CurveSpec { Radius = Radius, DeltaDegrees = Delta, ChordAzimuthDegrees = 90.0 - Delta / 2 };
        var s = CurveSolver.Solve(spec, 0.01, 5.0);
        var p = CurveSolver.Place(s, spec, new P2(0, 0), 90.0, false);
        Assert.True(p.Ok, p.Error);
        Assert.True(p.TurnsLeft);
        Assert.Equal("chord bearing", p.Method);
        Assert.Contains(p.Notes, n => n.Contains("LEFT"));
    }

    [Fact]
    public void ARadialBearingPlacesTheCurveWithAStatedTurn()
    {
        // Centre due south of the start, left turn: travel starts due west.
        var spec = new CurveSpec { Radius = Radius, DeltaDegrees = Delta, RadialInAzimuthDegrees = 180.0, Turn = "LEFT" };
        var s = CurveSolver.Solve(spec, 0.01, 5.0);
        var p = CurveSolver.Place(s, spec, new P2(0, 0), null, false);
        Assert.True(p.Ok, p.Error);
        Assert.Equal(270.0, p.TangentInAzimuthDegrees, 6);
        Assert.Equal(-Radius, p.Course.Center.Y, 6);
    }

    [Fact]
    public void ACurveWithNoWayToOrientItIsRefusedNotGuessed()
    {
        var spec = new CurveSpec { Radius = Radius, DeltaDegrees = Delta };
        var s = CurveSolver.Solve(spec, 0.01, 5.0);
        var p = CurveSolver.Place(s, spec, new P2(0, 0), null, false);
        Assert.False(p.Ok);
        Assert.Contains("cannot be placed", p.Error);

        var noTurn = CurveSolver.Place(s, spec, new P2(0, 0), 90.0, false);
        Assert.False(noTurn.Ok);
        Assert.Contains("turn direction", noTurn.Error);
    }

    [Fact]
    public void ANonTangentChordIsNotedAgainstThePreviousCourse()
    {
        var spec = new CurveSpec { Radius = Radius, DeltaDegrees = Delta, ChordAzimuthDegrees = 100.0, Turn = "RIGHT" };
        var s = CurveSolver.Solve(spec, 0.01, 5.0);
        var p = CurveSolver.Place(s, spec, new P2(0, 0), 90.0, false);     // a tangent chord would be at 101.46
        Assert.True(p.Ok);
        Assert.Contains(p.Notes, n => n.Contains("Non-tangent"));
    }

    [Fact]
    public void AReversedCurveTraversesTheOtherWay()
    {
        var spec = new CurveSpec { Radius = Radius, DeltaDegrees = Delta, ChordAzimuthDegrees = 90.0, Turn = "RIGHT" };
        var s = CurveSolver.Solve(spec, 0.01, 5.0);
        var forward = CurveSolver.Place(s, spec, new P2(0, 0), null, false);
        var back = CurveSolver.Place(s, spec, forward.Course.End, null, true);
        Assert.True(back.Ok, back.Error);
        Assert.Equal(0.0, back.Course.End.X, 6);
        Assert.Equal(0.0, back.Course.End.Y, 6);
        Assert.Equal(forward.Course.Center.X, back.Course.Center.X, 6);
        Assert.Equal(forward.Course.Center.Y, back.Course.Center.Y, 6);
    }

    // ---------------------------------------------------------------- table columns

    [Fact]
    public void CurveTableColumnsAreIdentifiedByTheMathematics()
    {
        // The row gives three distances in an unknown order; only one assignment satisfies L = RΔ and CH = 2R sin(Δ/2).
        var spec = CurveSolver.IdentifyColumns(new[] { Chord, Radius, Arc }, Delta, 0.02);
        Assert.NotNull(spec);
        Assert.Equal(Radius, spec!.Radius!.Value, 6);
        Assert.Equal(Arc, spec.ArcLength!.Value, 6);
        Assert.Equal(Chord, spec.ChordLength!.Value, 6);
    }

    [Fact]
    public void AmbiguousOrInconsistentColumnsAreNotAssigned()
    {
        Assert.Null(CurveSolver.IdentifyColumns(new[] { 100.0, 200.0, 300.0 }, Delta, 0.02));
    }

    [Theory]
    [InlineData("LEFT", true)]
    [InlineData("rt", false)]
    [InlineData("", null)]
    [InlineData("MAYBE", null)]
    public void TurnWordsParse(string text, bool? expected)
    {
        Assert.Equal(expected, CurveSolver.ParseTurn(text));
    }
}
