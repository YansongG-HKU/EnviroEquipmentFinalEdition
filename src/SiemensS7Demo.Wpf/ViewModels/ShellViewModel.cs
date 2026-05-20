using System;
using System.Collections.Generic;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly OverviewViewModel _overview;
    private readonly SingleDeviceViewModel _single;
    private DispatcherTimer? _clockTimer;

    public ShellViewModel(OverviewViewModel overview, SingleDeviceViewModel single)
    {
        _overview = overview;
        _single = single;
        _activeScreenViewModel = _overview;
        UpdateClock();
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
    /// Left navigation. Only 总览 (overview) and 当前试验 (single) are wired in Pkg 1; the rest
    /// are visible-but-disabled placeholders for later packages, mirroring NAV_ITEMS in mock-data.jsx.
    /// </summary>
    public IReadOnlyList<NavItem> NavItems { get; } = new List<NavItem>
    {
        new("overview", "总览",       "grid",    IsEnabled: true),
        new("single",   "当前试验",   "monitor", IsEnabled: true),
        new("program",  "程序编辑",   "edit",    IsEnabled: false),
        new("history",  "历史试验",   "archive", IsEnabled: false),
        new("alarm",    "报警中心",   "alarm",   IsEnabled: false),
        new("lims",     "LIMS / 黑灯", "link",   IsEnabled: false),
        new("layout",   "监控布局",   "layout",  IsEnabled: false),
        new("device",   "设备接入",   "plug",    IsEnabled: false),
        new("maint",    "设备维护",   "tool",    IsEnabled: false),
        new("users",    "用户与权限", "users",   IsEnabled: false),
        new("settings", "系统设置",   "cog",     IsEnabled: false),
    };

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
            _        => _overview,
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
