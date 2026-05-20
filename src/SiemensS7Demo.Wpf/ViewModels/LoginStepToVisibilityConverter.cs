using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed class LoginStepToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LoginStep step && parameter is string name &&
            Enum.TryParse<LoginStep>(name, out var target))
        {
            return step == target ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
