using System;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Wpf.ViewModels;

namespace SiemensS7Demo.Wpf.Smoke;

public sealed class HeadlessSmokeRunner
{
    public IDeviceSessionManager? SessionManager { get; set; }
    public OverviewViewModel? Overview { get; set; }
    public SingleDeviceViewModel? Single { get; set; }

    public async Task<int> RunAsync()
    {
        if (SessionManager is null || Overview is null || Single is null)
        {
            return 2;
        }

        Overview.Subscribe();
        Single.Subscribe();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await SessionManager.ConnectAllAsync(cts.Token);

        // Poll until at least 3 cards arrive (config seeds 3 InMemory devices).
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && Overview.Cards.Count < 3)
        {
            await Task.Delay(100, cts.Token);
        }
        if (Overview.Cards.Count < 3)
        {
            return 3;
        }

        // "Click" the first card.
        var firstId = Overview.Cards[0].Id;
        Single.Select(firstId);

        // Write a fresh SV through the UI command path.
        Single.NewSvInput = 77.5;
        await Single.WriteSetpointCommand.ExecuteAsync(null);
        if (!Single.LastWriteOk)
        {
            return 4;
        }

        // Wait for one more poll so the readback reflects the new SV.
        await Task.Delay(1500, cts.Token);

        return 0;
    }
}
