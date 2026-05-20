using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.Wpf.ViewModels;

public enum LoginStep { SelectAccount, EnterPassword, ConfirmShift, SignedIn }

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;

    public LoginViewModel(IAuthService auth)
    {
        _auth = auth;
        var today = DateOnly.FromDateTime(DateTime.Now);
        Shifts = Shift.AllForDate(today);
        SelectedShift = Shift.ForLocalNow();
    }

    [ObservableProperty]
    private LoginStep _step = LoginStep.SelectAccount;

    [ObservableProperty]
    private string? _selectedCode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitPasswordCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    private Shift? _selectedShift;

    [ObservableProperty]
    private string? _errorMessage;

    public IReadOnlyList<Shift> Shifts { get; }

    /// <summary>
    /// 4 known accounts mirroring the 202605 design's login picker. Real systems would render the
    /// full user list from <see cref="IUserRepository"/>; this preserves the locked design.
    /// </summary>
    public IReadOnlyList<(string Code, string Display)> KnownAccounts { get; } =
        new[]
        {
            ("OP-1042", "李工 · 实验员"),
            ("OP-1043", "王工 · 实验员"),
            ("EN-2011", "张工 · 工程师"),
            ("AD-0001", "Admin · 管理员"),
        }.Select(x => (x.Item1, x.Item2)).ToList();

    public void SelectUser(string code)
    {
        SelectedCode = code;
        ErrorMessage = null;
        Step = LoginStep.EnterPassword;
    }

    [RelayCommand(CanExecute = nameof(CanSubmitPassword))]
    public async Task SubmitPasswordAsync(CancellationToken ct)
    {
        if (SelectedCode is null) return;
        // Probe-and-release: we sign in to validate the password against the hasher, then sign out
        // so the persistent session only begins after the shift-confirm step in ConfirmShiftAsync.
        var probe = await _auth.SignInAsync(SelectedCode, Password, SelectedShift ?? Shift.ForLocalNow(), ct);
        if (!probe.Success)
        {
            ErrorMessage = probe.ErrorMessage;
            return;
        }
        _auth.SignOut();
        ErrorMessage = null;
        Step = LoginStep.ConfirmShift;
    }

    private bool CanSubmitPassword() => !string.IsNullOrEmpty(SelectedCode) && !string.IsNullOrEmpty(Password);

    [RelayCommand]
    public async Task ConfirmShiftAsync(CancellationToken ct)
    {
        if (SelectedCode is null || SelectedShift is null) return;
        var result = await _auth.SignInAsync(SelectedCode, Password, SelectedShift, ct);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            Step = LoginStep.EnterPassword;
            return;
        }
        ErrorMessage = null;
        Step = LoginStep.SignedIn;
    }

    [RelayCommand]
    public void Back()
    {
        ErrorMessage = null;
        switch (Step)
        {
            case LoginStep.EnterPassword:
                Password = string.Empty;
                Step = LoginStep.SelectAccount;
                break;
            case LoginStep.ConfirmShift:
                Step = LoginStep.EnterPassword;
                break;
            default:
                Password = string.Empty;
                SelectedCode = null;
                Step = LoginStep.SelectAccount;
                break;
        }
    }

    [RelayCommand]
    public void SelectShift(Shift shift) => SelectedShift = shift;
}
