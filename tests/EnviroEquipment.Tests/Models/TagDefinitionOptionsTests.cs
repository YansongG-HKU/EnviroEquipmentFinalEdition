using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Models;

public class TagDefinitionOptionsTests
{
    [Fact]
    public void Options_DefaultEmpty()
    {
        var tag = MakeTag();
        tag.Options.Should().BeEmpty();
    }

    [Fact]
    public void TryGetOptionLabel_ReturnsLabelOnMatch()
    {
        var tag = new TagDefinition
        {
            Name = "Mode", DisplayName = "Mode", Group = "g", Address = "V9.5",
            DataType = TagDataType.Bool, Unit = "",
            Options = new[] { new TagOption(0, "Off"), new TagOption(1, "On") }
        };

        tag.TryGetOptionLabel(1, out var label).Should().BeTrue();
        label.Should().Be("On");
    }

    [Fact]
    public void TryGetOptionLabel_ReturnsFalseWhenNoMatch()
    {
        var tag = new TagDefinition
        {
            Name = "X", DisplayName = "X", Group = "g", Address = "MW0",
            DataType = TagDataType.Int16, Unit = "",
            Options = new[] { new TagOption(0, "Off"), new TagOption(1, "On") }
        };

        tag.TryGetOptionLabel(42, out var label).Should().BeFalse();
        label.Should().BeNull();
    }

    private static TagDefinition MakeTag() => new()
    {
        Name = "T", DisplayName = "T", Group = "g", Address = "MW0",
        DataType = TagDataType.Int16, Unit = ""
    };
}
