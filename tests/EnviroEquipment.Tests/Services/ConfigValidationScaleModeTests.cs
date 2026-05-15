using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ConfigValidationScaleModeTests
{
    [Fact]
    public void DivisorWithZeroScale_IsError()
    {
        var tag = new TagDefinition
        {
            Name = "T", DisplayName = "T", Group = "g",
            Address = "MW0", DataType = TagDataType.Int16, Unit = "",
            Scale = 0.0, ScaleMode = ScaleMode.Divisor
        };

        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("Divisor", System.StringComparison.OrdinalIgnoreCase) &&
            i.Message.Contains("Scale", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DivisorWithNonZeroScale_IsValid()
    {
        var tag = new TagDefinition
        {
            Name = "T", DisplayName = "T", Group = "g",
            Address = "MW0", DataType = TagDataType.Int16, Unit = "",
            Scale = 10.0, ScaleMode = ScaleMode.Divisor
        };

        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().NotContain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("Divisor", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MultiplierWithZeroScale_UsesExistingScaleZeroError()
    {
        // The existing rule "Scale must not be 0 for numeric tags" still fires for Multiplier+0.
        var tag = new TagDefinition
        {
            Name = "T", DisplayName = "T", Group = "g",
            Address = "MW0", DataType = TagDataType.Int16, Unit = "",
            Scale = 0.0, ScaleMode = ScaleMode.Multiplier
        };

        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().Contain(i => i.Severity == ConfigIssueSeverity.Error);
    }
}
