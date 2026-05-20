# 现场 15 分钟 PLC 演示 SOP

更新时间：2026-05-20

本文档是销售或工程师到客户现场，用客户自己的 PLC 在 15 分钟内完成"网线接上、连得上、点位读得出、日志落得下"完整演示的固定流程。

## 0. 适用范围与边界

适用：

- 客户现场有一台 Siemens S7 系列 PLC（最稳定的是 S7-200 SMART）。
- 现场可以让我们直接插网线或临时接入一台带 PLC 的交换机。
- 客户希望先看一次"能不能连、能不能读"，再决定是否启动正式项目。

不在本次演示范围（要主动说清楚，避免误判）：

- 不做真实 PLC 写入。
- 不部署 Windows Service、不接 SQLite、不接客户 MES / SCADA。
- 不承诺连续 24 小时运行；只跑有限轮询展示数据落盘形式。
- 不演示完整 UI；只展示命令行运行结果和 JSONL 日志结构。
- Modbus 真实设备在演示场景下不作为承诺项，只在客户主动要求时作为下一阶段验证议题提出。

## 1. 前置准备（3 分钟）

### 1.1 物理网卡 IP 配置

到现场后第一件事是把演示电脑的网卡 IP 与 PLC 放在同一网段。

| 默认假设 | 取值 |
|---|---|
| PLC IP | `192.168.2.180`（先和客户确认现场实际 IP） |
| 演示电脑物理网卡 IP | `192.168.2.10/24` |
| PLC 通信端口 | `102` |

操作步骤：

1. 网线插入演示电脑的物理以太网口，另一端进 PLC 所在网络。
2. 用脚本把以太网卡固定为静态 IP（需管理员权限）：

```powershell
.\tools\configure-plc-ethernet-ip.ps1
```

3. 关掉所有 VPN、TAP、NordLynx、LetsTAP 等虚拟网卡，或至少把它们的路由优先级调低。

### 1.2 路由检查

确认 PLC 流量真的走物理网卡，不是被虚拟网卡抢走：

```powershell
.\tools\check-prereqs.ps1
```

期望看到：

- `dotnet` 命令存在或 `csc` fallback 可用。
- `Snap7 DLL` 已就位（`src\SiemensS7Demo\Native\Snap7\win64\snap7.dll`）。
- 默认 XML 编码无问题。

失败处置：

- `dotnet command - not found` 且 `csc fallback` 也未命中：换一台预装好环境的演示机，不要在客户面前现装。
- `Snap7 DLL` 缺失：当场停止演示，按"无依赖"问题处理，回去补 DLL 后再约。

## 2. 网络诊断（2 分钟）

```powershell
.\tools\test-plc-network.ps1 -Ip 192.168.2.180 -Port 102
```

期望输出（关键字段）：

```text
Ping: True
TcpTestSucceeded: True
RemoteAddress: 192.168.2.180
InterfaceAlias: 以太网
SourceAddress: 192.168.2.10
Network precheck passed.
```

向客户解读：

- `TcpTestSucceeded=True` 说明 TCP 102 通。
- `InterfaceAlias=以太网` 说明流量走物理网线，不是 VPN。
- `SourceAddress=192.168.2.10` 说明用的是我们刚配的本地静态 IP。

失败处置（按故障层级区分）：

| 现象 | 故障层 | 现场动作 |
|---|---|---|
| `Ping: False` 且 `TcpTestSucceeded: False` | route / 物理层 | 检查网线、交换机指示灯、PLC 上电与以太网模块。 |
| `TcpTestSucceeded: True` 但 `InterfaceAlias` 是 `LetsTAP`/`Nord`/`TAP` | route | 关闭 VPN/TAP，重跑脚本；不要继续进入 Snap7 步骤。 |
| `TcpTestSucceeded: True` 但 `SourceAddress` 不是 `192.168.2.x` | route | 重新执行 `configure-plc-ethernet-ip.ps1`，确认网卡静态 IP 生效。 |
| `Ping: True` 但 `TcpTestSucceeded: False` | tcp | 端口 102 被防火墙拦截或 PLC 通信参数未开放，向客户确认 PLC 是否启用了对外通信。 |

不能继续往下走的红线：路由还走在虚拟网卡上时，TCP 看似通，Snap7 大概率握手超时；必须先把路由问题解决。

## 3. 协议握手（2 分钟）

```powershell
.\tools\connect-plc.ps1
```

如果客户的 PLC IP 不是 `192.168.2.180`，显式覆盖：

```powershell
.\tools\connect-plc.ps1 -Ip <现场 IP>
```

脚本会先复用网络诊断结果，然后按 `basic / op / pg` 三种连接类型和 `slot=0/1/2` 组合依次尝试 Snap7 握手。

期望输出（最简版）：

```text
Network precheck passed.
Trying connectionType=basic rack=0 slot=0 ...
Connected.
Connection handshake succeeded. No tag read/write was attempted.
SUCCESS: Snap7 connected with connectionType=basic rack=0 slot=0.
```

向客户解读：

- `Connected.` 说明 Snap7 ISO-on-TCP 协议握手通过。
- 此时还没有读任何点位，纯握手成功就是一个独立里程碑。
- 哪种 `connection-type` 和 `slot` 成功要现场记录下来，正式项目沿用。

失败处置：

| 现象 | 故障层 | 现场动作 |
|---|---|---|
| 全部连接类型都 `ISO : An error occurred during recv TCP : Connection timed out` | protocol | 90% 是路由问题没解决，回到第 2 步重检；若路由确实是物理网卡仍超时，确认 PLC 是否启用 PUT/GET 通信。 |
| 报错提到 `connection refused` | protocol / 端口 | PLC 通信通道被另一会话占用，或 PLC 端口策略阻断；让客户关闭 STEP 7 / SMART 编程软件后重试。 |
| `Snap7 DLL` 加载失败 | 本机依赖 | 回到 `check-prereqs`；现场不要尝试重新拷贝 DLL，约下次。 |

## 4. 设备信息读取（2 分钟）

```powershell
.\tools\device-info.bat
```

或等价 PowerShell：

```powershell
.\tools\run-s7-demo.ps1 --adapter snap7 --device-info
```

期望输出（S7-200 SMART 典型）：

```text
BlockCounts: OB=1, FB=0, FC=0, DB=1, SFB=0, SFC=0, SDB=2
Unsupported: GetOrderCode, GetCpuInfo, GetCpInfo, GetPlcStatus, GetProtection
```

向客户解读（重要 talking point）：

- 块数量信息可读，证明已经能从 PLC 拉出真实结构化数据。
- `Unsupported` 不是错误：S7-200 SMART 这台 CPU 本身不响应这些标准接口，但这不影响连接和 V/M 区读取。其他型号（S7-1200/1500）这部分会显示更多。
- 这一步是把"连得上"和"读得出"之间的过渡说清楚。

失败处置：

- 整体命令失败而前一步握手成功：通常是 DLL 路径问题或单次 socket 异常；重跑一次。若依然失败，按"DLL/运行时"故障处理，记录后续排查。

## 5. 默认点位读取（3 分钟）

用项目自带的 S7-200 SMART 探测点位先做一次只读：

```powershell
.\tools\read-once.bat
```

或：

```powershell
.\tools\run-s7-demo.ps1 --adapter snap7 --read-once
```

读取的是 `src\SiemensS7Demo\Config\siemens_s7_200_smart_sample.xml` 中的探测点。

期望输出（每一行都应 `GOOD`）：

```text
M0.0  GOOD False
V0.0  GOOD False
VW0   GOOD <数值>
VW2   GOOD <数值>
VD4   GOOD <数值>
VD8   GOOD <数值>
```

向客户解读：

- `GOOD` 表示读取质量良好，不只是网络通，而是真的拿到了 V/M 区的当前值。
- 默认点位是 V 区和 M 区的探测点，不是客户业务点。正式项目第一步就是把这份点位表替换成客户的真实点位（这部分在试点验收清单里展开）。
- 这里读到的数值可能不稳定（探测点会随 PLC 运行变化），那是正常的，重点是 `GOOD`。

失败处置：

| 现象 | 故障层 | 现场动作 |
|---|---|---|
| 单点 `BAD`，多数 `GOOD` | tag | 该点位地址不存在或 DB 没开放，记录后跳过，不影响演示。 |
| 全部 `BAD` | tag / 协议 | 该型号 PLC 可能不支持默认探测点的地址形式（例如 S7-1500 优化 DB），换标准 XML 或直接进入"业务点位讨论"。 |
| 报 `connect failed` | 回到第 3 步重检握手，不要在这一步死磕。 |

## 6. 有限轮询 + 日志落盘（3 分钟）

```powershell
.\tools\run-s7-demo.ps1 --adapter snap7 --cycles 2 --interval 1 --run-log .\artifacts\runtime\site-demo.jsonl
```

期望：

- 终端显示 2 轮读取，每轮显示当前点位值和 `GOOD/BAD` 数。
- 命令结束后退出码 `0`。
- 文件 `.\artifacts\runtime\site-demo.jsonl` 已生成。

日志文件结构示例（每行一个 JSON 对象）：

```text
{"type":"snapshot","timestampUtc":"...","deviceId":"...","good":6,"bad":0,"values":[{"name":"V0.0","quality":"Good","value":false}, ...]}
{"type":"snapshot","timestampUtc":"...","deviceId":"...","good":6,"bad":0,"values":[...]}
```

向客户解读：

- `--cycles 2` 表示"跑两轮自动结束"，演示场合用，不会卡住。正式项目会改成持续运行模式。
- `--run-log` 让每轮快照都进 JSONL 文件，每行独立 JSON 对象，可以直接被数据库、ETL、客户脚本消费。
- 让客户用 `notepad` 或 `code` 当场打开这个 JSONL 文件看一眼，这是结构化数据的最直观证据。

失败处置：

- 中间某轮某点 `BAD`：在演示场景下接受，不需要重跑；但要在交付物里如实记录。
- 文件未生成：检查 `artifacts\runtime` 目录是否被权限阻挡；当场可以换成 `%TEMP%\site-demo.jsonl`。

## 7. 故障兜底速查表

每一步失败时，先判断故障在哪一层，再决定接下来动作。这套分层判断是和客户建立专业感的关键。

| 层级 | 表征 | 排查命令 | 典型修复 |
|---|---|---|---|
| route | 流量走错网卡 / 不在同段 | `test-plc-network.ps1` | 配静态 IP、关 VPN、改路由优先级 |
| tcp | 端口不通 / 防火墙 | `Test-NetConnection -Port 102` | 让客户开放通信端口、关 STEP 7 占用 |
| protocol | TCP 通但 Snap7 握手失败 | `connect-plc.ps1` 尝试多组 connection-type / slot | 改 connection-type、确认 PUT/GET、客户 PLC 通信参数 |
| tag | 协议通但单点读失败 | `--read-once` 看哪几个点 BAD | 换地址、确认 DB 非优化、补点位定义 |
| config | XML/JSON 结构问题 | `--validate-config` | 修正配置文件，重新走 read-once |
| runtime | 程序本身依赖 | `check-prereqs.ps1` | 更换演示机，回去补依赖 |

判断顺序固定：route → tcp → protocol → tag → config → runtime。永远从外向内排查。

## 8. 演示后立刻发给客户的交付件清单

演示结束前 2 分钟把以下文件打包，当面或现场微信/邮件发给客户决策人和 IT 联系人：

1. **现场诊断快照**：演示当场的 `test-plc-network.ps1`、`connect-plc.ps1`、`device-info`、`read-once`、`--cycles 2 --run-log` 屏幕截图或终端导出，按 6 步顺序贴在一份文档里。
2. **运行日志样例**：`.\artifacts\runtime\site-demo.jsonl` 文件本体，让客户可以打开看到结构化数据。
3. **试点验收清单**：`docs\sales\pilot-acceptance-checklist.md` 的 PDF 或打印件，告诉客户"如果继续做试点，下面这些步骤就是验收口径，每条都可以签字"。
4. **试点报价模板（草稿）**：`docs\sales\pilot-quote-template.md` 填好客户基本信息和初步设备清单的版本，金额栏可以留空待报价，把"项目制三段付款 / 不包含项 / 验收口径 / 前置条件"清楚摆出来。
5. **下一步建议（一页 A4 内）**：本现场演示的结论一句话（成功 / 部分成功 / 失败原因）、试点范围一段、决策时间窗一段。不超过 300 字。

要点：

- 不要发完整代码、不要发任何 PLC 写入相关脚本、不要发 `--allow-write` 演示。
- 不要承诺 SaaS、订阅、平台、24/7 监控。
- 把客户认知锚定在"项目制现金流、按阶段验收、按节点付款"。

## 9. 演示完一句话总结模板

向客户决策人当面收尾的固定话术（可按现场情况调）：

> "今天我们用您现场的 PLC，按 6 步完成了从网卡配置、网络诊断、Snap7 握手、设备信息、点位读取到结构化日志落盘的完整链路验证。这一份诊断快照和 JSONL 日志是您可以带走的成果。如果您希望把这条链路扩展到您真正的业务点位、做几天到几周的试点交付，我们可以按这份验收清单和报价模板推进。"
