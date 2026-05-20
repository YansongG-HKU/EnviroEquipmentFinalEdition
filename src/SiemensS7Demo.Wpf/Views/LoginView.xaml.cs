using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SiemensS7Demo.Domain.Users;
using SiemensS7Demo.Wpf.ViewModels;

namespace SiemensS7Demo.Wpf.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private void OnSelectAccount(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is Button b && b.Tag is string code)
        {
            vm.SelectUser(code);
        }
    }

    private void OnPwdChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is PasswordBox pb)
        {
            vm.Password = pb.Password;
        }
    }

    private void OnSelectShift(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is Button b && b.Tag is Shift shift)
        {
            vm.SelectedShift = shift;
        }
    }
}
