using FieldCodes.Review;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// The drawing's review list: what each check raised, what a drafter did about it, and the
/// Drafting QC Complete mark that required items hold back.
/// </summary>
public sealed class ReviewRegisterTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 9, 0, 0);

    private static ReviewItem Gate(string post) =>
        ReviewKinds.New(ReviewKinds.GateUnpaired, post, "no partner") is var i ? Located(i, post) : null!;

    private static ReviewItem Located(ReviewItem i, string post)
    {
        i.Points = post;
        i.Handles.Add("H" + post);
        i.X = 1; i.Y = 2;
        return i;
    }

    [Fact]
    public void ACheckRaisesOpenItemsWithTheKindsWording()
    {
        var r = new ReviewRegister();
        Assert.Equal(2, r.Report("Gates", new[] { Gate("1"), Gate("2") }, T0));
        var item = r.Find("gate.unpaired:1")!;
        Assert.Equal(ReviewItemStatus.Open, item.Status);
        Assert.Equal("Gates", item.Source);
        Assert.Equal("Gate", item.FeatureType);
        Assert.Equal("Gate post has no partner", item.Issue);
        Assert.Equal("FTFGATE", item.FixCommand);
        Assert.Equal("Gates", item.SettingsPage);
        Assert.Equal(ReviewSeverity.Warning, item.Severity);
        Assert.True(item.Required);
        Assert.Equal(T0, item.FirstSeen);
        Assert.Equal(2, r.RequiredOpen.Count);
    }

    [Fact]
    public void RerunningDoesNotDuplicate()
    {
        var r = new ReviewRegister();
        r.Report("Gates", new[] { Gate("1") }, T0);
        Assert.Equal(0, r.Report("Gates", new[] { Gate("1"), Gate("1") }, T0.AddHours(1)));
        var item = Assert.Single(r.Items);
        Assert.Equal(T0, item.FirstSeen);
        Assert.Equal(T0.AddHours(1), item.LastSeen);
    }

    [Fact]
    public void AnItemTheCheckNoLongerFindsIsResolved()
    {
        var r = new ReviewRegister();
        r.Report("Gates", new[] { Gate("1"), Gate("2") }, T0);
        r.Report("Gates", new[] { Gate("2") }, T0.AddHours(1));
        Assert.Equal(ReviewItemStatus.Resolved, r.Find("gate.unpaired:1")!.Status);
        Assert.Contains("no longer found", r.Find("gate.unpaired:1")!.Note);
        Assert.Equal(ReviewItemStatus.Open, r.Find("gate.unpaired:2")!.Status);

        // Found again: open again.
        r.Report("Gates", new[] { Gate("1"), Gate("2") }, T0.AddHours(2));
        Assert.Equal(ReviewItemStatus.Open, r.Find("gate.unpaired:1")!.Status);
    }

    [Fact]
    public void APartialCheckResolvesNothingAndOtherChecksAreUntouched()
    {
        var r = new ReviewRegister();
        r.Report("Gates", new[] { Gate("1") }, T0);
        r.Report("Codes", new[] { ReviewKinds.New(ReviewKinds.CodeUnknown, "XYZ", "no rule") }, T0);
        r.Report("Gates", new ReviewItem[0], T0.AddHours(1), complete: false);
        Assert.Equal(ReviewItemStatus.Open, r.Find("gate.unpaired:1")!.Status);
        r.Report("Gates", new ReviewItem[0], T0.AddHours(1));
        Assert.Equal(ReviewItemStatus.Resolved, r.Find("gate.unpaired:1")!.Status);
        Assert.Equal(ReviewItemStatus.Open, r.Find("code.unknown:XYZ")!.Status);
    }

    [Fact]
    public void NotApplicableNeedsAReasonAndSurvivesReruns()
    {
        var r = new ReviewRegister();
        r.Report("Gates", new[] { Gate("1") }, T0);
        Assert.False(r.MarkNotApplicable("gate.unpaired:1", "  ", "MC", T0, out var problem));
        Assert.NotNull(problem);
        Assert.True(r.MarkNotApplicable("gate.unpaired:1", "lone post, gate removed", "MC", T0, out problem));
        r.Report("Gates", new[] { Gate("1") }, T0.AddDays(1));
        var item = r.Find("gate.unpaired:1")!;
        Assert.Equal(ReviewItemStatus.NotApplicable, item.Status);
        Assert.Equal("lone post, gate removed", item.Note);
        Assert.Equal("MC", item.ReviewedBy);
        Assert.Empty(r.RequiredOpen);
    }

    [Fact]
    public void AcceptanceIsRecorded()
    {
        var r = new ReviewRegister();
        r.Report("Gates", new[] { Gate("1") }, T0);
        Assert.True(r.Accept("gate.unpaired:1", null, "MC", T0.AddMinutes(5)));
        var item = r.Find("gate.unpaired:1")!;
        Assert.Equal(ReviewItemStatus.Accepted, item.Status);
        Assert.Equal("MC", item.ReviewedBy);
        Assert.Equal(T0.AddMinutes(5), item.ReviewedAt);
        Assert.True(r.Reopen("gate.unpaired:1"));
        Assert.Equal(ReviewItemStatus.Open, item.Status);
    }

    [Fact]
    public void QcCompleteIsRefusedWhileRequiredItemsAreOpen()
    {
        var r = new ReviewRegister();
        var s = new ReviewSettings();
        r.Report("Gates", new[] { Gate("1") }, T0, settings: s);
        Assert.False(r.MarkQcComplete(s, "MC", T0, out var problem));
        Assert.Contains("1 required review item", problem);
        Assert.Contains("Gate post has no partner (1)", problem);
        Assert.Null(r.QcCompleteAt);

        r.MarkNotApplicable("gate.unpaired:1", "lone post", "MC", T0, out _);
        Assert.True(r.MarkQcComplete(s, "MC", T0.AddHours(1), out problem));
        Assert.Equal(T0.AddHours(1), r.QcCompleteAt);
        Assert.Equal("MC", r.QcCompleteBy);
    }

    [Fact]
    public void ANewRequiredItemTakesTheDrawingBackOutOfQcComplete()
    {
        var r = new ReviewRegister();
        var s = new ReviewSettings();
        Assert.True(r.MarkQcComplete(s, "MC", T0, out _));
        r.Report("Gates", new[] { Gate("5") }, T0.AddHours(1), settings: s);
        Assert.Null(r.QcCompleteAt);
    }

    [Fact]
    public void WhatIsRequiredIsASetting()
    {
        var s = new ReviewSettings();
        Assert.True(s.IsRequired(ReviewKinds.GateUnpaired));
        Assert.False(s.IsRequired(ReviewKinds.LineUnidentified));
        s.Required[ReviewKinds.GateUnpaired] = false;
        s.Required[ReviewKinds.LineUnidentified] = true;
        Assert.False(s.IsRequired(ReviewKinds.GateUnpaired));
        Assert.True(s.IsRequired(ReviewKinds.LineUnidentified));

        var r = new ReviewRegister();
        r.Report("Gates", new[] { Gate("1") }, T0, settings: s);
        Assert.False(r.Find("gate.unpaired:1")!.Required);
        Assert.True(r.MarkQcComplete(s, "MC", T0, out _));

        var problems = new List<string>();
        s.Required["no.such.kind"] = true;
        s.Validate(problems);
        Assert.Single(problems);
    }

    [Fact]
    public void QcCanBeAllowedWithRequiredItemsOpen()
    {
        var s = new ReviewSettings { BlockQcComplete = false };
        var r = new ReviewRegister();
        r.Report("Gates", new[] { Gate("1") }, T0, settings: s);
        Assert.True(r.MarkQcComplete(s, "MC", T0, out _));
    }

    [Fact]
    public void TheRegisterTravelsAsJson()
    {
        var r = new ReviewRegister();
        r.Report("Gates", new[] { Gate("1"), Gate("2") }, T0);
        r.MarkNotApplicable("gate.unpaired:2", "removed", "MC", T0, out _);
        var back = ReviewRegister.FromJson(r.ToJson());
        Assert.Equal(2, back.Items.Count);
        Assert.Equal(ReviewItemStatus.NotApplicable, back.Find("gate.unpaired:2")!.Status);
        Assert.Equal(new[] { "H1" }, back.Find("gate.unpaired:1")!.Handles);
        Assert.Equal(1, back.Find("gate.unpaired:1")!.X);

        Assert.Empty(ReviewRegister.FromJson(null).Items);
        Assert.Empty(ReviewRegister.FromJson("{ not json").Items);
    }

    [Fact]
    public void EveryKindHasWordingAndAction()
    {
        Assert.Equal(ReviewKinds.All.Count, ReviewKinds.All.Select(k => k.Id).Distinct().Count());
        foreach (var k in ReviewKinds.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(k.Title), k.Id);
            Assert.False(string.IsNullOrWhiteSpace(k.Action), k.Id);
            Assert.False(string.IsNullOrWhiteSpace(k.Source), k.Id);
        }
    }
}
