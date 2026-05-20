using System.Collections.Generic;

namespace SiemensS7Demo.Domain;

public sealed record DeviceProvisioning(
    string Id,
    string Bay,
    DeviceType Type,
    string IpAddress,
    int Port,
    string CpuType,
    short Rack,
    short Slot,
    string PvTagName,
    string SvTagName,
    string PvAddress,
    string SvAddress,
    bool UseInMemoryAdapter);

public sealed record ProjectConfig(IReadOnlyList<DeviceProvisioning> Devices);
