using System.Collections.Generic;

namespace SiemensS7Demo.Models;

public sealed class DeviceDefinition
{
    public string Id { get; init; } = "device-001";
    public string Name { get; init; } = "Device";
    public string Protocol { get; init; } = "s7";
    public string Ip { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 102;
    public bool Enabled { get; init; } = true;
    public string CpuType { get; init; } = "S7-200 SMART";
    public short Rack { get; init; }
    public short Slot { get; init; }
    public string ConnectionType { get; init; } = "basic";
    public byte UnitId { get; init; } = 1;
    public int PollingIntervalMs { get; init; } = 1000;
    public List<TagDefinition> Tags { get; init; } = new();

    /// <summary>
    /// Auxiliary function metadata loaded from legacy XML 手动辅助功能 / 程序辅助功能 groups.
    /// Transitional home until Gap #9 DeviceTemplate is introduced.
    /// </summary>
    public List<AuxiliaryFunction> Auxiliaries { get; init; } = new();

    public PlcConnectionOptions ToConnectionOptions() => new()
    {
        Name = Name,
        IpAddress = Ip,
        CpuType = CpuType,
        Port = Port,
        Rack = Rack,
        Slot = Slot,
        Snap7ConnectionType = ConnectionType,
        Protocol = Protocol,
        UnitId = UnitId
    };
}
