using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ConfigValidationBitDerivationsTests
{
    [Fact]
    public void BitOffsetOutOfRange_ForUInt16_IsRejected()
    {
        var tag = Make("X", "MW0", TagDataType.UInt16,
            new[] { new BitDerivation("Bad", 16) });
        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().Contain(i => i.Severity == ConfigIssueSeverity.Error && i.Message.Contains("BitOffset", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BitOffset_AllowedUpTo31_ForDInt_Host()
    {
        var tag = Make("X", "MD0", TagDataType.DInt,
            new[] { new BitDerivation("HighBit", 31) });
        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().NotContain(i => i.Severity == ConfigIssueSeverity.Error);
    }

    [Fact]
    public void DuplicateDerivationNames_AreFlagged()
    {
        var tag = Make("X", "MW0", TagDataType.UInt16,
            new[] { new BitDerivation("Same", 0), new BitDerivation("Same", 1) });
        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().Contain(i => i.Severity == ConfigIssueSeverity.Error && i.Message.Contains("duplicate", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DerivationNameCollidingWithSiblingTag_IsFlagged()
    {
        var host = Make("X", "MW0", TagDataType.UInt16,
            new[] { new BitDerivation("Sibling", 0) });
        var sibling = Make("Sibling", "MW2", TagDataType.UInt16, System.Array.Empty<BitDerivation>());
        var issues = ConfigValidationService.ValidateTags(new[] { host, sibling }, "mock", "test");
        issues.Should().Contain(i => i.Severity == ConfigIssueSeverity.Error && i.Message.Contains("collides", System.StringComparison.OrdinalIgnoreCase));
    }

    private static TagDefinition Make(string name, string address, TagDataType type, BitDerivation[] derivations) => new()
    {
        Name = name, DisplayName = name, Group = "g",
        Address = address, DataType = type, Unit = "",
        BitDerivations = derivations
    };
}
