# 文档索引 · 环境试验箱上位机软件

> 更新时间：2026-05-20

## 项目级文档（建议从这里开始）

| 文档 | 内容 |
|------|------|
| [项目说明 PROJECT_OVERVIEW](PROJECT_OVERVIEW.md) | 定位 / 背景 / 三份资料关系 / 目标 / 设计原则 / 范围 / 交付 |
| [开发情况 DEVELOPMENT_STATUS](DEVELOPMENT_STATUS.md) | 技术栈 / 已实现 / 测试与真机 / 能力矩阵 / 仓库结构 / 怎么跑 |
| [项目进展说明 PROJECT_PROGRESS](PROJECT_PROGRESS.md) | 总进度 / 已完成 / 待做 / 里程碑 / Issue 看板 / 风险 |
| [技术方案 TECHNICAL_SOLUTION](TECHNICAL_SOLUTION.md) | 分层架构 / 通信 / 配置驱动 / 七模块 / 存储 / 界面 / 测试 / 部署 |
| [商业化说明 business](business.md) | 商业定位 / 目标客户 / 核心痛点 |

## 架构图

官方《工程化方案设计 V1.0》的 8 张图，位于 [`references/diagrams/`](references/diagrams/)：

| 图 | 主题 |
|----|------|
| `diagram_01_architecture.svg` | 系统总体架构（设备/上位机/本地文件三段式） |
| `diagram_02_protocol_topology.svg` | 通信协议拓扑（3 PLC × 3 设备 = 9 组合） |
| `diagram_03_data_flow.svg` | 端到端数据流（采集/实时/存储/回放四泳道） |
| `diagram_04_software_layers.svg` | 四层模块依赖 |
| `diagram_05_ui_navigation.svg` | 界面导航流（登录→总览↔单设备详情） |
| `diagram_06_design_system.svg` | 设计系统（暗色主题/状态语言/曲线色） |
| `diagram_07_gantt.svg` | 项目甘特图（2026-05 → 2026-11） |
| `diagram_08_state_machines.svg` | 通信状态机 + 控制权限四级校验 |

## 参考资料盘点

| 文档 | 内容 |
|------|------|
| [202604 功能盘点](references/202604-feature-inventory.md) | 旧版 Qt 子系统 → 新工程映射 |
| [202605 组件盘点](references/202605-mock-inventory.md) | React mock 每个 jsx → 哪个 Pkg 用 |

## 技术执行记录（通信后端）

| 文档 | 内容 |
|------|------|
| [DEVICE_GATEWAY_REFACTOR_PLAN](DEVICE_GATEWAY_REFACTOR_PLAN.md) | 升级/重构技术蓝图 |
| [PLC_S7_200_SMART_EXECUTION_REPORT](PLC_S7_200_SMART_EXECUTION_REPORT.md) | S7-200 SMART 真机执行报告 |
| [SNAP7_INTEGRATION](SNAP7_INTEGRATION.md) | Snap7 集成细节 |
| [OPERATION_CAPABILITY_MATRIX](OPERATION_CAPABILITY_MATRIX.md) | 能力与测试矩阵 |
| [LEGACY_POINT_IMPORT_AND_RUNTIME_TESTS](LEGACY_POINT_IMPORT_AND_RUNTIME_TESTS.md) | 旧点表导入与运行测试记录 |
| [CLAUDE_DEVICE_CONNECTION_HANDOFF](CLAUDE_DEVICE_CONNECTION_HANDOFF.md) | 设备连接交接 |

## 设计规格与实施计划

- 设计 spec：[`superpowers/specs/`](superpowers/specs/)
  - `2026-05-15-phase2-wpf-client-design.md` —— Phase 2 WPF 客户端总设计
  - `2026-05-14-legacy-protocol-coverage-design.md` —— 协议覆盖设计（已实现）
- 实施计划：[`superpowers/plans/`](superpowers/plans/)
  - Phase 2：`phase2-pkg1/2/4-*.md`（Pkg 3 见 GitHub issue #19）
  - 协议 gap：`gap1`–`gap9`（均已实现并合入 main）
