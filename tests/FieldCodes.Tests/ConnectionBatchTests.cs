using FieldCodes.Settings;
using FieldCodes.Utilities;

namespace FieldCodes.Tests;

/// <summary>Find all connections: every open pipe searched at once, paired across the project, High ones confirmed.</summary>
public sealed class ConnectionBatchTests
{
    private static StructureRecord S(UtilityProject project, string number, double north, double east, double rim = 100, UtilitySystem system = UtilitySystem.Storm)
    {
        var s = new StructureRecord { System = system, StructureType = "SDMH", Cad = new CadStructureSnapshot { PointNumber = number, Rim = rim, Northing = north, Easting = east } };
        s.Field.PointNumber = number;
        s.Field.FieldCode = "SDMH";
        project.Structures.Add(s);
        return s;
    }

    private static PipeObservation P(StructureRecord s, string direction, double size, string material, double dip)
    {
        var p = new QuickPipeEntry { Direction = DirectionShortcuts.Parse(direction), SizeIn = size, Material = material, MeasuredDip = dip, Reference = MeasurementReference.Invert }.Create();
        s.Field.Pipes.Add(p);
        return p;
    }

    [Fact]
    public void TwoEndsOfOnePipeBecomeOneHighConnection()
    {
        var project = new UtilityProject();
        var a = S(project, "1", 0, 0);
        var b = S(project, "2", 200, 0);
        var up = P(a, "N", 12, "RCP", 5);
        var down = P(b, "S", 12, "RCP", 6);

        var found = ConnectionBatch.FindAll(project, new UtilitySettings());
        var real = found.Where(f => f.ToStructureId != null).ToList();
        Assert.Single(real);                                   // not one per end
        Assert.Equal(Confidence.High, real[0].Confidence);
        Assert.Empty(project.Connections);                     // finding changes nothing

        Assert.Equal(1, ConnectionBatch.ConfirmHigh(project, found, new UtilitySettings()));
        var c = project.Connections.Single();
        Assert.Equal(ConnectionStatus.Confirmed, c.Status);
        Assert.Same(c, project.ConnectionFor(a.Id, up.Id));
        Assert.Same(c, project.ConnectionFor(b.Id, down.Id));  // the far pipe is its other end
        Assert.Contains(ConnectionBatch.AutoBasis, c.Basis);
        Assert.Contains(project.Overrides, o => o.Target == c.Id && o.What == "Confirmed by Find all connections");
        Assert.True(real[0].AutoConfirmed);
        Assert.NotNull(SlopeCalculator.Compute(project, c).SlopePercent);
    }

    [Fact]
    public void MediumAndLowWaitForTheDrafter()
    {
        var project = new UtilityProject();
        var a = S(project, "1", 0, 0);
        var b = S(project, "2", 200, 0);
        var c = S(project, "3", 0, 200);
        P(a, "N", 12, "RCP", 5);
        P(b, "S", 15, "RCP", 6);        // size differs: Medium
        P(a, "E", 8, "PVC", 4);         // nothing observed at 3 yet: Low

        var found = ConnectionBatch.FindAll(project, new UtilitySettings());
        Assert.Equal(new[] { Confidence.Medium, Confidence.Low }, found.Where(f => f.ToStructureId != null).Select(f => f.Confidence));
        Assert.Equal(c.Id, found.Single(f => f.Confidence == Confidence.Low).ToStructureId);
        Assert.Equal(0, ConnectionBatch.ConfirmHigh(project, found, new UtilitySettings()));
        Assert.Empty(project.Connections);
    }

    [Fact]
    public void AFarPipeServesOnlyOnePipe()
    {
        // Two pipes at 1 and 3 both aim at 2, whose single west pipe points back toward 1 more closely.
        var project = new UtilityProject();
        var a = S(project, "1", 0, 0);
        var b = S(project, "2", 0, 200);
        var d = S(project, "3", 30, 0);
        P(a, "E", 12, "RCP", 5);
        P(d, "E", 12, "RCP", 5);
        var back = P(b, "W", 12, "RCP", 6);

        var found = ConnectionBatch.FindAll(project, new UtilitySettings());
        var usingBack = found.Where(f => f.MatchingPipeId == back.Id).ToList();
        Assert.Single(usingBack);
        Assert.Equal(a.Id, usingBack[0].FromStructureId);
        // The other still runs to 2, but with no matching pipe of its own there.
        Assert.Contains(found, f => f.FromStructureId == d.Id && f.ToStructureId == b.Id && f.MatchingPipeId == null);
    }

    [Fact]
    public void ConnectedPipesAreLeftAloneAndTheRestAreExplained()
    {
        var project = new UtilityProject();
        var a = S(project, "1", 0, 0);
        var b = S(project, "2", 200, 0);
        var up = P(a, "N", 12, "RCP", 5);
        var done = P(a, "W", 8, "PVC", 5);
        ConnectionFinder.MarkOutsideLimits(project, a, done);
        var lost = new QuickPipeEntry { Direction = ObservedDirection.Unknown("?"), SizeIn = 6 }.Create();
        a.Field.Pipes.Add(lost);
        var lonely = P(b, "E", 10, "RCP", 5);   // nothing east of 2

        var found = ConnectionBatch.FindAll(project, new UtilitySettings());
        Assert.DoesNotContain(found, f => f.PipeId == done.Id);
        Assert.Contains("no direction", found.Single(f => f.PipeId == lost.Id).NothingBecause);
        Assert.Contains("no surveyed structure", found.Single(f => f.PipeId == lonely.Id).NothingBecause);
        Assert.Equal(Confidence.None, found.Last().Confidence);
        Assert.Contains(found, f => f.PipeId == up.Id && f.ToStructureId == b.Id);
    }

    [Fact]
    public void TheSameSearchAsOnePipe()
    {
        var project = new UtilityProject();
        var a = S(project, "1", 0, 0);
        S(project, "2", 200, 5);
        S(project, "3", 150, -10);
        var up = P(a, "N", 12, "RCP", 5);
        var one = ConnectionFinder.Find(project, a, up, new UtilitySettings()).First();
        var all = ConnectionBatch.FindAll(project, new UtilitySettings()).Single(f => f.PipeId == up.Id);
        Assert.Equal(one.Structure.Id, all.ToStructureId);
        Assert.Equal(one.Confidence, all.Confidence);
    }
}
