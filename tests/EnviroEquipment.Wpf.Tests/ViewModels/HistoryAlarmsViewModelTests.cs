using System;
using System.Collections.Generic;
using System.Linq;
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
public class HistoryAlarmsViewModelTests
{
    private static readonly DeviceId D1 = new("d1");
    private static readonly DeviceId D2 = new("d2");
    private static readonly DateTimeOffset T0 = new(2026, 5, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadAsync_NoFilter_ReturnsAllRowsOrderedByAtDescending()
    {
        var repo = new InMemoryAlarmRepository();
        await Seed(repo, ("a", D1, T0), ("b", D1, T0.AddMinutes(1)), ("c", D2, T0.AddMinutes(2)));

        var vm = new HistoryAlarmsViewModel(repo);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Rows.Should().HaveCount(3);
        vm.Rows.Select(r => r.Id).Should().Equal("c", "b", "a");
    }

    [Fact]
    public async Task LoadAsync_DeviceFilter_AppliesAtRepoLayer()
    {
        var repo = new InMemoryAlarmRepository();
        await Seed(repo, ("a", D1, T0), ("b", D2, T0));

        var vm = new HistoryAlarmsViewModel(repo) { DeviceFilter = D1 };
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Rows.Should().HaveCount(1);
        vm.Rows[0].DeviceId.Should().Be(D1);
    }

    [Fact]
    public async Task LoadAsync_LevelFilter_AppliesAtRepoLayer()
    {
        var repo = new InMemoryAlarmRepository();
        await repo.InsertAsync(MakeEvent("a", D1, AlarmLevel.Info, T0), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("b", D1, AlarmLevel.Critical, T0), CancellationToken.None);

        var vm = new HistoryAlarmsViewModel(repo) { LevelFilter = AlarmLevel.Critical };
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Rows.Should().HaveCount(1);
        vm.Rows[0].Level.Should().Be(AlarmLevel.Critical);
    }

    [Fact]
    public async Task LoadAsync_DateRange_FiltersInclusively()
    {
        var repo = new InMemoryAlarmRepository();
        await Seed(repo, ("a", D1, T0), ("b", D1, T0.AddMinutes(5)), ("c", D1, T0.AddMinutes(10)));

        var vm = new HistoryAlarmsViewModel(repo)
        {
            FromFilter = T0.AddMinutes(1),
            ToFilter = T0.AddMinutes(9)
        };
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Rows.Should().HaveCount(1);
        vm.Rows[0].Code.Should().Be("b");
    }

    [Fact]
    public async Task ClearFiltersCommand_ResetsFiltersAndReloadsAll()
    {
        var repo = new InMemoryAlarmRepository();
        await Seed(repo, ("a", D1, T0), ("b", D2, T0));

        var vm = new HistoryAlarmsViewModel(repo) { DeviceFilter = D1 };
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Rows.Should().HaveCount(1);

        await vm.ClearFiltersCommand.ExecuteAsync(null);

        vm.DeviceFilter.Should().BeNull();
        vm.LevelFilter.Should().BeNull();
        vm.FromFilter.Should().BeNull();
        vm.ToFilter.Should().BeNull();
        vm.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task IsBusy_IsTrueDuringLoad_FalseAfter()
    {
        var slow = new SlowRepo();
        var vm = new HistoryAlarmsViewModel(slow);

        var pending = vm.RefreshCommand.ExecuteAsync(null);
        // Wait for the VM to set IsBusy=true before the repo returns.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!vm.IsBusy && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
        vm.IsBusy.Should().BeTrue();

        slow.Release();
        await pending;
        vm.IsBusy.Should().BeFalse();
    }

    private static async Task Seed(InMemoryAlarmRepository repo, params (string id, DeviceId dev, DateTimeOffset at)[] rows)
    {
        foreach (var (id, dev, at) in rows)
        {
            await repo.InsertAsync(MakeEvent(id, dev, AlarmLevel.Warn, at), CancellationToken.None);
        }
    }

    private static AlarmEvent MakeEvent(string id, DeviceId dev, AlarmLevel level, DateTimeOffset at)
        => new(id, dev, level, Code: id, Message: $"m-{id}", At: at,
               Ack: false, Reset: false, Muted: false);

    private sealed class SlowRepo : IAlarmRepository
    {
        private readonly TaskCompletionSource<bool> _gate = new();
        public void Release() => _gate.TrySetResult(true);
        public async Task<IReadOnlyList<AlarmEvent>> QueryAsync(AlarmFilter f, CancellationToken ct)
        {
            await _gate.Task;
            return Array.Empty<AlarmEvent>();
        }
        public Task InsertAsync(AlarmEvent e, CancellationToken ct) => Task.CompletedTask;
        public Task SetAckAsync(string id, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;
    }
}
