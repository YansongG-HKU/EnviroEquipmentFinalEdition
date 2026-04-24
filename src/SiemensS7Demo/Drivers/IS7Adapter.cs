using SiemensS7Demo.Models;

namespace SiemensS7Demo.Drivers;

/// <summary>
/// Adapter abstraction used by SiemensS7Client.
/// In production, implement this with S7NetPlus/Sharp7.
/// </summary>
public interface IS7Adapter
{
    Task ConnectAsync(PlcConnectionOptions options, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    bool IsConnected { get; }

    Task<object> ReadRawAsync(TagDefinition tag, CancellationToken cancellationToken);
    Task WriteRawAsync(TagDefinition tag, object value, CancellationToken cancellationToken);
}
