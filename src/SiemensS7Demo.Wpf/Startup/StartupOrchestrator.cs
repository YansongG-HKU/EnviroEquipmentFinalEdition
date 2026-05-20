using System;
using SiemensS7Demo.App.Auth;

namespace SiemensS7Demo.Wpf.Startup;

/// <summary>
/// Abstracts the WPF-coupled "show / close window" verbs so <see cref="StartupOrchestrator"/>
/// can be unit-tested without spinning up a Dispatcher. The live implementation lives in
/// <c>App.xaml.cs</c> and delegates to <see cref="System.Windows.Window.Show"/> /
/// <see cref="System.Windows.Window.Close"/>.
/// </summary>
public interface IShellHost
{
    void ShowLogin();
    void CloseLogin();
    void ShowShell();
    void CloseShell();
}

/// <summary>
/// Drives the login-gate state machine: at startup, show the LoginView; on a successful sign-in
/// (observed via <see cref="IAuthService.CurrentChanged"/>), close login and reveal the Shell; on
/// sign-out, swap back. This sits between <see cref="App"/> and the window classes so the
/// transition logic is unit-testable.
///
/// <para>Before this fix landed, <see cref="App.OnStartup"/> resolved and showed <c>Shell</c>
/// directly with no sign-in gate; the 3-step login flow was unreachable in the running app.</para>
/// </summary>
public sealed class StartupOrchestrator
{
    private readonly IAuthService _auth;
    private readonly IShellHost _host;
    private bool _started;
    private bool _shellShown;

    public StartupOrchestrator(IAuthService auth, IShellHost host)
    {
        _auth = auth;
        _host = host;
    }

    /// <summary>
    /// Mount the login surface and arm the auth-state listener. Idempotent — subsequent calls
    /// are no-ops so duplicate Application.OnStartup invocations don't double-render.
    /// </summary>
    public void Begin()
    {
        if (_started) return;
        _started = true;
        _auth.CurrentChanged += OnAuthChanged;
        _host.ShowLogin();
    }

    private void OnAuthChanged(object? sender, EventArgs e)
    {
        if (_auth.Current is not null && !_shellShown)
        {
            _host.CloseLogin();
            _host.ShowShell();
            _shellShown = true;
        }
        else if (_auth.Current is null && _shellShown)
        {
            _host.CloseShell();
            _shellShown = false;
            _host.ShowLogin();
        }
    }
}
