# 开发情况 · EnviroEquipmentFinalEdition

> 更新时间：2026-05-20 · 分支 main HEAD：`56bf4da`

本文档描述代码层的真实开发现状（已实现什么、怎么测的、怎么跑）。产品愿景见 [项目说明](PROJECT_OVERVIEW.md)，里程碑进度见 [项目进展说明](PROJECT_PROGRESS.md)。

---

## 1. 一句话现状

**通信后端已完整可用**（C# / .NET 8，134 个测试通过，真机 S7-200 SMART 验证过）；**WPF 桌面客户端尚未开始**（设计与实现计划已就绪）。

## 2. 技术栈

| 层 | 选型 |
|----|------|
| 运行时 | .NET 8（`net8.0`；UI 层将用 `net8.0-windows`） |
| 通信 | Snap7（西门子，原生 `snap7.dll`）+ 自研 Modbus TCP |
| 桌面 UI（Phase 2） | WPF + CommunityToolkit.Mvvm + Microsoft.Extensions.Hosting/DI |
| 持久化（Phase 2） | EF Core 8 + SQLite |
| 绘图（Phase 2） | OxyPlot.Wpf |
| 日志 | Serilog |
| 测试 | xunit + FluentAssertions |

> 工程化方案 V1.0 允许三种技术栈（Python+PySide6 / C#+WPF / Electron+React），本项目选定 **C# + WPF**：与既有 .NET 8 通信内核同语言零跨界、Windows 工控机原生性能最佳、与旧版 Qt Widgets 架构同构便于功能映射。

## 3. 已实现：通信内核

### 3.1 核心抽象与适配器
- `IPlcClient` / `SiemensS7Client` —— PLC 访问抽象，按设备串行化请求（`SemaphoreSlim`）
- `IS7Adapter` + 三实现：
  - `Snap7S7Adapter` —— 真西门子（S7-200 SMART / S7-1200 / S7-1500），走 `Snap7NativeLibrary`
  - `ModbusTcpAdapter` —— 施耐德 / 通用 Modbus TCP（线圈/离散/保持/输入寄存器）
  - `InMemoryS7Adapter` —— 开发/CI 用假 PLC（内存字典）
- `PlcPollingService` / `PlcWriteService` —— 周期轮询 + 守护式写入（SafeWrite 回读校验）

### 3.2 协议覆盖（9 个 gap 全部完成）
对照旧系统 `addressProtocol/*.xml`，已闭合 9 项协议差异：

| # | 功能 | 关键类型 |
|---|------|---------|
| gap1 | Modbus 32 位浮点 HRF + 字序 WordOrder（ABCD/CDAB/BADC/DCBA） | `WordOrder.cs` `ModbusAddress.cs` |
| gap2 | Modbus 32 位整型 HRD/HRDU + UInt32 | `ModbusAddress.cs` |
| gap3 | Tag 枚举值→标签映射 | `TagOption.cs` |
| gap4 | 一个字拆成 N 个派生 bool 点（位衍生） | `BitDerivation.cs` |
| gap5 | 除数缩放语义（legacy `scale=10` → `raw/10`） | `ScaleMode.cs` |
| gap6 | 旧 XML 地址合成（`db,dbnumber=1,addr=336` → `DB1.DBW336`） | `S7Address.cs` |
| gap7 | 手动/程序辅助功能配对 | `AuxiliaryFunction.cs` |
| gap8 | Snap7 批量读（窗口合并，N 点 1 次往返） | `Snap7BatchPlan.cs` `Snap7S7Adapter.cs` |
| gap9 | 设备模板（厂商×型号×点表 + 项目级引用） | `DeviceTemplate.cs` `ProjectDefinition.cs` |

### 3.3 配置 / 加载 / 校验
- `TagConfigLoader` —— 直接吃旧系统 `addressConfig.xml` + 现代 XML
- `ProjectConfigLoader` —— 项目级模板引用解析
- `ConfigValidationService` —— 选项唯一性、位偏移、缩放、辅助功能引用校验

## 4. 测试与真机验证

| 层次 | 手段 | 结果 |
|------|------|------|
| 单元/逻辑 | xunit（地址解析、字序交换、批量读规划、配置加载、校验） | ✅ **134 测试通过**（约 70ms） |
| adapter 往返 | `InMemoryS7Adapter` + `ModbusLoopbackServer`（本机真 Modbus TCP 协议服务器） | ✅ 通过 |
| 真机西门子 | 实物 S7-200 SMART 现场执行 | ✅ 见 [PLC_S7_200_SMART_EXECUTION_REPORT.md](PLC_S7_200_SMART_EXECUTION_REPORT.md) |
| 真机施耐德 Modbus | 暂无设备，靠 loopback 顶替 | ⚪ 未验证 |

> 测试不依赖任何硬件即可在 CI 跑完。真机西门子由专项现场执行验证。施耐德真机验证待现场联调。

## 5. 能力矩阵（程序自报）

**已实现**：Snap7 连 S7-200 SMART/1200/1500 · I/Q/M/DB + V 区映射 · 逐点读质量（单点坏不污染整快照）· 设备信息探测 · Modbus TCP 四类寄存器 · 项目 JSON 多设备 · 守护写（`--allow-write` + safeWrite + 范围检查 + 回读）

**待做（程序明确列出）**：现场真实点表（替代示例 V/M 地址）· 长跑多设备调度/重试/存储/报警事件模型 · **服务部署 + 操作员 UI + 审计日志 + 权限模型** · Modbus 设备与全部写点的现场验证

完整能力清单见 [OPERATION_CAPABILITY_MATRIX.md](OPERATION_CAPABILITY_MATRIX.md)。

## 6. 仓库结构

```
EnviroEquipmentFinalEdition/
├── src/SiemensS7Demo/           # 通信内核（已完成）
│   ├── Drivers/                 # IS7Adapter, Snap7/Modbus/InMemory, S7Address, Snap7BatchPlan
│   ├── Models/                  # TagDefinition, TagOption, WordOrder, DeviceTemplate, ...
│   ├── Services/                # Polling, Write, TagConfigLoader, ProjectConfigLoader, Validation
│   ├── Testing/                 # ModbusLoopbackServer
│   ├── Native/Snap7/            # snap7.dll + 许可
│   └── Program.cs               # 控制台 runner（多运行模式）
├── tests/EnviroEquipment.Tests/ # 134 个 xunit 测试
├── docs/                        # 本文档体系 + 设计 spec + 实施计划 + 参考盘点
└── tools/                       # 现场脚本（连机/读点/配网/自检）
```

> Phase 2 将在 `src/` 下新增 `SiemensS7Demo.Core`（现 SiemensS7Demo 改名）+ `.Domain` + `.Persistence` + `.App` + `.Wpf` 多工程分层。

## 7. 怎么跑（无需硬件）

```pwsh
# 1. 全套测试
dotnet test EnviroEquipmentFinalEdition.sln          # 134 passed

# 2. 自检烟雾（5 个场景：XML/JSON 校验、读+守护写、批量读、Modbus 回环）
dotnet run --project src/SiemensS7Demo -- --self-test # SelfTest: OK

# 3. 实时轮询演示（mock，3 轮，看 PV/SV 输出）
dotnet run --project src/SiemensS7Demo -- --mock --cycles 3 --interval 1

# 4. 能力矩阵
dotnet run --project src/SiemensS7Demo -- --capabilities

# 5. 连真机西门子
dotnet run --project src/SiemensS7Demo -- --real --ip 192.168.2.180 --cpu "S7-200 SMART" --once
```

> ⚠️ 当前只有控制台 runner，**没有图形界面** —— 图形界面是 Phase 2 WPF 客户端的交付内容。
