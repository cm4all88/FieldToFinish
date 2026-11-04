using FieldCodes.Linework;
using FieldCodes.Review;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// A feature genuinely missing from the drawing, against one the drawing shows but FTF could not
/// read well enough to confirm. Visible, hidden, missing and unverified symbols and labels: only a
/// read that shows something is not there is "missing"; a read that failed is "not verified", on a
/// required item a person closes, and resolves by itself once the read works.
/// </summary>
public sealed class UnverifiedEvidenceTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public UnverifiedEvidenceTests(RulesFixture fx) => _fx = fx;

    // What Accountability.ReadStyle gave at 65d1c05: a marker drawn, its style unread.
    private const string Ambiguous = "style name: AmbiguousMatchException: Ambiguous match found.";
    private static MarkerFact Unread() => new() { Known = true, Visible = true, StyleProblem = Ambiguous, LabelProblem = "label style name: AmbiguousMatchException: Ambiguous match found." };
    private static MarkerFact SymbolMarker(string block) => new() { Known = true, Visible = true, MarkerType = "UseSymbolForMarker", SymbolName = block, StyleName = "FTF-SMOKE-WFH", LabelShown = true, LabelStyleName = "Standard" };
    private static MarkerFact Generic() => new() { Known = true, Visible = true, MarkerType = "UseCustomMarker", SymbolName = string.Empty, StyleName = "Basic", LabelShown = true, LabelStyleName = "Standard" };

    private PointFacts P(string n, string d, MarkerFact marker, RulesConfig? rules = null)
    {
        var cfg = rules ?? _fx.Config;
        return new PointFacts
        {
            Number = n, Handle = "H" + n, RawDescription = d, X = double.Parse(n) * 10, Y = 0, Z = 100,
            Parsed = new FieldCodeParser(cfg).Parse(n, d), Coding = LineworkCoding.Parse(d, cfg), Marker = marker
        };
    }

    private AccountingContext Ctx(RulesConfig? rules = null) => new()
    {
        Rules = rules ?? _fx.Config, Settings = new FtfSettings(), Diagnostics = new CodeDiagnostics(rules ?? _fx.Config, null),
        Continuity = new ContinuityResult(), FamilyOf = c => c, UnitsPerFoot = 1.0
    };

    private static (PointLedger ledger, List<ReviewItem> items, ReviewRegister register, LedgerRun run) Account(
        PointLedger? previous, IList<PointFacts> points, AccountingContext ctx, ReviewRegister? register = null, DateTime? at = null)
    {
        var now = at ?? new DateTime(2026, 11, 4, 9, 0, 0);
        var r = PointAccounting.Build(previous, points, ctx, now);
        register ??= new ReviewRegister();
        register.Report(ReviewKinds.PointsSource, r.Items, now, true, null);
        var run = PointAccounting.Finish(r.Ledger, register, now, "test");
        return (r.Ledger, r.Items, register, run);
    }

    // ------------------------------------------------------------------ symbols

    [Fact]
    public void AHydrantSymbolThatIsThereIsAccountedFor()
    {
        var a = Account(null, new[] { P("1401", "WFH 14", SymbolMarker("FTF-SMOKE-WFH")) }, Ctx());
        var e = a.ledger.Find("1401")!;
        Assert.Equal(PointDisposition.Symbol, e.Disposition);
        Assert.Contains(e.Evidence, x => x == "its own symbol: FTF-SMOKE-WFH (style FTF-SMOKE-WFH)");
        Assert.Contains(e.Evidence, x => x == "marker: FTF-SMOKE-WFH (UseSymbolForMarker FTF-SMOKE-WFH)");
        Assert.Empty(a.items);
    }

    [Fact]
    public void AMarkerThatIsHiddenGenericOrAnotherBlockIsMissing()
    {
        var hidden = new MarkerFact { Known = true, Visible = false, MarkerType = "UseSymbolForMarker", SymbolName = "PMX-WFH", StyleName = "Hidden" };
        var other = SymbolMarker("PMX-CB");
        var noBlock = SymbolMarker(string.Empty);
        var a = Account(null, new[] { P("1", "WFH 1", hidden), P("2", "WFH 2", Generic()), P("3", "WFH 3", other), P("4", "WFH 4", noBlock) }, Ctx());
        foreach (var (n, why) in new[] { ("1", "draws no marker"), ("2", "generic custom marker"), ("3", "block PMX-CB"), ("4", "names no block") })
        {
            var item = a.register.Find("point.nosymbol:" + n)!;
            Assert.True(item.Required, n);
            Assert.Contains(why, item.Reason);
            Assert.Equal(PointDisposition.Review, a.ledger.Find(n)!.Disposition);
            Assert.Contains("symbol", a.ledger.Find(n)!.Missing);
        }
        Assert.Null(a.register.Find("point.symbolunverified:WFH"));
    }

    [Fact]
    public void AMarkerWhoseStyleCouldNotBeReadIsUnverifiedNotMissing()
    {
        // As at 65d1c05: the marker is drawn, its point style's type and block were not read.
        var a = Account(null, new[] { P("1401", "WFH 14", Unread()), P("1402", "WFH 15", Unread()) }, Ctx());
        Assert.DoesNotContain(a.items, i => i.Kind == ReviewKinds.PointNoSymbol);
        var item = a.register.Find("point.symbolunverified:WFH")!;
        Assert.True(item.Required);                                       // never passed by silence
        Assert.Equal(ReviewSeverity.Warning, item.Severity);
        Assert.Equal("1401, 1402", item.Points);                          // one item for the code
        Assert.Contains("This is not a missing symbol", item.Reason);
        Assert.Contains("AmbiguousMatchException", item.Reason);           // the reason survives to the review list
        foreach (var n in new[] { "1401", "1402" })
        {
            var e = a.ledger.Find(n)!;
            Assert.Equal(PointDisposition.Unverified, e.Disposition);
            Assert.Empty(e.Missing);
            Assert.Contains("symbol", e.Unverified);
            Assert.True(e.Represented);
            Assert.False(e.DraftingComplete);
            Assert.Contains(e.Evidence, x => x.StartsWith("marker: point style name not read (marker type not read); not read: " + Ambiguous));
        }
        Assert.Equal(2, a.run.Unverified);
        Assert.Equal(0, a.run.Accounted);
        Assert.Equal(0, a.run.Review);
        Assert.Contains("2 shown but not verified", a.run.Describe());
        Assert.False(a.register.MarkQcComplete(new ReviewSettings(), "x", DateTime.Now, out _));
        Assert.Contains(",symbol,", FieldCodes.Reporting.AccountabilityReport.RenderPoints(a.ledger).Split('\n')[1].Replace("\"", ""));
    }

    [Fact]
    public void ASymbolMarkerWhoseBlockCouldNotBeReadIsUnverified()
    {
        var m = new MarkerFact { Known = true, Visible = true, MarkerType = "UseSymbolForMarker", SymbolName = null, StyleName = "PMX HYDRANT", StyleProblem = "marker symbol: COMException: not available" };
        var a = Account(null, new[] { P("1", "WFH 1", m) }, Ctx());
        Assert.Contains("draws a symbol block, but FTF could not read which", a.register.Find("point.symbolunverified:WFH")!.Reason);
        // A marker whose visibility could not be read at all is not verified either.
        var b = Account(null, new[] { P("2", "WFH 2", new MarkerFact { Known = false, Problem = "point group style override could not be determined" }) }, Ctx());
        Assert.Contains("point group style override could not be determined", b.register.Find("point.symbolunverified:WFH")!.Reason);
        Assert.Null(b.register.Find("point.nosymbol:2"));
    }

    [Fact]
    public void ANamedSymbolStyleThatCannotBeReadIsUnverifiedAndAWrongOneIsMissing()
    {
        var rules = RulesFixture.WithFeatureStandard(@"{ ""requirements"": [ { ""name"": ""Meter"", ""codes"": [""WMT""], ""symbolStyle"": ""PMX WATER*"" } ] }");
        var a = Account(null, new[] { P("1", "WMT", Unread(), rules), P("2", "WMT", Generic(), rules) }, Ctx(rules));
        Assert.Contains("cannot tell whether it is one the standard names (PMX WATER*)", a.register.Find("point.symbolunverified:WMT")!.Reason);
        Assert.Equal("1", a.register.Find("point.symbolunverified:WMT")!.Points);
        Assert.Contains("is not one the standard names", a.register.Find("point.nosymbol:2")!.Reason);
    }

    [Fact]
    public void OnceTheReadWorksTheUnverifiedItemResolvesByItself()
    {
        var first = Account(null, new[] { P("1401", "WFH 14", Unread()) }, Ctx());
        var register = first.register;
        Assert.Equal(ReviewItemStatus.Open, register.Find("point.symbolunverified:WFH")!.Status);

        var fixedRead = Account(PointLedger.FromJson(first.ledger.ToJson()), new[] { P("1401", "WFH 14", SymbolMarker("FTF-SMOKE-WFH")) }, Ctx(), register, new DateTime(2026, 11, 5));
        var item = register.Find("point.symbolunverified:WFH")!;
        Assert.Equal(ReviewItemStatus.Resolved, item.Status);
        Assert.Equal(new[] { "opened", "resolved" }, item.History!.Select(h => h.Action).ToArray());
        Assert.Equal(PointDisposition.Symbol, fixedRead.ledger.Find("1401")!.Disposition);

        // And a read that shows the symbol is not there is missing, then: nothing was dismissed.
        var wrong = Account(PointLedger.FromJson(fixedRead.ledger.ToJson()), new[] { P("1401", "WFH 14", Generic()) }, Ctx(), register, new DateTime(2026, 11, 6));
        Assert.True(register.Find("point.nosymbol:1401")!.Required);
        Assert.Equal(PointDisposition.Review, wrong.ledger.Find("1401")!.Disposition);
    }

    [Fact]
    public void APersonWhoChecksAndAcceptsConfirmsItUntilThePointsChange()
    {
        var points = new[] { P("1401", "WFH 14", Unread()) };
        var first = Account(null, points, Ctx());
        var register = first.register;
        Assert.True(register.Accept("point.symbolunverified:WFH", "checked on screen: hydrant symbol drawn", "JD", new DateTime(2026, 11, 4, 10, 0, 0), out _));

        var again = Account(PointLedger.FromJson(first.ledger.ToJson()), points, Ctx(), register, new DateTime(2026, 11, 4, 11, 0, 0));
        Assert.Equal(ReviewItemStatus.Accepted, register.Find("point.symbolunverified:WFH")!.Status);
        var e = again.ledger.Find("1401")!;
        Assert.Equal(PointDisposition.Symbol, e.Disposition);              // confirmed by a person
        Assert.True(e.DraftingComplete);
        Assert.Contains("checked on screen", e.Decision);
        Assert.Empty(register.RequiredOpen);

        // Another hydrant whose style cannot be read either: the decision no longer covers the list.
        var more = Account(PointLedger.FromJson(again.ledger.ToJson()), new[] { points[0], P("1402", "WFH 15", Unread()) }, Ctx(), register, new DateTime(2026, 11, 4, 12, 0, 0));
        Assert.Equal(ReviewItemStatus.Open, register.Find("point.symbolunverified:WFH")!.Status);
        Assert.Equal(PointDisposition.Unverified, more.ledger.Find("1402")!.Disposition);
    }

    // ------------------------------------------------------------------ labels

    private static MarkerFact Label(bool? shown, string? style, string? problem = null) => new()
    {
        Known = true, Visible = true, MarkerType = "UseCustomMarker", SymbolName = string.Empty, StyleName = "Basic",
        LabelShown = shown, LabelStyleName = style, LabelProblem = problem
    };

    [Fact]
    public void AHouseNumberSignsVisibleLabelIsItsLabelAndAHiddenOneIsMissing()
    {
        var a = Account(null, new[] { P("1301", "HOUSE NUMBER 4735 SIGN", Label(true, "Point#-Elevation-Description")),
                                      P("1302", "HOUSE NUMBER 4737 SIGN", Label(false, "Point#-Elevation-Description", "the point's label is turned off")) }, Ctx());
        var shown = a.ledger.Find("1301")!;
        Assert.Equal(PointDisposition.PointStyle, shown.Disposition);
        Assert.Contains("label (point label)", shown.Found);
        Assert.Equal("HOUSE NUMBER 4735 SIGN", shown.RawDescription);      // the surveyed description, as it was
        Assert.DoesNotContain(a.items, i => i.Points == "1301" || i.Id.EndsWith(":1301"));

        var missing = a.register.Find("point.missingoutput:1302")!;
        Assert.True(missing.Required);
        Assert.Contains("its Civil 3D label is hidden (the point's label is turned off)", missing.Reason);
        Assert.Equal(PointDisposition.Review, a.ledger.Find("1302")!.Disposition);
    }

    [Fact]
    public void ALabelThatCouldNotBeReadIsUnverifiedNotMissing()
    {
        var a = Account(null, new[] { P("1301", "HOUSE NUMBER 4735 SIGN", Label(null, null, "label style name: AmbiguousMatchException: Ambiguous match found.")) }, Ctx());
        Assert.Null(a.register.Find("point.missingoutput:1301"));
        var item = a.register.Find("point.labelunverified:HOUSE")!;
        Assert.True(item.Required);
        Assert.Contains("This is not a missing label", item.Reason);
        Assert.Contains("AmbiguousMatchException", item.Reason);
        var e = a.ledger.Find("1301")!;
        Assert.Equal(PointDisposition.Unverified, e.Disposition);
        Assert.Contains("label (point label)", e.Unverified);

        // Shown, but the label style's name unread, where the standard names label styles: not verified.
        var rules = RulesFixture.WithFeatureStandard(@"{ ""requirements"": [ { ""codes"": [""SN*"", ""SIGN""], ""family"": ""Signs"", ""match"": ""\\bSIGNS?\\b"",
            ""symbol"": ""PointStyle"", ""label"": ""PointLabel"", ""labelStyle"": ""Description*"" } ] }");
        var b = Account(null, new[] { P("1", "HOUSE NUMBER 4735 SIGN", Label(true, null, "label style name: not read"), rules),
                                      P("2", "HOUSE NUMBER 4737 SIGN", Label(true, "Point#-Elevation", null), rules),
                                      P("3", "HOUSE NUMBER 4739 SIGN", Label(true, "Description Only", null), rules) }, Ctx(rules));
        Assert.Equal("1", b.register.Find("point.labelunverified:HOUSE")!.Points);
        Assert.Contains("label style Point#-Elevation is not one the standard names (Description*)", b.register.Find("point.missingoutput:2")!.Reason);
        Assert.Equal(PointDisposition.PointStyle, b.ledger.Find("3")!.Disposition);
    }

    [Fact]
    public void ALabelTheProjectStandardRequiresIsMissingWhateverThePointLabelShows()
    {
        // The office says address signs get a real label: the point's own label does not stand in.
        var rules = RulesFixture.WithFeatureStandard(@"{ ""requirements"": [ { ""codes"": [""SN*"", ""SIGN""], ""family"": ""Signs"", ""match"": ""\\bSIGNS?\\b"", ""label"": ""Required"",
            ""symbol"": ""PointStyle"" } ] }");
        var a = Account(null, new[] { P("1301", "HOUSE NUMBER 4735 SIGN", Label(true, "Point#-Elevation-Description"), rules) }, Ctx(rules));
        var item = a.register.Find("point.missingoutput:1301")!;
        Assert.True(item.Required);
        Assert.Contains("the standard requires a label", item.Reason);
        Assert.Null(a.register.Find("point.labelunverified:HOUSE"));
        // Labelled by hand: accounted for.
        var hand = P("1301", "HOUSE NUMBER 4735 SIGN", Label(true, "Point#-Elevation-Description"), rules);
        hand.Outputs.Add(new OutputFact { Kind = "Label", Handle = "L1" });
        Assert.Equal(PointDisposition.Drawn, Account(null, new[] { hand }, Ctx(rules)).ledger.Find("1301")!.Disposition);
    }
}
