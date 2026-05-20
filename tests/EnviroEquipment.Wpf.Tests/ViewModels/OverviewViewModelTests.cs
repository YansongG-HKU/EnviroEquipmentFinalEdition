using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg1")]
public class OverviewViewModelTests
{
    private sealed class FakeSessionManager : IDeviceSessionManager
    {
        private readonly Subject<Device> _subject = new();
        public IObservable<Device> Devices => _subject;
        public Task ConnectAllAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct)
            => Task.FromResult(DeviceWriteResult.Success());
        public void Push(Device d) => _subject.OnNext(d);
    }

    private static Device Make(string id, DeviceStatus status)
        => new() { Id = new DeviceId(id), Bay = "A1", Type = DeviceType.Standard, Status = status };

    [Fact]
    public void Ctor_StartsWithEmptyGrid()
    {
        var vm = new OverviewViewModel(new FakeSessionManager());
        vm.Cards.Should().BeEmpty();
        vm.OnlineCount.Should().Be(0);
        vm.AlarmCount.Should().Be(0);
    }

    [Fact]
    public async Task IncomingDeviceSnapshot_AddsCard()
    {
        var fake = new FakeSessionManager();
        var vm = new OverviewViewModel(fake);
        vm.Subscribe();

        fake.Push(Make("TH-01", DeviceStatus.Run));
        await Task.Delay(50);

        vm.Cards.Should().HaveCount(1);
        vm.Cards[0].Id.Should().Be("TH-01");
        vm.Cards[0].Status.Should().Be(DeviceStatus.Run);
    }

    [Fact]
    public async Task SecondSnapshotForSameDevice_UpdatesInPlace()
    {
        var fake = new FakeSessionManager();
        var vm = new OverviewViewModel(fake);
        vm.Subscribe();

        fake.Push(Make("TH-01", DeviceStatus.Run));
        fake.Push(Make("TH-01", DeviceStatus.Alarm));
        await Task.Delay(50);

        vm.Cards.Should().HaveCount(1);
        vm.Cards[0].Status.Should().Be(DeviceStatus.Alarm);
    }

    [Fact]
    public async Task SummaryCounters_TrackByStatus()
    {
        var fake = new FakeSessionManager();
        var vm = new OverviewViewModel(fake);
        vm.Subscribe();

        fake.Push(Make("TH-01", DeviceStatus.Run));
        fake.Push(Make("TH-02", DeviceStatus.Alarm));
        fake.Push(Make("TH-03", DeviceStatus.Offline));
        await Task.Delay(50);

        vm.OnlineCount.Should().Be(2);
        vm.AlarmCount.Should().Be(1);
        vm.OfflineCount.Should().Be(1);
    }

    [Fact]
    public async Task AlarmPipFlag_FlipsTrueWhenAnyDeviceAlarms()
    {
        var fake = new FakeSessionManager();
        var vm = new OverviewViewModel(fake);
        vm.Subscribe();

        fake.Push(Make("TH-01", DeviceStatus.Run));
        await Task.Delay(50);
        vm.AnyAlarm.Should().BeFalse();

        fake.Push(Make("TH-01", DeviceStatus.Alarm));
        await Task.Delay(50);
        vm.AnyAlarm.Should().BeTrue();
    }
}
