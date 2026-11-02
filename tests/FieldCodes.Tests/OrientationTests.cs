using FieldCodes.Easements;
using FieldCodes.Orient;

namespace FieldCodes.Tests;

/// <summary>
/// Symbol orientation from surveyed geometry. Every scene is built from what a crew shoots:
/// centre lines, edges, stop bars, one-way lines, valve and hydrant points. Assertions are on
/// the marker rotation written to the drawing (radians, for the PMX block as drawn), not only
/// on the direction: a right direction with the wrong block zero is still a wrong drawing.
/// X is east, Y is north; azimuths are clockwise from north.
/// </summary>
public sealed class OrientationTests
{
    // ----------------------------------------------------------------- scene building

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

        public Scene Point(string id, string description, double x, double y, string? note = null)
        {
            var std = OrientationStandard.Pmx();
            var code = OrientationStandard.BaseCode(description);
            var rule = std.SymbolFor(code);
            S.Features.Add(new OrientFeature
            {
                Id = id, Code = code, Description = description, Note = note, Location = new P2(x, y),
                Kind = rule?.Kind ?? OrientKind.Other
            });
            return this;
        }

        public OrientationResult Run(OrientationStandard? std = null) => new OrientationEngine(std ?? OrientationStandard.Pmx()).Run(S);
    }

    private static double Rad(double deg) => deg * Math.PI / 180.0;

    private static void Faces(OrientationResult r, string id, double azimuth, OrientationTier? tier = null)
    {
        var d = r.For(id);
        Assert.NotNull(d);
        Assert.True(d!.Azimuth.HasValue, id + ": " + d.Summary());
        var period = d.Symbol?.Period == 180 ? 180 : 360;      // a valve axis: 270 is 90
        Assert.True(SymbolRotation.Difference(d.Azimuth!.Value, azimuth, period) < 1.0, id + ": expected " + azimuth + ", got " + d.Summary());
        if (tier.HasValue) Assert.True(d.Tier == tier.Value, id + ": expected " + tier + ", got " + d.Summary());
    }

    private static void Rotation(OrientationResult r, string id, double degrees)
    {
        var d = r.For(id)!;
        Assert.True(d.MarkerRotation.HasValue, id + ": no rotation; " + d.Summary());
        var got = d.MarkerRotation!.Value * 180 / Math.PI;
        Assert.True(SymbolRotation.Difference(got, degrees) < 0.5, id + ": rotation " + got + ", expected " + degrees);
    }

    private static void ForReview(OrientationResult r, string id, string? because = null)
    {
        var d = r.For(id)!;
        Assert.True(d.Tier == OrientationTier.Review, id + ": " + d.Summary());
        Assert.Null(d.MarkerRotation);
        if (because != null) Assert.Contains(because, d.ReviewReason);
    }

    /// <summary>A four-way intersection at the origin; striped centre lines stop 30 ft short.</summary>
    private static Scene FourWay() => new Scene()
        .Line(LineRole.Centerline, -300, 0, -30, 0).Line(LineRole.Centerline, 30, 0, 300, 0)
        .Line(LineRole.Centerline, 0, -300, 0, -30).Line(LineRole.Centerline, 0, 30, 0, 300);

    // ----------------------------------------------------------------- block zero and angle conventions

    [Theory]
    // facing azimuth, block reference (CAD deg), period, expected marker rotation (CAD deg)
    [InlineData(90, 90, 360, 270)]     // PMX sign (face north at zero) turned to face east
    [InlineData(90, 0, 360, 0)]        // a sign block drawn facing east needs no turn
    [InlineData(90, 270, 360, 90)]     // a block drawn facing south
    [InlineData(270, 90, 180, 90)]     // a bowtie valve: west is the same axis as east
    [InlineData(90, 90, 180, 90)]
    [InlineData(0, 90, 180, 0)]
    [InlineData(135, 0, 360, 315)]     // the old FTF assumption (east-facing block) for "SN 135"
    [InlineData(135, 90, 360, 225)]    // the PMX sign block for "SN 135"
    public void MarkerRotationAccountsForTheBlocksOwnZero(double azimuth, double reference, double period, double expected)
    {
        var symbol = new SymbolRule { ReferenceCadDegrees = reference, Period = period };
        Assert.Equal(expected, SymbolRotation.MarkerDegrees(symbol, azimuth)!.Value, 6);
        var back = SymbolRotation.AzimuthOfRotation(symbol, SymbolRotation.MarkerRadians(symbol, azimuth)!.Value);
        Assert.True(SymbolRotation.Difference(back, azimuth, period) < 1e-6);
    }

    [Fact]
    public void ASymmetricSymbolIsNeverRotated()
    {
        Assert.Null(SymbolRotation.MarkerDegrees(new SymbolRule { Period = 0 }, 123));
        var r = new Scene().Line(LineRole.Centerline, -300, 0, 300, 0).Point("L1", "LT", 0, -20).Run();
        Assert.Equal(OrientationTier.NotDirectional, r.For("L1")!.Tier);
        Assert.Null(r.For("L1")!.MarkerRotation);
    }

    [Fact]
    public void ThePmxStandardMatchesItsBlocks()
    {
        var std = OrientationStandard.Pmx();
        Assert.Equal(90, std.SymbolFor("SN")!.ReferenceCadDegrees);      // SSV-SN: panel north of the post
        Assert.Equal(OrientKind.Sign, std.SymbolFor("SNS")!.Kind);
        Assert.Equal(OrientKind.Other, std.SymbolFor("SNL1")!.Kind);     // a sign line is linework
        Assert.Equal(0, std.SymbolFor("WFH")!.ReferenceCadDegrees);      // SSV-WFH: nozzles east
        Assert.Equal(180, std.SymbolFor("WVL")!.Period);                 // SSV-WVL: bowtie axis
        Assert.Equal(0, std.SymbolFor("LT")!.Period);                    // SSV-LT: round
        Assert.Equal(OrientKind.PavementArrow, std.SymbolFor("ALTO")!.Kind);
        Assert.True(std.SymbolFor("BART")!.RotateLabel);
    }

    // ----------------------------------------------------------------- signs on roads

    [Fact]
    public void SignsOnAStraightTwoWayRoadFaceTheTrafficBesideThem()
    {
        var r = new Scene().Line(LineRole.Centerline, 0, 0, 1000, 0)
            .Point("S", "SN", 500, -20).Point("N", "SN", 500, 20).Run();
        Faces(r, "S", 270, OrientationTier.Alignment);       // eastbound traffic arrives from the west
        Faces(r, "N", 90, OrientationTier.Alignment);
        Rotation(r, "S", 90);                                 // PMX sign: face north at zero, turned to west
        Rotation(r, "N", 270);
    }

    [Fact]
    public void ASignOnACurveFollowsTheLocalTangent()
    {
        var s = new Scene();
        var pts = new List<double>();
        for (var a = 0.0; a <= 90; a += 0.5) { pts.Add(300 * Math.Cos(Rad(a))); pts.Add(300 * Math.Sin(Rad(a))); }
        s.Line(LineRole.Centerline, pts.ToArray());
        s.Point("OUT", "SN", 330 * Math.Cos(Rad(45)), 330 * Math.Sin(Rad(45)));
        s.Point("IN", "SN", 275 * Math.Cos(Rad(45)), 275 * Math.Sin(Rad(45)));
        var r = s.Run();
        Faces(r, "OUT", 135);
        Faces(r, "IN", 315);
    }

    [Fact]
    public void StopSignsAtAFourWayFaceTheirOwnApproachNotTheNearestRoad()
    {
        var r = FourWay()
            .Point("NE", "SNS", 40, 18).Point("SW", "SNS", -40, -18)
            .Point("NW", "SNS", -18, 40).Point("SE", "SNS", 18, -40).Run();
        Assert.Single(r.Intersections);
        Faces(r, "NE", 90, OrientationTier.Relationship);   // westbound traffic on the east leg
        Faces(r, "SW", 270, OrientationTier.Relationship);
        Faces(r, "NW", 0, OrientationTier.Relationship);
        Faces(r, "SE", 180, OrientationTier.Relationship);
        Rotation(r, "NE", 270);
        Rotation(r, "NW", 0);
    }

    [Fact]
    public void AStopSignOnNoApproachIsReviewedNotTakenFromTheNearestRoad()
    {
        // NE corner, tight to the north leg's east side: the left of southbound traffic, and the
        // nearest road's keep-right rule would face it at traffic leaving the intersection.
        var r = FourWay().Point("ODD", "SNS", 12, 40).Run();
        ForReview(r, "ODD", "on the right of no approach");
    }

    [Fact]
    public void AStopSignOnTheStemOfATFacesTheStemTraffic()
    {
        var r = new Scene().Line(LineRole.Centerline, -300, 0, 300, 0).Line(LineRole.Centerline, 0, -300, 0, -30)
            .Point("STEM", "SNS", 18, -40).Run();
        Assert.Single(r.Intersections);
        Assert.Equal(3, r.Intersections[0].Approaches.Count);
        Faces(r, "STEM", 180, OrientationTier.Relationship);
    }

    [Fact]
    public void ASkewedApproachIsServedAtItsOwnAngle()
    {
        var u = new P2(Math.Cos(Rad(60)), Math.Sin(Rad(60)));         // leg heading azimuth 30
        var s = new Scene().Line(LineRole.Centerline, -300, 0, -30, 0).Line(LineRole.Centerline, 30, 0, 300, 0)
            .Line(LineRole.Centerline, u.X * 30, u.Y * 30, u.X * 300, u.Y * 300)
            .Line(LineRole.Centerline, -u.X * 30, -u.Y * 30, -u.X * 300, -u.Y * 300);
        var at = u * 45 + new P2(-0.866, 0.5) * 18;
        s.Point("SKEW", "SNS", at.X, at.Y);
        var r = s.Run();
        Faces(r, "SKEW", 30, OrientationTier.Relationship);
    }

    [Fact]
    public void OneWayAndDividedRoadsAreNotTreatedAsTwoWay()
    {
        // One-way westbound: a sign on the left (south) still faces the approaching traffic.
        var oneWay = new Scene().Line(LineRole.OneWay, 500, 0, -500, 0).Point("L", "SN", 0, -20).Point("R", "SN", 0, 20).Run();
        Faces(oneWay, "L", 90);
        Faces(oneWay, "R", 90);

        // Divided: eastbound carriageway south, westbound north; a median sign beside the eastbound one.
        var divided = new Scene().Line(LineRole.OneWay, -500, -20, 500, -20).Line(LineRole.OneWay, 500, 20, -500, 20)
            .Point("MED", "SN", 0, -8).Run();
        Faces(divided, "MED", 270);
    }

    [Fact]
    public void AStopBarDecidesWhichTrafficAStopSignFaces()
    {
        // North-south two-way road; a stop bar across the northbound lane, skewed 30 degrees.
        var r = new Scene().Line(LineRole.Centerline, 0, -300, 0, 300)
            .Line(LineRole.StopBar, 0, -40, 10.4, -34)
            .Point("STOP", "SNS", 18, -45).Run();
        Faces(r, "STOP", 180, OrientationTier.Relationship);
        Assert.Contains("stop bar", r.For("STOP")!.Association);
    }

    [Fact]
    public void AMeasuredFacingWinsOverInference()
    {
        var s = new Scene().Line(LineRole.Centerline, 0, 0, 1000, 0)
            .Point("M", "SN", 500, -20, "FACES E").Point("A", "SN 45", 300, -20);
        // "SN 45": the office sign rule reads the number as the field azimuth (FTFORIENT passes it in).
        s.S.Features.Single(f => f.Id == "A").MeasuredAzimuth = 45;
        var r = s.Run();
        Faces(r, "M", 90, OrientationTier.Measured);         // inference would say 270
        Faces(r, "A", 45, OrientationTier.Measured);
        Rotation(r, "A", 315);
    }

    [Fact]
    public void MultipleSignsOnOnePostAreEachMeasuredOrSentToReview()
    {
        var r = new Scene().Line(LineRole.Centerline, 0, 0, 1000, 0)
            .Point("A", "SN", 500, -20, "STOP FACES W").Point("B", "SN", 500.5, -20, "FACES N")
            .Point("C", "SN", 500, -20.5).Run();
        Faces(r, "A", 270, OrientationTier.Measured);
        Faces(r, "B", 0, OrientationTier.Measured);
        ForReview(r, "C", "signs on one post");
        Assert.NotNull(r.For("C")!.Azimuth);                 // the suggestion is still shown for review
    }

    [Fact]
    public void ASignBetweenTwoRoadsIsAmbiguous()
    {
        var r = new Scene().Line(LineRole.Centerline, -500, 0, 500, 0).Line(LineRole.Centerline, -500, 60, 500, 60)
            .Point("MID", "SN", 0, 30).Run();
        ForReview(r, "MID", "near two roads");
        Assert.NotNull(r.For("MID")!.AlternativeAzimuth);
    }

    [Fact]
    public void AStreetNameSignNeedsItsFacingRecorded()
    {
        var r = new Scene().Line(LineRole.Centerline, 0, 0, 1000, 0).Point("ST", "SNST", 500, -20).Run();
        ForReview(r, "ST", "two-sided");
    }

    [Fact]
    public void AShotSignPanelGivesTheFaceFromThePostSide()
    {
        var r = new Scene().Line(LineRole.Centerline, 0, 0, 1000, 0)
            .Line(LineRole.SignPanel, 498, -19, 502, -19)               // panel 1 ft north of the post
            .Point("P", "SN", 500, -20).Run();
        Faces(r, "P", 0, OrientationTier.Measured);
    }

    // ----------------------------------------------------------------- lights and signals

    [Fact]
    public void StreetLightArmsReachTheRoadOrFollowWhatWasMeasured()
    {
        var r = new Scene().Line(LineRole.Centerline, -300, 0, 300, 0)
            .Point("A", "LTARM", 0, -20)
            .Point("M", "LTARM", 100, -20, "ARM W")
            .Point("TWO", "LTARM", 200, -20, "ARMS N S")
            .Point("P", "LTARM", -100, -20).Point("LUM", "LUM", -100, -10)
            .Run();
        Faces(r, "A", 0, OrientationTier.Alignment);
        Rotation(r, "A", 0);                                   // SSV-LTARM: arm north at zero
        Faces(r, "M", 270, OrientationTier.Measured);
        Faces(r, "TWO", 0, OrientationTier.Measured);
        Assert.Equal("2 arms", r.For("TWO")!.Association);
        Faces(r, "P", 0, OrientationTier.Measured);           // luminaire shot at the end of the arm
    }

    [Fact]
    public void ASignalPoleIsAssociatedWithTheApproachItsArmSpans()
    {
        var r = FourWay().Point("SIG", "TSP", 25, 45).Point("AMB", "TSP", 30, 30).Run();
        Faces(r, "SIG", 270, OrientationTier.Relationship);
        ForReview(r, "AMB", "several approaches");
    }

    // ----------------------------------------------------------------- hydrants

    [Fact]
    public void AHydrantNozzleIsMeasuredOrInferredTowardTheStreet()
    {
        var r = new Scene().Line(LineRole.Edge, -300, -12, 300, -12).Line(LineRole.Edge, -300, 12, 300, 12)
            .Point("SEEN", "WFH", 0, -25, "NOZ E").Point("HID", "WFH", 50, -25).Run();
        Faces(r, "SEEN", 90, OrientationTier.Measured);
        Rotation(r, "SEEN", 0);                                 // SSV-WFH: nozzle east at zero
        Faces(r, "HID", 0, OrientationTier.Inferred);
        Rotation(r, "HID", 90);
    }

    [Fact]
    public void ACornerHydrantWithTwoStreetsIsSentToReview()
    {
        var r = new Scene().Line(LineRole.Edge, -300, -12, -12, -12).Line(LineRole.Edge, -12, -12, -12, -300)
            .Point("C", "WFH", -22, -22).Run();
        ForReview(r, "C", "two roads");
    }

    // ----------------------------------------------------------------- valves

    private static Scene HydrantStreet() => new Scene()
        .Line(LineRole.Centerline, -300, 0, 300, 0).Line(LineRole.Edge, -300, -12, 300, -12);

    [Fact]
    public void AThreeValveTeeFindsTheHydrantLateral()
    {
        var r = HydrantStreet()
            .Point("M1", "WVL", -5, -6).Point("M2", "WVL", 5, -6).Point("L", "WVL", 0, -14)
            .Point("H", "WFH", 0, -22).Run();
        Faces(r, "M1", 90, OrientationTier.Relationship);
        Faces(r, "M2", 90, OrientationTier.Relationship);
        Faces(r, "L", 180, OrientationTier.Relationship);
        Assert.Contains("hydrant lateral", r.For("L")!.Association);
        Rotation(r, "M1", 90);                                  // bowtie drawn N-S, turned to E-W
        Rotation(r, "L", 0);                                    // N-S axis: no turn
        Assert.Contains(r.Connections, c => c.Kind.StartsWith("hydrant lateral") && c.FeatureIds.Contains("L"));
        Assert.Contains(r.Connections, c => c.Kind.StartsWith("water main"));
        Assert.All(r.Connections, c => Assert.Contains("inferred", c.Kind));
    }

    [Fact]
    public void ATeeTurnedWithTheRoadIsStillFound()
    {
        // The same tee on a road heading azimuth 45 (CAD 45 degrees).
        var u = new P2(Math.Sqrt(0.5), Math.Sqrt(0.5));
        var n = new P2(u.Y, -u.X);                                // right of the road direction
        P2 At(double along, double off) => u * along + n * off;
        var s = new Scene().Line(LineRole.Centerline, -300 * u.X, -300 * u.Y, 300 * u.X, 300 * u.Y);
        var m1 = At(-5, 6); var m2 = At(5, 6); var l = At(0, 14); var h = At(0, 22);
        s.Point("M1", "WVL", m1.X, m1.Y).Point("M2", "WVL", m2.X, m2.Y).Point("L", "WVL", l.X, l.Y).Point("H", "WFH", h.X, h.Y);
        var r = s.Run();
        Faces(r, "M1", 45);
        Faces(r, "L", 135);
    }

    [Fact]
    public void TwoValvesOneOnTheLateral()
    {
        var r = HydrantStreet().Point("M", "WVL", 0, -6).Point("L", "WVL", 0, -14).Point("H", "WFH", 0, -22).Run();
        Faces(r, "M", 90, OrientationTier.Relationship);
        Faces(r, "L", 180, OrientationTier.Relationship);
    }

    [Fact]
    public void ValvesFarFromAnyHydrantAreNotLinkedToOne()
    {
        var r = HydrantStreet()
            .Point("A", "WVL", 0, -6).Point("B", "WVL", 10, -6).Point("C", "WVL", 20, -6)   // in line, along the road
            .Point("X", "WVL", 100, -6).Point("Y", "WVL", 108, -14).Point("Z", "WVL", 103, -18) // irregular
            .Point("H", "WFH", 400, -22).Run();
        Faces(r, "A", 90, OrientationTier.Alignment);
        Assert.DoesNotContain("hydrant", r.For("A")!.Association);
        ForReview(r, "X", "no hydrant");
        Assert.Empty(r.Connections);
    }

    [Fact]
    public void AnIrregularClusterAtAHydrantIsNotForcedIntoATee()
    {
        var r = HydrantStreet()
            .Point("A", "WVL", -8, -4).Point("B", "WVL", 6, -15).Point("C", "WVL", -2, -18)
            .Point("H", "WFH", 20, -30).Run();
        foreach (var id in new[] { "A", "B", "C" }) ForReview(r, id);
        Assert.Empty(r.Connections);
    }

    [Fact]
    public void AValveOnAMappedMainFollowsIt()
    {
        var r = HydrantStreet().Line(LineRole.WaterMain, -300, -6, 300, -6)
            .Point("V", "WVL", 40, -5).Point("G", "GVL", 0, 20, "N-S").Run();
        Faces(r, "V", 90, OrientationTier.Alignment);
        Faces(r, "G", 0, OrientationTier.Measured);
        Rotation(r, "G", 90);                                   // gas valve axis east at zero
    }

    [Fact]
    public void ASymmetricValveSymbolKeepsItsRelationshipButIsNotRotated()
    {
        var std = OrientationStandard.Pmx();
        std.SymbolFor("WVL")!.Period = 0;
        var r = HydrantStreet()
            .Point("M1", "WVL", -5, -6).Point("M2", "WVL", 5, -6).Point("L", "WVL", 0, -14)
            .Point("H", "WFH", 0, -22).Run(std);
        Assert.Null(r.For("L")!.MarkerRotation);
        Assert.Contains("hydrant lateral", r.For("L")!.Association);
        Assert.NotEmpty(r.Connections);
    }

    // ----------------------------------------------------------------- pavement markings

    [Fact]
    public void ArrowsAndWordsPointWithTheirLane()
    {
        var r = new Scene().Line(LineRole.Centerline, -500, 0, 500, 0)
            .Point("EB", "ASTO", 100, -6).Point("WB", "ALTO", 100, 6).Point("WORD", "BART", 150, -6).Run();
        Faces(r, "EB", 90);
        Faces(r, "WB", 270);
        Faces(r, "WORD", 90);
        Rotation(r, "EB", 270);                                 // SSV-ASTO travels north at zero
        Rotation(r, "WB", 90);
        Rotation(r, "WORD", 270);
    }

    [Fact]
    public void ArrowsApproachingAnIntersectionPointIntoIt()
    {
        var r = FourWay().Point("IN", "ALTO", 60, 6).Point("OUT", "ASTO", 60, -6).Run();
        Faces(r, "IN", 270, OrientationTier.Relationship);
        Faces(r, "OUT", 90);
    }

    [Fact]
    public void AMeasuredArrowTellsASignWhichWayTrafficGoes_AnInferredOneDoesNot()
    {
        // No centre line: only edges. A measured arrow says the near lane runs west (one-way, unmapped).
        var measured = new Scene().Line(LineRole.Edge, -300, -12, 300, -12).Line(LineRole.Edge, -300, 12, 300, 12)
            .Point("ARROW", "ASTO", 0, -6, "AZ 270").Point("SIGN", "SN", 10, -20).Run();
        Faces(measured, "SIGN", 90, OrientationTier.Relationship);
        Assert.Contains("measured arrow", measured.For("SIGN")!.Association);

        var inferred = new Scene().Line(LineRole.Edge, -300, -12, 300, -12).Line(LineRole.Edge, -300, 12, 300, 12)
            .Point("ARROW", "ASTO", 0, -6).Point("SIGN", "SN", 10, -20).Run();
        Assert.DoesNotContain("arrow", inferred.For("SIGN")!.Association ?? "");
    }

    // ----------------------------------------------------------------- measured directions

    [Theory]
    [InlineData("SN AZ 135", null, OrientKind.Sign, new[] { 135.0 })]
    [InlineData("SN 135AZ", null, OrientKind.Sign, new[] { 135.0 })]
    [InlineData("SN", "STOP FACES NE", OrientKind.Sign, new[] { 45.0 })]
    [InlineData("WFH", "NOZ W", OrientKind.Hydrant, new[] { 270.0 })]
    [InlineData("WFH", "STEAMER 180", OrientKind.Hydrant, new[] { 180.0 })]
    [InlineData("LTARM", "ARMS N S", OrientKind.StreetLight, new[] { 0.0, 180.0 })]
    [InlineData("WVL", "N-S", OrientKind.WaterValve, new[] { 0.0 })]
    [InlineData("WVL EW", null, OrientKind.WaterValve, new[] { 90.0 })]
    [InlineData("SN", "STOP", OrientKind.Sign, new double[0])]
    [InlineData("WVL", "NEW VALVE", OrientKind.WaterValve, new double[0])]
    public void DirectionsTheCrewRecordedAreRead(string description, string? note, OrientKind kind, double[] expected)
    {
        Assert.Equal(expected, MeasuredDirection.Read(description, note, kind));
    }

    // ----------------------------------------------------------------- side roads drawn by their edges only

    /// <summary>
    /// A rural T as a real PMX topo surveyed it: the main road's double yellow (east-west), its edges, and a
    /// side road to the north shot as two edges with no centre line. The main road's north edges
    /// turn the corners up the side road.
    /// </summary>
    private static Scene EdgeOnlyT() => new Scene()
        .Line(LineRole.Centerline, -300, 0, 300, 0)
        .Line(LineRole.Edge, -300, 18, -25, 18, -15, 28, -15, 300)
        .Line(LineRole.Edge, 300, 18, 25, 18, 15, 28, 15, 300)
        .Line(LineRole.Edge, -300, -18, 300, -18);

    [Fact]
    public void AStopSignOnASideRoadWithNoCentreLineFacesTheTrafficComingUpIt()
    {
        var r = EdgeOnlyT().Point("S1", "SNS", -22, 45).Run();
        Faces(r, "S1", 0, OrientationTier.Relationship);      // southbound drivers coming up to the main road
        Rotation(r, "S1", 0);
        Assert.Contains("side road", r.For("S1")!.Association);
    }

    [Fact]
    public void AStopSignOnTheLeftOfTheSideRoadTrafficIsForReview()
    {
        var r = EdgeOnlyT().Point("S2", "SNS", 22, 45).Run();
        ForReview(r, "S2", "on the left of the traffic");
    }

    [Fact]
    public void ASignBesideASideRoadFollowsThatRoadNotTheCentreLineItMeets()
    {
        // 7 ft off the side road's edge, 60 ft off the main road's stripe: it serves the side road.
        var r = EdgeOnlyT().Point("N1", "SN", 22, 60).Run();
        Faces(r, "N1", 180, OrientationTier.Alignment);       // northbound traffic keeps right
        Assert.Contains("side road", r.For("N1")!.Evidence.Last().Text);
    }

    [Fact]
    public void ASignAlongTheMainRoadIsUnchangedByItsOwnEdges()
    {
        var r = EdgeOnlyT().Point("M1", "SN", 120, 26).Run();
        Faces(r, "M1", 90, OrientationTier.Alignment);        // westbound traffic on the north side
    }

    [Fact]
    public void AStopSignBesideAThroughRoadWithNothingToStopAtIsSuggestedNotApplied()
    {
        var r = EdgeOnlyT().Point("S3", "SN STOP", 120, -26).Run();
        ForReview(r, "S3", "no stop bar, intersection approach or side road");
        Assert.Equal(270, r.For("S3")!.Azimuth!.Value, 1);   // the suggestion: facing eastbound traffic
    }
}
