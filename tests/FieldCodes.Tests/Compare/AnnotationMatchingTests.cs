using FieldCodes.Compare;

namespace FieldCodes.Tests;

/// <summary>Annotation is compared through its feature and the facts it carries.</summary>
public sealed class AnnotationMatchingTests
{
    private static (CompareDataset s, CompareDataset c) MatchedManhole()
    {
        var s = CF.Survey(); s.Add(CF.Cogo("MH", "SDMH 101", 50, 50, 0));
        var c = CF.Consultant(); c.Add(CF.Block("CMH", "E-STRM-MH", "STMH", 50.3, 50.1));
        return (s, c);
    }

    [Fact]
    public void ExistingStructureWithMissingAnnotationKeepsOurLabelsPlotting()
    {
        // Their drawing shows the symbol only; our rim and inverts must still plot.
        var (s, c) = MatchedManhole();
        s.Add(CF.Text("RIM", "V-UTIL-STRM-TEXT", "RIM 120.50", 52, 52));
        s.Add(CF.Text("IEN", "V-UTIL-STRM-TEXT", "IE N 115.20", 52, 51));
        s.Add(CF.Text("PIPE", "V-UTIL-STRM-TEXT", "12\" PVC", 52, 50));
        var result = CF.Run(s, c);
        Assert.Equal(MatchStatus.Matched, CF.R(result, "MH").Status);
        foreach (var h in new[] { "RIM", "IEN", "PIPE" })
        {
            var r = CF.R(result, h);
            Assert.Equal(MatchStatus.Missing, r.Status);
            Assert.Equal(PlannedAction.None, r.Action);
            Assert.Equal("MH", r.AssociatedFeatureId);
        }
    }

    [Fact]
    public void EquivalentConsultantLabelMakesOursADuplicate()
    {
        var (s, c) = MatchedManhole();
        s.Add(CF.Text("RIM", "V-UTIL-STRM-TEXT", "RIM 120.50", 52, 52));
        s.Add(CF.Text("IEN", "V-UTIL-STRM-TEXT", "IE N 115.20", 52, 51));
        c.Add(CF.Text("CRIM", "E-STRM-TXT", "RIM=120.5", 48, 53));
        var result = CF.Run(s, c);
        var rim = CF.R(result, "RIM");
        Assert.Equal(MatchStatus.Matched, rim.Status);
        Assert.Equal(PlannedAction.Nonplot, rim.Action);
        Assert.Equal("PMX-COMP-NP-TEXT", rim.NonplotLayer);
        Assert.Equal(MatchStatus.Missing, CF.R(result, "IEN").Status);   // they have the rim, not the invert
    }

    [Fact]
    public void ConflictingValuesAreFlaggedNotChosen()
    {
        var (s, c) = MatchedManhole();
        s.Add(CF.Text("RIM", "V-UTIL-STRM-TEXT", "RIM 120.50", 52, 52));
        c.Add(CF.Text("CRIM", "E-STRM-TXT", "RIM 121.00", 48, 53));
        var r = CF.R(CF.Run(s, c), "RIM");
        Assert.Equal(MatchStatus.Discrepancy, r.Status);
        Assert.Equal(PlannedAction.None, r.Action);
        Assert.Equal(-0.5, r.VerticalDifference!.Value, 2);
        Assert.Contains(r.Notes, n => n.Contains("RIM=120.5 vs RIM=121"));
    }

    [Fact]
    public void ConsultantAttributesCountAsTheirAnnotation()
    {
        var s = CF.Survey(); s.Add(CF.Cogo("MH", "SDMH 101", 50, 50, 0));
        s.Add(CF.Text("RIM", "V-UTIL-STRM-TEXT", "RIM 120.50", 52, 52));
        var c = CF.Consultant();
        c.Add(CF.Block("CMH", "E-STRM-MH", "STMH", 50.3, 50.1, null, new Dictionary<string, string> { { "RIM", "120.50" }, { "INV_N", "115.2" } }));
        var result = CF.Run(s, c);
        Assert.Equal(MatchStatus.Matched, CF.R(result, "RIM").Status);
    }

    [Fact]
    public void AnnotationOnAMissingFeatureIsMissing()
    {
        var s = CF.Survey();
        s.Add(CF.Cogo("HH", "HH 12", 200, 200, 0));
        s.Add(CF.Text("LBL", "V-UTIL-TELE-TEXT", "HH", 201, 201));
        var c = CF.Consultant();
        var r = CF.R(CF.Run(s, c), "LBL");
        Assert.Equal(MatchStatus.Missing, r.Status);
        Assert.Contains(r.Notes, n => n.Contains("feature is missing"));
    }

    [Fact]
    public void TextOnlyLabelsCompareByWordingWithSynonyms()
    {
        var s = CF.Survey(); s.Add(CF.Line("EC", "V-SURF-CONC-E", 0, 0, 100, 0));
        s.Add(CF.Text("T", "V-SURF-TEXT", "CONC WALK", 50, 2));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-SIDEWALK", 0, 0, 100, 0));
        c.Add(CF.Text("CT", "E-TEXT", "SIDEWALK", 55, -2));
        var r = CF.R(CF.Run(s, c), "T");
        Assert.Equal(MatchStatus.Matched, r.Status);

        var c2 = CF.Consultant(); c2.Add(CF.Line("C1", "E-SIDEWALK", 0, 0, 100, 0));
        c2.Add(CF.Text("CT", "E-TEXT", "GRAVEL", 55, -2));
        Assert.Equal(MatchStatus.NeedsReview, CF.R(CF.Run(s, c2), "T").Status);

        var c3 = CF.Consultant(); c3.Add(CF.Line("C1", "E-SIDEWALK", 0, 0, 100, 0));
        Assert.Equal(MatchStatus.Missing, CF.R(CF.Run(s, c3), "T").Status);
    }

    [Fact]
    public void LabelOverTheUncoveredStretchOfAPartialLineKeepsPlotting()
    {
        var s = CF.Survey(); s.Add(CF.Line("EP", "V-SURF-ASPH-E", 0, 0, 350, 0));
        s.Add(CF.Text("T1", "V-SURF-TEXT", "EDGE OF PAVEMENT", 100, 2));
        s.Add(CF.Text("T2", "V-SURF-TEXT", "EDGE OF PAVEMENT", 300, 2));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-EOP", 0, 0, 200, 0));
        c.Add(CF.Text("CT", "E-TEXT", "EOP", 95, -2));
        var result = CF.Run(s, c);
        Assert.Equal(MatchStatus.Partial, CF.R(result, "EP").Status);
        Assert.Equal(MatchStatus.Matched, CF.R(result, "T1").Status);
        Assert.Equal(MatchStatus.Missing, CF.R(result, "T2").Status);
    }

    [Fact]
    public void AnnotationOfAFeatureUnderReviewWaitsWithIt()
    {
        var s = CF.Survey(); s.Add(CF.Cogo("MH", "SDMH 5", 10, 10, 0));
        s.Add(CF.Text("RIM", "V-UTIL-STRM-TEXT", "RIM 100.00", 11, 11));
        var c = CF.Consultant(); c.Add(CF.Block("C1", "E-STRM-CB", "CB", 10.3, 10));
        var result = CF.Run(s, c);
        Assert.Equal(MatchStatus.NeedsReview, CF.R(result, "MH").Status);
        Assert.Equal(MatchStatus.NeedsReview, CF.R(result, "RIM").Status);
    }

    // ----------------------------------------------------------------- parser

    [Fact]
    public void ParserReadsStructureNotes()
    {
        var facts = AnnotationParser.Parse("SDMH 1234 RIM=120.50 IE N 115.2 IE S 115.1 12\" PVC", new AnnotationRules());
        Assert.Contains(facts, f => f.Key == "ID" && f.Text == "SDMH 1234");
        Assert.Contains(facts, f => f.Key == "RIM" && f.Number == 120.5);
        Assert.Contains(facts, f => f.Key == "IE-N" && f.Number == 115.2);
        Assert.Contains(facts, f => f.Key == "IE-S" && f.Number == 115.1);
        Assert.Contains(facts, f => f.Key == "SIZE" && f.Number == 12);
        Assert.Contains(facts, f => f.Key == "MAT" && f.Text == "PVC");
        Assert.DoesNotContain(facts, f => f.Key == "TEXT");
    }

    [Fact]
    public void ParserStripsMtextFormattingAndReadsElevationsAndDirectionsAfterTheNumber()
    {
        var facts = AnnotationParser.Parse("{\\fArial|b0;RIM 120.50\\PINV 115.20 (N)}", new AnnotationRules());
        Assert.Contains(facts, f => f.Key == "RIM" && f.Number == 120.5);
        Assert.Contains(facts, f => f.Key == "IE-N" && f.Number == 115.2);

        var elev = Assert.Single(AnnotationParser.Parse("123.45", new AnnotationRules()));
        Assert.Equal("ELEV", elev.Key);
        Assert.Equal(123.45, elev.Number);

        var text = Assert.Single(AnnotationParser.Parse("EDGE OF CONC", new AnnotationRules()));
        Assert.Equal("TEXT", text.Key);
        Assert.Equal("EDGE OF CONC", text.Text);
    }

    [Fact]
    public void NormalisedWordingFoldsSynonymsAndPunctuation()
    {
        var rules = new AnnotationRules();
        Assert.True(AnnotationParser.SameText("Conc. Walk", "CONCRETE WALK", rules));
        Assert.True(AnnotationParser.SameText("EOP", "EDGE OF PAVEMENT", rules));
        Assert.False(AnnotationParser.SameText("EOP", "EDGE OF CONCRETE", rules));
    }
}
