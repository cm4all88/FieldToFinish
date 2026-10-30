using FieldCodes.Compare;

namespace FieldCodes.Tests;

/// <summary>The default nonplot layering: each object goes to its own layer's name plus
/// "-NP"; the fixed PMX-COMP-NP-* set stays available. Also the COGO point-number fix:
/// an identifier attribute is never read as an elevation the consultant lacks.</summary>
public sealed class NonplotLayeringTests
{
    private static CompareProfile SuffixProfile()
    {
        var p = CF.Profile();
        p.NonplotLayers.Mode = NonplotLayerNames.ModeSuffix;
        return p;
    }

    [Fact]
    public void TheShippedDefaultUsesTheSuffixConvention()
    {
        var p = CompareProfile.CreateDefault();
        Assert.True(p.NonplotLayers.UseSuffix);
        Assert.Equal("-NP", p.NonplotLayers.Suffix);
        var problems = new List<string>();
        p.Validate(problems);
        Assert.Empty(problems);
    }

    [Fact]
    public void EveryKindGoesToItsOwnLayerPlusNP()
    {
        var profile = SuffixProfile();
        var s = CF.Survey();
        s.Add(CF.Cogo("COGO", "PP 1", 0, 0, 0));
        s.Add(CF.Block("BLK", "V-SIGN", "SIGN", 10, 0));
        s.Add(CF.Line("LINE", "V-SURF-CURB-E", 20, 0, 30, 0));
        s.Add(CF.Text("TXT", "V-SIGN-TEXT", "STOP", 10, 1));
        var c = CF.Consultant();
        c.Add(CF.Block("C1", "E-PWR", "POLE", 0.2, 0));
        c.Add(CF.Block("C2", "E-SIGN", "SIGN", 10.2, 0));
        c.Add(CF.Line("C3", "E-CURB", 20, 0, 30, 0));
        c.Add(CF.Text("C4", "E-SIGN-TXT", "STOP", 10.5, 1.5));
        var result = CF.Run(s, c, profile);
        Assert.Equal("V-NODE-NP", CF.R(result, "COGO").NonplotLayer);
        Assert.Equal("V-SIGN-NP", CF.R(result, "BLK").NonplotLayer);
        Assert.Equal("V-SURF-CURB-E-NP", CF.R(result, "LINE").NonplotLayer);
        Assert.Equal("V-SIGN-TEXT-NP", CF.R(result, "TXT").NonplotLayer);
        var plan = ChangePlanner.Build(result, profile);
        Assert.All(plan.Changes, ch => Assert.Equal(ch.FromLayer + "-NP", ch.ToLayer));
    }

    [Fact]
    public void FixedModeStillUsesTheNamedSet()
    {
        var profile = CF.Profile();
        Assert.False(profile.NonplotLayers.UseSuffix);
        var s = CF.Survey(); s.Add(CF.Cogo("COGO", "PP 1", 0, 0, 0));
        var c = CF.Consultant(); c.Add(CF.Block("C1", "E-PWR", "POLE", 0.2, 0));
        Assert.Equal("PMX-COMP-NP-UTILITY", CF.R(CF.Run(s, c, profile), "COGO").NonplotLayer);
    }

    [Fact]
    public void NonplotNamesAreRecognisedInEitherMode()
    {
        var suffix = new NonplotLayerNames();
        Assert.True(suffix.IsNonplotName("V-UTIL-POWR-POLE-NP"));
        Assert.True(suffix.IsNonplotName("v-node-np"));
        Assert.True(suffix.IsNonplotName("PMX-COMP-NP-LINE"));
        Assert.False(suffix.IsNonplotName("V-UTIL-POWR-POLE"));
        Assert.False(suffix.IsNonplotName("-NP"));
        Assert.Equal("V-NODE-NP", suffix.For("V-NODE", "PMX-COMP-NP-POINT"));
        Assert.Equal("V-NODE-NP", suffix.For("V-NODE-NP", "PMX-COMP-NP-POINT"));   // already a nonplot name
        Assert.Equal("PMX-COMP-NP-POINT", suffix.For("", "PMX-COMP-NP-POINT"));

        var fixedNames = new NonplotLayerNames { Mode = NonplotLayerNames.ModeFixed };
        Assert.False(fixedNames.IsNonplotName("V-UTIL-POWR-POLE-NP"));
        Assert.True(fixedNames.IsNonplotName("PMX-COMP-NP-LINE"));
        Assert.Equal("PMX-COMP-NP-POINT", fixedNames.For("V-NODE", "PMX-COMP-NP-POINT"));
    }

    [Fact]
    public void ProfileValidationCoversModeAndSuffix()
    {
        var p = CompareProfile.CreateDefault();
        p.NonplotLayers.Mode = "Sideways";
        var problems = new List<string>();
        p.Validate(problems);
        Assert.Contains(problems, x => x.Contains("mode"));

        p = CompareProfile.CreateDefault();
        p.NonplotLayers.Suffix = " ";
        problems.Clear();
        p.Validate(problems);
        Assert.Contains(problems, x => x.Contains("suffix"));

        p = CompareProfile.CreateDefault();
        p.NonplotLayers.Suffix = "-N*P";
        problems.Clear();
        p.Validate(problems);
        Assert.Contains(problems, x => x.Contains("character"));

        // A profile saved before the mode existed loads in suffix mode.
        var json = CompareProfile.CreateDefault().ToJson().Replace("\"mode\": \"Suffix\",", "").Replace("\"suffix\": \"-NP\",", "");
        var loaded = CompareProfile.FromJson(json);
        Assert.True(loaded.NonplotLayers.UseSuffix);
        Assert.Equal("-NP", loaded.NonplotLayers.Suffix);
    }

    [Fact]
    public void QcChecksTheSuffixLayersActuallyUsed()
    {
        var profile = SuffixProfile();
        var s = CF.Survey(); s.Add(CF.Line("LINE", "V-SURF-CURB-E", 0, 0, 100, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        var result = CF.Run(s, c, profile);
        var plan = ChangePlanner.Build(result, profile);
        Assert.Single(plan.Changes);
        var input = new QcInput { Profile = profile, Result = result, Plan = plan, DrawingPath = "/tmp/site.dwg", Applied = new AppliedChangeSet { RunId = result.RunId } };
        var change = plan.Changes[0];
        input.Applied.Changes.Add(new AppliedChange { Handle = change.Handle, OriginalLayer = change.FromLayer, NonplotLayer = change.ToLayer, Action = change.Action });
        input.Entities["LINE"] = new EntityState { Handle = "LINE", Exists = true, Layer = "V-SURF-CURB-E-NP" };

        // The used -NP layer missing from the drawing fails; present and no-plot passes;
        // present but plottable fails. The PMX names are not required in suffix mode.
        Assert.False(CompareQc.Evaluate(input).Single(x => x.Code == "NONPLOT-LAYERS").Passed);
        input.Layers["V-SURF-CURB-E-NP"] = new LayerState { Name = "V-SURF-CURB-E-NP", Exists = true, Plottable = false };
        var checks = CompareQc.Evaluate(input);
        Assert.True(checks.Single(x => x.Code == "NONPLOT-LAYERS").Passed, CompareQc.Render(checks));
        Assert.True(checks.Single(x => x.Code == "DUPLICATES-NONPLOT").Passed, CompareQc.Render(checks));
        input.Layers["V-SURF-CURB-E-NP"].Plottable = true;
        checks = CompareQc.Evaluate(input);
        Assert.False(checks.Single(x => x.Code == "NONPLOT-LAYERS").Passed);
        Assert.False(checks.Single(x => x.Code == "DUPLICATES-NONPLOT").Passed);
    }

    [Fact]
    public void APointNumberIsNotAnElevationTheConsultantLacks()
    {
        // Exactly what the first Civil 3D run hit: every COGO point carries its point number
        // as an attribute, and a bare number used to parse as an ELEV fact.
        var s = CF.Survey();
        var cogo = CF.Cogo("S1", "PP 1", 1000, 1200, 0);
        cogo.Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "PointNumber", "501" } };
        s.Add(cogo);
        var mh = CF.Cogo("S2", "SDMH 1", 1050, 1200, 120.5);
        mh.Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "PointNumber", "503" } };
        s.Add(mh);
        var c = CF.Consultant();
        c.Add(CF.Block("C1", "E-PWR", "POLE", 1000.4, 1200.3));
        c.Add(CF.Block("C2", "E-STRM-MH", "STMH", 1050.3, 1200.1));
        var result = CF.Run(s, c);
        Assert.Equal(MatchStatus.Matched, CF.R(result, "S1").Status);
        Assert.Equal(PlannedAction.Nonplot, CF.R(result, "S1").Action);
        Assert.Equal(MatchStatus.Matched, CF.R(result, "S2").Status);
        Assert.Equal(PlannedAction.Nonplot, CF.R(result, "S2").Action);

        // A rim carried in a block attribute still counts as information they lack.
        var s2 = CF.Survey();
        s2.Add(CF.Block("B1", "V-UTIL-STRM", "SDMH", 10, 10, null, new Dictionary<string, string> { { "RIM", "121.50" }, { "NUMBER", "7" } }));
        var c2 = CF.Consultant(); c2.Add(CF.Block("C3", "E-STRM-MH", "STMH", 10.2, 10.1));
        var r = CF.R(CF.Run(s2, c2), "B1");
        Assert.Equal(MatchStatus.NeedsReview, r.Status);
        Assert.Contains(r.Notes, n => n.Contains("RIM"));
        Assert.DoesNotContain(r.Notes, n => n.Contains("ELEV"));
    }

    [Theory]
    [InlineData("PointNumber", true)]
    [InlineData("PT_NO", true)]
    [InlineData("ID", true)]
    [InlineData("STRUCT_ID", true)]
    [InlineData("NAME", true)]
    [InlineData("RIM", false)]
    [InlineData("INV_IN", false)]
    [InlineData("SIZE", false)]
    public void IdentifierTagsAreRecognised(string tag, bool identifier)
    {
        Assert.Equal(identifier, ComparisonEngine.IsIdentifierTag(tag));
    }
}
