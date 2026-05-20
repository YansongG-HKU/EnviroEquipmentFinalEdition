using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests;

public class SmokeTest
{
    [Fact]
    [Trait("Category", "Smoke")]
    public void TagDefinition_CanConstructWithRequiredFields()
    {
        var tag = new TagDefinition
        {
            Name = "TemperaturePV",
            DisplayName = "Current Temperature",
            Group = "Acquire",
            Address = "DB100.DBD10",
            DataType = TagDataType.Real,
            Unit = "C",
            Access = TagAccess.Read
        };

        tag.Name.Should().Be("TemperaturePV");
        tag.DataType.Should().Be(TagDataType.Real);
        tag.Access.Should().Be(TagAccess.Read);
        tag.Scale.Should().Be(1.0);
        tag.Offset.Should().Be(0.0);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task InMemoryAdapter_ConnectWriteRead_RoundTripsValue()
    {
        var options = new PlcConnectionOptions
        {
            Name = "Smoke",
            IpAddress = "127.0.0.1",
            CpuType = "Mock"
        };
        var client = new SiemensS7Client(options, new InMemoryS7Adapter());

        await client.ConnectAsync(CancellationToken.None);
        client.IsConnected.Should().BeTrue();

        var tag = new TagDefinition
        {
            Name = "TemperatureSP",
            DisplayName = "Temperature Setpoint",
            Group = "Control",
            Address = "DB100.DBD20",
            DataType = TagDataType.Real,
            Unit = "C",
            Access = TagAccess.ReadWrite
        };

        await client.WriteTagAsync(tag, 25.5f, CancellationToken.None);

        var values = await client.ReadTagsAsync(new[] { tag }, CancellationToken.None);

        values.Should().ContainKey("TemperatureSP");
        var read = values["TemperatureSP"];
        read.IsQualityGood.Should().BeTrue();
        read.Value.Should().Be(25.5d);
    }
}
