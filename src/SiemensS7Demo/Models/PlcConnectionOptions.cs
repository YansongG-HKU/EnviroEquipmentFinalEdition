namespace SiemensS7Demo.Models;

public sealed class PlcConnectionOptions
{
    public required string Name { get; init; }
    public required string IpAddress { get; init; }
    public int Port { get; init; } = 102;
    public short Rack { get; init; } = 0;
    public short Slot { get; init; } = 0;
    public required string CpuType { get; init; }
    public int ConnectTimeoutMs { get; init; } = 3000;
    public int ReadTimeoutMs { get; init; } = 2000;
    public int WriteTimeoutMs { get; init; } = 2000;

    public override string ToString()
        => $"{Name} ({CpuType}) @ {IpAddress}:{Port}, Rack={Rack}, Slot={Slot}";
}
