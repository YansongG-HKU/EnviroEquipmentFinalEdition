using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class S7Address32BitTests
{
    [Fact]
    public void UInt32_Parses_DBD_AddressKind()
    {
        var tag = new TagDefinition
        {
            Name = "C", DisplayName = "C", Group = "g",
            Address = "DB1.DBD20", DataType = TagDataType.UInt32, Unit = ""
        };

        // We can't directly call S7Address.Parse from tests (it's internal),
        // so this assertion is indirect: ConfigValidationService runs Parse
        // for the "s7" protocol and reports errors. No error == accepted.
        var issues = SiemensS7Demo.Services.ConfigValidationService.ValidateTags(
            new[] { tag }, "s7", "test");
        issues.Should().NotContain(i => i.Severity == SiemensS7Demo.Services.ConfigIssueSeverity.Error);
    }
}
