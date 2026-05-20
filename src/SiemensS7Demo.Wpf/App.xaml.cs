using System;
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
            var exitCode = await runner.RunAsync();
            Shutdown(exitCode);
            return;
        }

        var shell = _host.Services.GetRequiredService<Shell>();
        shell.DataContext = _host.Services.GetRequiredService<ShellViewModel>();
        shell.Show();
        MainWindow = shell;
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
