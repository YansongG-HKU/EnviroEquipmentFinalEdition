using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SiemensS7Demo.App;
using SiemensS7Demo.Wpf.Smoke;
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

        // Normal launch: wire the live device stream into the screens, show the shell, then start
        // polling. Subscribe BEFORE ConnectAllAsync so the first snapshots are not missed (the
        // stream is a BehaviorSubject, but subscribing first guarantees every device's first poll
        // lands on the UI thread captured here).
        var overview = _host.Services.GetRequiredService<OverviewViewModel>();
        var single = _host.Services.GetRequiredService<SingleDeviceViewModel>();
        var shellVm = _host.Services.GetRequiredService<ShellViewModel>();
        overview.Subscribe();
        single.Subscribe();
        shellVm.BindOverview(overview);
        shellVm.StartClock();
        overview.CardActivated += id => shellVm.OpenDevice(id);

        var shell = _host.Services.GetRequiredService<Shell>();
        shell.DataContext = shellVm;
        shell.Show();
        MainWindow = shell;

        var sessionManager = _host.Services.GetRequiredService<IDeviceSessionManager>();
        await sessionManager.ConnectAllAsync(CancellationToken.None);
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
