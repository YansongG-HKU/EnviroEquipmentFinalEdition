using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Lims;
using SiemensS7Demo.Domain.Lims;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg4")]
public class LimsViewModelTests
{
    private sealed class FakeLims : ILimsClient
    {
        private readonly List<LimsTask> _tasks;
        public FakeLims(IEnumerable<LimsTask> tasks) => _tasks = tasks.ToList();
        public Task<IReadOnlyList<LimsTask>> ListTasksAsync(LimsFilter f, CancellationToken ct)
        {
            IEnumerable<LimsTask> q = _tasks;
            if (f.Status is not null) q = q.Where(t => t.Status == f.Status);
            if (!string.IsNullOrEmpty(f.DeviceId)) q = q.Where(t => t.DeviceId == f.DeviceId);
            if (!string.IsNullOrEmpty(f.ProjectId)) q = q.Where(t => t.ProjectId == f.ProjectId);
            return Task.FromResult<IReadOnlyList<LimsTask>>(q.ToList());
        }
        public Task UploadResultAsync(LimsTaskResult r, CancellationToken ct) => Task.CompletedTask;
    }

    private static LimsTask T(string id, LimsTaskStatus s, string dev = "TH-01", string proj = "P") =>
        new(id, dev, proj, "name", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            null, null, s);

    [Fact]
    public async Task Refresh_PopulatesAllFourTabs()
    {
        var lims = new FakeLims(new[]
        {
            T("L-1", LimsTaskStatus.Todo),
            T("L-2", LimsTaskStatus.Running),
            T("L-3", LimsTaskStatus.Running),
            T("L-4", LimsTaskStatus.Done),
            T("L-5", LimsTaskStatus.Cancelled),
        });
        var vm = new LimsViewModel(lims);

        await vm.RefreshAsync(CancellationToken.None);

        vm.Todo.Should().HaveCount(1);
        vm.Running.Should().HaveCount(2);
        vm.Done.Should().HaveCount(1);
        vm.Cancelled.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeviceFilter_LimitsTasksAcrossAllTabs()
    {
        var lims = new FakeLims(new[]
        {
            T("L-1", LimsTaskStatus.Todo, dev: "TH-01"),
            T("L-2", LimsTaskStatus.Todo, dev: "TH-02"),
            T("L-3", LimsTaskStatus.Running, dev: "TH-02"),
        });
        var vm = new LimsViewModel(lims) { DeviceFilter = "TH-02" };

        await vm.RefreshAsync(CancellationToken.None);

        vm.Todo.Should().ContainSingle().Which.Id.Should().Be("L-2");
        vm.Running.Should().ContainSingle().Which.Id.Should().Be("L-3");
    }

    [Fact]
    public async Task ProjectFilter_AppliesToAllTabs()
    {
        var lims = new FakeLims(new[]
        {
            T("L-1", LimsTaskStatus.Todo, proj: "P1"),
            T("L-2", LimsTaskStatus.Todo, proj: "P2"),
        });
        var vm = new LimsViewModel(lims) { ProjectFilter = "P2" };

        await vm.RefreshAsync(CancellationToken.None);

        vm.Todo.Should().ContainSingle().Which.Id.Should().Be("L-2");
    }

    [Fact]
    public async Task ActiveTab_DefaultsToRunning_WhenAnyRunningTaskPresent()
    {
        var lims = new FakeLims(new[]
        {
            T("L-1", LimsTaskStatus.Todo),
            T("L-2", LimsTaskStatus.Running),
        });
        var vm = new LimsViewModel(lims);

        await vm.RefreshAsync(CancellationToken.None);

        vm.ActiveTab.Should().Be(LimsTab.Running);
    }

    [Fact]
    public async Task ActiveTab_DefaultsToTodo_WhenNoRunningTasks()
    {
        var lims = new FakeLims(new[] { T("L-1", LimsTaskStatus.Todo) });
        var vm = new LimsViewModel(lims);

        await vm.RefreshAsync(CancellationToken.None);

        vm.ActiveTab.Should().Be(LimsTab.Todo);
    }

    [Fact]
    public async Task LastSyncMessage_RecordsCount()
    {
        var lims = new FakeLims(new[]
        {
            T("L-1", LimsTaskStatus.Todo),
            T("L-2", LimsTaskStatus.Done),
        });
        var vm = new LimsViewModel(lims);

        await vm.RefreshAsync(CancellationToken.None);

        vm.LastSyncMessage.Should().NotBeNullOrEmpty().And.Contain("2 task");
    }
}
