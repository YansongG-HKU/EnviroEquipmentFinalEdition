using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Reactive.Testing;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using SiemensS7Demo.Wpf.Alarms;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.Alarms;

[Trait("Category", "Pkg2")]
public class AlarmToastNotifierTests
{
    private static readonly DeviceId D1 = new("d1");
    private static readonly DateTimeOffset T0 = new(2026, 5, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WarnEvent_AddsToast()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var scheduler = new TestScheduler();
        using var notifier = new AlarmToastNotifier(svc,
            autoDismiss: TimeSpan.FromSeconds(5),
            scheduler: scheduler);

        subject.OnNext(MakeEvent("a", AlarmLevel.Warn));

        notifier.Toasts.Should().HaveCount(1);
        notifier.Toasts[0].Id.Should().Be("a");
        notifier.Toasts[0].Level.Should().Be(AlarmLevel.Warn);
        notifier.Toasts[0].Title.Should().Contain("a");
        notifier.Toasts[0].Body.Should().Be("m-a");
    }

    [Fact]
    public void InfoEvent_AddsToast()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var scheduler = new TestScheduler();
        using var notifier = new AlarmToastNotifier(svc,
            autoDismiss: TimeSpan.FromSeconds(5),
            scheduler: scheduler);

        subject.OnNext(MakeEvent("a", AlarmLevel.Info));

        notifier.Toasts.Should().HaveCount(1);
        notifier.Toasts[0].Level.Should().Be(AlarmLevel.Info);
    }

    [Fact]
    public void CriticalEvent_DoesNotAddToast()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var scheduler = new TestScheduler();
        using var notifier = new AlarmToastNotifier(svc,
            autoDismiss: TimeSpan.FromSeconds(5),
            scheduler: scheduler);

        subject.OnNext(MakeEvent("a", AlarmLevel.Critical));

        notifier.Toasts.Should().BeEmpty(because: "criticals are handled by the popup coordinator, not toast");
    }

    [Fact]
    public void AckedWarnEvent_DoesNotAddToast()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var scheduler = new TestScheduler();
        using var notifier = new AlarmToastNotifier(svc,
            autoDismiss: TimeSpan.FromSeconds(5),
            scheduler: scheduler);

        subject.OnNext(MakeEvent("a", AlarmLevel.Warn) with { Ack = true });

        notifier.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void ResetWarnEvent_DoesNotAddToast()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var scheduler = new TestScheduler();
        using var notifier = new AlarmToastNotifier(svc,
            autoDismiss: TimeSpan.FromSeconds(5),
            scheduler: scheduler);

        subject.OnNext(MakeEvent("a", AlarmLevel.Warn) with { Reset = true });

        notifier.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void MutedWarnEvent_DoesNotAddToast()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var scheduler = new TestScheduler();
        using var notifier = new AlarmToastNotifier(svc,
            autoDismiss: TimeSpan.FromSeconds(5),
            scheduler: scheduler);

        subject.OnNext(MakeEvent("a", AlarmLevel.Warn) with { Muted = true });

        notifier.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void Toast_AutoDismissesAfterTimeout()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var scheduler = new TestScheduler();
        using var notifier = new AlarmToastNotifier(svc,
            autoDismiss: TimeSpan.FromSeconds(5),
            scheduler: scheduler);

        subject.OnNext(MakeEvent("a", AlarmLevel.Warn));
        notifier.Toasts.Should().HaveCount(1);

        // Advance to just before the dismiss
        scheduler.AdvanceBy(TimeSpan.FromSeconds(4).Ticks);
        notifier.Toasts.Should().HaveCount(1, because: "still within the auto-dismiss window");

        // Advance past the dismiss
        scheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks);
        notifier.Toasts.Should().BeEmpty(because: "auto-dismiss timer fired");
    }

    [Fact]
    public void MultipleToasts_DismissIndependently()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var scheduler = new TestScheduler();
        using var notifier = new AlarmToastNotifier(svc,
            autoDismiss: TimeSpan.FromSeconds(5),
            scheduler: scheduler);

        subject.OnNext(MakeEvent("a", AlarmLevel.Warn));
        scheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks);
        subject.OnNext(MakeEvent("b", AlarmLevel.Warn));

        notifier.Toasts.Should().HaveCount(2);

        // a should auto-dismiss at t=5; we are at t=2, advance 3.5s -> t=5.5: a gone, b remains
        scheduler.AdvanceBy(TimeSpan.FromSeconds(3.5).Ticks);
        notifier.Toasts.Should().HaveCount(1);
        notifier.Toasts[0].Id.Should().Be("b");

        // b dismisses at t=7 (subscribed at t=2 + 5s window); we are at t=5.5, advance 2s -> t=7.5
        scheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks);
        notifier.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void Dismiss_RemovesToastImmediately()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var scheduler = new TestScheduler();
        using var notifier = new AlarmToastNotifier(svc,
            autoDismiss: TimeSpan.FromSeconds(5),
            scheduler: scheduler);

        subject.OnNext(MakeEvent("a", AlarmLevel.Warn));
        notifier.Toasts.Should().HaveCount(1);
        var toast = notifier.Toasts[0];

        notifier.Dismiss(toast);
        notifier.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_StopsReceivingEvents()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var scheduler = new TestScheduler();
        var notifier = new AlarmToastNotifier(svc,
            autoDismiss: TimeSpan.FromSeconds(5),
            scheduler: scheduler);

        subject.OnNext(MakeEvent("a", AlarmLevel.Warn));
        notifier.Toasts.Should().HaveCount(1);

        notifier.Dispose();

        subject.OnNext(MakeEvent("b", AlarmLevel.Warn));
        notifier.Toasts.Should().HaveCount(1, because: "subscription was disposed; later events are ignored");
    }

    [Fact]
    public void NullService_Throws()
    {
        Action act = () => new AlarmToastNotifier(null!,
            TimeSpan.FromSeconds(5), Scheduler.Default);
        act.Should().Throw<ArgumentNullException>();
    }

    private static AlarmEvent MakeEvent(string id, AlarmLevel level)
        => new(id, D1, level, Code: id, Message: $"m-{id}", At: T0,
               Ack: false, Reset: false, Muted: false);

    private sealed class FakeService : IAlarmService
    {
        private readonly IObservable<AlarmEvent> _stream;
        public FakeService(IObservable<AlarmEvent> stream) => _stream = stream;
        public IObservable<AlarmEvent> Stream => _stream;
        public Task AckAsync(string alarmId, CancellationToken ct) => Task.CompletedTask;
        public Task ResetAsync(string alarmId, CancellationToken ct) => Task.CompletedTask;
        public Task MuteAsync(string alarmId, TimeSpan w, CancellationToken ct) => Task.CompletedTask;
    }
}
