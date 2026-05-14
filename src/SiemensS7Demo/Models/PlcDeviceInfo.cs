using System;
using System.Collections.Generic;

namespace SiemensS7Demo.Models;

public sealed class PlcDeviceInfo
{
    public required DateTime TimestampUtc { get; init; }
    public required string IpAddress { get; init; }
    public required int Port { get; init; }
    public required short Rack { get; init; }
    public required short Slot { get; init; }
    public required string ConnectionType { get; init; }
    public required string ConfiguredCpuType { get; init; }

    public string? OrderCode { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? ModuleTypeName { get; set; }
    public string? ModuleName { get; set; }
    public string? SerialNumber { get; set; }
    public string? AsName { get; set; }
    public string? Copyright { get; set; }

    public int? MaxPduLength { get; set; }
    public int? MaxConnections { get; set; }
    public int? MaxMpiRate { get; set; }
    public int? MaxBusRate { get; set; }

    public int? PlcStatusRaw { get; set; }
    public string? PlcStatus { get; set; }

    public ushort? ProtectionLevel { get; set; }
    public ushort? ProtectionMode { get; set; }
    public ushort? ProtectionFlags { get; set; }

    public int? ObCount { get; set; }
    public int? FbCount { get; set; }
    public int? FcCount { get; set; }
    public int? DbCount { get; set; }
    public int? SfbCount { get; set; }
    public int? SfcCount { get; set; }
    public int? SdbCount { get; set; }

    public List<string> Warnings { get; } = new();
}
