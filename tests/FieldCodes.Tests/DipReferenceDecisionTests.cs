using FieldCodes.Settings;
using FieldCodes.Utilities;

namespace FieldCodes.Tests;

/// <summary>
/// The office decisions on unmarked dips, QC warning thresholds and structure
/// sizes: an assumption is never turned into field evidence.
/// </summary>
public sealed class DipReferenceDecisionTests
{
    private static StructureRecord Structure(string notes, double rim, double northing, double easting,
                                             UtilitySettings? settings = null,
                                             UtilitySystem system = UtilitySystem.Storm)
    {
        var parsed = new DipNoteParser(settings ?? new UtilitySettings()).Parse(notes);
        var field = Assert.Single(parsed.Structures);
        return new StructureRecord
        {
            Field = field,
            StructureType = field.FieldCode,
            System = system,
            Cad = new CadStructureSnapshot { PointNumber = field.PointNumber, Rim = rim, Northing = northing, Easting = easting }
        };
    }

    private static (UtilityProject, StructureRecord, StructureRecord, PipeConnection) Connected(
        string aNotes, string bNotes, UtilitySettings? settings = null)
    {
        settings ??= new UtilitySettings();
        var a = Structure(aNotes, 110.00, 1000, 1000, settings);
        var b = Structure(bNotes, 110.00, 1000, 1100, settings);
        var project = new UtilityProject();
        project.Structures.Add(a);
        project.Structures.Add(b);
        var c = ConnectionFinder.Accept(project, a, a.Field.Pipes[0],
            ConnectionFinder.Find(project, a, a.Field.Pipes[0], settings)[0], false, null);
        return (project, a, b, c);
    }

    // ------------------------------------------------------------ unmarked dips

    [Fact]
    public void WithTheDefaultOffAnUnmarkedDipGivesNoSlopeAndAFlaggedLabel()
    {
        var settings = new UtilitySettings { UnmarkedDipsAreInvertsByConvention = false };
        var (project, a, _, c) = Connected("PT 1 SDMH\n12 RCP E 5.00", "PT 2 CB\n12 RCP W 5.50", settings);

        var slope = SlopeCalculator.Compute(project, c);
        Assert.Null(slope.SlopePercent);
        Assert.Contains("does not say what it was measured to", slope.Explanation);

        Assert.DoesNotContain("@", UtilityLabelFormatter.PipeLabel(project, c, settings));
        Assert.Contains("IE? (E) = 105.00 12\" RCP", UtilityLabelFormatter.StructureLabel(project, a, settings));

        var findings = UtilityQc.Evaluate(project, settings);
        Assert.Equal(2, findings.Count(f => f.Code == QcCode.UnconfirmedReference && !f.NeedsFieldRevisit));
    }

    [Fact]
    public void WithTheDefaultOffTheAssumedReferenceIsShownButNotUsedAsFact()
    {
        var settings = new UtilitySettings { UnmarkedDipsAreInvertsByConvention = false };
        var s = Structure("PT 1 SDMH\n12 RCP E 5.00", 110.00, 0, 0, settings);
        var assumed = ObservationReview.Assumed(s, s.Field.Pipes[0], settings)!;

        Assert.Equal(105.00, assumed.Value, 6);
        Assert.Contains("assumed invert (not confirmed)", assumed.Formula);
        double? invert, crown;
        DipElevations.Envelope(s, s.Field.Pipes[0], out invert, out crown);
        Assert.Null(invert);   // QC does not treat it as an invert
    }

    [Fact]
    public void WithTheDefaultOffConfirmingInvertKeepsTheMeasurement_AndEnablesTheSlope()
    {
        var settings = new UtilitySettings { UnmarkedDipsAreInvertsByConvention = false };
        var (project, a, b, c) = Connected("PT 1 SDMH\n12 RCP E 5.00", "PT 2 CB\n12 RCP W 5.50", settings);
        var pa = a.Field.Pipes[0];
        var pb = b.Field.Pipes[0];

        Assert.True(ObservationReview.ConfirmReference(project, a, pa, MeasurementReference.Invert));
        Assert.Null(SlopeCalculator.Compute(project, c).SlopePercent);   // the other end is still unconfirmed
        Assert.True(ObservationReview.ConfirmReference(project, b, pb, MeasurementReference.Invert));

        Assert.Equal(0.5, SlopeCalculator.Compute(project, c).SlopePercent!.Value, 6);
        Assert.Equal(5.00, pa.MeasuredDip);
        Assert.Equal(ObservationSource.FieldNote, pa.Source);
        Assert.Equal(ReferenceBasis.ConfirmedByDrafter, pa.ReferenceBasis);
        Assert.Equal("12 RCP E 5.00", pa.RawText);
        Assert.Contains(project.Overrides, o => o.Target == pa.Id && o.What == "Measurement reference confirmed");
        Assert.DoesNotContain(UtilityQc.Evaluate(project, settings), f => f.Code == QcCode.UnconfirmedReference);
    }

    [Fact]
    public void ADocumentedConventionClassifiesUnmarkedDips_AndSaysSo()
    {
        var settings = new UtilitySettings
        {
            UnmarkedDipsAreInvertsByConvention = true,
            UnmarkedDipConventionSource = "Office dip note standard rev A"
        };
        var s = Structure("PT 1 SDMH\n12 RCP E 5.00", 110.00, 0, 0, settings);
        var p = s.Field.Pipes[0];

        Assert.Equal(MeasurementReference.Invert, p.Reference);
        Assert.Equal(ReferenceBasis.FieldNoteConvention, p.ReferenceBasis);
        Assert.Contains("Office dip note standard rev A", p.ReferenceNote);
    }

    [Fact]
    public void TheConventionCannotBeSwitchedOnWithoutSayingWhereItIsDocumented()
    {
        var settings = new UtilitySettings { UnmarkedDipsAreInvertsByConvention = true, UnmarkedDipConventionSource = "" };
        var problems = new List<string>();
        settings.Validate(problems);
        Assert.Contains(problems, p => p.Contains("documented"));
    }

    [Fact]
    public void TopPipeWrittenAsTwoWordsIsTopOfPipe()
    {
        var p = Structure("PT 1 CB\n12 RCP N 4.10 TOP PIPE", 100, 0, 0).Field.Pipes[0];
        Assert.Equal(MeasurementReference.TopOfPipe, p.Reference);
        Assert.Null(p.Notes);
    }

    [Fact]
    public void FirstReleaseDataWithADefaultedReferenceLoadsAsUnspecified()
    {
        const string legacy = "{\"schema\":\"ftf-dips-1\",\"structures\":[{\"id\":\"s1\",\"field\":{\"pointNumber\":\"1\",\"pipes\":[" +
            "{\"id\":\"p1\",\"dip\":5.0,\"reference\":\"Invert\",\"referenceDefaulted\":true,\"source\":\"FieldNote\"}," +
            "{\"id\":\"p2\",\"dip\":4.0,\"reference\":\"TopOfPipe\",\"referenceDefaulted\":false,\"source\":\"FieldNote\"}]}}]}";

        var project = UtilityProject.FromJson(legacy);
        var pipes = project.Structures[0].Field.Pipes;

        Assert.Equal(MeasurementReference.Unspecified, pipes[0].Reference);
        Assert.Equal(5.0, pipes[0].MeasuredDip);
        Assert.Equal(MeasurementReference.TopOfPipe, pipes[1].Reference);
        Assert.Equal(ReferenceBasis.StatedInFieldNote, pipes[1].ReferenceBasis);
        Assert.Equal(UtilityProject.CurrentSchema, project.Schema);
        Assert.DoesNotContain("referenceDefaulted", project.ToJson());
    }

    [Fact]
    public void AnUnreadableSecondWaterLineDoesNotWipeTheReadableOne()
    {
        var s = Structure("PT 1 SDMH\nWL 6.94\n12 RCP N 6.41 INV\nWL ???", 100, 0, 0);
        Assert.Equal(6.94, s.Field.WaterDip);
        Assert.Single(s.Field.NoteProblems);
    }

    [Fact]
    public void ASecondDifferentBottomReadingIsReportedAndTheFirstKept()
    {
        var result = new DipNoteParser(new UtilitySettings()).Parse("PT 1 SDMH\nBOT 7.82\nBOT 8.10");
        Assert.Equal(7.82, result.Structures[0].BottomDip);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("differs from the first"));
    }

    /// <summary>The verification values from the AsBuilt Storm spec: invert = rim - dip,
    /// shown to two decimals.</summary>
    [Theory]
    [InlineData(214.36, 5.10, 209.26)] [InlineData(214.36, 5.35, 209.01)] [InlineData(214.36, 4.95, 209.41)]
    [InlineData(214.36, 5.42, 208.94)] [InlineData(196.42, 3.85, 192.57)] [InlineData(196.42, 3.90, 192.52)]
    [InlineData(301.77, 6.12, 295.65)] [InlineData(301.77, 6.30, 295.47)] [InlineData(112.08, 4.40, 107.68)]
    [InlineData(112.08, 4.28, 107.80)] [InlineData(88.90, 7.75, 81.15)] [InlineData(88.90, 7.60, 81.30)]
    [InlineData(55.10, 2.90, 52.20)] [InlineData(77.31, 3.10, 74.21)]
    public void InvertsMatchTheAsBuiltVerificationValues(double rim, double dip, double invert)
    {
        var s = Structure("PT 1 SDMH\n12 RCP N " + dip.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " INV", rim, 0, 0);
        var e = DipElevations.Pipe(s, s.Field.Pipes[0])!;
        Assert.Equal(invert.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                     e.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ByDefaultAnUnmarkedDipIsTheInvert_AndTheRecordSaysItCameFromTheOfficeDefault()
    {
        var (project, a, _, c) = Connected("PT 1 SDMH\n12 RCP E 5.00", "PT 2 CB\n12 RCP W 5.50");
        var p = a.Field.Pipes[0];
        Assert.Equal(MeasurementReference.Invert, p.Reference);
        Assert.Equal(ReferenceBasis.FieldNoteConvention, p.ReferenceBasis);
        Assert.Contains("unless marked top of pipe", p.ReferenceNote);
        Assert.Equal(5.00, p.MeasuredDip);
        Assert.Equal(0.5, SlopeCalculator.Compute(project, c).SlopePercent!.Value, 6);
        Assert.DoesNotContain(UtilityQc.Evaluate(project, new UtilitySettings()), f => f.Code == QcCode.UnconfirmedReference);
    }

    [Fact]
    public void TopStillMeansTopOfPipeUnderTheDefault()
    {
        var p = Structure("PT 1 CB\n12 RCP N 4.10 TOP", 100, 0, 0).Field.Pipes[0];
        Assert.Equal(MeasurementReference.TopOfPipe, p.Reference);
        Assert.Equal(ReferenceBasis.StatedInFieldNote, p.ReferenceBasis);
    }

    // ---------------------------------------------------------- QC thresholds

    [Fact]
    public void SlopeWarningsSayTheyAreWarningThresholdsNotRequirements()
    {
        var (project, _, _, _) = Connected("PT 1 SDMH\n12 RCP E 2.00 INV", "PT 2 CB\n12 RCP W 30.00 INV");
        var finding = Assert.Single(UtilityQc.Evaluate(project, new UtilitySettings()), f => f.Code == QcCode.SlopeOutOfRange);
        Assert.Contains("QC warning range", finding.Message);
        Assert.Contains("not a design requirement", finding.Message);
    }

    [Fact]
    public void TheDefaultThresholdsAreTheAgreedWarningValues()
    {
        var s = new UtilitySettings();
        var storm = s.Standard(UtilitySystem.Storm);
        var sanitary = s.Standard(UtilitySystem.Sanitary);
        Assert.Equal((0.3, 20.0, 1.0, true), (storm.MinSlopePercent, storm.MaxSlopePercent, storm.MinCoverFt, storm.SlopeCheckEnabled));
        Assert.Equal((0.4, 15.0, 3.0, true), (sanitary.MinSlopePercent, sanitary.MaxSlopePercent, sanitary.MinCoverFt, sanitary.CoverCheckEnabled));
        Assert.False(s.Standard(UtilitySystem.Culvert).SlopeCheckEnabled);   // no thresholds set yet
        Assert.False(s.Standard(UtilitySystem.Other).CoverCheckEnabled);
    }

    [Fact]
    public void ASystemsSlopeCheckCanBeSwitchedOff()
    {
        var settings = new UtilitySettings();
        settings.Standard(UtilitySystem.Storm).SlopeCheckEnabled = false;
        var (project, _, _, _) = Connected("PT 1 SDMH\n12 RCP E 2.00 INV", "PT 2 CB\n12 RCP W 30.00 INV", settings);
        Assert.DoesNotContain(UtilityQc.Evaluate(project, settings), f => f.Code == QcCode.SlopeOutOfRange);
    }

    [Fact]
    public void IndividualChecksCanBeDisabled_ButNotTheOnesThatStopInvalidGeometry()
    {
        var settings = new UtilitySettings();
        settings.DisabledChecks.Add(QcCode.MissingOppositePipe);
        settings.DisabledChecks.Add(QcCode.NoCadPoint);

        var (project, _, _, _) = Connected("PT 1 SDMH\n12 RCP E 5.00 INV", "PT 2 CB\n8 PVC N 3.00 INV", settings);
        project.Structures.Add(new StructureRecord { Field = new StructureObservation { PointNumber = "99" } });

        var findings = UtilityQc.Evaluate(project, settings);
        Assert.DoesNotContain(findings, f => f.Code == QcCode.MissingOppositePipe);
        Assert.Contains(findings, f => f.Code == QcCode.NoCadPoint);   // blocks drafting: always kept
    }

    // ------------------------------------------------------- structure sizes

    [Fact]
    public void WithNoKnownInsideSizeThePipeTooLargeCheckDoesNotRun()
    {
        var s = Structure("PT 1 CB\n60 RCP E 5.00 INV", 110, 0, 0);
        DimensionSource source;
        Assert.Null(StructureDimensions.InsideWidth(s, new UtilitySettings(), out source));
        var project = new UtilityProject();
        project.Structures.Add(s);
        Assert.DoesNotContain(UtilityQc.Evaluate(project, new UtilitySettings()), f => f.Code == QcCode.PipeTooLargeForStructure);
    }

    [Fact]
    public void AFieldObservedInsideSizeIsUsedAndLabelledFieldObserved()
    {
        var s = Structure("PT 1 CB\nID 24\n30 RCP E 5.00 INV", 110, 0, 0);
        var project = new UtilityProject();
        project.Structures.Add(s);
        var finding = Assert.Single(UtilityQc.Evaluate(project, new UtilitySettings()), f => f.Code == QcCode.PipeTooLargeForStructure);
        Assert.Contains("FIELD OBSERVED", finding.Message);
    }

    [Fact]
    public void AProfileStandardSizeIsIdentifiedAsProfile_AndFieldValuesWin()
    {
        var settings = new UtilitySettings();
        settings.FindCode("CB")!.InsideWidthIn = 24;

        var fromProfile = Structure("PT 1 CB\n30 RCP E 5.00 INV", 110, 0, 0, settings);
        DimensionSource source;
        Assert.Equal(24, StructureDimensions.InsideWidth(fromProfile, settings, out source));
        Assert.Equal(DimensionSource.Profile, source);

        var fromField = Structure("PT 2 CB\nID 48\n30 RCP E 5.00 INV", 110, 0, 0, settings);
        Assert.Equal(48, StructureDimensions.InsideWidth(fromField, settings, out source));
        Assert.Equal(DimensionSource.FieldObserved, source);

        fromProfile.EnteredInsideWidthIn = 36;
        Assert.Equal(36, StructureDimensions.InsideWidth(fromProfile, settings, out source));
        Assert.Equal(DimensionSource.UserEntry, source);
    }

    [Fact]
    public void NoStructureCodeShipsWithAnInventedInsideSize()
    {
        Assert.All(new UtilitySettings().StructureCodes, c => Assert.Null(c.InsideWidthIn));
    }
}

