using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.Alarms;

/// <summary>
/// Subscribes to <see cref="IAlarmService.Stream"/> filtered to fresh Critical
/// events and serializes their display through <see cref="IAlarmPopupGate"/>.
/// Only one popup is visible at a time; further Criticals enqueue and show after
/// dismissal. Duplicate Ids inside the queue are deduped.
/// </summary>
public sealed class AlarmPopupCoordinator : IDisposable
{
    private readonly IAlarmPopupGate _gate;
    private readonly IDisposable _subscription;

    private readonly object _lock = new();
    private bool _popupOpen;
    private readonly Queue<AlarmEvent> _queue = new();
    private readonly HashSet<string> _enqueuedOrShownIds = new();

    public AlarmPopupCoordinator(IAlarmService service, IAlarmPopupGate gate)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));

        _subscription = service.Stream
            .Where(e => e.Level == AlarmLevel.Critical && !e.Ack && !e.Reset && !e.Muted)
            .Subscribe(Handle);
    }

    private void Handle(AlarmEvent e)
    {
        lock (_lock)
        {
            if (!_enqueuedOrShownIds.Add(e.Id))
            {
                return; // duplicate Id, skip
            }

            if (_popupOpen)
            {
                _queue.Enqueue(e);
                return;
            }

            _popupOpen = true;
        }

        _gate.Show(e, OnDismissed);
    }

    private void OnDismissed()
    {
        AlarmEvent? next = null;
        lock (_lock)
        {
            if (_queue.Count > 0)
            {
                next = _queue.Dequeue();
            }
            else
            {
                _popupOpen = false;
            }
        }

        if (next is not null)
        {
            _gate.Show(next, OnDismissed);
        }
    }

    public void Dispose() => _subscription.Dispose();
}
