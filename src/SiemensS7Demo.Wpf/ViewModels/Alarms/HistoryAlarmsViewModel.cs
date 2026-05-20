using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.ViewModels.Alarms;

public sealed partial class HistoryAlarmsViewModel : ObservableObject
{
    private readonly IAlarmRepository _repo;

    [ObservableProperty]
    private DateTimeOffset? fromFilter;

    [ObservableProperty]
    private DateTimeOffset? toFilter;

    [ObservableProperty]
    private DeviceId? deviceFilter;

    [ObservableProperty]
    private AlarmLevel? levelFilter;

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<AlarmRowViewModel> Rows { get; } = new();

    public HistoryAlarmsViewModel(IAlarmRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var filter = new AlarmFilter(FromFilter, ToFilter, DeviceFilter, LevelFilter);
            var results = await _repo.QueryAsync(filter, CancellationToken.None).ConfigureAwait(true);
            Rows.Clear();
            foreach (var e in results)
            {
                Rows.Add(new AlarmRowViewModel(e));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        FromFilter = null;
        ToFilter = null;
        DeviceFilter = null;
        LevelFilter = null;
        await RefreshAsync();
    }
}
