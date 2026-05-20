using System;
using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.Alarms;

/// <summary>
/// Subscribes to <see cref="IAlarmService.Stream"/>, filters to fresh
/// <see cref="AlarmLevel.Warn"/> and <see cref="AlarmLevel.Info"/> events (Critical
/// events are handled by <see cref="AlarmPopupCoordinator"/>), and projects them into
/// a live <see cref="ObservableCollection{T}"/> of <see cref="ToastNotificationViewModel"/>.
/// Each toast auto-dismisses after the configured timeout via the injected scheduler,
/// so test code can advance virtual time without sleeping.
/// </summary>
public sealed class AlarmToastNotifier : IDisposable
{
    private readonly TimeSpan _autoDismiss;
    private readonly IScheduler _scheduler;
    private readonly IDisposable _subscription;
    private readonly object _lock = new();

    public ObservableCollection<ToastNotificationViewModel> Toasts { get; } = new();

    /// <summary>
    /// Default production ctor — observes the stream on whatever thread the
    /// service publishes from and uses <see cref="DefaultScheduler.Instance"/>
    /// for auto-dismiss. The shell-side <see cref="AlarmToastHost"/> marshals
    /// collection changes back to the UI thread via a Dispatcher wrap.
    /// </summary>
    public AlarmToastNotifier(IAlarmService service)
        : this(service, TimeSpan.FromSeconds(5), DefaultScheduler.Instance) { }

    public AlarmToastNotifier(IAlarmService service, TimeSpan autoDismiss, IScheduler scheduler)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        if (scheduler is null) throw new ArgumentNullException(nameof(scheduler));
        if (autoDismiss <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(autoDismiss));

        _autoDismiss = autoDismiss;
        _scheduler = scheduler;

        _subscription = service.Stream
            .Where(e => (e.Level == AlarmLevel.Warn || e.Level == AlarmLevel.Info)
                        && !e.Ack && !e.Reset && !e.Muted)
            .Subscribe(OnEvent);
    }

    private void OnEvent(AlarmEvent e)
    {
        var toast = new ToastNotificationViewModel(e);
        lock (_lock)
        {
            Toasts.Add(toast);
        }
        _scheduler.Schedule(toast, _autoDismiss, (_, t) =>
        {
            Dismiss(t);
            return System.Reactive.Disposables.Disposable.Empty;
        });
    }

    /// <summary>
    /// Remove a toast immediately (called by user-driven close or by the auto-dismiss
    /// scheduler). Safe to call after the toast is already gone.
    /// </summary>
    public void Dismiss(ToastNotificationViewModel toast)
    {
        if (toast is null) return;
        lock (_lock)
        {
            Toasts.Remove(toast);
        }
    }

    public void Dispose() => _subscription.Dispose();
}
