using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class ModbusAddressTests
{
    [Fact]
    public void Parse_AcceptsHrfForReal()
    {
        var tag = MakeTag("Temp", "HRF200", TagDataType.Real);
        var address = ModbusAddress.Parse(tag);

        address.Area.Should().Be("HRF");
        address.Offset.Should().Be(200);
        address.IsHoldingRegister.Should().BeTrue();
    }

    [Fact]
    public void Parse_RejectsHrfForInt16()
    {
        var tag = MakeTag("Bad", "HRF200", TagDataType.Int16);
        var act = () => ModbusAddress.Parse(tag);
        act.Should().Throw<System.FormatException>();
    }

    [Fact]
    public void Parse_RejectsHrForReal()
    {
        var tag = MakeTag("Bad", "HR200", TagDataType.Real);
        var act = () => ModbusAddress.Parse(tag);
        act.Should().Throw<System.FormatException>();
    }

    private static TagDefinition MakeTag(string name, string address, TagDataType type) => new()
    {
        Name = name,
        DisplayName = name,
        Group = "Test",
        Address = address,
        DataType = type,
        Unit = string.Empty
    };
}
