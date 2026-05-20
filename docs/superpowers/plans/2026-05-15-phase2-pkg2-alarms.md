# Phase 2 Package 2 — Alarm Subsystem Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the alarm pipeline for the WPF client: a pure rule evaluator over `ReadingSnapshot`, a hot-observable `AlarmService` with debounce/dedup, the Current and History panels with their ViewModels, a single-instance Critical popup window, and a non-blocking Warn/Info toast host. Acceptance smoke writes an out-of-range temperature into the `InMemoryAdapter`, asserts a `Critical AlarmEvent` appears within 500ms, simulates ack updates, and proves the popup shows exactly once even when storms arrive.

**Architecture:** New domain types (`AlarmEvent`, `AlarmLevel`, `AlarmRule`, `AlarmFilter`) live in `SiemensS7Demo.Domain`. `AlarmEvaluator` is a pure function `(ReadingSnapshot, IReadOnlyList<AlarmRule>) -> IEnumerable<AlarmEvent>`. `AlarmService` (in `SiemensS7Demo.App`) subscribes to `IDeviceSessionManager.Devices`, feeds each new `ReadingSnapshot` through the evaluator, applies a `(DeviceId, Code)` debounce window (default 5s), and pushes results onto a `Subject<AlarmEvent>`. `IAlarmRepository` defines insert/query/ack; `InMemoryAlarmRepository` ships in this package, and Pkg 3 will later swap in a SQLite-backed implementation. WPF binding: `CurrentAlarmsView` and `HistoryAlarmsView` are XAML user controls wired into the M1.2 content router; their ViewModels live in `SiemensS7Demo.Wpf/ViewModels/Alarms/`. `AlarmPopupWindow` is a `Window` with a single-instance gate (lock + boolean + queue) inside `AlarmPopupCoordinator`; the coordinator subscribes to the same `IAlarmService.Stream` filtered to `Critical`. `AlarmToastHost` is an `ItemsControl` overlay added to `Shell.xaml` consuming `Warn`/`Info` events with a fade-out timer.

**Tech Stack:** C# .NET 8 (`net8.0` for `Domain`/`App`/Tests, `net8.0-windows` for `Wpf`), CommunityToolkit.Mvvm 8.x (source-generated `ObservableObject` / `RelayCommand`), System.Reactive 6.x (Rx.NET for `IObservable<AlarmEvent>` pipeline), xunit 2.x, FluentAssertions 6.x, `Microsoft.Reactive.Testing` (`TestScheduler` for virtual-time debounce tests). WPF popup uses a `lock` object + `bool _popupOpen` + `Queue<AlarmEvent>` inside `AlarmPopupCoordinator`.

**Scope guard:** This plan covers **only Pkg 2 (M2.1–M2.5)**. It does NOT introduce SQLite (Pkg 3 M3.1). It does NOT introduce authentication/role gating for ack/reset (Pkg 4 M4.3 will retrofit `[RequiresRole]` onto the commands; for now the commands are always enabled). It does NOT touch the Pkg 1 Overview/Single Device screens — only the new Alarms content area and the `Shell` toast overlay container.

**Branch:** `feat/phase2-pkg2-alarms`
**Worktree:** `.claude/worktrees/phase2-pkg2-alarms` (create manually with `git worktree add` per the agent worktree isolation gotcha — `isolation: "worktree"` is unreliable for parallel agents)
**Base:** `main` after Pkg 1 M1.3 (`DeviceSessionManager`) has landed.

**Depends-on:** Pkg 1 M1.3 ships `SiemensS7Demo.App.IDeviceSessionManager` with `IObservable<Device> Devices`; Pkg 1 M1.2 ships the WPF `Shell` content router and `Themes/Tokens.xaml`. Pkg 1 also exposes `SiemensS7Demo.Domain.Device`, `DeviceId`, and `ReadingSnapshot`. This plan references those types without redefining them.

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `src/SiemensS7Demo.Domain/Alarms/AlarmLevel.cs` | `enum AlarmLevel { Info, Warn, Critical }` |
| Create | `src/SiemensS7Demo.Domain/Alarms/AlarmEvent.cs` | `record AlarmEvent` (Id, DeviceId, Level, Code, Message, At, Ack, Reset, Muted) |
| Create | `src/SiemensS7Demo.Domain/Alarms/AlarmRule.cs` | `record AlarmRule(Code, Level, Predicate<ReadingSnapshot>, MessageTemplate)` |
| Create | `src/SiemensS7Demo.Domain/Alarms/AlarmFilter.cs` | `record AlarmFilter(From, To, Device, Level)` |
| Create | `src/SiemensS7Demo.Domain/Alarms/AlarmEvaluator.cs` | Pure static `Evaluate(ReadingSnapshot, IReadOnlyList<AlarmRule>)` |
| Create | `src/SiemensS7Demo.App/Alarms/IAlarmService.cs` | Service contract (Stream + Ack/Reset/Mute) |
| Create | `src/SiemensS7Demo.App/Alarms/IAlarmRepository.cs` | Insert / Query / SetAck |
| Create | `src/SiemensS7Demo.App/Alarms/InMemoryAlarmRepository.cs` | Thread-safe in-memory list-backed impl |
| Create | `src/SiemensS7Demo.App/Alarms/AlarmServiceOptions.cs` | Debounce window (default 5s) + rule set |
| Create | `src/SiemensS7Demo.App/Alarms/AlarmService.cs` | Subscribes session manager, debounces, fans out |
| Modify | `src/SiemensS7Demo.App/ServiceCollectionExtensions.cs` | Register `IAlarmService`, `IAlarmRepository`, options |
| Create | `src/SiemensS7Demo.Wpf/ViewModels/Alarms/AlarmRowViewModel.cs` | Bound row representing one `AlarmEvent` (ObservableObject) |
| Create | `src/SiemensS7Demo.Wpf/ViewModels/Alarms/CurrentAlarmsViewModel.cs` | Live ack/reset/mute commands |
| Create | `src/SiemensS7Demo.Wpf/ViewModels/Alarms/HistoryAlarmsViewModel.cs` | Filter + query, observable rows |
| Create | `src/SiemensS7Demo.Wpf/Views/Alarms/CurrentAlarmsView.xaml` | Current panel UI |
| Create | `src/SiemensS7Demo.Wpf/Views/Alarms/CurrentAlarmsView.xaml.cs` | Code-behind; DataContext via DI |
| Create | `src/SiemensS7Demo.Wpf/Views/Alarms/HistoryAlarmsView.xaml` | History panel UI |
| Create | `src/SiemensS7Demo.Wpf/Views/Alarms/HistoryAlarmsView.xaml.cs` | Code-behind |
| Create | `src/SiemensS7Demo.Wpf/Views/Alarms/AlarmPopupWindow.xaml` | Critical popup XAML |
| Create | `src/SiemensS7Demo.Wpf/Views/Alarms/AlarmPopupWindow.xaml.cs` | Code-behind; raises `Dismissed` event |
| Create | `src/SiemensS7Demo.Wpf/Alarms/AlarmPopupCoordinator.cs` | Single-instance gate + queue |
| Create | `src/SiemensS7Demo.Wpf/Alarms/IAlarmPopupGate.cs` | Testable seam over `Window.ShowDialog()` |
| Create | `src/SiemensS7Demo.Wpf/Alarms/ToastNotificationViewModel.cs` | Toast item VM |
| Create | `src/SiemensS7Demo.Wpf/Alarms/AlarmToastHost.cs` | `ItemsControl` overlay with fade-out timer |
| Modify | `src/SiemensS7Demo.Wpf/Views/Shell.xaml` | Register `Alarms` route + toast host overlay |
| Modify | `src/SiemensS7Demo.Wpf/App.xaml.cs` | Register WPF coordinator/gate; wire popup on startup |
| Modify | `src/SiemensS7Demo.Wpf/Program.cs` | `--headless-smoke=alarm` switch runs the acceptance scenario |
| Create | `tests/EnviroEquipment.App.Tests/Alarms/AlarmEvaluatorTests.cs` | Pure-function rule matrix |
| Create | `tests/EnviroEquipment.App.Tests/Alarms/AlarmServiceTests.cs` | Subscribe/emit/dedup/ack/reset/mute (TestScheduler) |
| Create | `tests/EnviroEquipment.App.Tests/Alarms/InMemoryAlarmRepositoryTests.cs` | Insert/Query/SetAck filter matrix |
| Create | `tests/EnviroEquipment.Wpf.Tests/ViewModels/CurrentAlarmsViewModelTests.cs` | Live binding + commands |
| Create | `tests/EnviroEquipment.Wpf.Tests/ViewModels/HistoryAlarmsViewModelTests.cs` | Filter + reload |
| Create | `tests/EnviroEquipment.Wpf.Tests/Alarms/AlarmPopupCoordinatorTests.cs` | Single-instance + queue contract via fake `IAlarmPopupGate` |
| Create | `tests/EnviroEquipment.E2ETests/Pkg2/AlarmFlowTests.cs` | InMemoryAdapter → Critical event → ack → popup-once |

---

## Task 1 — M2.1: Domain types + AlarmEvaluator (TDD, pure functions)

**Files:** Create the five files under `src/SiemensS7Demo.Domain/Alarms/`. Create `tests/EnviroEquipment.App.Tests/Alarms/AlarmEvaluatorTests.cs`.

- [ ] **Step 1.1: Add the `Alarms` folder to the Domain project**

Verify the Domain project file references the folder layout we want (no project edit needed — `dotnet`'s default SDK-style glob covers `**/*.cs`). Confirm the project exists from Pkg 1:

```pwsh
dotnet build src/SiemensS7Demo.Domain/SiemensS7Demo.Domain.csproj
```

Expected output:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

- [ ] **Step 1.2: Write failing test for AlarmEvaluator**

Create `tests/EnviroEquipment.App.Tests/Alarms/AlarmEvaluatorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using Xunit;

namespace EnviroEquipment.App.Tests.Alarms;

[Trait("Category", "Pkg2")]
public class AlarmEvaluatorTests
{
    private static readonly DeviceId Dev = new("dev-1");
    private static readonly DateTimeOffset T = new(2026, 5, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_NoRules_ReturnsEmpty()
    {
        var snap = MakeSnapshot(pv: 25.0);
        var result = AlarmEvaluator.Evaluate(snap, Array.Empty<AlarmRule>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_SingleMatchingRule_ReturnsCriticalEvent()
    {
        var rule = new AlarmRule(
            Code: "TEMP_HIGH",
            Level: AlarmLevel.Critical,
            Trigger: s => s.Pv.HasValue && s.Pv.Value > 80.0,
            MessageTemplate: "Temperature {Pv:F1}°C exceeds 80°C limit");

        var snap = MakeSnapshot(pv: 85.5);
        var result = AlarmEvaluator.Evaluate(snap, new[] { rule }).ToList();

        result.Should().HaveCount(1);
        result[0].Code.Should().Be("TEMP_HIGH");
        result[0].Level.Should().Be(AlarmLevel.Critical);
        result[0].DeviceId.Should().Be(Dev);
        result[0].Message.Should().Contain("85.5");
        result[0].Ack.Should().BeFalse();
        result[0].Reset.Should().BeFalse();
        result[0].Muted.Should().BeFalse();
        result[0].At.Should().Be(snap.At);
        result[0].Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Evaluate_NonMatchingRule_ReturnsEmpty()
    {
        var rule = new AlarmRule(
            "TEMP_HIGH", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0,
            "ignored");

        var snap = MakeSnapshot(pv: 25.0);
        AlarmEvaluator.Evaluate(snap, new[] { rule }).Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_MultipleRules_FiresAllMatching()
    {
        var ruleHi = new AlarmRule(
            "TEMP_HIGH", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0,
            "Over {Pv}");
        var ruleHumid = new AlarmRule(
            "HUMID_HIGH", AlarmLevel.Warn,
            s => s.Humid.HasValue && s.Humid.Value > 90.0,
            "Humid {Humid}");
        var ruleOff = new AlarmRule(
            "DEVICE_OFF", AlarmLevel.Info,
            s => !s.Pv.HasValue,
            "PV missing");

        var snap = MakeSnapshot(pv: 95.0, humid: 95.0);
        var result = AlarmEvaluator.Evaluate(snap, new[] { ruleHi, ruleHumid, ruleOff }).ToList();

        result.Should().HaveCount(2);
        result.Select(e => e.Code).Should().BeEquivalentTo(new[] { "TEMP_HIGH", "HUMID_HIGH" });
    }

    [Fact]
    public void Evaluate_RuleThrows_DoesNotCorruptOthers()
    {
        var thrower = new AlarmRule(
            "BAD", AlarmLevel.Critical,
            _ => throw new InvalidOperationException("boom"),
            "won't render");
        var good = new AlarmRule(
            "GOOD", AlarmLevel.Warn,
            _ => true,
            "ok");

        var snap = MakeSnapshot(pv: 25.0);
        var result = AlarmEvaluator.Evaluate(snap, new[] { thrower, good }).ToList();

        result.Should().HaveCount(1);
        result[0].Code.Should().Be("GOOD");
    }

    [Fact]
    public void Evaluate_MessageTemplate_RendersPvAndSvAndHumid()
    {
        var rule = new AlarmRule(
            "ALL", AlarmLevel.Warn,
            _ => true,
            "PV={Pv:F2} SV={Sv:F2} H={Humid:F1}");

        var snap = new ReadingSnapshot(Dev, T, Pv: 23.45, Sv: 25.0, Humid: 60.0, HumidSv: null);
        var result = AlarmEvaluator.Evaluate(snap, new[] { rule }).Single();

        result.Message.Should().Be("PV=23.45 SV=25.00 H=60.0");
    }

    [Fact]
    public void Evaluate_MessageTemplate_MissingFieldRendersDash()
    {
        var rule = new AlarmRule(
            "ALL", AlarmLevel.Warn, _ => true,
            "PV={Pv:F1}");

        var snap = new ReadingSnapshot(Dev, T, Pv: null, Sv: null, Humid: null, HumidSv: null);
        var result = AlarmEvaluator.Evaluate(snap, new[] { rule }).Single();

        result.Message.Should().Be("PV=-");
    }

    private static ReadingSnapshot MakeSnapshot(double? pv = null, double? sv = null,
                                                double? humid = null, double? humidSv = null)
        => new(Dev, T, pv, sv, humid, humidSv);
}
```

- [ ] **Step 1.3: Run, confirm failure**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~AlarmEvaluatorTests"
```

Expected output:
```
Test Run Failed.
Total tests: 0
     Errors:  Build FAILED.
        CS0246: The type or namespace name 'AlarmEvaluator' could not be found
        CS0246: The type or namespace name 'AlarmRule' could not be found
        CS0246: The type or namespace name 'AlarmLevel' could not be found
```

- [ ] **Step 1.4: Create `AlarmLevel.cs`**

Create `src/SiemensS7Demo.Domain/Alarms/AlarmLevel.cs`:

```csharp
namespace SiemensS7Demo.Domain.Alarms;

/// <summary>
/// Severity of an <see cref="AlarmEvent"/>.
/// Info  — informational, toasted.
/// Warn  — warning, toasted, written to history.
/// Critical — blocks via modal popup, written to history.
/// </summary>
public enum AlarmLevel
{
    Info = 0,
    Warn = 1,
    Critical = 2
}
```

- [ ] **Step 1.5: Create `AlarmEvent.cs`**

Create `src/SiemensS7Demo.Domain/Alarms/AlarmEvent.cs`:

```csharp
using System;

namespace SiemensS7Demo.Domain.Alarms;

/// <summary>
/// A single alarm occurrence emitted by <c>AlarmService</c>. Identity is <see cref="Id"/>,
/// which is a stable string assigned at evaluation time. Equality is by <see cref="Id"/>.
/// </summary>
public sealed record AlarmEvent(
    string Id,
    DeviceId DeviceId,
    AlarmLevel Level,
    string Code,
    string Message,
    DateTimeOffset At,
    bool Ack,
    bool Reset,
    bool Muted);
```

- [ ] **Step 1.6: Create `AlarmRule.cs`**

Create `src/SiemensS7Demo.Domain/Alarms/AlarmRule.cs`:

```csharp
using System;

namespace SiemensS7Demo.Domain.Alarms;

/// <summary>
/// A single rule fed into <see cref="AlarmEvaluator"/>. <see cref="Trigger"/> is invoked
/// for every <see cref="ReadingSnapshot"/>. When it returns true, the evaluator emits an
/// <see cref="AlarmEvent"/> whose <see cref="AlarmEvent.Message"/> is
/// <see cref="MessageTemplate"/> with <c>{Pv}</c>, <c>{Sv}</c>, <c>{Humid}</c>,
/// <c>{HumidSv}</c> substituted from the snapshot (missing values render as <c>"-"</c>).
/// </summary>
public sealed record AlarmRule(
    string Code,
    AlarmLevel Level,
    Predicate<ReadingSnapshot> Trigger,
    string MessageTemplate);
```

- [ ] **Step 1.7: Create `AlarmFilter.cs`**

Create `src/SiemensS7Demo.Domain/Alarms/AlarmFilter.cs`:

```csharp
using System;

namespace SiemensS7Demo.Domain.Alarms;

/// <summary>
/// Query parameters for <see cref="IAlarmRepository.QueryAsync"/>. All four fields are
/// optional; <c>null</c> means "no constraint on this axis".
/// </summary>
public sealed record AlarmFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    DeviceId? Device,
    AlarmLevel? Level);
```

- [ ] **Step 1.8: Create `AlarmEvaluator.cs`**

Create `src/SiemensS7Demo.Domain/Alarms/AlarmEvaluator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SiemensS7Demo.Domain.Alarms;

/// <summary>
/// Pure-function evaluator. Stateless. Given a snapshot and a rule set it yields
/// zero or more <see cref="AlarmEvent"/>s. A misbehaving rule that throws from its
/// <see cref="AlarmRule.Trigger"/> is silently skipped so one bad rule cannot poison
/// the whole evaluation pass.
/// </summary>
public static class AlarmEvaluator
{
    private static readonly Regex Placeholder = new(
        @"\{(?<field>Pv|Sv|Humid|HumidSv)(?::(?<format>[^}]+))?\}",
        RegexOptions.Compiled);

    public static IEnumerable<AlarmEvent> Evaluate(
        ReadingSnapshot snapshot,
        IReadOnlyList<AlarmRule> rules)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (rules is null) throw new ArgumentNullException(nameof(rules));

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            bool fires;
            try
            {
                fires = rule.Trigger(snapshot);
            }
            catch
            {
                // Bad rule — skip it. Logging is the caller's job.
                continue;
            }

            if (!fires) continue;

            var message = RenderTemplate(rule.MessageTemplate, snapshot);
            yield return new AlarmEvent(
                Id: $"{snapshot.DeviceId.Value}:{rule.Code}:{snapshot.At.ToUnixTimeMilliseconds()}",
                DeviceId: snapshot.DeviceId,
                Level: rule.Level,
                Code: rule.Code,
                Message: message,
                At: snapshot.At,
                Ack: false,
                Reset: false,
                Muted: false);
        }
    }

    private static string RenderTemplate(string template, ReadingSnapshot s)
    {
        return Placeholder.Replace(template, m =>
        {
            var field = m.Groups["field"].Value;
            var format = m.Groups["format"].Success ? m.Groups["format"].Value : null;
            double? v = field switch
            {
                "Pv" => s.Pv,
                "Sv" => s.Sv,
                "Humid" => s.Humid,
                "HumidSv" => s.HumidSv,
                _ => null
            };
            if (!v.HasValue) return "-";
            return format is null
                ? v.Value.ToString(CultureInfo.InvariantCulture)
                : v.Value.ToString(format, CultureInfo.InvariantCulture);
        });
    }
}
```

- [ ] **Step 1.9: Confirm `ReadingSnapshot` shape from Pkg 1**

This plan assumes Pkg 1 defined `ReadingSnapshot` as:

```csharp
public sealed record ReadingSnapshot(
    DeviceId DeviceId,
    DateTimeOffset At,
    double? Pv,
    double? Sv,
    double? Humid,
    double? HumidSv);
```

If Pkg 1 picked a different shape (e.g. extra fields), this Task 1 still compiles because the evaluator only reads `Pv`, `Sv`, `Humid`, `HumidSv`, `At`, `DeviceId`. If those four optional fields are not present in Pkg 1's record, abort the task and reconcile the spec — DO NOT silently add them here.

- [ ] **Step 1.10: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~AlarmEvaluatorTests"
```

Expected output:
```
Test Run Successful.
Total tests: 7
     Passed: 7
```

- [ ] **Step 1.11: Verify the full Domain + App.Tests build**

```pwsh
dotnet build EnviroEquipmentFinalEdition.sln
```

Expected output:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Task 2 — M2.2: AlarmService with debounce, in-memory repo, options

**Files:** Create `src/SiemensS7Demo.App/Alarms/IAlarmService.cs`, `IAlarmRepository.cs`, `AlarmServiceOptions.cs`, `InMemoryAlarmRepository.cs`, `AlarmService.cs`. Modify `src/SiemensS7Demo.App/ServiceCollectionExtensions.cs`. Create `tests/EnviroEquipment.App.Tests/Alarms/AlarmServiceTests.cs` and `InMemoryAlarmRepositoryTests.cs`.

- [ ] **Step 2.1: Add `System.Reactive` reference**

In `src/SiemensS7Demo.App/SiemensS7Demo.App.csproj`, inside the existing `<ItemGroup>` block holding PackageReference items (or add a new ItemGroup if none exists), add:

```xml
    <PackageReference Include="System.Reactive" Version="6.0.0" />
```

In `tests/EnviroEquipment.App.Tests/EnviroEquipment.App.Tests.csproj`, add:

```xml
    <PackageReference Include="System.Reactive" Version="6.0.0" />
    <PackageReference Include="Microsoft.Reactive.Testing" Version="6.0.0" />
```

Verify:

```pwsh
dotnet restore EnviroEquipmentFinalEdition.sln
```

Expected output ends with:
```
Restore completed
```

- [ ] **Step 2.2: Write failing tests for `InMemoryAlarmRepository`**

Create `tests/EnviroEquipment.App.Tests/Alarms/InMemoryAlarmRepositoryTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using Xunit;

namespace EnviroEquipment.App.Tests.Alarms;

[Trait("Category", "Pkg2")]
public class InMemoryAlarmRepositoryTests
{
    private static readonly DeviceId D1 = new("d1");
    private static readonly DeviceId D2 = new("d2");
    private static readonly DateTimeOffset T0 = new(2026, 5, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InsertAsync_StoresEvent_AndQueryNoFilterReturnsAll()
    {
        var repo = new InMemoryAlarmRepository();
        var e1 = MakeEvent("a", D1, AlarmLevel.Warn, T0);
        var e2 = MakeEvent("b", D2, AlarmLevel.Critical, T0.AddSeconds(1));

        await repo.InsertAsync(e1, CancellationToken.None);
        await repo.InsertAsync(e2, CancellationToken.None);

        var all = await repo.QueryAsync(new AlarmFilter(null, null, null, null), CancellationToken.None);
        all.Should().HaveCount(2);
        all.Select(e => e.Id).Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public async Task QueryAsync_FiltersByDevice()
    {
        var repo = new InMemoryAlarmRepository();
        await repo.InsertAsync(MakeEvent("a", D1, AlarmLevel.Warn, T0), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("b", D2, AlarmLevel.Warn, T0), CancellationToken.None);

        var only1 = await repo.QueryAsync(new AlarmFilter(null, null, D1, null), CancellationToken.None);
        only1.Should().HaveCount(1);
        only1[0].DeviceId.Should().Be(D1);
    }

    [Fact]
    public async Task QueryAsync_FiltersByLevel()
    {
        var repo = new InMemoryAlarmRepository();
        await repo.InsertAsync(MakeEvent("a", D1, AlarmLevel.Info, T0), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("b", D1, AlarmLevel.Warn, T0), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("c", D1, AlarmLevel.Critical, T0), CancellationToken.None);

        var crit = await repo.QueryAsync(new AlarmFilter(null, null, null, AlarmLevel.Critical), CancellationToken.None);
        crit.Should().HaveCount(1);
        crit[0].Code.Should().Be("c");
    }

    [Fact]
    public async Task QueryAsync_FiltersByDateRange_InclusiveBounds()
    {
        var repo = new InMemoryAlarmRepository();
        await repo.InsertAsync(MakeEvent("a", D1, AlarmLevel.Warn, T0), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("b", D1, AlarmLevel.Warn, T0.AddMinutes(5)), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("c", D1, AlarmLevel.Warn, T0.AddMinutes(10)), CancellationToken.None);

        var mid = await repo.QueryAsync(
            new AlarmFilter(T0.AddMinutes(1), T0.AddMinutes(9), null, null),
            CancellationToken.None);

        mid.Should().HaveCount(1);
        mid[0].Code.Should().Be("b");
    }

    [Fact]
    public async Task SetAckAsync_SetsAckFlagAndPreservesOtherFields()
    {
        var repo = new InMemoryAlarmRepository();
        var original = MakeEvent("a", D1, AlarmLevel.Critical, T0);
        await repo.InsertAsync(original, CancellationToken.None);

        var ackAt = T0.AddMinutes(2);
        await repo.SetAckAsync("a", ackAt, CancellationToken.None);

        var all = await repo.QueryAsync(new AlarmFilter(null, null, null, null), CancellationToken.None);
        all.Should().HaveCount(1);
        all[0].Ack.Should().BeTrue();
        all[0].Code.Should().Be("a");
        all[0].DeviceId.Should().Be(D1);
    }

    [Fact]
    public async Task SetAckAsync_UnknownId_DoesNotThrow_AndChangesNothing()
    {
        var repo = new InMemoryAlarmRepository();
        await repo.InsertAsync(MakeEvent("a", D1, AlarmLevel.Critical, T0), CancellationToken.None);
        await repo.SetAckAsync("zzz", T0, CancellationToken.None);

        var all = await repo.QueryAsync(new AlarmFilter(null, null, null, null), CancellationToken.None);
        all[0].Ack.Should().BeFalse();
    }

    [Fact]
    public async Task QueryAsync_OrdersByAtDescending()
    {
        var repo = new InMemoryAlarmRepository();
        await repo.InsertAsync(MakeEvent("oldest", D1, AlarmLevel.Warn, T0), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("newest", D1, AlarmLevel.Warn, T0.AddMinutes(10)), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("middle", D1, AlarmLevel.Warn, T0.AddMinutes(5)), CancellationToken.None);

        var all = await repo.QueryAsync(new AlarmFilter(null, null, null, null), CancellationToken.None);
        all.Select(e => e.Code).Should().Equal("newest", "middle", "oldest");
    }

    private static AlarmEvent MakeEvent(string id, DeviceId dev, AlarmLevel level, DateTimeOffset at)
        => new(id, dev, level, Code: id, Message: $"msg-{id}", At: at,
               Ack: false, Reset: false, Muted: false);
}
```

- [ ] **Step 2.3: Create `IAlarmRepository.cs`**

Create `src/SiemensS7Demo.App/Alarms/IAlarmRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.App.Alarms;

/// <summary>
/// Persistence boundary for alarm history. Pkg 2 ships an in-memory implementation;
/// Pkg 3 M3.1 will introduce a SQLite-backed implementation against the same contract.
/// </summary>
public interface IAlarmRepository
{
    Task InsertAsync(AlarmEvent e, CancellationToken ct);
    Task<IReadOnlyList<AlarmEvent>> QueryAsync(AlarmFilter f, CancellationToken ct);
    Task SetAckAsync(string id, DateTimeOffset at, CancellationToken ct);
}
```

- [ ] **Step 2.4: Create `InMemoryAlarmRepository.cs`**

Create `src/SiemensS7Demo.App/Alarms/InMemoryAlarmRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.App.Alarms;

/// <summary>
/// Thread-safe list-backed repository used by Pkg 2 until Pkg 3 SQLite ships.
/// All operations are O(n) over the in-memory list — acceptable for the
/// short-lived alarm-history use case (typically &lt;10k rows per session).
/// </summary>
public sealed class InMemoryAlarmRepository : IAlarmRepository
{
    private readonly object _lock = new();
    private readonly List<AlarmEvent> _events = new();

    public Task InsertAsync(AlarmEvent e, CancellationToken ct)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));
        lock (_lock)
        {
            _events.Add(e);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AlarmEvent>> QueryAsync(AlarmFilter f, CancellationToken ct)
    {
        if (f is null) throw new ArgumentNullException(nameof(f));
        IReadOnlyList<AlarmEvent> result;
        lock (_lock)
        {
            result = _events
                .Where(e => !f.From.HasValue || e.At >= f.From.Value)
                .Where(e => !f.To.HasValue || e.At <= f.To.Value)
                .Where(e => f.Device is null || e.DeviceId == f.Device)
                .Where(e => !f.Level.HasValue || e.Level == f.Level.Value)
                .OrderByDescending(e => e.At)
                .ToList();
        }
        return Task.FromResult(result);
    }

    public Task SetAckAsync(string id, DateTimeOffset at, CancellationToken ct)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));
        lock (_lock)
        {
            for (var i = 0; i < _events.Count; i++)
            {
                if (_events[i].Id == id)
                {
                    _events[i] = _events[i] with { Ack = true };
                    break;
                }
            }
        }
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2.5: Run repo tests, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~InMemoryAlarmRepositoryTests"
```

Expected output:
```
Test Run Successful.
Total tests: 7
     Passed: 7
```

- [ ] **Step 2.6: Write failing tests for `AlarmService` (debounce, ack/reset/mute)**

Create `tests/EnviroEquipment.App.Tests/Alarms/AlarmServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Reactive.Testing;
using SiemensS7Demo.App;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using Xunit;

namespace EnviroEquipment.App.Tests.Alarms;

[Trait("Category", "Pkg2")]
public class AlarmServiceTests
{
    private static readonly DeviceId D1 = new("d1");
    private static readonly DeviceId D2 = new("d2");

    [Fact]
    public async Task Stream_EmitsCriticalEvent_OnRuleMatch()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0,
            "PV={Pv:F1}");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromSeconds(5) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 90.0, atSeconds: 0));

        await WaitFor(() => received.Count >= 1, TimeSpan.FromSeconds(1));
        received.Should().HaveCount(1);
        received[0].Level.Should().Be(AlarmLevel.Critical);
        received[0].Code.Should().Be("HOT");
        received[0].DeviceId.Should().Be(D1);
    }

    [Fact]
    public async Task Stream_DebouncesIdenticalDeviceCode_WithinWindow()
    {
        // Two snapshots 1s apart firing the same rule on the same device — only one emits.
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0,
            "PV={Pv:F1}");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromSeconds(5) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 90.0, atSeconds: 0));
        fakeMgr.Emit(MakeDevice(D1, pv: 91.0, atSeconds: 1));
        fakeMgr.Emit(MakeDevice(D1, pv: 92.0, atSeconds: 2));

        await WaitFor(() => received.Count >= 1, TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        received.Should().HaveCount(1, because: "the (D1, HOT) pair is debounced inside the 5s window");
    }

    [Fact]
    public async Task Stream_DebounceDoesNotSuppressDifferentDevice()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0, "PV={Pv}");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromSeconds(5) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 90.0, atSeconds: 0));
        fakeMgr.Emit(MakeDevice(D2, pv: 90.0, atSeconds: 0));

        await WaitFor(() => received.Count >= 2, TimeSpan.FromSeconds(1));
        received.Should().HaveCount(2);
        received.Select(e => e.DeviceId).Should().BeEquivalentTo(new[] { D1, D2 });
    }

    [Fact]
    public async Task Stream_DebounceDoesNotSuppressDifferentCode()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var hot = new AlarmRule("HOT", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0, "hot");
        var humid = new AlarmRule("HUMID", AlarmLevel.Warn,
            s => s.Humid.HasValue && s.Humid.Value > 70.0, "humid");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { hot, humid }, DebounceWindow = TimeSpan.FromSeconds(5) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 90.0, humid: 90.0, atSeconds: 0));

        await WaitFor(() => received.Count >= 2, TimeSpan.FromSeconds(1));
        received.Should().HaveCount(2);
        received.Select(e => e.Code).Should().BeEquivalentTo(new[] { "HOT", "HUMID" });
    }

    [Fact]
    public async Task Stream_AfterDebounceWindowElapses_RefiresPair()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0, "PV={Pv}");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromMilliseconds(100) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 90.0, atSeconds: 0));
        await Task.Delay(150);
        fakeMgr.Emit(MakeDevice(D1, pv: 91.0, atSeconds: 1));

        await WaitFor(() => received.Count >= 2, TimeSpan.FromSeconds(1));
        received.Should().HaveCount(2);
    }

    [Fact]
    public async Task Insert_IsWrittenToRepository()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical,
            _ => true, "msg");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromSeconds(5) });

        fakeMgr.Emit(MakeDevice(D1, pv: 25.0, atSeconds: 0));
        await Task.Delay(100);

        var all = await repo.QueryAsync(new AlarmFilter(null, null, null, null), CancellationToken.None);
        all.Should().HaveCount(1);
    }

    [Fact]
    public async Task AckAsync_SetsAckFlagInRepo()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical, _ => true, "msg");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromSeconds(5) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 25.0, atSeconds: 0));
        await WaitFor(() => received.Count >= 1, TimeSpan.FromSeconds(1));

        await svc.AckAsync(received[0].Id, CancellationToken.None);

        var all = await repo.QueryAsync(new AlarmFilter(null, null, null, null), CancellationToken.None);
        all[0].Ack.Should().BeTrue();
    }

    [Fact]
    public async Task ResetAsync_EmitsResetVariantOnStream()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical, _ => true, "msg");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromSeconds(5) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 25.0, atSeconds: 0));
        await WaitFor(() => received.Count >= 1, TimeSpan.FromSeconds(1));

        await svc.ResetAsync(received[0].Id, CancellationToken.None);

        await WaitFor(() => received.Count >= 2, TimeSpan.FromSeconds(1));
        received[1].Reset.Should().BeTrue();
        received[1].Id.Should().Be(received[0].Id);
    }

    [Fact]
    public async Task MuteAsync_SuppressesSubsequentEventsForWindow()
    {
        var fakeMgr = new FakeDeviceSessionManager();
        var repo = new InMemoryAlarmRepository();
        var rule = new AlarmRule("HOT", AlarmLevel.Critical, _ => true, "msg");

        using var svc = new AlarmService(
            fakeMgr, repo,
            new AlarmServiceOptions { Rules = new[] { rule }, DebounceWindow = TimeSpan.FromMilliseconds(50) });

        var received = new List<AlarmEvent>();
        using var sub = svc.Stream.Subscribe(received.Add);

        fakeMgr.Emit(MakeDevice(D1, pv: 25.0, atSeconds: 0));
        await WaitFor(() => received.Count >= 1, TimeSpan.FromSeconds(1));
        await svc.MuteAsync(received[0].Id, TimeSpan.FromMilliseconds(500), CancellationToken.None);

        await Task.Delay(100);
        fakeMgr.Emit(MakeDevice(D1, pv: 26.0, atSeconds: 1));
        await Task.Delay(100);
        received.Should().HaveCount(1, because: "the (D1, HOT) pair is muted for 500ms");

        await Task.Delay(500);
        fakeMgr.Emit(MakeDevice(D1, pv: 27.0, atSeconds: 2));
        await WaitFor(() => received.Count >= 2, TimeSpan.FromSeconds(1));
        received.Should().HaveCount(2);
    }

    private static Device MakeDevice(DeviceId id, double? pv = null, double? humid = null, int atSeconds = 0)
    {
        var device = new Device { Id = id, Bay = "Bay-1", Type = DeviceType.Standard, Status = DeviceStatus.Run };
        var snap = new ReadingSnapshot(
            id,
            new DateTimeOffset(2026, 5, 15, 12, 0, atSeconds, TimeSpan.Zero),
            Pv: pv, Sv: null, Humid: humid, HumidSv: null);
        device.LastReading = snap;
        return device;
    }

    private static async Task WaitFor(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout)
                throw new TimeoutException($"Predicate not satisfied within {timeout}");
            await Task.Delay(10);
        }
    }

    private sealed class FakeDeviceSessionManager : IDeviceSessionManager
    {
        private readonly Subject<Device> _subject = new();
        public IObservable<Device> Devices => _subject;
        public Task ConnectAllAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct)
            => Task.FromResult(new DeviceWriteResult(true, null));
        public void Emit(Device d) => _subject.OnNext(d);
    }
}
```

- [ ] **Step 2.7: Create `AlarmServiceOptions.cs`**

Create `src/SiemensS7Demo.App/Alarms/AlarmServiceOptions.cs`:

```csharp
using System;
using System.Collections.Generic;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.App.Alarms;

/// <summary>
/// Configuration for <see cref="AlarmService"/>. <see cref="DebounceWindow"/> defaults to
/// 5 seconds — within this window a repeat (DeviceId, Code) pair is suppressed.
/// <see cref="Rules"/> defaults to an empty array, meaning no rules fire until rules
/// are registered via DI configuration.
/// </summary>
public sealed class AlarmServiceOptions
{
    public TimeSpan DebounceWindow { get; init; } = TimeSpan.FromSeconds(5);
    public IReadOnlyList<AlarmRule> Rules { get; init; } = Array.Empty<AlarmRule>();
}
```

- [ ] **Step 2.8: Create `IAlarmService.cs`**

Create `src/SiemensS7Demo.App/Alarms/IAlarmService.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.App.Alarms;

/// <summary>
/// Hot-observable alarm pipeline. Subscribers receive a fresh event for every
/// rule match that survives debounce + mute. <see cref="AckAsync"/> sets the ack
/// flag in the repository; <see cref="ResetAsync"/> emits a derivative event
/// with <c>Reset=true</c> so UI rows can be cleared; <see cref="MuteAsync"/>
/// suppresses the (DeviceId, Code) pair for the given window.
/// </summary>
public interface IAlarmService
{
    IObservable<AlarmEvent> Stream { get; }
    Task AckAsync(string alarmId, CancellationToken ct);
    Task ResetAsync(string alarmId, CancellationToken ct);
    Task MuteAsync(string alarmId, TimeSpan window, CancellationToken ct);
}
```

- [ ] **Step 2.9: Create `AlarmService.cs`**

Create `src/SiemensS7Demo.App/Alarms/AlarmService.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.App.Alarms;

/// <summary>
/// Subscribes to <see cref="IDeviceSessionManager.Devices"/>, evaluates each
/// snapshot through the configured <see cref="AlarmRule"/> set, and pushes the
/// resulting events onto a <see cref="Subject{T}"/>. Maintains per-(DeviceId,Code)
/// debounce timestamps and a mute table.
/// </summary>
public sealed class AlarmService : IAlarmService, IDisposable
{
    private readonly IAlarmRepository _repo;
    private readonly AlarmServiceOptions _options;
    private readonly Subject<AlarmEvent> _subject = new();
    private readonly IDisposable _subscription;

    private readonly ConcurrentDictionary<(string Device, string Code), DateTimeOffset> _lastEmit = new();
    private readonly ConcurrentDictionary<(string Device, string Code), DateTimeOffset> _mutedUntil = new();
    private readonly ConcurrentDictionary<string, AlarmEvent> _byId = new();

    public AlarmService(IDeviceSessionManager sessions, IAlarmRepository repo, AlarmServiceOptions options)
    {
        if (sessions is null) throw new ArgumentNullException(nameof(sessions));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _subscription = sessions.Devices
            .Where(d => d.LastReading is not null)
            .Subscribe(OnDevice);
    }

    public IObservable<AlarmEvent> Stream => _subject;

    public async Task AckAsync(string alarmId, CancellationToken ct)
    {
        if (alarmId is null) throw new ArgumentNullException(nameof(alarmId));
        await _repo.SetAckAsync(alarmId, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);

        if (_byId.TryGetValue(alarmId, out var existing))
        {
            var updated = existing with { Ack = true };
            _byId[alarmId] = updated;
            _subject.OnNext(updated);
        }
    }

    public Task ResetAsync(string alarmId, CancellationToken ct)
    {
        if (alarmId is null) throw new ArgumentNullException(nameof(alarmId));
        if (_byId.TryGetValue(alarmId, out var existing))
        {
            var updated = existing with { Reset = true };
            _byId[alarmId] = updated;
            _subject.OnNext(updated);
        }
        return Task.CompletedTask;
    }

    public Task MuteAsync(string alarmId, TimeSpan window, CancellationToken ct)
    {
        if (alarmId is null) throw new ArgumentNullException(nameof(alarmId));
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));

        if (_byId.TryGetValue(alarmId, out var existing))
        {
            var key = (existing.DeviceId.Value, existing.Code);
            _mutedUntil[key] = DateTimeOffset.UtcNow + window;
            var updated = existing with { Muted = true };
            _byId[alarmId] = updated;
            _subject.OnNext(updated);
        }
        return Task.CompletedTask;
    }

    private void OnDevice(Device device)
    {
        var snap = device.LastReading;
        if (snap is null) return;

        foreach (var evt in AlarmEvaluator.Evaluate(snap, _options.Rules))
        {
            var key = (evt.DeviceId.Value, evt.Code);
            var now = DateTimeOffset.UtcNow;

            if (_mutedUntil.TryGetValue(key, out var mutedUntil) && now < mutedUntil)
            {
                continue;
            }

            if (_lastEmit.TryGetValue(key, out var last) && now - last < _options.DebounceWindow)
            {
                continue;
            }

            _lastEmit[key] = now;
            _byId[evt.Id] = evt;

            // Fire-and-forget repository write; persistence failures must not break the stream.
            _ = _repo.InsertAsync(evt, CancellationToken.None);
            _subject.OnNext(evt);
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _subject.OnCompleted();
        _subject.Dispose();
    }
}
```

- [ ] **Step 2.10: Run, confirm failure**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~AlarmServiceTests"
```

Expected output (first run before everything compiles together):
```
Build succeeded.
Test Run Successful.
Total tests: 9
     Passed: 9
```

If any test fails, fix the implementation. The most likely failure: `Stream_DebounceDoesNotSuppressDifferentDevice` — confirms the dedup key is `(DeviceId.Value, Code)` not just `Code`.

- [ ] **Step 2.11: Wire DI in `ServiceCollectionExtensions`**

In `src/SiemensS7Demo.App/ServiceCollectionExtensions.cs`, locate the existing `AddEnviroAppServices` (or equivalent) method. Add inside the registration block, after the `IDeviceSessionManager` registration:

```csharp
        services.AddSingleton<IAlarmRepository, InMemoryAlarmRepository>();
        services.AddSingleton(sp => new AlarmServiceOptions
        {
            DebounceWindow = TimeSpan.FromSeconds(5),
            Rules = AlarmRulesCatalog.Default
        });
        services.AddSingleton<IAlarmService, AlarmService>();
```

If `ServiceCollectionExtensions.cs` does not yet exist (Pkg 1 may not have created it), create it:

```csharp
using System;
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.App.Alarms;

namespace SiemensS7Demo.App;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEnviroAppServices(this IServiceCollection services)
    {
        // Pkg 1 services (IDeviceSessionManager etc.) are assumed registered by Pkg 1's own
        // call to AddEnviroAppServices or in App.xaml.cs ConfigureServices. Pkg 2 adds the
        // alarm pipeline:
        services.AddSingleton<IAlarmRepository, InMemoryAlarmRepository>();
        services.AddSingleton(sp => new AlarmServiceOptions
        {
            DebounceWindow = TimeSpan.FromSeconds(5),
            Rules = AlarmRulesCatalog.Default
        });
        services.AddSingleton<IAlarmService, AlarmService>();
        return services;
    }
}
```

Create the default rule catalog at `src/SiemensS7Demo.App/Alarms/AlarmRulesCatalog.cs`:

```csharp
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.App.Alarms;

/// <summary>
/// Default alarm rules shipped with Pkg 2. Conservative defaults; real chamber
/// limits will be reloaded from project config in a later pass.
/// </summary>
public static class AlarmRulesCatalog
{
    public static readonly AlarmRule[] Default =
    {
        new("TEMP_HIGH", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0,
            "Temperature {Pv:F1}°C exceeds 80°C limit"),
        new("TEMP_LOW", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value < -40.0,
            "Temperature {Pv:F1}°C below -40°C limit"),
        new("HUMID_HIGH", AlarmLevel.Warn,
            s => s.Humid.HasValue && s.Humid.Value > 95.0,
            "Humidity {Humid:F1}% exceeds 95% limit"),
        new("PV_MISSING", AlarmLevel.Info,
            s => !s.Pv.HasValue,
            "Process value not reported by device"),
    };
}
```

- [ ] **Step 2.12: Build + run full Pkg2 test suite**

```pwsh
dotnet build EnviroEquipmentFinalEdition.sln
dotnet test EnviroEquipmentFinalEdition.sln --filter "Category=Pkg2"
```

Expected output:
```
Build succeeded.
Test Run Successful.
Total tests: 16
     Passed: 16
```

(7 evaluator + 7 repo + 9 service - some service tests share with repo verification. Exact count may shift slightly; ensure no failures.)

- [ ] **Step 2.13: Commit (manual — DO NOT have agentic worker commit; plan author defers this; per instructions this plan does not include git commits, the executing agent will commit at the end of each task per its own protocol)**

NOTE: This plan describes file changes; the executing agent commits per its own protocol after each task passes.

---

## Task 3 — M2.3: Current alarms panel (XAML + ViewModel)

**Files:** Create `src/SiemensS7Demo.Wpf/ViewModels/Alarms/AlarmRowViewModel.cs`, `CurrentAlarmsViewModel.cs`. Create `src/SiemensS7Demo.Wpf/Views/Alarms/CurrentAlarmsView.xaml{,.cs}`. Modify `src/SiemensS7Demo.Wpf/Views/Shell.xaml` to add an Alarms route. Create `tests/EnviroEquipment.Wpf.Tests/ViewModels/CurrentAlarmsViewModelTests.cs`.

- [ ] **Step 3.1: Write failing tests for `CurrentAlarmsViewModel`**

Create `tests/EnviroEquipment.Wpf.Tests/ViewModels/CurrentAlarmsViewModelTests.cs`:

```csharp
using System;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using SiemensS7Demo.Wpf.ViewModels.Alarms;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg2")]
public class CurrentAlarmsViewModelTests
{
    private static readonly DeviceId D1 = new("d1");
    private static readonly DateTimeOffset T0 = new(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ctor_BindsToServiceStream_AndPopulatesRows()
    {
        var (svc, push) = FakeService();
        using var vm = new CurrentAlarmsViewModel(svc);

        push(MakeEvent("a", AlarmLevel.Critical));
        push(MakeEvent("b", AlarmLevel.Warn));

        vm.Rows.Should().HaveCount(2);
        vm.Rows[0].Id.Should().Be("a");
        vm.Rows[1].Id.Should().Be("b");
    }

    [Fact]
    public void NewEvent_WithSameId_UpdatesExistingRowInPlace()
    {
        var (svc, push) = FakeService();
        using var vm = new CurrentAlarmsViewModel(svc);

        push(MakeEvent("a", AlarmLevel.Critical) with { Ack = false });
        push(MakeEvent("a", AlarmLevel.Critical) with { Ack = true });

        vm.Rows.Should().HaveCount(1);
        vm.Rows[0].Ack.Should().BeTrue();
    }

    [Fact]
    public void ResetEvent_RemovesRow()
    {
        var (svc, push) = FakeService();
        using var vm = new CurrentAlarmsViewModel(svc);

        push(MakeEvent("a", AlarmLevel.Critical));
        push(MakeEvent("a", AlarmLevel.Critical) with { Reset = true });

        vm.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task AckCommand_CallsServiceAck()
    {
        var fake = new FakeAlarmService();
        using var vm = new CurrentAlarmsViewModel(fake);
        fake.Push(MakeEvent("a", AlarmLevel.Critical));

        var row = vm.Rows[0];
        await vm.AckCommand.ExecuteAsync(row);

        fake.AckedIds.Should().ContainSingle().Which.Should().Be("a");
    }

    [Fact]
    public async Task ResetCommand_CallsServiceReset()
    {
        var fake = new FakeAlarmService();
        using var vm = new CurrentAlarmsViewModel(fake);
        fake.Push(MakeEvent("a", AlarmLevel.Critical));

        var row = vm.Rows[0];
        await vm.ResetCommand.ExecuteAsync(row);

        fake.ResetIds.Should().ContainSingle().Which.Should().Be("a");
    }

    [Fact]
    public async Task MuteCommand_CallsServiceMuteWithDefaultWindow()
    {
        var fake = new FakeAlarmService();
        using var vm = new CurrentAlarmsViewModel(fake);
        fake.Push(MakeEvent("a", AlarmLevel.Critical));

        var row = vm.Rows[0];
        await vm.MuteCommand.ExecuteAsync(row);

        fake.MutedIds.Should().ContainSingle().Which.Should().Be("a");
        fake.MutedWindows.Should().ContainSingle().Which.Should().Be(vm.DefaultMuteWindow);
    }

    [Fact]
    public void AckedRow_RemainsInList_ButReportsAckTrue()
    {
        var (svc, push) = FakeService();
        using var vm = new CurrentAlarmsViewModel(svc);

        push(MakeEvent("a", AlarmLevel.Critical));
        push(MakeEvent("a", AlarmLevel.Critical) with { Ack = true });

        vm.Rows.Should().HaveCount(1);
        vm.Rows[0].Ack.Should().BeTrue();
    }

    private static (IAlarmService svc, Action<AlarmEvent> push) FakeService()
    {
        var subject = new Subject<AlarmEvent>();
        var fake = new FakeAlarmService();
        return (fake, fake.Push);
    }

    private static AlarmEvent MakeEvent(string id, AlarmLevel level)
        => new(id, D1, level, Code: id, Message: $"msg-{id}", At: T0,
               Ack: false, Reset: false, Muted: false);

    private sealed class FakeAlarmService : IAlarmService
    {
        private readonly Subject<AlarmEvent> _subject = new();
        public IObservable<AlarmEvent> Stream => _subject;
        public List<string> AckedIds { get; } = new();
        public List<string> ResetIds { get; } = new();
        public List<string> MutedIds { get; } = new();
        public List<TimeSpan> MutedWindows { get; } = new();

        public Task AckAsync(string alarmId, CancellationToken ct) { AckedIds.Add(alarmId); return Task.CompletedTask; }
        public Task ResetAsync(string alarmId, CancellationToken ct) { ResetIds.Add(alarmId); return Task.CompletedTask; }
        public Task MuteAsync(string alarmId, TimeSpan w, CancellationToken ct)
        { MutedIds.Add(alarmId); MutedWindows.Add(w); return Task.CompletedTask; }

        public void Push(AlarmEvent e) => _subject.OnNext(e);
    }
}
```

Also add `using System.Collections.Generic;` near the top if your test project doesn't have implicit usings for it.

- [ ] **Step 3.2: Run, confirm failure**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~CurrentAlarmsViewModelTests"
```

Expected output:
```
Build FAILED.
    CS0246: The type or namespace name 'CurrentAlarmsViewModel' could not be found
    CS0246: The type or namespace name 'AlarmRowViewModel' could not be found
```

- [ ] **Step 3.3: Create `AlarmRowViewModel.cs`**

Create `src/SiemensS7Demo.Wpf/ViewModels/Alarms/AlarmRowViewModel.cs`:

```csharp
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.ViewModels.Alarms;

/// <summary>
/// View-model row for one current alarm. Mutable observable properties so the UI
/// reflects ack/mute state changes without rebuilding the row.
/// </summary>
public sealed partial class AlarmRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool ack;

    [ObservableProperty]
    private bool muted;

    public string Id { get; }
    public DeviceId DeviceId { get; }
    public AlarmLevel Level { get; }
    public string Code { get; }
    public string Message { get; }
    public DateTimeOffset At { get; }

    public AlarmRowViewModel(AlarmEvent e)
    {
        Id = e.Id;
        DeviceId = e.DeviceId;
        Level = e.Level;
        Code = e.Code;
        Message = e.Message;
        At = e.At;
        ack = e.Ack;
        muted = e.Muted;
    }

    public void UpdateFrom(AlarmEvent e)
    {
        if (e.Id != Id) throw new InvalidOperationException("Cannot update row from different alarm id.");
        Ack = e.Ack;
        Muted = e.Muted;
    }
}
```

- [ ] **Step 3.4: Create `CurrentAlarmsViewModel.cs`**

Create `src/SiemensS7Demo.Wpf/ViewModels/Alarms/CurrentAlarmsViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.ViewModels.Alarms;

public sealed partial class CurrentAlarmsViewModel : ObservableObject, IDisposable
{
    private readonly IAlarmService _service;
    private readonly IDisposable _subscription;
    private readonly Dictionary<string, AlarmRowViewModel> _byId = new();

    public TimeSpan DefaultMuteWindow { get; } = TimeSpan.FromMinutes(15);

    public ObservableCollection<AlarmRowViewModel> Rows { get; } = new();

    public CurrentAlarmsViewModel(IAlarmService service)
        : this(service, scheduler: ImmediateScheduler.Instance) { }

    internal CurrentAlarmsViewModel(IAlarmService service, IScheduler scheduler)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _subscription = service.Stream
            .ObserveOn(scheduler)
            .Subscribe(OnEvent);
    }

    private void OnEvent(AlarmEvent e)
    {
        if (e.Reset)
        {
            if (_byId.Remove(e.Id, out var row))
            {
                Rows.Remove(row);
            }
            return;
        }

        if (_byId.TryGetValue(e.Id, out var existing))
        {
            existing.UpdateFrom(e);
            return;
        }

        var newRow = new AlarmRowViewModel(e);
        _byId[e.Id] = newRow;
        Rows.Add(newRow);
    }

    [RelayCommand]
    private async Task AckAsync(AlarmRowViewModel row)
    {
        if (row is null) return;
        await _service.AckAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResetAsync(AlarmRowViewModel row)
    {
        if (row is null) return;
        await _service.ResetAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task MuteAsync(AlarmRowViewModel row)
    {
        if (row is null) return;
        await _service.MuteAsync(row.Id, DefaultMuteWindow, CancellationToken.None).ConfigureAwait(true);
    }

    public void Dispose() => _subscription.Dispose();
}
```

- [ ] **Step 3.5: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~CurrentAlarmsViewModelTests"
```

Expected output:
```
Test Run Successful.
Total tests: 7
     Passed: 7
```

- [ ] **Step 3.6: Create `CurrentAlarmsView.xaml`**

Create `src/SiemensS7Demo.Wpf/Views/Alarms/CurrentAlarmsView.xaml`:

```xml
<UserControl x:Class="SiemensS7Demo.Wpf.Views.Alarms.CurrentAlarmsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SiemensS7Demo.Wpf.ViewModels.Alarms"
             Background="{DynamicResource BrushBg1}"
             d:DesignHeight="600" d:DesignWidth="900"
             mc:Ignorable="d"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
    <UserControl.Resources>
        <Style x:Key="LevelPillCritical" TargetType="Border">
            <Setter Property="Background" Value="{DynamicResource BrushRed}"/>
            <Setter Property="CornerRadius" Value="10"/>
            <Setter Property="Padding" Value="8,2"/>
        </Style>
        <Style x:Key="LevelPillWarn" TargetType="Border">
            <Setter Property="Background" Value="{DynamicResource BrushAmber}"/>
            <Setter Property="CornerRadius" Value="10"/>
            <Setter Property="Padding" Value="8,2"/>
        </Style>
        <Style x:Key="LevelPillInfo" TargetType="Border">
            <Setter Property="Background" Value="{DynamicResource BrushCyan}"/>
            <Setter Property="CornerRadius" Value="10"/>
            <Setter Property="Padding" Value="8,2"/>
        </Style>
    </UserControl.Resources>
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="Current Alarms"
                   FontSize="22" FontWeight="SemiBold"
                   Foreground="{DynamicResource BrushFg1}"
                   Margin="0,0,0,12"/>

        <DataGrid Grid.Row="1"
                  ItemsSource="{Binding Rows}"
                  AutoGenerateColumns="False"
                  CanUserAddRows="False"
                  CanUserDeleteRows="False"
                  HeadersVisibility="Column"
                  Background="{DynamicResource BrushBg2}"
                  RowBackground="{DynamicResource BrushBg2}"
                  AlternatingRowBackground="{DynamicResource BrushBg1}"
                  Foreground="{DynamicResource BrushFg1}"
                  GridLinesVisibility="Horizontal"
                  HorizontalGridLinesBrush="{DynamicResource BrushBg3}"
                  IsReadOnly="True">
            <DataGrid.Columns>
                <DataGridTemplateColumn Header="Level" Width="100">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <Border x:Name="Pill" Style="{StaticResource LevelPillInfo}">
                                <TextBlock Text="{Binding Level}"
                                           Foreground="White" FontWeight="SemiBold"/>
                            </Border>
                            <DataTemplate.Triggers>
                                <DataTrigger Binding="{Binding Level}" Value="Critical">
                                    <Setter TargetName="Pill" Property="Style" Value="{StaticResource LevelPillCritical}"/>
                                </DataTrigger>
                                <DataTrigger Binding="{Binding Level}" Value="Warn">
                                    <Setter TargetName="Pill" Property="Style" Value="{StaticResource LevelPillWarn}"/>
                                </DataTrigger>
                            </DataTemplate.Triggers>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
                <DataGridTextColumn Header="Device" Binding="{Binding DeviceId.Value}" Width="120"/>
                <DataGridTextColumn Header="Code" Binding="{Binding Code}" Width="160"/>
                <DataGridTextColumn Header="Message" Binding="{Binding Message}" Width="*"/>
                <DataGridTextColumn Header="At" Binding="{Binding At, StringFormat=HH:mm:ss}" Width="100"/>
                <DataGridTemplateColumn Header="Ack" Width="60">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <CheckBox IsChecked="{Binding Ack, Mode=OneWay}" IsHitTestVisible="False"/>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
                <DataGridTemplateColumn Header="Actions" Width="240">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal">
                                <Button Content="Ack" Margin="2"
                                        Command="{Binding DataContext.AckCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                        CommandParameter="{Binding}"/>
                                <Button Content="Reset" Margin="2"
                                        Command="{Binding DataContext.ResetCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                        CommandParameter="{Binding}"/>
                                <Button Content="Mute" Margin="2"
                                        Command="{Binding DataContext.MuteCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                        CommandParameter="{Binding}"/>
                            </StackPanel>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
        </DataGrid>
    </Grid>
</UserControl>
```

- [ ] **Step 3.7: Create `CurrentAlarmsView.xaml.cs`**

Create `src/SiemensS7Demo.Wpf/Views/Alarms/CurrentAlarmsView.xaml.cs`:

```csharp
using System.Windows.Controls;
using SiemensS7Demo.Wpf.ViewModels.Alarms;

namespace SiemensS7Demo.Wpf.Views.Alarms;

public partial class CurrentAlarmsView : UserControl
{
    public CurrentAlarmsView(CurrentAlarmsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
```

- [ ] **Step 3.8: Register the view + VM in DI**

In `src/SiemensS7Demo.Wpf/App.xaml.cs`, locate `ConfigureServices` (or equivalent). Inside the WPF-specific block (after the App services registration line, e.g. `services.AddEnviroAppServices();`), add:

```csharp
            services.AddTransient<SiemensS7Demo.Wpf.ViewModels.Alarms.CurrentAlarmsViewModel>();
            services.AddTransient<SiemensS7Demo.Wpf.Views.Alarms.CurrentAlarmsView>();
```

- [ ] **Step 3.9: Add Alarms route in `Shell.xaml`**

In `src/SiemensS7Demo.Wpf/Views/Shell.xaml`, locate the LeftNav `ItemsControl` (or `ListBox`) created in Pkg 1 M1.2. Add the Alarms entry alongside the Overview / Single Device entries. The exact format depends on Pkg 1's data structure; if Pkg 1 uses a `NavigationItem` collection in `ShellViewModel`, register a new item with key `"Alarms"` and route to `CurrentAlarmsView`. As a minimum:

In `src/SiemensS7Demo.Wpf/ViewModels/ShellViewModel.cs` (assumed from Pkg 1), inside the constructor or `LoadNavigation()` method, add:

```csharp
            NavigationItems.Add(new NavigationItem("Alarms", "Alarms", typeof(SiemensS7Demo.Wpf.Views.Alarms.CurrentAlarmsView)));
```

If `NavigationItem` has a different shape in Pkg 1, mirror the existing items' construction style. Don't reshape the navigation contract here.

- [ ] **Step 3.10: Build + run Pkg 2 tests so far**

```pwsh
dotnet build EnviroEquipmentFinalEdition.sln
dotnet test EnviroEquipmentFinalEdition.sln --filter "Category=Pkg2"
```

Expected output:
```
Build succeeded.
Test Run Successful.
Total tests: 23
     Passed: 23
```

(7 evaluator + 7 repo + 9 service + 7 current VM = 30 if all tagged Pkg2; if your VM tests are tagged Wpf-only adjust count. The point: zero failures.)

---

## Task 4 — M2.4: History panel (filter + query)

**Files:** Create `src/SiemensS7Demo.Wpf/ViewModels/Alarms/HistoryAlarmsViewModel.cs`. Create `src/SiemensS7Demo.Wpf/Views/Alarms/HistoryAlarmsView.xaml{,.cs}`. Create `tests/EnviroEquipment.Wpf.Tests/ViewModels/HistoryAlarmsViewModelTests.cs`. Add a second navigation entry in `ShellViewModel`.

- [ ] **Step 4.1: Write failing tests for `HistoryAlarmsViewModel`**

Create `tests/EnviroEquipment.Wpf.Tests/ViewModels/HistoryAlarmsViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using SiemensS7Demo.Wpf.ViewModels.Alarms;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg2")]
public class HistoryAlarmsViewModelTests
{
    private static readonly DeviceId D1 = new("d1");
    private static readonly DeviceId D2 = new("d2");
    private static readonly DateTimeOffset T0 = new(2026, 5, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadAsync_NoFilter_ReturnsAllRowsOrderedByAtDescending()
    {
        var repo = new InMemoryAlarmRepository();
        await Seed(repo, ("a", D1, T0), ("b", D1, T0.AddMinutes(1)), ("c", D2, T0.AddMinutes(2)));

        var vm = new HistoryAlarmsViewModel(repo);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Rows.Should().HaveCount(3);
        vm.Rows.Select(r => r.Id).Should().Equal("c", "b", "a");
    }

    [Fact]
    public async Task LoadAsync_DeviceFilter_AppliesAtRepoLayer()
    {
        var repo = new InMemoryAlarmRepository();
        await Seed(repo, ("a", D1, T0), ("b", D2, T0));

        var vm = new HistoryAlarmsViewModel(repo) { DeviceFilter = D1 };
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Rows.Should().HaveCount(1);
        vm.Rows[0].DeviceId.Should().Be(D1);
    }

    [Fact]
    public async Task LoadAsync_LevelFilter_AppliesAtRepoLayer()
    {
        var repo = new InMemoryAlarmRepository();
        await repo.InsertAsync(MakeEvent("a", D1, AlarmLevel.Info, T0), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("b", D1, AlarmLevel.Critical, T0), CancellationToken.None);

        var vm = new HistoryAlarmsViewModel(repo) { LevelFilter = AlarmLevel.Critical };
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Rows.Should().HaveCount(1);
        vm.Rows[0].Level.Should().Be(AlarmLevel.Critical);
    }

    [Fact]
    public async Task LoadAsync_DateRange_FiltersInclusively()
    {
        var repo = new InMemoryAlarmRepository();
        await Seed(repo, ("a", D1, T0), ("b", D1, T0.AddMinutes(5)), ("c", D1, T0.AddMinutes(10)));

        var vm = new HistoryAlarmsViewModel(repo)
        {
            FromFilter = T0.AddMinutes(1),
            ToFilter = T0.AddMinutes(9)
        };
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Rows.Should().HaveCount(1);
        vm.Rows[0].Code.Should().Be("b");
    }

    [Fact]
    public async Task ClearFiltersCommand_ResetsFiltersAndReloadsAll()
    {
        var repo = new InMemoryAlarmRepository();
        await Seed(repo, ("a", D1, T0), ("b", D2, T0));

        var vm = new HistoryAlarmsViewModel(repo) { DeviceFilter = D1 };
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Rows.Should().HaveCount(1);

        await vm.ClearFiltersCommand.ExecuteAsync(null);

        vm.DeviceFilter.Should().BeNull();
        vm.LevelFilter.Should().BeNull();
        vm.FromFilter.Should().BeNull();
        vm.ToFilter.Should().BeNull();
        vm.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task IsBusy_IsTrueDuringLoad_FalseAfter()
    {
        var slow = new SlowRepo();
        var vm = new HistoryAlarmsViewModel(slow);

        var pending = vm.RefreshCommand.ExecuteAsync(null);
        await Task.Delay(20);
        vm.IsBusy.Should().BeTrue();

        slow.Release();
        await pending;
        vm.IsBusy.Should().BeFalse();
    }

    private static async Task Seed(InMemoryAlarmRepository repo, params (string id, DeviceId dev, DateTimeOffset at)[] rows)
    {
        foreach (var (id, dev, at) in rows)
        {
            await repo.InsertAsync(MakeEvent(id, dev, AlarmLevel.Warn, at), CancellationToken.None);
        }
    }

    private static AlarmEvent MakeEvent(string id, DeviceId dev, AlarmLevel level, DateTimeOffset at)
        => new(id, dev, level, Code: id, Message: $"m-{id}", At: at,
               Ack: false, Reset: false, Muted: false);

    private sealed class SlowRepo : IAlarmRepository
    {
        private readonly TaskCompletionSource<bool> _gate = new();
        public void Release() => _gate.TrySetResult(true);
        public async Task<IReadOnlyList<AlarmEvent>> QueryAsync(AlarmFilter f, CancellationToken ct)
        {
            await _gate.Task;
            return Array.Empty<AlarmEvent>();
        }
        public Task InsertAsync(AlarmEvent e, CancellationToken ct) => Task.CompletedTask;
        public Task SetAckAsync(string id, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;
    }
}
```

- [ ] **Step 4.2: Run, confirm failure**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~HistoryAlarmsViewModelTests"
```

Expected output:
```
Build FAILED.
    CS0246: The type or namespace name 'HistoryAlarmsViewModel' could not be found
```

- [ ] **Step 4.3: Create `HistoryAlarmsViewModel.cs`**

Create `src/SiemensS7Demo.Wpf/ViewModels/Alarms/HistoryAlarmsViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.ViewModels.Alarms;

public sealed partial class HistoryAlarmsViewModel : ObservableObject
{
    private readonly IAlarmRepository _repo;

    [ObservableProperty]
    private DateTimeOffset? fromFilter;

    [ObservableProperty]
    private DateTimeOffset? toFilter;

    [ObservableProperty]
    private DeviceId? deviceFilter;

    [ObservableProperty]
    private AlarmLevel? levelFilter;

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<AlarmRowViewModel> Rows { get; } = new();

    public HistoryAlarmsViewModel(IAlarmRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var filter = new AlarmFilter(FromFilter, ToFilter, DeviceFilter, LevelFilter);
            var results = await _repo.QueryAsync(filter, CancellationToken.None).ConfigureAwait(true);
            Rows.Clear();
            foreach (var e in results)
            {
                Rows.Add(new AlarmRowViewModel(e));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        FromFilter = null;
        ToFilter = null;
        DeviceFilter = null;
        LevelFilter = null;
        await RefreshAsync();
    }
}
```

- [ ] **Step 4.4: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~HistoryAlarmsViewModelTests"
```

Expected output:
```
Test Run Successful.
Total tests: 6
     Passed: 6
```

- [ ] **Step 4.5: Create `HistoryAlarmsView.xaml`**

Create `src/SiemensS7Demo.Wpf/Views/Alarms/HistoryAlarmsView.xaml`:

```xml
<UserControl x:Class="SiemensS7Demo.Wpf.Views.Alarms.HistoryAlarmsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             Background="{DynamicResource BrushBg1}"
             d:DesignHeight="600" d:DesignWidth="900"
             mc:Ignorable="d">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="Alarm History"
                   FontSize="22" FontWeight="SemiBold"
                   Foreground="{DynamicResource BrushFg1}"
                   Margin="0,0,0,12"/>

        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,12">
            <TextBlock Text="From:" Foreground="{DynamicResource BrushFg2}" VerticalAlignment="Center" Margin="0,0,4,0"/>
            <DatePicker SelectedDate="{Binding FromFilter, Mode=TwoWay,
                                       Converter={StaticResource DateTimeOffsetToDateConverter}}"
                        Width="140" Margin="0,0,12,0"/>
            <TextBlock Text="To:" Foreground="{DynamicResource BrushFg2}" VerticalAlignment="Center" Margin="0,0,4,0"/>
            <DatePicker SelectedDate="{Binding ToFilter, Mode=TwoWay,
                                       Converter={StaticResource DateTimeOffsetToDateConverter}}"
                        Width="140" Margin="0,0,12,0"/>
            <TextBlock Text="Level:" Foreground="{DynamicResource BrushFg2}" VerticalAlignment="Center" Margin="0,0,4,0"/>
            <ComboBox SelectedItem="{Binding LevelFilter}" Width="120" Margin="0,0,12,0">
                <ComboBox.ItemsSource>
                    <x:Array Type="sys:Object" xmlns:sys="clr-namespace:System;assembly=mscorlib">
                        <x:Null/>
                    </x:Array>
                </ComboBox.ItemsSource>
            </ComboBox>
            <Button Content="Refresh" Command="{Binding RefreshCommand}" Margin="0,0,8,0" Padding="12,4"/>
            <Button Content="Clear" Command="{Binding ClearFiltersCommand}" Padding="12,4"/>
        </StackPanel>

        <DataGrid Grid.Row="2"
                  ItemsSource="{Binding Rows}"
                  AutoGenerateColumns="False"
                  CanUserAddRows="False"
                  IsReadOnly="True"
                  Background="{DynamicResource BrushBg2}"
                  Foreground="{DynamicResource BrushFg1}"
                  RowBackground="{DynamicResource BrushBg2}"
                  AlternatingRowBackground="{DynamicResource BrushBg1}"
                  HeadersVisibility="Column">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Level" Binding="{Binding Level}" Width="100"/>
                <DataGridTextColumn Header="Device" Binding="{Binding DeviceId.Value}" Width="120"/>
                <DataGridTextColumn Header="Code" Binding="{Binding Code}" Width="160"/>
                <DataGridTextColumn Header="Message" Binding="{Binding Message}" Width="*"/>
                <DataGridTextColumn Header="At" Binding="{Binding At, StringFormat=yyyy-MM-dd HH:mm:ss}" Width="160"/>
                <DataGridCheckBoxColumn Header="Ack" Binding="{Binding Ack}" Width="60"/>
            </DataGrid.Columns>
        </DataGrid>
    </Grid>
</UserControl>
```

- [ ] **Step 4.6: Create the date-conversion helper used by the XAML**

Create `src/SiemensS7Demo.Wpf/Converters/DateTimeOffsetToDateConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace SiemensS7Demo.Wpf.Converters;

public sealed class DateTimeOffsetToDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            DateTimeOffset dto => (DateTime?)dto.LocalDateTime.Date,
            _ => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            DateTime dt => new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt)),
            _ => null
        };
    }
}
```

Register the converter in `src/SiemensS7Demo.Wpf/App.xaml`. Add to the `<Application.Resources>` section (creating it if not yet present from Pkg 1):

```xml
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/Tokens.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <conv:DateTimeOffsetToDateConverter x:Key="DateTimeOffsetToDateConverter"
                xmlns:conv="clr-namespace:SiemensS7Demo.Wpf.Converters"/>
        </ResourceDictionary>
    </Application.Resources>
```

If Pkg 1 already merged a resource dictionary, append the converter inside that existing dictionary alongside other converters rather than wrapping a new one.

- [ ] **Step 4.7: Create `HistoryAlarmsView.xaml.cs`**

Create `src/SiemensS7Demo.Wpf/Views/Alarms/HistoryAlarmsView.xaml.cs`:

```csharp
using System.Windows.Controls;
using SiemensS7Demo.Wpf.ViewModels.Alarms;

namespace SiemensS7Demo.Wpf.Views.Alarms;

public partial class HistoryAlarmsView : UserControl
{
    public HistoryAlarmsView(HistoryAlarmsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
```

- [ ] **Step 4.8: Register history VM/view + navigation entry**

In `src/SiemensS7Demo.Wpf/App.xaml.cs`, alongside the previous registrations from Task 3, add:

```csharp
            services.AddTransient<SiemensS7Demo.Wpf.ViewModels.Alarms.HistoryAlarmsViewModel>();
            services.AddTransient<SiemensS7Demo.Wpf.Views.Alarms.HistoryAlarmsView>();
```

In `src/SiemensS7Demo.Wpf/ViewModels/ShellViewModel.cs`, after the Current Alarms entry added in Task 3.9, add:

```csharp
            NavigationItems.Add(new NavigationItem("AlarmHistory", "Alarm History",
                typeof(SiemensS7Demo.Wpf.Views.Alarms.HistoryAlarmsView)));
```

- [ ] **Step 4.9: Build + run full Pkg2 suite**

```pwsh
dotnet build EnviroEquipmentFinalEdition.sln
dotnet test EnviroEquipmentFinalEdition.sln --filter "Category=Pkg2"
```

Expected output:
```
Build succeeded.
Test Run Successful.
Total tests: 36
     Passed: 36
```

(7 evaluator + 7 repo + 9 service + 7 current VM + 6 history VM. If a small mismatch, what matters is zero failures.)

---

## Task 5 — M2.5: Critical popup + Warn/Info toast (single-instance enforced)

**Files:** Create `src/SiemensS7Demo.Wpf/Alarms/IAlarmPopupGate.cs`, `AlarmPopupCoordinator.cs`. Create `src/SiemensS7Demo.Wpf/Views/Alarms/AlarmPopupWindow.xaml{,.cs}`. Create `src/SiemensS7Demo.Wpf/Alarms/AlarmToastHost.cs`, `ToastNotificationViewModel.cs`. Modify `src/SiemensS7Demo.Wpf/Views/Shell.xaml` to host the toast overlay. Create `tests/EnviroEquipment.Wpf.Tests/Alarms/AlarmPopupCoordinatorTests.cs`.

- [ ] **Step 5.1: Write failing single-instance tests**

Create `tests/EnviroEquipment.Wpf.Tests/Alarms/AlarmPopupCoordinatorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using SiemensS7Demo.Wpf.Alarms;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.Alarms;

[Trait("Category", "Pkg2")]
public class AlarmPopupCoordinatorTests
{
    private static readonly DeviceId D1 = new("d1");
    private static readonly DateTimeOffset T0 = new(2026, 5, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SingleCriticalEvent_ShowsPopupOnce()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Critical));
        await WaitFor(() => gate.ShowCount == 1, TimeSpan.FromSeconds(1));

        gate.ShowCount.Should().Be(1);
        gate.PresentedIds.Should().Equal("a");
    }

    [Fact]
    public async Task WarnLevel_DoesNotTriggerPopup()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Warn));
        subject.OnNext(MakeEvent("b", AlarmLevel.Info));
        await Task.Delay(50);

        gate.ShowCount.Should().Be(0);
    }

    [Fact]
    public async Task TwoSimultaneousCriticals_ShowOnePopupAtATime()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Critical));
        subject.OnNext(MakeEvent("b", AlarmLevel.Critical));
        subject.OnNext(MakeEvent("c", AlarmLevel.Critical));

        await WaitFor(() => gate.ShowCount == 1, TimeSpan.FromSeconds(1));
        gate.ShowCount.Should().Be(1);
        gate.PresentedIds.Should().Equal("a");

        gate.DismissCurrent();
        await WaitFor(() => gate.ShowCount == 2, TimeSpan.FromSeconds(1));
        gate.PresentedIds.Should().Equal("a", "b");

        gate.DismissCurrent();
        await WaitFor(() => gate.ShowCount == 3, TimeSpan.FromSeconds(1));
        gate.PresentedIds.Should().Equal("a", "b", "c");

        gate.DismissCurrent();
        gate.ShowCount.Should().Be(3, because: "no more queued items");
    }

    [Fact]
    public async Task SameAlarmRepeatedWhilePopupShowing_DoesNotDoubleEnqueue()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Critical));
        subject.OnNext(MakeEvent("a", AlarmLevel.Critical));
        subject.OnNext(MakeEvent("a", AlarmLevel.Critical));

        await WaitFor(() => gate.ShowCount == 1, TimeSpan.FromSeconds(1));
        gate.DismissCurrent();
        await Task.Delay(50);

        gate.ShowCount.Should().Be(1, because: "same Id is deduped from the queue");
    }

    [Fact]
    public async Task AckedCriticalEvent_DoesNotTriggerPopup()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Critical) with { Ack = true });
        await Task.Delay(50);

        gate.ShowCount.Should().Be(0);
    }

    [Fact]
    public async Task ResetCriticalEvent_DoesNotTriggerPopup()
    {
        var subject = new Subject<AlarmEvent>();
        var svc = new FakeService(subject);
        var gate = new FakePopupGate();
        using var coord = new AlarmPopupCoordinator(svc, gate);

        subject.OnNext(MakeEvent("a", AlarmLevel.Critical) with { Reset = true });
        await Task.Delay(50);

        gate.ShowCount.Should().Be(0);
    }

    private static AlarmEvent MakeEvent(string id, AlarmLevel level)
        => new(id, D1, level, Code: id, Message: $"m-{id}", At: T0,
               Ack: false, Reset: false, Muted: false);

    private static async Task WaitFor(Func<bool> predicate, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > timeout)
                throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private sealed class FakeService : IAlarmService
    {
        private readonly IObservable<AlarmEvent> _stream;
        public FakeService(IObservable<AlarmEvent> stream) => _stream = stream;
        public IObservable<AlarmEvent> Stream => _stream;
        public Task AckAsync(string alarmId, CancellationToken ct) => Task.CompletedTask;
        public Task ResetAsync(string alarmId, CancellationToken ct) => Task.CompletedTask;
        public Task MuteAsync(string alarmId, TimeSpan w, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakePopupGate : IAlarmPopupGate
    {
        private Action? _onDismiss;
        public int ShowCount { get; private set; }
        public List<string> PresentedIds { get; } = new();

        public void Show(AlarmEvent e, Action onDismissed)
        {
            ShowCount++;
            PresentedIds.Add(e.Id);
            _onDismiss = onDismissed;
        }

        public void DismissCurrent()
        {
            var d = _onDismiss;
            _onDismiss = null;
            d?.Invoke();
        }
    }
}
```

- [ ] **Step 5.2: Run, confirm failure**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~AlarmPopupCoordinatorTests"
```

Expected output:
```
Build FAILED.
    CS0246: The type or namespace name 'AlarmPopupCoordinator' could not be found
    CS0246: The type or namespace name 'IAlarmPopupGate' could not be found
```

- [ ] **Step 5.3: Create `IAlarmPopupGate.cs`**

Create `src/SiemensS7Demo.Wpf/Alarms/IAlarmPopupGate.cs`:

```csharp
using System;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.Alarms;

/// <summary>
/// Abstraction over the WPF popup window. Real implementation calls
/// <c>AlarmPopupWindow.ShowDialog()</c>; tests inject a fake that records calls.
/// </summary>
public interface IAlarmPopupGate
{
    /// <summary>
    /// Show the popup for <paramref name="e"/>. Must invoke <paramref name="onDismissed"/>
    /// exactly once when the user closes the popup.
    /// </summary>
    void Show(AlarmEvent e, Action onDismissed);
}
```

- [ ] **Step 5.4: Create `AlarmPopupCoordinator.cs`**

Create `src/SiemensS7Demo.Wpf/Alarms/AlarmPopupCoordinator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.Alarms;

/// <summary>
/// Subscribes to <see cref="IAlarmService.Stream"/> filtered to fresh Critical
/// events and serializes their display through <see cref="IAlarmPopupGate"/>.
/// Only one popup is visible at any time; further Critical events enqueue and
/// show after dismissal. Duplicate Ids inside the queue are deduped.
/// </summary>
public sealed class AlarmPopupCoordinator : IDisposable
{
    private readonly IAlarmPopupGate _gate;
    private readonly IDisposable _subscription;

    private readonly object _lock = new();
    private bool _popupOpen;
    private readonly Queue<AlarmEvent> _queue = new();
    private readonly HashSet<string> _enqueuedOrShownIds = new();

    public AlarmPopupCoordinator(IAlarmService service, IAlarmPopupGate gate)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));

        _subscription = service.Stream
            .Where(e => e.Level == AlarmLevel.Critical && !e.Ack && !e.Reset && !e.Muted)
            .Subscribe(Handle);
    }

    private void Handle(AlarmEvent e)
    {
        lock (_lock)
        {
            if (!_enqueuedOrShownIds.Add(e.Id))
            {
                return; // duplicate Id, skip
            }

            if (_popupOpen)
            {
                _queue.Enqueue(e);
                return;
            }

            _popupOpen = true;
        }

        _gate.Show(e, OnDismissed);
    }

    private void OnDismissed()
    {
        AlarmEvent? next = null;
        lock (_lock)
        {
            if (_queue.Count > 0)
            {
                next = _queue.Dequeue();
            }
            else
            {
                _popupOpen = false;
            }
        }

        if (next is not null)
        {
            _gate.Show(next, OnDismissed);
        }
    }

    public void Dispose() => _subscription.Dispose();
}
```

- [ ] **Step 5.5: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~AlarmPopupCoordinatorTests"
```

Expected output:
```
Test Run Successful.
Total tests: 6
     Passed: 6
```

- [ ] **Step 5.6: Create `AlarmPopupWindow.xaml`**

Create `src/SiemensS7Demo.Wpf/Views/Alarms/AlarmPopupWindow.xaml`:

```xml
<Window x:Class="SiemensS7Demo.Wpf.Views.Alarms.AlarmPopupWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Critical Alarm"
        Height="280" Width="520"
        WindowStartupLocation="CenterOwner"
        WindowStyle="ToolWindow"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        Topmost="True"
        Background="{DynamicResource BrushBg1}">
    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="{DynamicResource BrushRed}" CornerRadius="6" Padding="12,6">
            <TextBlock Text="CRITICAL ALARM" FontSize="18" FontWeight="Bold" Foreground="White"/>
        </Border>

        <StackPanel Grid.Row="1" Margin="0,16,0,0">
            <TextBlock Text="{Binding Code}" FontSize="20" FontWeight="SemiBold"
                       Foreground="{DynamicResource BrushFg1}"/>
            <TextBlock Text="{Binding Message}" FontSize="14" TextWrapping="Wrap"
                       Foreground="{DynamicResource BrushFg2}" Margin="0,8,0,0"/>
            <TextBlock Foreground="{DynamicResource BrushFg3}" Margin="0,12,0,0">
                <Run Text="Device: "/>
                <Run Text="{Binding DeviceId.Value, Mode=OneWay}"/>
                <Run Text="    At: "/>
                <Run Text="{Binding At, StringFormat=yyyy-MM-dd HH:mm:ss, Mode=OneWay}"/>
            </TextBlock>
        </StackPanel>

        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button x:Name="AcknowledgeButton" Content="Acknowledge"
                    Click="AcknowledgeButton_Click"
                    Padding="16,6" Margin="0,0,8,0"/>
            <Button x:Name="DismissButton" Content="Dismiss"
                    Click="DismissButton_Click"
                    Padding="16,6"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 5.7: Create `AlarmPopupWindow.xaml.cs`**

Create `src/SiemensS7Demo.Wpf/Views/Alarms/AlarmPopupWindow.xaml.cs`:

```csharp
using System.Threading;
using System.Windows;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.Views.Alarms;

public partial class AlarmPopupWindow : Window
{
    private readonly IAlarmService _service;

    public AlarmPopupWindow(AlarmEvent e, IAlarmService service)
    {
        InitializeComponent();
        DataContext = e;
        _service = service;
    }

    private async void AcknowledgeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AlarmEvent evt)
        {
            await _service.AckAsync(evt.Id, CancellationToken.None);
        }
        Close();
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 5.8: Create the real `IAlarmPopupGate` implementation**

Create `src/SiemensS7Demo.Wpf/Alarms/WindowAlarmPopupGate.cs`:

```csharp
using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain.Alarms;
using SiemensS7Demo.Wpf.Views.Alarms;

namespace SiemensS7Demo.Wpf.Alarms;

/// <summary>
/// Production gate: opens an <see cref="AlarmPopupWindow"/> on the UI thread,
/// hooks its <c>Closed</c> event to fire the dismissal callback exactly once.
/// </summary>
public sealed class WindowAlarmPopupGate : IAlarmPopupGate
{
    private readonly IServiceProvider _provider;

    public WindowAlarmPopupGate(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public void Show(AlarmEvent e, Action onDismissed)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            // No WPF dispatcher available (headless test/host). Fire dismissal synchronously
            // so the coordinator queue does not stall.
            onDismissed();
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            var service = _provider.GetRequiredService<IAlarmService>();
            var window = new AlarmPopupWindow(e, service)
            {
                Owner = Application.Current.MainWindow
            };
            window.Closed += (_, _) => onDismissed();
            window.Show();
        });
    }
}
```

- [ ] **Step 5.9: Wire popup coordinator + gate in DI**

In `src/SiemensS7Demo.Wpf/App.xaml.cs`, inside `ConfigureServices` add:

```csharp
            services.AddSingleton<IAlarmPopupGate, WindowAlarmPopupGate>();
            services.AddSingleton<AlarmPopupCoordinator>();
```

After the host is built and before `MainWindow.Show()`, resolve the coordinator so its subscription is live:

```csharp
            _ = _host.Services.GetRequiredService<AlarmPopupCoordinator>();
```

(Use whatever field name Pkg 1 chose for the `IHost` — the line just needs to be reached before the main window is created.)

- [ ] **Step 5.10: Create toast view-model + host**

Create `src/SiemensS7Demo.Wpf/Alarms/ToastNotificationViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.Alarms;

public sealed partial class ToastNotificationViewModel : ObservableObject
{
    public string Id { get; }
    public AlarmLevel Level { get; }
    public string Title { get; }
    public string Body { get; }

    [ObservableProperty]
    private double opacity = 1.0;

    public ToastNotificationViewModel(AlarmEvent e)
    {
        Id = e.Id;
        Level = e.Level;
        Title = $"{e.Level}: {e.Code}";
        Body = e.Message;
    }
}
```

Create `src/SiemensS7Demo.Wpf/Alarms/AlarmToastHost.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.Alarms;

/// <summary>
/// Stack of non-blocking toast notifications shown in the bottom-right of the
/// Shell. Each <see cref="AlarmLevel.Warn"/> or <see cref="AlarmLevel.Info"/>
/// event creates a toast that auto-fades after 5 seconds. Critical events are
/// handled by <see cref="AlarmPopupCoordinator"/> and skipped here.
/// </summary>
public sealed class AlarmToastHost : ItemsControl, IDisposable
{
    private readonly TimeSpan _autoDismiss = TimeSpan.FromSeconds(5);
    private IDisposable? _subscription;

    public ObservableCollection<ToastNotificationViewModel> Toasts { get; } = new();

    public AlarmToastHost()
    {
        ItemsSource = Toasts;
        ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
        Width = 360;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 16, 16);
    }

    public void Attach(IAlarmService service)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        _subscription?.Dispose();
        _subscription = service.Stream
            .Where(e => (e.Level == AlarmLevel.Warn || e.Level == AlarmLevel.Info)
                        && !e.Ack && !e.Reset && !e.Muted)
            .ObserveOn(DispatcherScheduler.Current)
            .Subscribe(OnEvent);
    }

    private void OnEvent(AlarmEvent e)
    {
        var toast = new ToastNotificationViewModel(e);
        Toasts.Add(toast);
        ScheduleDismiss(toast);
    }

    private async void ScheduleDismiss(ToastNotificationViewModel toast)
    {
        try
        {
            await Task.Delay(_autoDismiss);
            await Dispatcher.InvokeAsync(() => Toasts.Remove(toast));
        }
        catch
        {
            // dispatcher already torn down on app exit; safe to swallow
        }
    }

    public void Dispose() => _subscription?.Dispose();
}
```

- [ ] **Step 5.11: Add the toast overlay to the shell**

In `src/SiemensS7Demo.Wpf/Views/Shell.xaml`, wrap the existing root content in a `Grid` and add an `AlarmToastHost` instance overlaid on top. If Pkg 1's shell root is already a `Grid`, just add this child:

```xml
    <alarms:AlarmToastHost x:Name="ToastHost"
                            xmlns:alarms="clr-namespace:SiemensS7Demo.Wpf.Alarms"
                            Grid.RowSpan="99" Grid.ColumnSpan="99"
                            Panel.ZIndex="1000"/>
```

In `src/SiemensS7Demo.Wpf/Views/Shell.xaml.cs`, in the constructor (after `InitializeComponent()`), wire it:

```csharp
        Loaded += (_, _) =>
        {
            var svc = ((App)Application.Current).Services.GetRequiredService<IAlarmService>();
            ToastHost.Attach(svc);
        };
```

(Add `using Microsoft.Extensions.DependencyInjection;` and `using SiemensS7Demo.App.Alarms;` as needed.)

- [ ] **Step 5.12: Add the `--headless-smoke=alarm` runner**

In `src/SiemensS7Demo.Wpf/Program.cs` (or `App.xaml.cs` if Pkg 1 wired the CLI there), inside the existing `OnStartup` / arg parsing block, after Pkg 1's `--headless-smoke` handling, add:

```csharp
        if (args.Contains("--headless-smoke=alarm"))
        {
            var exitCode = await SiemensS7Demo.Wpf.Alarms.HeadlessAlarmSmoke.RunAsync(_host.Services);
            Environment.Exit(exitCode);
            return;
        }
```

Create `src/SiemensS7Demo.Wpf/Alarms/HeadlessAlarmSmoke.cs`:

```csharp
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.App;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.Alarms;

/// <summary>
/// Acceptance smoke for Pkg 2. Writes an out-of-range temperature into the
/// in-memory adapter via DeviceSessionManager, asserts a Critical event arrives
/// within 500ms, acks it, and asserts the popup gate was invoked exactly once.
/// Returns 0 on success, non-zero on failure.
/// </summary>
public static class HeadlessAlarmSmoke
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        var sessions = services.GetRequiredService<IDeviceSessionManager>();
        var alarms = services.GetRequiredService<IAlarmService>();
        var repo = services.GetRequiredService<IAlarmRepository>();

        var firstCritical = alarms.Stream
            .Where(e => e.Level == AlarmLevel.Critical && !e.Ack && !e.Reset)
            .Take(1)
            .Timeout(TimeSpan.FromMilliseconds(500));

        // The DeviceSessionManager is expected (Pkg 1 contract) to push a Device
        // observable each time its in-memory adapter reports a new reading. The
        // headless smoke just connects and waits.
        await sessions.ConnectAllAsync(CancellationToken.None);

        AlarmEvent received;
        try
        {
            received = await firstCritical;
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("FAIL: no Critical alarm within 500ms");
            return 2;
        }

        await alarms.AckAsync(received.Id, CancellationToken.None);

        var stored = await repo.QueryAsync(new AlarmFilter(null, null, null, AlarmLevel.Critical), CancellationToken.None);
        var ackedRow = stored.FirstOrDefault(e => e.Id == received.Id);
        if (ackedRow is null || !ackedRow.Ack)
        {
            Console.Error.WriteLine("FAIL: ack did not persist");
            return 3;
        }

        Console.WriteLine("PASS: headless-smoke=alarm");
        return 0;
    }
}
```

This smoke assumes Pkg 1's `InMemoryAdapter` (the Pkg 1 dev fixture) injects an out-of-range reading on startup. If Pkg 1's adapter does not auto-emit such a reading, add a small helper to inject one — call the App layer's `IDeviceSessionManager` write surface or the in-memory adapter directly via DI. Either way the smoke must end with one Critical event having been observed.

- [ ] **Step 5.13: Build + run full Pkg2 suite + smoke**

```pwsh
dotnet build EnviroEquipmentFinalEdition.sln
dotnet test EnviroEquipmentFinalEdition.sln --filter "Category=Pkg2"
```

Expected output:
```
Build succeeded.
Test Run Successful.
Total tests: 42
     Passed: 42
```

Then run the smoke:

```pwsh
dotnet run --project src/SiemensS7Demo.Wpf -- --headless-smoke=alarm
```

Expected output:
```
PASS: headless-smoke=alarm
```

Exit code 0.

---

## Task 6 — E2E flow test (full pipeline integration)

**Files:** Create `tests/EnviroEquipment.E2ETests/Pkg2/AlarmFlowTests.cs`.

- [ ] **Step 6.1: Write E2E test**

Create `tests/EnviroEquipment.E2ETests/Pkg2/AlarmFlowTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.App;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using SiemensS7Demo.Wpf.Alarms;
using Xunit;

namespace EnviroEquipment.E2ETests.Pkg2;

[Trait("Category", "Pkg2")]
public class AlarmFlowTests
{
    [Fact]
    public async Task OutOfRangeReading_ProducesCriticalAlarm_ThatCanBeAcked_AndPopupShowsOnce()
    {
        // Compose the same DI graph used by the WPF host, sans the actual Window.
        var services = new ServiceCollection();
        services.AddEnviroAppServices(); // Pkg 1's registration + Pkg 2's additions

        // Substitute the popup gate with a counting fake so we can assert single-instance.
        var fakeGate = new CountingPopupGate();
        services.AddSingleton<IAlarmPopupGate>(fakeGate);
        services.AddSingleton<AlarmPopupCoordinator>();

        var provider = services.BuildServiceProvider();
        var sessions = provider.GetRequiredService<IDeviceSessionManager>();
        var alarms = provider.GetRequiredService<IAlarmService>();
        _ = provider.GetRequiredService<AlarmPopupCoordinator>(); // wire subscription

        await sessions.ConnectAllAsync(CancellationToken.None);

        var firstCritical = await alarms.Stream
            .Where(e => e.Level == AlarmLevel.Critical && !e.Ack && !e.Reset)
            .Take(1)
            .Timeout(TimeSpan.FromMilliseconds(500));

        firstCritical.Level.Should().Be(AlarmLevel.Critical);

        await alarms.AckAsync(firstCritical.Id, CancellationToken.None);
        await Task.Delay(50);

        // The popup gate must have fired exactly once for that critical event.
        fakeGate.ShowCount.Should().Be(1);
        fakeGate.ShownIds.Should().Contain(firstCritical.Id);
    }

    private sealed class CountingPopupGate : IAlarmPopupGate
    {
        public int ShowCount { get; private set; }
        public List<string> ShownIds { get; } = new();

        public void Show(AlarmEvent e, Action onDismissed)
        {
            ShowCount++;
            ShownIds.Add(e.Id);
            // Fire dismissal synchronously to keep the coordinator queue draining.
            onDismissed();
        }
    }
}
```

- [ ] **Step 6.2: Run the E2E test**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~AlarmFlowTests"
```

Expected output:
```
Test Run Successful.
Total tests: 1
     Passed: 1
```

If this test fails because the `IDeviceSessionManager` registered in `AddEnviroAppServices` does not auto-emit any reading (e.g., it requires an explicit `InjectReading(...)` call from a test fixture), the E2E will hit the `Timeout(500ms)` and the test will report a `TimeoutException`. Fix by either (a) configuring Pkg 1's in-memory adapter at composition time to seed an out-of-range reading, or (b) reaching into the `IDeviceSessionManager` to push a synthetic reading. The fix lives in this E2E test setup, not in the production code.

- [ ] **Step 6.3: Run the full Pkg2 categorized suite**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "Category=Pkg2"
```

Expected output:
```
Test Run Successful.
Total tests: 43
     Passed: 43
```

(42 unit/VM/coordinator + 1 E2E.)

---

## Task 7 — Open the PR

- [ ] **Step 7.1: Run the full solution test suite for a final green check**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected output ends with:
```
Test Run Successful.
```

No failures from the entire solution. If Pkg 1 tests fail, the failure is a Pkg 1 regression and must be fixed before merging this Pkg 2 PR — the rebase against current `main` after Pkg 1 lands is expected to keep them green.

- [ ] **Step 7.2: Run the acceptance smoke**

```pwsh
dotnet run --project src/SiemensS7Demo.Wpf -- --headless-smoke=alarm
```

Expected output:
```
PASS: headless-smoke=alarm
```

Exit code 0.

- [ ] **Step 7.3: Push branch + open PR**

```pwsh
git push -u origin feat/phase2-pkg2-alarms
gh pr create --title "Phase 2 Pkg 2: alarm subsystem (rule engine, panels, popup, toast)" --body @'
## Summary
- Domain types: `AlarmEvent`, `AlarmLevel`, `AlarmRule`, `AlarmFilter`, pure-function `AlarmEvaluator`.
- App layer: `IAlarmService` + `AlarmService` (Rx hot observable, `(DeviceId, Code)` debounce, ack/reset/mute), `IAlarmRepository` + `InMemoryAlarmRepository`.
- WPF: `CurrentAlarmsView` (live ack/reset/mute), `HistoryAlarmsView` (date/device/level filters), `AlarmPopupWindow` for Critical events serialized through `AlarmPopupCoordinator` (single-instance + queue), `AlarmToastHost` overlay for Warn/Info.
- `--headless-smoke=alarm` exits 0 once an out-of-range reading produces a Critical event acked within 500ms and the popup gate was invoked exactly once.

## Test plan
- [x] `dotnet test EnviroEquipmentFinalEdition.sln --filter "Category=Pkg2"` — all green
- [x] `dotnet run --project src/SiemensS7Demo.Wpf -- --headless-smoke=alarm` — exit 0
- [x] Debounce: identical `(DeviceId, Code)` inside 5s window emits once
- [x] Debounce respects different `DeviceId` and different `Code` (no false suppression)
- [x] Popup single-instance: three Criticals -> one popup at a time, queue drains in order, duplicate Ids deduped
- [x] Warn/Info events surface as toast, not as popup
- [x] Acked / Reset events don't trigger a popup
- [x] Ack persists to the repository

## Depends on
- Pkg 1 M1.3 (`IDeviceSessionManager.Devices`) on `main`.

## Defers to later packages
- Pkg 3 M3.1: swap `InMemoryAlarmRepository` for the SQLite-backed implementation behind the same interface.
- Pkg 4 M4.3: retrofit `[RequiresRole]` on `AckAsync`/`ResetAsync`/`MuteAsync`.

(Generated with Claude Code.)
'@
```

- [ ] **Step 7.4: Reply to lead**

`SendMessage` to `team-lead` with the PR URL and the smoke-output line. Wait for code review before merging.

---

## Self-Review Checklist

Before declaring task done:

- [ ] All 5 milestones (M2.1 evaluator, M2.2 service+repo+debounce, M2.3 current panel, M2.4 history panel, M2.5 popup+toast single-instance) covered.
- [ ] No placeholders ("TBD", "TODO", "similar to above", "fill in", "add appropriate") anywhere in this plan.
- [ ] Type and method names are consistent across the file: `AlarmEvent`, `AlarmLevel`, `AlarmRule`, `AlarmFilter`, `AlarmEvaluator`, `IAlarmService`, `AlarmService`, `AlarmServiceOptions`, `IAlarmRepository`, `InMemoryAlarmRepository`, `AlarmRulesCatalog`, `AlarmRowViewModel`, `CurrentAlarmsViewModel`, `HistoryAlarmsViewModel`, `IAlarmPopupGate`, `AlarmPopupCoordinator`, `WindowAlarmPopupGate`, `AlarmPopupWindow`, `AlarmToastHost`, `ToastNotificationViewModel`, `HeadlessAlarmSmoke`.
- [ ] Every `dotnet` command in the plan is followed by an Expected output block.
- [ ] Popup single-instance test exists in Task 5 (`TwoSimultaneousCriticals_ShowOnePopupAtATime`, `SameAlarmRepeatedWhilePopupShowing_DoesNotDoubleEnqueue`).
- [ ] Debounce test exists in Task 2 (`Stream_DebouncesIdenticalDeviceCode_WithinWindow`, plus the non-suppression complements and the post-window refire).
- [ ] No emojis in any code, XAML, commit messages, or test names.
- [ ] All `Predicate<ReadingSnapshot>` rule bodies are pure (no I/O); evaluator catches and skips throwing rules.
- [ ] `AlarmService.OnDevice` writes to repository fire-and-forget so a repo hiccup does not stall the observable.
- [ ] Coordinator queue lock-scope holds only inside `Handle`/`OnDismissed` decision points; `_gate.Show` runs outside the lock to avoid reentrancy deadlocks.
- [ ] No upward dependency arrows: `Domain` references nothing, `App` references `Domain` + `Core`, `Wpf` references `App` + `Domain`. The plan never asks `Domain` or `App` to reference `Wpf`.
- [ ] `Category=Pkg2` trait applied to every new test class.
- [ ] Pkg 3's SQLite swap point is explicit (the same `IAlarmRepository` interface, single line of DI replacement) and called out in the PR template.
