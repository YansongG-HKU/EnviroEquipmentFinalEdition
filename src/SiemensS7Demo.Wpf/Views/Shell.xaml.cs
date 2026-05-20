using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.App.Alarms;

namespace SiemensS7Demo.Wpf.Views;

public partial class Shell : Window
{
    public Shell()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // M2.5: wire the toast overlay to the live alarm stream. The notifier owns the subscription;
        // the host simply binds its ItemsSource.
        if (Application.Current is App app)
        {
            var svc = app.Services.GetRequiredService<IAlarmService>();
            ToastHost.Attach(svc);
        }
    }
}
