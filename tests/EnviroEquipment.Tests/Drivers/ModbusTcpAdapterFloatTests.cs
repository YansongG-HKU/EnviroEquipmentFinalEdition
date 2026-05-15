using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using SiemensS7Demo.Testing;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class ModbusTcpAdapterFloatTests
{
    [Fact]
    public async Task ReadRaw_DecodesAbcdFloatFromTwoRegisters()
    {
        await using var server = ModbusLoopbackServer.Start();
        using var adapter = new ModbusTcpAdapter();
        var options = new PlcConnectionOptions
        {
            Name = "T", IpAddress = "127.0.0.1", Port = server.Port,
            Protocol = "modbus", CpuType = "Modbus TCP", UnitId = 1,
            WordOrder = WordOrder.ABCD
        };
        await adapter.ConnectAsync(options, CancellationToken.None);

        var tag = new TagDefinition
        {
            Name = "F", DisplayName = "F", Group = "g",
            Address = "HRF10", DataType = TagDataType.Real, Unit = ""
        };

        var value = await adapter.ReadRawAsync(tag, CancellationToken.None);
        value.Should().BeOfType<float>().Which.Should().Be(1.0f);
    }

    [Fact]
    public async Task ReadRaw_DecodesCdabFloatFromTwoRegisters()
    {
        await using var server = ModbusLoopbackServer.Start();
        using var adapter = new ModbusTcpAdapter();
        var options = new PlcConnectionOptions
        {
            Name = "T", IpAddress = "127.0.0.1", Port = server.Port,
            Protocol = "modbus", CpuType = "Modbus TCP", UnitId = 1,
            WordOrder = WordOrder.CDAB
        };
        await adapter.ConnectAsync(options, CancellationToken.None);

        // HR10/HR11 wire bytes = 0x3F80, 0x0000. Under CDAB the device-published bytes for
        // 1.0f would be 0x0000, 0x3F80. Our loopback always serves 0x3F80, 0x0000, so under
        // CDAB this decodes to something other than 1.0f — the swap must be applied.
        var tag = new TagDefinition
        {
            Name = "F", DisplayName = "F", Group = "g",
            Address = "HRF10", DataType = TagDataType.Real, Unit = ""
        };
        var value = (float)await adapter.ReadRawAsync(tag, CancellationToken.None);

        value.Should().NotBe(1.0f);
    }

    [Theory]
    [InlineData(WordOrder.ABCD)]
    [InlineData(WordOrder.CDAB)]
    [InlineData(WordOrder.BADC)]
    [InlineData(WordOrder.DCBA)]
    public async Task WriteRaw_ThenReadRaw_RoundTripsFloat(WordOrder order)
    {
        await using var server = ModbusLoopbackServer.Start();
        using var adapter = new ModbusTcpAdapter();
        var options = new PlcConnectionOptions
        {
            Name = "T", IpAddress = "127.0.0.1", Port = server.Port,
            Protocol = "modbus", CpuType = "Modbus TCP", UnitId = 1,
            WordOrder = order
        };
        await adapter.ConnectAsync(options, CancellationToken.None);

        var tag = new TagDefinition
        {
            Name = "F", DisplayName = "F", Group = "g",
            Address = "HRF20", DataType = TagDataType.Real, Unit = ""
        };

        await adapter.WriteRawAsync(tag, 3.14159f, CancellationToken.None);
        var readBack = (float)await adapter.ReadRawAsync(tag, CancellationToken.None);

        readBack.Should().BeApproximately(3.14159f, 0.0001f);
    }
}
