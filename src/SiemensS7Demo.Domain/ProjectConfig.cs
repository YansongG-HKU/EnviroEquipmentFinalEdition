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
    bool UseInMemoryAdapter,
    // Optional demo-seed (Pkg 1 only). When null the device behaves as before: idle->run with
    // flat InMemory readings. When set, the InMemory session reports this status and simulates
    // readings around the seed setpoints so the overview mirrors the locked 202605 design.
    DeviceSeed? Seed = null);

/// <summary>
/// Demo seed for an InMemory device — mirrors a row of INITIAL_DEVICES in mock-data.jsx.
/// Drives the simulated status, PV/SV/humidity and program/progress shown on the overview.
/// Pkg 1 demo data only; real devices ignore this.
/// </summary>
public sealed record DeviceSeed(
    DeviceStatus Status,
    double? Temp = null,
    double? TempSet = null,
    double? Humid = null,
    double? HumidSet = null,
    string? ProgName = null,
    int Seg = 0,
    int SegTotal = 0,
    int Cycle = 0,
    int CycleTotal = 0,
    int RemainSec = 0,
    double Progress = 0,
    string? AlarmCode = null,
    string? AlarmMessage = null,
    string? Note = null);

public sealed record ProjectConfig(IReadOnlyList<DeviceProvisioning> Devices);
