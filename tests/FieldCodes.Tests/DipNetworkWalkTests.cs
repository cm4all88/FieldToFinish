using FieldCodes.Settings;
using FieldCodes.Utilities;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// Walking the network: completing the far end of a connected pipe, what is copied and what never is, and the
/// drafter's explicit structure type. Nothing here may change how dips, slopes or connections are calculated.
/// </summary>
public sealed class DipNetworkWalkTests
{
    // ------------------------------------------------------------------ opposite direction

    [Fact]
    public void EveryCompassDirectionHasItsOpposite()
    {
        var names = DirectionShortcuts.Names.ToList();
        for (var i = 0; i < 16; i++)
        {
            var back = DirectionShortcuts.Opposite(DirectionShortcuts.For(names[i]))!;
            Assert.Equal(names[(i + 8) % 16], back.Text);
            Assert.Equal((i * 22.5 + 180) % 360, back.AzimuthDegrees);
        }
        Assert.Equal("S/SE", DirectionShortcuts.Opposite(DirectionShortcuts.For("N/NW"))!.Text);
    }

    [Theory]
    [InlineData("N", "S")]
    [InlineData("SW", "NE")]
    [InlineData("N45E", "S45W")]
    [InlineData("S12.3000W", "N12.3000E")]
    [InlineData("AZ215", "AZ35")]
    [InlineData("AZ10", "AZ190")]
    [InlineData("@90", "@270")]
    public void OppositeKeepsTheWrittenForm(string observed, string expected)
    {
        var d = DirectionShortcuts.Parse(observed)!;
        var back = DirectionShortcuts.Opposite(d)!;
        Assert.Equal(expected, back.Text);
        Assert.Equal((d.AzimuthDegrees!.Value + 180) % 360, back.AzimuthDegrees!.Value, 6);
    }

    [Fact]
    public void AnUnknownDirectionIsNotGuessed()
    {
        Assert.Null(DirectionShortcuts.Opposite(ObservedDirection.Unknown("?")));
        Assert.Null(DirectionShortcuts.Opposite(null));
    }

    // ------------------------------------------------------------------ completing a pipe

    private static (UtilityProject Project, StructureRecord A, StructureRecord B, PipeObservation Source, PipeConnection Connection) Walk()
    {
        var a = new StructureRecord { StructureType = "SDMH", Cad = new CadStructureSnapshot { PointNumber = "1047", Rim = 326.00, Northing = 0, Easting = 0 } };
        a.Field.PointNumber = "1047";
        a.Field.FieldCode = "SDMH";
        var b = new StructureRecord { StructureType = "CB", Cad = new CadStructureSnapshot { PointNumber = "1048", Rim = 327.90, Northing = 100, Easting = -41.421 } };
        b.Field.PointNumber = "1048";
        var source = new QuickPipeEntry
        {
            Direction = DirectionShortcuts.For("N/NW"), SizeIn = 17.5, Material = "RIBBED PVC", MeasuredDip = 6.41, Reference = MeasurementReference.Invert
        }.Create();
        a.Field.Pipes.Add(source);
        var project = new UtilityProject();
        project.Structures.Add(a);
        project.Structures.Add(b);
        var candidate = ConnectionFinder.ManualCandidate(project, a, source, b, new UtilitySettings());
        var connection = ConnectionFinder.Accept(project, a, source, candidate, true, "walk");
        return (project, a, b, source, connection);
    }

    [Fact]
    public void TheWaitingConnectionIsOfferedButNothingIsCreated()
    {
        var w = Walk();
        Assert.Single(NetworkCompletion.Waiting(w.Project, w.B));
        Assert.Empty(NetworkCompletion.Waiting(w.Project, w.A));

        var prefill = NetworkCompletion.Prefill(w.Project, w.Connection);
        Assert.Equal("S/SE", prefill.Direction!.Text);
        Assert.Equal(17.5, prefill.SizeIn);
        Assert.Equal("RIBBED PVC", prefill.Material);
        // The far end's measure down and reference are never copied.
        Assert.Null(prefill.MeasuredDip);
        Assert.Equal(MeasurementReference.Unspecified, prefill.Reference);
        Assert.Equal(new[] { "direction", "size", "material" }, prefill.Prefilled);
        Assert.StartsWith("SDMH 1047: ", prefill.PrefilledFrom);

        // Asking for the prefill adds nothing: the pipe exists only once the drafter adds it.
        Assert.Empty(w.B.Field.Pipes);
        Assert.Null(w.Connection.ToPipeId);
    }

    [Fact]
    public void CompletingTiesTheNewPipeToTheExistingConnection()
    {
        var w = Walk();
        var connectionsBefore = w.Project.Connections.Count;
        var entry = NetworkCompletion.Prefill(w.Project, w.Connection);
        entry.MeasuredDip = 6.9;
        entry.Reference = MeasurementReference.Invert;
        var pipe = entry.Create();

        Assert.Null(NetworkCompletion.Complete(w.Project, w.Connection.Id, w.B.Id, pipe));
        Assert.Same(pipe, w.B.Field.Pipes.Single());
        Assert.Equal(pipe.Id, w.Connection.ToPipeId);
        Assert.Equal(connectionsBefore, w.Project.Connections.Count);          // the same connection, not a new one
        Assert.Equal(ConnectionStatus.ManualOverride, w.Connection.Status);   // the drafter's choice stays the drafter's
        Assert.Same(w.Connection, w.Project.ConnectionFor(w.B.Id, pipe.Id));
        Assert.Contains(w.Connection.Basis, b => b.Contains("Other end entered at CB 1048 by the drafter"));
        Assert.Contains(w.Project.Overrides, o => o.What == "Pipe completed from connected pipe" && o.Target == w.Connection.Id);
        Assert.Empty(NetworkCompletion.Waiting(w.Project, w.B));

        // Provenance: entered by the drafter, with the copied values marked as copied.
        Assert.Equal(ObservationSource.UserEntry, pipe.Source);
        Assert.Equal(ReferenceBasis.EnteredByDrafter, pipe.ReferenceBasis);
        Assert.Equal(new[] { "direction", "size", "material" }, pipe.Prefilled);
        Assert.StartsWith("SDMH 1047: ", pipe.PrefilledFrom);
    }

    [Fact]
    public void SlopeOnlyWhenTheExistingRulesAllowIt()
    {
        var w = Walk();
        Assert.Equal(SlopeBasis.MissingOppositeObservation, SlopeCalculator.Compute(w.Project, w.Connection).Basis);

        // The far end entered with no stated reference: still no slope.
        var entry = NetworkCompletion.Prefill(w.Project, w.Connection);
        entry.MeasuredDip = 6.9;
        var pipe = entry.Create();
        Assert.Null(NetworkCompletion.Complete(w.Project, w.Connection.Id, w.B.Id, pipe));
        var unconfirmed = SlopeCalculator.Compute(w.Project, w.Connection);
        Assert.Null(unconfirmed.SlopePercent);
        Assert.Equal(SlopeBasis.IncomparableReferences, unconfirmed.Basis);

        // Confirmed as an invert: the ordinary calculation, from both field dips.
        ObservationReview.ConfirmReference(w.Project, w.B, pipe, MeasurementReference.Invert);
        var slope = SlopeCalculator.Compute(w.Project, w.Connection);
        Assert.Equal(SlopeBasis.CalculatedFromBothObservations, slope.Basis);
        var expected = Math.Abs((326.00 - 6.41) - (327.90 - 6.9)) / SlopeCalculator.Distance(w.A, w.B) * 100;
        Assert.Equal(expected, slope.SlopePercent!.Value, 9);
    }

    [Fact]
    public void ACompletedConnectionCannotBeCompletedTwice()
    {
        var w = Walk();
        var first = NetworkCompletion.Prefill(w.Project, w.Connection).Create();
        Assert.Null(NetworkCompletion.Complete(w.Project, w.Connection.Id, w.B.Id, first));
        var second = NetworkCompletion.Prefill(w.Project, w.Connection).Create();
        Assert.NotNull(NetworkCompletion.Complete(w.Project, w.Connection.Id, w.B.Id, second));
        Assert.Single(w.B.Field.Pipes);                                      // nothing added the second time
        Assert.NotNull(NetworkCompletion.Complete(w.Project, w.Connection.Id, w.A.Id, second));   // wrong structure
        Assert.Single(w.A.Field.Pipes);
    }

    [Fact]
    public void AValueChangedHereIsNoLongerMarkedCopied()
    {
        var w = Walk();
        var entry = NetworkCompletion.Prefill(w.Project, w.Connection);
        // The drafter found an 18" at CB 1048 and entered it; direction and material stay as copied.
        entry.SizeIn = 18;
        entry.Prefilled.Remove(QuickPipeEntry.SizeField);
        var pipe = entry.Create();
        Assert.Equal(18, pipe.WidthIn);
        Assert.Equal(new[] { "direction", "material" }, pipe.Prefilled);
        Assert.True(pipe.IsPrefilled("material"));
        Assert.False(pipe.IsPrefilled("size"));
    }

    [Fact]
    public void CopiedMarksSurviveSavingAndOlderDrawingsHaveNone()
    {
        var w = Walk();
        var pipe = NetworkCompletion.Prefill(w.Project, w.Connection).Create();
        NetworkCompletion.Complete(w.Project, w.Connection.Id, w.B.Id, pipe);
        var back = UtilityProject.FromJson(w.Project.ToJson());
        var copy = back.Pipe(w.B.Id, pipe.Id)!;
        Assert.Equal(new[] { "direction", "size", "material" }, copy.Prefilled);
        Assert.Equal(pipe.PrefilledFrom, copy.PrefilledFrom);
        Assert.Equal(pipe.Id, back.Connections.Single().ToPipeId);

        // An observation saved before this existed reads with nothing copied.
        var old = JsonConvert.DeserializeObject<PipeObservation>("{\"id\":\"x\",\"widthIn\":12,\"material\":\"RCP\"}")!;
        Assert.Empty(old.Prefilled);
        Assert.False(old.IsPrefilled("size"));
        Assert.DoesNotContain("prefilled", JsonConvert.SerializeObject(old));
    }

    // ------------------------------------------------------------------ size precision

    [Theory]
    [InlineData(12, "12\"")]
    [InlineData(17.5, "17.5\"")]
    [InlineData(1.25, "1.25\"")]
    [InlineData(1.125, "1.125\"")]
    [InlineData(8.0, "8\"")]
    public void PipeSizesKeepTheirEnteredPrecision(double size, string expected)
    {
        var pipe = new PipeObservation { WidthIn = size, HeightIn = size };
        Assert.Equal(expected, UtilityLabelFormatter.FormatSize(pipe));
        Assert.Equal(size, pipe.WidthIn);   // the stored value is untouched
    }

    [Fact]
    public void TheDrawnPipeLabelShows125()
    {
        var w = Walk();
        w.Source.WidthIn = 1.25;
        w.Source.HeightIn = 1.25;
        Assert.StartsWith("1.25\" RIBBED PVC", UtilityLabelFormatter.PipeLabel(w.Project, w.Connection, new UtilitySettings()));
    }

    [Fact]
    public void StructureSizesKeepTheirPrecisionToo()
    {
        var s = new StructureRecord { StructureType = "SDMH", EnteredInsideWidthIn = 48.25 };
        s.Field.FieldCode = "SDMH";
        Assert.Equal("48.25\"", StructureDimensions.SizeText(s, new UtilitySettings()));
    }

    // ------------------------------------------------------------------ the drafter's structure type

    [Fact]
    public void TheDrafterTypeDrivesShapeButtonsAndLabel()
    {
        var settings = new UtilitySettings();
        var s = new StructureRecord { StructureType = "SDMH", EnteredInsideWidthIn = 24, EnteredInsideLengthIn = 36 };
        s.Field.FieldCode = "SDMH";
        s.Field.PointNumber = "1047";
        Assert.Equal(StructureShape.Round, StructureDimensions.ShapeOf(s, settings));
        Assert.Equal("SDMH 1047", s.Label);

        // The drafter sets CB: round -> rectangular, catch basin buttons, label header, all together.
        s.StructureType = "CB";
        s.TypeSetByDrafter = true;
        Assert.Equal("CB", s.EffectiveCode);
        Assert.Equal(StructureShape.Rectangular, StructureDimensions.ShapeOf(s, settings));
        Assert.Equal("24\"x36\"", StructureDimensions.SizeText(s, settings));
        Assert.Equal("CB 1047", s.Label);
        Assert.Equal("Catch basins and inlets", settings.PipeChoicesFor(s.EffectiveCode, s.System).RuleName);
        Assert.StartsWith("CB 1047", UtilityLabelFormatter.StructureLabel(new UtilityProject { Structures = { s } }, s, settings)[0]);

        // The field observation is untouched.
        Assert.Equal("SDMH", s.Field.FieldCode);
    }

    [Fact]
    public void ATypeFromTheCadDescriptionNeverOutranksTheFieldCode()
    {
        // A type filled from the point description before notes were read is not the drafter's choice.
        var s = new StructureRecord { StructureType = "CB" };
        s.Field.FieldCode = "SDMH";
        Assert.False(s.TypeSetByDrafter);
        Assert.Equal("SDMH", s.EffectiveCode);
        Assert.Equal(StructureShape.Round, StructureDimensions.ShapeOf(s, new UtilitySettings()));
    }

    [Fact]
    public void TheDrafterTypeIsSavedAndOlderDrawingsKeepTheirBehaviour()
    {
        var s = new StructureRecord { StructureType = "CB", TypeSetByDrafter = true };
        s.Field.FieldCode = "SDMH";
        var back = JsonConvert.DeserializeObject<StructureRecord>(JsonConvert.SerializeObject(s))!;
        Assert.True(back.TypeSetByDrafter);
        Assert.Equal("SDMH", back.Field.FieldCode);
        Assert.Equal("CB", back.EffectiveCode);

        var old = JsonConvert.DeserializeObject<StructureRecord>("{\"type\":\"CB\",\"field\":{\"fieldCode\":\"SDMH\"}}")!;
        Assert.False(old.TypeSetByDrafter);
        Assert.Equal("SDMH", old.EffectiveCode);
        Assert.DoesNotContain("typeSetByDrafter", JsonConvert.SerializeObject(old));
    }
}
