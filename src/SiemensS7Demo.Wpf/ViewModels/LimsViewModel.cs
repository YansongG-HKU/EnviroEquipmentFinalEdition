using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App.Lims;
using SiemensS7Demo.Domain.Lims;

namespace SiemensS7Demo.Wpf.ViewModels;

public enum LimsTab { Todo, Running, Done, Cancelled }

public sealed partial class LimsViewModel : ObservableObject
{
    private readonly ILimsClient _lims;

    public LimsViewModel(ILimsClient lims) { _lims = lims; }

    public ObservableCollection<LimsTask> Todo { get; } = new();
    public ObservableCollection<LimsTask> Running { get; } = new();
    public ObservableCollection<LimsTask> Done { get; } = new();
    public ObservableCollection<LimsTask> Cancelled { get; } = new();

    [ObservableProperty] private LimsTab _activeTab = LimsTab.Todo;
    [ObservableProperty] private string? _deviceFilter;
    [ObservableProperty] private string? _projectFilter;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _lastSyncMessage;

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct)
    {
        IsLoading = true;
        try
        {
            var filter = new LimsFilter(DeviceFilter, ProjectFilter, null);
            var all = await _lims.ListTasksAsync(filter, ct);
            Todo.Clear(); Running.Clear(); Done.Clear(); Cancelled.Clear();
            foreach (var t in all)
            {
                switch (t.Status)
                {
                    case LimsTaskStatus.Todo: Todo.Add(t); break;
                    case LimsTaskStatus.Running: Running.Add(t); break;
                    case LimsTaskStatus.Done: Done.Add(t); break;
                    case LimsTaskStatus.Cancelled: Cancelled.Add(t); break;
                }
            }
            // Default to Running when any in-flight task exists; otherwise Todo (operators are
            // usually queueing up the next experiment, not auditing finished work).
            ActiveTab = Running.Count > 0 ? LimsTab.Running : LimsTab.Todo;
            LastSyncMessage = $"Synced {all.Count} task(s) at {DateTime.Now:HH:mm:ss}";
        }
        finally { IsLoading = false; }
    }
}
