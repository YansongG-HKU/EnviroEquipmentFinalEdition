using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Wpf.Smoke;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.E2ETests.Pkg1;

[Trait("Category", "Pkg1")]
public class SmokeTests
{
    [Fact]
    public async Task ThreeDevices_AppearInOverviewAndWriteSvSurvivesReadback()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSiemensS7DemoApp();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<SingleDeviceViewModel>();
        services.AddSingleton(sp => new ShellViewModel(
            sp.GetRequiredService<OverviewViewModel>(),
            sp.GetRequiredService<SingleDeviceViewModel>()));
        services.AddTransient<HeadlessSmokeRunner>();

        await using var sp = services.BuildServiceProvider();

        var runner = sp.GetRequiredService<HeadlessSmokeRunner>();
        runner.SessionManager = sp.GetRequiredService<IDeviceSessionManager>();
        runner.Overview = sp.GetRequiredService<OverviewViewModel>();
        runner.Single = sp.GetRequiredService<SingleDeviceViewModel>();

        var exit = await runner.RunAsync();
        exit.Should().Be(0);
        runner.Overview!.Cards.Count.Should().BeGreaterThanOrEqualTo(3);
        runner.Single!.LastWriteOk.Should().BeTrue();
        runner.Single.Sv.Should().Be(77.5);
    }
}
