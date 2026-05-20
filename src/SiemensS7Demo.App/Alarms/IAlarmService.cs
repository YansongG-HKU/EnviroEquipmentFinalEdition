using System;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.App.Alarms;

/// <summary>
/// Hot-observable alarm pipeline. Subscribers receive a fresh event for every
/// rule match that survives debounce + mute.
/// </summary>
public interface IAlarmService
{
    IObservable<AlarmEvent> Stream { get; }
    Task AckAsync(string alarmId, CancellationToken ct);
    Task ResetAsync(string alarmId, CancellationToken ct);
    Task MuteAsync(string alarmId, TimeSpan window, CancellationToken ct);
}
