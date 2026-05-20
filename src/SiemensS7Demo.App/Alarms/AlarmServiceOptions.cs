using System;
using System.Collections.Generic;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.App.Alarms;

public sealed class AlarmServiceOptions
{
    public TimeSpan DebounceWindow { get; init; } = TimeSpan.FromSeconds(5);
    public IReadOnlyList<AlarmRule> Rules { get; init; } = Array.Empty<AlarmRule>();
}
