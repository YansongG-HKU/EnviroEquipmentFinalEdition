using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using SiemensS7Demo.Wpf.Alarms;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.Alarms;

[Trait("Category", "Pkg2")]
public class AlarmPopupCoordinatorTests
{
    private static readonly DeviceId D1 = new("d1");
    private static readonly DateTimeOffset T0 = new(2026, 5, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SingleCriticalEvent_ShowsPopupOnce()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Critical));
        await WaitFor(() => gate.ShowCount == 1, TimeSpan.FromSeconds(1));

        gate.ShowCount.Should().Be(1);
        gate.PresentedIds.Should().Equal("a");
    }

    [Fact]
    public async Task WarnLevel_DoesNotTriggerPopup()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Warn));
        subject.OnNext(MakeEvent("b", AlarmLevel.Info));
        await Task.Delay(50);

        gate.ShowCount.Should().Be(0);
    }

    [Fact]
    public async Task TwoSimultaneousCriticals_ShowOnePopupAtATime()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Critical));
        subject.OnNext(MakeEvent("b", AlarmLevel.Critical));
        subject.OnNext(MakeEvent("c", AlarmLevel.Critical));

        await WaitFor(() => gate.ShowCount == 1, TimeSpan.FromSeconds(1));
        gate.ShowCount.Should().Be(1);
        gate.PresentedIds.Should().Equal("a");

        gate.DismissCurrent();
        await WaitFor(() => gate.ShowCount == 2, TimeSpan.FromSeconds(1));
        gate.PresentedIds.Should().Equal("a", "b");

        gate.DismissCurrent();
        await WaitFor(() => gate.ShowCount == 3, TimeSpan.FromSeconds(1));
        gate.PresentedIds.Should().Equal("a", "b", "c");

        gate.DismissCurrent();
        gate.ShowCount.Should().Be(3, because: "no more queued items");
    }

    [Fact]
    public async Task SameAlarmRepeatedWhilePopupShowing_DoesNotDoubleEnqueue()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Critical));
        subject.OnNext(MakeEvent("a", AlarmLevel.Critical));
        subject.OnNext(MakeEvent("a", AlarmLevel.Critical));

        await WaitFor(() => gate.ShowCount == 1, TimeSpan.FromSeconds(1));
        gate.DismissCurrent();
        await Task.Delay(50);

        gate.ShowCount.Should().Be(1, because: "same Id is deduped from the queue");
    }

    [Fact]
    public async Task AckedCriticalEvent_DoesNotTriggerPopup()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Critical) with { Ack = true });
        await Task.Delay(50);

        gate.ShowCount.Should().Be(0);
    }

    [Fact]
    public async Task ResetCriticalEvent_DoesNotTriggerPopup()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Critical) with { Reset = true });
        await Task.Delay(50);

        gate.ShowCount.Should().Be(0);
    }

    [Fact]
    public async Task MutedCriticalEvent_DoesNotTriggerPopup()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Critical) with { Muted = true });
        await Task.Delay(50);

        gate.ShowCount.Should().Be(0);
    }

    [Fact]
    public async Task NullServiceOrGate_Throws()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();

        Action ctor1 = () => new AlarmPopupCoordinator(null!, gate);
        Action ctor2 = () => new AlarmPopupCoordinator(svc, null!);

        ctor1.Should().Throw<ArgumentNullException>();
        ctor2.Should().Throw<ArgumentNullException>();
        await Task.CompletedTask;
    }

    private static AlarmEvent MakeEvent(string id, AlarmLevel level)
        => new(id, D1, level, Code: id, Message: $"m-{id}", At: T0,
               Ack: false, Reset: false, Muted: false);

    private static async Task WaitFor(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout)
                throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private sealed class FakeService : IAlarmService
    {
        private readonly IObservable<AlarmEvent> _stream;
        public FakeService(IObservable<AlarmEvent> stream) => _stream = stream;
        public IObservable<AlarmEvent> Stream => _stream;
        public Task AckAsync(string alarmId, CancellationToken ct) => Task.CompletedTask;
        public Task ResetAsync(string alarmId, CancellationToken ct) => Task.CompletedTask;
        public Task MuteAsync(string alarmId, TimeSpan w, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakePopupGate : IAlarmPopupGate
    {
        private Action? _onDismiss;
        public int ShowCount { get; private set; }
        public List<string> PresentedIds { get; } = new();

        public void Show(AlarmEvent e, Action onDismissed)
        {
            ShowCount++;
            PresentedIds.Add(e.Id);
            _onDismiss = onDismissed;
        }

        public void DismissCurrent()
        {
            var d = _onDismiss;
            _onDismiss = null;
            d?.Invoke();
        }
    }
}
