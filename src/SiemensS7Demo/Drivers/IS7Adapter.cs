using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Drivers;

/// <summary>
/// Adapter abstraction used by SiemensS7Client.
/// </summary>
public interface IS7Adapter
{
    Task ConnectAsync(PlcConnectionOptions options, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    bool IsConnected { get; }

    Task<PlcDeviceInfo> GetDeviceInfoAsync(PlcConnectionOptions options, CancellationToken cancellationToken);
    Task<object> ReadRawAsync(TagDefinition tag, CancellationToken cancellationToken);
    Task WriteRawAsync(TagDefinition tag, object value, CancellationToken cancellationToken);

    /// <summary>
    /// Batch read. Default implementation invokes <see cref="ReadRawAsync"/> per tag and
    /// captures per-tag failures so a single bad point does not abort the snapshot.
    /// Adapters with a native batch path (Snap7 windows, Modbus multi-register reads)
    /// should override.
    /// </summary>
    async Task<IReadOnlyDictionary<string, BatchReadResult>> ReadRawBatchAsync(
        IReadOnlyList<TagDefinition> tags,
        CancellationToken cancellationToken)
    {
        var output = new Dictionary<string, BatchReadResult>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            try
            {
                var raw = await ReadRawAsync(tag, cancellationToken);
                output[tag.Name] = BatchReadResult.Ok(raw);
            }
            catch (System.Exception ex)
            {
                output[tag.Name] = BatchReadResult.Bad(ex.Message);
            }
        }
        return output;
    }
}

public readonly record struct BatchReadResult(object? Value, string? Error)
{
    public bool IsGood => Error is null;

    public static BatchReadResult Ok(object value) => new(value, null);
    public static BatchReadResult Bad(string error) => new(null, error);
}
