using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.App.Alarms;

/// <summary>
/// Thread-safe list-backed repository used by Pkg 2 until Pkg 3 SQLite ships.
/// </summary>
public sealed class InMemoryAlarmRepository : IAlarmRepository
{
    private readonly object _lock = new();
    private readonly List<AlarmEvent> _events = new();

    public Task InsertAsync(AlarmEvent e, CancellationToken ct)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));
        lock (_lock)
        {
            _events.Add(e);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AlarmEvent>> QueryAsync(AlarmFilter f, CancellationToken ct)
    {
        if (f is null) throw new ArgumentNullException(nameof(f));
        IReadOnlyList<AlarmEvent> result;
        lock (_lock)
        {
            result = _events
                .Where(e => !f.From.HasValue || e.At >= f.From.Value)
                .Where(e => !f.To.HasValue || e.At <= f.To.Value)
                .Where(e => f.Device is null || e.DeviceId == f.Device)
                .Where(e => !f.Level.HasValue || e.Level == f.Level.Value)
                .OrderByDescending(e => e.At)
                .ToList();
        }
        return Task.FromResult(result);
    }

    public Task SetAckAsync(string id, DateTimeOffset at, CancellationToken ct)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));
        lock (_lock)
        {
            for (var i = 0; i < _events.Count; i++)
            {
                if (_events[i].Id == id)
                {
                    _events[i] = _events[i] with { Ack = true };
                    break;
                }
            }
        }
        return Task.CompletedTask;
    }
}
