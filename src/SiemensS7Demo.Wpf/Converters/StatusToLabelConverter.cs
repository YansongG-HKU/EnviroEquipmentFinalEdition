using System;
using System.Globalization;
using System.Windows.Data;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.Converters;

public sealed class StatusToLabelConverter : IValueConverter
{
    public static readonly NotVisibleWhenTrueConverter NotVisibleWhenTrue = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            DeviceStatus.Run       => "运行",
            DeviceStatus.Idle      => "待机",
            DeviceStatus.Scheduled => "预约",
            DeviceStatus.Paused    => "暂停",
            DeviceStatus.Alarm     => "报警",
            DeviceStatus.Offline   => "离线",
            _                      => "—",
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class NotVisibleWhenTrueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
