namespace SiemensS7Demo.Wpf.ViewModels;

/// <summary>
/// One left-nav entry. <see cref="IsEnabled"/> is false for screens that belong to later
/// packages (program/history/alarm-center/LIMS/etc.) — they render as disabled placeholders.
/// <see cref="Badge"/> is an optional count chip (e.g. open alarms on 报警中心).
/// </summary>
public sealed record NavItem(string Id, string Label, string Icon, bool IsEnabled = true, int? Badge = null);
