using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    /// <summary>
    /// Maps a <see cref="DeviceStatus"/> to a themed brush. Pass ConverterParameter="Bg" for the
    /// translucent background variant used by status pills (matches the .pill.* tints in styles.css).
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var bg = parameter is string s && string.Equals(s, "Bg", StringComparison.OrdinalIgnoreCase);
        var key = value switch
        {
            DeviceStatus.Run       => "BrushRun",
            DeviceStatus.Idle      => "BrushOk",
            DeviceStatus.Scheduled => "BrushSched",
            DeviceStatus.Paused    => "BrushPause",
            DeviceStatus.Alarm     => "BrushAlarm",
            DeviceStatus.Offline   => "BrushOffline",
            _                      => "BrushTxt2",
        };
        if (bg)
        {
            // *Bg keys exist for the status set; Idle maps to Ok which also has a *Bg.
            key += "Bg";
        }
        var resource = Application.Current?.Resources[key];
        return resource as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
