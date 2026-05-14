# Snap7 Integration

This project embeds the required Snap7 runtime files as the Siemens S7 native protocol dependency:

```text
https://github.com/davenardella/snap7.git
```

Project-owned layout:

```text
EnviroEquipmentFinalEdition\
  src\SiemensS7Demo\Native\Snap7\
    win64\snap7.dll
    reference\dotnet\snap7.net.cs
    licenses\lgpl-3.0.txt
```

Run this from the project root to verify the bundled dependency:

```powershell
.\tools\ensure-snap7.ps1
```

## What We Reuse

The runtime dependency is the official native DLL:

```text
src\SiemensS7Demo\Native\Snap7\win64\snap7.dll
```

The C# adapter in this repository calls the same exported functions used by the official .NET wrapper:

| Project adapter | Official Snap7 API |
|---|---|
| `Snap7S7Adapter.ConnectAsync` | `Cli_Create`, `Cli_SetParam`, `Cli_SetConnectionType`, `Cli_ConnectTo` |
| `Snap7S7Adapter.ReadRawAsync` | `Cli_DBRead`, `Cli_ReadArea` |
| `Snap7S7Adapter.WriteRawAsync` | `Cli_DBWrite`, `Cli_WriteArea` |
| Error reporting | `Cli_ErrorText` |

We keep a small project adapter for the runtime path and keep the official `snap7.net.cs` under `Native\Snap7\reference\dotnet` as an API reference. The application does not depend on a sibling `snap7` checkout.

## Official Source Mapping

Key official files:

```text
upstream snap7\src\lib\snap7_libmain.cpp              C export layer, including Cli_ConnectTo
upstream snap7\src\core\s7_micro_client.cpp           rack/slot -> TSAP, read/write PDU logic
upstream snap7\src\core\s7_isotcp.cpp                 TCP 102 + COTP/ISO connection handshake
upstream snap7\src\core\s7_types.h                    S7 area and word-length constants
src\SiemensS7Demo\Native\Snap7\reference\dotnet\snap7.net.cs  bundled C# wrapper reference
```

The important constants used locally match Snap7:

```text
S7AreaPE = 0x81  Input
S7AreaPA = 0x82  Output
S7AreaMK = 0x83  Marker
S7AreaDB = 0x84  DB
S7WLByte = 0x02  Byte transfer
```

For S7-200 SMART, local `V` addresses are mapped to Snap7 `DB1` reads:

```text
V0.0 -> DB1.DBX0.0
VW20 -> DB1.DBW20
VD24 -> DB1.DBD24
```

The default S7-200 SMART probe config is:

```text
src\SiemensS7Demo\Config\siemens_s7_200_smart_sample.xml
```

The project-mode sample that uses the same S7 connection is:

```text
src\SiemensS7Demo\Config\project.sample.json
```

Writes are guarded in the runner. A write command is rejected unless the operator passes the `--allow-write` CLI flag and the target tag is configured with `safeWrite=true`. For `ReadWrite` tags, the runner reads back after writing and fails the command if readback is bad.

## Connection-First Flow

Use this first:

```powershell
.\tools\connect-plc.ps1
```

That script checks Windows routing before attempting Snap7. For the connected S7-200 SMART, the verified handshake is:

```text
PLC IP:          192.168.2.180
Connection type: basic
Rack:            0
Slot:            0
```

A TCP 102 success through a VPN/TAP adapter is not enough; Snap7 still needs the ISO/COTP/S7 handshake to complete through the physical PLC network.

For the team-facing execution report, see:

```text
docs\PLC_S7_200_SMART_EXECUTION_REPORT.md
```
