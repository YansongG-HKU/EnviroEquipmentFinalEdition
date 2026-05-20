using System;
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
public class SingleDeviceViewModelTests
{
    private sealed class FakeSession : IDeviceSessionManager
    {
        private readonly Subject<Device> _subject = new();
        public IObservable<Device> Devices => _subject;
        public Task ConnectAllAsync(CancellationToken ct) => Task.CompletedTask;

        public DeviceId? LastWriteTarget;
        public Setpoints? LastWriteSp;
        public DeviceWriteResult NextResult = DeviceWriteResult.Success();

        public Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct)
        {
            LastWriteTarget = id;
            LastWriteSp = sp;
            return Task.FromResult(NextResult);
        }

        public void Push(Device d) => _subject.OnNext(d);
    }

    private static Device Make(string id, DeviceStatus status, double? pv = 25.0, double? sv = 25.0)
        => new()
        {
            Id = new DeviceId(id),
            Bay = "A1",
            Type = DeviceType.Standard,
            Status = status,
            Setpoints = new Setpoints(sv, null, null),
            LastReading = new ReadingSnapshot(DateTimeOffset.UtcNow, pv, sv, null, null, null, null),
        };

    [Fact]
    public void Ctor_StartsWithNoSelection()
    {
        var vm = new SingleDeviceViewModel(new FakeSession(), new AdminRbacContext());
        vm.SelectedDeviceId.Should().BeNull();
        vm.Pv.Should().BeNull();
        vm.Sv.Should().BeNull();
    }

    [Fact]
    public async Task SelectDevice_LoadsLastReading()
    {
        var fake = new FakeSession();
        var vm = new SingleDeviceViewModel(fake, new AdminRbacContext());
        vm.Subscribe();
        fake.Push(Make("TH-01", DeviceStatus.Run, 50.5, 50.0));
        await Task.Delay(50);

        vm.Select("TH-01");
        vm.SelectedDeviceId.Should().Be("TH-01");
        vm.Pv.Should().Be(50.5);
        vm.Sv.Should().Be(50.0);
    }

    [Fact]
    public async Task WriteSetpointCommand_DispatchesToSessionManager()
    {
        var fake = new FakeSession();
        var vm = new SingleDeviceViewModel(fake, new AdminRbacContext());
        vm.Subscribe();
        fake.Push(Make("TH-01", DeviceStatus.Run));
        await Task.Delay(50);
        vm.Select("TH-01");

        vm.NewSvInput = 82.5;
        await vm.WriteSetpointCommand.ExecuteAsync(null);

        fake.LastWriteTarget!.Value.Should().Be("TH-01");
        fake.LastWriteSp!.Temp.Should().Be(82.5);
        vm.LastWriteOk.Should().BeTrue();
    }

    [Fact]
    public async Task WriteSetpoint_PropagatesFailure()
    {
        var fake = new FakeSession { NextResult = DeviceWriteResult.Failure("X", "boom") };
        var vm = new SingleDeviceViewModel(fake, new AdminRbacContext());
        vm.Subscribe();
        fake.Push(Make("TH-01", DeviceStatus.Run));
        await Task.Delay(50);
        vm.Select("TH-01");

        vm.NewSvInput = 60.0;
        await vm.WriteSetpointCommand.ExecuteAsync(null);

        vm.LastWriteOk.Should().BeFalse();
        vm.LastWriteError.Should().Be("boom");
    }

    [Theory]
    [InlineData(DeviceStatus.Idle, true,  false, false, false)]
    [InlineData(DeviceStatus.Run,  false, true,  true,  false)]
    [InlineData(DeviceStatus.Paused, true, false, true, false)]
    [InlineData(DeviceStatus.Alarm, false, false, true, true)]
    [InlineData(DeviceStatus.Offline, false, false, false, false)]
    public async Task CommandEnablement_Matrix(DeviceStatus status, bool canRun, bool canPause, bool canStop, bool canReset)
    {
        var fake = new FakeSession();
        var vm = new SingleDeviceViewModel(fake, new AdminRbacContext());
        vm.Subscribe();
        fake.Push(Make("TH-01", status));
        await Task.Delay(50);
        vm.Select("TH-01");

        vm.RunCommand.CanExecute(null).Should().Be(canRun);
        vm.PauseCommand.CanExecute(null).Should().Be(canPause);
        vm.StopCommand.CanExecute(null).Should().Be(canStop);
        vm.ResetCommand.CanExecute(null).Should().Be(canReset);
    }

    [Fact]
    public async Task RbacOperatorRole_DisablesStop()
    {
        var fake = new FakeSession();
        var rbac = new FixedRbac(Role.Operator);
        var vm = new SingleDeviceViewModel(fake, rbac);
        vm.Subscribe();
        fake.Push(Make("TH-01", DeviceStatus.Run));
        await Task.Delay(50);
        vm.Select("TH-01");
        vm.StopCommand.CanExecute(null).Should().BeFalse();
    }

    private sealed class FixedRbac : IRbacContext
    {
        public FixedRbac(Role r) { Current = r; }
        public Role Current { get; }
        public bool IsAtLeast(Role minimum) => (int)Current >= (int)minimum;
    }
}
