using System;

namespace SiemensS7Demo.Domain.Alarms;

public sealed record AlarmRule(
    string Code,
    AlarmLevel Level,
    Predicate<ReadingSnapshot> Trigger,
    string MessageTemplate);
