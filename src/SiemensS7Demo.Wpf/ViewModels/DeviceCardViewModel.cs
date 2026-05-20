using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.ViewModels;

/// <summary>
/// One device tile in the overview grid. Mirrors the DeviceCard in 温箱202605/components-core.jsx:
/// status band + id/bay + status pill, temperature PV/SV, optional humidity PV/SV, a rolling PV
/// sparkline, program segment / cycle / remaining time, and (for alarm/pause) a message row.
/// </summary>
public sealed partial class DeviceCardViewModel : ObservableObject
{
    /// <summary>Points kept in the rolling PV trend buffer (matches the design's ~30-40 sample sparkline).</summary>
    public const int TrendCapacity = 30;
    /// <summary>Sparkline viewport the normalized geometry is mapped into (device-independent units).</summary>
    public const double SparkWidth = 240;
    public const double SparkHeight = 36;

    private readonly Queue<double> _trend = new(TrendCapacity);

    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _bay = string.Empty;
    [ObservableProperty] private DeviceType _type;
    [ObservableProperty] private DeviceStatus _status;
    [ObservableProperty] private double? _pv;
    [ObservableProperty] private double? _sv;
    [ObservableProperty] private double? _humidity;
    [ObservableProperty] private double? _humiditySv;
    [ObservableProperty] private bool _online;

    [ObservableProperty] private string? _programName;
    [ObservableProperty] private int _seg;
    [ObservableProperty] private int _segTotal;
    [ObservableProperty] private int _cycle;
    [ObservableProperty] private int _cycleTotal;
    [ObservableProperty] private int _remainSec;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string? _alarmCode;
    [ObservableProperty] private string? _alarmMessage;
    [ObservableProperty] private string? _note;

    [ObservableProperty]
    private PointCollection _trendPoints = new();

    /// <summary>True only for devices that report humidity (controls the humidity block visibility).</summary>
    public bool HasHumidity => Humidity is not null;

    /// <summary>段 N/M — segment progress, blank when the device has no program.</summary>
    public string SegmentDisplay => SegTotal > 0 ? $"{Seg}/{SegTotal}" : "—";

    /// <summary>循环 N/M — cycle progress.</summary>
    public string CycleDisplay => CycleTotal > 0 ? $"{Cycle}/{CycleTotal}" : "—";

    /// <summary>Remaining time as mm:ss, or h:mm:ss past an hour. "—" when nothing is running.</summary>
    public string RemainDisplay => FormatRemain(RemainSec);

    /// <summary>True when an alarm message row should be shown (alarm status + a message).</summary>
    public bool HasAlarm => Status == DeviceStatus.Alarm && !string.IsNullOrEmpty(AlarmMessage);

    /// <summary>True when the pause-reason row should be shown.</summary>
    public bool HasPauseNote => Status == DeviceStatus.Paused && !string.IsNullOrEmpty(Note);

    /// <summary>Formats RemainSec the way the design's fmtDuration does (mm:ss, or "Hh MMm" past an hour).</summary>
    public static string FormatRemain(int totalSeconds)
    {
        if (totalSeconds <= 0) return "—";
        var h = totalSeconds / 3600;
        var m = (totalSeconds % 3600) / 60;
        var s = totalSeconds % 60;
        return h > 0 ? $"{h}h {m:00}m" : $"{m:00}:{s:00}";
    }

    public void Apply(Device d)
    {
        Id = d.Id.Value;
        Bay = d.Bay;
        Type = d.Type;
        Status = d.Status;
        Pv = d.LastReading?.Pv;
        Sv = d.Setpoints.Temp;
        Humidity = d.LastReading?.Humid;
        HumiditySv = d.Setpoints.Humidity;
        Online = d.Status != DeviceStatus.Offline;

        var prog = d.Program;
        ProgramName = prog.Name;
        Seg = prog.Seg;
        SegTotal = prog.SegTotal;
        Cycle = prog.Cycle;
        CycleTotal = prog.CycleTotal;
        RemainSec = prog.RemainSec;
        Progress = prog.Progress;
        AlarmCode = prog.AlarmCode;
        AlarmMessage = prog.AlarmMessage;
        Note = prog.Note;

        // Recompute the computed display strings whose inputs just changed.
        OnPropertyChanged(nameof(HasHumidity));
        OnPropertyChanged(nameof(SegmentDisplay));
        OnPropertyChanged(nameof(CycleDisplay));
        OnPropertyChanged(nameof(RemainDisplay));
        OnPropertyChanged(nameof(HasAlarm));
        OnPropertyChanged(nameof(HasPauseNote));

        if (Pv is double pv)
        {
            PushTrend(pv);
        }
    }

    /// <summary>Append one PV sample to the rolling buffer and rebuild the sparkline geometry.</summary>
    public void PushTrend(double value)
    {
        if (_trend.Count >= TrendCapacity)
        {
            _trend.Dequeue();
        }
        _trend.Enqueue(value);
        TrendPoints = BuildPoints(_trend);
    }

    /// <summary>Current rolling buffer contents, oldest first. Exposed for tests.</summary>
    public IReadOnlyList<double> TrendBuffer => _trend.ToArray();

    private static PointCollection BuildPoints(IReadOnlyCollection<double> values)
    {
        var pts = new PointCollection();
        if (values.Count < 2)
        {
            return pts;
        }
        var arr = values.ToArray();
        var min = arr.Min();
        var max = arr.Max();
        var range = max - min;
        if (range <= double.Epsilon) range = 1; // flat line -> centered
        for (var i = 0; i < arr.Length; i++)
        {
            var x = arr.Length == 1 ? 0 : (double)i / (arr.Length - 1) * SparkWidth;
            // Invert Y: WPF y grows downward; high values should sit near the top.
            var y = SparkHeight - (arr[i] - min) / range * (SparkHeight - 4) - 2;
            pts.Add(new Point(Math.Round(x, 2), Math.Round(y, 2)));
        }
        pts.Freeze();
        return pts;
    }
}
