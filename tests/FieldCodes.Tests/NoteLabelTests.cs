using FieldCodes;
using FieldCodes.Editing;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// Labels built from a crew's field notes: a pole shot "PP 1234 PSE XFMR TEL CATV" or a
/// sign shot "SN 135 NO PARKING". The words are read through the rule's own vocabulary
/// into groups, and the label format reads the groups in a fixed order -- so the order the
/// crew typed the notes in never changes the label, and nothing they wrote is lost.
/// </summary>
public sealed class NoteLabelTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public NoteLabelTests(RulesFixture fx) => _fx = fx;

    // ----------------------------------------------------------------- the shipped pole

    [Fact]
    public void APoleWithNotesReadsNumberOwnerThenWhatIsOnIt()
    {
        var p = _fx.Parse("PP 1234 PSE XFMR TEL CATV");

        Assert.False(p.HasErrors);
        Assert.False(p.HasWarnings);
        Assert.Equal("pole", p.RuleId);
        Assert.Equal(new[] { "POLE 1234 PSE", "W/ XFMR, TEL, CATV" }, p.LabelLines);
        Assert.Equal("POLE 1234 PSE\\PW/ XFMR, TEL, CATV", p.LabelText);
        Assert.Equal("POLE 1234 PSE / W/ XFMR, TEL, CATV", p.LabelDisplay);
        Assert.Equal("V-UTIL-POWR-TEXT", p.LabelLayer);
    }

    [Theory]
    [InlineData("PP CATV PSE TEL 1234 XFMR")]
    [InlineData("PP 1234 TEL CATV XFMR PSE")]
    [InlineData("PP #1234 PSE TRANS PHONE CABLE")]
    [InlineData("PP 1234 PSE TRANSFORMER TELCO TV")]
    public void TheOrderAndSpellingOfTheNotesDoNotChangeTheLabel(string description)
    {
        var p = _fx.Parse(description);
        Assert.False(p.HasErrors);
        Assert.Equal("POLE 1234 PSE\\PW/ XFMR, TEL, CATV", p.LabelText);
    }

    [Fact]
    public void ANumberedPoleWithNoNotesIsStillOneLine()
    {
        var p = _fx.Parse("PP 1234");
        Assert.Equal("POLE 1234", p.LabelText);
        Assert.Equal(new[] { "POLE 1234" }, p.LabelLines);
    }

    [Fact]
    public void AModifierStillComposesAndItsSuffixSitsOnTheFirstLine()
    {
        var p = _fx.Parse("PP 1234 XFMR DEAD");
        Assert.False(p.HasErrors);
        Assert.Equal(new[] { "POLE 1234 (DEAD)", "W/ XFMR" }, p.LabelLines);
        Assert.Equal("V-UTIL-POWR-TEXT-DEAD", p.LabelLayer);
        Assert.Contains(p.Modifiers, m => m.Id == "dead");
    }

    [Fact]
    public void HeightAndMaterialAreReadAndANumberIsNotMistakenForAHeight()
    {
        var p = _fx.Parse("PP 45FT WD 1234 SL");
        Assert.False(p.HasErrors);
        Assert.Equal(new[] { "POLE 1234 WOOD 45'", "W/ LUMINAIRE" }, p.LabelLines);
    }

    [Fact]
    public void APoleWithNotesButNoNumberIsLabelledFromTheNotes()
    {
        var p = _fx.Parse("PP PSE GUY");
        Assert.False(p.HasErrors);
        Assert.Equal(new[] { "POLE PSE", "W/ GUY" }, p.LabelLines);
    }

    [Fact]
    public void ABarePoleStillDrawsOnlyItsSymbolLabel()
    {
        var p = _fx.Parse("PP");
        Assert.Equal("symbol-label", p.RuleId);
        Assert.Equal("PP", p.LabelText);
    }

    [Fact]
    public void AWordTheVocabularyDoesNotKnowIsLabelledAsWrittenAndFlagged()
    {
        var p = _fx.Parse("PP 1234 XFMR LEANING");

        Assert.False(p.HasErrors);
        Assert.True(p.HasWarnings);
        Assert.Equal(new[] { "POLE 1234", "W/ XFMR", "LEANING" }, p.LabelLines);
        var d = Assert.Single(p.Diagnostics, x => x.Code == "NOTE");
        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Contains("LEANING", d.Message);
    }

    [Fact]
    public void ATermMustMatchWholeWords()
    {
        // TELCO is a term, but TELCOM is not: it is not read as TEL plus leftovers.
        var p = _fx.Parse("PP 1234 TELCOM");
        Assert.Equal(new[] { "POLE 1234", "TELCOM" }, p.LabelLines);
    }

    // ----------------------------------------------------------------- the shipped sign

    [Fact]
    public void ABareSignIsStillJustSign()
    {
        var p = _fx.Parse("SN 135");
        Assert.Equal("SIGN", p.LabelText);
        Assert.Equal(315.0, p.RotationDegrees!.Value, 9);
    }

    [Theory]
    [InlineData("SN 135 STOP", "STOP SIGN")]
    [InlineData("SN 135 NP", "NO PARKING SIGN")]
    [InlineData("SN 90 NO PARKING ANYTIME", "NO PARKING ANYTIME SIGN")]
    [InlineData("SN 90 SPEED 25", "SPEED LIMIT 25 SIGN")]
    [InlineData("SN 90 SPD 25 MPH WP", "SPEED LIMIT 25 SIGN (WOOD POST)")]
    [InlineData("SN 270 DEAD END", "DEAD END SIGN")]
    [InlineData("SN 270 STOP MP", "STOP SIGN (METAL POST)")]
    public void SignWordingKeepsItsOrderAndShorthandIsExpanded(string description, string label)
    {
        var p = _fx.Parse(description);
        Assert.False(p.HasErrors);
        Assert.False(p.HasWarnings);
        Assert.Equal(label, p.LabelText);
    }

    [Fact]
    public void DeadEndOnASignIsWordingNotTheDeadModifier()
    {
        var p = _fx.Parse("SN 270 DEAD END");
        Assert.Empty(p.Modifiers);
        Assert.Equal("V-SIGN-TEXT", p.LabelLayer);
    }

    // ----------------------------------------------------------------- policies (synthetic rules)

    private static FieldCodeParser ParserFor(string notesJson, string format)
    {
        var json = @"{
  ""codes"": [ {
    ""id"": ""thing"",
    ""match"": ""^(?<code>TH)(?<mods>(?:\\s+\\S+)+)\\s*$"",
    ""notes"": " + notesJson + @",
    ""label"": { ""format"": " + JsonConvert.ToString(format) + @", ""layer"": ""TXT"" }
  } ],
  ""modifiers"": [ { ""id"": ""dead"", ""pattern"": ""^DEAD$"", ""priority"": 20, ""labelSuffix"": "" (DEAD)"" } ]
}";
        var cfg = JsonConvert.DeserializeObject<RulesConfig>(json)!;
        cfg.Compile();
        return new FieldCodeParser(cfg);
    }

    [Fact]
    public void RefuseMakesAnUnknownWordAnErrorAsAModifierWouldBe()
    {
        var parser = ParserFor(@"{ ""unknown"": ""Refuse"", ""terms"": [ { ""match"": ""RED"", ""group"": ""colour"" } ] }", "THING[ {colour}]");
        var p = parser.Parse("1", "TH RED BLUE");
        Assert.True(p.HasErrors);
        Assert.Contains(p.Diagnostics, d => d.Code == "NOTE" && d.Severity == Severity.Error && d.Message.Contains("BLUE"));
    }

    [Fact]
    public void KeepLabelsAnUnknownWordWithOnlyAnInfoNote()
    {
        var parser = ParserFor(@"{ ""unknown"": ""Keep"", ""terms"": [] }", "THING[ {other}]");
        var p = parser.Parse("1", "TH BLUE");
        Assert.False(p.HasWarnings);
        Assert.Equal("THING BLUE", p.LabelText);
        Assert.Contains(p.Diagnostics, d => d.Code == "NOTE" && d.Severity == Severity.Info);
    }

    [Fact]
    public void AVocabularyGroupReadsInTermOrderAndAnAsShotGroupInWrittenOrder()
    {
        var notes = @"{ ""unknown"": ""Keep"", ""groups"": { ""said"": { ""order"": ""AsShot"", ""joiner"": "" "" } },
          ""terms"": [ { ""match"": ""A"", ""group"": ""ranked"" }, { ""match"": ""B"", ""group"": ""ranked"" },
                       { ""match"": ""X"", ""group"": ""said"" }, { ""match"": ""Y"", ""group"": ""said"" } ] }";
        var parser = ParserFor(notes, "{ranked}\\P{said}");
        var p = parser.Parse("1", "TH Y B X A");
        Assert.Equal(new[] { "A, B", "Y X" }, p.LabelLines);
    }

    [Fact]
    public void ATermCanSpanWordsAndCaptureAValueForItsWording()
    {
        var notes = @"{ ""terms"": [ { ""match"": ""SIZE\\s+(?<n>[0-9]+)\\s+IN"", ""text"": ""{n}\"" PIPE"", ""group"": ""size"" } ] }";
        var parser = ParserFor(notes, "THING[ {size}]");
        var p = parser.Parse("1", "TH SIZE 12 IN");
        Assert.False(p.HasErrors);
        Assert.Equal("THING 12\" PIPE", p.LabelText);
    }

    [Fact]
    public void RepeatedWordsAreLabelledOnce()
    {
        var parser = ParserFor(@"{ ""terms"": [ { ""match"": ""TEL"", ""group"": ""attach"" } ] }", "THING[ W/ {attach}]");
        Assert.Equal("THING W/ TEL", parser.Parse("1", "TH TEL TEL").LabelText);
    }

    [Fact]
    public void TheNotesPlaceholderIsEveryGroupInOrder()
    {
        var notes = @"{ ""unknown"": ""Keep"", ""terms"": [ { ""match"": ""A"", ""group"": ""first"" }, { ""match"": ""B"", ""group"": ""second"" } ] }";
        var parser = ParserFor(notes, "THING[ {notes}]");
        Assert.Equal("THING A B ZED", parser.Parse("1", "TH ZED B A").LabelText);
    }

    // ----------------------------------------------------------------- the label template

    [Theory]
    [InlineData("POLE[ {number}]", "POLE")]
    [InlineData("POLE[ #{number}][ ({owner})]", "POLE (PSE)")]
    [InlineData("[[{owner}]]", "[PSE]")]
    [InlineData("A\\P\\P[{number}]\\PB", "A|B")]
    [InlineData("A\nB", "A|B")]
    public void OptionalPartsVanishWithoutAValueAndEmptyLinesAreDropped(string template, string expected)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["owner"] = "PSE" };
        Assert.Equal(expected, string.Join("|", FieldCodeParser.ComposeLines(template, fields)));
    }

    [Fact]
    public void AnOrdinaryOneLineFormatExpandsExactlyAsBefore()
    {
        // No brackets, no line breaks: the old expansion, spaces and all.
        var p = _fx.Parse("WFH 12");
        Assert.Equal("HYDRANT 12", p.LabelText);
        Assert.Equal(new[] { "HYDRANT 12" }, p.LabelLines);
    }

    // ----------------------------------------------------------------- rules file checks

    [Fact]
    public void ABadTermIsRefusedWhenTheRulesLoad()
    {
        var json = @"{ ""codes"": [ { ""id"": ""t"", ""match"": ""^(?<code>T)(?<mods>(?:\\s+\\S+)*)$"",
          ""notes"": { ""terms"": [ { ""match"": ""(unclosed"", ""group"": ""g"" }, { ""match"": ""X?"", ""group"": ""g"" }, { ""match"": ""Y"" } ] } } ] }";
        var cfg = JsonConvert.DeserializeObject<RulesConfig>(json)!;
        var ex = Assert.Throws<ConfigException>(() => cfg.Compile());
        Assert.Contains("invalid regex", ex.Message);
        Assert.Contains("matches nothing at all", ex.Message);
        Assert.Contains("names no 'group'", ex.Message);
    }

    [Fact]
    public void TheEditorAcceptsNoteGroupsInTheLabelAndChecksTermWording()
    {
        var good = RuleDocument.FromText(@"{ ""codes"": [ { ""id"": ""t"", ""match"": ""^(?<code>T)(?<mods>(?:\\s+\\S+)+)$"",
          ""notes"": { ""terms"": [ { ""match"": ""S(?<n>[0-9]+)"", ""text"": ""SIZE {n}"", ""group"": ""size"" } ] },
          ""label"": { ""format"": ""T[ {size}][ {other}][ {notes}]"", ""layer"": ""TXT"" } } ] }");
        Assert.Empty(good.Validate());

        var bad = RuleDocument.FromText(@"{ ""codes"": [ { ""id"": ""t"", ""match"": ""^(?<code>T)(?<x>(?:\\s+\\S+)+)$"",
          ""notes"": { ""terms"": [ { ""match"": ""S(?<n>[0-9]+)"", ""text"": ""SIZE {m}"", ""group"": ""size"" } ] },
          ""label"": { ""format"": ""T {size} {colour}"", ""layer"": ""TXT"" } } ] }");
        var problems = bad.Validate();
        Assert.Contains(problems, p => p.Contains("{colour}"));
        Assert.Contains(problems, p => p.Contains("{m}"));
        Assert.Contains(problems, p => p.Contains("no (?<mods>...) group"));
    }

    [Fact]
    public void TheShippedRulesStillPassTheEditorChecks()
    {
        var doc = RuleDocument.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        Assert.Empty(doc.Validate());
    }
}
