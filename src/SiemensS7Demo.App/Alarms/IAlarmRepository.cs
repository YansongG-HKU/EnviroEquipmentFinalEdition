using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.App.Alarms;

/// <summary>
/// Persistence boundary for alarm history. Pkg 2 ships an in-memory implementation;
/// Pkg 3 M3.1 will introduce a SQLite-backed implementation against the same contract.
/// </summary>
public interface IAlarmRepository
{
    Task InsertAsync(AlarmEvent e, CancellationToken ct);
    Task<IReadOnlyList<AlarmEvent>> QueryAsync(AlarmFilter f, CancellationToken ct);
    Task SetAckAsync(string id, DateTimeOffset at, CancellationToken ct);
}
