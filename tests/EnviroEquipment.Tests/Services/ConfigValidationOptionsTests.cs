using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ConfigValidationOptionsTests
{
    [Fact]
    public void DuplicateOptionValues_AreFlagged()
    {
        var tag = new TagDefinition
        {
            Name = "X", DisplayName = "X", Group = "g", Address = "MW0",
            DataType = TagDataType.Int16, Unit = "",
            Options = new[] { new TagOption(0, "Off"), new TagOption(0, "Also Off") }
        };

        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().Contain(i => i.Severity == ConfigIssueSeverity.Error && i.Message.Contains("duplicate option value", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EmptyOptionLabel_IsFlagged()
    {
        var tag = new TagDefinition
        {
            Name = "X", DisplayName = "X", Group = "g", Address = "MW0",
            DataType = TagDataType.Int16, Unit = "",
            Options = new[] { new TagOption(0, "") }
        };

        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().Contain(i => i.Severity == ConfigIssueSeverity.Error && i.Message.Contains("empty", System.StringComparison.OrdinalIgnoreCase));
    }
}
