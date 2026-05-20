using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.App.Alarms;

/// <summary>
/// Default alarm rules shipped with Pkg 2. Conservative defaults that match the
/// seeded TH-03 alarm device (152.8C > 80C limit) so the headless smoke fires.
/// </summary>
public static class AlarmRulesCatalog
{
    public static readonly AlarmRule[] Default =
    {
        new("TEMP_HIGH", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0,
            "Temperature {Pv:F1}C exceeds 80C limit"),
        new("TEMP_LOW", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value < -40.0,
            "Temperature {Pv:F1}C below -40C limit"),
        new("HUMID_HIGH", AlarmLevel.Warn,
            s => s.Humid.HasValue && s.Humid.Value > 95.0,
            "Humidity {Humid:F1}% exceeds 95% limit"),
        new("PV_MISSING", AlarmLevel.Info,
            s => !s.Pv.HasValue,
            "Process value not reported by device"),
    };
}
