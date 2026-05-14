# Claude Device Connection Handoff

更新时间：2026-05-13

本文档用于给 Claude 或后续接手机器人说明当前现场设备连接状态。结论基于本机在 `H:\qtFileForVscode\EnviroEquipmentFinalEdition` 下的实际只读测试。

## 结论

当前网线连接到的真实 PLC 目标是：

| 项目 | 当前值 |
|---|---|
| PLC 型号 | S7-200 SMART |
| 已验证 PLC IP | `192.168.2.180` |
| PLC MAC | `4C-E7-05-E8-83-18` |
| PLC 端口 | `102` |
| PC 物理以太网 IP | `192.168.2.10/24` |
| Windows 出口网卡 | `以太网` |
| Snap7 connection-type | `basic` |
| Rack / Slot | `rack=0`, `slot=0` |
| Snap7 握手 | 成功 |
| 只读点位读取 | 成功 |

不要把 `192.168.2.233` 当作当前物理网线上的 PLC 目标继续调试。它目前被 Windows 路由到了 `LetsTAP / 10.197.73.12`，物理以太网侧 ARP 为 `Incomplete`。

## 已执行测试

### 1. 网络预检查

命令：

```powershell
.\tools\test-plc-network.ps1 -Ip 192.168.2.180 -Port 102 -RequireLocalSubnet
```

关键结果：

```text
Ping: True
TcpTestSucceeded: True
RemoteAddress: 192.168.2.180
InterfaceAlias: 以太网
SourceAddress: 192.168.2.10
ARP: 192.168.2.180 -> 4C-E7-05-E8-83-18, Reachable
Network precheck passed.
```

这说明 `192.168.2.180` 是通过物理网卡直连可达，不是 VPN/TAP 虚拟网卡假通。

### 2. Snap7 握手

命令：

```powershell
.\tools\connect-plc.ps1 -Ip 192.168.2.180 -Cpu "S7-200 SMART" -Rack 0 -Slots 0 -ConnectionTypes basic
```

关键结果：

```text
Adapter: snap7
Target: DemoPLC (S7-200 SMART) @ 192.168.2.180:102, Rack=0, Slot=0, ConnectionType=basic
Connected.
Connection handshake succeeded. No tag read/write was attempted.
SUCCESS: Snap7 connected with connectionType=basic rack=0 slot=0.
```

### 3. 设备信息读取

命令：

```powershell
.\tools\run-s7-demo.ps1 --adapter snap7 --ip 192.168.2.180 --cpu "S7-200 SMART" --rack 0 --slot 0 --connection-type basic --device-info
```

关键结果：

```text
Connected.
BlockCounts:
  OB: 1
  DB: 1
  SDB: 2
```

说明：`GetOrderCode`、`GetCpuInfo`、`GetCpInfo`、`GetPlcStatus`、`GetProtection` 返回 `CPU : Item not available (0x00C00000)`。这在当前 S7-200 SMART 上不等于连接失败；Snap7 握手、块数量读取和点位读取均已成功。

### 4. 只读点位读取

命令：

```powershell
.\tools\run-s7-demo.ps1 --adapter snap7 --ip 192.168.2.180 --cpu "S7-200 SMART" --rack 0 --slot 0 --connection-type basic --read-once
```

关键结果：

```text
Config validation: OK
GOOD MBit0 (M区位0.0) address=M0.0 value=False
GOOD VBit0 (V区位0.0) address=V0.0 value=False
GOOD VDInt4 (V区双字4) address=VD4 value=33554433 raw=33554433
GOOD VReal8 (V区浮点8) address=VD8 value=0 raw=0
GOOD VWord0 (V区字0) address=VW0 value=8 raw=8
GOOD VWord2 (V区字2) address=VW2 value=0 raw=0
```

这一步证明当前不是仅仅 TCP 102 端口开放，而是 Snap7 协议和默认 S7-200 SMART 点表都能正常读。

### 5. 本地项目自检

命令：

```powershell
.\tools\run-s7-demo.ps1 --self-test
.\tools\run-s7-demo.ps1 --validate-config
```

关键结果：

```text
SelfTest: OK
Config validation: OK
```

## 192.168.2.233 当前状态

已测试：

```powershell
.\tools\test-plc-network.ps1 -Ip 192.168.2.233 -Port 102 -RequireLocalSubnet
```

关键结果：

```text
Ping: True
TcpTestSucceeded: True
RemoteAddress: 192.168.2.233
InterfaceAlias: LetsTAP
SourceAddress: 10.197.73.12
ARP on physical Ethernet: 192.168.2.233 -> 00-00-00-00-00-00, Incomplete
FAIL: route is not suitable for the default direct-PLC startup path.
```

`Find-NetRoute -RemoteIPAddress 192.168.2.233` 也选择了 `LetsTAP`，而不是物理 `以太网`。因此 `192.168.2.233` 当前不是这根网线直连确认到的设备。

如果后续必须验证 `192.168.2.233`，先处理 Windows 路由或虚拟网卡优先级，直到网络预检查显示：

```text
InterfaceAlias: 以太网
SourceAddress: 192.168.2.10
ARP: 192.168.2.233 -> 真实 MAC, Reachable
```

再执行 Snap7 `--connect-only`，不要直接读写点位。

## 后续给 Claude 的执行建议

1. 继续调试时默认目标使用 `192.168.2.180`，参数使用 `connection-type=basic`、`rack=0`、`slot=0`。
2. 每次现场重新插线或重启 VPN 后，先跑：

```powershell
.\tools\test-plc-network.ps1 -Ip 192.168.2.180 -Port 102 -RequireLocalSubnet
```

3. 如果网络预检查不是 `InterfaceAlias: 以太网` 和 `SourceAddress: 192.168.2.10`，先修路由，不要继续猜 rack/slot。
4. 当前阶段已经验证连接、设备信息、默认只读点位读取；写入尚未现场执行。任何真实写入都必须使用 `--allow-write`，并且目标 tag 必须配置 `safeWrite=true`。
5. 本仓库当前已有大量未提交改动和未跟踪文件。不要 reset、checkout 或清理现有改动；本文件只是新增交接说明。

## 相关文件

| 文件 | 用途 |
|---|---|
| `tools\connect-plc.ps1` | 网络预检查加 Snap7 握手探测 |
| `tools\test-plc-network.ps1` | Ping、TCP、出口网卡、源地址、ARP/邻居诊断 |
| `tools\run-s7-demo.ps1` | 编译并运行通讯 demo，支持无 .NET SDK 的 fallback |
| `src\SiemensS7Demo\Config\siemens_s7_200_smart_sample.xml` | 当前默认只读探测点表 |
| `src\SiemensS7Demo\Config\project.sample.json` | 多设备项目样例 |
| `docs\PLC_S7_200_SMART_EXECUTION_REPORT.md` | 5 月 8 日之前的连接执行报告 |
| `docs\SNAP7_INTEGRATION.md` | Snap7 集成说明 |
