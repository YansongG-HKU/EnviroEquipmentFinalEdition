using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SiemensS7Demo.App;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Wpf.Smoke;
using SiemensS7Demo.Wpf.Startup;
using SiemensS7Demo.Wpf.Views;
using SiemensS7Demo.Wpf.ViewModels;

namespace SiemensS7Demo.Wpf;

public partial class App : Application
{
    private IHost? _host;

    public IServiceProvider Services => _host?.Services
        ?? throw new InvalidOperationException("Host has not been built yet.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File("logs/wpf-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseSerilog()
            .ConfigureServices((ctx, services) =>
            {
                services.AddSiemensS7DemoApp();
                services.AddPkg4Auth(ctx.Configuration);
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<OverviewViewModel>();
                services.AddTransient<SingleDeviceViewModel>();
                services.AddSingleton<Shell>();
                services.AddSingleton<LoginViewModel>();
                services.AddSingleton<LoginWindow>();
                services.AddTransient<HeadlessSmokeRunner>();
            })
            .Build();

        await _host.StartAsync();

        if (TryGetHeadlessSwitch(e.Args))
        {
            var runner = _host.Services.GetRequiredService<HeadlessSmokeRunner>();
            runner.SessionManager = _host.Services.GetRequiredService<IDeviceSessionManager>();
            runner.Overview = _host.Services.GetRequiredService<OverviewViewModel>();
            runner.Single = _host.Services.GetRequiredService<SingleDeviceViewModel>();
            var exitCode = await runner.RunAsync();
            Shutdown(exitCode);
            return;
        }

        // Normal launch. Wire the shell up-front (data context + RBAC + observable subscriptions)
        // so the moment the StartupOrchestrator decides to Show() it, every binding is live. The
        // shell window itself is NOT shown here — the orchestrator displays LoginView first and
        // only swaps in the shell after a successful sign-in. Before this gate landed, the shell
        // popped immediately and the 3-step login flow was unreachable.
        var overview = _host.Services.GetRequiredService<OverviewViewModel>();
        var single = _host.Services.GetRequiredService<SingleDeviceViewModel>();
        var shellVm = _host.Services.GetRequiredService<ShellViewModel>();
        overview.Subscribe();
        single.Subscribe();
        shellVm.BindOverview(overview);
        shellVm.StartClock();
        var auth = _host.Services.GetRequiredService<IAuthService>();
        var rbac = _host.Services.GetRequiredService<IRbacContext>();
        shellVm.WireRbac(auth, rbac);
        overview.CardActivated += id => shellVm.OpenDevice(id);

        var shell = _host.Services.GetRequiredService<Shell>();
        shell.DataContext = shellVm;
        var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
        loginWindow.DataContext = _host.Services.GetRequiredService<LoginViewModel>();

        // ShutdownMode is left at OnLastWindowClose by default; we set MainWindow explicitly so
        // closing either surface during the sign-in/sign-out swap doesn't terminate the app.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var host = new WindowShellHost(this, loginWindow, shell);
        var orchestrator = new StartupOrchestrator(auth, host);
        orchestrator.Begin();

        var sessionManager = _host.Services.GetRequiredService<IDeviceSessionManager>();
        await sessionManager.ConnectAllAsync(CancellationToken.None);
    }

    /// <summary>
    /// WPF-bound implementation of <see cref="IShellHost"/>. Lives in this file because it
    /// directly drives <see cref="Window.Show"/>/<see cref="Window.Close"/> on the two pre-built
    /// window instances. Tests target the orchestrator with a fake host.
    /// </summary>
    private sealed class WindowShellHost : IShellHost
    {
        private readonly App _app;
        private readonly LoginWindow _login;
        private readonly Shell _shell;

        public WindowShellHost(App app, LoginWindow login, Shell shell)
        {
            _app = app;
            _login = login;
            _shell = shell;
        }

        public void ShowLogin()
        {
            _app.MainWindow = _login;
            _login.Show();
        }

        public void CloseLogin() => _login.Hide();

        public void ShowShell()
        {
            _app.MainWindow = _shell;
            _shell.Show();
        }

        public void CloseShell() => _shell.Hide();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static bool TryGetHeadlessSwitch(string[] args)
    {
        foreach (var a in args)
        {
            if (string.Equals(a, "--headless-smoke", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
