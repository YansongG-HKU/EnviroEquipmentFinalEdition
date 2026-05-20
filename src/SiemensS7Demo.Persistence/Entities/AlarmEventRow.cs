using System;

namespace SiemensS7Demo.Persistence.Entities;

/// <summary>
/// Schema mirror of Pkg 2's <c>AlarmEvent</c> domain record. Pkg 2 currently uses an in-memory
/// repository; Pkg 3 ships this table + <c>SqliteAlarmRepository</c> behind the same
/// <c>IAlarmRepository</c> interface so Pkg 2's DI registration can swap in one line later.
/// <c>Level</c> stored as INT (0=Info, 1=Warn, 2=Critical) to keep the schema decoupled from
/// the Pkg 2 enum identifier strings.
/// </summary>
public sealed class AlarmEventRow
{
    public required string Id { get; set; }
    public required string DeviceId { get; set; }
    public int Level { get; set; }
    public required string Code { get; set; }
    public required string Message { get; set; }
    public DateTimeOffset At { get; set; }
    public bool Ack { get; set; }
    public bool Reset { get; set; }
    public bool Muted { get; set; }
}
