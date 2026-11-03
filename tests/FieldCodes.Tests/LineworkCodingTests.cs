using System.Text.RegularExpressions;
using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// Step 1: the linework coding a crew writes, read as Civil 3D reads it -- which figures a shot is
/// on (figure numbers kept), whether it begins, ends or closes each, the commands' parameters, and
/// what is left over. Descriptions follow the forms seen in real field coding; numbers are invented.
/// </summary>
public sealed class LineworkCodingTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public LineworkCodingTests(RulesFixture fx) => _fx = fx;

    private LineworkShot Parse(string description) => LineworkCoding.Parse(description, _fx.Config);

    [Fact]
    public void AFigureKeepsItsNumberAndItsBeginOrEnd()
    {
        var begin = Assert.Single(Parse("FNC1 B").Figures);
        Assert.Equal("FNC1", begin.FigureId);
        Assert.Equal("FNC", begin.Code);
        Assert.Equal("1", begin.Number);
        Assert.True(begin.Begins);
        Assert.False(begin.Ends);

        var end = Assert.Single(Parse("FNC1 E").Figures);
        Assert.True(end.Ends);
        Assert.Equal("", Assert.Single(Parse("FNC B").Figures).Number);
        Assert.NotEqual(Parse("FNC B").Figures[0].FigureId, begin.FigureId);   // FNC and FNC1 are two strings
    }

    [Fact]
    public void AChainedShotIsOnEveryFigureItNames()
    {
        Assert.Equal(new[] { "TOP", "TOP1" }, Parse("TOP B TOP1 B").Figures.Select(f => f.FigureId));
        Assert.All(Parse("TOP B TOP1 B").Figures, f => Assert.True(f.Begins));

        var three = Parse("BLD B EC B BLD1 B").Figures;
        Assert.Equal(new[] { "BLD", "EC", "BLD1" }, three.Select(f => f.FigureId));
        Assert.All(three, f => Assert.True(f.Begins));

        // EC is a feature code (edge of concrete) in this code set, never "end curve".
        var ramp = Parse("EC RMP B").Figures;
        Assert.Equal(new[] { "EC", "RMP" }, ramp.Select(f => f.FigureId));
        Assert.False(ramp[0].Begins);
        Assert.True(ramp[1].Begins);

        Assert.Equal(new[] { "TOE1", "FNC" }, Parse("TOE1 FNC").Figures.Select(f => f.FigureId));
    }

    [Fact]
    public void CurveCommandsAreRead()
    {
        var ep = Assert.Single(Parse("EP1 T E").Figures);
        Assert.Equal(new[] { LineworkCommandKind.CurveEnd, LineworkCommandKind.End }, ep.Commands.Select(c => c.Kind));
        Assert.Equal(LineworkCommandKind.CurveBegin, Assert.Single(Assert.Single(Parse("EGR P").Figures).Commands).Kind);
        Assert.Equal(LineworkCommandKind.PointOnCurve, Assert.Single(Assert.Single(Parse("EP OC").Figures).Commands).Kind);
    }

    [Fact]
    public void CommandParametersAndLeftoverNumbersAreKeptApart()
    {
        var height = Assert.Single(Parse("FNC B 3.5").Figures);
        Assert.Equal(new[] { "3.5" }, height.Values);

        var offset = Assert.Single(Parse("FNC HO 2.5").Figures);
        var ho = Assert.Single(offset.Commands);
        Assert.Equal(LineworkCommandKind.Offset, ho.Kind);
        Assert.Equal(new[] { "2.5" }, ho.Parameters);
        Assert.Empty(offset.Values);

        var connect = Assert.Single(Parse("FNC CPN 10234").Figures);
        Assert.Equal(LineworkCommandKind.ConnectPoint, connect.Commands[0].Kind);
        Assert.Equal(new[] { "10234" }, connect.Commands[0].Parameters);

        var recall = Assert.Single(Parse("EP RPN 501 B").Figures);
        Assert.Equal(new[] { "RPN 501", "B" }, recall.Commands.Select(c => c.ToString()));
    }

    [Fact]
    public void SidesAreReadAndNotTakenForCommands()
    {
        var right = Assert.Single(Parse("FNC R 10 20").Figures);
        Assert.Equal(LineLabelSide.Right, right.Side);
        Assert.Equal(new[] { "10", "20" }, right.Values);
        Assert.Equal(LineLabelSide.Left, Assert.Single(Parse("ASPH LEFT B").Figures).Side);
    }

    [Fact]
    public void ACodeThatIsAlsoACommandIsDecidedByPosition()
    {
        // CIR is a figure prefix and the circle command.
        var figure = Assert.Single(Parse("CIR B").Figures);
        Assert.Equal("CIR", figure.FigureId);
        Assert.True(figure.Begins);

        var circle = Assert.Single(Parse("TOP CIR 5").Figures);
        Assert.Equal("TOP", circle.FigureId);
        Assert.Equal(LineworkCommandKind.Circle, circle.Commands[0].Kind);
        Assert.Equal(new[] { "5" }, circle.Commands[0].Parameters);
    }

    [Fact]
    public void PointCodesBeforeTheFigureAreLeading()
    {
        var shot = Parse("FGP FNC");
        Assert.Equal(new[] { "FGP" }, shot.Leading);
        Assert.Equal("FNC", Assert.Single(shot.Figures).FigureId);
        Assert.Equal(new[] { "FGP", "12" }, Parse("FGP 12 FNC B 5").Leading);
        Assert.Equal(new[] { "5" }, Parse("FGP 12 FNC B 5").Figures[0].Values);
    }

    [Fact]
    public void UnknownWordsAndEscapedWordsAreKeptAsWords()
    {
        Assert.Equal(new[] { "CL" }, Assert.Single(Parse("FNC CL").Figures).Words);
        var escaped = Assert.Single(Parse("FNC /B").Figures);
        Assert.Empty(escaped.Commands);
        Assert.Equal(new[] { "B" }, escaped.Words);
        Assert.Equal(new[] { "WOOD" }, Assert.Single(Parse("fnc1 b 6' wood").Figures).Words.Select(w => w.ToUpperInvariant()));
        Assert.Equal(new[] { "6'" }, Parse("FNC1 B 6' WOOD").Figures[0].Values);
    }

    [Fact]
    public void ADescriptionWithNoLineworkCodeIsNotLinework()
    {
        var shot = Parse("PP 1234 PSE");
        Assert.False(shot.IsLinework);
        Assert.Equal(new[] { "PP", "1234", "PSE" }, shot.Leading);
        Assert.False(Parse("").IsLinework);
        Assert.False(Parse("   ").IsLinework);
    }

    [Fact]
    public void AnotherOfficesCommandSetIsConfiguration()
    {
        var other = new LineworkCommandSettings { Begin = new List<string> { "BEG" }, End = new List<string> { "END" } };
        other.FillMissing();
        var shot = LineworkCoding.Parse("FNC BEG", new[] { "FNC" }, other);
        Assert.True(shot.Figures[0].Begins);
        Assert.Equal(new[] { "B" }, LineworkCoding.Parse("FNC B", new[] { "FNC" }, other).Figures[0].Words);
    }

    [Fact]
    public void ACommandListedTwiceIsAConfigurationError()
    {
        var bad = LineworkCommandSettings.Pmx();
        bad.End = new List<string> { "B" };
        Assert.Equal(new[] { "B" }, LineworkCommandSettings.Clashes(bad));
        Assert.Empty(LineworkCommandSettings.Clashes(LineworkCommandSettings.Pmx()));
        Assert.Empty(LineworkCommandSettings.Clashes(_fx.Config.LineworkCommands));
    }

    [Fact]
    public void TheShippedRulesCarryThePmxSetAndAnOlderFileGetsItToo()
    {
        Assert.Equal(new[] { "B" }, _fx.Config.LineworkCommands.Begin);
        Assert.Equal(new[] { "HO", "VO", "SO" }, _fx.Config.LineworkCommands.Offset);

        // A rules file from before "lineworkCommands" existed still parses linework the PMX way.
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        var withoutKey = Regex.Replace(json, "\"lineworkCommands\"\\s*:\\s*\\{[^}]*\\},", string.Empty);
        Assert.DoesNotContain("\"lineworkCommands\":", withoutKey);
        var path = Path.Combine(Path.GetTempPath(), "ftf-rules-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, withoutKey);
        try
        {
            var older = RulesConfig.Load(path);
            Assert.Equal(new[] { "B" }, older.LineworkCommands.Begin);
            Assert.True(LineworkCoding.Parse("FNC1 E", older).Figures[0].Ends);
        }
        finally { File.Delete(path); }
    }
}
