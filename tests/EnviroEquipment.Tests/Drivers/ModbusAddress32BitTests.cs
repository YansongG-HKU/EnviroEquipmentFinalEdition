using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class ModbusAddress32BitTests
{
    [Fact]
    public void Parse_AcceptsHrdForDInt()
    {
        var tag = MakeTag("X", "HRD100", TagDataType.DInt);
        var address = ModbusAddress.Parse(tag);
        address.Area.Should().Be("HRD");
        address.Offset.Should().Be(100);
        address.IsHoldingRegister.Should().BeTrue();
    }

    [Fact]
    public void Parse_AcceptsHrduForUInt32()
    {
        var tag = MakeTag("X", "HRDU200", TagDataType.UInt32);
        var address = ModbusAddress.Parse(tag);
        address.Area.Should().Be("HRDU");
        address.Offset.Should().Be(200);
        address.IsHoldingRegister.Should().BeTrue();
    }

    [Fact]
    public void Parse_RejectsHrdForInt16()
    {
        var tag = MakeTag("X", "HRD100", TagDataType.Int16);
        var act = () => ModbusAddress.Parse(tag);
        act.Should().Throw<System.FormatException>();
    }

    [Fact]
    public void Parse_RejectsHrForDInt()
    {
        var tag = MakeTag("X", "HR100", TagDataType.DInt);
        var act = () => ModbusAddress.Parse(tag);
        act.Should().Throw<System.FormatException>();
    }

    private static TagDefinition MakeTag(string name, string address, TagDataType type) => new()
    {
        Name = name, DisplayName = name, Group = "g",
        Address = address, DataType = type, Unit = ""
    };
}
