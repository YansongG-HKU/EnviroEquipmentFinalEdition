namespace SiemensS7Demo.Models;

public sealed class PlcConnectionOptions
{
    public required string Name { get; init; }
    public required string IpAddress { get; init; }
    public int Port { get; init; } = 102;
    public string Protocol { get; init; } = "s7";
    public short Rack { get; init; } = 0;
    public short Slot { get; init; } = 0;
    public string Snap7ConnectionType { get; init; } = "basic";
    public byte UnitId { get; init; } = 1;
    public required string CpuType { get; init; }
    public int ConnectTimeoutMs { get; init; } = 3000;
    public int ReadTimeoutMs { get; init; } = 2000;
    public int WriteTimeoutMs { get; init; } = 2000;
    public WordOrder WordOrder { get; init; } = WordOrder.ABCD;

    public override string ToString()
        => Protocol.Equals("modbus", System.StringComparison.OrdinalIgnoreCase)
            ? $"{Name} (Modbus TCP) @ {IpAddress}:{Port}, UnitId={UnitId}"
            : $"{Name} ({CpuType}) @ {IpAddress}:{Port}, Rack={Rack}, Slot={Slot}, ConnectionType={Snap7ConnectionType}";
}
