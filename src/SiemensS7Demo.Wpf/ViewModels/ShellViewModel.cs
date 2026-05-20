using System;
using System.Collections.Generic;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.Wpf.ViewModels.Alarms;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly OverviewViewModel _overview;
    private readonly SingleDeviceViewModel _single;
    private readonly CurrentAlarmsViewModel? _currentAlarms;
    private readonly HistoryAlarmsViewModel? _historyAlarms;
    private DispatcherTimer? _clockTimer;

    public ShellViewModel(OverviewViewModel overview, SingleDeviceViewModel single)
        : this(overview, single, null, null) { }

    public ShellViewModel(
        OverviewViewModel overview,
        SingleDeviceViewModel single,
        CurrentAlarmsViewModel? currentAlarms,
        HistoryAlarmsViewModel? historyAlarms)
    {
        _overview = overview;
        _single = single;
        _currentAlarms = currentAlarms;
        _historyAlarms = historyAlarms;
        _activeScreenViewModel = _overview;
        UpdateClock();
        BuildNavItems();
    }

    [ObservableProperty]
    private string _title = "温箱控制系统";

    // Brand block (matches TopBar in components-core.jsx).
    [ObservableProperty]
    private string _brandTitle = "温箱";

    [ObservableProperty]
    private string _brandSubtitle = "THERMOTRON CONTROL";

    // Breadcrumb — static for Pkg 1 (lab / shift wiring is Pkg 4).
    [ObservableProperty]
    private string _labName = "环境可靠性 3F";

    [ObservableProperty]
    private string _shift = "白班 B";

    // User chip — static until auth (Pkg 4).
    [ObservableProperty]
    private string _userName = "管理员 · Admin";

    [ObservableProperty]
    private string _userInitial = "A";

    [ObservableProperty]
    private string _clock = string.Empty;

    [ObservableProperty]
    private string _clockDate = string.Empty;

    [ObservableProperty]
    private string _activeScreen = "overview";

    [ObservableProperty]
    private object _activeScreenViewModel;

    [ObservableProperty]
    private int _alarmCount;

    [ObservableProperty]
    private int _warningCount;

    /// <summary>
    /// Left navigation. Overview + 当前试验 are wired in Pkg 1; Pkg 2 wires 报警中心 (current)
    /// and 历史试验 (alarm history) when those VMs are supplied via DI. Anything still null
    /// renders as a disabled placeholder.
    /// </summary>
    public List<NavItem> NavItems { get; } = new();

    private void BuildNavItems()
    {
        NavItems.Add(new("overview", "总览", "grid", IsEnabled: true));
        NavItems.Add(new("single", "当前试验", "monitor", IsEnabled: true));
        NavItems.Add(new("program", "程序编辑", "edit", IsEnabled: false));
        NavItems.Add(new("history", "历史试验", "archive", IsEnabled: _historyAlarms is not null));
        NavItems.Add(new("alarm", "报警中心", "alarm", IsEnabled: _currentAlarms is not null));
        NavItems.Add(new("lims", "LIMS / 黑灯", "link", IsEnabled: false));
        NavItems.Add(new("layout", "监控布局", "layout", IsEnabled: false));
        NavItems.Add(new("device", "设备接入", "plug", IsEnabled: false));
        NavItems.Add(new("maint", "设备维护", "tool", IsEnabled: false));
        NavItems.Add(new("users", "用户与权限", "users", IsEnabled: false));
        NavItems.Add(new("settings", "系统设置", "cog", IsEnabled: false));
    }

    /// <summary>
    /// Connect this shell to the live overview VM: surface its alarm count in the top bar.
    /// Called once at startup (after DI resolve) by App.OnStartup.
    /// </summary>
    public void BindOverview(OverviewViewModel overview)
    {
        AlarmCount = overview.AlarmCount;
        overview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverviewViewModel.AlarmCount))
            {
                AlarmCount = overview.AlarmCount;
            }
        };
    }

    /// <summary>Open a specific device in the single-device screen (from a card click).</summary>
    public void OpenDevice(string deviceId)
    {
        _single.Select(deviceId);
        Navigate("single");
    }

    /// <summary>Start the live HH:mm:ss clock. No-op in headless tests (no dispatcher running).</summary>
    public void StartClock()
    {
        if (_clockTimer is not null) return;
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        Clock = now.ToString("HH:mm:ss");
        ClockDate = now.ToString("yyyy-MM-dd");
    }

    [RelayCommand]
    private void Navigate(string id)
    {
        // Disabled (later-package) entries are not navigable.
        var target = NavItemFor(id);
        if (target is { IsEnabled: false }) return;

        ActiveScreen = id;
        ActiveScreenViewModel = id switch
        {
            "single" => _single,
            "alarm" => (object?)_currentAlarms ?? _overview,
            "history" => (object?)_historyAlarms ?? _overview,
            _ => _overview,
        };
    }

    private NavItem? NavItemFor(string id)
    {
        foreach (var n in NavItems)
        {
            if (n.Id == id) return n;
        }
        return null;
    }
}
