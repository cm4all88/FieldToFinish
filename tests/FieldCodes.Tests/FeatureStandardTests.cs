using FieldCodes.Annotation;
using FieldCodes.Easements;
using FieldCodes.Linework;
using FieldCodes.Orient;
using FieldCodes.Review;
using FieldCodes.Settings;
using FieldCodes.Standards;

namespace FieldCodes.Tests;

/// <summary>
/// The drafting standard per feature (Settings > Point Features): what each field code and family
/// needs on the plan, read by point accountability, Label All and FTFORIENT alike. Point
/// accountability must find genuine missing drafting work, not hundreds of legitimate survey
/// points: ground shots, control check shots, signs that are not traffic signs, hydrants whose
/// symbol is their whole representation, and bare codes their point style shows.
/// </summary>
public sealed class FeatureStandardTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public FeatureStandardTests(RulesFixture fx) => _fx = fx;

    private static readonly MarkerFact Generic = new() { Known = true, Visible = true, MarkerType = "UseCustomMarker", StyleName = "Basic" };

    private PointFacts P(string n, string d, MarkerFact? marker = null, double x = double.NaN, double y = 0, double z = 100, RulesConfig? rules = null)
    {
        var cfg = rules ?? _fx.Config;
        if (double.IsNaN(x)) x = double.Parse(n) * 10;
        return new PointFacts
        {
            Number = n, Handle = "H" + n, RawDescription = d, X = x, Y = y, Z = z,
            Parsed = new FieldCodeParser(cfg).Parse(n, d), Coding = LineworkCoding.Parse(d, cfg),
            Marker = marker ?? Generic
        };
    }

    private static MarkerFact Symbol(string block) => new() { Known = true, Visible = true, MarkerType = "UseSymbolForMarker", SymbolName = block, StyleName = "PMX " + block };

    private AccountingContext Ctx(RulesConfig? rules = null, FtfSettings? settings = null) => new()
    {
        Rules = rules ?? _fx.Config,
        Settings = settings ?? new FtfSettings(),
        Diagnostics = new CodeDiagnostics(rules ?? _fx.Config, null),
        Continuity = new ContinuityResult(),
        FamilyOf = c => c,
        UnitsPerFoot = 1.0
    };

    private static (PointLedger ledger, List<ReviewItem> items, ReviewRegister register, LedgerRun run) Account(
        PointLedger? previous, IList<PointFacts> points, AccountingContext ctx, ReviewRegister? register = null, DateTime? at = null)
    {
        var now = at ?? new DateTime(2026, 11, 3, 12, 0, 0);
        var r = PointAccounting.Build(previous, points, ctx, now);
        register ??= new ReviewRegister();
        register.Report(ReviewKinds.PointsSource, r.Items, now, true, null);
        var run = PointAccounting.Finish(r.Ledger, register, now, "test");
        return (r.Ledger, r.Items, register, run);
    }

    private EffectiveRequirement Req(string description, RulesConfig? rules = null)
    {
        var cfg = rules ?? _fx.Config;
        return FeatureRequirements.For(cfg, new FieldCodeParser(cfg).Parse("1", description), description);
    }

    private static RulesConfig RulesWith(string featureStandardJson) => RulesFixture.WithFeatureStandard(featureStandardJson);

    // ------------------------------------------------------------------ the standard itself

    [Fact]
    public void TheBuiltInStandardSaysWhatEachNamedFeatureNeeds()
    {
        var gs = Req("GS");
        Assert.Equal(PointClass.Surface, gs.Classification);
        Assert.Equal(IntendedDisposition.SurfaceOnly, gs.Disposition);
        Assert.Equal(SymbolRequirement.None, gs.Symbol);
        Assert.Equal(LabelRequirement.None, gs.Label);
        Assert.True(gs.Surface);

        var zk = Req("ZK");
        Assert.Equal(PointClass.Control, zk.Classification);
        Assert.Equal(IntendedDisposition.ControlCheck, zk.Disposition);
        Assert.Equal(SymbolRequirement.None, zk.Symbol);
        Assert.Equal(LabelRequirement.None, zk.Label);

        var wfh = Req("WFH 12");
        Assert.Equal(PointClass.Utility, wfh.Classification);
        Assert.Equal(SymbolRequirement.Specific, wfh.Symbol);
        Assert.Equal(LabelRequirement.None, wfh.Label);
        Assert.True(wfh.Rotation);
        Assert.StartsWith("Fire hydrant", wfh.Source);

        // Bare utility and site codes: shown by their point style, no unique marker assumed.
        foreach (var code in new[] { "WMT", "GYA", "PP", "BOL", "SSCO", "MB" })
            Assert.Equal(SymbolRequirement.PointStyle, Req(code).Symbol);
    }

    [Fact]
    public void SignsAreCategorisedFromTheirWordsAndAPrivateSignIsNotATrafficSign()
    {
        var house = Req("HOUSE NUMBER 4735 SIGN");
        Assert.Equal(PointClass.Sign, house.Classification);
        Assert.Equal(SignCategory.Address, house.SignCategory);
        Assert.True(house.Specific);                                   // the standard names it, by its words
        Assert.Equal(SymbolRequirement.PointStyle, house.Symbol);
        Assert.Equal(LabelRequirement.PointLabel, house.Label);
        Assert.False(house.Rotation);
        Assert.Contains("Address / house number sign", house.Source);

        Assert.Equal(SignCategory.Address, Req("SN HOUSE NUMBER 4735 SIGN").SignCategory);
        Assert.Equal(SignCategory.Private, Req("SN PRIVATE PROPERTY").SignCategory);
        Assert.False(Req("SN PRIVATE PROPERTY").Rotation);
        Assert.Equal(SignCategory.Regulatory, Req("SN STOP").SignCategory);
        Assert.True(Req("SN STOP").Rotation);
        Assert.Equal(SignCategory.Regulatory, Req("SNS").SignCategory);
        Assert.Equal(SignCategory.StreetName, Req("SNST").SignCategory);
        Assert.Equal(SignCategory.Warning, Req("SN CURVE AHEAD").SignCategory);
        Assert.Equal(SignCategory.Other, Req("SN WELCOME TO THE PARK").SignCategory);
        Assert.False(Req("SN WELCOME TO THE PARK").Rotation);
        // A bare sign, or one with only its direction: a traffic sign as before, turned to its traffic.
        Assert.Equal(SignCategory.None, Req("SN").SignCategory);
        Assert.Equal(SignCategory.None, Req("SN 135").SignCategory);
        Assert.Equal(SignCategory.None, Req("SN FACES N").SignCategory);
        Assert.True(Req("SN").Rotation);
        Assert.Equal(SymbolRequirement.Specific, Req("SN").Symbol);
        // A code FTF knows is never taken for a sign because its note mentions one.
        Assert.Equal(PointClass.Utility, Req("PP 1234 W/ SIGN").Classification);
    }

    [Fact]
    public void AnOfficeEntryReplacesOnlyItsOwnKeyAndCanSwitchABuiltInOff()
    {
        var rules = RulesWith(@"{ ""requirements"": [
            { ""name"": ""Cleanout"", ""codes"": [""SSCO""], ""label"": ""Required"" },
            { ""name"": ""No hydrant standard"", ""codes"": [""WFH"", ""FH""], ""enabled"": false } ] }");
        Assert.Equal(LabelRequirement.Required, Req("SSCO", rules).Label);
        Assert.Equal(SymbolRequirement.PointStyle, Req("SSCO", rules).Symbol);       // inherited from its family
        Assert.Equal(IntendedDisposition.SurfaceOnly, Req("GS", rules).Disposition);  // built-in, not mentioned
        Assert.Equal(SymbolRequirement.PointStyle, Req("WFH 12", rules).Symbol);     // the hydrant entry is off: Utilities
        Assert.Contains(rules.FeatureStandard.Requirements, r => r.IsDefault);
    }

    [Fact]
    public void AStandardThatCannotWorkIsRefusedWhenTheRulesLoad()
    {
        var ex = Assert.Throws<ConfigException>(() => RulesWith(@"{ ""requirements"": [
            { ""name"": ""Bad"", ""family"": ""Hydrants"", ""match"": ""[unclosed"" },
            { ""name"": ""No condition"", ""codes"": [""X""], ""exceptions"": [ { ""name"": ""always"" } ] } ] }"));
        Assert.Contains("'Hydrants' is not a feature family", ex.Message);
        Assert.Contains("not a valid regular expression", ex.Message);
        Assert.Contains("has no condition", ex.Message);
    }

    // ------------------------------------------------------------------ accountability, by scenario

    [Fact]
    public void GroundShotWithoutASurfaceIsAccountedForAsSurfaceData()
    {
        var a = Account(null, new[] { P("1", "GS"), P("2", "GS 1") }, Ctx());
        Assert.All(new[] { "1", "2" }, n =>
        {
            var e = a.ledger.Find(n)!;
            Assert.Equal(PointDisposition.Surface, e.Disposition);
            Assert.Equal("designated", e.SurfaceStatus);
            Assert.True(e.DraftingComplete);
        });
        Assert.Empty(a.items);
        Assert.Empty(a.register.RequiredOpen);
        Assert.Equal("GS 1", a.ledger.Find("2")!.RawDescription);                 // preserved as shot
    }

    [Fact]
    public void GroundShotInASurfaceIsVerifiedWhenTheCheckIsOn()
    {
        var settings = new FtfSettings();
        settings.Accountability.VerifySurfaceMembership = true;
        var member = P("1", "GS");
        member.Surfaces.Add(new SurfaceFact { Surface = "EG", InDefinition = true, PointGroup = "GROUND", IsVertex = true });
        var a = Account(null, new[] { member }, Ctx(settings: settings));
        Assert.Equal("verified", a.ledger.Find("1")!.SurfaceStatus);
        Assert.Contains(a.ledger.Find("1")!.Evidence, e => e.Contains("surface EG: in its definition (point group GROUND)"));
        Assert.Empty(a.items);
    }

    [Fact]
    public void ControlCheckShotsAreControlCheckWithNoLabelOrSymbol()
    {
        var a = Account(null, new[] { P("1", "FIP 5/8", x: 0, y: 0), P("2", "ZK", x: 0.02, y: 0.01) }, Ctx());
        var zk = a.ledger.Find("2")!;
        Assert.Equal(PointDisposition.Control, zk.Disposition);
        Assert.Equal("Control/Check", zk.Classification);
        Assert.Equal("control check ZK", zk.Reading);
        Assert.Empty(a.items);
        Assert.Contains("2,ZK,", FieldCodes.Reporting.AccountabilityReport.RenderControlChecks(a.ledger, 2.0, 1.0));
    }

    [Fact]
    public void AHouseNumberSignKeepsItsDescriptionAndIsLabelledByItsPointLabel()
    {
        var shown = new MarkerFact { Known = true, Visible = true, MarkerType = "UseCustomMarker", StyleName = "Basic", LabelShown = true, LabelStyleName = "Description Only" };
        var hidden = new MarkerFact { Known = true, Visible = true, MarkerType = "UseCustomMarker", StyleName = "Basic", LabelShown = false };
        var a = Account(null, new[] { P("54953", "HOUSE NUMBER 4735 SIGN", shown), P("54954", "HOUSE NUMBER 4737 SIGN", hidden) }, Ctx());

        var sign = a.ledger.Find("54953")!;
        Assert.Equal("HOUSE NUMBER 4735 SIGN", sign.RawDescription);
        Assert.Equal("Sign (address / house number)", sign.Classification);
        Assert.Equal(PointDisposition.PointStyle, sign.Disposition);
        Assert.Contains("label (point label)", sign.Found);
        Assert.Contains(sign.Evidence, e => e.Contains("label style Description Only"));
        Assert.DoesNotContain(a.items, i => i.Points == "54953");
        // Not a code nothing handles: the standard names it by its words.
        Assert.DoesNotContain(a.ledger.Find("54953")!.Issues, i => i.StartsWith("code.unknown"));
        Assert.Contains(sign.Evidence, e => e.StartsWith("no rule reads its words; its drafting standard applies"));
        // Nor does the code check (FTFREVIEWCHECK, the Diagnostics screen) call HOUSE a code nothing handles.
        var diagnostics = new CodeDiagnostics(_fx.Config, null);
        var issue = diagnostics.Classify(_fx.Parse("HOUSE NUMBER 4735 SIGN"));
        Assert.Equal(CodeIssueKind.StandardFeature, issue.Kind);
        Assert.Equal(CodeIssueCategory.Informational, issue.Category);
        Assert.Empty(ReviewChecks.Codes(new[] { new ReviewPoint { Number = "54953", Description = "HOUSE NUMBER 4735 SIGN", Parsed = _fx.Parse("HOUSE NUMBER 4735 SIGN") } }, diagnostics));
        // A description no standard entry names is still a code nothing handles.
        Assert.Equal(CodeIssueKind.NoRule, diagnostics.Classify(_fx.Parse("XYZQ 12")).Kind);

        // The same sign with its point label hidden: its description shows nowhere, a genuine gap.
        var missing = a.register.Find("point.missingoutput:54954")!;
        Assert.True(missing.Required);
        Assert.Contains("label is hidden", missing.Reason);
        Assert.DoesNotContain(a.items, i => i.Id == "point.nosymbol:54954");       // its point style is enough
    }

    [Fact]
    public void AHydrantWithItsSymbolNeedsNoTextLabel()
    {
        var a = Account(null, new[] { P("1", "WFH 12", Symbol("PMX-WFH")), P("2", "WFH", Symbol("SSV-WFH")) }, Ctx());
        Assert.All(new[] { "1", "2" }, n =>
        {
            var e = a.ledger.Find(n)!;
            Assert.Equal(PointDisposition.Symbol, e.Disposition);
            Assert.DoesNotContain("label", e.Expected);
            Assert.True(e.DraftingComplete);
        });
        Assert.Empty(a.items);
        // Label All does not draw one either: the same standard.
        var decision = AnnotationPolicy.Label(_fx.Parse("WFH 12"), new AnnotationSettings(), null, _fx.Config);
        Assert.False(decision.Annotate);
        Assert.Contains("drafting standard draws no label", decision.Reason);
    }

    [Fact]
    public void BareCodesShownByAnAcceptablePointStyleAreAccountedFor()
    {
        var points = new[] { "WMT", "GYA", "BOL", "SSCO", "PP" }.Select((c, i) => P((i + 1).ToString(), c)).ToList();
        var a = Account(null, points, Ctx());
        Assert.All(a.ledger.Points.Where(e => e.Number != "5"), e => Assert.Equal(PointDisposition.PointStyle, e.Disposition));
        Assert.All(a.ledger.Points, e => Assert.Contains("point style", e.Found));
        Assert.DoesNotContain(a.items, i => i.Kind == ReviewKinds.PointNoSymbol);
        // PP is in the office's symbolLabels, so Label All labels a bare PP with its code: the
        // same rule says that label is expected. Its point style is still enough for the symbol.
        var pole = a.register.Find("point.missingoutput:5")!;
        Assert.Contains("Label \"PP\" is not drawn", pole.Reason);
        var drawn = P("5", "PP");
        drawn.Outputs.Add(new OutputFact { Kind = "Label", Handle = "L5" });
        var labelled = Account(null, points.Take(4).Concat(new[] { drawn }).ToList(), Ctx());
        Assert.Equal(PointDisposition.Drawn, labelled.ledger.Find("5")!.Disposition);
        Assert.Empty(labelled.items);
        // A standard naming the styles that show it: another style does not.
        var rules = RulesWith(@"{ ""requirements"": [ { ""name"": ""Meter"", ""codes"": [""WMT""], ""symbolStyle"": ""PMX WATER*"" } ] }");
        var b = Account(null, new[] { P("1", "WMT", Generic, rules: rules), P("2", "WMT", new MarkerFact { Known = true, Visible = true, MarkerType = "UseCustomMarker", StyleName = "PMX WATER METER" }, rules: rules) }, Ctx(rules));
        Assert.Contains("is not one the standard names (PMX WATER*)", b.register.Find("point.nosymbol:1")!.Reason);
        Assert.Equal(PointDisposition.PointStyle, b.ledger.Find("2")!.Disposition);
    }

    [Fact]
    public void AMissingRequiredSymbolIsARequiredAction()
    {
        var hidden = new MarkerFact { Known = true, Visible = false, MarkerType = "UseCustomMarker", StyleName = "Hidden" };
        var a = Account(null, new[] { P("1", "WFH 12"), P("2", "PP", hidden) }, Ctx());
        var hydrant = a.register.Find("point.nosymbol:1")!;
        Assert.True(hydrant.Required);
        Assert.Equal(ReviewSeverity.Error, hydrant.Severity);
        Assert.Contains("generic custom marker", hydrant.Reason);
        Assert.Contains("draws no marker", a.register.Find("point.nosymbol:2")!.Reason);
        Assert.Equal(2, a.run.Review);
    }

    [Fact]
    public void AMissingRequiredLabelIsARequiredAction()
    {
        var rules = RulesWith(@"{ ""requirements"": [ { ""name"": ""Bollard"", ""codes"": [""BOL""], ""label"": ""Required"" } ] }");
        var labelled = P("1", "BOL 3", rules: rules);
        labelled.Outputs.Add(new OutputFact { Kind = "Label", Handle = "L1" });
        var a = Account(null, new[] { labelled, P("2", "BOL 4", rules: rules), P("3", "BOL", rules: rules) }, Ctx(rules));
        Assert.Equal(PointDisposition.Drawn, a.ledger.Find("1")!.Disposition);
        Assert.True(a.register.Find("point.missingoutput:2")!.Required);
        Assert.Contains("POST 4", a.register.Find("point.missingoutput:2")!.Reason);
        Assert.Contains("no rule writes its label", a.register.Find("point.missingoutput:3")!.Reason);
    }

    [Fact]
    public void ChangingTheConfigurationChangesWhatIsRequiredAndBack()
    {
        var register = new ReviewRegister();
        var plain = Account(null, new[] { P("1", "SSCO") }, Ctx(), register);
        Assert.Empty(plain.items);

        // The office now requires cleanouts labelled.
        var strict = RulesWith(@"{ ""requirements"": [ { ""name"": ""Cleanout"", ""codes"": [""SSCO""], ""label"": ""Required"" } ] }");
        var second = Account(PointLedger.FromJson(plain.ledger.ToJson()), new[] { P("1", "SSCO", rules: strict) }, Ctx(strict), register, new DateTime(2026, 11, 4));
        var item = register.Find("point.missingoutput:1")!;
        Assert.Equal(ReviewItemStatus.Open, item.Status);
        Assert.Contains("Cleanout (SSCO)", item.Reason);
        Assert.Equal(PointDisposition.Review, second.ledger.Find("1")!.Disposition);

        // And back: the item resolves by itself on the recheck, its history kept.
        var third = Account(PointLedger.FromJson(second.ledger.ToJson()), new[] { P("1", "SSCO") }, Ctx(), register, new DateTime(2026, 11, 5));
        item = register.Find("point.missingoutput:1")!;
        Assert.Equal(ReviewItemStatus.Resolved, item.Status);
        Assert.Equal(new[] { "opened", "resolved" }, item.History!.Select(h => h.Action).ToArray());
        Assert.Equal(PointDisposition.PointStyle, third.ledger.Find("1")!.Disposition);
        Assert.Equal(new[] { "baseline" }, third.ledger.Find("1")!.History.Select(h => h.Action).ToArray());
    }

    [Fact]
    public void PreviouslyFlaggedFalsePositivesResolveAndGenuineDecisionsAreKept()
    {
        // The register a build before the standard left: a bare code without "its own symbol", a
        // ground shot with no surface, a ZK without a reason, and a genuine hydrant item a person
        // closed with a reason.
        var before = new DateTime(2026, 11, 1);
        var register = new ReviewRegister();
        var old = new List<ReviewItem>
        {
            ReviewKinds.New(ReviewKinds.PointNoSymbol, "1", "\"WMT\": its marker is a generic custom marker"),
            ReviewKinds.New(ReviewKinds.PointMissingOutput, "2", "Point 2 \"GS\" is surface data but no surface in the drawing is built from it."),
            ReviewKinds.New(ReviewKinds.PointExcludedNoReason, "3", "\"ZK\" is excluded by the rules' ignore list (ZK) but the entry gives no reason."),
        };
        foreach (var i in old) { i.Source = ReviewKinds.PointsSource; i.Points = i.Id.Split(':')[1]; }
        register.Report(ReviewKinds.PointsSource, old, before);
        var points = new[] { P("1", "WMT"), P("2", "GS"), P("3", "ZK"), P("4", "WFH 12") };

        // The hydrant's item is genuine: raised now, closed by a person.
        var first = Account(null, points, Ctx(), register, new DateTime(2026, 11, 3));
        foreach (var id in new[] { "point.nosymbol:1", "point.missingoutput:2", "point.excludednoreason:3" })
        {
            var item = register.Find(id)!;
            Assert.Equal(ReviewItemStatus.Resolved, item.Status);
            Assert.Contains("no longer found", item.Note);
            Assert.Equal(new[] { "opened", "resolved" }, item.History!.Select(h => h.Action).ToArray());
        }
        Assert.True(register.MarkNotApplicable("point.nosymbol:4", "hydrant drawn from the water plan", "JD", new DateTime(2026, 11, 3, 13, 0, 0), out _));

        // A recheck: the decision still applies (same point, same marker) and is kept.
        var again = Account(PointLedger.FromJson(first.ledger.ToJson()), points, Ctx(), register, new DateTime(2026, 11, 4));
        Assert.Equal(ReviewItemStatus.NotApplicable, register.Find("point.nosymbol:4")!.Status);
        Assert.Equal(PointDisposition.Excluded, again.ledger.Find("4")!.Disposition);
        Assert.Contains("hydrant drawn from the water plan", again.ledger.Find("4")!.Decision);
        // One item per point and kind, however many runs.
        Assert.Equal(register.Items.Count, register.Items.Select(i => i.Id).Distinct().Count());
        Assert.Empty(register.RequiredOpen);
        Assert.Equal(4, again.run.Points);
        Assert.Equal(4, again.run.Accounted);
        Assert.All(again.ledger.Points, e => Assert.NotNull(e.Standard));
    }

    [Fact]
    public void WarningsAreListedButDoNotHoldAPointForReview()
    {
        // Two shots at one spot: a warning to look at, not a missing deliverable.
        var a = Account(null, new[] { P("1", "SSCO", x: 0), P("2", "SSCO", x: 0.004) }, Ctx());
        var dup = a.register.Find("point.duplicate:1")!;
        Assert.True(dup.IsOpen);
        Assert.False(dup.Required);
        Assert.All(new[] { "1", "2" }, n => Assert.Equal(PointDisposition.PointStyle, a.ledger.Find(n)!.Disposition));
        Assert.Equal(0, a.run.Review);
        Assert.Equal(2, a.run.Warnings);
        Assert.Contains("2 more with a warning", a.run.Describe());
        Assert.True(a.register.MarkQcComplete(new ReviewSettings(), "JD", DateTime.Now, out _));
    }

    [Fact]
    public void AFeatureTheStandardAsksToBeReviewedIsAlwaysListed()
    {
        var rules = RulesWith(@"{ ""requirements"": [ { ""name"": ""Monitoring well"", ""codes"": [""MW""], ""review"": true, ""note"": ""check against the environmental report"" } ] }");
        var a = Account(null, new[] { P("1", "MW", rules: rules) }, Ctx(rules));
        var item = a.register.Find("point.review:1")!;
        Assert.True(item.Required);
        Assert.Contains("check against the environmental report", item.Reason);
    }

    // ------------------------------------------------------------------ labels and orientation

    [Fact]
    public void LabelAllDrawsASignLabelOnlyWhereThePointsOwnLabelIsHidden()
    {
        var sign = _fx.Parse("SN");                      // a symbol label: "SN"
        Assert.True(AnnotationPolicy.Label(sign, new AnnotationSettings(), null, _fx.Config, pointLabelShown: false).Annotate);
        var shown = AnnotationPolicy.Label(sign, new AnnotationSettings(), null, _fx.Config, pointLabelShown: true);
        Assert.False(shown.Annotate);
        Assert.Contains("own Civil 3D label shows it", shown.Reason);
    }

    [Fact]
    public void APointLabelStandsInOnlyWhereFtfsLabelWouldSayNoMore()
    {
        var shown = new MarkerFact { Known = true, Visible = true, MarkerType = "UseSymbolForMarker", SymbolName = "PMX-SN", StyleName = "PMX SIGN", LabelShown = true, LabelStyleName = "Description Only" };
        // "SN STOP" is finished by FTF as "STOP SIGN"; the point label shows only "SN STOP".
        var stop = _fx.Parse("SN STOP");
        Assert.False(FeatureRequirements.PointLabelSaysIt(stop, "SN STOP"));
        Assert.True(AnnotationPolicy.Label(stop, new AnnotationSettings(), null, _fx.Config, pointLabelShown: true).Annotate);
        var a = Account(null, new[] { P("1", "SN STOP", shown) }, Ctx());
        var missing = a.register.Find("point.missingoutput:1")!;
        Assert.True(missing.Required);
        Assert.Contains("says more than the description its Civil 3D label shows", missing.Reason);
        // Drawn: accounted for.
        var drawn = P("1", "SN STOP", shown);
        drawn.Outputs.Add(new OutputFact { Kind = "Label", Handle = "L1" });
        drawn.Outputs.Add(new OutputFact { Kind = "Tag", Handle = "T1" });              // its rule tags signs too
        var dl = Account(null, new[] { drawn }, Ctx());
        Assert.Equal(PointDisposition.Drawn, dl.ledger.Find("1")!.Disposition);
        Assert.Empty(dl.items);
        // A bare SN: FTF's label would be "SN", which the point label already shows.
        Assert.True(FeatureRequirements.PointLabelSaysIt(_fx.Parse("SN"), "SN"));
        // A crew note is something the point label does not show.
        var noted = new FieldCodeParser(_fx.Config).Parse("1", "SN", "NO PARKING");
        Assert.False(FeatureRequirements.PointLabelSaysIt(noted, "SN"));
    }

    [Fact]
    public void OnlyAnExplicitNoStopsFtforientInferringADirection()
    {
        // Nothing in the standard about turning: FTFORIENT's own rules decide, as before the standard.
        foreach (var d in new[] { "CB", "WVL 8", "SSCO", "PP 1236", "ASTO" })
        {
            Assert.False(Req(d).RotationDeclined, d);
            Assert.False(Req(d).Rotation, d);
        }
        // Required: hydrants and traffic signs.
        Assert.True(Req("WFH 12").Rotation);
        Assert.True(Req("SN STOP").Rotation);
        // Declined outright: address, private and other descriptive signs.
        foreach (var d in new[] { "HOUSE NUMBER 4735 SIGN", "SN PRIVATE DRIVE", "SN BEWARE OF DOG" })
        {
            Assert.True(Req(d).RotationDeclined, d);
            Assert.Contains("turned only by a recorded direction", Req(d).Describe());
        }
        // An office row that says no.
        var rules = RulesWith(@"{ ""requirements"": [ { ""codes"": [""CB""], ""rotation"": false } ] }");
        Assert.True(Req("CB", rules).RotationDeclined);
    }

    [Fact]
    public void APrivateSignIsLeftAsSurveyedUnlessItsDirectionIsRecorded()
    {
        var std = OrientationStandard.Pmx();
        var road = new OrientLine { Id = "CL", Role = LineRole.Centerline };
        road.Points.Add(new P2(-200, 0)); road.Points.Add(new P2(200, 0));
        OrientationDecision Decide(string description, bool? infer, double? measured = null)
        {
            var scene = new OrientationScene();
            scene.Lines.Add(road);
            scene.Features.Add(new OrientFeature { Id = "1", Code = "SN", Kind = OrientKind.Sign, Location = new P2(10, 15), Description = description, InferFacing = infer, MeasuredAzimuth = measured, MeasuredFrom = measured.HasValue ? "test" : null });
            return new OrientationEngine(std).Run(scene).Decisions.Single();
        }
        var house = Decide("SN HOUSE NUMBER 4735 SIGN", false);
        Assert.Equal(OrientationTier.NoSupport, house.Tier);
        Assert.Null(house.MarkerRotation);
        Assert.Contains(house.Evidence, e => e.Text.Contains("not assumed to face a road"));
        var recorded = Decide("SN HOUSE NUMBER 4735 SIGN FACES N", false, 90);
        Assert.Equal(OrientationTier.Measured, recorded.Tier);
        // A traffic sign by the road is still turned to its traffic.
        var traffic = Decide("SN", null);
        Assert.NotEqual(OrientationTier.NoSupport, traffic.Tier);
    }
}
