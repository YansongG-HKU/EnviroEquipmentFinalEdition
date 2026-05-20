using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.ViewModels.Alarms;

public sealed partial class AlarmRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool ack;

    [ObservableProperty]
    private bool muted;

    public string Id { get; }
    public DeviceId DeviceId { get; }
    public AlarmLevel Level { get; }
    public string Code { get; }
    public string Message { get; }
    public DateTimeOffset At { get; }

    public AlarmRowViewModel(AlarmEvent e)
    {
        Id = e.Id;
        DeviceId = e.DeviceId;
        Level = e.Level;
        Code = e.Code;
        Message = e.Message;
        At = e.At;
        ack = e.Ack;
        muted = e.Muted;
    }

    public void UpdateFrom(AlarmEvent e)
    {
        if (e.Id != Id) throw new InvalidOperationException("Cannot update row from different alarm id.");
        Ack = e.Ack;
        Muted = e.Muted;
    }
}
