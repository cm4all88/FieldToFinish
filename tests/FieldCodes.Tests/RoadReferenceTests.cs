using FieldCodes.Easements;
using FieldCodes.Orient;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// Orientation when the road is only partly surveyed: no centre line, edges that stop short or
/// have gaps, curves, and other linework (fences, driveways) nearer than the road. The road's
/// direction comes from road lines only, ranked by how far each kind is trusted, its local
/// tangent where the feature stands, and how far it had to be followed past its end; road
/// lines that disagree send the feature to review. X is east, Y is north; azimuths clockwise
/// from north. A sign on the south side of an east-west road faces 270 (the eastbound traffic
/// beside it keeps right); on the north side, 90.
/// </summary>
public sealed class RoadReferenceTests
{
    private sealed class Scene
    {
        public readonly OrientationScene S = new();
        private int _n;

        public Scene Line(LineRole role, params double[] xy)
        {
            var l = new OrientLine { Id = role + "-" + (++_n), Role = role, Layer = role.ToString() };
            for (var i = 0; i < xy.Length; i += 2) l.Points.Add(new P2(xy[i], xy[i + 1]));
            S.Lines.Add(l);
            return this;
        }

        public Scene Arc(LineRole role, double cx, double cy, double radius, double fromDeg, double toDeg, double stepDeg)
        {
            var l = new OrientLine { Id = role + "-" + (++_n), Role = role, Layer = role.ToString() };
            for (var a = fromDeg; a <= toDeg + 1e-9; a += stepDeg)
                l.Points.Add(new P2(cx + radius * Math.Cos(a * Math.PI / 180), cy + radius * Math.Sin(a * Math.PI / 180)));
            S.Lines.Add(l);
            return this;
        }

        public Scene Point(string id, string description, double x, double y, string? note = null)
        {
            var code = OrientationStandard.BaseCode(description);
            var rule = OrientationStandard.Pmx().SymbolFor(code);
            S.Features.Add(new OrientFeature
            {
                Id = id, Code = code, Description = description, Note = note, Location = new P2(x, y), Kind = rule?.Kind ?? OrientKind.Other
            });
            return this;
        }

        public OrientationResult Run(OrientationStandard? std = null) => new OrientationEngine(std ?? OrientationStandard.Pmx()).Run(S);
    }

    private static OrientationDecision Faces(OrientationResult r, string id, double azimuth, double within = 1.0)
    {
        var d = r.For(id)!;
        Assert.True(d.Azimuth.HasValue, id + ": " + d.Summary());
        Assert.True(SymbolRotation.Difference(d.Azimuth!.Value, azimuth) < within, id + ": expected " + azimuth + ", got " + d.Summary());
        return d;
    }

    private static void ForReview(OrientationResult r, string id, string because)
    {
        var d = r.For(id)!;
        Assert.True(d.Tier == OrientationTier.Review, id + ": " + d.Summary());
        Assert.Null(d.MarkerRotation);
        Assert.Contains(because, d.ReviewReason);
    }

    private static bool Applied(OrientationDecision d)
    {
        var std = OrientationStandard.Pmx();
        return d.Applies(std.ApplyMinimumConfidence, std.ApplyWeakestTier);
    }

    // ----------------------------------------------------------------- no centre line

    [Fact]
    public void WithoutACentreLineTheRoadEdgeGivesTheDirectionButIsNotSureEnoughToTurn()
    {
        var r = new Scene().Line(LineRole.Edge, -300, -12, 300, -12).Line(LineRole.Edge, -300, 12, 300, 12)
            .Point("S", "SN", 0, -20).Run();
        var d = Faces(r, "S", 270);
        Assert.Equal(OrientationTier.Alignment, d.Tier);
        Assert.Equal(0.55, d.Confidence, 2);                 // keep right with no stripe: could be one-way
        Assert.False(Applied(d));
        Assert.Contains("road edge", d.Reference);
        Assert.Equal(8, d.ReferenceDistance!.Value, 1);
        Assert.Contains("runs the same way", d.Evidence.Last().Text);   // the far edge agrees
    }

    [Fact]
    public void AFogLineIsTrustedMoreThanAPavementEdge()
    {
        var r = new Scene().Line(LineRole.LaneLine, -300, -10, 300, -10).Point("S", "SN", 0, -20).Run();
        var d = Faces(r, "S", 270);
        Assert.Equal(0.6, d.Confidence, 2);
        Assert.True(Applied(d));
        Assert.Contains("lane line", d.Reference);
    }

    [Fact]
    public void AnEdgeOfConcreteAloneIsContextNotEnoughToTurnAnything()
    {
        var r = new Scene().Line(LineRole.RoadContext, -300, -12, 300, -12).Point("S", "SN", 0, -20).Run();
        var d = Faces(r, "S", 270);
        Assert.Equal(OrientationTier.Inferred, d.Tier);
        Assert.False(Applied(d));
        Assert.Contains("concrete edge", d.Reference);
    }

    // ----------------------------------------------------------------- lines that stop short

    [Fact]
    public void AnEdgeThatStopsShortIsFollowedALimitedWayWithLessConfidence()
    {
        var r = new Scene().Line(LineRole.Edge, -300, -12, 0, -12).Point("S", "SN", 10, -20).Run();
        var d = Faces(r, "S", 270);
        Assert.Contains("followed 10.0 ft past its end", d.Reference);
        Assert.Equal(Math.Round(0.55 * (1 - 0.4 * 10 / 25.0), 2), d.Confidence, 2);
        Assert.Equal(8, d.ReferenceDistance!.Value, 1);       // to the line's extension, not to its end point
    }

    [Fact]
    public void ACentreLineThatStopsShortIsFollowedButNotFarEnoughToTurnOnItsOwn()
    {
        var r = new Scene().Line(LineRole.Centerline, -300, 0, 0, 0).Point("S", "SN", 15, -20).Run();
        var d = Faces(r, "S", 270);
        Assert.Equal(Math.Round(0.7 * (1 - 0.4 * 15 / 25.0), 2), d.Confidence, 2);
        Assert.False(Applied(d));
    }

    [Fact]
    public void ARoadLineIsNeverExtrapolatedPastTheLimit()
    {
        var r = new Scene().Line(LineRole.Edge, -300, -12, 0, -12).Point("S", "SN", 40, -20).Run();
        ForReview(r, "S", "no road");
        // The office may allow further.
        var std = OrientationStandard.Pmx();
        std.MaxExtensionFeet = 50;
        Faces(new Scene().Line(LineRole.Edge, -300, -12, 0, -12).Point("S", "SN", 40, -20).Run(std), "S", 270);
    }

    [Fact]
    public void AFeatureInAGapOfAnEdgeTakesTheEdgeAsIfItRanThrough()
    {
        // The edge is broken by a driveway; the sign stands in the gap.
        var r = new Scene().Line(LineRole.Edge, -300, -12, -15, -12).Line(LineRole.Edge, 15, -12, 300, -12)
            .Point("S", "SN", 0, -20).Run();
        var d = Faces(r, "S", 270);
        Assert.Contains("gap", d.Reference);
        Assert.Equal(0.55, d.Confidence, 2);                 // no extension penalty: the edge resumes
    }

    // ----------------------------------------------------------------- curves and jogs

    [Fact]
    public void OnACurveTheLocalTangentIsUsedNotTheOverallBearing()
    {
        // A quarter circle of radius 200 about the origin, counter-clockwise from east to north;
        // the sign stands outside it at 20 degrees.
        var scene = new Scene().Arc(LineRole.Centerline, 0, 0, 200, 0, 90, 2);
        var at = 20 * Math.PI / 180;
        scene.Point("S", "SN", 220 * Math.Cos(at), 220 * Math.Sin(at));
        var r = scene.Run();
        // Counter-clockwise traffic there heads 340; the sign on its right faces it: 160.
        var d = Faces(r, "S", 160, 3.0);
        // The chord of the whole curve heads 315: facing 135 would be 25 degrees off.
        Assert.True(SymbolRotation.Difference(d.Azimuth!.Value, 135) > 20);
    }

    [Fact]
    public void AShortJogInAShotEdgeDoesNotSwingTheDirection()
    {
        var r = new Scene().Line(LineRole.Edge, -300, -12, -1, -12, 0, -13, 1, -12, 300, -12)
            .Point("S", "SN", 0, -20).Run();
        Faces(r, "S", 270, 3.0);
    }

    // ----------------------------------------------------------------- the wrong reference

    [Fact]
    public void AFenceNearerThanTheRoadIsNeverTheRoad()
    {
        // A fence 3 ft from the sign, running diagonally: not a road line, never read as one.
        var r = new Scene().Line(LineRole.Edge, -300, -12, 300, -12)
            .Line(LineRole.Other, -50, -73, 50, 27)
            .Point("S", "SN", 0, -20).Run();
        var d = Faces(r, "S", 270);
        Assert.Contains("road edge", d.Reference);
    }

    [Fact]
    public void AShortDrivewayEdgeBesideASignLosesToTheLongRoadEdge()
    {
        var r = new Scene().Line(LineRole.Edge, -300, -12, 300, -12)
            .Line(LineRole.Edge, 5, -12, 5, -32)                  // a driveway edge, 20 ft long, 3 ft from the sign
            .Point("S", "SN", 2, -20).Run();
        var d = Faces(r, "S", 270);
        Assert.Contains("road edge Edge-1", d.Reference);
    }

    [Fact]
    public void RoadLinesThatDisagreeSendTheFeatureToReviewInsteadOfPickingOne()
    {
        // Two long edges meeting at a corner, no centre lines: the sign is about as near to each.
        var r = new Scene().Line(LineRole.Edge, -300, -12, 300, -12).Line(LineRole.Edge, 12, -300, 12, 300)
            .Point("S", "SN", 5, -20).Run();
        ForReview(r, "S", "road lines here disagree");
        Assert.NotNull(r.For("S")!.Azimuth);                 // with a suggestion
        Assert.NotNull(r.For("S")!.AlternativeAzimuth);      // and the other line's
    }

    [Fact]
    public void TheAlignmentToleranceIsTheOfficesSetting()
    {
        // Two edges 10 degrees apart: within 15 they agree; at 5 they do not.
        var scene = new Scene().Line(LineRole.Edge, -300, -12, 300, -12)
            .Line(LineRole.Edge, -300, -28 - 300 * Math.Tan(10 * Math.PI / 180), 300, -28 + 300 * Math.Tan(10 * Math.PI / 180))
            .Point("S", "SN", 0, -20);
        Assert.NotEqual(OrientationTier.Review, scene.Run().For("S")!.Tier);
        var strict = OrientationStandard.Pmx();
        strict.AlignmentToleranceDegrees = 5;
        ForReview(scene.Run(strict), "S", "disagree");
    }

    [Fact]
    public void TheSearchRadiusIsTheOfficesSetting()
    {
        var std = OrientationStandard.Pmx();
        std.RoadSearchFeet = 5;
        ForReview(new Scene().Line(LineRole.Edge, -300, -12, 300, -12).Point("S", "SN", 0, -20).Run(std), "S", "no road");
    }

    // ----------------------------------------------------------------- markings with partial roads

    [Fact]
    public void AWrittenArrowDirectionFollowsEdgesThatStopShortOfIt()
    {
        var r = new Scene().Line(LineRole.Edge, -300, -12, 0, -12).Line(LineRole.Edge, -300, 12, 0, 12)
            .Point("A", "ASTO E", 10, -6).Run();
        var d = Faces(r, "A", 90);
        Assert.Equal(OrientationTier.Measured, d.Tier);
        Assert.True(Applied(d));
    }

    [Fact]
    public void AWrittenArrowDirectionOnACurveFollowsTheCurveWhereTheArrowIs()
    {
        var scene = new Scene().Arc(LineRole.Edge, 0, 0, 200, 0, 90, 2).Arc(LineRole.Edge, 0, 0, 224, 0, 90, 2);
        var at = 20 * Math.PI / 180;
        scene.Point("A", "ASTO N", 206 * Math.Cos(at), 206 * Math.Sin(at));
        var d = Faces(scene.Run(), "A", 340, 3.0);           // north-ish, along the curve there
        Assert.Equal(OrientationTier.Measured, d.Tier);
    }

    // ----------------------------------------------------------------- repeated runs

    [Fact]
    public void RunningAgainOnTheTurnedSymbolsGivesTheSameRotation()
    {
        var scene = new Scene().Line(LineRole.Centerline, -300, 0, 300, 0).Point("S", "SN", 0, -20).Point("A", "ASTO E", 50, -6);
        var first = scene.Run();
        foreach (var f in scene.S.Features) f.CurrentRotation = first.For(f.Id)!.MarkerRotation ?? 0;
        var second = scene.Run();
        foreach (var f in scene.S.Features)
            Assert.Equal(first.For(f.Id)!.MarkerRotation!.Value, second.For(f.Id)!.MarkerRotation!.Value, 9);
    }

    // ----------------------------------------------------------------- the standard

    [Fact]
    public void TheNewSettingsReadFromTheRulesFile()
    {
        var std = JsonConvert.DeserializeObject<OrientationStandard>(@"{
            ""maxExtensionFeet"": 40, ""localTangentFeet"": 30, ""alignmentToleranceDegrees"": 10,
            ""intersectionHandling"": ""Review"", ""handTurned"": ""Keep"", ""qcGraphics"": ""All"", ""qcShowInferredConnections"": true,
            ""fallbacks"": { ""StreetLight"": ""Apply"", ""Hydrant"": ""Review"" },
            ""roadReferences"": [ { ""role"": ""Edge"", ""reliability"": 0.7 } ] }")!;
        Assert.Equal(40, std.MaxExtensionFeet);
        Assert.Equal(30, std.LocalTangentFeet);
        Assert.Equal(10, std.AlignmentToleranceDegrees);
        Assert.Equal(IntersectionHandling.Review, std.IntersectionHandling);
        Assert.Equal(HandTurnedRotation.Keep, std.HandTurned);
        Assert.Equal(QcGraphicsMode.All, std.QcGraphics);
        Assert.True(std.QcShowInferredConnections);
        Assert.Equal(OrientFallback.Apply, std.FallbackFor(OrientKind.StreetLight));
        Assert.Equal(OrientFallback.Review, std.FallbackFor(OrientKind.Hydrant));
        Assert.Equal(OrientFallback.Apply, std.FallbackFor(OrientKind.Sign));        // not listed: Apply
        Assert.Equal(0.7, std.ReliabilityOf(LineRole.Edge));
        Assert.Equal(0, std.ReliabilityOf(LineRole.Centerline));                     // the office's list replaces the built-in one
        Assert.Equal(0, std.ReliabilityOf(LineRole.WaterMain));                      // never a road line
    }

    [Fact]
    public void TheBuiltInStandardTrustsCentreLinesMostAndConcreteEdgesLeast()
    {
        var std = OrientationStandard.Pmx();
        Assert.True(std.ReliabilityOf(LineRole.Centerline) > std.ReliabilityOf(LineRole.LaneLine));
        Assert.True(std.ReliabilityOf(LineRole.LaneLine) > std.ReliabilityOf(LineRole.Edge));
        Assert.True(std.ReliabilityOf(LineRole.Edge) > std.ReliabilityOf(LineRole.RoadContext));
        Assert.Equal(0, std.ReliabilityOf(LineRole.Other));                          // fences, property lines
        Assert.Equal(LineRole.RoadContext, std.RoleFor("V-SURF-CONC-E", null));
        Assert.Equal(OrientFallback.Review, std.FallbackFor(OrientKind.StreetLight));
        Assert.Equal(OrientFallback.Review, std.FallbackFor(OrientKind.SignalPole));
        Assert.Equal(OrientFallback.Apply, std.FallbackFor(OrientKind.Hydrant));
    }

    [Fact]
    public void AHydrantTurnedByTheStreetSaysItWasTheFallbackAndAnOfficeMayHaveItReviewed()
    {
        var scene = new Scene().Line(LineRole.Edge, -300, -12, 300, -12).Point("H", "WFH", 0, -25);
        var d = Faces(scene.Run(), "H", 0);
        Assert.True(d.ByFallback);
        Assert.Equal("fallback rule", d.Source);
        Assert.Contains("fallback rule", d.Evidence.Last().Text);
        var std = OrientationStandard.Pmx();
        std.Fallbacks[OrientKind.Hydrant] = OrientFallback.Review;
        ForReview(scene.Run(std), "H", "no nozzle direction recorded");
        std.Fallbacks[OrientKind.Hydrant] = OrientFallback.Leave;
        Assert.Equal(OrientationTier.NoSupport, scene.Run(std).For("H")!.Tier);
    }
}
