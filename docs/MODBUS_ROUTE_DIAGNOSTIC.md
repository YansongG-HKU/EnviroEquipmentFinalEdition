# Modbus 路由诊断 SOP

更新时间：2026-05-20

本文档面向现场技术人员，给出 Modbus TCP 连接出现 "TCP 可连但协议读取失败" 类问题时的标准化诊断与处置步骤。

## 1. 适用场景

- 真实设备 IP 已知（如 `192.168.2.173:502`、`192.168.105.43:502`）。
- `Test-NetConnection` 显示 `TcpTestSucceeded=True`，但 `--cycles 1 --fail-on-bad-quality` 报错：
  - `Modbus TCP connection closed`
  - `temperatureshockboxdevice-2: 10 tag(s) returned BAD quality`
- 电脑同时安装了 VPN（NordLynx、OpenVPN）、虚拟网卡（LetsTAP、Hyper-V vEthernet、Tailscale、ZeroTier）或多个物理网卡。

典型症状原因：操作系统选择了一张 **虚拟网卡** 来转发到目标 PLC IP，TCP 握手能通过 VPN 对端的 NAT 表，但底层根本没接到现场 PLC，导致 Modbus 协议数据无法应答。

## 2. 推荐排查顺序

按以下顺序执行，任一步发现根因即可止于此步。

### 第 1 步：路由诊断（一行命令）

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\diagnose-modbus-route.ps1 -Ip 192.168.2.173 -Port 502
```

退出码语义：

| 退出码 | 含义 | 处置 |
|---|---|---|
| `0` | 路由健康，可以继续 Modbus 协议层验证 | 进入第 4 步 |
| `1` | RISK：TCP 走了虚拟网卡或没有同网段物理网卡 | 进入第 2 步，按提示处置 |
| `2` | BLOCK：TCP 不通或没有任何活动网卡 | 进入第 3 步 |

脚本输出会自动给出：

- 当前所有 IPv4 活动适配器及分类（`ethernet` / `wifi` / `virtual` / `loopback` / `other`）
- `Find-NetRoute` 选择的源地址和适配器
- `Test-NetConnection` 实际使用的源地址和适配器
- 物理 LAN 候选 vs 虚拟适配器侧对侧对比
- 三条具体补救命令（带实际参数和适配器名）

### 第 2 步：路由被虚拟网卡劫持（RISK，退出码 1）

如果效果适配器（`effective adapter`）是 `LetsTAP`、`NordLynx`、`Hyper-V` 等虚拟设备，按以下顺序处置。

**方案 A：临时禁用虚拟网卡**（最快、最干净）

```powershell
Disable-NetAdapter -Name 'LetsTAP' -Confirm:$false
```

之后重跑诊断脚本，确认 `effective adapter` 变成 Ethernet 或 Wi-Fi。完成现场调试后恢复：

```powershell
Enable-NetAdapter -Name 'LetsTAP'
```

**方案 B：降低物理网卡的接口度量值**（如果必须保留 VPN 在线）

```powershell
Set-NetIPInterface -InterfaceAlias '以太网' -InterfaceMetric 5
Set-NetIPInterface -InterfaceAlias 'LetsTAP' -InterfaceMetric 200
```

操作系统会按度量值由低到高选路，物理网卡度量更低 → 同网段流量优先走物理网卡。

**方案 C：为目标 IP 添加主机级静态路由**（最精确、影响最小）

```powershell
# 把 192.168.2.173 单条强制走物理网卡的网关
route ADD 192.168.2.173 MASK 255.255.255.255 192.168.2.1 IF <物理网卡 ifIndex>
```

- `<物理网卡 ifIndex>` 取自诊断脚本输出（`InterfaceIndex` 列）。
- 如果 PLC 直连无网关，把网关填 `0.0.0.0` 表示 on-link。
- 验证：`Get-NetRoute -DestinationPrefix '192.168.2.173/32'`。
- 撤销：`route DELETE 192.168.2.173`。

任何一种方案处理完，**必须重跑** `diagnose-modbus-route.ps1` 确认 `effective adapter` 变成物理网卡再进入第 4 步。

### 第 3 步：TCP 完全不通（BLOCK，退出码 2）

可能原因：

1. PLC 没上电或网线没插。
2. 现场电脑 IP 不在同网段（例如电脑是 `192.168.100.x`，PLC 是 `192.168.2.x`，没有 Ethernet 直连）。
3. PLC 端固件未启用 Modbus TCP（默认端口 502）或防火墙阻断。
4. 电脑端 Windows 防火墙阻止出站 502。

操作：

```powershell
# 详细 TCP 诊断
Test-NetConnection -ComputerName 192.168.2.173 -Port 502 -InformationLevel Detailed

# 设置 PLC LAN 静态 IP（参考已有脚本）
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\configure-plc-ethernet-ip.ps1
```

如果是案例 2，把现场电脑网卡设到 `192.168.2.x`（避开 PLC IP，常用 `192.168.2.10` / `255.255.255.0`，无网关），然后回到第 1 步重新诊断。

### 第 4 步：路由健康后做协议层验证

路由健康只是 TCP 层 OK，还要确认 Modbus 协议层能应答。优先使用项目里的冒烟配置：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\run-s7-demo.ps1 `
    --project .\src\SiemensS7Demo\Config\legacy_modbus_smoke.project.json `
    --cycles 1 `
    --fail-on-bad-quality `
    --run-log .\artifacts\runtime\modbus-smoke-strict.jsonl
```

期望结果（路由真正修好以后）：

```text
ExitCode=0
good=N (N > 0)
bad=0
```

如果路由健康但仍返回 `Modbus TCP connection closed`：

- 设备 UnitId 不是 1。打开 `legacy_modbus_smoke.project.json` 把 `unitId` 改成实际值（常见 1 / 2 / 255）。
- 地址偏移规则不一致。`HRn` 默认从 0 开始，部分老设备表是从 1 开始；若发现批量 BAD，把第一点的 `address` 减 1 试一次。
- 寄存器类型不一致。线圈 = `Cn`，保持寄存器 = `HRn`。`HRF`（float）/`HRU`（uint16）/`HRS`（int16）由旧 XML 类型决定。
- 字节序 / 字序。32 位类型还要在脚本侧确认大端、小端、混合端。`docs/superpowers/plans/2026-05-14-gap2-modbus-32bit-int.md` 里有完整字节序设计。

## 3. 现场快速参考卡

打印张贴于现场调试机：

```text
1. 先跑：tools\diagnose-modbus-route.ps1 -Ip <ip> -Port 502
2. 退码 0：直接跑 run-s7-demo.ps1 协议层
3. 退码 1：根据脚本输出禁用虚拟网卡 / 改度量 / 加路由
4. 退码 2：检查电源、网线、网段、防火墙
5. 任何处置后必须重跑步骤 1 确认效果
```

## 4. 相关脚本与文档

- `tools/diagnose-modbus-route.ps1` — 本 SOP 的诊断引擎
- `tools/test-plc-network.ps1` — 通用 TCP/网络预检查，保留兼容
- `tools/connect-plc.ps1` — S7 连接握手探针
- `tools/configure-plc-ethernet-ip.ps1` — PLC 网卡静态 IP 设置
- `docs/LEGACY_POINT_IMPORT_AND_RUNTIME_TESTS.md` §4.1 — 当前 Modbus 未通过实测的原始记录
- `docs/OPERATION_CAPABILITY_MATRIX.md` §5 — 不能直接承诺的事项清单
- `docs/superpowers/plans/2026-05-14-gap2-modbus-32bit-int.md` — Modbus 32 位类型支持设计

## 5. 反例：什么时候 SOP 不适用

- 多个 PLC 在同一局域网且地址段冲突，需要 PLC 端 / 交换机端处置，不在本 SOP 范围。
- 现场使用 Modbus RTU 串口或 Modbus over RS485 网关，本 SOP 只覆盖 Modbus TCP。
- 客户内部限制 502 端口，必须由客户运维放行后再走本流程。

## 6. 验收标准

本 SOP 视为生效条件：

- `tools\diagnose-modbus-route.ps1 -Ip 127.0.0.1 -Port <实际监听端口>` 输出 `Severity=INFO`，退出码 0。
- `tools\diagnose-modbus-route.ps1 -Ip 192.168.2.173 -Port 502` 在出现 VPN/TAP 劫持时输出 `Severity=RISK`，退出码 1，并打印三条具体补救命令。
- 现场技术员按方案 A/B/C 任一处置后，再次诊断脚本输出物理网卡作为 `effective adapter`，且 `Modbus TCP connection closed` 不再复现。

上述均为本 dev 机器在 2026-05-20 实测通过。
