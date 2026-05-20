# 试点验收清单

更新时间：2026-05-20

本清单是试点项目现场逐步执行的验收文档。按 9 步执行，每步客户和工程师双方签字确认；任何一步失败需在"备注"栏写明原因和下一步处置。

参考依据：`docs\OPERATION_CAPABILITY_MATRIX.md` §6 推荐验收顺序。

---

## 现场信息

| 字段 | 内容 |
|---|---|
| 客户公司 | [_____] |
| 现场地址 | [_____] |
| 验收日期 | [_____] |
| 验收开始时间 | [_____] |
| 验收结束时间 | [_____] |
| 工程师姓名 | [_____] |
| 客户对接人 | [_____] |
| 客户决策人（验收签字人） | [_____] |
| 演示电脑型号 / 操作系统 | [_____] / [_____] |
| 目标 PLC 型号 | [_____] |
| 目标 PLC IP | [_____] |
| PC 物理网卡 IP | [_____] / [____] |

---

## 步骤 1：固定 PLC IP 和电脑网卡

| 字段 | 内容 |
|---|---|
| 命令 | `.\tools\configure-plc-ethernet-ip.ps1`（需管理员权限） |
| 期望输出 | 物理网卡 IP 配置为 `192.168.2.10/24`（或客户网段对应静态 IP），PLC IP `192.168.2.180`（或现场实际值）保持不变。 |
| 实际输出 | [_____] |
| 通过否 | [ ] 通过 [ ] 不通过 |
| 备注 | [_____] |
| 客户签字 | [_____] |
| 工程师签字 | [_____] |

失败处置参考：

- 网卡无法配静态 IP：是否有域策略锁定；改用客户自备同段电脑。
- PLC IP 与计划不符：以现场实际为准，在本表所有后续 PLC IP 字段中替换。

---

## 步骤 2：前置条件检查 + PLC 网络检查

### 2a：本机前置条件

| 字段 | 内容 |
|---|---|
| 命令 | `.\tools\check-prereqs.ps1` |
| 期望输出 | `dotnet command - OK` 或 `csc fallback - OK`；`Snap7 DLL - OK`；XML 编码无错。 |
| 实际输出 | [_____] |
| 通过否 | [ ] 通过 [ ] 不通过 |
| 备注 | [_____] |

### 2b：PLC 网络可达性

| 字段 | 内容 |
|---|---|
| 命令 | `.\tools\test-plc-network.ps1 -Ip <PLC IP> -Port 102` |
| 期望输出 | `Ping: True`；`TcpTestSucceeded: True`；`InterfaceAlias` 为物理网卡（如 `以太网`）；`SourceAddress` 在 PLC 同网段（如 `192.168.2.10`）；`Network precheck passed.`。 |
| 实际输出 | [_____] |
| 通过否 | [ ] 通过 [ ] 不通过 |
| 备注 | [_____] |
| 客户签字 | [_____] |
| 工程师签字 | [_____] |

失败处置参考：

- `InterfaceAlias` 是 `LetsTAP/Nord/TAP`：路由被虚拟网卡抢占，关闭对应 VPN 后重跑。
- `TcpTestSucceeded: False`：检查网线、交换机、防火墙；端口 102 是否对外开放。

---

## 步骤 3：connect-only 握手

| 字段 | 内容 |
|---|---|
| 命令 | `.\tools\connect-plc.ps1`（或带 `-Ip <现场 IP>` 覆盖） |
| 期望输出 | `Network precheck passed.`；`Trying connectionType=basic rack=0 slot=0 ...`；`Connected.`；`SUCCESS: Snap7 connected with connectionType=<X> rack=<Y> slot=<Z>.`。 |
| 现场实测成功的 connection-type | [_____] |
| 现场实测成功的 rack | [_____] |
| 现场实测成功的 slot | [_____] |
| 实际输出 | [_____] |
| 通过否 | [ ] 通过 [ ] 不通过 |
| 备注 | [_____] |
| 客户签字 | [_____] |
| 工程师签字 | [_____] |

失败处置参考：

- 所有 connection-type 都超时：90% 是路由问题；回步骤 2。
- 路由没问题仍超时：确认 PLC 是否启用 PUT/GET 通信、是否被 STEP 7 编程软件独占。

**重要**：本步骤记录的 `connection-type / rack / slot` 是后续所有项目 JSON 的固定参数。

---

## 步骤 4：device-info 设备信息

| 字段 | 内容 |
|---|---|
| 命令 | `.\tools\device-info.bat` 或 `.\tools\run-s7-demo.ps1 --adapter snap7 --device-info` |
| 期望输出 | 至少输出 `BlockCounts:` 一行（如 `OB=1, DB=1, SDB=2`）；可能伴随 `Unsupported:` 列出该 CPU 不支持的 Snap7 接口（S7-200 SMART 常见，**不算失败**）。 |
| 实际输出 - BlockCounts | [_____] |
| 实际输出 - Unsupported | [_____] |
| 通过否 | [ ] 通过 [ ] 不通过 |
| 备注 | [_____] |
| 客户签字 | [_____] |
| 工程师签字 | [_____] |

通过判定：只要 `BlockCounts` 一行可读，即视为通过。Unsupported 接口仅做记录。

---

## 步骤 5：validate-config 配置校验

| 字段 | 内容 |
|---|---|
| 命令（默认 XML） | `.\tools\run-s7-demo.ps1 --validate-config` |
| 命令（项目 JSON） | `.\tools\run-s7-demo.ps1 --project <客户项目 JSON 路径> --validate-config` |
| 期望输出 | `Config validation: OK`；不含 `error` / `invalid`。 |
| 校验对象 | [ ] 默认 S7 XML [ ] 客户项目 JSON：[_____] |
| 实际输出 | [_____] |
| 通过否 | [ ] 通过 [ ] 不通过 |
| 备注 | [_____] |
| 客户签字 | [_____] |
| 工程师签字 | [_____] |

失败处置参考：

- 提示某点位地址非法：修正 XML/JSON 后重跑，**不能跳过**。
- 提示 `safeWrite` 与 `--allow-write` 不一致：试点阶段保持写入关闭，按只读处理。

---

## 步骤 6：read-once 默认探测点位

| 字段 | 内容 |
|---|---|
| 命令 | `.\tools\read-once.bat` 或 `.\tools\run-s7-demo.ps1 --adapter snap7 --read-once` |
| 默认读取的点位 | `V0.0`、`VW0`、`VW2`、`VD4`、`VD8`、`M0.0`（来源 `siemens_s7_200_smart_sample.xml`） |
| 期望输出 | 每个点位单独一行，质量列均为 `GOOD`。 |
| 实际 GOOD 点位 | [_____] |
| 实际 BAD 点位 | [_____] |
| 通过否 | [ ] 通过（全部 GOOD） [ ] 部分通过（备注说明） [ ] 不通过 |
| 备注 | [_____] |
| 客户签字 | [_____] |
| 工程师签字 | [_____] |

通过判定：

- 全部 GOOD：通过。
- 部分 GOOD 且能解释（如非 S7-200 SMART 设备，部分探测点不存在）：备注后视为通过，进入步骤 8 替换为真实点位。
- 全部 BAD：返回步骤 3 复核握手。

---

## 步骤 7：cycles 2 有限轮询 + 运行日志落盘

| 字段 | 内容 |
|---|---|
| 命令 | `.\tools\run-s7-demo.ps1 --adapter snap7 --cycles 2 --interval 1 --run-log .\artifacts\runtime\pilot-acceptance.jsonl` |
| 期望输出 - 控制台 | 显示 2 轮快照，每轮各点 `GOOD/BAD` 统计；命令退出码 `0`。 |
| 期望输出 - 文件 | `.\artifacts\runtime\pilot-acceptance.jsonl` 存在，包含至少 2 行；每行为合法 JSON，含 `type=snapshot, timestampUtc, deviceId, good, bad, values[]`。 |
| 实际控制台输出 good/bad 计数（每轮） | 第 1 轮：good=[_____] bad=[_____]；第 2 轮：good=[_____] bad=[_____] |
| 日志文件路径 | [_____] |
| 日志文件大小 | [____] 字节 |
| 通过否 | [ ] 通过 [ ] 不通过 |
| 备注 | [_____] |
| 客户签字 | [_____] |
| 工程师签字 | [_____] |

通过判定：退出码 0 + 日志文件含 ≥ 2 行合法 JSON snapshot。

---

## 步骤 8：真实业务点位读取

| 字段 | 内容 |
|---|---|
| 业务点位来源 | [ ] 客户旧 XML 导入 [ ] 客户旧上位机配置导入 [ ] 客户人工提供点位表 [ ] 现场口述 |
| 导入命令（如适用） | `.\tools\import-legacy-config.ps1 -S7IpOverride <PLC IP> -MaxTagsPerDevice <N> -OutputPath src\SiemensS7Demo\Config\pilot.project.json` |
| 导入后配置校验命令 | `.\tools\run-s7-demo.ps1 --project src\SiemensS7Demo\Config\pilot.project.json --validate-config` |
| 严格读取命令 | `.\tools\run-s7-demo.ps1 --project src\SiemensS7Demo\Config\pilot.project.json --cycles 1 --fail-on-bad-quality --run-log .\artifacts\runtime\pilot-business.jsonl` |
| 期望输出 | 导入成功提示 `Imported devices: N, Imported tags: M`；配置校验 `OK`；严格读取退出码 `0`，`good=N, bad=0`。 |
| 实际导入设备数 | [_____] |
| 实际导入点位数 | [_____] |
| 实际 GOOD 点位数 | [_____] |
| 实际 BAD 点位数 | [_____] |
| 严格读取退出码 | [_____] |
| 通过否 | [ ] 通过（≥ 30 个 GOOD，退出码 0） [ ] 部分通过（备注） [ ] 不通过 |
| 备注 | [_____] |
| 客户签字 | [_____] |
| 工程师签字 | [_____] |

通过判定（试点最低门槛）：

- 至少 **30 个**业务点位返回 GOOD。
- `--fail-on-bad-quality` 退出码为 `0`。
- 业务点位含义、地址、类型可被客户工程师当场复核。

部分通过处置：

- 某些点位地址在客户旧表中显示中文乱码：记录字段名，回去后修复编码再次提供新配置。
- 某些点位 BAD 因 DB 优化：与客户沟通是否调整 DB 配置或映射到通信 DB；在备注中明确双方决策。

---

## 步骤 9（条件性）：安全写入验证

**前提**：客户已在报价书 §9 中书面确认提供安全写入测试点位，且本次验收范围包含真实写入。否则跳过本步并在通过栏标记"不适用"。

| 字段 | 内容 |
|---|---|
| 客户指定的安全写入点位（地址） | [_____] |
| 客户指定的安全写入点位（用途说明） | [_____] |
| 写入前点位当前值 | [_____] |
| 计划写入值 | [_____] |
| 写入命令 | `.\tools\run-s7-demo.ps1 --adapter snap7 --config <已配置 safeWrite=true 的 XML> --allow-write --write <TagName>=<Value> --read-once` |
| 期望输出 | 写入成功，立即读回显示新值；未带 `--allow-write` 重试时被拒绝（验证保护机制有效）。 |
| 写入后实际读回值 | [_____] |
| 不带 `--allow-write` 重试结果 | [_____] |
| 复位 / 回滚动作 | [_____] |
| 是否触发非预期设备动作 | [ ] 否 [ ] 是（描述）：[_____] |
| 通过否 | [ ] 通过 [ ] 不通过 [ ] 不适用（客户未授权） |
| 备注 | [_____] |
| 客户签字 | [_____] |
| 工程师签字 | [_____] |

通过判定：

- 写入成功 + 读回正确 + 写入开关拦截验证通过 + 无非预期设备动作。
- 任何一项失败立即停止后续写入测试，并按 PLC 程序逻辑回退。

---

## 验收结论

| 字段 | 内容 |
|---|---|
| 步骤 1–7 全部通过 | [ ] 是 [ ] 否 |
| 步骤 8 通过 | [ ] 是 [ ] 否（备注：[_____]） |
| 步骤 9 通过 / 不适用 | [ ] 通过 [ ] 不适用 [ ] 不通过 |
| 整体验收结论 | [ ] 通过 [ ] 部分通过 [ ] 不通过 |
| 不通过原因（如有） | [_____] |
| 下一步行动 | [_____] |

## 交付件清单（验收通过后由工程师提交客户）

- [ ] 项目 JSON 配置文件：`src\SiemensS7Demo\Config\pilot.project.json`
- [ ] 业务点位表（含地址、类型、单位、含义）
- [ ] 运行日志样例：`.\artifacts\runtime\pilot-acceptance.jsonl` 和 `.\artifacts\runtime\pilot-business.jsonl`
- [ ] 启动脚本与命令清单（一页 A4）
- [ ] 本验收记录签字版（扫描件或纸质件）
- [ ] 现场诊断快照（test-plc-network / connect-plc / device-info / read-once 屏幕截图）
- [ ] 后续正式项目建议方向（一页 A4，参考报价模板 §11）

## 双方最终签字

| | 客户方 | 服务方 |
|---|---|---|
| 公司 | [_____] | [_____] |
| 签字人 | [_____] | [_____] |
| 职务 | [_____] | [_____] |
| 签字日期 | [_____] | [_____] |
| 公章 | | |

---

**附注（不出现在客户版本中）**：

- 每一步签字字段都不可空，"通过"或"不通过"必须明确；"备注"栏遇到失败时必填，不能用"略"。
- 步骤 6（默认探测点位）与步骤 8（真实业务点位）不能合并；前者验证链路，后者验证业务价值。
- 步骤 9 默认不做。仅在客户书面确认+现场指定安全点+PLC 程序允许写入三项同时满足时执行。
- 验收记录扫描件作为 M3 验收尾款回款的关键凭证之一。
