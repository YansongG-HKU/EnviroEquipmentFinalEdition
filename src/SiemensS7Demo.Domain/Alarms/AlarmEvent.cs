using System;

namespace SiemensS7Demo.Domain.Alarms;

public sealed record AlarmEvent(
    string Id,
    DeviceId DeviceId,
    AlarmLevel Level,
    string Code,
    string Message,
    DateTimeOffset At,
    bool Ack,
    bool Reset,
    bool Muted);
