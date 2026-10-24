using FieldCodes.Easements;
using FieldCodes.Lots;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// FTFLOTS's geometry and wording, with no CAD: lots cut square to the front, alleys, rounded
/// corners, setbacks and easements, drainage arrows, and the checks that frontages and areas add up.
/// </summary>
public class LotTests
{
    private static LotJob Block(double width = 200, double depth = 100, params double[] widths)
    {
        var job = new LotJob { BlockName = "A" };
        job.Corners.AddRange(new[] { new P2(0, 0), new P2(width, 0), new P2(width, depth), new P2(0, depth) });
        job.RowA.FrontSide = 0;
        job.RowA.WidthsFeet.AddRange(widths.Length > 0 ? widths : new[] { 50.0, 50.0, 50.0, 50.0 });
        return job;
    }

    private static LotPlan Plan(LotJob job)
    {
        var plan = LotBuilder.Build(job);
        Assert.True(plan.Ok, string.Join("; ", plan.Errors));
        return plan;
    }

    private static bool Near(P2 a, double x, double y) { return a.DistanceTo(new P2(x, y)) < 1e-6; }

    private static (double minX, double minY, double maxX, double maxY) Box(IEnumerable<P2> points)
    {
        var list = points.ToList();
        return (list.Min(p => p.X), list.Min(p => p.Y), list.Max(p => p.X), list.Max(p => p.Y));
    }

    // -------------------------------------------------------------- division

    [Fact]
    public void ABlockIsCutIntoLotsSquareToTheFront()
    {
        var plan = Plan(Block());

        Assert.Equal(4, plan.Lots.Count);
        Assert.Equal(new[] { "1", "2", "3", "4" }, plan.Lots.Select(l => l.Number).ToArray());
        Assert.All(plan.Lots, l => Assert.Equal(5000.0, l.AreaSqFt, 6));
        Assert.All(plan.Lots, l => Assert.Equal(50.0, l.FrontageFeet, 6));
        Assert.All(plan.Lots, l => Assert.Equal(100.0, l.MeanDepthFeet, 6));
        var second = Box(plan.Lots[1].Polygon);
        Assert.Equal((50.0, 0.0, 100.0, 100.0), second);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void OnlyTheEndLotsAreCornerLots()
    {
        var plan = Plan(Block());
        Assert.Equal(new[] { true, false, false, true }, plan.Lots.Select(l => l.IsCorner).ToArray());
    }

    [Fact]
    public void ALotLineTwoLotsShareIsDrawnOnce()
    {
        var plan = Plan(Block());

        Assert.Equal(3, plan.Lines.Count(l => l.Kind == LotLineKind.Lot));
        Assert.All(plan.Lines.Where(l => l.Kind == LotLineKind.Lot), l => Assert.Equal(100.0, l.Data.Length, 6));
        // Front 4, rear 4 and the two ends make up the block's own boundary.
        Assert.Equal(10, plan.Lines.Count(l => l.Kind == LotLineKind.Perimeter));
        Assert.All(plan.Lines.Where(l => l.Kind == LotLineKind.Perimeter), l => Assert.True(l.DistanceOnly));
    }

    [Fact]
    public void ABlockPickedClockwiseDividesTheSame()
    {
        var job = Block();
        job.Corners.Clear();
        job.Corners.AddRange(new[] { new P2(0, 100), new P2(200, 100), new P2(200, 0), new P2(0, 0) });
        // Side 2 runs (200,0) to (0,0): the front, heading west.
        job.RowA.FrontSide = 2;
        var plan = Plan(job);

        Assert.All(plan.Lots, l => Assert.Equal(5000.0, l.AreaSqFt, 6));
        Assert.Equal((150.0, 0.0, 200.0, 100.0), Box(plan.Lots[0].Polygon));
    }

    [Fact]
    public void ASkewedBlockEndsItsEndLotsOnTheBlockSides()
    {
        var job = Block();
        job.Corners.Clear();
        job.Corners.AddRange(new[] { new P2(0, 0), new P2(200, 0), new P2(180, 100), new P2(20, 100) });
        var plan = Plan(job);

        Assert.Equal(18000.0, plan.BlockAreaSqFt, 6);
        Assert.Equal(18000.0, plan.Lots.Sum(l => l.AreaSqFt), 6);
        Assert.Equal(4000.0, plan.Lots[0].AreaSqFt, 6);        // x from the slanted side to 50: 50 - 0.2y, mean 40
        Assert.Equal(5000.0, plan.Lots[1].AreaSqFt, 6);
        Assert.Empty(plan.Warnings);
    }

    // ---------------------------------------------------------------- frontages

    [Fact]
    public void EqualFrontagesCanCarryExtraOnTheCornerLots()
    {
        Assert.Equal(new[] { 50.0, 50.0, 50.0, 50.0 }, LotRow.EqualWidths(200, 4, 0).ToArray());

        var widths = LotRow.EqualWidths(200, 4, 5);
        Assert.Equal(new[] { 52.5, 47.5, 47.5, 52.5 }, widths.ToArray());
        Assert.Equal(200.0, widths.Sum(), 9);

        Assert.Equal(new[] { 100.0 }, LotRow.EqualWidths(100, 1, 5).ToArray());
        Assert.Empty(LotRow.EqualWidths(0, 3, 0));
    }

    [Fact]
    public void FrontagesThatMissTheSideAreRefusedUnlessALotTakesTheDifference()
    {
        var job = Block(200, 100, 50, 50, 50, 49);
        var refused = LotBuilder.Build(job);
        Assert.False(refused.Ok);
        Assert.Contains("add up to", refused.Errors[0]);

        job.RowA.Close = LotWidthClose.LastLot;
        var plan = Plan(job);
        Assert.Equal(50.0, plan.Lots[3].FrontageFeet, 9);
        Assert.Contains(plan.Notes, n => n.Contains("last lot"));

        job.RowA.Close = LotWidthClose.FirstLot;
        job.RowA.WidthsFeet[0] = 50;
        Assert.Equal(51.0, Plan(job).Lots[0].FrontageFeet, 9);
    }

    [Fact]
    public void AFrontageWithinTheToleranceNeedsNoAdjusting()
    {
        var job = Block(200, 100, 50, 50, 50, 49.995);
        var plan = Plan(job);
        Assert.Equal(49.995, plan.Lots[3].FrontageFeet, 9);
    }

    // --------------------------------------------------------------- depth, alley

    [Fact]
    public void AFixedDepthLeavesTheRestOfTheBlockUndivided()
    {
        var job = Block();
        job.RowA.DepthFeet = 80;
        var plan = Plan(job);

        Assert.All(plan.Lots, l => Assert.Equal(4000.0, l.AreaSqFt, 6));
        Assert.Equal(4000.0, plan.RemainderSqFt, 6);
        Assert.Contains(plan.Notes, n => n.Contains("not divided"));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void ADepthDeeperThanTheBlockIsRefused()
    {
        var job = Block();
        job.RowA.DepthFeet = 100;
        Assert.Contains("reaches the back", LotBuilder.Build(job).Errors[0]);
    }

    [Fact]
    public void ARearAlleyTakesItsWidthFromTheBackOfTheLots()
    {
        var job = Block();
        job.Alley = LotAlleyMode.Rear;
        job.AlleyWidthFeet = 20;
        var plan = Plan(job);

        Assert.All(plan.Lots, l => Assert.Equal(4000.0, l.AreaSqFt, 6));
        Assert.NotNull(plan.Alley);
        Assert.Equal(4000.0, plan.Alley!.AreaSqFt, 6);
        Assert.Equal(20.0, plan.Alley.WidthFeet, 9);
        // The lots' rear lines already draw the alley's near edge; only its far edge and ends are new.
        Assert.Equal(3, plan.Lines.Count(l => l.Kind == LotLineKind.Alley));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void ARearAlleyRunsAlongTheBackSideEvenWhenTheBlockIsNotSquare()
    {
        var job = Block();
        job.Corners.Clear();
        job.Corners.AddRange(new[] { new P2(0, 0), new P2(200, 0), new P2(200, 100), new P2(0, 130) });
        job.Alley = LotAlleyMode.Rear;
        job.AlleyWidthFeet = 20;
        var plan = Plan(job);

        Assert.Equal(plan.BlockAreaSqFt, plan.Lots.Sum(l => l.AreaSqFt) + plan.Alley!.AreaSqFt, 4);
        // The alley is a constant 20 feet wide measured from the slanted back, not from the front.
        var alley = plan.Alley.Polygon;
        Assert.Contains(alley, p => Math.Abs(p.X) < 1e-6);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void AnAlleyBetweenTwoRowsSplitsTheBlockEvenly()
    {
        var job = Block(200, 160);
        job.RowB = new LotRow { FrontSide = 2 };
        job.RowB.WidthsFeet.AddRange(new[] { 50.0, 50.0, 50.0, 50.0 });
        job.Alley = LotAlleyMode.MidBlock;
        job.AlleyWidthFeet = 20;
        var plan = Plan(job);

        Assert.Equal(8, plan.Lots.Count);
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7", "8" }, plan.Lots.Select(l => l.Number).ToArray());
        Assert.All(plan.Lots, l => Assert.Equal(3500.0, l.AreaSqFt, 6));
        Assert.Equal(4000.0, plan.Alley!.AreaSqFt, 6);
        Assert.Equal(0, plan.Lots[0].Row);
        Assert.Equal(1, plan.Lots[4].Row);
        // The second row is numbered on round the block: it starts at the east end of the top side.
        Assert.True(plan.Lots[4].LabelPoint.X > 150);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void OneDepthFixesTheOtherWhenTheAlleyWidthIsKnown()
    {
        var job = Block(200, 160);
        job.RowB = new LotRow { FrontSide = 2 };
        job.RowB.WidthsFeet.AddRange(new[] { 100.0, 100.0 });
        job.Alley = LotAlleyMode.MidBlock;
        job.AlleyWidthFeet = 20;
        job.RowA.DepthFeet = 80;
        var plan = Plan(job);

        Assert.Equal(4000.0, plan.Lots.First(l => l.Row == 0).AreaSqFt, 6);
        Assert.Equal(6000.0, plan.Lots.First(l => l.Row == 1).AreaSqFt, 6);     // 100 x 60
    }

    [Fact]
    public void DepthsAndAnAlleyThatDoNotAddUpToTheBlockAreRefused()
    {
        var job = Block(200, 160);
        job.RowB = new LotRow { FrontSide = 2, DepthFeet = 70 };
        job.RowB.WidthsFeet.AddRange(new[] { 100.0, 100.0 });
        job.RowA.DepthFeet = 70;
        job.Alley = LotAlleyMode.MidBlock;
        job.AlleyWidthFeet = 30;
        var plan = LotBuilder.Build(job);
        Assert.Contains("do not add up", plan.Errors[0]);
    }

    [Fact]
    public void TwoRowsWithNoAlleyShareTheBlockBackToBack()
    {
        var job = Block(200, 160);
        job.RowB = new LotRow { FrontSide = 2 };
        job.RowB.WidthsFeet.AddRange(new[] { 100.0, 100.0 });
        var plan = Plan(job);

        Assert.Null(plan.Alley);
        Assert.Equal(160.0 * 200.0, plan.Lots.Sum(l => l.AreaSqFt), 6);
    }

    // ---------------------------------------------------------------- corners

    [Fact]
    public void ARoundedBlockCornerRoundsTheLotAtThatCorner()
    {
        var job = Block();
        job.CornerRadiiFeet.AddRange(new[] { 15.0, 0, 0, 0 });
        var plan = Plan(job);

        var lot = plan.Lots[0];
        Assert.Contains(lot.Boundary, c => c.Kind == CourseKind.Arc && Math.Abs(c.Radius - 15) < 1e-9);
        Assert.Equal(5000.0 - 15 * 15 * (1 - Math.PI / 4), lot.AreaSqFt, 6);
        Assert.Equal(5000.0, plan.Lots[1].AreaSqFt, 6);
        var arc = plan.Lines.Single(l => l.Course.Kind == CourseKind.Arc);
        Assert.Equal(LotLineKind.Perimeter, arc.Kind);
        Assert.Equal(90.0, arc.Data.DeltaDegrees!.Value, 6);
    }

    [Fact]
    public void ARadiusLargerThanTheLotIsLeftSharpAndSaid()
    {
        var job = Block(200, 100, 20, 180);
        job.CornerRadiiFeet.AddRange(new[] { 30.0, 0, 0, 0 });
        var plan = Plan(job);

        Assert.Contains(plan.Warnings, w => w.Contains("Lot 1") && w.Contains("do not fit"));
    }

    // ----------------------------------------------------- setbacks and easements

    [Fact]
    public void SetbacksBecomeABuildingEnvelopeInsideEachLot()
    {
        var job = Block();
        job.FrontSetbackFeet = 25;
        job.SideSetbackFeet = 5;
        job.CornerSideSetbackFeet = 15;
        job.RearSetbackFeet = 20;
        var plan = Plan(job);

        var setbacks = plan.Envelopes.Where(e => e.Kind == LotOffsetKind.Setback).ToList();
        Assert.Equal(4, setbacks.Count);
        Assert.Equal((55.0, 25.0, 95.0, 80.0), Box(setbacks[1].Polygon));
        // The corner lot keeps its larger street-side setback on the block's own side only.
        Assert.Equal((15.0, 25.0, 45.0, 80.0), Box(setbacks[0].Polygon));
        Assert.Empty(plan.Envelopes.Where(e => e.Kind == LotOffsetKind.Easement));
    }

    [Fact]
    public void UtilityEasementsAreTheirOwnEnvelope()
    {
        var job = Block();
        job.FrontEasementFeet = 10;
        job.SideEasementFeet = 5;
        job.RearEasementFeet = 10;
        var plan = Plan(job);

        var easement = plan.Envelopes.Single(e => e.Kind == LotOffsetKind.Easement && e.LotIndex == 1);
        Assert.Equal((55.0, 10.0, 95.0, 90.0), Box(easement.Polygon));
    }

    [Fact]
    public void SetbacksThatLeaveNothingToBuildOnAreReported()
    {
        var job = Block();
        job.SideSetbackFeet = 30;
        var plan = Plan(job);
        Assert.Contains(plan.Warnings, w => w.Contains("Lot 2") && w.Contains("no buildable area"));
    }

    // ---------------------------------------------------------------- drainage

    [Fact]
    public void EveryLotGetsADrainageArrowTowardTheStreetByDefault()
    {
        var plan = Plan(Block());
        var arrow = plan.Lots[1].Drains.Single();

        Assert.Equal(2.0, arrow.Percent, 9);
        Assert.True(arrow.To.Y < arrow.From.Y);            // the front is at the bottom
        Assert.True(Math.Abs(arrow.To.X - arrow.From.X) < 1e-9);
        Assert.True(arrow.From.DistanceTo(arrow.To) <= 20.0 + 1e-9);
    }

    [Fact]
    public void ALotCanDrainToTheRearOrEitherSide()
    {
        var job = Block();
        job.DrainOverrides[2] = LotDrain.ToRear;
        job.DrainOverrides[3] = LotDrain.ToLeft;
        job.DrainOverrides[4] = LotDrain.ToRight;
        var plan = Plan(job);

        Assert.True(plan.Lots[1].Drains[0].To.Y > plan.Lots[1].Drains[0].From.Y);
        // Standing on the front line looking into the lot (north), left is west.
        Assert.True(plan.Lots[2].Drains[0].To.X < plan.Lots[2].Drains[0].From.X);
        Assert.True(plan.Lots[3].Drains[0].To.X > plan.Lots[3].Drains[0].From.X);
    }

    [Fact]
    public void ARidgeLotDrainsBothWaysAndNoDrainageDrawsNothing()
    {
        var job = Block();
        job.DrainOverrides[1] = LotDrain.FrontAndRear;
        job.DrainOverrides[2] = LotDrain.None;
        var plan = Plan(job);

        Assert.Equal(2, plan.Lots[0].Drains.Count);
        Assert.Empty(plan.Lots[1].Drains);
    }

    [Fact]
    public void DrainageTowardTheStreetFollowsTheRowOnTheOtherSideOfTheBlock()
    {
        var job = Block(200, 160);
        job.RowB = new LotRow { FrontSide = 2 };
        job.RowB.WidthsFeet.AddRange(new[] { 100.0, 100.0 });
        job.Alley = LotAlleyMode.MidBlock;
        job.AlleyWidthFeet = 20;
        var plan = Plan(job);

        var south = plan.Lots.First(l => l.Row == 0).Drains.Single();
        var north = plan.Lots.First(l => l.Row == 1).Drains.Single();
        Assert.True(south.To.Y < south.From.Y);
        Assert.True(north.To.Y > north.From.Y);
    }

    // -------------------------------------------------------------------- checks

    [Fact]
    public void LotsUnderTheMinimumsAreFlagged()
    {
        var job = Block();
        job.MinLotAreaSqFt = 6000;
        job.MinLotWidthFeet = 60;
        var plan = Plan(job);

        Assert.Equal(4, plan.Warnings.Count(w => w.Contains("sq ft, under the 6,000")));
        Assert.Equal(4, plan.Warnings.Count(w => w.Contains("under the 60'")));
    }

    [Fact]
    public void BadInputIsRefusedWithAnExplanation()
    {
        var job = Block();
        job.Corners.RemoveRange(2, 2);
        Assert.Contains("at least 3", LotBuilder.Build(job).Errors[0]);

        job = Block();
        job.RowA.FrontSide = 9;
        Assert.Contains("does not front", LotBuilder.Build(job).Errors[0]);

        job = Block(200, 100, 50, 0, 150);
        Assert.Contains("greater than zero", LotBuilder.Build(job).Errors[0]);

        job = Block();
        job.RowB = new LotRow { FrontSide = 0 };
        job.RowB.WidthsFeet.Add(200);
        Assert.Contains("same side", LotBuilder.Build(job).Errors.Single(e => e.Contains("same side")));

        job = Block();
        job.Alley = LotAlleyMode.MidBlock;
        Assert.Contains("second row", LotBuilder.Build(job).Errors[0]);

        job = Block();
        job.Alley = LotAlleyMode.Rear;
        Assert.Contains("alley's width", LotBuilder.Build(job).Errors[0]);

        job = Block();
        job.Alley = LotAlleyMode.Rear;
        job.AlleyWidthFeet = 20;
        job.BackSide = 0;
        Assert.Contains("cannot run along the side the lots front", LotBuilder.Build(job).Errors[0]);

        Assert.False(LotBuilder.Build(null).Ok);
    }

    [Fact]
    public void ABlockThatIsNotConvexGetsAWarning()
    {
        var job = new LotJob();
        job.Corners.AddRange(new[] { new P2(0, 0), new P2(200, 0), new P2(200, 100), new P2(100, 50), new P2(0, 100) });
        job.RowA.FrontSide = 0;
        job.RowA.WidthsFeet.AddRange(new[] { 100.0, 100.0 });
        var plan = LotBuilder.Build(job);
        Assert.Contains(plan.Warnings, w => w.Contains("not convex"));
    }

    [Fact]
    public void DrawingUnitsThatAreNotFeetStillGiveFeet()
    {
        // Half-foot drawing units: a 400 x 200 unit block is 200 x 100 feet.
        var job = Block(400, 200);
        job.UnitsPerFoot = 2.0;
        var plan = Plan(job);

        Assert.All(plan.Lots, l => Assert.Equal(5000.0, l.AreaSqFt, 6));
        Assert.Equal(20000.0, plan.BlockAreaSqFt, 6);
        Assert.Equal(50.0, plan.Lots[0].FrontageFeet, 9);
    }

    // --------------------------------------------------------------------- words

    [Fact]
    public void LotsAreLabelledInTheOfficeWording()
    {
        var job = Block();
        var plan = Plan(job);
        LotLabels.Apply(plan, job, new LotSettings(), new DraftingLineType(), new DraftingLineType(), 1.0, "°");

        Assert.Equal(new[] { "LOT 1", "5,000 SF" }, plan.Lots[0].Text.ToArray());
        Assert.Equal("BLOCK A", plan.Title);
        Assert.Equal("2%", plan.Lots[0].Drains[0].Text);
    }

    [Fact]
    public void LotLinesCarryBearingAndDistanceButTheBlockBoundaryOnlyItsDistance()
    {
        var job = Block();
        var plan = Plan(job);
        LotLabels.Apply(plan, job, new LotSettings(), new DraftingLineType(), new DraftingLineType(), 1.0, "°");

        var inner = plan.Lines.First(l => l.Kind == LotLineKind.Lot);
        Assert.Contains("100.00'", inner.Text.Single());
        Assert.Contains("00°00'00\"", inner.Text.Single());

        var edge = plan.Lines.First(l => l.Kind == LotLineKind.Perimeter && Math.Abs(l.Data.Length - 50) < 1e-9);
        Assert.Equal("50.00'", edge.Text.Single());
    }

    [Fact]
    public void ThePlatNotesStateTheSetbacksAndEasementsThatWereUsed()
    {
        var job = Block();
        job.FrontSetbackFeet = 25;
        job.SideSetbackFeet = 5;
        job.CornerSideSetbackFeet = 15;
        job.RearSetbackFeet = 20;
        job.FrontEasementFeet = 10;
        job.RearEasementFeet = 7.5;
        var plan = Plan(job);
        LotLabels.Apply(plan, job, new LotSettings(), new DraftingLineType(), new DraftingLineType(), 1.0, "°");

        Assert.Equal("BUILDING SETBACKS: FRONT 25', SIDE 5', CORNER SIDE 15', REAR 20'", plan.GeneralNotes[0]);
        Assert.Equal("UTILITY EASEMENTS: FRONT 10', REAR 7.5'", plan.GeneralNotes[1]);
    }

    [Fact]
    public void NoSetbacksMeansNoNotes()
    {
        var job = Block();
        var plan = Plan(job);
        LotLabels.Apply(plan, job, new LotSettings(), new DraftingLineType(), new DraftingLineType(), 1.0, "°");
        Assert.Empty(plan.GeneralNotes);
    }

    [Fact]
    public void TheAlleyIsLabelledWithItsWidth()
    {
        var job = Block();
        job.Alley = LotAlleyMode.Rear;
        job.AlleyWidthFeet = 20;
        var plan = Plan(job);
        LotLabels.Apply(plan, job, new LotSettings(), new DraftingLineType(), new DraftingLineType(), 1.0, "°");
        Assert.Equal("20' ALLEY", plan.Alley!.Text);
    }

    [Fact]
    public void TheOfficeCanReword()
    {
        var job = Block();
        var plan = Plan(job);
        var settings = new LotSettings
        {
            UpperCase = false, LotNumberFormat = "Lot {num}, Block {block}", LotAreaFormat = "{sf} sq. ft.",
            LotAcresFormat = "{ac} ac.", BlockTitleFormat = string.Empty, DrainFormat = string.Empty
        };
        LotLabels.Apply(plan, job, settings, new DraftingLineType(), new DraftingLineType(), 1.0, "°");

        Assert.Equal(new[] { "Lot 1, Block A", "5,000 sq. ft.", "0.115 ac." }, plan.Lots[0].Text.ToArray());
        Assert.Equal(string.Empty, plan.Title);
        Assert.Equal(string.Empty, plan.Lots[0].Drains[0].Text);
    }

    // ------------------------------------------------------------------ settings

    [Fact]
    public void TheShippedLotStandardIsValidAndLeavesLayersUnconfigured()
    {
        var settings = new LotSettings();
        var problems = new List<string>();
        settings.Validate(problems);
        Assert.Empty(problems);
        Assert.Equal(string.Empty, settings.SetbackLayer);
        Assert.Equal(string.Empty, settings.EasementLayer);
        Assert.Equal("Lot Line", settings.LotLineTypeName);
    }

    [Fact]
    public void BrokenLotSettingsAreReported()
    {
        var settings = new LotSettings { AlleyFormat = "ALLEY", DrainFormat = "slope", DefaultSlopePercent = -1, TextHeightPlotted = 0 };
        var problems = new List<string>();
        settings.Validate(problems);
        Assert.Equal(4, problems.Count);
    }

    [Fact]
    public void LotSettingsSurviveARoundTripAndAnOldFileGetsDefaults()
    {
        var s = new FtfSettings();
        s.Lots.SetbackLayer = "V-LOT-SBCK";
        s.Lots.DefaultFrontSetbackFeet = 25;
        var again = Newtonsoft.Json.JsonConvert.DeserializeObject<FtfSettings>(Newtonsoft.Json.JsonConvert.SerializeObject(s))!;
        Assert.Equal("V-LOT-SBCK", again.Lots.SetbackLayer);
        Assert.Equal(25.0, again.Lots.DefaultFrontSetbackFeet);

        var old = Newtonsoft.Json.JsonConvert.DeserializeObject<FtfSettings>("{\"version\":\"1\"}")!;
        old.FillMissingSections();
        Assert.NotNull(old.Lots);
    }
}
