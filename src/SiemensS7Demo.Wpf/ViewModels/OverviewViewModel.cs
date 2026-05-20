using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class OverviewViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceSessionManager? _sessionManager;
    private readonly object _applyLock = new();
    private SynchronizationContext? _uiContext;
    private IDisposable? _subscription;

    public OverviewViewModel() : this(null) { }

    public OverviewViewModel(IDeviceSessionManager? sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public ObservableCollection<DeviceCardViewModel> Cards { get; } = new();

    [ObservableProperty]
    private int _onlineCount;

    [ObservableProperty]
    private int _alarmCount;

    [ObservableProperty]
    private int _offlineCount;

    [ObservableProperty]
    private bool _anyAlarm;

    public void Subscribe()
    {
        if (_sessionManager is null) return;
        // Capture the UI SynchronizationContext at subscribe time. In the running WPF app
        // this is the DispatcherSynchronizationContext; in headless unit tests it is null,
        // so snapshots are applied synchronously (no dispatcher to deadlock against).
        _uiContext = SynchronizationContext.Current;
        _subscription?.Dispose();
        _subscription = _sessionManager.Devices.Subscribe(ApplyOnUi);
    }

    private void ApplyOnUi(Device device)
    {
        if (_uiContext is not null && _uiContext != SynchronizationContext.Current)
        {
            _uiContext.Post(_ => Apply(device), null);
        }
        else
        {
            Apply(device);
        }
    }

    private void Apply(Device device)
    {
        // In headless mode there is no UI dispatcher to serialize callbacks, so multiple
        // device polling threads can publish concurrently. Guard the ObservableCollection
        // mutation + counter recompute. In the running app every call already arrives on the
        // UI thread, so this lock is uncontended there.
        lock (_applyLock)
        {
            var existing = Cards.FirstOrDefault(c => c.Id == device.Id.Value);
            if (existing is null)
            {
                var card = new DeviceCardViewModel();
                card.Apply(device);
                Cards.Add(card);
            }
            else
            {
                existing.Apply(device);
            }
            Recompute();
        }
    }

    private void Recompute()
    {
        OnlineCount = Cards.Count(c => c.Online);
        AlarmCount = Cards.Count(c => c.Status == DeviceStatus.Alarm);
        OfflineCount = Cards.Count(c => c.Status == DeviceStatus.Offline);
        AnyAlarm = AlarmCount > 0;
    }

    [RelayCommand]
    private void Refresh() => Recompute();

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
