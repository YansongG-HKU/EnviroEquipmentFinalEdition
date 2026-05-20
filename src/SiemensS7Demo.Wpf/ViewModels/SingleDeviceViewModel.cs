using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class SingleDeviceViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceSessionManager? _sessionManager;
    private readonly IRbacContext _rbac;
    private readonly Dictionary<string, Device> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _applyLock = new();
    private SynchronizationContext? _uiContext;
    private IDisposable? _subscription;

    public SingleDeviceViewModel()
        : this(null, new AdminRbacContext()) { }

    public SingleDeviceViewModel(IDeviceSessionManager? sessionManager, IRbacContext rbac)
    {
        _sessionManager = sessionManager;
        _rbac = rbac;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(WriteSetpointCommand))]
    private string? _selectedDeviceId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private DeviceStatus? _selectedStatus;

    [ObservableProperty]
    private double? _pv;

    [ObservableProperty]
    private double? _sv;

    [ObservableProperty]
    private string _bay = string.Empty;

    [ObservableProperty]
    private double _newSvInput;

    [ObservableProperty]
    private bool _lastWriteOk;

    [ObservableProperty]
    private string? _lastWriteError;

    [ObservableProperty]
    private string _segmentDisplay = "段 —/—";

    public void Subscribe()
    {
        if (_sessionManager is null) return;
        // Capture the UI SynchronizationContext (see OverviewViewModel for rationale).
        _uiContext = SynchronizationContext.Current;
        _subscription?.Dispose();
        _subscription = _sessionManager.Devices.Subscribe(ApplyOnUi);
    }

    private void ApplyOnUi(Device d)
    {
        if (_uiContext is not null && _uiContext != SynchronizationContext.Current)
        {
            _uiContext.Post(_ => Apply(d), null);
        }
        else
        {
            Apply(d);
        }
    }

    private void Apply(Device d)
    {
        // See OverviewViewModel: serialize concurrent device-thread publishes in headless mode.
        lock (_applyLock)
        {
            _snapshots[d.Id.Value] = d;
            if (SelectedDeviceId == d.Id.Value)
            {
                HydrateFrom(d);
            }
        }
    }

    public void Select(string deviceId)
    {
        SelectedDeviceId = deviceId;
        lock (_applyLock)
        {
            if (_snapshots.TryGetValue(deviceId, out var d))
            {
                HydrateFrom(d);
            }
        }
    }

    private void HydrateFrom(Device d)
    {
        Bay = d.Bay;
        Pv = d.LastReading?.Pv;
        Sv = d.Setpoints.Temp;
        SelectedStatus = d.Status;
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task WriteSetpointAsync()
    {
        if (_sessionManager is null || SelectedDeviceId is null) return;
        var result = await _sessionManager.WriteSetpointAsync(
            new DeviceId(SelectedDeviceId),
            new Setpoints(NewSvInput, null, null),
            CancellationToken.None);
        LastWriteOk = result.Ok;
        LastWriteError = result.ErrorMessage;
        if (result.Ok) Sv = NewSvInput;
    }

    private bool CanWrite() =>
        SelectedDeviceId is not null
        && SelectedStatus is not (DeviceStatus.Offline or null)
        && _rbac.IsAtLeast(Role.Engineer);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Run() { /* command write goes to PLC in Pkg 3 program engine */ }
    private bool CanRun() => SelectedStatus is DeviceStatus.Idle or DeviceStatus.Paused && _rbac.IsAtLeast(Role.Operator);

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() { /* command write goes to PLC in Pkg 3 program engine */ }
    private bool CanPause() => SelectedStatus is DeviceStatus.Run && _rbac.IsAtLeast(Role.Operator);

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() { /* command write goes to PLC in Pkg 3 program engine */ }
    private bool CanStop() => SelectedStatus is DeviceStatus.Run or DeviceStatus.Paused or DeviceStatus.Alarm
        && _rbac.IsAtLeast(Role.Engineer);

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset() { /* alarm reset write — wired in Pkg 2 */ }
    private bool CanReset() => SelectedStatus is DeviceStatus.Alarm && _rbac.IsAtLeast(Role.Operator);

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
