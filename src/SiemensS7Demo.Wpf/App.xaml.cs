using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SiemensS7Demo.App;
using SiemensS7Demo.Wpf.Alarms;
using SiemensS7Demo.Wpf.Smoke;
using SiemensS7Demo.Wpf.Views;
using SiemensS7Demo.Wpf.ViewModels;
using SiemensS7Demo.Wpf.ViewModels.Alarms;

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
                services.AddSingleton<OverviewViewModel>();
                services.AddTransient<SingleDeviceViewModel>();
                services.AddSingleton<CurrentAlarmsViewModel>();
                services.AddSingleton<HistoryAlarmsViewModel>();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<Shell>();
                services.AddTransient<HeadlessSmokeRunner>();
                // M2.5: critical-alarm popup pipeline + Warn/Info toast surface.
                services.AddSingleton<IAlarmPopupGate, WindowAlarmPopupGate>();
                services.AddSingleton<AlarmPopupCoordinator>();
                services.AddSingleton<AlarmToastNotifier>();
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
        // Eagerly resolve the alarm service so its subscription is live BEFORE we start polling.
        // The Current/History VMs are singletons and subscribe in their ctors, so resolving them
        // here keeps the alarm pipeline hot from the first device snapshot.
        _ = _host.Services.GetRequiredService<SiemensS7Demo.App.Alarms.IAlarmService>();
        _ = _host.Services.GetRequiredService<CurrentAlarmsViewModel>();
        _ = _host.Services.GetRequiredService<HistoryAlarmsViewModel>();
        // M2.5: eagerly resolve the popup coordinator + toast notifier so their subscriptions to
        // IAlarmService.Stream are live before the first device snapshot lands.
        _ = _host.Services.GetRequiredService<AlarmPopupCoordinator>();
        _ = _host.Services.GetRequiredService<AlarmToastNotifier>();
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
