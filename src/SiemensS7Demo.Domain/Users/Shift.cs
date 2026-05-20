using System;
using System.Collections.Generic;

namespace SiemensS7Demo.Domain.Users;

public sealed record Shift(string Code, string Name, DateOnly Date)
{
    public static Shift ForLocalNow(DateTimeOffset? now = null)
    {
        var moment = now ?? DateTimeOffset.Now;
        // Use the DateTimeOffset's own wallclock — when a caller passes a value with
        // an explicit offset, they mean "treat this as the local time at that offset",
        // not "convert to the CI runner's local zone first". The default path is also
        // correct: DateTimeOffset.Now returns the machine wallclock paired with its
        // local offset, so .Hour is the local hour either way.
        var hour = moment.Hour;
        var date = DateOnly.FromDateTime(moment.DateTime);
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
