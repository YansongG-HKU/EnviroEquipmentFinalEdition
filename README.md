# EnviroEquipmentFinalEdition

## Siemens S7 Demo

This project is a runnable device communication demo. It supports three adapters:

- `mock`: local in-memory mode, no PLC hardware required.
- `snap7`: real PLC mode through the native `snap7.dll` library.
- `modbus`: Modbus TCP mode for coils, discrete inputs, holding registers, and input registers.

It also supports a project JSON mode for sequential multi-device connect/read checks.

## Team Documents

项目级文档体系（建议从这里开始，中文）：

```text
docs\README.md                  文档索引
docs\PROJECT_OVERVIEW.md        项目说明（定位/背景/目标/范围）
docs\DEVELOPMENT_STATUS.md      开发情况（已实现/技术栈/测试/怎么跑）
docs\PROJECT_PROGRESS.md        项目进展说明（里程碑/Issue 看板/路线）
docs\TECHNICAL_SOLUTION.md      技术方案（架构/通信/模块/测试/部署）
```

Start here for the upgrade/refactor technical blueprint:

```text
docs\DEVICE_GATEWAY_REFACTOR_PLAN.md
```

Current S7-200 SMART field execution report:

```text
docs\PLC_S7_200_SMART_EXECUTION_REPORT.md
```

Snap7 implementation details:

```text
docs\SNAP7_INTEGRATION.md
```

Operation capability and latest test matrix:

```text
docs\OPERATION_CAPABILITY_MATRIX.md
```

Legacy point import and runtime test records:

```text
docs\LEGACY_POINT_IMPORT_AND_RUNTIME_TESTS.md
```

Current verified field result:

```text
PLC:             S7-200 SMART
PLC IP:          192.168.2.180
PC Ethernet IP:  192.168.2.10/24
Snap7:           connection-type=basic, rack=0, slot=0
Startup script:  tools\connect-plc.bat
```

The sample tag file is:

```text
src\SiemensS7Demo\Config\siemens_s7_200_smart_sample.xml
```

The current multi-device project sample is:

```text
src\SiemensS7Demo\Config\project.sample.json
```

The write template is intentionally locked by default:

```text
src\SiemensS7Demo\Config\siemens_s7_200_smart_write_template.xml
```

The mock-only write sample for safe CLI write/readback tests is:

```text
src\SiemensS7Demo\Config\mock_write_sample.xml
```

## Prerequisites

- Recommended: .NET 8 SDK for normal `dotnet run`.
- This checkout also includes `tools\run-s7-demo.ps1`, which can build and run with the installed .NET 8 runtime plus Visual Studio Build Tools when the SDK is missing.
- For real PLC mode, the Snap7 native DLL is bundled inside this project:

```text
src\SiemensS7Demo\Native\Snap7\win64\snap7.dll
```

The bundled files are sourced from the official Snap7 repository:

```text
https://github.com/davenardella/snap7.git
```

Run this to verify the project-bundled dependency:

```powershell
.\tools\ensure-snap7.ps1
```

The project file copies that DLL into the build output as `snap7.dll`. The runtime also supports `SNAP7_DLL` as an explicit override for diagnostics, but normal project use does not depend on any external `snap7` folder.

## Run On This Machine

This machine currently has the .NET 8 runtime but not the .NET SDK. Use the bundled script:

```powershell
.\tools\run-s7-demo.ps1 --once
```

Double-click helpers are available:

```text
tools\ensure-snap7.bat    Verify the Snap7 files bundled in this project.
tools\check-prereqs.bat   Check runtime, compiler fallback, Snap7 DLL, and XML encoding.
tools\start-mock.bat      Start one mock read without PLC hardware.
tools\connect-plc.bat     Check network route, then try the S7-200 SMART Snap7 handshake.
tools\device-info.bat     Read PLC device information through Snap7.
tools\read-once.bat       Read configured S7-200 SMART V/M probe tags once.
```

Real PLC one-shot read for the connected S7-200 SMART:

```powershell
.\tools\run-s7-demo.ps1 --adapter snap7 --ip 192.168.2.180 --cpu "S7-200 SMART" --rack 0 --slot 0 --connection-type basic --once
```

Current implemented and pending capabilities:

```powershell
.\tools\run-s7-demo.ps1 --capabilities
```

Run safe local self-tests, including Modbus loopback read/write:

```powershell
.\tools\run-s7-demo.ps1 --self-test
```

Validate the default S7 XML:

```powershell
.\tools\run-s7-demo.ps1 --validate-config
```

Run two finite real-PLC polling cycles:

```powershell
.\tools\run-s7-demo.ps1 --adapter snap7 --ip 192.168.2.180 --cpu "S7-200 SMART" --rack 0 --slot 0 --connection-type basic --cycles 2 --interval 1
```

Core connection only:

```powershell
.\tools\run-s7-demo.ps1 --adapter snap7 --ip 192.168.2.180 --cpu "S7-200 SMART" --rack 0 --slot 0 --connection-type basic --connect-only
```

Read PLC device information:

```powershell
.\tools\run-s7-demo.ps1 --adapter snap7 --device-info
```

Read configured probe tags once:

```powershell
.\tools\run-s7-demo.ps1 --adapter snap7 --read-once
```

Run the project JSON sample:

```powershell
.\tools\run-s7-demo.ps1 --project .\src\SiemensS7Demo\Config\project.sample.json --read-once
```

Run two finite project polling cycles:

```powershell
.\tools\run-s7-demo.ps1 --project .\src\SiemensS7Demo\Config\project.sample.json --cycles 2
```

Import legacy project point tables, then run a strict read-only S7 smoke test:

```powershell
.\tools\import-legacy-config.ps1 -S7IpOverride 192.168.2.180 -MaxTagsPerDevice 30 -OutputPath src\SiemensS7Demo\Config\legacy_imported_smoke.project.json
.\tools\run-s7-demo.ps1 --project .\src\SiemensS7Demo\Config\legacy_imported_smoke.project.json --cycles 1 --fail-on-bad-quality --run-log .\artifacts\runtime\legacy-smoke-strict.jsonl
```

Use `--fail-on-bad-quality` when an automated check should fail if any point returns BAD.

Network check plus automatic Snap7 probe:

```powershell
.\tools\connect-plc.ps1
```

By default, `connect-plc.ps1` targets the detected S7-200 SMART at `192.168.2.180` and tries `connection-type=basic, rack=0, slot=0` first. It stops before the Snap7 handshake if Windows routes the PLC address through a VPN/TAP adapter. If that route is intentional, override it explicitly:

```powershell
.\tools\connect-plc.ps1 -Ip 192.168.2.180 -AllowVirtualRoute
```

Real PLC continuous polling:

```powershell
.\tools\run-s7-demo.ps1 --adapter snap7 --ip 192.168.2.180 --cpu "S7-200 SMART" --rack 0 --slot 0 --connection-type basic --interval 1
```

Before connecting to hardware, check the network:

```powershell
.\tools\test-plc-network.ps1 -Ip 192.168.2.180 -Port 102
```

If real PLC mode fails with `ISO : An error occurred during recv TCP : Connection timed out`, first check the route printed by `test-plc-network.ps1`.

For a directly connected PLC at `192.168.2.180`, the source address should usually be another `192.168.2.x` address on the physical Ethernet adapter. If the script shows a VPN/TAP adapter such as `LetsTAP` or a source like `10.x.x.x`, Windows is routing the PLC traffic through a virtual adapter. In that case:

1. Connect the Ethernet cable to the PLC network.
2. Configure the Ethernet adapter with a static address such as `192.168.2.10/24`.
3. Temporarily disable or lower priority for VPN/TAP routes while testing.
4. Run `.\tools\test-plc-network.ps1 -Ip 192.168.2.180 -Port 102` again before running Snap7 mode.

## Run Without PLC

From this repository root:

```powershell
dotnet run --project src\SiemensS7Demo\SiemensS7Demo.csproj -- --once
```

Continuous mock polling:

```powershell
dotnet run --project src\SiemensS7Demo\SiemensS7Demo.csproj -- --interval 1
```

## Run Against A Real PLC

Use the real Snap7 adapter and pass the PLC address:

```powershell
dotnet run --project src\SiemensS7Demo\SiemensS7Demo.csproj -- --adapter snap7 --ip 192.168.2.180 --cpu "S7-200 SMART" --rack 0 --slot 0 --connection-type basic --connect-only
```

For continuous polling:

```powershell
dotnet run --project src\SiemensS7Demo\SiemensS7Demo.csproj -- --adapter snap7 --ip 192.168.2.180 --cpu "S7-200 SMART" --rack 0 --slot 0 --connection-type basic --interval 1
```

Write a configured tag only after the point is confirmed safe:

```powershell
dotnet run --project src\SiemensS7Demo\SiemensS7Demo.csproj -- --adapter snap7 --ip 192.168.2.180 --cpu "S7-200 SMART" --rack 0 --slot 0 --connection-type basic --config src\SiemensS7Demo\Config\your_confirmed_write_config.xml --allow-write --write ConfirmedSafeTag=true --once
```

Writes are blocked unless both conditions are true:

- The command includes `--allow-write`.
- The tag config sets `safeWrite="true"` and, for numeric values, any configured `min` / `max` bounds are satisfied.

When a tag is `ReadWrite`, the runner reads it back after writing and fails the command if readback is bad.

Safe mock write/readback test:

```powershell
.\tools\run-s7-demo.ps1 --mock --config .\src\SiemensS7Demo\Config\mock_write_sample.xml --allow-write --write MockWord=123 --read-once
```

Common rack/slot values:

```text
S7-200 SMART:      connection-type=basic, rack=0, slot=0
S7-1200 / S7-1500: rack=0, slot=1
S7-300:            rack=0, slot=2
```

## Supported Address Forms

The Snap7 adapter currently supports:

```text
V0.0             Bool S7-200 SMART V memory bit, mapped through DB1
VW20             Int16 S7-200 SMART V memory word, mapped through DB1
VD24             DInt / Real S7-200 SMART V memory double word, mapped through DB1
DB100.DBX100.0   Bool
DB100.DBW10      Int16
DB100.DBD10      DInt / Real
M10.0            Bool marker
MW20             Int16 marker
MD24             DInt / Real marker
I0.0 / E0.0      Bool input
Q0.0 / A0.0      Bool output
```

The Modbus TCP adapter currently supports:

```text
C0 / COIL0       Bool coil
DI0              Bool discrete input
HR0              Holding register
IR0              Input register
```

## Notes For S7-200 SMART / S7-1200 / S7-1500

For real hardware validation, make sure:

- The PC can reach the PLC on TCP port `102`.
- PUT/GET access is enabled in the PLC settings when required.
- DB tags used by this demo are accessible in a non-optimized layout, or mapped to a compatible communication DB.
- The XML addresses match the actual PLC DB layout.
