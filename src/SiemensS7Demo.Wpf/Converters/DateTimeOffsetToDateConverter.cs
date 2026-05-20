using System;
using System.Globalization;
using System.Windows.Data;

namespace SiemensS7Demo.Wpf.Converters;

public sealed class DateTimeOffsetToDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            DateTimeOffset dto => (DateTime?)dto.LocalDateTime.Date,
            _ => null,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            DateTime dt => new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt)),
            _ => null,
        };
    }
}
