using FieldCodes;
using FieldCodes.Easements;
using FieldCodes.Orient;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// The orientation standard as the office ships it (rules.json), the field azimuth on a sign
/// code turned against the sign block's own zero, and the report FTFORIENT writes.
/// </summary>
public sealed class OrientationConfigTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public OrientationConfigTests(RulesFixture fx) => _fx = fx;

    // ------------------------------------------------------------ rules.json

    /// <summary>A rules file with one code rule (a file needs one) and the given extra sections.</summary>
    private static RulesConfig Rules(string sections)
    {
        var json = "{ \"codes\": [ { \"id\": \"x\", \"match\": \"^X$\" } ]" + (sections.Length > 0 ? ", " + sections : "") + " }";
        var cfg = JsonConvert.DeserializeObject<RulesConfig>(json)!;
        cfg.Compile();
        return cfg;
    }

    [Fact]
    public void ShippedRulesCarryTheTunablesAndKeepThePmxSymbols()
    {
        var o = _fx.Config.Orientation;
        Assert.NotNull(o);
        Assert.Equal(0.6, o.ApplyMinimumConfidence, 6);
        Assert.Equal(OrientationTier.Inferred, o.ApplyWeakestTier);
        Assert.Equal("FTF-QC-ORIENT", o.QcLayer);

        // The section names no symbol lists, so PMX's apply.
        var pmx = OrientationStandard.Pmx();
        Assert.Equal(pmx.Symbols.Count, o.Symbols.Count);
        Assert.Equal(pmx.Lines.Count, o.Lines.Count);
        Assert.Equal(90, o.SymbolFor("SN")!.ReferenceCadDegrees);
        Assert.Equal(OrientKind.Hydrant, o.SymbolFor("WFH")!.Kind);
        Assert.Contains("LUM", o.LuminaireCodes);
        Assert.Contains("SNS", o.StopSignCodes);
    }

    [Fact]
    public void AListInTheSectionReplacesPmxsInsteadOfAddingToIt()
    {
        var cfg = Rules("\"orientation\": { \"stopSignCodes\": [ \"STOPSN\" ], \"luminaireCodes\": [] }");
        Assert.Equal(new[] { "STOPSN" }, cfg.Orientation.StopSignCodes);
        // An empty list is "not named": PMX's applies.
        Assert.Equal(OrientationStandard.Pmx().LuminaireCodes, cfg.Orientation.LuminaireCodes);
        Assert.Equal(OrientationStandard.Pmx().Symbols.Count, cfg.Orientation.Symbols.Count);
    }

    [Fact]
    public void SymbolsNamedInTheSectionReplaceTheWholeList()
    {
        var cfg = Rules("\"orientation\": { \"symbols\": [ { \"codes\": [ \"MYSIGN\" ], \"kind\": \"Sign\", " +
                        "\"block\": \"OFFICE-SIGN\", \"referenceCadDegrees\": 0, \"period\": 360 } ] }");
        Assert.Single(cfg.Orientation.Symbols);
        Assert.Null(cfg.Orientation.SymbolFor("SN"));
        Assert.Equal(0, cfg.Orientation.SymbolFor("MYSIGN")!.ReferenceCadDegrees);
    }

    [Fact]
    public void ARulesFileWithNoSectionGetsPmx()
    {
        var cfg = Rules("");
        Assert.Equal(OrientationStandard.Pmx().Symbols.Count, cfg.Orientation.Symbols.Count);
    }

    // ------------------------------------------------------------ SN 135 in FTFPOINTS

    [Theory]
    [InlineData("SN 135 STOP", 135)]
    [InlineData("SN 0 STOP", 0)]
    [InlineData("SN 90 NO PARKING", 90)]
    [InlineData("SN 270.5 SPEED 25", 270.5)]
    public void AFieldAzimuthOnASignFacesTheSignThatWay(string description, double azimuth)
    {
        var p = _fx.Parse(description);
        Assert.True(p.RotationDegrees.HasValue, description);
        Assert.True(p.RotationIsAzimuth);

        var marker = SymbolRotation.MarkerDegreesFor(_fx.Config.Orientation, p);
        var sign = _fx.Config.Orientation.SymbolFor(p.Code)!;

        // The SSV-SN block faces north at rotation zero; turned by the marker it must face
        // the azimuth the crew wrote.
        var faces = SymbolRotation.AzimuthOfRotation(sign, Geometry.Angles.CadDegreesToApiRadians(marker));
        Assert.True(SymbolRotation.Difference(faces, azimuth) < 1e-6, description + " faces " + faces);
    }

    [Fact]
    public void SN135TurnsTheMarker225NotTheBareAzimuthAngle()
    {
        var p = _fx.Parse("SN 135 STOP");
        Assert.Equal(315, p.RotationDegrees!.Value, 6);    // the azimuth as an AutoCAD angle
        Assert.Equal(225, SymbolRotation.MarkerDegreesFor(_fx.Config.Orientation, p), 6);
    }

    [Fact]
    public void ACodeTheStandardDoesNotKnowKeepsTheAngleAsGiven()
    {
        var std = OrientationStandard.Pmx();
        var p = new ParsedPoint { Code = "SIGN", RotationDegrees = 315, RotationIsAzimuth = true };
        Assert.Equal(315, SymbolRotation.MarkerDegreesFor(std, p), 6);

        // A rule that gives a plain AutoCAD angle is a rotation, not a direction.
        var plain = new ParsedPoint { Code = "SN", RotationDegrees = 40, RotationIsAzimuth = false };
        Assert.Equal(40, SymbolRotation.MarkerDegreesFor(std, plain), 6);

        // A round light looks the same at every rotation: nothing to correct.
        var light = new ParsedPoint { Code = "LT", RotationDegrees = 40, RotationIsAzimuth = true };
        Assert.Equal(40, SymbolRotation.MarkerDegreesFor(std, light), 6);
    }

    [Theory]
    [InlineData("SSV-SNS", true)]
    [InlineData("ssv-sn", true)]
    [InlineData("SSV-WFH", false)]
    [InlineData("", false)]
    public void ASymbolKnowsItsOwnBlock(string block, bool expected)
    {
        Assert.Equal(expected, OrientationStandard.Pmx().SymbolFor("SN")!.DrawnWith(block));
    }

    // ------------------------------------------------------------ the report

    private static OrientationResult Sample(out Dictionary<OrientationDecision, OrientOutcome> outcomes)
    {
        var std = OrientationStandard.Pmx();
        var sign = new OrientFeature { Id = "101", Code = "SN", Kind = OrientKind.Sign, Location = new P2(0, 0), Description = "SN STOP, MAIN ST" };
        var valve = new OrientFeature { Id = "102", Code = "WV", Kind = OrientKind.WaterValve, Location = new P2(5, 5) };
        var result = new OrientationResult();
        var d1 = new OrientationDecision
        {
            Feature = sign, Symbol = std.SymbolFor("SN"), Tier = OrientationTier.Relationship, Confidence = 0.85,
            Azimuth = 90, MarkerRotation = Geometry.Angles.CadDegreesToApiRadians(270), Association = "stop bar \"A\""
        };
        d1.Evidence.Add(new Evidence(OrientationTier.Relationship, 0.85, "faces traffic stopping at the bar"));
        var d2 = new OrientationDecision
        {
            Feature = valve, Symbol = std.SymbolFor("WV"), Tier = OrientationTier.Review, Confidence = 0.3,
            Azimuth = 0, AlternativeAzimuth = 90, ReviewReason = "two mains equally likely"
        };
        result.Decisions.Add(d1);
        result.Decisions.Add(d2);
        result.Connections.Add(new InferredConnection
        {
            Kind = "hydrant lateral (inferred)", From = new P2(0, 0), To = new P2(10, 0), Confidence = 0.7,
            FeatureIds = new List<string> { "102", "103" }
        });
        outcomes = new Dictionary<OrientationDecision, OrientOutcome> { { d1, OrientOutcome.Rotated }, { d2, OrientOutcome.Review } };
        return result;
    }

    [Fact]
    public void TheCsvListsEveryDecisionStrongestFirstWithBeforeAndAfter()
    {
        var result = Sample(out var outcomes);
        var lines = OrientationReport.Csv(result, outcomes).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

        Assert.Equal(OrientationReport.Header, lines[0]);
        Assert.Equal(4, lines.Count);
        Assert.StartsWith("101,SN,Sign,Relationship,0.85,90.0,,0.0,270.0,Rotated,\"stop bar \"\"A\"\"\",", lines[1]);
        Assert.StartsWith("102,WV,WaterValve,Review,0.30,0.0,90.0,0.0,0.0,Review,", lines[2]);
        Assert.EndsWith("two mains equally likely", lines[2]);
        Assert.StartsWith("102 103,,InferredConnection,Inferred,0.70,", lines[3]);
        Assert.Contains("NotDrawnAsUtility", lines[3]);
    }

    [Fact]
    public void TheSummaryCountsTiersAndOutcomesAndListsReviews()
    {
        var result = Sample(out var outcomes);
        var summary = OrientationReport.Summary(result, outcomes);
        Assert.Equal("2 symbol(s): 1 relationship, 1 review.", summary[0]);
        Assert.Equal("1 rotated, 1 for review.", summary[1]);
        Assert.Contains(summary, l => l.Contains("never drawn as utility lines"));
        Assert.Contains(summary, l => l == "  REVIEW WV 102: two mains equally likely");
    }
}
