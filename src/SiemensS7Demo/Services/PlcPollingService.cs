using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Services;

public sealed class PlcPollingService
{
    private readonly IPlcClient _plcClient;

    public PlcPollingService(IPlcClient plcClient)
    {
        _plcClient = plcClient;
    }

    public async Task RunAsync(
        IReadOnlyList<TagDefinition> readTags,
        TimeSpan interval,
        Action<IReadOnlyDictionary<string, TagValue>> onSnapshot,
        CancellationToken cancellationToken)
    {
        if (interval < TimeSpan.FromSeconds(1) || interval > TimeSpan.FromSeconds(120))
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be between 1s and 120s.");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var values = await _plcClient.ReadTagsAsync(readTags, cancellationToken);
                onSnapshot(values);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:O}] PollingError: {ex.Message}");
            }

            await Task.Delay(interval, cancellationToken);
        }
    }
}
