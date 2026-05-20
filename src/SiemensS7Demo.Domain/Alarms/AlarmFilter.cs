using System;

namespace SiemensS7Demo.Domain.Alarms;

public sealed record AlarmFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    DeviceId? Device,
    AlarmLevel? Level);
