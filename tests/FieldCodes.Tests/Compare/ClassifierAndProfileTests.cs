using FieldCodes;
using FieldCodes.Compare;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

[Collection(ProfileDirectoryCollection.Name)]
public sealed class ClassifierAndProfileTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public ClassifierAndProfileTests(RulesFixture fx) => _fx = fx;

    [Theory]
    [InlineData("PP 1234", "UTILITY-POLE")]
    [InlineData("SDMH1", "STORM-MH")]
    [InlineData("SSMH 12 DEAD", "SAN-MH")]
    [InlineData("CB 3", "CATCH-BASIN")]
    [InlineData("WV", "WATER-VALVE")]
    [InlineData("FH 1", "HYDRANT")]
    [InlineData("HH 1", "HANDHOLE")]
    [InlineData("CO 9", "CLEANOUT")]
    public void SurveyCodesClassifyPoints(string description, string category)
    {
        var f = CF.Cogo("1", description, 0, 0, 0);
        var cls = new FeatureClassifier(CF.Profile()).Classify(f);
        Assert.Equal(category, cls.Category);
        Assert.False(cls.Conflict);
        Assert.True(cls.Confidence >= 0.75);
    }

    [Fact]
    public void ConflictingEvidenceIsFlagged()
    {
        var f = CF.Cogo("1", "SDMH 1", 0, 0, 0, "V-UTIL-SSWR-MH");
        var cls = new FeatureClassifier(CF.Profile()).Classify(f);
        Assert.Equal("STORM-MH", cls.Category);      // the code is the stronger evidence
        Assert.True(cls.Conflict);
        Assert.True(cls.Confidence <= 0.5);
        Assert.Contains("SAN-MH", cls.Evidence);
    }

    [Fact]
    public void ConsultantLayerAloneIsEnoughAndSpecificPatternsWinOverGenericOnes()
    {
        var classifier = new FeatureClassifier(CF.Profile());
        var c = CF.Consultant();
        var mhBlock = CF.Block("1", "E-STRM-MH", "MH", 0, 0); c.Add(mhBlock);
        var mh = classifier.Classify(mhBlock);
        Assert.Equal("STORM-MH", mh.Category);
        Assert.False(mh.Conflict);

        var backLine = CF.Line("2", "E-CURB-BACK", 0, 0, 1, 0); c.Add(backLine);
        var curbBack = classifier.Classify(backLine);
        Assert.Equal("CURB-BACK", curbBack.Category);
        Assert.False(curbBack.Conflict);

        var curbLine = CF.Line("3", "E-CURB", 0, 0, 1, 0); c.Add(curbLine);
        var curb = classifier.Classify(curbLine);
        Assert.Equal("CURB", curb.Category);
        Assert.False(curb.Conflict);
    }

    [Fact]
    public void KeywordsAreWholeWords()
    {
        Assert.True(FeatureClassifier.ContainsPhrase("EX STORM MH RIM 123", "STORM MH"));
        Assert.True(FeatureClassifier.ContainsPhrase("storm_mh-12", "STORM MH"));
        Assert.False(FeatureClassifier.ContainsPhrase("CURB", "CB"));
        Assert.False(FeatureClassifier.ContainsPhrase("UPPER", "UP"));
    }

    [Fact]
    public void UnknownLayersAreReportedNotIgnored()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", "XX-UNKNOWN", 0, 0, 10, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "YY-UNKNOWN", 0, 0, 10, 0));
        var result = CF.Run(s, c);
        var r = CF.R(result, "S1");
        Assert.Equal(MatchStatus.NotEvaluated, r.Status);
        Assert.Contains(r.Notes, n => n.Contains("XX-UNKNOWN"));
        Assert.True(result.UnknownSurveyLayers.ContainsKey("XX-UNKNOWN"));
        Assert.True(result.UnknownConsultantLayers.ContainsKey("YY-UNKNOWN"));
        Assert.Contains(result.Issues, i => i.Code == "UNKNOWN-LAYER");
    }

    [Fact]
    public void IgnoredLayersAreExcluded()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", "DEFPOINTS", 0, 0, 10, 0));
        var c = CF.Consultant();
        Assert.Equal(MatchStatus.Excluded, CF.R(CF.Run(s, c), "S1").Status);
    }

    [Theory]
    [InlineData("*CURB*", "E-CURB-BACK", true)]
    [InlineData("V-UTIL-STRM-?B", "V-UTIL-STRM-CB", true)]
    [InlineData("*-SD-*", "E-SDWK", false)]
    [InlineData("*-SD-*", "E-SD-PIPE", true)]
    [InlineData("curb", "CURB", true)]
    public void WildcardsAreAnchoredAndCaseInsensitive(string pattern, string text, bool expected)
    {
        Assert.Equal(expected, Wildcard.Matches(pattern, text));
    }

    [Fact]
    public void DefaultProfileIsValidAndRoundTripsThroughJson()
    {
        var p = CompareProfile.CreateDefault();
        var problems = new List<string>();
        p.Validate(problems);
        Assert.Empty(problems);
        Assert.True(p.Categories.Count >= 40);

        var copy = CompareProfile.FromJson(p.ToJson());
        Assert.Equal(p.Categories.Count, copy.Categories.Count);
        Assert.Equal(p.Tolerances.LineOffsetFeet, copy.Tolerances.LineOffsetFeet);
        Assert.Equal("PMX-COMP-NP-UTILITY", copy.NonplotLayers.Utility);
        Assert.Equal(p.ToJson(), copy.ToJson());
    }

    [Fact]
    public void SurveyLayersComeFromTheFieldToFinishRules()
    {
        var p = CompareProfile.CreateDefault();
        p.ApplySurveyDefaultsFrom(_fx.Config);
        Assert.Contains("V-SURF-CURB-E", p.Category("CURB")!.Survey.Layers);
        Assert.Contains("V-SURF-FENC-CHNL-E", p.Category("FENCE")!.Survey.Layers);
        Assert.Contains("V-UTIL-STRM-E", p.Category("STORM-PIPE")!.Survey.Layers);

        // A plain polyline on the office layer, no figure name: identified by layer.
        var f = CF.Line("1", "V-SURF-FENC-CHNL-E", 0, 0, 1, 0);
        Assert.Equal("FENCE", new FeatureClassifier(p).Classify(f).Category);
    }

    [Fact]
    public void ProfilesResolveFromTheUserFolderAndMissingNamesFail()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ftf-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var previous = FtfSettings.ProfileDirectoryOverride;
        FtfSettings.ProfileDirectoryOverride = dir;
        try
        {
            var builtIn = CompareProfile.Resolve(null, null, null, _fx.Config);
            Assert.Equal("BuiltIn", builtIn.Level);
            Assert.Contains("V-SURF-CURB-E", builtIn.Profile.Category("CURB")!.Survey.Layers);

            var custom = CompareProfile.CreateDefault();
            custom.Name = "Acme Consulting";
            custom.Tolerances.LineOffsetFeet = 1.5;
            custom.Save(CompareProfile.ProfilePath("Acme Consulting")!);
            var user = CompareProfile.Resolve("Acme Consulting", null, null, null);
            Assert.Equal("User", user.Level);
            Assert.Equal(1.5, user.Profile.Tolerances.LineOffsetFeet);
            Assert.Contains("Acme Consulting", CompareProfile.ListProfiles(null));

            Assert.Throws<ConfigException>(() => CompareProfile.Resolve("Nobody", null, null, null));

            // A per-job file beside the drawing wins over everything.
            var job = Path.Combine(dir, "job");
            Directory.CreateDirectory(job);
            custom.Tolerances.LineOffsetFeet = 2.5;
            custom.Save(Path.Combine(job, CompareProfile.BesideDrawingFileName));
            Assert.Equal("BesideDrawing", CompareProfile.Resolve("Acme Consulting", job, null, null).Level);
        }
        finally
        {
            FtfSettings.ProfileDirectoryOverride = previous;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void InvalidProfileIsNeverSaved()
    {
        var p = CompareProfile.CreateDefault();
        p.Tolerances.LineOffsetFeet = -1;
        var path = Path.Combine(Path.GetTempPath(), "ftf-" + Path.GetRandomFileName() + ".json");
        Assert.Throws<ConfigException>(() => p.Save(path));
        Assert.False(File.Exists(path));
    }

    // ------------------------------------------------------- xrefs, units, transforms

    [Fact]
    public void UnresolvedXrefPreventsMissingVerdicts()
    {
        var s = CF.Survey();
        s.Add(CF.Line("S1", "V-SURF-CURB-E", 0, 0, 100, 0));
        s.Add(CF.Cogo("S2", "HH 1", 500, 500, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        c.AddIssue(Severity.Error, "XREF-UNRESOLVED", "UTIL-BASE.dwg could not be found", true);
        var result = CF.Run(s, c);
        Assert.True(result.ConsultantIncomplete);
        Assert.Equal(MatchStatus.Matched, CF.R(result, "S1").Status);
        var hh = CF.R(result, "S2");
        Assert.Equal(MatchStatus.NotEvaluated, hh.Status);
        Assert.Contains(hh.Notes, n => n.Contains("incomplete"));
        Assert.Equal(0, result.Summary().Missing);
    }

    [Fact]
    public void RotatedAndTranslatedXrefGeometryIsComparedInSurveyCoordinates()
    {
        // The consultant xref is inserted at (1000, 2000), rotated 90 degrees: its curb
        // drawn from (0,0) to (100,0) in the xref lands from (1000,2000) to (1000,2100).
        var insert = Affine2.Insertion(1000, 2000, Math.PI / 2, 1.0);
        var curbInXref = CF.Line("C1", "E-CURB", 0, 0, 100, 0);
        curbInXref.Vertices = curbInXref.Vertices.Select(insert.Apply).ToList();
        curbInXref.OwnerPath = "BASE";
        var c = CF.Consultant(); c.Add(curbInXref);
        var s = CF.Survey(); s.Add(CF.Line("S1", "V-SURF-CURB-E", 1000, 2000, 1000, 2100));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Matched, r.Status);
        Assert.Equal("BASE|C1", r.MatchedHandles.Single());
    }

    [Fact]
    public void NestedTransformsCompose()
    {
        var outer = Affine2.Insertion(10, 0, Math.PI / 2, 2.0);         // rotate 90, scale 2, move 10
        var inner = Affine2.Insertion(1, 1, 0, 1.0);                    // move 1,1
        var composed = outer.Then(inner);
        var p = new CPoint(1, 0, 5);
        var expected = outer.Apply(inner.Apply(p));
        var actual = composed.Apply(p);
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Z, actual.Z, 9);
        Assert.Equal(2.0, composed.Scale, 9);
        Assert.Equal(90.0, composed.RotationDegrees, 6);
        Assert.True(composed.IsUniform);
        Assert.True(Affine2.Identity.IsIdentity);
    }

    [Fact]
    public void MixedDrawingUnitsAreRefusedUntilConfirmed()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", "V-SURF-CURB-E", 0, 0, 100, 0));
        var c = CF.Consultant(); c.UnitsPerFoot = 0.3048;
        c.Add(CF.Line("C1", "E-CURB", 0, 0, 30.48, 0));
        var ex = Assert.Throws<ConfigException>(() => CF.Run(s, c));
        Assert.Contains("consultantUnitsPerFoot", ex.Message);
    }

    [Fact]
    public void CompareSettingsSectionIsWiredIntoFtfSettings()
    {
        var settings = new FtfSettings();
        Assert.Contains(settings.Sections, sec => sec is CompareSettings);
        Assert.Equal("Default", settings.Compare.ActiveProfile);
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings);
        Assert.Contains("\"compare\"", json);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<FtfSettings>(json)!;
        back.FillMissingSections();
        Assert.Equal("-ACC", back.Compare.DeliverySuffix);
        var problems = new List<string>();
        back.Compare.HighlightSurveyColor = 0;
        back.Compare.Validate(problems);
        Assert.Single(problems);
    }
}
