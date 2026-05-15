using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class SiemensS7ClientBatchReadTests
{
    [Fact]
    public async Task ReadTagsAsync_PassesAllTagsThroughBatchAndPreservesQuality()
    {
        var spy = new SpyAdapter();
        var client = new SiemensS7Client(
            new PlcConnectionOptions { Name = "T", IpAddress = "127.0.0.1", CpuType = "Mock" },
            spy);
        await client.ConnectAsync(CancellationToken.None);

        var good = MakeTag("Good", "MW0");
        var bad = MakeTag("Bad", "MW2");
        spy.GoodNames.Add("Good");
        spy.GoodValues["Good"] = (short)42;
        spy.BadNames.Add("Bad");
        spy.BadMessages["Bad"] = "simulated failure";

        var values = await client.ReadTagsAsync(new[] { good, bad }, CancellationToken.None);

        spy.BatchInvocations.Should().Be(1);
        spy.SingleInvocations.Should().Be(0);
        values["Good"].IsQualityGood.Should().BeTrue();
        values["Bad"].IsQualityGood.Should().BeFalse();
        values["Bad"].QualityMessage.Should().Be("simulated failure");
    }

    private static TagDefinition MakeTag(string name, string address) => new()
    {
        Name = name,
        DisplayName = name,
        Group = "g",
        Address = address,
        DataType = TagDataType.Int16,
        Unit = ""
    };

    private sealed class SpyAdapter : IS7Adapter
    {
        public List<string> GoodNames { get; } = new();
        public List<string> BadNames { get; } = new();
        public Dictionary<string, object> GoodValues { get; } = new();
        public Dictionary<string, string> BadMessages { get; } = new();
        public int BatchInvocations;
        public int SingleInvocations;

        public bool IsConnected => true;
        public Task ConnectAsync(PlcConnectionOptions options, CancellationToken ct) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<PlcDeviceInfo> GetDeviceInfoAsync(PlcConnectionOptions options, CancellationToken ct)
            => Task.FromResult(new PlcDeviceInfo
            {
                TimestampUtc = System.DateTime.UtcNow,
                IpAddress = "",
                Port = 0,
                Rack = 0,
                Slot = 0,
                ConnectionType = "",
                ConfiguredCpuType = ""
            });

        public Task<object> ReadRawAsync(TagDefinition tag, CancellationToken ct)
        {
            SingleInvocations++;
            return Task.FromResult<object>(0);
        }

        public Task WriteRawAsync(TagDefinition tag, object value, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, BatchReadResult>> ReadRawBatchAsync(
            IReadOnlyList<TagDefinition> tags, CancellationToken ct)
        {
            BatchInvocations++;
            var dict = new Dictionary<string, BatchReadResult>();
            foreach (var tag in tags)
            {
                dict[tag.Name] = BadNames.Contains(tag.Name)
                    ? BatchReadResult.Bad(BadMessages[tag.Name])
                    : BatchReadResult.Ok(GoodValues[tag.Name]);
            }
            return Task.FromResult<IReadOnlyDictionary<string, BatchReadResult>>(dict);
        }
    }
}
