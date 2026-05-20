# 技术方案 · 环境试验箱上位机软件

> 更新时间：2026-05-20
> 依据：《环境试验箱上位机软件 工程化方案设计 V1.0》(2026-04-27) + 当前 .NET 8 实现

本文档是技术架构方案。产品愿景见 [项目说明](PROJECT_OVERVIEW.md)，进度见 [项目进展说明](PROJECT_PROGRESS.md)，代码现状见 [开发情况](DEVELOPMENT_STATUS.md)。架构图位于 [`references/diagrams/`](references/diagrams/)。

---

## 1. 总体架构

系统采用 **「设备层 + 通信层 + 业务层 + 配置层 + 数据层 + 界面层 + 日志层」** 分层结构。每一层只处理自己负责的事，依赖关系**单向**：界面层依赖业务层，业务层依赖通信层和数据层，通信层不依赖界面层。

> 架构图：[`diagram_01_architecture.svg`](references/diagrams/diagram_01_architecture.svg) — 设备层 / 上位机软件 / 本地文件层 三段式分层

### 1.1 部署形态
**单机桌面部署**：软件运行在现场一台 Windows 工业电脑或普通 Windows 主机上，不依赖云平台。可离线运行，安装维护简单，数据本地保存便于拷贝备份。

### 1.2 技术栈选型
工程方案允许三种实现（Python+PySide6 / C#+WPF / Electron+React）。本项目选定 **C# + .NET 8 + WPF**：

| 维度 | 理由 |
|------|------|
| 与通信内核同语言 | 通信后端已是 .NET 8，UI 同语言零跨进程/跨序列化成本 |
| Windows 工控机原生 | WPF 原生 GPU 加速，长跑稳定，无 Electron/Node 运行时开销 |
| 与旧版 Qt 同构 | 202604 是 Qt Widgets（MVVM 风格），WPF + MVVM 功能映射直接 |
| 控件生态成熟 | DataGrid、Ribbon、图表（OxyPlot）在 WPF 上成熟 |

## 2. 工程分层（.NET 解决方案）

Phase 2 将单一控制台工程拆为分层解决方案，依赖严格向下：

```
┌──────────────────────────────────────────────┐
│ SiemensS7Demo.Wpf       net8.0-windows, WPF   │  界面层
│   Views(XAML) + ViewModels(CommunityToolkit)  │
├──────────────────────────────────────────────┤
│ SiemensS7Demo.App       业务/编排服务          │  业务层
│   DeviceSessionManager · AlarmService         │
│   ProgramExecutionService · HistoryWriter     │
│   AuthService · LimsClient · Mqtt · Ftp       │
├──────────────────────────────────────────────┤
│ SiemensS7Demo.Domain    纯领域模型             │  领域层
├──────────────────────────────────────────────┤
│ SiemensS7Demo.Persistence  EF Core 8 + SQLite │  数据层
├──────────────────────────────────────────────┤
│ SiemensS7Demo.Core (现 SiemensS7Demo 改名)     │  通信+配置层
│   Drivers / Models / Services                 │
└──────────────────────────────────────────────┘
```

> 架构图：[`diagram_04_software_layers.svg`](references/diagrams/diagram_04_software_layers.svg) — 四层模块依赖关系：UI / Core / 通信·配置·数据 / 基础支撑

## 3. 通信方案

### 3.1 统一接口屏蔽协议差异
业务层通过统一接口 `IPlcClient` / `IS7Adapter` 访问 PLC，**不直接操作具体协议**。新增 PLC 协议时只需新增一个适配器，业务层与界面层不变。

| 适配器 | 协议 | 设备 |
|--------|------|------|
| `Snap7S7Adapter` | S7 over TCP | 西门子 S7-200 SMART / S7-1200 / S7-1500 |
| `ModbusTcpAdapter` | Modbus TCP | 施耐德 Modicon M221 / 通用 |
| `InMemoryS7Adapter` | —（内存） | 开发 / CI |

> 架构图：[`diagram_02_protocol_topology.svg`](references/diagrams/diagram_02_protocol_topology.svg) — 3 PLC × 3 设备型号 = 9 联调组合

### 3.2 读取流程
```
采集调度器 → 通信层 → PLC 批量读取 → 解析器(类型转换+缩放) → 设备快照 → 界面/曲线/报警/存储
```
读取尽量**批量**：一次读一段连续地址，减少通信次数。Snap7 批量读由 `Snap7BatchPlan` 做窗口合并规划（N 点合并为 ≤PDU 窗口的一次往返）。

### 3.3 写入流程（四级前置校验）
```
UI → 控制服务 → 检查[在线? 远程? 范围? 状态允许?] → 通信层 → PLC → 返回结果 → 写日志 → 提示用户
```
**控制命令绝不直接从按钮发给 PLC**，必须先过四级前置校验（`--allow-write` + 每点 `safeWrite=true` + 范围检查 + 写后回读确认）。

> 架构图：[`diagram_08_state_machines.svg`](references/diagrams/diagram_08_state_machines.svg) — 通信状态机 + 控制权限状态机（控制下发四级前置校验）

### 3.4 字节序与数据类型
支持 Int16/UInt16/DInt/UInt32/Real/Bool；32 位跨双寄存器值支持 4 种字序（ABCD/CDAB/BADC/DCBA）；旧 XML 缩放语义支持乘数与除数两种模式。

## 4. 配置驱动

协议配置是「可配置」的关键 —— 只要协议 XML 设计合理，**新设备适配无需频繁改代码**。采集通道 XML 示例：

```xml
<Channel id="TEMP_ACT" category="Acquisition">
  <Name zh="当前温度" en="Actual Temperature" />
  <Unit>℃</Unit>
  <Address area="DB" db="10" byteOffset="0" bitOffset="0" />
  <DataType>Int16</DataType>
  <Access>Read</Access>
  <Scale>0.1</Scale>
  <Precision>1</Precision>
  <Display enabled="true" order="1" />
  <Storage enabled="true" />
  <Curve enabled="true" />
</Channel>
```

配置分五类：系统配置 / 设备配置 / 协议配置 / 曲线配置 / 语言配置。已实现 `TagConfigLoader`（吃旧版 `addressConfig.xml` + 现代 XML）、`ProjectConfigLoader`（模板引用）、`ConfigValidationService`（保存前校验）。

## 5. 七大核心模块

| 模块 | 职责 | 实现状态 |
|------|------|---------|
| 设备管理 | 新增/编辑/删除/连接/断开/状态；每台独立配置；单台异常隔离 | 🟢 Pkg 1 `DeviceSessionManager` |
| 通信 | PLC 读写；`IPlcClient` 统一接口；批量读 + 四级校验写 | ✅ 已实现 |
| 配置 | 系统/设备/协议/曲线/语言配置；协议 XML 可配置 | ✅ 已实现 |
| 数据采集存储 | 周期 1–120s 可配；一次采集驱动 界面/曲线/报警/存储 四路；单设备隔离 | 🟢 采集已实现，存储 Pkg 3 |
| 曲线 | 实时+历史曲线；多通道/双轴/十字光标/多窗口对比；自动降采样 | 🟢 Pkg 3 `HistoryTrendView`(OxyPlot) |
| 报警 | 判断/展示/记录；明显但不反复弹窗；当前+历史；恢复时间 | 🟢 Pkg 2 `AlarmService` |
| 日志 | 运行/通信/操作/报警/存储 五类；按天分文件 | 🟢 Serilog（Phase 2 接入） |

> 架构图：[`diagram_03_data_flow.svg`](references/diagrams/diagram_03_data_flow.svg) — 端到端数据流：采集 / 实时 / 存储 / 回放 四泳道

## 6. 数据存储

工程方案 V1.0 原设计为**本地 CSV**（每设备每日分段，失败行保留通信状态不顶替前值）。

当前实现选型在 Phase 2 升级为 **EF Core 8 + SQLite**：

| 维度 | CSV（原方案） | SQLite（实现选型） |
|------|--------------|-------------------|
| 历史回看查询 | 需自行解析拼接 | SQL 索引查询，跨段拼接天然支持 |
| 报警/程序/用户统一存储 | 多份文件 | 单库多表，事务一致 |
| 导出 CSV/Excel | 原生 | 查询后导出 |
| 可拷贝/可备份 | ✅ | ✅ 单文件 `.db` |

> 决策：SQLite 在「跨段拼接回看 + 报警历史 + 程序存储 + 用户表」统一场景下优于裸 CSV，同时保留单文件可拷贝特性。CSV 导出作为对外交付格式保留。`HistoryWriter` 用有界 `Channel` + 自适应采样（运行 1s / 保持 5s / 空闲 1min）+ 背压丢弃计数，对应工程方案「缓存限长 + 异步写入」稳定性要求。

## 7. 界面方案

### 7.1 双场景导航
界面采用 **「多设备总览 + 单设备详情」** 双场景结构。总览页「快速看状态」，不放复杂控制按钮以免误操作；详情页才聚焦读数、曲线、控制。所有页面顶栏全程显示 在线/报警/语言/班次。

> 架构图：[`diagram_05_ui_navigation.svg`](references/diagrams/diagram_05_ui_navigation.svg) — 登录 → 总览(HUB) ↔ 单设备详情(DETAIL)

### 7.2 九个关键页面 → Phase 2 包映射
| # | 页面 | Phase 2 包 |
|---|------|-----------|
| 1 | 多设备总览（2×2/3×3/4×4 自适应网格） | Pkg 1 |
| 2 | 单设备详情（大字读数 + 全屏趋势 + 操作区） | Pkg 1 |
| 3 | 程序编辑（分段曲线 + 校验 + 回读确认） | Pkg 3 |
| 4 | 报警中心（当前/历史/已屏蔽，三级，批量操作） | Pkg 2 |
| 5 | 历史回看（跨段拼接 + 多曲线对比 + 统计表） | Pkg 3 |
| 6 | LIMS/黑灯任务（四列看板） | Pkg 4 |
| 7 | 设备接入（列表 + 详情 + 自检 + 地址表导入） | Phase 3 |
| 8 | 监控布局编辑（多页布局 + 拖拽） | Phase 3 |
| 9 | 设备维护（PID/限值/工程参数读写） | Phase 3 |

> 界面视觉以 **202605 设计原型为准、保持不变**（`温箱202605/`，详见 [202605 组件盘点](references/202605-mock-inventory.md)）。

### 7.3 设计系统
**任务关键型暗色主题**（mission-critical dark theme）：深石墨背景 + 克制青色高亮 + 高对比文字。状态用 **颜色 + 形状双通道编码**（OK 圆点 / 待命方块 / 暂停三角 / 报警闪烁圆 / 离线灰圆），保证色觉障碍可识别。曲线限定 5 色避免视觉过载。WPF 实现：`styles.css` 设计 token 机械转 `Themes/Tokens.xaml`。

> 架构图：[`diagram_06_design_system.svg`](references/diagrams/diagram_06_design_system.svg) — 表面色 / 文字层级 / 状态语言 / 曲线色 / 字体 / 关键组件

## 8. 关键状态机
设备状态由通信层统一上报，业务层订阅，界面层只负责渲染 —— 避免各模块各自判断导致界面状态错乱。`DeviceSessionManager` 暴露 `IObservable<Device>` 快照流，程序执行用 `Idle→Ramping→Holding→Jumping→Ended/Paused` 状态机驱动 setpoint。

## 9. 稳定性与测试

### 9.1 稳定性保障
- 通信与界面分线程
- 数据写入异步（队列 / 有界 Channel）
- 曲线缓存限长（滑动窗口 + 自动降采样）
- 日志按日滚动归档
- 配置保存前校验 + 自动备份 + 写入失败回滚
- 异常必须捕获，主程序不直接退出

### 9.2 异常分级
| 级别 | 说明 | 处理 |
|------|------|------|
| 一级 | 主程序无法启动 | 阻止运行并提示修复 |
| 二级 | 单台设备配置错误/通信故障 | 仅该设备异常，其他继续运行 |
| 三级 | 导出失败/历史读取失败 | 当前操作失败，系统继续 |
| 四级 | 用户输入错误 | 直接提示修改 |

### 9.3 测试矩阵
**3 PLC × 3 设备型号 = 9 组合**，每组合需完成 单元 / 模块 / 集成 / 现场联调 / 30 天稳定性。

| PLC \ 设备 | 标准温湿型 | 低气压型 | 温度冲击型 |
|-----------|----------|---------|----------|
| 施耐德 M221 | ✓ | ✓ | ✓ |
| 西门子 S7-200 SMART | ✓（真机已验证） | ✓ | ✓ |
| 西门子 S7-1500 | ✓ | ✓ | ✓ |

当前自动化测试：134 个 xunit（mock + Modbus loopback），CI 无硬件依赖。真机西门子 S7-200 SMART 已现场验证；其余组合待天津现场联调。

### 9.4 30 天稳定性关注点
持续采集/写文件 · 内存不异常增长（RSS 趋势）· 无明显卡顿（FPS/控制时延）· 断线可恢复（拔网线/拔电/软重启 PLC）· 历史数据可正常回看。

## 10. 部署与交付

### 10.1 现场部署目录
```
EnvChamberSupervisor/
├─ EnvChamberSupervisor.exe   # 上位机主程序
├─ ProtocolEditor.exe         # 协议配置工具（独立 EXE）
├─ Config/                    # 设备配置 + 协议 XML + 语言资源
├─ Data/                      # 历史数据（按设备/按日/按段）
├─ Logs/                      # 运行/通信/操作/报警/存储 日志
├─ Templates/                 # 协议模板 + 配置示例
├─ Docs/                      # 使用说明书 + 部署说明
└─ Backup/                    # 配置自动备份
```

### 10.2 交付清单
主程序 EXE + 协议配置工具 EXE + 3 套 PLC 协议模板（M221 / S7-200 SMART / S7-1500）+ 3 种设备配置示例 + 使用/部署/运维手册 + 测试报告 + 30 天稳定性记录 + 验收记录。源代码与设计文档随同交付。

### 10.3 质保
1 年质保期免费修复缺陷；远程 + 现场支持（重大问题 48h 响应）；协议模板与界面布局升级支持。

## 11. 实施路线
项目按标准范式推进：需求分析 → 概要设计 → 详细设计 → 编码 → 单元测试 → 集成测试 → 现场联调 → 稳定性测试 → 验收交付。6 个月 / 4 阶段 / 16 任务 / 3 里程碑。

> 甘特图：[`diagram_07_gantt.svg`](references/diagrams/diagram_07_gantt.svg) — 项目实施甘特图（2026-05 → 2026-11）

详细排期与里程碑见 [项目进展说明](PROJECT_PROGRESS.md)。
