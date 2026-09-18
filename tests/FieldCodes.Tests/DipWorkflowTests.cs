using FieldCodes.Settings;
using FieldCodes.Utilities;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// The production workflow: walking a utility network structure by structure.
///
/// These tests follow the real sequence a drafter works in -- open a structure,
/// enter its observations, connect a pipe to the structure they know it runs to,
/// draw it, open the far structure, enter the opposite observation -- and assert
/// what the drafter must be able to see at each point. They also assert what the
/// workflow must NEVER do: turn a drafter's pick into a field observation, show a
/// slope that only one end supports, or report a stale drawing as current.
/// </summary>
public sealed class DipWorkflowTests
{
    private static readonly UtilitySettings Settings = new UtilitySettings();

    private static StructureRecord Structure(string notes, double rim, double northing, double easting,
                                             UtilitySystem system = UtilitySystem.Storm)
    {
        var parsed = new DipNoteParser(Settings).Parse(notes);
        Assert.Single(parsed.Structures);
        var field = parsed.Structures[0];
        return new StructureRecord
        {
            Field = field,
            StructureType = field.FieldCode,
            System = system,
            Cad = new CadStructureSnapshot
            {
                PointNumber = field.PointNumber, Rim = rim, Northing = northing, Easting = easting
            }
        };
    }

    private static UtilityProject Project(params StructureRecord[] structures)
    {
        var project = new UtilityProject();
        project.Structures.AddRange(structures);
        return project;
    }

    /// <summary>1045 with three pipes, 1048 north of it with the opposite pipe.
    /// 183.42' apart, which is the distance the real example uses.</summary>
    private static UtilityProject Network(out StructureRecord s1045, out StructureRecord s1048)
    {
        s1045 = Structure("PT 1045 SDMH\nBOT 7.82\n12 RCP N INV 6.41\n18 RCP SW INV 7.02", 328.42, 1000, 500);
        s1048 = Structure("PT 1048 SDMH\n12 RCP S INV 5.20", 326.28, 1183.42, 500);
        return Project(s1045, s1048);
    }

    private static PipeObservation NorthPipe(StructureRecord s)
        => s.Field.Pipes.Single(p => p.Direction.Text == "N");

    // ==================================================== session navigation

    [Fact]
    public void Back_ReturnsToTheStructureIWasWorkingOn()
    {
        var nav = new DipNavigator();
        nav.Open("1045");
        nav.Open("1048");

        Assert.Equal("1048", nav.Current);
        Assert.True(nav.CanGoBack);
        Assert.Equal("1045", nav.Back());
        Assert.Equal("1045", nav.Current);
    }

    [Fact]
    public void Forward_UndoesBack()
    {
        var nav = new DipNavigator();
        nav.Open("1045");
        nav.Open("1048");
        nav.Back();

        Assert.True(nav.CanGoForward);
        Assert.Equal("1048", nav.Forward());
        Assert.Equal("1048", nav.Current);
    }

    [Fact]
    public void OpeningSomewhereNew_DropsTheForwardTrail()
    {
        var nav = new DipNavigator();
        nav.Open("1045");
        nav.Open("1048");
        nav.Back();
        nav.Open("1052");

        Assert.False(nav.CanGoForward);
        Assert.Equal("1052", nav.Current);
    }

    [Fact]
    public void Recent_IsMostRecentFirst_AndExcludesWhereIAmNow()
    {
        var nav = new DipNavigator();
        nav.Open("1045");
        nav.Open("1048");
        nav.Open("1052");

        Assert.Equal(new[] { "1048", "1045" }, nav.Recent);
    }

    [Fact]
    public void ReopeningTheCurrentStructure_DoesNotStackHistory()
    {
        var nav = new DipNavigator();
        nav.Open("1045");
        nav.Open("1045");
        nav.Open("1045");

        Assert.False(nav.CanGoBack);
        Assert.Single(nav.Visited);
    }

    [Fact]
    public void Recent_IsCappedAndDropsTheOldest()
    {
        var nav = new DipNavigator { RecentLimit = 3 };
        foreach (var id in new[] { "a", "b", "c", "d" }) nav.Open(id);

        Assert.Equal(new[] { "d", "c", "b" }, nav.Visited);
    }

    [Fact]
    public void Prune_DropsStructuresTheProjectNoLongerHas()
    {
        StructureRecord a, b;
        var project = Network(out a, out b);
        var nav = new DipNavigator();
        nav.Open(a.Id);
        nav.Open(b.Id);

        project.Structures.Remove(a);
        nav.Prune(project);

        Assert.False(nav.CanGoBack);
        Assert.DoesNotContain(a.Id, nav.Visited);
    }

    /// <summary>Navigation is session state. If it ever starts serialising with the
    /// project, that is a design change and this test should fail loudly.</summary>
    [Fact]
    public void NavigationHistory_IsNotProjectData()
    {
        StructureRecord a, b;
        var project = Network(out a, out b);
        var nav = new DipNavigator();
        nav.Open(a.Id);
        nav.Open(b.Id);

        var json = project.ToJson();

        Assert.DoesNotContain("\"recent\"", json);
        Assert.DoesNotContain("\"visited\"", json);
        Assert.DoesNotContain("\"navigation\"", json);
        Assert.DoesNotContain("navigator", json.ToLowerInvariant());
    }

    // ============================================ drafter-selected connection

    [Fact]
    public void DrafterPicksTheStructure_RecordedAsAManualOverride_NotAsObserved()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);

        var candidate = ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings);
        var connection = ConnectionFinder.Accept(project, s1045, north, candidate, true, "known from the field sketch");

        Assert.Equal(ConnectionStatus.ManualOverride, connection.Status);
        Assert.Equal(PipeConnectionState.DrafterSelected, DipWorkflow.StateOf(connection));

        // A drafter's certainty is not evidence: no confidence is claimed for it.
        Assert.Equal(Confidence.None, connection.Confidence);
        Assert.Contains(connection.Basis, b => b.Contains("manually by the drafter"));
        Assert.Contains("known from the field sketch", connection.OverrideNote);
    }

    [Fact]
    public void ADrafterSelectedConnection_LeavesTheFieldNotesAlone()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var rawBefore = north.RawText;
        var sourceBefore = north.Source;
        var referenceBefore = north.Reference;
        var basisBefore = north.ReferenceBasis;

        var candidate = ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings);
        ConnectionFinder.Accept(project, s1045, north, candidate, true, null);

        Assert.Equal(rawBefore, north.RawText);
        Assert.Equal(sourceBefore, north.Source);
        Assert.Equal(referenceBefore, north.Reference);
        Assert.Equal(basisBefore, north.ReferenceBasis);
        Assert.Equal(ObservationSource.FieldNote, north.Source);
    }

    [Fact]
    public void ManualConnection_IsDistinguishableFromAConfirmedSuggestion()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);

        var suggested = ConnectionFinder.Find(project, s1045, north, Settings).First();
        var confirmed = ConnectionFinder.Accept(project, s1045, north, suggested, false, null);
        Assert.Equal(PipeConnectionState.Confirmed, DipWorkflow.StateOf(confirmed));

        var manual = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        Assert.Equal(PipeConnectionState.DrafterSelected, DipWorkflow.StateOf(manual));

        Assert.NotEqual(DipWorkflow.StateOf(confirmed), DipWorkflow.StateOf(manual));
    }

    [Fact]
    public void ManualPick_RecordsAnOverrideEntry()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);

        ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);

        Assert.Contains(project.Overrides, o => o.What == "Manual connection");
    }

    // ================================================= suggestions stay open

    [Fact]
    public void Suggestions_AreNeverConfirmedOnTheirOwn()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);

        var candidates = ConnectionFinder.Find(project, s1045, north, Settings);

        Assert.NotEmpty(candidates);
        Assert.Empty(project.Connections);

        var state = DipWorkflow.Describe(project, s1045, north, null);
        Assert.Equal(PipeConnectionState.NotExamined, state.ConnectionState);
        Assert.Equal(PipeDraftingState.NotReady, state.DraftingState);
    }

    [Fact]
    public void AnUnconfirmedPipe_TellsTheDrafterWhatToDoNext()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);

        var state = DipWorkflow.Describe(project, s1045, NorthPipe(s1045), null);

        Assert.Contains("Connect this pipe", state.NextAction);
    }

    // ================================================ walking to the far end

    [Fact]
    public void AConnectedPipe_NamesTheStructureToOpenNext()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);

        var state = DipWorkflow.Describe(project, s1045, north, null);

        Assert.NotNull(state.ConnectedTo);
        Assert.Equal(s1048.Id, state.ConnectedTo.Id);
        Assert.Equal("1048", state.ConnectedTo.Field.PointNumber);
    }

    [Fact]
    public void BothEndsObserved_OnlyWhenBothEndsWereActuallyDipped()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);

        var state = DipWorkflow.Describe(project, s1045, north, null);

        Assert.True(state.BothEndsObserved);
        Assert.NotNull(state.OppositePipe);
        Assert.Equal(SlopeBasis.CalculatedFromBothObservations, state.Slope.Basis);
        Assert.NotNull(state.Slope.SlopePercent);
    }

    [Fact]
    public void FarEndNotObserved_ShowsNoSlopeAndDoesNotClaimBothEnds()
    {
        var s1045 = Structure("PT 1045 SDMH\n12 RCP N INV 6.41", 328.42, 1000, 500);
        var s1048 = Structure("PT 1048 SDMH", 326.28, 1183.42, 500);   // nothing dipped here
        var project = Project(s1045, s1048);
        var north = NorthPipe(s1045);

        ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);

        var state = DipWorkflow.Describe(project, s1045, north, null);

        Assert.False(state.BothEndsObserved);
        Assert.Null(state.OppositePipe);
        Assert.Equal(SlopeBasis.MissingOppositeObservation, state.Slope.Basis);
        Assert.Null(state.Slope.SlopePercent);
    }

    /// <summary>With the office convention off, an unmarked dip stays Unspecified.
    /// The far end is observed, but nothing says what it was measured to -- so there
    /// is no slope, and one is not invented to fill the label.</summary>
    [Fact]
    public void UnconfirmedReferenceAtTheFarEnd_StillYieldsNoSlope()
    {
        var noConvention = new UtilitySettings
        {
            UnmarkedDipsAreInvertsByConvention = false,
            UnmarkedDipConventionSource = null
        };
        var parsed = new DipNoteParser(noConvention).Parse("PT 1048 SDMH\n12 RCP S 5.20");
        var s1045 = Structure("PT 1045 SDMH\n12 RCP N INV 6.41", 328.42, 1000, 500);
        var s1048 = new StructureRecord
        {
            Field = parsed.Structures[0],
            StructureType = parsed.Structures[0].FieldCode,
            System = UtilitySystem.Storm,
            Cad = new CadStructureSnapshot { PointNumber = "1048", Rim = 326.28, Northing = 1183.42, Easting = 500 }
        };
        var project = Project(s1045, s1048);
        var north = NorthPipe(s1045);

        var opposite = s1048.Field.Pipes.Single();
        Assert.True(opposite.ReferenceUnconfirmed);
        Assert.Equal(ReferenceBasis.NotStated, opposite.ReferenceBasis);

        ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, noConvention), true, null);

        var state = DipWorkflow.Describe(project, s1045, north, null);

        Assert.Equal(SlopeBasis.IncomparableReferences, state.Slope.Basis);
        Assert.Null(state.Slope.SlopePercent);
    }

    /// <summary>And with the convention on -- the office default -- the same note is
    /// classified as an invert, with the convention recorded on the pipe as its
    /// basis. The measurement itself is untouched either way.</summary>
    [Fact]
    public void TheOfficeConvention_ClassifiesAnUnmarkedDipAndSaysSo()
    {
        var s = Structure("PT 1048 SDMH\n12 RCP S 5.20", 326.28, 1183.42, 500);
        var pipe = s.Field.Pipes.Single();

        Assert.Equal(MeasurementReference.Invert, pipe.Reference);
        Assert.Equal(ReferenceBasis.FieldNoteConvention, pipe.ReferenceBasis);
        Assert.False(string.IsNullOrWhiteSpace(pipe.ReferenceNote));
        Assert.Equal(5.20, pipe.MeasuredDip!.Value, 3);
    }

    // ===================================================== drafting lifecycle

    [Fact]
    public void AnAcceptedConnection_IsReadyToDraw()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);

        var state = DipWorkflow.Describe(project, s1045, north, null);

        Assert.Equal(PipeDraftingState.ReadyToDraw, state.DraftingState);
        Assert.Equal("Draw pipe + label", state.NextAction);
    }

    [Fact]
    public void AfterDrawing_ThePipeReadsAsDrawnAndCurrent()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var connection = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);

        DipWorkflow.RecordDrafted(project, connection);
        var state = DipWorkflow.Describe(project, s1045, north, null);

        Assert.Equal(PipeDraftingState.Drawn, state.DraftingState);
        Assert.Equal(PipeLabelState.LabelPlaced, state.LabelState);
        Assert.Null(state.NextAction);
        Assert.NotNull(connection.DraftedUtc);
    }

    [Fact]
    public void ChangingTheObservationAfterDrawing_MakesTheDrawingStale()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var connection = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        DipWorkflow.RecordDrafted(project, connection);

        north.MeasuredDip = 6.55;                                  // re-read from the field book

        var state = DipWorkflow.Describe(project, s1045, north, null);

        Assert.Equal(PipeDraftingState.DrawingStale, state.DraftingState);
        Assert.Contains("data changed", state.NextAction);
    }

    [Fact]
    public void MovingTheSurveyPointAfterDrawing_MakesTheDrawingStale()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var connection = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        DipWorkflow.RecordDrafted(project, connection);

        s1048.Cad.Easting += 3.0;

        Assert.Equal(PipeDraftingState.DrawingStale,
                     DipWorkflow.Describe(project, s1045, north, null).DraftingState);
    }

    [Fact]
    public void ChangingTheConnectionAfterDrawing_MakesTheOldDraftingStale()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var s1052 = Structure("PT 1052 SDMH\n12 RCP S INV 5.60", 327.10, 1150, 620);
        project.Structures.Add(s1052);

        var north = NorthPipe(s1045);
        var connection = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        DipWorkflow.RecordDrafted(project, connection);
        Assert.Equal(PipeDraftingState.Drawn, DipWorkflow.Describe(project, s1045, north, null).DraftingState);

        // The drafter decides it actually runs to 1052. Accept replaces the record;
        // the drawing in the file is now of the wrong pipe and must say so.
        var redirected = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1052, Settings), true, "corrected");

        Assert.False(redirected.Drafted);

        // The pipe drawn to 1048 is still in the file. Until it is redrawn the pipe
        // reads as stale, not as "ready to draw" -- the old geometry is not current.
        Assert.True(redirected.HasSupersededDrafting);
        Assert.Equal(connection.Id, redirected.SupersededDraftingId);
        var state = DipWorkflow.Describe(project, s1045, north, null);
        Assert.Equal(PipeDraftingState.DrawingStale, state.DraftingState);
        Assert.Contains("runs somewhere else", state.NextAction);

        // Redrawing settles it.
        DipWorkflow.RecordDrafted(project, redirected);
        Assert.False(redirected.HasSupersededDrafting);
        Assert.Equal(PipeDraftingState.Drawn,
                     DipWorkflow.Describe(project, s1045, north, null).DraftingState);
    }

    /// <summary>A label the drafter positioned or retyped belongs to the pipe at this
    /// structure. Re-pointing the connection must not silently discard it.</summary>
    [Fact]
    public void ChangingTheConnection_KeepsTheDraftersLabelWork()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var s1052 = Structure("PT 1052 SDMH\n12 RCP S INV 5.60", 327.10, 1150, 620);
        project.Structures.Add(s1052);

        var north = NorthPipe(s1045);
        var first = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        DipWorkflow.RecordDrafted(project, first);
        first.LabelLocation = new LabelPlacement(505.0, 1085.0, 0.25);
        first.LabelTextOverride = "12\" RCP SD FIELD VERIFY";

        var redirected = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1052, Settings), true, null);

        Assert.NotNull(redirected.LabelLocation);
        Assert.Equal(505.0, redirected.LabelLocation.X, 4);
        Assert.Equal("12\" RCP SD FIELD VERIFY", redirected.LabelTextOverride);
    }

    [Fact]
    public void DraftedBeforeFingerprintsExisted_IsReportedAsDrawnNotStale()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var connection = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);

        connection.Drafted = true;                 // old data: no fingerprint recorded
        connection.DraftedFingerprint = null;

        Assert.Equal(PipeDraftingState.Drawn,
                     DipWorkflow.Describe(project, s1045, north, null).DraftingState);
    }

    [Fact]
    public void ReadyToDraw_ListsAcceptedAndStalePipesOnly()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var sw = s1045.Field.Pipes.Single(p => p.Direction.Text == "SW");

        var drawn = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        DipWorkflow.RecordDrafted(project, drawn);
        ConnectionFinder.LeaveUnresolved(project, s1045, sw);

        Assert.Empty(DipWorkflow.ReadyToDraw(project, null));

        north.MeasuredDip = 6.60;
        Assert.Single(DipWorkflow.ReadyToDraw(project, null));
    }

    // ========================================================= label state

    [Fact]
    public void AMovedLabel_IsRememberedAndReadsAsMoved()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var connection = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        DipWorkflow.RecordDrafted(project, connection);

        connection.LabelLocation = new LabelPlacement(512.5, 1090.25, 1.5707963);

        var state = DipWorkflow.Describe(project, s1045, north, null);
        Assert.Equal(PipeLabelState.LabelMoved, state.LabelState);
        Assert.True(connection.LabelWasPlacedByDrafter);
    }

    /// <summary>The whole point of item 15: a redraw must not send the label back to
    /// the midpoint. The placement is project data, so it survives the round trip.</summary>
    [Fact]
    public void AMovedLabel_SurvivesSaveAndReopen()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var connection = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        DipWorkflow.RecordDrafted(project, connection);
        connection.LabelLocation = new LabelPlacement(512.5, 1090.25, 0.7853981);

        var reopened = UtilityProject.FromJson(project.ToJson());
        var reloaded = reopened.Connections.Single();

        Assert.NotNull(reloaded.LabelLocation);
        Assert.Equal(512.5, reloaded.LabelLocation.X, 4);
        Assert.Equal(1090.25, reloaded.LabelLocation.Y, 4);
        Assert.Equal(0.7853981, reloaded.LabelLocation.RotationRadians, 6);
    }

    [Fact]
    public void AnEditedLabel_KeepsTheGeneratedTextBesideIt()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var connection = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        DipWorkflow.RecordDrafted(project, connection);

        var generated = UtilityLabelFormatter.PipeLabel(project, connection, Settings);
        connection.LabelTextOverrodeGenerated = generated;
        connection.LabelTextOverride = "12\" RCP SD @ 0.51% (FIELD VERIFY)";

        var state = DipWorkflow.Describe(project, s1045, north, null);

        Assert.Equal(PipeLabelState.LabelTextOverridden, state.LabelState);
        Assert.Equal(generated, connection.LabelTextOverrodeGenerated);
        Assert.True(connection.LabelTextIsOverridden);
    }

    [Fact]
    public void AnEditedLabel_SurvivesSaveAndReopen()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var connection = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        connection.LabelTextOverride = "12\" RCP SD FIELD VERIFY";

        var reloaded = UtilityProject.FromJson(project.ToJson()).Connections.Single();

        Assert.Equal("12\" RCP SD FIELD VERIFY", reloaded.LabelTextOverride);
        Assert.True(reloaded.LabelTextIsOverridden);
    }

    [Fact]
    public void StaleDrafting_MarksTheLabelOutOfDateToo()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var connection = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        DipWorkflow.RecordDrafted(project, connection);

        north.WidthIn = 15;

        Assert.Equal(PipeLabelState.LabelNeedsUpdate,
                     DipWorkflow.Describe(project, s1045, north, null).LabelState);
    }

    // ==================================================== unresolved is valid

    [Fact]
    public void LeftUnresolved_IsASettledStateWithNothingToDoNext()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);

        ConnectionFinder.LeaveUnresolved(project, s1045, north);
        var state = DipWorkflow.Describe(project, s1045, north, null);

        Assert.Equal(PipeConnectionState.LeftUnresolved, state.ConnectionState);
        Assert.Equal(PipeDraftingState.NotReady, state.DraftingState);
        Assert.Null(state.NextAction);
    }

    [Fact]
    public void OutsideSurveyLimits_IsSettledAndNotAFieldQuestion()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);

        ConnectionFinder.MarkOutsideLimits(project, s1045, north);
        var state = DipWorkflow.Describe(project, s1045, north, null);

        Assert.Equal(PipeConnectionState.OutsideSurveyLimits, state.ConnectionState);
        Assert.Null(state.NextAction);
    }

    [Fact]
    public void AnUnresolvedPipe_CanStillBeConnectedLater()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);

        ConnectionFinder.LeaveUnresolved(project, s1045, north);
        ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);

        Assert.Single(project.Connections);
        Assert.Equal(PipeConnectionState.DrafterSelected,
                     DipWorkflow.Describe(project, s1045, north, null).ConnectionState);
    }

    // =================================================== structure completion

    [Fact]
    public void AStructureWithNoObservations_IsNotComplete()
    {
        var s = Structure("PT 1060 SDMH", 330.0, 900, 400);
        var project = Project(s);

        Assert.Equal(StructureStatus.NoObservations, DipWorkflow.StatusOf(project, s, null));
    }

    [Fact]
    public void ObservationsWithNoConnections_NeedsConnections()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);

        Assert.Equal(StructureStatus.NeedsConnections, DipWorkflow.StatusOf(project, s1045, null));
    }

    [Fact]
    public void EverythingSettledButNotDrawn_IsReadyToDraw()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var sw = s1045.Field.Pipes.Single(p => p.Direction.Text == "SW");

        ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        ConnectionFinder.MarkOutsideLimits(project, s1045, sw);

        Assert.Equal(StructureStatus.ReadyToDraw, DipWorkflow.StatusOf(project, s1045, null));
    }

    [Fact]
    public void EverythingSettledAndDrawn_IsComplete()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var sw = s1045.Field.Pipes.Single(p => p.Direction.Text == "SW");

        var c = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        DipWorkflow.RecordDrafted(project, c);
        ConnectionFinder.MarkOutsideLimits(project, s1045, sw);

        Assert.Equal(StructureStatus.Complete, DipWorkflow.StatusOf(project, s1045, null));
    }

    /// <summary>Filling every box in is not completion. One pipe still unexamined
    /// keeps the whole structure out of Complete.</summary>
    [Fact]
    public void OnePipeStillUnexamined_KeepsTheStructureOutOfComplete()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);

        var c = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        DipWorkflow.RecordDrafted(project, c);
        // the SW pipe is left untouched

        Assert.Equal(StructureStatus.NeedsConnections, DipWorkflow.StatusOf(project, s1045, null));
    }

    [Fact]
    public void StaleDrafting_ShowsOnTheStructureStatus()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        var sw = s1045.Field.Pipes.Single(p => p.Direction.Text == "SW");

        var c = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        DipWorkflow.RecordDrafted(project, c);
        ConnectionFinder.MarkOutsideLimits(project, s1045, sw);
        north.Material = "DIP";

        Assert.Equal(StructureStatus.DrawingStale, DipWorkflow.StatusOf(project, s1045, null));
    }

    [Fact]
    public void AFieldRevisitFinding_OutranksEverythingElse()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var findings = new List<QcFinding>
        {
            new QcFinding { StructureId = s1045.Id, Severity = Severity.Warning, NeedsFieldRevisit = true,
                            Message = "Could not dip the south pipe" }
        };

        Assert.Equal(StructureStatus.FieldRevisit, DipWorkflow.StatusOf(project, s1045, findings));
    }

    // ============================================== the whole session, in order

    /// <summary>
    /// The production sequence end to end: 1045 -> connect north to 1048 -> draw ->
    /// open 1048 -> its opposite observation is already there -> slope appears ->
    /// Back returns to 1045. This is the test that fails if the workflow stops
    /// feeling like walking the network.
    /// </summary>
    [Fact]
    public void WalkingTheNetwork_FromOneStructureToTheNextAndBack()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var nav = new DipNavigator();

        // 1. open 1045
        nav.Open(s1045.Id);
        Assert.Equal(StructureStatus.NeedsConnections, DipWorkflow.StatusOf(project, s1045, null));

        // 2-5. select the north pipe and click 1048, which the drafter knows it runs to
        var north = NorthPipe(s1045);
        var connection = ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);
        Assert.Equal(PipeConnectionState.DrafterSelected, DipWorkflow.StateOf(connection));

        // 6. draw pipe + label
        DipWorkflow.RecordDrafted(project, connection);
        Assert.Equal(PipeDraftingState.Drawn,
                     DipWorkflow.Describe(project, s1045, north, null).DraftingState);

        // 7. open 1048 straight from the connection
        var next = DipWorkflow.Describe(project, s1045, north, null).ConnectedTo;
        nav.Open(next.Id);
        Assert.Equal(s1048.Id, nav.Current);

        // 8-10. the opposite observation is recognised and the slope is supported
        var opposite = s1048.Field.Pipes.Single();
        var backState = DipWorkflow.Describe(project, s1048, opposite, null);
        Assert.NotNull(backState.Connection);
        Assert.Equal(connection.Id, backState.Connection.Id);
        Assert.True(backState.BothEndsObserved);
        Assert.Equal(SlopeBasis.CalculatedFromBothObservations, backState.Slope.Basis);
        Assert.Equal(0.51, backState.Slope.SlopePercent.Value, 2);
        Assert.Equal(183.42, backState.Slope.HorizontalDistance, 2);

        // 12. Back returns to 1045
        Assert.Equal(s1045.Id, nav.Back());
    }

    /// <summary>The far structure sees the same connection from its own side, so
    /// arriving at 1048 does not look like starting a new record.</summary>
    [Fact]
    public void TheFarStructure_SeesTheSameConnectionFromItsSide()
    {
        StructureRecord s1045, s1048;
        var project = Network(out s1045, out s1048);
        var north = NorthPipe(s1045);
        ConnectionFinder.Accept(project, s1045, north,
            ConnectionFinder.ManualCandidate(project, s1045, north, s1048, Settings), true, null);

        var farSide = DipWorkflow.Describe(project, s1048, s1048.Field.Pipes.Single(), null);

        Assert.NotNull(farSide.Connection);
        Assert.Equal(s1045.Id, farSide.Connection.FromStructureId);
    }
}
