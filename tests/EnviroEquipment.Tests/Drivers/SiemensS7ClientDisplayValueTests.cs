using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class SiemensS7ClientDisplayValueTests
{
    [Fact]
    public async Task ReadTagsAsync_SetsDisplayValueWhenOptionMatches()
    {
        var options = new PlcConnectionOptions { Name = "T", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new InMemoryS7Adapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(CancellationToken.None);

        var tag = new TagDefinition
        {
            Name = "Mode", DisplayName = "Mode", Group = "g", Address = "MW0",
            DataType = TagDataType.Int16, Unit = "",
            Options = new[] { new TagOption(0, "Off"), new TagOption(1, "On") }
        };

        await adapter.WriteRawAsync(tag, (short)1, CancellationToken.None);
        var values = await client.ReadTagsAsync(new[] { tag }, CancellationToken.None);

        values["Mode"].DisplayValue.Should().Be("On");
        values["Mode"].IsQualityGood.Should().BeTrue();
    }

    [Fact]
    public async Task ReadTagsAsync_LeavesDisplayValueNullWhenNoMatch()
    {
        var options = new PlcConnectionOptions { Name = "T", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new InMemoryS7Adapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(CancellationToken.None);

        var tag = new TagDefinition
        {
            Name = "Mode", DisplayName = "Mode", Group = "g", Address = "MW0",
            DataType = TagDataType.Int16, Unit = "",
            Options = new[] { new TagOption(0, "Off"), new TagOption(1, "On") }
        };

        await adapter.WriteRawAsync(tag, (short)7, CancellationToken.None);
        var values = await client.ReadTagsAsync(new[] { tag }, CancellationToken.None);

        values["Mode"].DisplayValue.Should().BeNull();
    }
}
