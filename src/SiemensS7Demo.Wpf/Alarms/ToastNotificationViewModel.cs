using CommunityToolkit.Mvvm.ComponentModel;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.Alarms;

/// <summary>
/// View-model for a non-blocking toast notification surfaced on the shell for
/// Warn/Info alarm events. Bound by <see cref="AlarmToastHost"/>.
/// </summary>
public sealed partial class ToastNotificationViewModel : ObservableObject
{
    public string Id { get; }
    public DeviceId DeviceId { get; }
    public AlarmLevel Level { get; }
    public string Code { get; }
    public string Title { get; }
    public string Body { get; }

    [ObservableProperty]
    private double opacity = 1.0;

    public ToastNotificationViewModel(AlarmEvent e)
    {
        Id = e.Id;
        DeviceId = e.DeviceId;
        Level = e.Level;
        Code = e.Code;
        Title = $"{e.Level}: {e.Code}";
        Body = e.Message;
    }
}
