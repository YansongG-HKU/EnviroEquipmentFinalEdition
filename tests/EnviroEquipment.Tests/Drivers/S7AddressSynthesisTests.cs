using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

/// <summary>
/// Verifies that addresses synthesized by TagConfigLoader.SynthesizeLegacyAddress
/// round-trip through the appropriate parser (S7Address or ModbusAddress) without error.
/// </summary>
public class S7AddressSynthesisTests
{
    // --- Siemens DB int16 ---
    [Fact]
    public void Siemens_DbInt16_SynthesizesDBW()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "db", dbnumber: 1, rawAddress: 336, type: "HRS（int16）", deviation: 0);
        address.Should().Be("DB1.DBW336");
        dataType.Should().Be(TagDataType.Int16);
        // Must parse without throwing.
        S7Address.Parse(MakeTag(address, dataType));
    }

    // --- Siemens DB real ---
    [Fact]
    public void Siemens_DbReal_SynthesizesDBD()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "db", dbnumber: 1, rawAddress: 340, type: "HRF（Real）", deviation: 0);
        address.Should().Be("DB1.DBD340");
        dataType.Should().Be(TagDataType.Real);
        S7Address.Parse(MakeTag(address, dataType));
    }

    // --- Siemens V bit ---
    [Fact]
    public void Siemens_VBit_SynthesizesVDotBit()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "v", dbnumber: 0, rawAddress: 9, type: "V", deviation: 6);
        address.Should().Be("V9.6");
        dataType.Should().Be(TagDataType.Bool);
        S7Address.Parse(MakeTag(address, dataType));
    }

    // --- Schneider coil ---
    [Fact]
    public void Schneider_Coil_SynthesizesCAddress()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "", dbnumber: 0, rawAddress: 80, type: "Q", deviation: 0);
        address.Should().Be("C80");
        dataType.Should().Be(TagDataType.Bool);
    }

    // --- Schneider HR int16 ---
    [Fact]
    public void Schneider_HrInt16_SynthesizesHRAddress()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "", dbnumber: 0, rawAddress: 100, type: "HRS", deviation: 0);
        address.Should().Be("HR100");
        dataType.Should().Be(TagDataType.Int16);
    }

    // --- Schneider HR float ---
    [Fact]
    public void Schneider_HrFloat_SynthesizesHRFAddress()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "", dbnumber: 0, rawAddress: 200, type: "HRF", deviation: 0);
        address.Should().Be("HRF200");
        dataType.Should().Be(TagDataType.Real);
    }

    // --- Schneider HR dint ---
    [Fact]
    public void Schneider_HrDint_SynthesizesHRDAddress()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "", dbnumber: 0, rawAddress: 300, type: "HRD", deviation: 0);
        address.Should().Be("HRD300");
        dataType.Should().Be(TagDataType.DInt);
    }

    // --- Schneider HR uint32 ---
    [Theory]
    [InlineData("HRU")]
    [InlineData("HRDU")]
    public void Schneider_HrUint32_SynthesizesHRDUAddress(string type)
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "", dbnumber: 0, rawAddress: 400, type: type, deviation: 0);
        address.Should().Be("HRDU400");
        dataType.Should().Be(TagDataType.UInt32);
    }

    // --- Unknown type throws ---
    [Fact]
    public void UnknownType_Throws()
    {
        var act = () => TagConfigLoader.SynthesizeLegacyAddress(
            area: "db", dbnumber: 1, rawAddress: 0, type: "BOGUS", deviation: 0);
        act.Should().Throw<System.InvalidOperationException>()
            .WithMessage("*BOGUS*");
    }

    private static TagDefinition MakeTag(string address, TagDataType dt) => new()
    {
        Name = "T", DisplayName = "T", Group = "g", Address = address, DataType = dt, Unit = ""
    };
}
