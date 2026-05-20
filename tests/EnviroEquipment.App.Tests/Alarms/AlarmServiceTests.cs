using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using Xunit;

namespace EnviroEquipment.App.Tests.Alarms;

[Trait("Category", "Pkg2")]
public class AlarmServiceTests
{
    private static readonly DeviceId D1 = new("d1");
    private static readonly DeviceId D2 = new("d2");

    [Fact]
    public async Task Stream_EmitsCriticalEvent_OnRuleMatch()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0,
            "PV={Pv:F1}");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromSeconds(5) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 90.0, atSeconds: 0));

        await WaitFor(() => received.Count >= 1, TimeSpan.FromSeconds(1));
        received.Should().HaveCount(1);
        received[0].Level.Should().Be(AlarmLevel.Critical);
        received[0].Code.Should().Be("HOT");
        received[0].DeviceId.Should().Be(D1);
    }

    [Fact]
    public async Task Stream_DebouncesIdenticalDeviceCode_WithinWindow()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0,
            "PV={Pv:F1}");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromSeconds(5) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 90.0, atSeconds: 0));
        fakeMgr.Emit(MakeDevice(D1, pv: 91.0, atSeconds: 1));
        fakeMgr.Emit(MakeDevice(D1, pv: 92.0, atSeconds: 2));

        await WaitFor(() => received.Count >= 1, TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        received.Should().HaveCount(1, because: "the (D1, HOT) pair is debounced inside the 5s window");
    }

    [Fact]
    public async Task Stream_DebounceDoesNotSuppressDifferentDevice()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0, "PV={Pv}");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromSeconds(5) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 90.0, atSeconds: 0));
        fakeMgr.Emit(MakeDevice(D2, pv: 90.0, atSeconds: 0));

        await WaitFor(() => received.Count >= 2, TimeSpan.FromSeconds(1));
        received.Should().HaveCount(2);
        received.Select(e => e.DeviceId).Should().BeEquivalentTo(new[] { D1, D2 });
    }

    [Fact]
    public async Task Stream_DebounceDoesNotSuppressDifferentCode()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var hot = new AlarmRule("HOT", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0, "hot");
        var humid = new AlarmRule("HUMID", AlarmLevel.Warn,
            s => s.Humid.HasValue && s.Humid.Value > 70.0, "humid");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { hot, humid }, DebounceWindow = TimeSpan.FromSeconds(5) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 90.0, humid: 90.0, atSeconds: 0));

        await WaitFor(() => received.Count >= 2, TimeSpan.FromSeconds(1));
        received.Should().HaveCount(2);
        received.Select(e => e.Code).Should().BeEquivalentTo(new[] { "HOT", "HUMID" });
    }

    [Fact]
    public async Task Stream_AfterDebounceWindowElapses_RefiresPair()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0, "PV={Pv}");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromMilliseconds(100) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 90.0, atSeconds: 0));
        await Task.Delay(150);
        fakeMgr.Emit(MakeDevice(D1, pv: 91.0, atSeconds: 1));

        await WaitFor(() => received.Count >= 2, TimeSpan.FromSeconds(1));
        received.Should().HaveCount(2);
    }

    [Fact]
    public async Task Insert_IsWrittenToRepository()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical,
            _ => true, "msg");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromSeconds(5) });

        fakeMgr.Emit(MakeDevice(D1, pv: 25.0, atSeconds: 0));
        await Task.Delay(100);

        var all = await repo.QueryAsync(new AlarmFilter(null, null, null, null), CancellationToken.None);
        all.Should().HaveCount(1);
    }

    [Fact]
    public async Task AckAsync_SetsAckFlagInRepo()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical, _ => true, "msg");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromSeconds(5) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 25.0, atSeconds: 0));
        await WaitFor(() => received.Count >= 1, TimeSpan.FromSeconds(1));

        await svc.AckAsync(received[0].Id, CancellationToken.None);

        var all = await repo.QueryAsync(new AlarmFilter(null, null, null, null), CancellationToken.None);
        all[0].Ack.Should().BeTrue();
    }

    [Fact]
    public async Task ResetAsync_EmitsResetVariantOnStream()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical, _ => true, "msg");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromSeconds(5) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 25.0, atSeconds: 0));
        await WaitFor(() => received.Count >= 1, TimeSpan.FromSeconds(1));

        await svc.ResetAsync(received[0].Id, CancellationToken.None);

        await WaitFor(() => received.Count >= 2, TimeSpan.FromSeconds(1));
        received[1].Reset.Should().BeTrue();
        received[1].Id.Should().Be(received[0].Id);
    }

    [Fact]
    public async Task MuteAsync_SuppressesSubsequentEventsForWindow()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical, _ => true, "msg");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromMilliseconds(50) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 25.0, atSeconds: 0));
        await WaitFor(() => received.Count >= 1, TimeSpan.FromSeconds(1));
        // MuteAsync emits one Muted variant on the stream — capture and discard.
        await svc.MuteAsync(received[0].Id, TimeSpan.FromMilliseconds(500), CancellationToken.None);
        await WaitFor(() => received.Count >= 2, TimeSpan.FromSeconds(1));
        var afterMute = received.Count;

        await Task.Delay(100);
        fakeMgr.Emit(MakeDevice(D1, pv: 26.0, atSeconds: 1));
        await Task.Delay(100);
        received.Count.Should().Be(afterMute, because: "(D1, HOT) pair is muted for 500ms");

        await Task.Delay(500);
        fakeMgr.Emit(MakeDevice(D1, pv: 27.0, atSeconds: 2));
        await WaitFor(() => received.Count >= afterMute + 1, TimeSpan.FromSeconds(1));
        received.Count.Should().Be(afterMute + 1);
    }

    private static Device MakeDevice(DeviceId id, double? pv = null, double? humid = null, int atSeconds = 0)
    {
        var snap = new ReadingSnapshot(
            new DateTimeOffset(2026, 5, 15, 12, 0, atSeconds, TimeSpan.Zero),
            Pv: pv, Sv: null, Humid: humid, HumidSv: null, Press: null, PressSv: null);
        return new Device
        {
            Id = id,
            Bay = "Bay-1",
            Type = DeviceType.Standard,
            Status = DeviceStatus.Run,
            LastReading = snap,
        };
    }

    private static async Task WaitFor(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout)
                throw new TimeoutException($"Predicate not satisfied within {timeout}");
            await Task.Delay(10);
        }
    }

    private sealed class FakeDeviceSessionManager : IDeviceSessionManager
    {
        private readonly Subject<Device> _subject = new();
        public IObservable<Device> Devices => _subject;
        public Task ConnectAllAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct)
            => Task.FromResult(DeviceWriteResult.Success());
        public void Emit(Device d) => _subject.OnNext(d);
    }
}
