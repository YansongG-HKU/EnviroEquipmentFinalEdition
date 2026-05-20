using System;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.App.Alarms;

/// <summary>
/// Subscribes to <see cref="IDeviceSessionManager.Devices"/>, evaluates each
/// snapshot through the configured rules, and pushes the resulting events onto
/// a Subject. Maintains per-(DeviceId,Code) debounce timestamps and a mute table.
/// </summary>
public sealed class AlarmService : IAlarmService, IDisposable
{
    private readonly IAlarmRepository _repo;
    private readonly AlarmServiceOptions _options;
    private readonly Subject<AlarmEvent> _subject = new();
    private readonly IDisposable _subscription;

    private readonly ConcurrentDictionary<(string Device, string Code), DateTimeOffset> _lastEmit = new();
    private readonly ConcurrentDictionary<(string Device, string Code), DateTimeOffset> _mutedUntil = new();
    private readonly ConcurrentDictionary<string, AlarmEvent> _byId = new();

    public AlarmService(IDeviceSessionManager sessions, IAlarmRepository repo, AlarmServiceOptions options)
    {
        if (sessions is null) throw new ArgumentNullException(nameof(sessions));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _subscription = sessions.Devices
            .Where(d => d.LastReading is not null)
            .Subscribe(OnDevice);
    }

    public IObservable<AlarmEvent> Stream => _subject;

    public async Task AckAsync(string alarmId, CancellationToken ct)
    {
        if (alarmId is null) throw new ArgumentNullException(nameof(alarmId));
        await _repo.SetAckAsync(alarmId, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);

        if (_byId.TryGetValue(alarmId, out var existing))
        {
            var updated = existing with { Ack = true };
            _byId[alarmId] = updated;
            _subject.OnNext(updated);
        }
    }

    public Task ResetAsync(string alarmId, CancellationToken ct)
    {
        if (alarmId is null) throw new ArgumentNullException(nameof(alarmId));
        if (_byId.TryGetValue(alarmId, out var existing))
        {
            var updated = existing with { Reset = true };
            _byId[alarmId] = updated;
            _subject.OnNext(updated);
        }
        return Task.CompletedTask;
    }

    public Task MuteAsync(string alarmId, TimeSpan window, CancellationToken ct)
    {
        if (alarmId is null) throw new ArgumentNullException(nameof(alarmId));
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));

        if (_byId.TryGetValue(alarmId, out var existing))
        {
            var key = (existing.DeviceId.Value, existing.Code);
            _mutedUntil[key] = DateTimeOffset.UtcNow + window;
            var updated = existing with { Muted = true };
            _byId[alarmId] = updated;
            _subject.OnNext(updated);
        }
        return Task.CompletedTask;
    }

    private void OnDevice(Device device)
    {
        var snap = device.LastReading;
        if (snap is null) return;

        foreach (var evt in AlarmEvaluator.Evaluate(device.Id, snap, _options.Rules))
        {
            var key = (evt.DeviceId.Value, evt.Code);
            var now = DateTimeOffset.UtcNow;

            if (_mutedUntil.TryGetValue(key, out var mutedUntil) && now < mutedUntil)
            {
                continue;
            }

            if (_lastEmit.TryGetValue(key, out var last) && now - last < _options.DebounceWindow)
            {
                continue;
            }

            _lastEmit[key] = now;
            _byId[evt.Id] = evt;

            // Fire-and-forget repository write; persistence failures must not stall the stream.
            _ = _repo.InsertAsync(evt, CancellationToken.None);
            _subject.OnNext(evt);
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _subject.OnCompleted();
        _subject.Dispose();
    }
}
