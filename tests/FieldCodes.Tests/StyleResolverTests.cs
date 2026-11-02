using FieldCodes;
using FieldCodes.Annotation;
using FieldCodes.Settings;
using FieldCodes.Styles;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// Annotation styles: with nothing defined the labels look exactly as before; a family's
/// style and a code's style combine field by field, most specific last; a style names only
/// what it changes; missing styles are reported, never swapped silently; and the label text
/// takes the style's prefix, suffix, rounding and width.
/// </summary>
public sealed class StyleResolverTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public StyleResolverTests(RulesFixture fx) => _fx = fx;

    private static StyleSubject Hydrant(StyledKind kind = StyledKind.Label) => new()
    {
        Kind = kind,
        Category = AnnotationCategory.Utilities,
        RuleId = "fire-hydrant",
        Code = "FH",
        BaseLayer = "V-UTIL-TEXT",
        BaseLeader = LeaderMode.Auto
    };

    [Fact]
    public void WithNoStylesTheExistingSettingsDecide()
    {
        var s = new FtfSettings();
        s.Labels.TextStyle = "L80";
        s.Labels.TextHeightPlotted = 0.09;
        s.Labels.DrawMask = true;
        s.Labels.LeaderArrowSizePlotted = 0.05;

        var r = StyleResolver.Resolve(s, Hydrant());

        Assert.Equal("L80", r.Text.DrawingTextStyle);
        Assert.Equal(0.09, r.Text.HeightPlotted);
        Assert.Equal(MaskMode.Wipeout, r.Text.Mask);
        Assert.Equal(1.0, r.Text.WidthFactor);
        Assert.Equal("V-UTIL-TEXT", r.Layer);
        Assert.Equal(LeaderMode.Auto, r.LeaderMode);
        Assert.True(r.Leader.SizeArrow);
        Assert.Equal(0.05, r.Leader.ArrowSizePlotted);
        Assert.Equal(string.Empty, r.Leader.MLeaderStyle);
        Assert.Null(r.Leader.LineType);
        Assert.True(r.Enabled);
        Assert.Empty(r.Problems);
        Assert.Equal("Point Labels", r.OriginOf("Text height"));
        Assert.Equal("the multileader style", r.OriginOf("Leader line"));
    }

    [Fact]
    public void TagsStartFromTheTagSettings()
    {
        var s = new FtfSettings();
        s.Tags.TagTextStyle = "TAGS";
        s.Tags.TagTextHeightPlotted = 0.12;
        s.Tags.TagLayer = "V-TREE-TAG";
        s.Tags.TagLeader = false;

        var r = StyleResolver.Resolve(s, Hydrant(StyledKind.Tag));

        Assert.Equal("TAGS", r.Text.DrawingTextStyle);
        Assert.Equal(0.12, r.Text.HeightPlotted);
        Assert.Equal(MaskMode.None, r.Text.Mask);
        Assert.Equal("V-TREE-TAG", r.Layer);
        Assert.Equal(LeaderMode.Never, r.LeaderMode);
    }

    [Fact]
    public void AFamilyStyleAndACodeStyleCombineFieldByField()
    {
        var s = new FtfSettings();
        s.Styles.Text.Add(new TextStyleDef { Name = "Utility", HeightPlotted = 0.10, Color = "4" });
        s.Styles.Text.Add(new TextStyleDef { Name = "Hydrant", Prefix = "FH ", Mask = MaskMode.Background });
        s.Styles.Leaders.Add(new LeaderStyleDef { Name = "Spline dot", LineType = LeaderLineKind.Spline, ArrowBlock = "_Dot", ArrowSizePlotted = 0.04 });
        s.Styles.Assignments.Add(new StyleAssignment { Kind = StyledKind.Label, Category = "Utilities", TextStyle = "Utility", LeaderStyle = "Spline dot", Layer = "V-UTIL-LABEL" });
        s.Styles.Assignments.Add(new StyleAssignment { Kind = StyledKind.Label, Code = "FH", TextStyle = "Hydrant", Leader = LeaderMode.Always });

        var r = StyleResolver.Resolve(s, Hydrant());

        // The code names its own text style, so its fields win -- but the family's leader,
        // layer and the Point Labels height still apply where the code's style says nothing.
        Assert.Equal("Hydrant", r.Text.Name);
        Assert.Equal("FH ", r.Text.Prefix);
        Assert.Equal(MaskMode.Background, r.Text.Mask);
        Assert.Equal(s.Labels.TextHeightPlotted, r.Text.HeightPlotted);
        Assert.Equal("Spline dot", r.Leader.Name);
        Assert.Equal(LeaderLineKind.Spline, r.Leader.LineType);
        Assert.Equal("_Dot", r.Leader.ArrowBlock);
        Assert.Equal(0.04, r.Leader.ArrowSizePlotted);
        Assert.Equal("V-UTIL-LABEL", r.Layer);
        Assert.Equal(LeaderMode.Always, r.LeaderMode);
        Assert.StartsWith("assignment for code FH", r.OriginOf("Leader"));
        Assert.Contains("family Utilities", r.OriginOf("Layer"));
    }

    [Fact]
    public void AssignmentsForOtherFamiliesKindsAndCodesDoNotApply()
    {
        var s = new FtfSettings();
        s.Styles.Text.Add(new TextStyleDef { Name = "Big", HeightPlotted = 0.2 });
        s.Styles.Assignments.Add(new StyleAssignment { Kind = StyledKind.Label, Category = "Trees", TextStyle = "Big" });
        s.Styles.Assignments.Add(new StyleAssignment { Kind = StyledKind.Tag, Category = "Utilities", TextStyle = "Big" });
        s.Styles.Assignments.Add(new StyleAssignment { Kind = StyledKind.Label, Code = "WV", TextStyle = "Big" });

        var r = StyleResolver.Resolve(s, Hydrant());
        Assert.Equal(s.Labels.TextHeightPlotted, r.Text.HeightPlotted);
    }

    [Fact]
    public void AMissingNamedStyleIsReportedNotSwapped()
    {
        var s = new FtfSettings();
        s.Styles.Assignments.Add(new StyleAssignment { Kind = StyledKind.Label, Code = "FH", TextStyle = "Nope", LeaderStyle = "Gone" });

        var r = StyleResolver.Resolve(s, Hydrant());

        Assert.Equal(2, r.Problems.Count);
        Assert.Contains(r.Problems, p => p.Contains("\"Nope\"") && p.Contains("not defined"));
        Assert.Contains(r.Problems, p => p.Contains("\"Gone\""));
        var problems = new List<string>();
        s.Styles.Validate(problems);
        Assert.Contains(problems, p => p.Contains("\"Nope\""));
    }

    [Fact]
    public void ATurnedOffFeatureIsNotLabelled()
    {
        var s = new FtfSettings();
        s.Styles.Assignments.Add(new StyleAssignment { Kind = StyledKind.Label, Rule = "fire-hydrant", Enabled = false });
        Assert.False(StyleResolver.Resolve(s, Hydrant()).Enabled);
        Assert.True(StyleResolver.Resolve(s, Hydrant(StyledKind.Tag)).Enabled);
    }

    [Fact]
    public void ARealPointResolvesThroughItsFamily()
    {
        var s = new FtfSettings();
        s.Styles.Text.Add(new TextStyleDef { Name = "Tree text", HeightPlotted = 0.07 });
        s.Styles.Assignments.Add(new StyleAssignment { Kind = StyledKind.Label, Category = "Trees", TextStyle = "Tree text" });

        var tree = _fx.Parse("CON 18 . 25");
        var r = StyleResolver.Resolve(s, StyleSubject.For(StyledKind.Label, tree, _fx.Config));
        Assert.Equal(0.07, r.Text.HeightPlotted);

        var pole = _fx.Parse("PP 1234");
        Assert.Equal(s.Labels.TextHeightPlotted, StyleResolver.Resolve(s, StyleSubject.For(StyledKind.Label, pole, _fx.Config)).Text.HeightPlotted);
    }

    [Fact]
    public void FamiliesAreReadByTheirShownNamesToo()
    {
        Assert.Equal(AnnotationCategory.StormSewer, StyleResolver.TryCategory("Storm and sewer structures (dips)"));
        Assert.Equal(AnnotationCategory.OtherPoints, StyleResolver.TryCategory("other points"));
        Assert.Equal(AnnotationCategory.Trees, StyleResolver.TryCategory("trees"));
        Assert.Null(StyleResolver.TryCategory("Bridges"));
    }

    [Fact]
    public void TheSettingsRoundTripWithoutWritingEmptyFields()
    {
        var s = new FtfSettings();
        s.Styles.Text.Add(new TextStyleDef { Name = "Tree text", HeightPlotted = 0.07 });
        s.Styles.Leaders.Add(new LeaderStyleDef { Name = "Dot", ArrowBlock = "_Dot", Attachment = LeaderAttachment.UnderlineBottomLine });
        s.Styles.Assignments.Add(new StyleAssignment { Kind = StyledKind.Tag, Category = "Trees", TextStyle = "Tree text" });

        var json = JsonConvert.SerializeObject(s.Styles);
        Assert.DoesNotContain("widthFactor", json);
        Assert.Contains("\"UnderlineBottomLine\"", json);
        Assert.Contains("\"Tag\"", json);

        var back = JsonConvert.DeserializeObject<StyleSettings>(json)!;
        Assert.Single(back.Text);
        Assert.Equal(0.07, back.Text[0].HeightPlotted);
        Assert.Null(back.Text[0].WidthFactor);
        Assert.Equal(LeaderAttachment.UnderlineBottomLine, back.Leaders[0].Attachment);
        Assert.Equal(StyledKind.Tag, back.Assignments[0].Kind);
    }

    [Fact]
    public void ValidationCatchesBadValues()
    {
        var s = new StyleSettings();
        s.Text.Add(new TextStyleDef { Name = "A", HeightPlotted = 0, WidthFactor = 0, Color = "purple-ish", Decimals = 12 });
        s.Text.Add(new TextStyleDef { Name = "a" });
        s.Leaders.Add(new LeaderStyleDef { Name = "L", Lineweight = "thick", ArrowSizePlotted = -1 });
        s.Assignments.Add(new StyleAssignment { Kind = StyledKind.LineLabel, Category = "Bridges", LeaderStyle = "L", Layer = "BAD:LAYER" });

        var problems = new List<string>();
        s.Validate(problems);

        Assert.Contains(problems, p => p.Contains("Two text styles"));
        Assert.Contains(problems, p => p.Contains("height"));
        Assert.Contains(problems, p => p.Contains("width factor"));
        Assert.Contains(problems, p => p.Contains("purple-ish"));
        Assert.Contains(problems, p => p.Contains("decimal places"));
        Assert.Contains(problems, p => p.Contains("thick"));
        Assert.Contains(problems, p => p.Contains("arrow size"));
        Assert.Contains(problems, p => p.Contains("Bridges"));
        Assert.Contains(problems, p => p.Contains("no leaders"));
        Assert.Contains(problems, p => p.Contains("BAD:LAYER"));
    }

    [Fact]
    public void AssignmentForFindsOrAddsExactlyOneScope()
    {
        var s = new StyleSettings();
        var a = s.AssignmentFor(StyledKind.Label, null, null, "FH");
        a.TextStyle = "X";
        Assert.Same(a, s.AssignmentFor(StyledKind.Label, "", "", "fh"));
        Assert.NotSame(a, s.AssignmentFor(StyledKind.Tag, null, null, "FH"));
        s.Prune();
        Assert.Single(s.Assignments);
    }

    [Theory]
    [InlineData(0.0, 0.06, 0.06, 4, 4)]
    [InlineData(0.30, 0.06, 0.06, 4, 4)]
    [InlineData(0.18, 0.06, 0.06, 4, 3)]
    [InlineData(0.05, 0.06, 0.06, 4, 1)]
    public void MaximumLeaderLengthLimitsTheSearchRings(double max, double offset, double step, int rings, int expected)
    {
        Assert.Equal(expected, StyleResolver.RingsWithin(max, offset, step, rings));
    }
}

public sealed class StyleTextTests
{
    private static ResolvedTextStyle Style(Action<ResolvedTextStyle> set)
    {
        var s = new ResolvedTextStyle();
        set(s);
        return s;
    }

    [Fact]
    public void NothingSetLeavesTheTextAlone()
    {
        Assert.Equal("18\" FIR\\PW/ TEL", StyleText.Compose("18\" FIR\\PW/ TEL", new ResolvedTextStyle()));
    }

    [Fact]
    public void PrefixAndSuffixWrapTheText()
    {
        Assert.Equal("FH 12'", StyleText.Compose("12", Style(s => { s.Prefix = "FH "; s.Suffix = "'"; })));
    }

    [Fact]
    public void DecimalsRoundMeasurementsButNeverWholeNumbers()
    {
        Assert.Equal("PP 1234\\PMD 6.42", StyleText.RoundNumbers("PP 1234\\PMD 6.418", 2));
        Assert.Equal("6.40 TC", StyleText.RoundNumbers("6.4 TC", 2));
        Assert.Equal("EL 101", StyleText.RoundNumbers("EL 100.5", 0));
        Assert.Equal("V1.2.3", StyleText.RoundNumbers("V1.2.3", 0));
        Assert.Equal("1.2.3", StyleText.RoundNumbers("1.2.3", 0));
    }

    [Fact]
    public void UpperCaseKeepsFormattingCodes()
    {
        Assert.Equal("{\\fArial|b0;POLE}\\PW/ TEL", StyleText.UpperCase("{\\fArial|b0;pole}\\Pw/ tel"));
    }

    [Fact]
    public void WidthAndObliqueRideOnEveryLine()
    {
        var text = StyleText.Compose("A\\PB", Style(s => { s.WidthFactor = 0.8; s.ObliqueDegrees = 15; }));
        Assert.Equal("{\\W0.8;\\Q15;A\\P\\W0.8;\\Q15;B}", text);
        Assert.Equal("A\nB", StyleText.Plain(text));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(135, -45)]
    [InlineData(200, 20)]
    [InlineData(270, 90)]
    [InlineData(-90, 90)]
    [InlineData(300, -60)]
    public void FollowingTheSymbolNeverReadsUpsideDown(double symbol, double expected)
    {
        var style = Style(s => s.Rotation = TextRotation.FollowSymbol);
        Assert.Equal(expected, StyleText.AngleDegrees(style, symbol), 6);
    }

    [Fact]
    public void FixedAndHorizontalRotation()
    {
        Assert.Equal(0, StyleText.AngleDegrees(new ResolvedTextStyle(), 77));
        Assert.Equal(30, StyleText.AngleDegrees(Style(s => { s.Rotation = TextRotation.Fixed; s.RotationDegrees = 30; }), null));
        Assert.Equal(0, StyleText.AngleDegrees(Style(s => s.Rotation = TextRotation.FollowSymbol), null));
    }
}

public sealed class StyleCheckTests
{
    private sealed class Drawing : IStyleResources
    {
        public HashSet<string> Text = new(StringComparer.OrdinalIgnoreCase) { "Standard", "L80" };
        public HashSet<string> Leaders = new(StringComparer.OrdinalIgnoreCase) { "Standard" };
        public HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase) { "ARROWX" };
        public HashSet<string> Linetypes = new(StringComparer.OrdinalIgnoreCase) { "DASHED" };
        public bool HasTextStyle(string n) => Text.Contains(n);
        public bool HasMLeaderStyle(string n) => Leaders.Contains(n);
        public bool HasBlock(string n) => Blocks.Contains(n);
        public bool HasLinetype(string n) => Linetypes.Contains(n);
    }

    [Fact]
    public void EverythingNamedMustExist()
    {
        var s = new FtfSettings();
        s.Labels.TextStyle = "L100";
        s.Styles.Text.Add(new TextStyleDef { Name = "Tree", TextStyle = "L80" });
        s.Styles.Text.Add(new TextStyleDef { Name = "Sign", TextStyle = "SIGNS" });
        s.Styles.Leaders.Add(new LeaderStyleDef { Name = "A", MLeaderStyle = "FTF", ArrowBlock = "MYARROW", Linetype = "HIDDEN" });
        s.Styles.Leaders.Add(new LeaderStyleDef { Name = "B", MLeaderStyle = "Standard", ArrowBlock = "_DotSmall", Linetype = "ByLayer" });

        var missing = StyleCheck.Missing(s, new Drawing());

        Assert.Equal(5, missing.Count);
        Assert.Contains(missing, m => m.Contains("Point Labels") && m.Contains("L100"));
        Assert.Contains(missing, m => m.Contains("SIGNS"));
        Assert.Contains(missing, m => m.Contains("multileader style \"FTF\""));
        Assert.Contains(missing, m => m.Contains("MYARROW"));
        Assert.Contains(missing, m => m.Contains("HIDDEN"));
    }

    [Fact]
    public void BuiltInArrowsHaveReadableNames()
    {
        Assert.True(StyleCheck.IsBuiltInArrow("_dotsmall"));
        Assert.Equal("Dot small", StyleCheck.ArrowDisplayName("_DotSmall"));
        Assert.Equal("Closed filled (style default)", StyleCheck.ArrowDisplayName(""));
        Assert.Equal("TICK (block)", StyleCheck.ArrowDisplayName("TICK"));
    }
}
