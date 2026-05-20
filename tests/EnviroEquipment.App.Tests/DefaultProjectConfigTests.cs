using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;
using Xunit;

namespace EnviroEquipment.App.Tests;

[Trait("Category", "Pkg1")]
public class DefaultProjectConfigTests
{
    [Fact]
    public void BuildDefaultProjectConfig_Seeds9DevicesForA3x3Grid()
    {
        var config = AppServiceCollectionExtensions.BuildDefaultProjectConfig();
        config.Devices.Should().HaveCount(9, "the overview is a 3x3 grid mirroring INITIAL_DEVICES");
        config.Devices.Select(d => d.Id).Should().OnlyHaveUniqueItems();
        config.Devices.Should().OnlyContain(d => d.UseInMemoryAdapter, "demo data must stay offline-safe");
        config.Devices.Should().OnlyContain(d => d.Seed != null, "every demo device carries a seed");
    }

    [Fact]
    public void BuildDefaultProjectConfig_HasTheDesignStatusMix()
    {
        var config = AppServiceCollectionExtensions.BuildDefaultProjectConfig();
        var byStatus = config.Devices
            .GroupBy(d => d.Seed!.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        // Mirrors mock-data.jsx: 4 run, 1 alarm, 1 pause, 1 sched, 1 idle, 1 offline.
        byStatus[DeviceStatus.Run].Should().Be(4);
        byStatus[DeviceStatus.Alarm].Should().Be(1);
        byStatus[DeviceStatus.Paused].Should().Be(1);
        byStatus[DeviceStatus.Scheduled].Should().Be(1);
        byStatus[DeviceStatus.Idle].Should().Be(1);
        byStatus[DeviceStatus.Offline].Should().Be(1);
    }

    [Fact]
    public void BuildDefaultProjectConfig_AlarmDeviceCarriesAlarmMetadata()
    {
        var config = AppServiceCollectionExtensions.BuildDefaultProjectConfig();
        var alarm = config.Devices.Single(d => d.Seed!.Status == DeviceStatus.Alarm);
        alarm.Seed!.AlarmCode.Should().NotBeNullOrEmpty();
        alarm.Seed!.AlarmMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SeededSession_PublishesSeededStatusAndReadings()
    {
        var config = AppServiceCollectionExtensions.BuildDefaultProjectConfig();
        var mgr = new DeviceSessionManager(config, NullLogger<DeviceSessionManager>.Instance,
            pollingInterval: TimeSpan.FromMilliseconds(150));

        var latest = new Dictionary<string, Device>(StringComparer.Ordinal);
        using var sub = mgr.Devices.Subscribe(d => { lock (latest) { latest[d.Id.Value] = d; } });

        await mgr.ConnectAllAsync(CancellationToken.None);

        // Wait until all 9 devices have published at least once.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (latest) { if (latest.Count >= 9) break; }
            await Task.Delay(100);
        }

        Device alarm, offline, run;
        lock (latest)
        {
            latest.Should().HaveCount(9);
            alarm = latest["TH-03"];
            offline = latest["TH-07"];
            run = latest["TH-01"];
        }

        // Alarm device reports Alarm + a non-zero PV near its setpoint.
        alarm.Status.Should().Be(DeviceStatus.Alarm);
        alarm.LastReading!.Pv.Should().NotBeNull();
        alarm.LastReading!.Pv!.Value.Should().BeApproximately(152.8, 5.0);
        alarm.Program.AlarmCode.Should().Be("E-1108");

        // Offline device stays offline with no reading.
        offline.Status.Should().Be(DeviceStatus.Offline);

        // A run device with humidity reports both PV and humidity (TH-01 seeds humidity).
        run.Status.Should().Be(DeviceStatus.Run);
        run.LastReading!.Pv.Should().NotBeNull();
        run.LastReading!.Humid.Should().NotBeNull();

        await mgr.DisposeAsync();
    }

    [Fact]
    public async Task SeededSession_OperatorWriteOverridesSeededSetpoint()
    {
        var config = AppServiceCollectionExtensions.BuildDefaultProjectConfig();
        var mgr = new DeviceSessionManager(config, NullLogger<DeviceSessionManager>.Instance,
            pollingInterval: TimeSpan.FromMilliseconds(120));

        Device? th01 = null;
        using var sub = mgr.Devices.Subscribe(d => { if (d.Id.Value == "TH-01") th01 = d; });

        await mgr.ConnectAllAsync(CancellationToken.None);
        await Task.Delay(300);

        var write = await mgr.WriteSetpointAsync(new DeviceId("TH-01"),
            new Setpoints(99.0, null, null), CancellationToken.None);
        write.Ok.Should().BeTrue();

        // Next polls should reflect the written setpoint, not the seeded 85.0.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && (th01?.Setpoints.Temp is not 99.0))
        {
            await Task.Delay(80);
        }
        th01!.Setpoints.Temp.Should().Be(99.0);

        await mgr.DisposeAsync();
    }
}
