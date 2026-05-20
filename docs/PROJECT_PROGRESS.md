# 项目进展说明 · EnviroEquipmentFinalEdition

> 更新时间：2026-05-20

本文档跟踪里程碑与进度。产品愿景见 [项目说明](PROJECT_OVERVIEW.md)，技术细节见 [技术方案](TECHNICAL_SOLUTION.md)。

---

## 1. 总进度

```
通信后端    ██████████████████████  100%  ✅ 已完成（含真机西门子验证）
Phase 2 设计 ██████████████████████  100%  ✅ 设计 + 实施计划已就绪
Phase 2 实现 ███████░░░░░░░░░░░░░░░   30%  🟡 Pkg 1 已合 (PR #38)；Pkg 2/4 在 worktree 中
现场交付    ░░░░░░░░░░░░░░░░░░░░░░    0%  ⚪ 待 Phase 2 完成
```

## 2. 已完成

### 2.1 Phase 1 — Siemens S7 MVP ✅
通信骨架、`IPlcClient` / `SiemensS7Client`、`InMemoryS7Adapter`、轮询/写入服务。

### 2.2 通信后端协议覆盖（9 个 gap）✅
全部 9 个协议差异闭合，134 测试通过。明细见 [开发情况 §3.2](DEVELOPMENT_STATUS.md)。已通过 PR #2–#18 合并入 main：

| 能力 | PR |
|------|-----|
| 测试工程脚手架 | #2 |
| phase-2 baseline（Snap7 + Modbus + 项目加载） | #4 |
| gap1 Modbus float / gap3 Tag Options / gap8 Snap7 batch | #8 / #9 / #10 |
| gap4 BitDerivations / gap2 Modbus 32bit | #11 / #12 |
| gap5 Scale / gap6 地址合成 / gap7 Auxiliary / gap9 模板 | #14 / #15 / #16 / #18 |

### 2.3 真机验证 ✅
实物 S7-200 SMART 现场执行通过，见 [PLC_S7_200_SMART_EXECUTION_REPORT.md](PLC_S7_200_SMART_EXECUTION_REPORT.md) 与 [SNAP7_INTEGRATION.md](SNAP7_INTEGRATION.md)。

### 2.4 Phase 2 设计与计划 ✅（PR #20）
- 设计 spec：[2026-05-15-phase2-wpf-client-design.md](superpowers/specs/2026-05-15-phase2-wpf-client-design.md)
- 实施计划：[Pkg 1](superpowers/plans/2026-05-15-phase2-pkg1-shell-overview-single.md) · [Pkg 2](superpowers/plans/2026-05-15-phase2-pkg2-alarms.md) · [Pkg 4](superpowers/plans/2026-05-15-phase2-pkg4-login-lims-mqtt-ftp.md)（Pkg 3 见 issue #19）
- 参考盘点：[202604 功能](references/202604-feature-inventory.md) · [202605 组件](references/202605-mock-inventory.md)

### 2.5 Phase 2 Pkg 1 — WPF Shell + 总览 + 单设备 ✅（PR #38）
4 段 Shell（左导航 / 顶 KPI / 中工作区 / 底状态条）、多设备总览、单设备详情，配套 `IDeviceSessionManager`（M1.3）已合入 main，后续 Pkg 2/3/4 均可基于此构建。

## 3. 待做：Phase 2 WPF 桌面客户端

把官方工程方案的 9 个界面、202605 锁定 UI、202604 旧版业务，在 WPF 上落地。拆为 4 个包：

| 包 | GitHub Issue | 内容（对应工程方案界面） | 依赖 | 当前状态 |
|----|-------------|------------------------|------|---------|
| **Pkg 1** | [#21](https://github.com/YansongG-HKU/EnviroEquipmentFinalEdition/issues/21) | WPF 外壳 + 多设备总览 + 单设备详情 | 无 | ✅ 已合并 PR #38 |
| Pkg 2 | [#22](https://github.com/YansongG-HKU/EnviroEquipmentFinalEdition/issues/22) | 报警中心（当前/历史/弹出/toast） | Pkg 1 M1.3 | 🟡 M2.1–M2.4 完成（4 commits 在 `feat/phase2-pkg2-alarms`，36 测试通过）；M2.5 弹出/toast 未提交 |
| Pkg 3 | [#19](https://github.com/YansongG-HKU/EnviroEquipmentFinalEdition/issues/19) | 程序编辑 + 历史回看 + 持久化 | Pkg 1 M1.3 | 🟡 实施计划已写（1 commit 在 `feat/phase2-pkg3-programs-history-sqlite`）；M3.1 SQLite 待启动 |
| Pkg 4 | [#23](https://github.com/YansongG-HKU/EnviroEquipmentFinalEdition/issues/23) + [#24–31](https://github.com/YansongG-HKU/EnviroEquipmentFinalEdition/issues/24) | 登录/RBAC + LIMS + MQTT + FTP | Pkg 1 M1.3 + Pkg 3 M3.1 | 🟡 M4.1–M4.5 完成（5 commits 在 `feat/phase2-pkg4-login-lims-mqtt-ftp`，~46 测试）；M4.6 MQTT 待启动 |

执行顺序：**~~#21~~ → (#19 + #22 并行) → #23**（Pkg 1 已交付，其余三包已在 worktree 推进）

> 工程方案中的「监控布局编辑」「设备维护(PID)」「设备接入」三屏目前归入 Phase 3，待 Phase 2 四包落地后排期。

## 4. 里程碑（官方工程方案 6 个月计划）

| 里程碑 | 时间 | 出口标准 | 当前状态 |
|--------|------|---------|---------|
| M1 设计冻结 | 2026-07-05 | 概要+详细设计评审通过；协议 XML 模板冻结 | 🟢 设计文档已就绪，协议层已实现（超前） |
| M2 现场就绪 | 2026-09-10 | 全部模块开发 + 集成测试通过；具备天津现场联调条件 | ⚪ 取决于 Phase 2 四包完成 |
| M3 验收交付 | 2026-10-30 | 30 天稳定性全过 + 交付物移交 + 1 年质保启动 | ⚪ 未开始 |

> **进度判断**：通信后端已**超前**于 M1 计划（协议层本应 M1 冻结模板、详设阶段才编码，实际已完成并真机验证）。当前瓶颈在 Phase 2 客户端 UI，是 M2「现场就绪」的关键路径。

## 5. Phase 2 内部时间线（方案 B 分层并行，约 12 周）

```
Week  1  2  3  4  5  6  7  8  9 10 11 12
Pkg1  [█ M1.1–M1.6 关键路径 █]
Pkg2          [██████ M2 ██████]
Pkg3          [████████████ M3 ████████████]
Pkg4              [████████ M4 ████████]
```

- Pkg 1 第 3 周交付（先用 InMemoryAdapter）
- Pkg 1 M1.3（DeviceSessionManager）落地后，Pkg 2/3 并行起飞
- Pkg 3 M3.1（SQLite）落地后，Pkg 4 用户表迁移可叠加

## 6. 风险状态（对照工程方案风险表）

| 风险 | 应对 | 当前状态 |
|------|------|---------|
| 地址表不完整 | 前期整理 + 联调确认；纳入设计冻结门槛 | ⚪ 现场点表待天津确认 |
| 设备差异大 | 配置驱动 + UI 按协议 XML 动态生成 | ✅ 配置驱动已实现 |
| 通信压力大（采样超时/丢点） | 批量读 + 错峰采集 + 分组调度 | ✅ Snap7 批量读已实现；调度待 Phase 2 |
| 长时间运行内存上涨 | 缓存限长 + 日志滚动 + 30 天专项测试 | ⚪ HistoryWriter 背压设计在 Pkg 3 |
| 配置误改 | 保存前校验 + 自动备份 + 一键回滚 | 🟢 校验已实现；备份/回滚待 Phase 2 |
| 程序下载失败 | 分段写入 + 失败定位 + 回读确认 | ⚪ 程序执行在 Pkg 3 M3.4 |
| 现场网络抖动 | 重连退避 + 断线缓存 + 状态机统一 | ⚪ 待 Phase 2 设备会话管理 |

## 7. 下一步

1. **Pkg 2 收尾**：把 `feat/phase2-pkg2-alarms`（M2.1–M2.4，36 测试）开 PR；M2.5 弹出/toast 在新分支续做
2. **Pkg 3 启动**：合入 Pkg 3 实施计划文档；立刻启动 M3.1（`SiemensS7Demo.Persistence` + EnviroDbContext + 初始 migration），它解锁 Pkg 4 M4.1 的用户表替换
3. **Pkg 4 收尾**：把 `feat/phase2-pkg4-login-lims-mqtt-ftp`（M4.1–M4.5，~46 测试）开 PR；之后启动 M4.6 MQTT + DPAPI（同时处理 appsettings 明文种子密码）
4. 四包完成 → 集成测试 → 天津现场联调（M2）→ 30 天稳定性 → 验收（M3）

> **已废弃分支**：`claude/determined-cray-7a0dfe`、`claude/nostalgic-mccarthy-395d38` 是 Wave 1 之前的 `EnviroEquipment.*` 架构尝试，与当前 `SiemensS7Demo.*` 体系不兼容，已在 2026-05-20 清理。
