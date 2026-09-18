using FieldCodes;
using FieldCodes.Settings;
using FieldCodes.Utilities;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// The storm / sewer dip builder's calculation core. Field observations stay
/// exactly as written; elevations, connections and slopes are derived and always
/// carry their inputs; nothing missing is ever invented.
/// </summary>
public sealed class DipBuilderTests
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

    // ============================================================ parsing

    private const string Example = "PT 1045 SDMH\nBOT 7.82\nWL 6.94\n12 RCP N 6.41\n18 RCP SW 7.02\n8 PVC E 5.73";

    [Fact]
    public void TheExampleNoteParsesIntoItsObservations()
    {
        var result = new DipNoteParser(Settings).Parse(Example);

        Assert.False(result.HasErrors);
        var s = Assert.Single(result.Structures);
        Assert.Equal("1045", s.PointNumber);
        Assert.Equal("SDMH", s.FieldCode);
        Assert.Equal(7.82, s.BottomDip);
        Assert.Equal(6.94, s.WaterDip);
        Assert.Equal(3, s.Pipes.Count);

        var north = s.Pipes[0];
        Assert.Equal(12, north.WidthIn);
        Assert.Equal(PipeShape.Round, north.Shape);
        Assert.Equal("RCP", north.Material);
        Assert.Equal(0.0, north.Direction.AzimuthDegrees);
        Assert.Equal(6.41, north.MeasuredDip);
        Assert.Equal(ObservationSource.FieldNote, north.Source);
        Assert.Equal("12 RCP N 6.41", north.RawText);
    }

    [Fact]
    public void ElevationsAreRimMinusDip_AndTheMeasuredDipIsKept()
    {
        var s = Structure(Example, 328.42, 5000, 5000);

        Assert.Equal(320.60, DipElevations.Bottom(s)!.Value, 6);
        Assert.Equal(321.48, DipElevations.Water(s)!.Value, 6);

        var north = DipElevations.Pipe(s, s.Field.Pipes[0])!;
        Assert.Equal(322.01, north.Value, 6);
        Assert.Equal(328.42, north.RimUsed);
        Assert.Equal(6.41, north.DipUsed);
        Assert.Contains("rim 328.42 - dip 6.41", north.Formula);

        // The observation itself is untouched by the calculation.
        Assert.Equal(6.41, s.Field.Pipes[0].MeasuredDip);
    }

    [Fact]
    public void AnUnmarkedDipIsKeptExactlyAndIsTheInvertByTheOfficeDefault()
    {
        var s = Structure(Example, 328.42, 0, 0);
        Assert.All(s.Field.Pipes, p =>
        {
            Assert.Equal(MeasurementReference.Invert, p.Reference);
            Assert.Equal(ReferenceBasis.FieldNoteConvention, p.ReferenceBasis);
        });
        Assert.Equal(6.41, s.Field.Pipes[0].MeasuredDip);
    }

    [Fact]
    public void ATopOfPipeDipIsNeverTreatedAsAnInvert()
    {
        var s = Structure("PT 7 CB\n24 RCP W 4.00 TOP", 100.00, 0, 0);
        var pipe = s.Field.Pipes[0];

        Assert.Equal(MeasurementReference.TopOfPipe, pipe.Reference);
        Assert.Equal(ReferenceBasis.StatedInFieldNote, pipe.ReferenceBasis);

        double? invert, crown;
        DipElevations.Envelope(s, pipe, out invert, out crown);
        Assert.Null(invert);                       // not derived from the crown
        Assert.Equal(96.00, crown!.Value, 6);
        Assert.Equal(MeasurementReference.TopOfPipe, DipElevations.Pipe(s, pipe)!.Reference);
    }

    [Theory]
    [InlineData("N", 0.0)]
    [InlineData("NE", 45.0)]
    [InlineData("SW", 225.0)]
    [InlineData("NW", 315.0)]
    [InlineData("N45.3000E", 45.5)]
    [InlineData("S10W", 190.0)]
    [InlineData("AZ215", 215.0)]
    [InlineData("@90", 90.0)]
    public void DirectionsAcceptCardinalsBearingsAndAzimuths(string token, double azimuth)
    {
        var d = DipNoteParser.ParseDirection(token)!;
        Assert.True(d.IsKnown);
        Assert.Equal(azimuth, d.AzimuthDegrees!.Value, 6);
    }

    [Fact]
    public void ABoxCulvertKeepsItsWidthAndHeight()
    {
        var s = Structure("PT 88 HEADWALL\n48X36 BOX CONC E 3.10", 50, 0, 0, UtilitySystem.Culvert);
        var pipe = s.Field.Pipes[0];

        Assert.Equal(PipeShape.Box, pipe.Shape);
        Assert.Equal(48, pipe.WidthIn);
        Assert.Equal(36, pipe.HeightIn);
        Assert.Equal("48\"x36\"", UtilityLabelFormatter.FormatSize(pipe));

        // Drafting uses the observed WIDTH.
        Assert.True(PipeDraftingRules.DrawDoubleLine(pipe, Settings));
        Assert.Equal(2.0, PipeDraftingRules.HalfWidthFeet(pipe), 6);
    }

    [Fact]
    public void TheDoubleLineThresholdIsInclusiveOfTwelveInches()
    {
        Assert.False(PipeDraftingRules.DrawDoubleLine(new PipeObservation { WidthIn = 12 }, Settings));
        Assert.True(PipeDraftingRules.DrawDoubleLine(new PipeObservation { WidthIn = 15 }, Settings));
        Assert.True(PipeDraftingRules.DrawDoubleLine(new PipeObservation { WidthIn = 60 }, Settings));
    }

    [Fact]
    public void MalformedNotesAreReported_NeverGuessed()
    {
        var result = new DipNoteParser(Settings).Parse(
            "12 RCP N 4.0\n" +              // before any header
            "PT 9 CB\n" +
            "BOT\n" +                        // no value
            "12 RCP N -3.0\n" +             // negative dip
            "12 XYZ Q 4.5 5.5\n" +          // unknown material, no direction, second number
            "LID STUCK");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("before any"));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("bottom line has no readable dip"));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("negative dip"));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("No recognised material"));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("No readable direction"));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("second number"));

        var s = result.Structures[0];
        Assert.Null(s.BottomDip);
        Assert.Null(s.Pipes[0].MeasuredDip);             // the negative dip was refused
        Assert.Null(s.Pipes[1].Material);                  // not assumed
        Assert.Contains("LID STUCK", s.Notes);
    }

    [Fact]
    public void ConditionsAndRolesAreRead()
    {
        var s = Structure("PT 3 SDMH\n15 CPEP S 6.00 OUT SILTED\n12 RCP N UTD", 200, 0, 0);
        Assert.Equal(FlowRole.Out, s.Field.Pipes[0].Role);
        Assert.True(s.Field.Pipes[0].HasCondition("SILTED"));
        Assert.True(s.Field.Pipes[1].HasCondition("UNABLE TO DIP"));
        Assert.Null(s.Field.Pipes[1].MeasuredDip);
    }

    // ======================================================== connections

    private static (UtilityProject, StructureRecord, StructureRecord) MatchedPair(
        string aNotes = "PT 1045 SDMH\n12 RCP E 5.00 INV OUT",
        string bNotes = "PT 1088 CB\n12 RCP W 5.50 INV IN",
        double bEasting = 100.0)
    {
        var a = Structure(aNotes, 110.00, 1000, 1000);
        var b = Structure(bNotes, 110.00, 1000, 1000 + bEasting);
        return (Project(a, b), a, b);
    }

    [Fact]
    public void ASingleStructureHasNoCandidates()
    {
        var a = Structure("PT 1 SDMH\n12 RCP E 5.0", 100, 0, 0);
        Assert.Empty(ConnectionFinder.Find(Project(a), a, a.Field.Pipes[0], Settings));
    }

    [Fact]
    public void MatchingDipsAtBothStructuresGiveAHighConfidenceConnection()
    {
        var (project, a, b) = MatchedPair();
        var candidates = ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings);

        var best = Assert.Single(candidates);
        Assert.Equal(b.Id, best.Structure.Id);
        Assert.Equal(Confidence.High, best.Confidence);
        Assert.Equal(100.0, best.Distance, 6);
        Assert.Equal(b.Field.Pipes[0].Id, best.MatchingPipe!.Id);
        Assert.Contains(best.Basis, l => l.StartsWith("Matching field observation: 12\" RCP W"));
    }

    [Fact]
    public void SlopeIsCalculatedOnlyFromBothObservations()
    {
        var (project, a, b) = MatchedPair();
        var c = ConnectionFinder.Accept(project, a, a.Field.Pipes[0],
            ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings)[0], false, null);

        var slope = SlopeCalculator.Compute(project, c);
        Assert.Equal(SlopeBasis.CalculatedFromBothObservations, slope.Basis);
        Assert.Equal(0.50, slope.SlopePercent!.Value, 6);               // (105.00 - 104.50) / 100
        Assert.True(slope.SignedPercentFromTo < 0);                      // falls toward 1088
        Assert.Contains("Calculated from field dips at both structures", slope.Explanation);
        Assert.Equal(ConnectionStatus.Confirmed, c.Status);
    }

    [Fact]
    public void AMissingOppositeDipIsSaid_AndNoSlopeIsManufactured()
    {
        var (project, a, b) = MatchedPair(bNotes: "PT 1088 CB\n8 PVC N 3.00");
        var candidates = ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings);

        var best = Assert.Single(candidates);
        Assert.Null(best.MatchingPipe);
        Assert.Equal(Confidence.Low, best.Confidence);
        Assert.Contains(best.Basis, l => l.Contains("no corresponding field dip observed"));

        var c = ConnectionFinder.Accept(project, a, a.Field.Pipes[0], best, false, null);
        Assert.Null(c.ToPipeId);                                          // nothing invented

        var slope = SlopeCalculator.Compute(project, c);
        Assert.Equal(SlopeBasis.MissingOppositeObservation, slope.Basis);
        Assert.Null(slope.SlopePercent);

        var findings = UtilityQc.Evaluate(project, Settings);
        Assert.Contains(findings, f => f.Code == QcCode.MissingOppositePipe && f.NeedsFieldRevisit);
    }

    [Fact]
    public void AWrongDirectionFindsNothing()
    {
        var (project, a, _) = MatchedPair(aNotes: "PT 1045 SDMH\n12 RCP N 5.00");
        Assert.Empty(ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings));
    }

    [Fact]
    public void AnUnknownDirectionIsNeverConnectedByGeometryAlone()
    {
        var (project, a, _) = MatchedPair(aNotes: "PT 1045 SDMH\n12 RCP ? 5.00");
        Assert.Empty(ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings));
        Assert.Contains(UtilityQc.Evaluate(project, Settings), f => f.Code == QcCode.UnknownDirection);
    }

    [Fact]
    public void DifferentSizesAndMaterialsLowerConfidence_AndAreFlagged()
    {
        var (project, a, _) = MatchedPair(bNotes: "PT 1088 CB\n18 PVC W 5.50");
        var best = ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings)[0];
        Assert.Equal(Confidence.Medium, best.Confidence);

        ConnectionFinder.Accept(project, a, a.Field.Pipes[0], best, false, null);
        var findings = UtilityQc.Evaluate(project, Settings);
        Assert.Contains(findings, f => f.Code == QcCode.SizeMismatch);
        Assert.Contains(findings, f => f.Code == QcCode.MaterialMismatch);
    }

    [Fact]
    public void AReverseSlopeAgainstTheNotedFlowIsFlagged()
    {
        // OUT of 1045 but 1088's invert is higher: water would run backwards.
        var (project, a, b) = MatchedPair(aNotes: "PT 1045 SDMH\n12 RCP E 6.00 INV OUT",
                                          bNotes: "PT 1088 CB\n12 RCP W 5.00 INV IN");
        ConnectionFinder.Accept(project, a, a.Field.Pipes[0],
            ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings)[0], false, null);

        Assert.Contains(UtilityQc.Evaluate(project, Settings), f => f.Code == QcCode.ReverseFlow);
    }

    [Fact]
    public void AnExtremeSlopeIsFlaggedButNotBlocked()
    {
        var (project, a, _) = MatchedPair(aNotes: "PT 1045 SDMH\n12 RCP E 2.00 INV",
                                          bNotes: "PT 1088 CB\n12 RCP W 30.00 INV");
        ConnectionFinder.Accept(project, a, a.Field.Pipes[0],
            ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings)[0], false, null);

        var finding = Assert.Single(UtilityQc.Evaluate(project, Settings), f => f.Code == QcCode.SlopeOutOfRange);
        Assert.False(finding.BlocksDrafting);
    }

    [Fact]
    public void SlopeLimitsAreConfigurablePerSystem()
    {
        var settings = new UtilitySettings();
        settings.Standard(UtilitySystem.Storm).MaxSlopePercent = 50;
        var (project, a, _) = MatchedPair(aNotes: "PT 1045 SDMH\n12 RCP E 2.00 INV",
                                          bNotes: "PT 1088 CB\n12 RCP W 30.00 INV");
        ConnectionFinder.Accept(project, a, a.Field.Pipes[0],
            ConnectionFinder.Find(project, a, a.Field.Pipes[0], settings)[0], false, null);

        Assert.DoesNotContain(UtilityQc.Evaluate(project, settings), f => f.Code == QcCode.SlopeOutOfRange);
    }

    [Fact]
    public void ASubmergedStructureIsFlagged()
    {
        // 12" invert at 95.00, crown 96.00, water at 97.00.
        var s = Structure("PT 5 SDMH\nWL 3.00\n12 RCP E 5.00 INV", 100.00, 0, 0);
        Assert.Contains(UtilityQc.Evaluate(Project(s), Settings), f => f.Code == QcCode.SubmergedPipe);
    }

    [Fact]
    public void AnInvertBelowTheStructureBottomIsFlagged()
    {
        var s = Structure("PT 5 SDMH\nBOT 6.00\n12 RCP E 6.40 INV", 100.00, 0, 0);
        Assert.Contains(UtilityQc.Evaluate(Project(s), Settings), f => f.Code == QcCode.InvertBelowBottom);
    }

    [Fact]
    public void ABlockedPipeGoesOnTheFieldRevisitList()
    {
        var s = Structure("PT 1452 SDMH\n12 RCP N 5.00 BLOCKED", 100.00, 0, 0);
        var findings = UtilityQc.Evaluate(Project(s), Settings);

        Assert.Contains(findings, f => f.Code == QcCode.BlockedPipe && f.NeedsFieldRevisit);
        var revisit = UtilityQc.FieldRevisitText(Project(s), findings);
        Assert.StartsWith("SDMH 1452", revisit);
        Assert.Contains("blocked", revisit);
    }

    [Fact]
    public void AnUnreadableNoteLineIsKeptAndSentToTheFieldRevisitList()
    {
        var s = Structure("PT 1046 CB\n12 RCP S 7.20\nBOT\nWL ???", 330.10, 0, 0);

        Assert.Equal(2, s.Field.NoteProblems.Count);
        Assert.Contains("\"BOT\"", s.Field.NoteProblems[0]);
        Assert.Null(s.Field.BottomDip);   // nothing is invented for the unreadable line

        var findings = UtilityQc.Evaluate(Project(s), Settings);
        Assert.Equal(2, findings.Count(f => f.Code == QcCode.MalformedNote && f.NeedsFieldRevisit));
        Assert.Contains("WL ???", UtilityQc.FieldRevisitText(Project(s), findings));
    }

    [Fact]
    public void APipeNobodyHasConnectedIsFlaggedAsOfficeWorkNotAFieldQuestion()
    {
        var s = Structure("PT 7 SDMH\n12 RCP N 5.00", 100, 0, 0);
        var finding = Assert.Single(UtilityQc.Evaluate(Project(s), Settings), f => f.Code == QcCode.UnresolvedConnection);
        Assert.False(finding.NeedsFieldRevisit);
    }

    [Fact]
    public void APipeLeftUnresolvedOnPurposeGoesToTheFieldRevisitList()
    {
        var s = Structure("PT 2000 SSMH\n8 PVC N TOP 9.00", 329.50, 0, 0);
        var project = Project(s);
        ConnectionFinder.LeaveUnresolved(project, s, s.Field.Pipes[0]);

        var findings = UtilityQc.Evaluate(project, Settings);
        var finding = Assert.Single(findings, f => f.Code == QcCode.UnresolvedConnection);
        Assert.True(finding.NeedsFieldRevisit);
        Assert.Contains("trace the run", UtilityQc.FieldRevisitText(project, findings));
    }

    [Fact]
    public void ADuplicateEntryIsFlagged()
    {
        var s = Structure("PT 12 CB\n12 RCP E 4.00\n12 RCP E 4.02", 100.00, 0, 0);
        Assert.Contains(UtilityQc.Evaluate(Project(s), Settings), f => f.Code == QcCode.DuplicatePipe);
    }

    [Fact]
    public void AnObservationWithTheReciprocalOutranksACloserStructureWithout()
    {
        // Two structures due east: 1088 at 60' with no dip back, 1090 at 150' with one.
        var a = Structure("PT 1045 SDMH\n12 RCP E 5.00", 110, 1000, 1000);
        var near = Structure("PT 1088 CB\n8 PVC N 2.0", 110, 1000, 1060);
        var far = Structure("PT 1090 SDMH\n12 RCP W 5.90", 110, 1003, 1150);
        var candidates = ConnectionFinder.Find(Project(a, near, far), a, a.Field.Pipes[0], Settings);

        Assert.Equal(2, candidates.Count);
        Assert.Equal(far.Id, candidates[0].Structure.Id);
        Assert.Equal(near.Id, candidates[1].Structure.Id);
    }

    [Fact]
    public void AManualOverrideIsRecordedAsOne()
    {
        var a = Structure("PT 1045 SDMH\n12 RCP E 5.00", 110, 1000, 1000);
        var north = Structure("PT 1200 SDMH\n12 RCP S 5.40", 110, 1100, 1000);   // outside the east cone
        var project = Project(a, north);

        var candidate = ConnectionFinder.ManualCandidate(project, a, a.Field.Pipes[0], north, Settings);
        var c = ConnectionFinder.Accept(project, a, a.Field.Pipes[0], candidate, true, "per as-built sheet 4");

        Assert.Equal(ConnectionStatus.ManualOverride, c.Status);
        Assert.Contains(c.Basis, l => l.Contains("Outside the search"));
        Assert.Contains(c.Basis, l => l.Contains("chosen manually"));
        Assert.Contains(project.Overrides, o => o.Target == c.Id && o.What == "Manual connection");
        Assert.Contains(UtilityQc.Evaluate(project, Settings), f => f.Code == QcCode.DirectionMismatch);
    }

    [Fact]
    public void AProbableConnectionIsNeverSilentlyConfirmed()
    {
        var (project, a, _) = MatchedPair();
        ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings);
        Assert.Empty(project.Connections);                                // searching confirms nothing
        Assert.Equal(2, UtilityQc.Summarize(project, UtilityQc.Evaluate(project, Settings), 0).UnresolvedConnections);
    }

    // ============================================================ labels

    [Fact]
    public void ThePipeLabelFollowsTheProfileFormat()
    {
        var (project, a, _) = MatchedPair(aNotes: "PT 1045 SDMH\n18 RCP E 5.00 INV", bNotes: "PT 1088 CB\n18 RCP W 5.67 INV");
        var c = ConnectionFinder.Accept(project, a, a.Field.Pipes[0],
            ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings)[0], false, null);

        Assert.Equal("18\" RCP SD @ 0.67%", UtilityLabelFormatter.PipeLabel(project, c, Settings));
    }

    [Fact]
    public void WithoutASlopeThePipeLabelDropsTheSlopePart()
    {
        var (project, a, _) = MatchedPair(bNotes: "PT 1088 CB\n8 PVC N 3.00");
        var c = ConnectionFinder.Accept(project, a, a.Field.Pipes[0],
            ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings)[0], false, null);

        Assert.Equal("12\" RCP SD", UtilityLabelFormatter.PipeLabel(project, c, Settings));
    }

    [Fact]
    public void TheStructureLabelMatchesTheExampleLayout()
    {
        var s = Structure("PT 1045 SDMH\nBOT 7.82\nWL 6.94\n12 RCP N 6.41 INV OUT\n8 PVC E 5.73 INV IN\n18 RCP SW 7.02 INV IN",
                          328.42, 0, 0);
        var lines = UtilityLabelFormatter.StructureLabel(Project(s), s, Settings);

        Assert.Equal(new[]
        {
            "SDMH 1045",
            "RIM = 328.42",
            "IE IN (E) = 322.69 8\" PVC",
            "IE IN (SW) = 321.40 18\" RCP",
            "IE OUT (N) = 322.01 12\" RCP",
            "BOT = 320.60",
            "WL = 321.48"
        }, lines);
    }

    [Fact]
    public void AnUndippedPipeSaysSoInTheStructureLabel()
    {
        var s = Structure("PT 9 CB\n12 RCP N UTD", 100, 0, 0);
        Assert.Contains("(N) 12\" RCP - NOT DIPPED", UtilityLabelFormatter.StructureLabel(Project(s), s, Settings));
    }

    // ===================================================== review & staleness

    [Fact]
    public void TheReviewSummaryCountsWhatNeedsAttention()
    {
        var (project, a, _) = MatchedPair(bNotes: "PT 1088 CB\n8 PVC N 3.00\n12 RCP W");
        ConnectionFinder.Accept(project, a, a.Field.Pipes[0],
            ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings)[0], false, null);

        var summary = UtilityQc.Summarize(project, UtilityQc.Evaluate(project, Settings), 0);
        Assert.Equal(2, summary.Structures);
        Assert.Equal(3, summary.Pipes);
        Assert.True(summary.Warnings > 0);
        // 1088's west pipe is the matched far end of the accepted run; only its
        // north pipe is still unresolved.
        Assert.Equal(1, summary.UnresolvedConnections);
    }

    [Fact]
    public void AChangedRimMakesTheStructureStale_AndNothingIsChanged()
    {
        var (project, a, b) = MatchedPair();
        var live = new Dictionary<string, CadStructureSnapshot>
        {
            { "1045", new CadStructureSnapshot { PointNumber = "1045", Rim = 110.25, Northing = 1000, Easting = 1000 } },
            { "1088", new CadStructureSnapshot { PointNumber = "1088", Rim = 110.00, Northing = 1000, Easting = 1100 } }
        };

        var stale = UtilityQc.StaleStructures(project, live);
        Assert.Equal(a.Id, Assert.Single(stale).Id);
        Assert.Equal(110.00, a.Cad!.Rim);                                  // not silently updated
    }

    [Fact]
    public void ProvenanceTracesACalculatedInvertToItsFieldNote()
    {
        var (project, a, _) = MatchedPair();
        var c = ConnectionFinder.Accept(project, a, a.Field.Pipes[0],
            ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings)[0], false, null);

        var text = UtilityProvenance.ForConnection(project, c, UtilityQc.Evaluate(project, Settings), Settings);
        Assert.Contains("CAD rim 110.00", text);
        Assert.Contains("Field note: 12 RCP E 5.00 INV OUT", text);
        Assert.Contains("invert = rim 110.00 - dip 5.00 = 105.00", text);
        Assert.Contains("Confirmed (High confidence)", text);
        Assert.Contains("Calculated from field dips at both structures", text);
    }

    [Fact]
    public void TheProjectRoundTripsWithoutLosingObservations()
    {
        var (project, a, _) = MatchedPair();
        ConnectionFinder.Accept(project, a, a.Field.Pipes[0],
            ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings)[0], false, null);

        var json = project.ToJson();
        var chunks = TextChunks.Split(json);
        Assert.All(chunks, c => Assert.True(c.Length <= TextChunks.Size));
        var back = UtilityProject.FromJson(TextChunks.Join(chunks));

        Assert.Equal(json, back.ToJson());
        Assert.Equal(5.00, back.Structures[0].Field.Pipes[0].MeasuredDip);
        Assert.Equal(ReferenceBasis.StatedInFieldNote, back.Structures[0].Field.Pipes[0].ReferenceBasis);
        Assert.Equal(ConnectionStatus.Confirmed, back.Connections[0].Status);
    }

    [Fact]
    public void NoCalculationOrQcPassModifiesAFieldObservation()
    {
        var (project, a, b) = MatchedPair(bNotes: "PT 1088 CB\n18 PVC W 9.50 INV IN SILTED\nBOT 12.0");
        var before = JsonConvert.SerializeObject(project.Structures.Select(s => s.Field).ToList());

        var best = ConnectionFinder.Find(project, a, a.Field.Pipes[0], Settings)[0];
        var c = ConnectionFinder.Accept(project, a, a.Field.Pipes[0], best, false, null);
        SlopeCalculator.Compute(project, c);
        var findings = UtilityQc.Evaluate(project, Settings);
        UtilityQc.FieldRevisitText(project, findings);
        UtilityLabelFormatter.StructureLabel(project, b, Settings);
        UtilityProvenance.ForConnection(project, c, findings, Settings);

        Assert.Equal(before, JsonConvert.SerializeObject(project.Structures.Select(s => s.Field).ToList()));
    }

    [Fact]
    public void CrossingStormAndSanitaryWithLittleClearanceIsFlagged()
    {
        // Storm runs east at invert ~95; sanitary runs north under it at invert ~94.5.
        var s1 = Structure("PT 1 SDMH\n12 RCP E 5.00 INV", 100, 1000, 1000);
        var s2 = Structure("PT 2 SDMH\n12 RCP W 5.00 INV", 100, 1000, 1100);
        var t1 = Structure("PT 3 SSMH\n8 PVC N 5.50 INV", 100, 950, 1050, UtilitySystem.Sanitary);
        var t2 = Structure("PT 4 SSMH\n8 PVC S 5.50 INV", 100, 1050, 1050, UtilitySystem.Sanitary);
        var project = Project(s1, s2, t1, t2);

        ConnectionFinder.Accept(project, s1, s1.Field.Pipes[0], ConnectionFinder.Find(project, s1, s1.Field.Pipes[0], Settings)[0], false, null);
        ConnectionFinder.Accept(project, t1, t1.Field.Pipes[0], ConnectionFinder.Find(project, t1, t1.Field.Pipes[0], Settings)[0], false, null);

        Assert.Contains(UtilityQc.Evaluate(project, Settings), f => f.Code == QcCode.CrossingClearance);
    }
}
