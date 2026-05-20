using System;

namespace SiemensS7Demo.Persistence.Entities;

public sealed class HistoryPointRow
{
    public long Id { get; set; }
    public required string DeviceId { get; set; }
    public DateTimeOffset At { get; set; }
    public double? Pv { get; set; }
    public double? Sv { get; set; }
    public double? Humid { get; set; }
    public double? HumidSv { get; set; }
}
