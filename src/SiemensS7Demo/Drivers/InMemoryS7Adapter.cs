using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Drivers;

/// <summary>
/// Local adapter for phase-1 development & CI without PLC hardware.
/// </summary>
public sealed class InMemoryS7Adapter : IS7Adapter
{
    private readonly ConcurrentDictionary<string, object> _memory = new();

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(PlcConnectionOptions options, CancellationToken cancellationToken)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<PlcDeviceInfo> GetDeviceInfoAsync(PlcConnectionOptions options, CancellationToken cancellationToken)
    {
        var info = new PlcDeviceInfo
        {
            TimestampUtc = System.DateTime.UtcNow,
            IpAddress = options.IpAddress,
            Port = options.Port,
            Rack = options.Rack,
            Slot = options.Slot,
            ConnectionType = options.Snap7ConnectionType,
            ConfiguredCpuType = options.CpuType,
            ModuleTypeName = "Mock PLC",
            ModuleName = options.Name,
            PlcStatusRaw = 0x08,
            PlcStatus = "RUN"
        };

        info.Warnings.Add("mock adapter: values are simulated.");
        return Task.FromResult(info);
    }

    public Task<object> ReadRawAsync(TagDefinition tag, CancellationToken cancellationToken)
    {
        _memory.TryGetValue(tag.Address, out var value);

        value ??= tag.DataType switch
        {
            TagDataType.Bool => false,
            TagDataType.Int16 => (short)0,
            TagDataType.UInt16 => (ushort)0,
            TagDataType.DInt => 0,
            TagDataType.Real => 0.0f,
            _ => 0
        };

        return Task.FromResult(value);
    }

    public Task WriteRawAsync(TagDefinition tag, object value, CancellationToken cancellationToken)
    {
        _memory[tag.Address] = value;
        return Task.CompletedTask;
    }
}
