using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
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
        var resource = Application.Current?.Resources[key];
        return resource as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
