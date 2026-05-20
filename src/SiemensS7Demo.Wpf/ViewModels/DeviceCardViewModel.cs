using CommunityToolkit.Mvvm.ComponentModel;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class DeviceCardViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _bay = string.Empty;

    [ObservableProperty]
    private DeviceType _type;

    [ObservableProperty]
    private DeviceStatus _status;

    [ObservableProperty]
    private double? _pv;

    [ObservableProperty]
    private double? _sv;

    [ObservableProperty]
    private bool _online;

    public void Apply(Device d)
    {
        Id = d.Id.Value;
        Bay = d.Bay;
        Type = d.Type;
        Status = d.Status;
        Pv = d.LastReading?.Pv;
        Sv = d.Setpoints.Temp;
        Online = d.Status != DeviceStatus.Offline;
    }
}
