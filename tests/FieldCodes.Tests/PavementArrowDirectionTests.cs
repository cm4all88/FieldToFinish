using FieldCodes.Geometry;
using FieldCodes.Easements;
using FieldCodes.Orient;

namespace FieldCodes.Tests;

/// <summary>
/// Pavement arrows whose direction of travel the crew wrote: "ASTO N" / "ASTO S" (points 10075 and
/// 10076 in the reference drawing, both drawn pointing the same way before). The written direction
/// decides which way along the lane; the road decides the exact angle, following curves and
/// picking the approach street at an intersection; the block's own zero is taken off. Scenes are
/// synthetic (no project coordinates); X is east, Y is north, azimuths clockwise from north.
/// </summary>
public sealed class PavementArrowDirectionTests
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

        public Scene Point(string id, string description, double x, double y, string? fullDescription = null, string? note = null)
        {
            var code = OrientationStandard.BaseCode(description);
            var rule = OrientationStandard.Pmx().SymbolFor(code);
            S.Features.Add(new OrientFeature
            {
                Id = id, Code = code, Description = description, FullDescription = fullDescription, Note = note,
                Location = new P2(x, y), Kind = rule?.Kind ?? OrientKind.Other
            });
            return this;
        }

        public OrientationResult Run(OrientationStandard? std = null) => new OrientationEngine(std ?? OrientationStandard.Pmx()).Run(S);
    }

    private static OrientationDecision Of(OrientationResult r, string id) => r.For(id)!;

    private static void Faces(OrientationResult r, string id, double azimuth, double tolerance = 0.5)
    {
        var d = Of(r, id);
        Assert.True(d.Azimuth.HasValue && SymbolRotation.Difference(d.Azimuth.Value, azimuth) < tolerance,
                    id + ": expected " + azimuth + ", got " + d.Summary());
    }

    private static double RotationDegrees(OrientationResult r, string id)
    {
        var d = Of(r, id);
        Assert.True(d.MarkerRotation.HasValue, id + ": not rotated; " + d.Summary());
        return Angles.NormalizeDegrees(d.MarkerRotation!.Value * 180 / Math.PI);
    }

    /// <summary>A two-way road running 17.6 degrees east of north (as the reference drawing's road does at those
    /// two arrows), centre line through the origin.</summary>
    private static Scene SkewedRoad()
    {
        var a = 17.6 * Math.PI / 180;
        var u = new P2(Math.Sin(a), Math.Cos(a));
        return new Scene().Line(LineRole.Centerline, -400 * u.X, -400 * u.Y, 400 * u.X, 400 * u.Y);
    }

    /// <summary>Along the skewed road <paramref name="along"/> ft, <paramref name="right"/> ft to
    /// the east side of the centre line.</summary>
    private static (double X, double Y) OnSkewed(double along, double right)
    {
        var a = 17.6 * Math.PI / 180;
        var u = (X: Math.Sin(a), Y: Math.Cos(a));
        var n = (X: u.Y, Y: -u.X);
        return (u.X * along + n.X * right, u.Y * along + n.Y * right);
    }

    [Fact]
    public void NorthboundAndSouthboundArrowsPointOppositeWays()
    {
        // Both on the same side of the centre line, 10.7 ft apart -- the keep-right rule alone
        // made them both northbound. The crew's N and S decide.
        var p75 = OnSkewed(0, 6);
        var p76 = OnSkewed(10.7, 6);
        var r = SkewedRoad().Point("10075", "ASTO N", p75.X, p75.Y).Point("10076", "ASTO S", p76.X, p76.Y).Run();

        Faces(r, "10075", 17.6);
        Faces(r, "10076", 197.6);
        Assert.Equal(OrientationTier.Measured, Of(r, "10076").Tier);
        Assert.Contains("southbound", Of(r, "10076").Evidence[0].Text);

        // SSV-ASTO travels north at rotation 0: the two markers differ by exactly 180 degrees.
        var n = RotationDegrees(r, "10075");
        var s = RotationDegrees(r, "10076");
        Assert.Equal(180, SymbolRotation.Difference(n, s), 3);
        Assert.Equal(Angles.NormalizeDegrees(90 - 197.6 - 90), s, 3);
    }

    [Fact]
    public void BeforeTheFixTheSameSceneGaveBothArrowsOneDirection()
    {
        var p75 = OnSkewed(0, 6);
        var p76 = OnSkewed(10.7, 6);
        var off = OrientationStandard.Pmx();
        off.MarkingDirectionWords = false;
        var r = SkewedRoad().Point("10075", "ASTO N", p75.X, p75.Y).Point("10076", "ASTO S", p76.X, p76.Y).Run(off);
        Assert.Equal(Of(r, "10075").Azimuth!.Value, Of(r, "10076").Azimuth!.Value, 3);
    }

    [Fact]
    public void TheFullDescriptionIsReadToo()
    {
        var p = OnSkewed(0, 6);
        var r = SkewedRoad().Point("A", "ASTO", p.X, p.Y, "STRAIGHT ARROW S").Run();
        Faces(r, "A", 197.6);
    }

    [Theory]
    [InlineData("ASTO S", null, null, 180, "S")]
    [InlineData("ASTO SB", null, null, 180, "SB")]
    [InlineData("ASTO S/B", null, null, 180, "S/B")]
    [InlineData("ASLT NORTHBOUND", null, null, 0, "NORTHBOUND")]
    [InlineData("ARTO SOUTH BOUND", null, null, 180, "SOUTH BOUND")]
    [InlineData("ASTO NE", null, null, 45, "NE")]
    [InlineData("ASTO WB", null, null, 270, "WB")]
    [InlineData("ASTO", "STRAIGHT ARROW E", null, 90, "E")]
    [InlineData("ASTO", "STRAIGHT ARROW", "EB LANE", 90, "EB")]
    public void DirectionWordsAreRead(string raw, string? full, string? note, double azimuth, string words)
    {
        string got;
        var a = TravelWords.Read(raw, full, note, out got);
        Assert.Equal(azimuth, a);
        Assert.Equal(words, got);
    }

    [Theory]
    [InlineData("ASTO", "STRAIGHT ARROW", null)]
    [InlineData("ALTO", "LEFT TURN ARROW", null)]
    [InlineData("ASTO W/ THERMO", null, null)]
    [InlineData("ASTO", null, "WORN")]
    public void NoDirectionIsInventedFromOtherWords(string raw, string? full, string? note)
    {
        string got;
        Assert.Null(TravelWords.Read(raw, full, note, out got));
    }

    [Fact]
    public void LeftRightAndCombinedArrowsTurnTheSameWay()
    {
        var r = SkewedRoad()
            .Point("L", "ALTO S", OnSkewed(0, -6).X, OnSkewed(0, -6).Y)
            .Point("R", "ARTO S", OnSkewed(20, -6).X, OnSkewed(20, -6).Y)
            .Point("SL", "ASLT S", OnSkewed(40, -6).X, OnSkewed(40, -6).Y)
            .Point("SR", "ASRT S", OnSkewed(60, -6).X, OnSkewed(60, -6).Y)
            .Point("LR", "ASLR S", OnSkewed(80, -6).X, OnSkewed(80, -6).Y)
            .Run();
        foreach (var id in new[] { "L", "R", "SL", "SR", "LR" }) Faces(r, id, 197.6);
    }

    [Fact]
    public void OnACurveTheArrowFollowsTheTangentWhereItStands()
    {
        // A quarter circle of radius 300 about the origin, from due east of it to due north of it.
        var s = new Scene();
        var pts = new List<double>();
        for (var i = 0; i <= 90; i += 2)
        {
            var t = i * Math.PI / 180;
            pts.Add(300 * Math.Cos(t));
            pts.Add(300 * Math.Sin(t));
        }
        s.Line(LineRole.Centerline, pts.ToArray());
        // 30 degrees round the curve, 6 ft outside it: the road runs 330 / 150 degrees azimuth
        // there (the curve is drawn in 2-degree chords, so within a degree or so). Due north or
        // the chord between the curve's ends would both be wrong.
        var t30 = 30 * Math.PI / 180;
        var r = s.Point("A", "ASTO N", 306 * Math.Cos(t30), 306 * Math.Sin(t30)).Run();
        Faces(r, "A", 330, 1.5);
        // 70 degrees round the road runs 290 / 110: a westbound arrow follows it; a southbound
        // one is 69 degrees off and goes to review rather than being guessed.
        var t70 = 70 * Math.PI / 180;
        var further = s.Point("B", "ASTO W", 306 * Math.Cos(t70), 306 * Math.Sin(t70))
                       .Point("C", "ASTO S", 306 * Math.Cos(t70) + 0.5, 306 * Math.Sin(t70)).Run();
        Faces(further, "B", 290, 2);
        Assert.Equal(OrientationTier.Review, Of(further, "C").Tier);
    }

    [Fact]
    public void AtAnIntersectionTheApproachStreetIsUsedNotTheCrossStreet()
    {
        // Four-way at the origin; the cross street's edge is nearer the arrow than the arrow's own
        // street's centre line, but the arrow says S and the cross street runs east-west.
        var r = new Scene()
            .Line(LineRole.Centerline, 0, 30, 0, 300).Line(LineRole.Centerline, 0, -30, 0, -300)
            .Line(LineRole.Centerline, 30, 0, 300, 0).Line(LineRole.Centerline, -30, 0, -300, 0)
            .Line(LineRole.Edge, 12, 12, 300, 12).Line(LineRole.Edge, -300, 12, -12, 12)
            .Point("A", "ASTO S", -6, 18).Run();
        Faces(r, "A", 180);
    }

    [Fact]
    public void WithNoRoadNearTheWrittenDirectionIsUsedOnTheDrawingGrid()
    {
        var r = new Scene().Point("A", "ASTO SW", 0, 0).Run();
        Faces(r, "A", 225);
        Assert.Contains("no road line", Of(r, "A").Evidence[0].Text);
    }

    [Fact]
    public void MatchingToTheRoadCanBeTurnedOff()
    {
        var p = OnSkewed(0, 6);
        var std = OrientationStandard.Pmx();
        std.MarkingFollowRoad = false;
        var r = SkewedRoad().Point("A", "ASTO S", p.X, p.Y).Run(std);
        Faces(r, "A", 180);
    }

    [Fact]
    public void ADirectionTheRoadCannotHaveIsPutUpForReviewNotGuessed()
    {
        var p = OnSkewed(0, 6);
        var r = SkewedRoad().Point("A", "ASTO E", p.X, p.Y).Run();
        var d = Of(r, "A");
        Assert.Equal(OrientationTier.Review, d.Tier);
        Assert.Null(d.MarkerRotation);
        Assert.Contains("check the description", d.ReviewReason);
    }

    [Fact]
    public void AMeasuredAzimuthStillWins()
    {
        var p = OnSkewed(0, 6);
        var r = SkewedRoad().Point("A", "ASTO S AZ 200", p.X, p.Y).Run();
        Faces(r, "A", 200);
    }

    [Fact]
    public void TheCrewsDirectionWinsOverKeepRightAndTheReportSaysSo()
    {
        // East side of a north-south road: keep right says northbound; the crew wrote S.
        var r = new Scene().Line(LineRole.Centerline, 0, -300, 0, 300).Point("A", "ASTO S", 6, 0).Run();
        Faces(r, "A", 180);
        Assert.Contains(Of(r, "A").Evidence, e => e.Text.Contains("the crew's direction was used"));
    }

    [Fact]
    public void TheBlocksOwnZeroIsTakenOff()
    {
        // An office arrow block drawn pointing east at rotation 0 (zero 0 instead of PMX's 90).
        var std = OrientationStandard.Pmx();
        std.Symbols.First(x => x.Kind == OrientKind.PavementArrow).ReferenceCadDegrees = 0;
        var r = new Scene().Line(LineRole.Centerline, 0, -300, 0, 300).Point("A", "ASTO S", 6, 0).Run(std);
        Assert.Equal(270, RotationDegrees(r, "A"), 3);         // south is 270 AutoCAD degrees
    }

    [Fact]
    public void TheSettingsReadFromAnOlderRulesFileWithTheirDefaults()
    {
        var std = Newtonsoft.Json.JsonConvert.DeserializeObject<OrientationStandard>("{ \"roadSearchFeet\": 80 }")!;
        Assert.True(std.MarkingDirectionWords);
        Assert.True(std.MarkingFollowRoad);
        Assert.Equal(40, std.MarkingRoadFeet);
        Assert.Equal(60, std.MarkingMaxTurnDegrees);
    }
}
