using CommunityToolkit.Mvvm.ComponentModel;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "温箱控制系统";

    // Full M1.2/M1.4/M1.5 fields are added in later tasks.
}
