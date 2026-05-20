using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain.Alarms;
using SiemensS7Demo.Wpf.Views.Alarms;

namespace SiemensS7Demo.Wpf.Alarms;

/// <summary>
/// Production gate: opens <see cref="AlarmPopupWindow"/> on the UI thread and
/// hooks its Closed event to fire the dismissal callback exactly once. When no
/// WPF dispatcher is available (headless tests/host), invokes the callback
/// synchronously so the coordinator queue does not stall.
/// </summary>
public sealed class WindowAlarmPopupGate : IAlarmPopupGate
{
    private readonly IServiceProvider _provider;

    public WindowAlarmPopupGate(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public void Show(AlarmEvent e, Action onDismissed)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            onDismissed();
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            var service = _provider.GetRequiredService<IAlarmService>();
            var window = new AlarmPopupWindow(e, service);
            if (Application.Current?.MainWindow is { IsVisible: true } main && !ReferenceEquals(main, window))
            {
                window.Owner = main;
            }
            window.Closed += (_, _) => onDismissed();
            window.Show();
        });
    }
}
