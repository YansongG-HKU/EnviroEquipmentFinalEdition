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
}
