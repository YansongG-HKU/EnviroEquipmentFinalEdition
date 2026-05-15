using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class SiemensS7ClientBitDerivationsTests
{
    [Fact]
    public async Task ReadTagsAsync_EmitsDerivedBoolForEachBitDerivation()
    {
        var options = new PlcConnectionOptions { Name = "T", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new InMemoryS7Adapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(CancellationToken.None);

        // Host word with bits 0 and 3 set.
        var host = new TagDefinition
        {
            Name = "Status", DisplayName = "Status", Group = "g",
            Address = "MW0", DataType = TagDataType.UInt16, Unit = "",
            BitDerivations = new[]
            {
                new BitDerivation("HeatRunning", 0),
                new BitDerivation("CoolRunning", 3, "Cool Running")
            }
        };

        await adapter.WriteRawAsync(host, (ushort)0b0000_0000_0000_1001, CancellationToken.None);

        var values = await client.ReadTagsAsync(new[] { host }, CancellationToken.None);

        values.Should().ContainKeys("Status", "HeatRunning", "CoolRunning");
        values["Status"].Value.Should().Be((double)9); // raw 9 carried as engineering after Scale=1.0
        values["HeatRunning"].Value.Should().Be(true);
        values["HeatRunning"].IsQualityGood.Should().BeTrue();
        values["CoolRunning"].Value.Should().Be(true);
        values["CoolRunning"].DisplayName.Should().Be("Cool Running");
    }

    [Fact]
    public async Task ReadTagsAsync_DerivedBoolIsFalseWhenBitNotSet()
    {
        var options = new PlcConnectionOptions { Name = "T", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new InMemoryS7Adapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(CancellationToken.None);

        var host = new TagDefinition
        {
            Name = "Status", DisplayName = "Status", Group = "g",
            Address = "MW0", DataType = TagDataType.UInt16, Unit = "",
            BitDerivations = new[] { new BitDerivation("Bit2", 2) }
        };
        await adapter.WriteRawAsync(host, (ushort)0, CancellationToken.None);

        var values = await client.ReadTagsAsync(new[] { host }, CancellationToken.None);
        values["Bit2"].Value.Should().Be(false);
        values["Bit2"].IsQualityGood.Should().BeTrue();
    }

    [Fact]
    public async Task ReadTagsAsync_DerivedBoolPropagatesBadQualityFromHost()
    {
        var options = new PlcConnectionOptions { Name = "T", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new ThrowingAdapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(CancellationToken.None);

        var host = new TagDefinition
        {
            Name = "Status", DisplayName = "Status", Group = "g",
            Address = "MW0", DataType = TagDataType.UInt16, Unit = "",
            BitDerivations = new[] { new BitDerivation("Bit0", 0) }
        };

        var values = await client.ReadTagsAsync(new[] { host }, CancellationToken.None);
        values["Status"].IsQualityGood.Should().BeFalse();
        values["Bit0"].IsQualityGood.Should().BeFalse();
        values["Bit0"].QualityMessage.Should().NotBeNullOrEmpty();
    }

    private sealed class ThrowingAdapter : IS7Adapter
    {
        public bool IsConnected => true;
        public Task ConnectAsync(PlcConnectionOptions options, CancellationToken ct) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<PlcDeviceInfo> GetDeviceInfoAsync(PlcConnectionOptions options, CancellationToken ct)
            => Task.FromResult(new PlcDeviceInfo { TimestampUtc = System.DateTime.UtcNow, IpAddress = "", Port = 0, Rack = 0, Slot = 0, ConnectionType = "", ConfiguredCpuType = "" });
        public Task<object> ReadRawAsync(TagDefinition tag, CancellationToken ct)
            => throw new System.IO.IOException("simulated failure");
        public Task WriteRawAsync(TagDefinition tag, object value, CancellationToken ct) => Task.CompletedTask;
    }
}
