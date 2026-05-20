using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.Wpf.ViewModels;

/// <summary>
/// One left-nav entry. <see cref="IsEnabled"/> is false for screens that belong to later
/// packages (program/history/alarm-center/LIMS/etc.) — they render as disabled placeholders.
/// <see cref="Badge"/> is an optional count chip (e.g. open alarms on 报警中心).
/// <see cref="MinimumRole"/> is the lowest role permitted to see this entry; null means visible
/// to everyone (including unauthenticated). Pkg 4 sign-in re-evaluates <see cref="IsVisible"/>
/// via <see cref="ShellViewModel.ApplyRbac"/>.
///
/// Constructed as a class (not a record) so per-sign-in visibility flips don't require
/// reallocating the nav list.
/// </summary>
public sealed class NavItem
{
    public NavItem(string id, string label, string icon, bool isEnabled = true, int? badge = null, Role? minimumRole = null)
    {
        Id = id;
        Label = label;
        Icon = icon;
        IsEnabled = isEnabled;
        Badge = badge;
        MinimumRole = minimumRole;
        IsVisible = true;
    }

    public string Id { get; }
    public string Label { get; }
    public string Icon { get; }
    public bool IsEnabled { get; set; }
    public int? Badge { get; set; }
    public Role? MinimumRole { get; }
    public bool IsVisible { get; set; }
}
