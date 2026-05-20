using System.Windows;

namespace SiemensS7Demo.Wpf.Views;

/// <summary>
/// Top-level Window wrapper around <see cref="LoginView"/> so the WPF host can
/// <see cref="System.Windows.Window.Show"/>/<see cref="System.Windows.Window.Close"/> it via the
/// <c>StartupOrchestrator</c>. Pkg 4's login gate is non-modal: the orchestrator listens for
/// <c>IAuthService.CurrentChanged</c> and swaps to the main Shell on a successful sign-in.
/// </summary>
public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }
}
