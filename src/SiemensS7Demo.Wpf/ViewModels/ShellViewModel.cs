using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly OverviewViewModel _overview;
    private readonly SingleDeviceViewModel _single;

    public ShellViewModel(OverviewViewModel overview, SingleDeviceViewModel single)
    {
        _overview = overview;
        _single = single;
        _activeScreenViewModel = _overview;
    }

    [ObservableProperty]
    private string _title = "温箱控制系统";

    [ObservableProperty]
    private string _activeScreen = "overview";

    [ObservableProperty]
    private object _activeScreenViewModel;

    [ObservableProperty]
    private int _alarmCount;

    [ObservableProperty]
    private int _warningCount;

    public IReadOnlyList<NavItem> NavItems { get; } = new List<NavItem>
    {
        new("overview", "总览", "grid"),
        new("single",   "单设备", "monitor"),
    };

    [RelayCommand]
    private void Navigate(string id)
    {
        ActiveScreen = id;
        ActiveScreenViewModel = id switch
        {
            "single" => _single,
            _        => _overview,
        };
    }
}
