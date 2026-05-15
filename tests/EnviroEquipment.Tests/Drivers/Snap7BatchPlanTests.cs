using System.Linq;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class Snap7BatchPlanTests
{
    [Fact]
    public void Plan_GroupsByAreaAndDbnumber()
    {
        var tags = new[]
        {
            MakeTag("a", "DB1.DBW0", TagDataType.Int16),
            MakeTag("b", "DB1.DBW2", TagDataType.Int16),
            MakeTag("c", "DB2.DBW0", TagDataType.Int16)
        };

        var plan = Snap7BatchPlan.Plan(tags, maxWindowBytes: 240, mergeSlack: 16);

        plan.Should().HaveCount(2);
        plan.Should().Contain(w => w.DbNumber == 1 && w.StartByte == 0 && w.Length == 4 && w.Tags.Count == 2);
        plan.Should().Contain(w => w.DbNumber == 2 && w.StartByte == 0 && w.Length == 2 && w.Tags.Count == 1);
    }

    [Fact]
    public void Plan_MergesAdjacentTagsIntoSingleWindow()
    {
        var tags = new[]
        {
            MakeTag("a", "DB1.DBW0", TagDataType.Int16),
            MakeTag("b", "DB1.DBW4", TagDataType.Int16),
            MakeTag("c", "DB1.DBW200", TagDataType.Int16)
        };

        var plan = Snap7BatchPlan.Plan(tags, maxWindowBytes: 240, mergeSlack: 16);

        plan.Should().HaveCount(2);
        var firstWindow = plan.Single(w => w.StartByte == 0);
        firstWindow.Length.Should().Be(6);
        firstWindow.Tags.Should().HaveCount(2);
        plan.Should().Contain(w => w.StartByte == 200 && w.Length == 2);
    }

    [Fact]
    public void Plan_SplitsWhenWindowExceedsMaxBytes()
    {
        var tags = new[]
        {
            MakeTag("a", "DB1.DBW0", TagDataType.Int16),
            MakeTag("b", "DB1.DBW238", TagDataType.Int16)
        };

        var plan = Snap7BatchPlan.Plan(tags, maxWindowBytes: 240, mergeSlack: 16);

        plan.Should().HaveCount(1);
        plan[0].Length.Should().Be(240);
    }

    [Fact]
    public void Plan_SplitsWhenSlackExceeded()
    {
        var tags = new[]
        {
            MakeTag("a", "DB1.DBW0", TagDataType.Int16),
            MakeTag("b", "DB1.DBW20", TagDataType.Int16)
        };

        var plan = Snap7BatchPlan.Plan(tags, maxWindowBytes: 240, mergeSlack: 16);

        plan.Should().HaveCount(2);
    }

    [Fact]
    public void Plan_AssignsTagsToCorrectOffsetWithinWindow()
    {
        var tags = new[]
        {
            MakeTag("a", "DB1.DBW10", TagDataType.Int16),
            MakeTag("b", "DB1.DBD14", TagDataType.Real)
        };

        var plan = Snap7BatchPlan.Plan(tags, maxWindowBytes: 240, mergeSlack: 16);
        plan.Should().HaveCount(1);
        var window = plan[0];
        window.StartByte.Should().Be(10);
        window.Length.Should().Be(8);
        window.Tags.Single(t => t.Tag.Name == "a").OffsetInWindow.Should().Be(0);
        window.Tags.Single(t => t.Tag.Name == "b").OffsetInWindow.Should().Be(4);
    }

    private static TagDefinition MakeTag(string name, string address, TagDataType type) => new()
    {
        Name = name,
        DisplayName = name,
        Group = "g",
        Address = address,
        DataType = type,
        Unit = ""
    };
}
