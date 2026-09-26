using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// What the drafter picks in the strip easement preview. The rules that matter:
/// the office settings are never changed by a choice made for one easement, only
/// what was changed is stored, and everything else still follows the profile when
/// the easement is rebuilt later.
/// </summary>
public sealed class EasementDraftingTests
{
    private static EasementSettings Office()
    {
        var es = new EasementSettings();
        es.HatchPattern = "ANSI31";
        es.TemporaryHatchPattern = "ANSI37";
        es.DrawHatch = true;
        es.LabelCenterline = true;
        es.LabelMode = EasementLabelMode.Auto;
        es.HatchLayer = "V-ESMT-PATT-E";
        es.TextLayer = "V-ESMT-TEXT-E";
        es.DimensionLayer = "V-ESMT-DIMS-E";
        return es;
    }

    [Fact]
    public void CopyIsSeparateFromTheOriginal()
    {
        var office = Office();
        var working = office.Copy();

        working.HatchPattern = "GRAVEL";
        working.DrawHatch = false;
        working.HatchLayer = "SOMETHING-ELSE";

        Assert.Equal("ANSI31", office.HatchPattern);
        Assert.True(office.DrawHatch);
        Assert.Equal("V-ESMT-PATT-E", office.HatchLayer);
    }

    [Fact]
    public void NothingChangedIsStoredAsNothing()
    {
        var office = Office();
        Assert.Null(EasementDrafting.Difference(office, office.Copy()));
    }

    [Fact]
    public void OnlyWhatChangedIsStored()
    {
        var office = Office();
        var chosen = office.Copy();
        chosen.HatchPattern = "ANSI38";
        chosen.DrawWidthDimensions = !office.DrawWidthDimensions;

        var difference = EasementDrafting.Difference(office, chosen);

        Assert.NotNull(difference);
        Assert.Equal("ANSI38", difference!.HatchPattern);
        Assert.Equal(!office.DrawWidthDimensions, difference.DrawWidthDimensions);
        Assert.Null(difference.TextLayer);
        Assert.Null(difference.LabelCenterline);
        Assert.Null(difference.DrawHatch);
    }

    [Fact]
    public void WhatWasNotChangedStillFollowsTheProfile()
    {
        var office = Office();
        var chosen = office.Copy();
        chosen.HatchPattern = "ANSI38";
        var difference = EasementDrafting.Difference(office, chosen)!;

        // The office later moves its text to another layer; this easement was never
        // given a text layer of its own, so a rebuild picks the new one up.
        var later = Office();
        later.TextLayer = "V-ESMT-ANNO-E";
        var applied = difference.ApplyTo(later);

        Assert.Equal("ANSI38", applied.HatchPattern);
        Assert.Equal("V-ESMT-ANNO-E", applied.TextLayer);
    }

    [Fact]
    public void ApplyingDoesNotChangeTheOfficeSettings()
    {
        var office = Office();
        var difference = new EasementDrafting { HatchPattern = "DOTS", TextLayer = "X-TEXT" };

        var applied = difference.ApplyTo(office);

        Assert.Equal("DOTS", applied.HatchPattern);
        Assert.Equal("ANSI31", office.HatchPattern);
        Assert.Equal("V-ESMT-TEXT-E", office.TextLayer);
    }

    [Fact]
    public void TurningTheTemporaryHatchOffIsStoredAsAnEmptyPattern()
    {
        var office = Office();
        var chosen = office.Copy();
        chosen.TemporaryHatchPattern = string.Empty;

        var difference = EasementDrafting.Difference(office, chosen)!;

        Assert.Equal(string.Empty, difference.TemporaryHatchPattern);
        Assert.Equal(string.Empty, difference.ApplyTo(office).TemporaryHatchPattern);
    }

    [Fact]
    public void ChoicesSurviveTheDrawingRoundTrip()
    {
        var office = Office();
        var chosen = office.Copy();
        chosen.LabelCenterline = false;
        chosen.LabelMode = EasementLabelMode.Table;
        chosen.HatchScale = office.HatchScale * 2;
        var difference = EasementDrafting.Difference(office, chosen)!;

        var record = new EasementRecord { Id = "E1", Drafting = difference };
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(record);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<EasementRecord>(json)!;

        Assert.NotNull(back.Drafting);
        Assert.False(back.Drafting!.LabelCenterline);
        Assert.Equal(EasementLabelMode.Table, back.Drafting.LabelMode);
        Assert.Equal(office.HatchScale * 2, back.Drafting.HatchScale);
    }

    [Fact]
    public void AnEasementWithNoChoicesStoresNothingInTheDrawing()
    {
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(new EasementRecord { Id = "E1" });
        Assert.DoesNotContain("drafting", json);
    }
}
