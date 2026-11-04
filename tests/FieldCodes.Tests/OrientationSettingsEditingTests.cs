using FieldCodes.Editing;
using FieldCodes.Orient;

namespace FieldCodes.Tests;

/// <summary>
/// The Symbols and Rotations settings for how FTFORIENT weighs road lines and what a feature's
/// fallback rule may do: read from the office's rules file (with the built-in defaults when it
/// says nothing), written only when changed, checked, and reaching the command's standard.
/// </summary>
public sealed class OrientationSettingsEditingTests
{
    private static readonly string RulesPath = Path.Combine(AppContext.BaseDirectory, "rules.json");

    [Fact]
    public void TheShippedRulesGiveTheBuiltInDefaultsAndWriteNone()
    {
        var doc = RuleDocument.Load(RulesPath);
        var a = doc.GetOrientationApply();
        Assert.Equal(80, a.RoadSearchFeet, 6);
        Assert.Equal(25, a.MaxExtensionFeet, 6);
        Assert.Equal(20, a.LocalTangentFeet, 6);
        Assert.Equal(15, a.AlignmentToleranceDegrees, 6);
        Assert.Equal("Approach", a.IntersectionHandling);
        Assert.Equal("MeasuredReplaces", a.HandTurned);
        Assert.Equal("ReviewOnly", a.QcGraphics);
        Assert.False(a.QcShowInferredConnections);
        Assert.Equal("Review", a.Fallbacks["StreetLight"]);
        Assert.Equal("Review", a.Fallbacks["SignalPole"]);
        Assert.Equal("Apply", a.Fallbacks["Hydrant"]);
        Assert.Equal("Apply", a.Fallbacks["Sign"]);
        Assert.Equal(1.0, a.Reliability["Centerline"], 6);
        Assert.Equal(0.9, a.Reliability["LaneLine"], 6);
        Assert.Equal(0.85, a.Reliability["Edge"], 6);
        Assert.Equal(0.5, a.Reliability["RoadContext"], 6);

        doc.ApplyOrientationApply(a);                       // nothing changed: nothing written
        var text = doc.Render();
        foreach (var name in new[] { "maxExtensionFeet", "localTangentFeet", "alignmentToleranceDegrees", "fallbacks", "roadReferences", "qcGraphics", "handTurned" })
            Assert.DoesNotContain(name, text);
    }

    [Fact]
    public void ChangedSettingsAreWrittenAndReachFtfOrient()
    {
        var doc = RuleDocument.Load(RulesPath);
        var a = doc.GetOrientationApply();
        a.MaxExtensionFeet = 40;
        a.LocalTangentFeet = 30;
        a.AlignmentToleranceDegrees = 10;
        a.IntersectionHandling = "Review";
        a.HandTurned = "Keep";
        a.QcGraphics = "All";
        a.QcShowInferredConnections = true;
        a.Fallbacks["Hydrant"] = "Review";
        a.Fallbacks["StreetLight"] = "Apply";
        a.Reliability["Edge"] = 0.7;
        doc.ApplyOrientationApply(a);
        Assert.Empty(doc.Validate());

        var std = doc.CompileCandidate().Orientation;
        Assert.Equal(40, std.MaxExtensionFeet, 6);
        Assert.Equal(30, std.LocalTangentFeet, 6);
        Assert.Equal(10, std.AlignmentToleranceDegrees, 6);
        Assert.Equal(IntersectionHandling.Review, std.IntersectionHandling);
        Assert.Equal(HandTurnedRotation.Keep, std.HandTurned);
        Assert.Equal(QcGraphicsMode.All, std.QcGraphics);
        Assert.True(std.QcShowInferredConnections);
        Assert.Equal(OrientFallback.Review, std.FallbackFor(OrientKind.Hydrant));
        Assert.Equal(OrientFallback.Apply, std.FallbackFor(OrientKind.StreetLight));
        Assert.Equal(OrientFallback.Review, std.FallbackFor(OrientKind.SignalPole));     // written whole: the rest kept
        Assert.Equal(0.7, std.ReliabilityOf(LineRole.Edge), 6);
        Assert.Equal(1.0, std.ReliabilityOf(LineRole.Centerline), 6);                    // written whole
        Assert.Equal(1.0, std.ReliabilityOf(LineRole.OneWay), 6);                        // one-way lines follow centre lines

        var again = RuleDocument.FromText(doc.Render()).GetOrientationApply();
        Assert.Equal("Review", again.Fallbacks["Hydrant"]);
        Assert.Equal(0.7, again.Reliability["Edge"], 6);
    }

    [Fact]
    public void OutOfRangeSettingsAreRefusedByName()
    {
        var doc = RuleDocument.Load(RulesPath);
        var a = doc.GetOrientationApply();
        a.RoadSearchFeet = 0;
        a.MaxExtensionFeet = 500;
        a.AlignmentToleranceDegrees = 120;
        a.QcGraphics = "Sometimes";
        a.Fallbacks["Hydrant"] = "Guess";
        a.Reliability["Edge"] = 2;
        doc.ApplyOrientationApply(a);
        var problems = doc.Validate();
        Assert.Contains(problems, p => p.Contains("between 1 and 500 ft"));
        Assert.Contains(problems, p => p.Contains("between 0 and 200 ft"));
        Assert.Contains(problems, p => p.Contains("between 1 and 90 degrees"));
        Assert.Contains(problems, p => p.Contains("qcGraphics must be one of"));
        Assert.Contains(problems, p => p.Contains("fallback for Hydrant must be Apply, Review or Leave"));
        Assert.Contains(problems, p => p.Contains("the trust in Edge lines must be between 0 and 1"));
        Assert.All(problems, p => Assert.StartsWith("Symbols and Rotations: ", p));
    }
}
