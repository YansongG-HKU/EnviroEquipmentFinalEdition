# Phase 2 — WPF Desktop Client (UI + Legacy Feature Inheritance) Design

- **Status**: Draft (pending user review)
- **Date**: 2026-05-15
- **Owner**: lead (opus-4-7-1m)
- **Stack**: .NET 8 + WPF (`net8.0-windows`, `UseWPF=true`), CommunityToolkit.Mvvm, EF Core 8 SQLite, OxyPlot.Wpf, Serilog
- **References**:
  - `温箱202605/` — React/JSX mockup of the target UI (8 screens, dark theme, role-aware guidance layer). Treated as the **UX/visual contract**.
  - `EnviroEquipmentFinalEdition_202604/` — legacy Qt/C++ implementation. Treated as the **feature contract** (what behaviors to inherit).
  - `docs/superpowers/specs/2026-05-14-legacy-protocol-coverage-design.md` — Wave 1/2/3 gap-coverage spec for the Core layer; runs in parallel to Phase 2.
- **Out of scope**: real Schneider PLC field verification (no hardware available), production deployment automation, mobile/web variant, multi-tenant cloud.

---

## 1. Context

The current `SiemensS7Demo` repository is a headless `.NET 8` console MVP exposing a PLC-communication kernel (S7 + Modbus TCP via `IS7Adapter`, polling/write services, in-memory adapter). Wave 1/2/3 plans extend this kernel to absorb the legacy `addressProtocol/*.xml` configuration (9 gaps, 7 device configs).

Phase 2 builds the WPF desktop client **on top of** that kernel, re-creating the operational surface from the legacy 202604 application while adopting the visual / interaction language from the 202605 React design. The new client targets Windows 10/11 desktop deployment in environmental-reliability labs running multiple chambers concurrently.

---

## 2. Goals & Non-Goals

**Goals**
- Ship a runnable WPF desktop client that renders the 202605 visual language with ≤5% color/typography delta on key design tokens.
- Re-implement the four functional packages from 202604 as independently shippable C# layers (see §4).
- Keep the existing `SiemensS7Demo.Core` (renamed) untouched at API level; Phase 2 layers consume it as a referenced project.
- All work covered by xunit tests; CI green without real PLC hardware. Real-PLC smoke verification done manually on the S7-200 SMART available on-site.
- Each package = its own implementation plan with sequenced milestones and acceptance commands.

**Non-Goals**
- 1:1 source-port of legacy Qt code. We treat 202604 as a behavior reference, not as code to translate line-by-line.
- Cross-platform (Linux/Mac) builds; Avalonia was considered and rejected for this phase.
- Real Schneider hardware loop testing (only Modbus loopback server).
- Multi-window dock layouts beyond what 202605 mock prescribes.
- Migration tooling for legacy SQLite/program files (out of scope; may be a separate plan).

---

## 3. Architecture

### 3.1 Project layout (after Phase 2)

```
EnviroEquipmentFinalEdition.sln
├── src/
│   ├── SiemensS7Demo.Core/          (existing SiemensS7Demo → renamed)
│   │   ├── Drivers/                  Snap7, Modbus, InMemory adapters
│   │   ├── Models/                   TagDefinition, TagValue, PlcConnectionOptions
│   │   ├── Services/                 Polling, Write, Loader, Validation
│   │   └── Config/                   sample protocol XML
│   ├── SiemensS7Demo.Domain/         [NEW] pure domain models
│   ├── SiemensS7Demo.Persistence/    [NEW] EF Core 8 + SQLite, migrations, repos
│   ├── SiemensS7Demo.App/            [NEW] business services / orchestration
│   ├── SiemensS7Demo.Wpf/            [NEW] net8.0-windows, UseWPF, XAML views
│   └── SiemensS7Demo.ConsoleHost/    [EXTRACT] existing Program.cs --self-test moves here
├── tests/
│   ├── EnviroEquipment.Tests/        existing Core-layer unit tests
│   ├── EnviroEquipment.App.Tests/    [NEW] service-layer unit tests
│   ├── EnviroEquipment.Wpf.Tests/    [NEW] ViewModel + interaction tests
│   └── EnviroEquipment.E2ETests/     [NEW] cross-layer scenarios on InMemoryAdapter
└── docs/superpowers/
    ├── specs/
    │   ├── 2026-05-14-legacy-protocol-coverage-design.md   (existing)
    │   └── 2026-05-15-phase2-wpf-client-design.md          (this doc)
    └── plans/
        ├── 2026-05-14-gap1-modbus-float.md                 (existing)
        ├── 2026-05-14-gap3-tag-options.md                  (existing)
        ├── 2026-05-14-gap8-snap7-batch-read.md             (existing)
        ├── 2026-05-15-phase2-pkg1-shell-overview-single.md (NEW, via writing-plans)
        ├── 2026-05-15-phase2-pkg2-alarms.md                (NEW)
        ├── 2026-05-15-phase2-pkg3-programs-history-sqlite.md (NEW)
        └── 2026-05-15-phase2-pkg4-login-lims-mqtt-ftp.md   (NEW)
```

### 3.2 Layer responsibilities

| Layer | Reference | Knows about | Doesn't know about |
|-------|-----------|-------------|--------------------|
| `Core` | existing | PLC wire protocols | UI, DB, business rules |
| `Domain` | new | Pure entities + value objects | I/O, persistence, UI |
| `Persistence` | new | `Domain`, EF Core, SQLite | UI, Core protocols |
| `App` | new | `Core`, `Domain`, `Persistence` | UI / XAML |
| `Wpf` | new | `App`, `Domain` | adapters, EF, raw SQL |
| `ConsoleHost` | extracted from existing `Program.cs` | `Core` (+ optionally `App` for self-test scenarios) | UI |

Dependency direction is strictly downward. UI → App → (Core | Persistence | Domain). No upward references.

### 3.3 DI / hosting

`App.xaml.cs` builds an `IHost` using `Microsoft.Extensions.Hosting`. Service collection wires `SiemensS7Demo.App` services + `SiemensS7Demo.Persistence` `EnviroDbContext` + per-device `SiemensS7Client` factories. Same host is reusable from `ConsoleHost` for smoke tests.

### 3.4 Theming / visual fidelity

202605 `styles.css` is the source of truth for color tokens, typography, spacing. Phase 2 mechanically translates it into a WPF `ResourceDictionary` (`Themes/Tokens.xaml`) with `Color`, `SolidColorBrush`, `FontFamily`, `Thickness` resources keyed identically to the CSS custom-property names (`--bg-1` → `BrushBg1`, `--cyan` → `BrushCyan`, etc.). A small `pwsh` script (`tools/CssToXaml.ps1`) one-shots this so future mock updates have a deterministic re-run path.

Dark mode is the default; light is reserved for Phase 3+.

---

## 4. Package Designs

### Package 1 — WPF Shell + Overview + Single Device

**Scope**: Bootstrap the WPF project, port the 202605 visual language, render the multi-device overview grid, render the single-device control screen, wire both to live `SiemensS7Client` instances via `DeviceSessionManager`.

**Maps to 202604**: `View/MainWindow/MainWindow.cpp`, `View/MainWindow/BackStageWindow/BackStageConnectDevice.cpp`, `autotemperaturecontrolWidget.cpp`, `autostandardcontrolWidget.cpp`.

**Maps to 202605**: `screens-a.jsx → ScreenAppFrame / ScreenOverview / ScreenSingle`, `components-core.jsx`.

**Key new types**
```csharp
// Domain
public sealed record DeviceId(string Value);
public sealed class Device { public DeviceId Id; public string Bay; public DeviceType Type;
                             public DeviceStatus Status; public Setpoints Setpoints;
                             public ReadingSnapshot? LastReading; }
public enum DeviceStatus { Run, Idle, Scheduled, Paused, Alarm, Offline }
public enum DeviceType  { Standard, Standard1500, LowPressure, Shock }

// App
public interface IDeviceSessionManager {
    IObservable<Device> Devices { get; }                // hot stream per device update
    Task ConnectAllAsync(CancellationToken ct);
    Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct);
}
```

**Milestones**

| # | Title | Deliverables |
|---|-------|--------------|
| M1.1 | WPF project bootstrap | `SiemensS7Demo.Wpf.csproj` (`net8.0-windows`, `UseWPF`), `App.xaml.cs` w/ `IHost`, DI wired, smoke window launches. |
| M1.2 | Theme & shell | `Themes/Tokens.xaml` from `styles.css`; `Shell.xaml` reproduces 202605 TopBar + LeftNav + content router; navigation tested. |
| M1.3 | DeviceSessionManager | `DeviceSessionManager` manages N `SiemensS7Client` lifecycles; reactive snapshot stream; ProjectConfig drives the device list. |
| M1.4 | Overview screen | `OverviewView.xaml` + `OverviewViewModel` bind to `DeviceSessionManager.Devices`; 9-card grid renders status pills, online/offline, alarm pip. |
| M1.5 | Single device screen | `SingleDeviceView.xaml` + ViewModel; PV/SV row, run/pause/stop/reset commands, segment indicator, status banner; bound to RBAC stub (always-admin until Pkg 4). |
| M1.6 | E2E smoke | E2E test: 3 InMemoryAdapter devices → cards visible → click → write SV via UI → PV trail updates. |

**Acceptance command**:
```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "Category=Pkg1"
dotnet run --project src/SiemensS7Demo.Wpf -- --headless-smoke
```
Headless-smoke runs the WPF host with `Application.Run` skipped, dispatches the E2E scenario through ViewModels + DI, exits 0 on success.

**Tests**
- `Wpf.Tests/Themes/TokensTests.cs` — every CSS custom property has a XAML brush counterpart.
- `App.Tests/DeviceSessionManagerTests.cs` — multi-device lifecycle, reconnect storm, polling cadence.
- `Wpf.Tests/ViewModels/OverviewViewModelTests.cs` — snapshot binding, alarm pip propagation.
- `Wpf.Tests/ViewModels/SingleDeviceViewModelTests.cs` — command enablement matrix, SV write happy path + cancel.
- `E2ETests/Pkg1/SmokeTests.cs` — 3-device InMemory scenario.

---

### Package 2 — Alarm Subsystem

**Scope**: Inbound alarm pipeline, current panel, history panel, popup widget for critical alarms, toast notice. Acknowledge / reset / mute flows. Persistence handled by Pkg 3's SQLite; Pkg 2 reads via injected `IAlarmRepository` and tolerates an in-memory implementation until Pkg 3 lands.

**Maps to 202604**: `AlarmCurrentWidget`, `AlarmHistoryWidget`, `AlarmNoticeWidget`, `alarmpopwidget`.

**Maps to 202605**: `screens-a.jsx → ScreenAlarm`, popup mock in `screens-b.jsx`.

**Key new types**
```csharp
// Domain
public sealed record AlarmEvent(string Id, DeviceId DeviceId, AlarmLevel Level,
                                string Code, string Message, DateTimeOffset At,
                                bool Ack, bool Reset, bool Muted);
public enum AlarmLevel { Info, Warn, Critical }

public sealed record AlarmRule(string Code, AlarmLevel Level,
                               Predicate<ReadingSnapshot> Trigger,
                               string MessageTemplate);

// App
public interface IAlarmService {
    IObservable<AlarmEvent> Stream { get; }
    Task AckAsync(string alarmId, CancellationToken ct);
    Task ResetAsync(string alarmId, CancellationToken ct);
    Task MuteAsync(string alarmId, TimeSpan window, CancellationToken ct);
}
```

**Milestones**

| # | Title | Deliverables |
|---|-------|--------------|
| M2.1 | Domain + rule engine | `AlarmEvent`, `AlarmRule`, `AlarmEvaluator` pure functions over `ReadingSnapshot`. |
| M2.2 | AlarmService | `AlarmService` subscribes `DeviceSessionManager`, runs evaluator, exposes hot observable. In-memory repo stub. |
| M2.3 | Current panel | `CurrentAlarmsView` + VM bound to `AlarmService.Stream`, ack/reset commands wired. |
| M2.4 | History panel | `HistoryAlarmsView` + VM with date / device / level filters. Reads from `IAlarmRepository`. |
| M2.5 | Popup + toast | `AlarmPopupWindow` triggers on Critical level; `AlarmToastHost` overlays non-blocking notice for Warn/Info. |

**Acceptance command**:
```pwsh
dotnet test --filter "Category=Pkg2"
dotnet run --project src/SiemensS7Demo.Wpf -- --headless-smoke=alarm
```
Headless-smoke writes an out-of-range temperature into the InMemoryAdapter, asserts a Critical AlarmEvent appears within 500ms, simulated ack updates state, popup window shows once and only once.

**Tests**
- `App.Tests/Alarms/AlarmEvaluatorTests.cs` — rule matrix.
- `App.Tests/Alarms/AlarmServiceTests.cs` — subscribe/emit/ack/reset.
- `Wpf.Tests/ViewModels/CurrentAlarmsViewModelTests.cs`, `.../HistoryAlarmsViewModelTests.cs`.
- `E2ETests/Pkg2/AlarmFlowTests.cs` — full pipeline.

---

### Package 3 — Programs + History + SQLite

**Scope**: EF Core 8 + SQLite database, program editor & execution engine, PV/SV history logging, trend plotting screen. Acts as the persistence foundation for Pkg 2 history.

**Maps to 202604**: `autotemperaturecontrolWidget`, `autostandardcontrolWidget`, `BackStagePlotting`, `VMPlotting`, `SQLITEService`.

**Maps to 202605**: `screens-b.jsx → ScreenProgramEditor`, `ScreenHistory`.

**Key new types**
```csharp
// Domain
public sealed class Program {
    public string Name { get; init; }
    public IReadOnlyList<Segment> Segments { get; init; }
}
public sealed class Segment {
    public int Index;
    public double TempSetpoint;
    public double? HumidSetpoint;
    public TimeSpan Duration;
    public SegmentMode Mode;     // Ramp, Hold
    public CycleAction? Cycle;   // null | JumpTo(targetIndex, count) | End
    public bool[] DigitalOutputs;
    public string? Note;
}

public sealed record HistoryPoint(DeviceId DeviceId, DateTimeOffset At,
                                  double? Pv, double? Sv,
                                  double? Humid, double? HumidSv);

// App
public interface IProgramExecutionService {
    Task StartAsync(DeviceId deviceId, Program program, CancellationToken ct);
    Task PauseAsync(DeviceId deviceId, CancellationToken ct);
    Task StopAsync(DeviceId deviceId, CancellationToken ct);
    IObservable<ProgramRuntimeState> State { get; }
}

public interface IHistoryWriter {
    void Enqueue(HistoryPoint point);   // backpressure: bounded channel
}
```

**Milestones**

| # | Title | Deliverables |
|---|-------|--------------|
| M3.1 | EF Core + initial migration | `EnviroDbContext`, migrations folder, `EnviroEquipment.db` template, `Program`, `Segment`, `HistoryPoint`, `AlarmEvent` tables. |
| M3.2 | Program model + repo | `IProgramRepository`, JSON+SQLite hybrid (JSON for editor draft, SQLite for committed). |
| M3.3 | Program editor screen | `ProgramEditorView` + VM, 8-row segment grid, ramp/hold toggle, JMP loop builder, validation rules. |
| M3.4 | Program execution engine | `ProgramExecutionService` state machine: idle→ramping→holding→jumping→ended. Drives `DeviceSessionManager.WriteSetpointAsync`. |
| M3.5 | HistoryWriter | Bounded channel + background flush task; adaptive sampling (1s during run, 5s during hold, 1min during idle). |
| M3.6 | Trend screen | `HistoryTrendView` + VM using `OxyPlot.Wpf`; pan/zoom/cursor readouts; device/range picker. |
| M3.7 | Acceptance smoke | E2E: load 3-segment program → execute against InMemoryAdapter → see PV curve → restart app → reload from SQLite. |

**Acceptance command**:
```pwsh
dotnet test --filter "Category=Pkg3"
dotnet run --project src/SiemensS7Demo.Wpf -- --headless-smoke=program
```
Headless-smoke loads a fixture program, executes against InMemoryAdapter for 30s, asserts ≥25 HistoryPoints persisted, asserts trend ViewModel returns a series with the expected segment-boundary inflections.

**Tests**
- `Persistence.Tests/Migrations/MigrationIdempotencyTests.cs`.
- `App.Tests/Programs/ProgramExecutionStateMachineTests.cs` — full transition table.
- `App.Tests/History/HistoryWriterBackpressureTests.cs` — overflow behavior, sample interval correctness.
- `Wpf.Tests/ViewModels/ProgramEditorViewModelTests.cs`, `.../HistoryTrendViewModelTests.cs`.
- `E2ETests/Pkg3/ProgramAndTrendTests.cs`.

---

### Package 4 — Login / RBAC + LIMS + MQTT / FTP

**Scope**: User authentication with shift selection, role-based authorization on commands, LIMS task list integration, MQTT telemetry publisher, FTP file uploader for program/data backup.

**Maps to 202604**: `LoginService`, `LoginWindow`, `BackStageLims`, `MQTTSerivce`, `FTPService`, `TCPService`.

**Maps to 202605**: `screens-a.jsx → ScreenLogin`, `screens-b.jsx → ScreenLims`, possibly `screens-c.jsx` device-config pages gated by RBAC.

**Key new types**
```csharp
// Domain
public sealed record User(string Id, string Name, Role Role, string Code, string PasswordHash);
public enum Role { Operator, Engineer, Admin }
public sealed record Shift(string Code, string Name, DateOnly Date);

// App
public interface IAuthService {
    Task<AuthResult> SignInAsync(string code, string password, Shift shift, CancellationToken ct);
    User? Current { get; }
    void SignOut();
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresRoleAttribute(Role minimum) : Attribute { public Role Minimum = minimum; }

public interface ILimsClient {
    Task<IReadOnlyList<LimsTask>> ListTasksAsync(LimsFilter filter, CancellationToken ct);
    Task UploadResultAsync(LimsTaskResult result, CancellationToken ct);
}

public interface IMqttPublisher {
    Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, MqttQos qos, CancellationToken ct);
}

public interface IFtpUploader {
    Task UploadAsync(string localPath, string remotePath, CancellationToken ct);
}
```

**Milestones**

| # | Title | Deliverables |
|---|-------|--------------|
| M4.1 | User/Role/Shift + AuthService | Domain types, `AuthService` w/ Argon2id password verification (`Konscious.Security.Cryptography`). Seed migration with admin/op/eng users. |
| M4.2 | Login screen | `LoginView` + VM reproducing 202605 3-step flow (account → password → shift); shift defaults to current local time bucket. |
| M4.3 | RBAC enforcement | `[RequiresRole]` attribute scanned by command-binder; UI disables/hides forbidden actions; integration tested across all existing commands. |
| M4.4 | LIMS protocol | Reverse-engineer `BackStageLims` (likely HTTP+JSON or SOAP; verify with `try-it` script before coding). `LimsClient` + `LimsMockServer` for tests. |
| M4.5 | LIMS task list screen | `LimsView` w/ 4 tabs (todo/running/done/cancelled), filter by device/project/status. |
| M4.6 | MQTT publisher | `MqttPublisher` using `MQTTnet`, config UI for broker/credentials/topic prefix; publishes per-device telemetry every 5s. |
| M4.7 | FTP uploader | `FtpUploader` using `FluentFTP`, on-demand + scheduled backup of program JSON + day-window SQLite snapshots. |
| M4.8 | Acceptance smoke | E2E: login as Operator → device-config command hidden; login as Admin → command visible; LIMS mock returns 3 tasks → list renders; MQTT broker (Mosquitto-in-docker) receives 1+ telemetry message; FTP uploads a fixture file. |

**Acceptance command**:
```pwsh
dotnet test --filter "Category=Pkg4"
docker compose -f tests/EnviroEquipment.E2ETests/Pkg4/compose.yml up -d
dotnet run --project src/SiemensS7Demo.Wpf -- --headless-smoke=auth
docker compose -f tests/EnviroEquipment.E2ETests/Pkg4/compose.yml down
```

**Tests**
- `App.Tests/Auth/AuthServiceTests.cs` — password hashing, shift selection, lockout.
- `App.Tests/Auth/RbacInterceptorTests.cs` — attribute scanning, denial path.
- `App.Tests/Lims/LimsClientTests.cs` against `LimsMockServer`.
- `App.Tests/Mqtt/MqttPublisherTests.cs` against an embedded MQTT broker.
- `App.Tests/Ftp/FtpUploaderTests.cs` against an embedded FTP server.
- `E2ETests/Pkg4/LoginAndLimsTests.cs`, `.../TelemetryUplinkTests.cs`.

---

## 5. Cross-cutting Testing Strategy

| Layer | Test project | Strategy |
|-------|-------------|----------|
| Core | `EnviroEquipment.Tests` (existing) | xunit + FluentAssertions, no real PLC; loopback servers. |
| Domain | folded into App.Tests | pure-function tests, table-driven. |
| Persistence | `EnviroEquipment.Persistence.Tests` (folded into App.Tests for now) | In-memory SQLite per test class via `EnviroDbContextFactory.CreateInMemory()`. |
| App | `EnviroEquipment.App.Tests` | Each service in isolation, with fakes for `Core` / `Persistence` boundaries. |
| Wpf VMs | `EnviroEquipment.Wpf.Tests` | ViewModel-only; XAML rendering not exercised here. |
| E2E | `EnviroEquipment.E2ETests` | Spin up the WPF DI host (no UI), inject InMemoryAdapter, drive ViewModels end-to-end. Tagged `[Trait("Category", "PkgN")]`. |

`dotnet test EnviroEquipmentFinalEdition.sln` is the single CI command. Total runtime target ≤90s.

Real-hardware smoke (S7-200 SMART) lives in `tests/EnviroEquipment.Hardware/` (manual, `[Trait("Category", "Hardware")]`, excluded from CI). Runs once per package release.

---

## 6. Team & Workflow

Plans run in parallel against `main` per the wave layout below. Every plan = one feature branch = one PR. Branch naming: `feat/phase2-pkgN-<slug>`.

| Wave | Agents | Tasks |
|------|--------|-------|
| Carry-over | `setup-engineer`, `modbus-engineer`, `models-engineer`, `snap7-engineer` | Wave 1 (gap1/3/8) — already planned in legacy-protocol-coverage spec. |
| Phase 2 — A | `shell-engineer` | Pkg 1 M1.1–M1.6. |
| Phase 2 — B | `alarm-engineer` | Pkg 2 (starts after Pkg 1 M1.3 lands). |
| Phase 2 — B | `program-history-engineer` | Pkg 3 (starts after Pkg 1 M1.3 lands; M3.1 unblocks Pkg 2 M2.4 persistence). |
| Phase 2 — C | `integrations-engineer` | Pkg 4 (independent of Pkg 2/3, can run in parallel with them). |

Lead reviews every PR with the `code-reviewer` agent before merging. Each PR shows the red→green TDD sequence per milestone.

---

## 7. Milestone Calendar

```
Week  1  2  3  4  5  6  7  8  9 10 11 12
Wave1 [█gap1│█gap3│█gap8]
Wave2          [█gap5│█gap6]
Wave3                  [█gap9]
Pkg1  [█M1.1│█M1.2│█M1.3│█M1.4│█M1.5│█M1.6]
Pkg2          [█M2.1│█M2.2│█M2.3│█M2.4│█M2.5]
Pkg3          [█M3.1│█M3.2│█M3.3│█M3.4│█M3.5│█M3.6│█M3.7]
Pkg4              [█M4.1│█M4.2│█M4.3│█M4.4│█M4.5│█M4.6│█M4.7│█M4.8]
```

Gating rules:
- Pkg 2/3 cannot start until **Pkg 1 M1.3 (DeviceSessionManager)** is on `main` — they need the device snapshot stream.
- Pkg 2 M2.4 (history filter) depends on **Pkg 3 M3.1 (initial migration)** for the `AlarmEvent` table. Until then Pkg 2 uses an in-memory repo.
- Pkg 4 has no upstream Phase 2 dependency, so its `integrations-engineer` can take the slot freed when Pkg 1 ships.

Phase 2 declared done when all four `Pkg N acceptance` commands exit 0 against `main` and the S7-200 SMART hardware smoke passes for Pkg 1 + Pkg 3 (the data-path packages). Pkg 2 alarms ride on the Pkg 1/3 smoke runs; Pkg 4 has no PLC dependency.

---

## 8. Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| 202605 React → WPF visual fidelity drift | `tools/CssToXaml.ps1` deterministic regen; `TokensTests` asserts brush/font parity; design-token diff posted in each Pkg 1 PR. |
| EF Core SQLite migration drift between dev machines | Migrations committed; `MigrationIdempotencyTests` runs the full chain forward+backward against a temp DB. |
| `DeviceSessionManager` becomes a god-object as packages stack on it | Define IObservable / write-command surface in Pkg 1, freeze API, additions go through PR review with `architect` agent. |
| Program execution drift vs real chamber inertia | State machine accepts a strategy interface (default = naive setpoint write); real-hardware smoke will drive refinement on S7-200 SMART. |
| Alarm pipeline floods UI on connection storm | `AlarmService` deduplicates `(DeviceId, Code)` within configurable debounce window; popup limited to 1 active at a time. |
| 202604 LIMS protocol undocumented | Reverse-engineer pass = 1-day spike before Pkg 4 M4.4 begins; if the spike shows the protocol is proprietary/unsafe to reimplement, Pkg 4 LIMS scope is cut to "task list display from manual JSON export". |
| MQTT credentials in plaintext config | Pkg 4 M4.6 includes DPAPI-protected config storage on Windows; tests assert plaintext doesn't leak to logs. |
| WPF UI tests historically flaky | Keep UI tests at ViewModel level; only `--headless-smoke` exercises the host; full XAML rendering tests deferred to a separate Phase 3 plan. |

---

## 9. Open Questions

- **MVVM toolkit confirmation**: `CommunityToolkit.Mvvm` chosen for source-gen + zero runtime overhead. Alternative would be Prism (heavier, but provides modular regions). Default proceeds with CommunityToolkit; revisit if Pkg 4 LIMS introduces a need for region-style composition.
- **Chart library confirmation**: `OxyPlot.Wpf` chosen for maturity + low memory footprint on long traces. Alternative is `LiveCharts2` (more animations, larger working set). Default proceeds with OxyPlot.
- **i18n scope**: 202605 is Chinese-only. Spec assumes Chinese-only; if multi-language is required, a `Resources/zh-CN.resx` + `Resources/en-US.resx` pair would be added in Pkg 1 M1.2 with negligible cost.
- **CenterWindow / FileManager / TaskSerivce inheritance**: deferred. These 202604 modules likely map to UX areas not in the 202605 mock; revisit after Phase 2 ships.

---

## 10. Acceptance for the Spec Itself

This design is considered approved when:

1. All four package sections (§4) survive user review without scope changes.
2. The milestone calendar in §7 fits the user's release expectations.
3. The risks in §8 are accepted as known.

On approval, four implementation plans are written via the `superpowers:writing-plans` skill, one per package, into `docs/superpowers/plans/2026-05-15-phase2-pkgN-*.md`. Each plan contains the per-milestone tasks with file paths, code skeletons, and red→green test sequences in the same shape as the existing Wave 1 plans.
