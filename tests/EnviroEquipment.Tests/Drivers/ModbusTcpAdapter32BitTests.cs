using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using SiemensS7Demo.Testing;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class ModbusTcpAdapter32BitTests
{
    [Fact]
    public async Task ReadRaw_DecodesDIntAbcdFromTwoRegisters()
    {
        var (server, adapter) = await ConnectAsync(WordOrder.ABCD);
        await using var _ = server;
        using var __ = adapter;

        var tag = Make("X", "HRD12", TagDataType.DInt);
        var value = await adapter.ReadRawAsync(tag, CancellationToken.None);
        value.Should().BeOfType<int>().Which.Should().Be(100000);
    }

    [Fact]
    public async Task ReadRaw_DecodesUInt32AbcdFromTwoRegisters()
    {
        var (server, adapter) = await ConnectAsync(WordOrder.ABCD);
        await using var _ = server;
        using var __ = adapter;

        var tag = Make("X", "HRDU14", TagDataType.UInt32);
        var value = await adapter.ReadRawAsync(tag, CancellationToken.None);
        value.Should().BeOfType<uint>().Which.Should().Be(0xFFFFFFFEu);
    }

    [Theory]
    [InlineData(WordOrder.ABCD)]
    [InlineData(WordOrder.CDAB)]
    [InlineData(WordOrder.BADC)]
    [InlineData(WordOrder.DCBA)]
    public async Task WriteThenRead_DIntRoundTrips(WordOrder order)
    {
        var (server, adapter) = await ConnectAsync(order);
        await using var _ = server;
        using var __ = adapter;

        var tag = Make("X", "HRD30", TagDataType.DInt);
        await adapter.WriteRawAsync(tag, -123456, CancellationToken.None);
        var readBack = (int)await adapter.ReadRawAsync(tag, CancellationToken.None);
        readBack.Should().Be(-123456);
    }

    [Theory]
    [InlineData(WordOrder.ABCD)]
    [InlineData(WordOrder.CDAB)]
    [InlineData(WordOrder.BADC)]
    [InlineData(WordOrder.DCBA)]
    public async Task WriteThenRead_UInt32RoundTrips(WordOrder order)
    {
        var (server, adapter) = await ConnectAsync(order);
        await using var _ = server;
        using var __ = adapter;

        var tag = Make("X", "HRDU40", TagDataType.UInt32);
        await adapter.WriteRawAsync(tag, 4000000000u, CancellationToken.None);
        var readBack = (uint)await adapter.ReadRawAsync(tag, CancellationToken.None);
        readBack.Should().Be(4000000000u);
    }

    private static TagDefinition Make(string name, string address, TagDataType type) => new()
    {
        Name = name, DisplayName = name, Group = "g",
        Address = address, DataType = type, Unit = ""
    };

    private static async Task<(ModbusLoopbackServer Server, ModbusTcpAdapter Adapter)> ConnectAsync(WordOrder order)
    {
        var server = ModbusLoopbackServer.Start();
        var adapter = new ModbusTcpAdapter();
        var options = new PlcConnectionOptions
        {
            Name = "T", IpAddress = "127.0.0.1", Port = server.Port,
            Protocol = "modbus", CpuType = "Modbus TCP", UnitId = 1,
            WordOrder = order
        };
        await adapter.ConnectAsync(options, CancellationToken.None);
        return (server, adapter);
    }
}
