using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using SiemensS7Demo.Wpf.ViewModels.Alarms;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg2")]
public class CurrentAlarmsViewModelTests
{
    private static readonly DeviceId D1 = new("d1");
    private static readonly DateTimeOffset T0 = new(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ctor_BindsToServiceStream_AndPopulatesRows()
    {
        var fake = new FakeAlarmService();
        using var vm = new CurrentAlarmsViewModel(fake);

        fake.Push(MakeEvent("a", AlarmLevel.Critical));
        fake.Push(MakeEvent("b", AlarmLevel.Warn));

        vm.Rows.Should().HaveCount(2);
        vm.Rows[0].Id.Should().Be("a");
        vm.Rows[1].Id.Should().Be("b");
    }

    [Fact]
    public void NewEvent_WithSameId_UpdatesExistingRowInPlace()
    {
        var fake = new FakeAlarmService();
        using var vm = new CurrentAlarmsViewModel(fake);

        fake.Push(MakeEvent("a", AlarmLevel.Critical) with { Ack = false });
        fake.Push(MakeEvent("a", AlarmLevel.Critical) with { Ack = true });

        vm.Rows.Should().HaveCount(1);
        vm.Rows[0].Ack.Should().BeTrue();
    }

    [Fact]
    public void ResetEvent_RemovesRow()
    {
        var fake = new FakeAlarmService();
        using var vm = new CurrentAlarmsViewModel(fake);

        fake.Push(MakeEvent("a", AlarmLevel.Critical));
        fake.Push(MakeEvent("a", AlarmLevel.Critical) with { Reset = true });

        vm.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task AckCommand_CallsServiceAck()
    {
        var fake = new FakeAlarmService();
        using var vm = new CurrentAlarmsViewModel(fake);
        fake.Push(MakeEvent("a", AlarmLevel.Critical));

        var row = vm.Rows[0];
        await vm.AckCommand.ExecuteAsync(row);

        fake.AckedIds.Should().ContainSingle().Which.Should().Be("a");
    }

    [Fact]
    public async Task ResetCommand_CallsServiceReset()
    {
        var fake = new FakeAlarmService();
        using var vm = new CurrentAlarmsViewModel(fake);
        fake.Push(MakeEvent("a", AlarmLevel.Critical));

        var row = vm.Rows[0];
        await vm.ResetCommand.ExecuteAsync(row);

        fake.ResetIds.Should().ContainSingle().Which.Should().Be("a");
    }

    [Fact]
    public async Task MuteCommand_CallsServiceMuteWithDefaultWindow()
    {
        var fake = new FakeAlarmService();
        using var vm = new CurrentAlarmsViewModel(fake);
        fake.Push(MakeEvent("a", AlarmLevel.Critical));

        var row = vm.Rows[0];
        await vm.MuteCommand.ExecuteAsync(row);

        fake.MutedIds.Should().ContainSingle().Which.Should().Be("a");
        fake.MutedWindows.Should().ContainSingle().Which.Should().Be(vm.DefaultMuteWindow);
    }

    [Fact]
    public void AckedRow_RemainsInList_ButReportsAckTrue()
    {
        var fake = new FakeAlarmService();
        using var vm = new CurrentAlarmsViewModel(fake);

        fake.Push(MakeEvent("a", AlarmLevel.Critical));
        fake.Push(MakeEvent("a", AlarmLevel.Critical) with { Ack = true });

        vm.Rows.Should().HaveCount(1);
        vm.Rows[0].Ack.Should().BeTrue();
    }

    private static AlarmEvent MakeEvent(string id, AlarmLevel level)
        => new(id, D1, level, Code: id, Message: $"msg-{id}", At: T0,
               Ack: false, Reset: false, Muted: false);

    private sealed class FakeAlarmService : IAlarmService
    {
        private readonly Subject<AlarmEvent> _subject = new();
        public IObservable<AlarmEvent> Stream => _subject;
        public List<string> AckedIds { get; } = new();
        public List<string> ResetIds { get; } = new();
        public List<string> MutedIds { get; } = new();
        public List<TimeSpan> MutedWindows { get; } = new();

        public Task AckAsync(string alarmId, CancellationToken ct) { AckedIds.Add(alarmId); return Task.CompletedTask; }
        public Task ResetAsync(string alarmId, CancellationToken ct) { ResetIds.Add(alarmId); return Task.CompletedTask; }
        public Task MuteAsync(string alarmId, TimeSpan w, CancellationToken ct)
        { MutedIds.Add(alarmId); MutedWindows.Add(w); return Task.CompletedTask; }

        public void Push(AlarmEvent e) => _subject.OnNext(e);
    }
}
