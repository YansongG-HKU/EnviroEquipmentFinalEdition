using System;
using System.Collections.Generic;

namespace SiemensS7Demo.Domain.Users;

public sealed record Shift(string Code, string Name, DateOnly Date)
{
    public static Shift ForLocalNow(DateTimeOffset? now = null)
    {
        var moment = now ?? DateTimeOffset.Now;
        var hour = moment.LocalDateTime.Hour;
        var date = DateOnly.FromDateTime(moment.LocalDateTime);
        return hour switch
        {
            >= 6 and < 14 => new Shift("DAY-A", "白班 A", date),
            >= 14 and < 22 => new Shift("DAY-B", "白班 B", date),
            _ => new Shift("NIGHT", "夜班", date)
        };
    }

    public static IReadOnlyList<Shift> AllForDate(DateOnly date) =>
        new[]
        {
            new Shift("DAY-A", "白班 A", date),
            new Shift("DAY-B", "白班 B", date),
            new Shift("NIGHT", "夜班", date)
        };
}
