using System.Threading;
using System.Windows;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.Views.Alarms;

public partial class AlarmPopupWindow : Window
{
    private readonly IAlarmService _service;

    public AlarmPopupWindow(AlarmEvent e, IAlarmService service)
    {
        InitializeComponent();
        DataContext = e;
        _service = service;
    }

    private async void AcknowledgeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AlarmEvent evt)
        {
            await _service.AckAsync(evt.Id, CancellationToken.None);
        }
        Close();
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e) => Close();
}
