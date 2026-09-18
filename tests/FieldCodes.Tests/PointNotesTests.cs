using FieldCodes;
using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// Point-note consensus: the survey points a line was drawn through still carry
/// the raw field coding the TBC export erased from the linework -- "FOG B" vs
/// "LNDY B" on the same flattened stripe layer, "RWRK B" wall subtypes, "ASPH L"
/// directional intent. The notes must AGREE (set intersection) or nothing is
/// claimed. Descriptions come from the real 553-2750-051 survey grammar.
/// </summary>
public sealed class PointNotesTests : IClassFixture<RulesFixture>
{
    private readonly LineworkCatalog _catalog;

    public PointNotesTests(RulesFixture fx)
        => _catalog = new LineworkCatalog(fx.Config.LineFeatures);

    private PointNoteConsensus Consensus(params string[] notes)
        => PointNotes.Consensus(notes, _catalog);

    // ------------------------------------------------------------- agreement

    [Fact]
    public void FogLineShotsRecoverTheFogLine()
    {
        var c = Consensus("FOG B", "FOG B", "FOG B");

        var feature = Assert.Single(c.Features);
        Assert.Equal("FOG", feature.Code);
        Assert.Equal(3, c.NoteCount);
        Assert.Null(c.Side);
        Assert.False(c.SideConflict);
    }

    [Fact]
    public void DoubleYellowIsDistinguishableFromFog_UnlikeTheFlattenedLayer()
    {
        // Both land on stripe layers in the export; the notes still know which.
        Assert.Equal("LNDY", Consensus("LNDY B", "LNDY B").Features.Single().Code);
        Assert.Equal("FOG", Consensus("FOG B").Features.Single().Code);
    }

    [Fact]
    public void AChainedShotNarrowsAgainstAPlainOne()
    {
        // "BLD B EC B BLD1 B" names building AND concrete edge; "EC B" pins it.
        var c = Consensus("BLD B EC B BLD1 B", "EC B");

        Assert.Equal("EC", c.Features.Single().Code);
    }

    [Fact]
    public void TrailingFigureDigitsAreStripped()
    {
        Assert.Equal("BLD", Consensus("BLD1 B").Features.Single().Code);
    }

    // ----------------------------------------------------------- directional

    [Fact]
    public void ASurvivingDirectionalModifierComesThrough()
    {
        var c = Consensus("ASPH L");

        Assert.Equal("ASPH", c.Features.Single().Code);
        Assert.Equal(LineLabelSide.Left, c.Side);
    }

    [Fact]
    public void ConflictingSidesClaimNothing()
    {
        var c = Consensus("ASPH L", "ASPH R");

        Assert.Equal("ASPH", c.Features.Single().Code);   // identity still agrees
        Assert.Null(c.Side);
        Assert.True(c.SideConflict);
    }

    // ------------------------------------------------------------- honesty

    [Fact]
    public void DisagreeingNotesAreReported_NeverChosenFrom()
    {
        var c = Consensus("RWRK B", "FCK B");

        Assert.Empty(c.Features);
        Assert.Contains("RWRK", c.DisagreeingCodes);
        Assert.Contains("FCK", c.DisagreeingCodes);
    }

    [Fact]
    public void NotesWithoutALineCodeAreIgnoredAsCoincidence()
    {
        // A ground shot happening to sit on a vertex bucket must not poison the
        // consensus -- only notes carrying a known line code count.
        var c = Consensus("GS", "CONC", "FOG B");

        Assert.Equal("FOG", c.Features.Single().Code);
        Assert.Equal(1, c.NoteCount);
        Assert.Equal(new[] { "FOG B" }, c.SampleNotes);
    }

    // ---------------------------- the same flattened layer, different labels

    /// <summary>The channelization slice of the real Gig Harbor layer table.</summary>
    private static readonly HashSet<string> ChanLayers = LabelLayerResolver.BuildCatalog(
        new[] { "V-CHAN-STRP-E", "V-CHAN-STRP-TEXT-E", "V-CHAN-MRKG-TEXT-E" });

    [Fact]
    public void TwoPolylinesOnTheSameStripeLayerResolveDifferentlyFromTheirNotes()
    {
        // Both polylines sit on V-CHAN-STRP-E after the TBC export. Their vertex
        // point notes still know which stripe each one is.
        var fog = PointNotes.Consensus(new[] { "FOG B", "FOG B" }, _catalog);
        var yellow = PointNotes.Consensus(new[] { "LNDY B", "LNDY B" }, _catalog);

        Assert.Equal("FOG LINE", LineworkCatalog.UnanimousLabel(fog.Features));
        Assert.Equal("DOUBLE YELLOW", LineworkCatalog.UnanimousLabel(yellow.Features));

        // And the shared label-layer resolver lands BOTH on the existing
        // channelization text layer, driven by the source layer they share.
        foreach (var identified in new[] { fog.Features, yellow.Features })
        {
            var layer = LabelLayerResolver.Resolve("V-CHAN-STRP-E", identified,
                                                   ChanLayers, "V-LINE-TEXT");
            Assert.Equal("V-CHAN-STRP-TEXT-E", layer.Layer);
            Assert.Equal(LabelLayerSource.DerivedText, layer.Source);
        }
    }

    [Fact]
    public void WithoutNotesTheSharedStripeLayerNowDisagrees_AndNothingIsGuessed()
    {
        // The documented consequence of FOG's own standard: layer-only evidence on
        // V-CHAN-STRP-E yields LNSO "STRIPE" vs FOG "FOG LINE" -- no unanimous
        // label, so the existing no-silent-choice rule refuses. Point notes are
        // what disambiguates this layer now.
        var byLayer = _catalog.FindByLayer("V-CHAN-STRP-E");

        Assert.Equal(2, byLayer.Count);
        Assert.Null(LineworkCatalog.UnanimousLabel(byLayer));
    }

    [Fact]
    public void ConflictingNotesFallBackThroughTheExistingPrecedence()
    {
        // FOG and LNDY notes on one line: no consensus -- so identification falls
        // to the layer, which now refuses too. Nothing is ever guessed.
        var conflicted = PointNotes.Consensus(new[] { "FOG B", "LNDY B" }, _catalog);

        Assert.Empty(conflicted.Features);
        Assert.Contains("FOG", conflicted.DisagreeingCodes);
        Assert.Contains("LNDY", conflicted.DisagreeingCodes);
    }

    [Fact]
    public void NoUsableNotesClaimNothing()
    {
        var empty = Consensus();
        Assert.Empty(empty.Features);
        Assert.Empty(empty.DisagreeingCodes);
        Assert.Equal(0, empty.NoteCount);

        var unknown = Consensus("GS", "XMAG AKA 1013");
        Assert.Empty(unknown.Features);
        Assert.Equal(0, unknown.NoteCount);
    }
}
