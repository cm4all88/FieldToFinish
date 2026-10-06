using CrewUpload.Integration;

namespace CrewUpload.Tests;

public sealed class EmployeeChoiceTests
{
    private static readonly ScheduledEmployee[] People =
    {
        new() { Id = "jeff_bearson", Name = "Jeff Bearson", Active = true },
        new() { Id = "jim_martin", Name = "Jim Martin", Active = true },
        new() { Id = "seth_d_agostino", Name = "Seth D'Agostino", Active = true },
        new() { Id = "old_hand", Name = "Old Hand", Active = false },
        new() { Id = "no_name", Name = "", Active = true },
        new() { Id = "chris_lee", Name = "Chris Lee", Active = true },
        new() { Id = "chris_lee_2", Name = "Chris Lee", Active = true },
    };

    [Fact]
    public void ShowsNamesAndKeepsIds()
    {
        var c = EmployeeChoices.Build(People, new[] { "jeff_bearson" });
        Assert.Equal("(not linked)", c[0].Display);
        Assert.Equal("", c[0].Id);
        var jeff = c.Single(x => x.Id == "jeff_bearson");
        Assert.Equal("Jeff Bearson", jeff.Display);
        Assert.Equal("Seth D'Agostino", c.Single(x => x.Id == "seth_d_agostino").Display);
        Assert.DoesNotContain(c, x => x.Id == "old_hand"); // departed and not linked: not offered
    }

    [Fact]
    public void FallsBackToTheIdOnlyWhenThereIsNoName()
    {
        var c = EmployeeChoices.Build(People, new[] { "old_hand", "gone_person" });
        Assert.Equal("no_name", c.Single(x => x.Id == "no_name").Display);
        Assert.Equal("Old Hand (inactive)", c.Single(x => x.Id == "old_hand").Display);
        Assert.Equal("gone_person (not in the schedule now)", c.Single(x => x.Id == "gone_person").Display);
    }

    [Fact]
    public void EveryChoiceCanBeToldApart()
    {
        var c = EmployeeChoices.Build(People, null);
        Assert.Equal(c.Count, c.Select(x => x.Display).Distinct().Count());
        Assert.Equal("Chris Lee (chris_lee)", c.Single(x => x.Id == "chris_lee").Display);
    }
}
