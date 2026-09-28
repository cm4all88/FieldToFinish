using FieldCodes.Settings;
using FieldCodes.Utilities;

namespace FieldCodes.Tests;

/// <summary>
/// Connecting a pipe the moment it is entered, so the drafter is not left with a pass
/// at the end. It confirms exactly what Find all connections confirms on its own -- a
/// pipe observed at both ends -- and nothing else.
/// </summary>
public sealed class ConnectAsEnteredTests
{
    private static readonly UtilitySettings Settings = new UtilitySettings();

    private static StructureRecord At(string number, double east, double north, string code = "SDMH")
    {
        return new StructureRecord
        {
            Id = "S" + number,
            StructureType = code,
            System = UtilitySystem.Storm,
            Cad = new CadStructureSnapshot { Easting = east, Northing = north, Rim = 100 },
            Field = { PointNumber = number }
        };
    }

    private static PipeObservation Pipe(string direction, double size, string material, double dip)
    {
        return new QuickPipeEntry
        {
            Direction = DirectionShortcuts.For(direction), SizeIn = size, Material = material, MeasuredDip = dip
        }.Create();
    }

    [Fact]
    public void APipeObservedAtBothEndsConnectsAsItIsEntered()
    {
        var a = At("1045", 0, 0);
        var b = At("1046", 0, 180);
        a.Field.Pipes.Add(Pipe("N", 12, "RCP", 6.41));
        var project = new UtilityProject { Structures = { a, b } };

        // Nothing at the far end yet: nothing to confirm.
        Assert.False(ConnectionBatch.ConnectAsEntered(project, Settings, a.Id, a.Field.Pipes[0].Id));
        Assert.Null(project.ConnectionFor(a.Id, a.Field.Pipes[0].Id));

        // The crew's matching pipe at the other end, entered second: that connects them.
        var back = Pipe("S", 12, "RCP", 7.2);
        b.Field.Pipes.Add(back);
        Assert.True(ConnectionBatch.ConnectAsEntered(project, Settings, b.Id, back.Id));

        var c = project.ConnectionFor(b.Id, back.Id)!;
        Assert.Equal(ConnectionStatus.Confirmed, c.Status);
        Assert.True(c.IsAccepted);
        // One connection holding both ends, not one per end.
        Assert.Single(project.Connections);
        Assert.NotNull(project.ConnectionFor(a.Id, a.Field.Pipes[0].Id));
    }

    [Fact]
    public void NothingIsConfirmedWithoutAPipeAtBothEnds()
    {
        var a = At("1045", 0, 0);
        var b = At("1046", 0, 180);
        a.Field.Pipes.Add(Pipe("N", 12, "RCP", 6.41));
        b.Field.Pipes.Add(Pipe("E", 8, "PVC", 4.0));      // points somewhere else
        var project = new UtilityProject { Structures = { a, b } };

        Assert.False(ConnectionBatch.ConnectAsEntered(project, Settings, a.Id, a.Field.Pipes[0].Id));
        Assert.Empty(project.Connections.Where(c => c.IsAccepted));
    }

    [Fact]
    public void APipeWithNoDirectionIsLeftAlone()
    {
        var a = At("1045", 0, 0);
        var b = At("1046", 0, 180);
        a.Field.Pipes.Add(new QuickPipeEntry { SizeIn = 12, Material = "RCP", MeasuredDip = 6.41 }.Create());
        b.Field.Pipes.Add(Pipe("S", 12, "RCP", 7.2));
        var project = new UtilityProject { Structures = { a, b } };

        Assert.False(ConnectionBatch.ConnectAsEntered(project, Settings, a.Id, a.Field.Pipes[0].Id));
    }

    [Fact]
    public void APipeAlreadyConnectedIsNotConnectedTwice()
    {
        var a = At("1045", 0, 0);
        var b = At("1046", 0, 180);
        a.Field.Pipes.Add(Pipe("N", 12, "RCP", 6.41));
        var back = Pipe("S", 12, "RCP", 7.2);
        b.Field.Pipes.Add(back);
        var project = new UtilityProject { Structures = { a, b } };

        Assert.True(ConnectionBatch.ConnectAsEntered(project, Settings, b.Id, back.Id));
        var count = project.Connections.Count;

        Assert.False(ConnectionBatch.ConnectAsEntered(project, Settings, b.Id, back.Id));
        Assert.Equal(count, project.Connections.Count);
    }
}
