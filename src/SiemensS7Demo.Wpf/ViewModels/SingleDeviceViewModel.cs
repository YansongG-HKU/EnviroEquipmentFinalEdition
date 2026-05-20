using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Users;

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
    private double? _humidity;

    [ObservableProperty]
    private double? _humiditySv;

    [ObservableProperty]
    private string _bay = string.Empty;

    [ObservableProperty]
    private string? _programName;

    [ObservableProperty]
    private double _newSvInput;

    [ObservableProperty]
    private bool _lastWriteOk;

    [ObservableProperty]
    private string? _lastWriteError;

    [ObservableProperty]
    private string _segmentDisplay = "段 —/—";

    [ObservableProperty]
    private string _cycleDisplay = "循环 —/—";

    [ObservableProperty]
    private string _remainDisplay = "剩余 —";

    [ObservableProperty]
    private bool _hasHumidity;

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
        Humidity = d.LastReading?.Humid;
        HumiditySv = d.Setpoints.Humidity;
        HasHumidity = Humidity is not null;
        SelectedStatus = d.Status;

        var prog = d.Program;
        ProgramName = prog.Name;
        SegmentDisplay = prog.SegTotal > 0 ? $"段 {prog.Seg}/{prog.SegTotal}" : "段 —/—";
        CycleDisplay = prog.CycleTotal > 0 ? $"循环 {prog.Cycle}/{prog.CycleTotal}" : "循环 —/—";
        RemainDisplay = "剩余 " + DeviceCardViewModel.FormatRemain(prog.RemainSec);

        if (Pv is double pv)
        {
            PushTrend(pv);
        }
    }

    // ── Rolling PV trend for the single-device trend area (Polyline; OxyPlot is Pkg 3) ──
    private readonly System.Collections.Generic.Queue<double> _trend = new(TrendCapacity);
    public const int TrendCapacity = 60;
    public const double SparkWidth = 760;
    public const double SparkHeight = 220;

    [ObservableProperty]
    private System.Windows.Media.PointCollection _trendPoints = new();

    public System.Collections.Generic.IReadOnlyList<double> TrendBuffer => _trend.ToArray();

    private void PushTrend(double value)
    {
        if (_trend.Count >= TrendCapacity) _trend.Dequeue();
        _trend.Enqueue(value);
        var arr = _trend.ToArray();
        var pts = new System.Windows.Media.PointCollection();
        if (arr.Length >= 2)
        {
            var min = arr.Min();
            var max = arr.Max();
            var range = max - min;
            if (range <= double.Epsilon) range = 1;
            for (var i = 0; i < arr.Length; i++)
            {
                var x = (double)i / (arr.Length - 1) * SparkWidth;
                var y = SparkHeight - (arr[i] - min) / range * (SparkHeight - 8) - 4;
                pts.Add(new System.Windows.Point(System.Math.Round(x, 2), System.Math.Round(y, 2)));
            }
            pts.Freeze();
        }
        TrendPoints = pts;
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
