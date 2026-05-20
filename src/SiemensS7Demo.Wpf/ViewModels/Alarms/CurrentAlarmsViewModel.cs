using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.ViewModels.Alarms;

public sealed partial class CurrentAlarmsViewModel : ObservableObject, IDisposable
{
    private readonly IAlarmService _service;
    private readonly IDisposable _subscription;
    private readonly Dictionary<string, AlarmRowViewModel> _byId = new();

    public TimeSpan DefaultMuteWindow { get; } = TimeSpan.FromMinutes(15);

    public ObservableCollection<AlarmRowViewModel> Rows { get; } = new();

    public CurrentAlarmsViewModel(IAlarmService service)
        : this(service, scheduler: ImmediateScheduler.Instance) { }

    internal CurrentAlarmsViewModel(IAlarmService service, IScheduler scheduler)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _subscription = service.Stream
            .ObserveOn(scheduler)
            .Subscribe(OnEvent);
    }

    private void OnEvent(AlarmEvent e)
    {
        if (e.Reset)
        {
            if (_byId.Remove(e.Id, out var row))
            {
                Rows.Remove(row);
            }
            return;
        }

        if (_byId.TryGetValue(e.Id, out var existing))
        {
            existing.UpdateFrom(e);
            return;
        }

        var newRow = new AlarmRowViewModel(e);
        _byId[e.Id] = newRow;
        Rows.Add(newRow);
    }

    [RelayCommand]
    private async Task AckAsync(AlarmRowViewModel? row)
    {
        if (row is null) return;
        await _service.AckAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResetAsync(AlarmRowViewModel? row)
    {
        if (row is null) return;
        await _service.ResetAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task MuteAsync(AlarmRowViewModel? row)
    {
        if (row is null) return;
        await _service.MuteAsync(row.Id, DefaultMuteWindow, CancellationToken.None).ConfigureAwait(true);
    }

    public void Dispose() => _subscription.Dispose();
}
