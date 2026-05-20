using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;
using Xunit;

namespace EnviroEquipment.App.Tests;

[Trait("Category", "Pkg1")]
public class DeviceSessionManagerTests
{
    private static ProjectConfig SampleConfig(int count) => new(
        Enumerable.Range(1, count).Select(i => new DeviceProvisioning(
            Id: $"TH-{i:00}",
            Bay: $"A{i}",
            Type: DeviceType.Standard,
            IpAddress: "127.0.0.1",
            Port: 102,
            CpuType: "Mock",
            Rack: 0,
            Slot: 1,
            PvTagName: "Pv",
            SvTagName: "Sv",
            PvAddress: "DB100.DBD10",
            SvAddress: "DB100.DBD14",
            UseInMemoryAdapter: true)).ToList());

    [Fact]
    public async Task ConnectAllAsync_PublishesOneSnapshotPerDevice()
    {
        var config = SampleConfig(3);
        var mgr = new DeviceSessionManager(config, NullLogger<DeviceSessionManager>.Instance,
            pollingInterval: TimeSpan.FromSeconds(1));

        var seen = new List<DeviceId>();
        using var sub = mgr.Devices.Subscribe(d => { lock (seen) { seen.Add(d.Id); } });

        await mgr.ConnectAllAsync(CancellationToken.None);

        // Wait up to 5s for all 3 to publish at least once.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (seen)
            {
                if (seen.Select(x => x.Value).Distinct().Count() >= 3) break;
            }
            await Task.Delay(100);
        }

        lock (seen)
        {
            seen.Select(x => x.Value).Should().Contain(new[] { "TH-01", "TH-02", "TH-03" });
        }

        await mgr.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAllAsync_IsIdempotent()
    {
        var config = SampleConfig(2);
        var mgr = new DeviceSessionManager(config, NullLogger<DeviceSessionManager>.Instance,
            pollingInterval: TimeSpan.FromSeconds(1));

        await mgr.ConnectAllAsync(CancellationToken.None);
        Func<Task> again = () => mgr.ConnectAllAsync(CancellationToken.None);
        await again.Should().NotThrowAsync();

        await mgr.DisposeAsync();
    }

    [Fact]
    public async Task WriteSetpointAsync_ReturnsSuccessForKnownDevice()
    {
        var config = SampleConfig(1);
        var mgr = new DeviceSessionManager(config, NullLogger<DeviceSessionManager>.Instance,
            pollingInterval: TimeSpan.FromSeconds(1));

        await mgr.ConnectAllAsync(CancellationToken.None);

        var result = await mgr.WriteSetpointAsync(
            new DeviceId("TH-01"),
            new Setpoints(85.0, null, null),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.ErrorCode.Should().BeNull();

        await mgr.DisposeAsync();
    }

    [Fact]
    public async Task WriteSetpointAsync_ReturnsFailureForUnknownDevice()
    {
        var config = SampleConfig(1);
        var mgr = new DeviceSessionManager(config, NullLogger<DeviceSessionManager>.Instance,
            pollingInterval: TimeSpan.FromSeconds(1));

        await mgr.ConnectAllAsync(CancellationToken.None);

        var result = await mgr.WriteSetpointAsync(
            new DeviceId("TH-NOPE"),
            new Setpoints(85.0, null, null),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.ErrorCode.Should().Be("UNKNOWN_DEVICE");

        await mgr.DisposeAsync();
    }

    [Fact]
    public async Task Devices_HotStream_ReplaysLastValueToLateSubscribers()
    {
        var config = SampleConfig(1);
        var mgr = new DeviceSessionManager(config, NullLogger<DeviceSessionManager>.Instance,
            pollingInterval: TimeSpan.FromMilliseconds(200));

        await mgr.ConnectAllAsync(CancellationToken.None);
        await Task.Delay(600);

        Device? captured = null;
        using var sub = mgr.Devices.Take(1).Subscribe(d => captured = d);

        await Task.Delay(800);
        captured.Should().NotBeNull();
        captured!.Id.Value.Should().Be("TH-01");

        await mgr.DisposeAsync();
    }
}
