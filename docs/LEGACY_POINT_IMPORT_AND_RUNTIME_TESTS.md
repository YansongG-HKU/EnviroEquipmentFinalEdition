# 旧项目点位导入与运行实测记录

更新时间：2026-05-08

本文档对应当前推进项：

2. 真实业务点位表推进。
3. Modbus 真实设备验收推进。
4. 长期运行能力推进。

## 1. 已新增能力

| 能力 | 文件 / 命令 | 状态 |
|---|---|---|
| 旧项目点位导入 | `tools\import-legacy-config.ps1` | 已实现 |
| UInt16 数据类型 | `TagDataType.UInt16` | 已实现并通过 self-test |
| 运行 JSONL 日志 | `--run-log <path>` | 已实现并实测落盘 |
| BAD 点位严格失败 | `--fail-on-bad-quality` | 已实现并实测 |
| 旧 S7 点位冒烟配置 | `src\SiemensS7Demo\Config\legacy_imported_smoke.project.json` | 已生成并实测 |
| 旧完整导入配置 | `src\SiemensS7Demo\Config\legacy_imported_full.project.json` | 已生成并校验 |
| 旧 Modbus 冒烟配置 | `src\SiemensS7Demo\Config\legacy_modbus_smoke.project.json` | 已生成并实测，未通过 |

## 2. 旧点位导入规则

旧项目来源：

```text
H:\qtFileForVscode\EnviroEquipmentFinalEdition_202604\Code\Bin\Debug
```

核心文件：

```text
ProjectData\equipmentConfig.xml
addressProtocol\Siemens\*\addressConfig.xml
addressProtocol\Schneider\*\addressConfig.xml
```

导入规则：

| 旧协议 | 新协议 |
|---|---|
| `Siemens` | `s7` |
| `Schneider` | `modbus` |

| 旧类型 | 新类型 | 地址转换 |
|---|---|---|
| `V` | `Bool` | `DBn.DBXbyte.bit` |
| `HRS(int16)` | `Int16` | `DBn.DBWbyte` / `HRoffset` |
| `HRU(unsigned int16)` | `UInt16` | `DBn.DBWbyte` / `HRoffset` |
| `HRF(float)` | `Real` | `DBn.DBDbyte` / `HRoffset` |
| `Q` | `Bool` | `Coffset` |

安全策略：

- 导入点位全部默认 `access=Read`。
- 导入点位全部默认 `safeWrite=false`。
- 不自动开放真实设备写入。

## 3. 已通过的实测

### 3.1 旧 S7 点位导入和配置校验

命令：

```powershell
.\tools\import-legacy-config.ps1 -S7IpOverride 192.168.2.180 -MaxTagsPerDevice 30 -OutputPath src\SiemensS7Demo\Config\legacy_imported_smoke.project.json
.\tools\run-s7-demo.ps1 --project .\src\SiemensS7Demo\Config\legacy_imported_smoke.project.json --validate-config
```

结果：

```text
Imported devices: 4
Imported tags: 113
Config validation: OK
```

### 3.2 旧 S7 点位真实 PLC 只读冒烟

命令：

```powershell
.\tools\run-s7-demo.ps1 --project .\src\SiemensS7Demo\Config\legacy_imported_smoke.project.json --cycles 1 --fail-on-bad-quality --run-log .\artifacts\runtime\legacy-smoke-strict.jsonl
```

结果：

```text
ExitCode=0
good=30
bad=0
```

说明：当前实测的 30 个旧 Siemens 点位通过真实 S7-200 SMART 读取，全部返回 GOOD。

### 3.3 运行日志落盘

输出文件：

```text
artifacts\runtime\legacy-smoke-strict.jsonl
```

日志包含：

```text
type=snapshot
timestampUtc
deviceId
good
bad
values[]
```

这证明当前 runner 已经不只是控制台输出，也能把每轮结果保存为结构化运行记录。

## 4. 未通过的实测

### 4.1 旧 Schneider / Modbus 真实设备冒烟

旧配置中的 Modbus 目标：

```text
192.168.2.173:502
192.168.105.43:502
```

TCP 检查：

```text
TcpTestSucceeded=True
InterfaceAlias=LetsTAP
SourceAddress=10.197.73.12
```

说明：TCP 502 能连，但不是走当前物理 PLC 网线，而是走 `LetsTAP`。

严格协议读取命令：

```powershell
.\tools\run-s7-demo.ps1 --project .\src\SiemensS7Demo\Config\legacy_modbus_smoke.project.json --cycles 1 --fail-on-bad-quality
```

结果：

```text
ExitCode=1
temperatureshockboxdevice-2: 10 tag(s) returned BAD quality
3: 10 tag(s) returned BAD quality
error=Modbus TCP connection closed
```

结论：当前环境下，旧 Modbus 设备不能算现场协议验收通过。下一步需要确认这些 IP 是否真的是现场 Modbus PLC、是否允许当前电脑通过该路径访问、UnitId 是否为 1、地址是否从 0 开始。

## 5. 当前完成状态

| 推进项 | 当前状态 | 是否算完成 |
|---|---|---|
| 2. 真实业务点位表 | 已从旧项目导入，30 个 S7 点位真实只读通过 | 部分完成 |
| 3. Modbus 真实设备验收 | loopback 通过，旧设备 TCP 可连但协议读取失败 | 未完成 |
| 4. 长期运行能力 | 有限轮询、结构化日志、严格失败判定已实现并实测 | 部分完成 |

## 6. 下一步验收条件

### 业务点位

- 扩大旧 S7 点位读取数量，从 30 个逐步增加到完整表。
- 修正旧 XML 里的中文编码显示问题。
- 对关键业务点位补单位、缩放、含义和预期范围。

### Modbus

- 确认真实 Modbus 设备 IP、端口、UnitId。
- 确认是线圈/离散输入/保持寄存器/输入寄存器。
- 确认地址从 0 开始还是从 1 开始。
- 确认 32 位和 float 的字节序/字序。

### 长期运行

- 增加断线重连和退避策略。
- 增加按设备状态输出健康文件。
- 后续再补 Windows Service 和 SQLite；当前 JSONL 已作为第一阶段运行记录落地。
