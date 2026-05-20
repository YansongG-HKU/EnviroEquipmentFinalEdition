using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiemensS7Demo.App;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using SiemensS7Demo.Wpf.Alarms;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.Alarms;

/// <summary>
/// Composition-root integration: the WPF host registers an
/// <see cref="AlarmPopupCoordinator"/> and an <see cref="IAlarmPopupGate"/>;
/// resolving the coordinator must subscribe to the alarm stream so that a
/// critical event fired anywhere in the host invokes the gate exactly once.
/// </summary>
[Trait("Category", "Pkg2")]
public class AlarmPopupShellWiringTests
{
    [Fact]
    public async Task ResolvingCoordinator_SubscribesGateToCriticalAlarms()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSiemensS7DemoApp();
        var fakeGate = new CountingPopupGate();
        services.AddSingleton<IAlarmPopupGate>(fakeGate);
        services.AddSingleton<AlarmPopupCoordinator>();
        services.AddSingleton<AlarmToastNotifier>();

        await using var provider = services.BuildServiceProvider();
        var sessions = provider.GetRequiredService<IDeviceSessionManager>();
        var alarms = provider.GetRequiredService<IAlarmService>();
        _ = provider.GetRequiredService<AlarmPopupCoordinator>();
        _ = provider.GetRequiredService<AlarmToastNotifier>();

        // Trigger live polling so the default seed (TH-03 over-limit) emits a critical.
        await sessions.ConnectAllAsync(CancellationToken.None);

        // Poll for the popup invocation. The seed produces an immediate critical event;
        // the alarm service publishes synchronously off the device snapshot.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (fakeGate.ShowCount == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        fakeGate.ShowCount.Should().BeGreaterOrEqualTo(1,
            because: "the seeded TH-03 alarm device produces an out-of-range temperature");
        fakeGate.ShownIds.Should().NotBeEmpty();
    }

    private sealed class CountingPopupGate : IAlarmPopupGate
    {
        private readonly object _gateLock = new();
        public int ShowCount { get; private set; }
        public List<string> ShownIds { get; } = new();

        public void Show(AlarmEvent e, Action onDismissed)
        {
            lock (_gateLock)
            {
                ShowCount++;
                ShownIds.Add(e.Id);
            }
            // Immediately dismiss so the coordinator can drain its queue if more events arrive.
            onDismissed();
        }
    }
}
