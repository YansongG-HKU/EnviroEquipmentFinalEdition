using System;
using System.Globalization;
using System.Windows.Data;

namespace SiemensS7Demo.Wpf.ViewModels;

/// <summary>
/// MultiBinding converter: [navItemId, activeScreenId] -> bool (this item is the active screen).
/// Used by the left nav to show the active-indicator bar. A singleton avoids per-item allocations.
/// </summary>
public sealed class NavActiveConverter : IMultiValueConverter
{
    public static readonly NavActiveConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        return values[0] is string id && values[1] is string active &&
               string.Equals(id, active, StringComparison.Ordinal);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
