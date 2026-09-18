using FieldCodes;

namespace FieldCodes.Tests;

/// <summary>
/// Symbol labelling: bare codes listed in symbolLabels get labelled with the
/// code itself ("like the code list"), on the family's text layer, through the
/// same label engine as everything else. The office rule that a bare code does
/// nothing still stands for every UNLISTED code -- this is an explicit opt-in
/// per code, never a default.
/// </summary>
public sealed class SymbolLabelTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public SymbolLabelTests(RulesFixture fx) => _fx = fx;

    [Fact]
    public void ABareCatchBasinLabelsItselfOnTheStormTextLayer()
    {
        var p = _fx.Parse("CB");

        Assert.True(p.Recognized);
        Assert.Equal("symbol-label", p.RuleId);
        Assert.Equal("CB", p.LabelText);
        Assert.Equal("V-UTIL-STRM-TEXT", p.LabelLayer);
        Assert.Equal(LeaderMode.Auto, p.Leader);
    }

    [Fact]
    public void TheLabelSaysTheCodeExactlyAsShot_IncludingVariants()
    {
        // CBS is not collapsed to CB: the plan says what the field said.
        Assert.Equal("CBS", _fx.Parse("CBS").LabelText);
        Assert.Equal("SNNP", _fx.Parse("SNNP").LabelText);
    }

    [Fact]
    public void TrailingFigureDigitsMatchTheBaseCode()
    {
        // A numbered shot like CB2 still labels, verbatim as shot.
        var p = _fx.Parse("CB2");

        Assert.True(p.Recognized);
        Assert.Equal("CB2", p.LabelText);
        Assert.Equal("V-UTIL-STRM-TEXT", p.LabelLayer);
    }

    [Fact]
    public void AnUnlistedBareCodeStillDoesNothing()
    {
        // The strict rule survives: opting CB in did not opt anything else in.
        Assert.True(_fx.Parse("SSCO").NoContent);
        Assert.True(_fx.Parse("FMON").NoContent);
        Assert.True(_fx.Parse("CONC").NoContent);
    }

    [Fact]
    public void DataCarryingDescriptionsStillUseTheirFullRules()
    {
        // "CB 12" has data: the catch-basin RULE owns it, not the symbol label.
        var p = _fx.Parse("CB 12");

        Assert.Equal("catch-basin", p.RuleId);
        Assert.NotEqual("CB", p.LabelText);      // the rule's format, not the code
    }

    [Fact]
    public void ControlShotsAreUntouched()
    {
        // Bare control still does nothing; AKA still labels the alias.
        Assert.True(_fx.Parse("XMAG").NoContent);
        Assert.Equal("control", _fx.Parse("XMAG AKA 1013").RuleId);
    }
}
