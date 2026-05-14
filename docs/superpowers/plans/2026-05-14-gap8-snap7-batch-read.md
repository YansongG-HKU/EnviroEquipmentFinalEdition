# Gap #8 — Snap7 Batch Read Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse N single-point S7 reads into one `Cli_DBRead` per "window" so a poll cycle of 50 connected DB tags becomes ~1 round-trip instead of 50.

**Architecture:** Introduce `ReadRawBatchAsync` on `IS7Adapter` as a default interface method that loops `ReadRawAsync`. `SiemensS7Client.ReadTagsAsync` calls the batch method. Create a pure `Snap7BatchPlan` planner that groups tags by `(area, dbnumber)` and greedily merges them into windows ≤ 240 bytes. `Snap7S7Adapter` overrides the batch method to use the planner and one `Cli_DBRead` (or `Cli_ReadArea`) per window. `InMemoryS7Adapter` and `ModbusTcpAdapter` inherit the default (no per-adapter optimization in this PR — Modbus batch is a later concern).

**Tech Stack:** C# .NET 8, xunit, FluentAssertions. Default interface methods (C# 8+).

**Scope guard:** Snap7 only. Modbus batch optimization is out of scope. Don't change protocol parsing or write paths.

**Branch:** `feat/gap8-snap7-batch-read`
**Worktree:** `.claude/worktrees/gap8-snap7-batch-read`
**Base:** `main` after Wave 0 is merged.

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Modify | `src/SiemensS7Demo/Drivers/IS7Adapter.cs` | Add `ReadRawBatchAsync` default interface method |
| Create | `src/SiemensS7Demo/Drivers/Snap7BatchPlan.cs` | Pure planner: tags → windows |
| Modify | `src/SiemensS7Demo/Drivers/Snap7S7Adapter.cs` | Override `ReadRawBatchAsync` with planner + per-window `Cli_DBRead` |
| Modify | `src/SiemensS7Demo/Drivers/SiemensS7Client.cs` | Use `ReadRawBatchAsync` |
| Create | `tests/EnviroEquipment.Tests/Drivers/Snap7BatchPlanTests.cs` | Pure-function planner correctness |
| Create | `tests/EnviroEquipment.Tests/Drivers/SiemensS7ClientBatchReadTests.cs` | Client routes to batch + per-tag quality preserved |

We rely on the InMemory adapter (which uses default batch impl) to prove the client-side behavior. Real Snap7 PLC isn't exercised in CI — that's smoke-tested via `--self-test`.

---

## Task 1: Add `ReadRawBatchAsync` to `IS7Adapter`

**Files:** Modify `src/SiemensS7Demo/Drivers/IS7Adapter.cs`.

- [ ] **Step 1.1: Add the default interface method**

Open `src/SiemensS7Demo/Drivers/IS7Adapter.cs` and replace the file with:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Drivers;

/// <summary>
/// Adapter abstraction used by SiemensS7Client.
/// </summary>
public interface IS7Adapter
{
    Task ConnectAsync(PlcConnectionOptions options, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    bool IsConnected { get; }

    Task<PlcDeviceInfo> GetDeviceInfoAsync(PlcConnectionOptions options, CancellationToken cancellationToken);
    Task<object> ReadRawAsync(TagDefinition tag, CancellationToken cancellationToken);
    Task WriteRawAsync(TagDefinition tag, object value, CancellationToken cancellationToken);

    /// <summary>
    /// Batch read. Default implementation invokes <see cref="ReadRawAsync"/> per tag and
    /// captures per-tag failures so a single bad point does not abort the snapshot.
    /// Adapters with a native batch path (Snap7 windows, Modbus multi-register reads)
    /// should override.
    /// </summary>
    async Task<IReadOnlyDictionary<string, BatchReadResult>> ReadRawBatchAsync(
        IReadOnlyList<TagDefinition> tags,
        CancellationToken cancellationToken)
    {
        var output = new Dictionary<string, BatchReadResult>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            try
            {
                var raw = await ReadRawAsync(tag, cancellationToken);
                output[tag.Name] = BatchReadResult.Ok(raw);
            }
            catch (System.Exception ex)
            {
                output[tag.Name] = BatchReadResult.Bad(ex.Message);
            }
        }
        return output;
    }
}

public readonly record struct BatchReadResult(object? Value, string? Error)
{
    public bool IsGood => Error is null;

    public static BatchReadResult Ok(object value) => new(value, null);
    public static BatchReadResult Bad(string error) => new(null, error);
}
```

- [ ] **Step 1.2: Build to confirm no callers break**

```bash
dotnet build EnviroEquipmentFinalEdition.sln
```

Expected: success.

- [ ] **Step 1.3: Commit**

```bash
git add src/SiemensS7Demo/Drivers/IS7Adapter.cs
git commit -m "feat(drivers): IS7Adapter.ReadRawBatchAsync with per-tag BatchReadResult"
```

---

## Task 2: `SiemensS7Client.ReadTagsAsync` uses the batch path

**Files:** Modify `src/SiemensS7Demo/Drivers/SiemensS7Client.cs`. Create `tests/EnviroEquipment.Tests/Drivers/SiemensS7ClientBatchReadTests.cs`.

- [ ] **Step 2.1: Write failing test**

Create `tests/EnviroEquipment.Tests/Drivers/SiemensS7ClientBatchReadTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class SiemensS7ClientBatchReadTests
{
    [Fact]
    public async Task ReadTagsAsync_PassesAllTagsThroughBatchAndPreservesQuality()
    {
        var spy = new SpyAdapter();
        var client = new SiemensS7Client(
            new PlcConnectionOptions { Name = "T", IpAddress = "127.0.0.1", CpuType = "Mock" },
            spy);
        await client.ConnectAsync(CancellationToken.None);

        var good = MakeTag("Good", "MW0");
        var bad = MakeTag("Bad", "MW2");
        spy.GoodNames.Add("Good");
        spy.GoodValues["Good"] = (short)42;
        spy.BadNames.Add("Bad");
        spy.BadMessages["Bad"] = "simulated failure";

        var values = await client.ReadTagsAsync(new[] { good, bad }, CancellationToken.None);

        spy.BatchInvocations.Should().Be(1);
        spy.SingleInvocations.Should().Be(0);
        values["Good"].IsQualityGood.Should().BeTrue();
        values["Bad"].IsQualityGood.Should().BeFalse();
        values["Bad"].QualityMessage.Should().Be("simulated failure");
    }

    private static TagDefinition MakeTag(string name, string address) => new()
    {
        Name = name, DisplayName = name, Group = "g",
        Address = address, DataType = TagDataType.Int16, Unit = ""
    };

    private sealed class SpyAdapter : IS7Adapter
    {
        public List<string> GoodNames { get; } = new();
        public List<string> BadNames { get; } = new();
        public Dictionary<string, object> GoodValues { get; } = new();
        public Dictionary<string, string> BadMessages { get; } = new();
        public int BatchInvocations;
        public int SingleInvocations;

        public bool IsConnected => true;
        public Task ConnectAsync(PlcConnectionOptions options, CancellationToken ct) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<PlcDeviceInfo> GetDeviceInfoAsync(PlcConnectionOptions options, CancellationToken ct)
            => Task.FromResult(new PlcDeviceInfo { TimestampUtc = System.DateTime.UtcNow, IpAddress = "", Port = 0, Rack = 0, Slot = 0, ConnectionType = "", ConfiguredCpuType = "" });

        public Task<object> ReadRawAsync(TagDefinition tag, CancellationToken ct)
        {
            SingleInvocations++;
            return Task.FromResult<object>(0);
        }

        public Task WriteRawAsync(TagDefinition tag, object value, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, BatchReadResult>> ReadRawBatchAsync(
            IReadOnlyList<TagDefinition> tags, CancellationToken ct)
        {
            BatchInvocations++;
            var dict = new Dictionary<string, BatchReadResult>();
            foreach (var tag in tags)
            {
                dict[tag.Name] = BadNames.Contains(tag.Name)
                    ? BatchReadResult.Bad(BadMessages[tag.Name])
                    : BatchReadResult.Ok(GoodValues[tag.Name]);
            }
            return Task.FromResult<IReadOnlyDictionary<string, BatchReadResult>>(dict);
        }
    }
}
```

- [ ] **Step 2.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~SiemensS7ClientBatchReadTests"
```

Expected: failure — `SingleInvocations` is 2 because client still iterates `ReadRawAsync`.

- [ ] **Step 2.3: Refactor `SiemensS7Client.ReadTagsAsync` to use batch**

In `src/SiemensS7Demo/Drivers/SiemensS7Client.cs`, replace the `ReadTagsAsync` body. The new structure: call `_adapter.ReadRawBatchAsync(tags, ct)` once under the lock, then build TagValues from results. Keep the same TagValue construction so Gap #3 (DisplayValue) compatibility is preserved if both PRs land.

Replace the body of `ReadTagsAsync` between `await _requestLock.WaitAsync(...)` and `finally` with:

```csharp
            var batch = await _adapter.ReadRawBatchAsync(tags, cancellationToken);
            foreach (var tag in tags)
            {
                if (!batch.TryGetValue(tag.Name, out var result))
                {
                    output[tag.Name] = new TagValue
                    {
                        Name = tag.Name,
                        DisplayName = tag.DisplayName,
                        Address = tag.Address,
                        Unit = tag.Unit,
                        Value = string.Empty,
                        TimestampUtc = DateTime.UtcNow,
                        IsQualityGood = false,
                        QualityMessage = "Adapter omitted this tag from the batch result."
                    };
                    continue;
                }

                if (!result.IsGood)
                {
                    output[tag.Name] = new TagValue
                    {
                        Name = tag.Name,
                        DisplayName = tag.DisplayName,
                        Address = tag.Address,
                        Unit = tag.Unit,
                        Value = string.Empty,
                        TimestampUtc = DateTime.UtcNow,
                        IsQualityGood = false,
                        QualityMessage = result.Error
                    };
                    continue;
                }

                output[tag.Name] = new TagValue
                {
                    Name = tag.Name,
                    DisplayName = tag.DisplayName,
                    Address = tag.Address,
                    Unit = tag.Unit,
                    RawValue = result.Value,
                    Value = ConvertReadValue(tag, result.Value!),
                    TimestampUtc = DateTime.UtcNow,
                    IsQualityGood = true
                };
            }
```

(Remove the old `foreach (var tag in tags)` block that called `ReadRawAsync` per tag.)

- [ ] **Step 2.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~SiemensS7ClientBatchReadTests"
```

Expected: 1 passing.

- [ ] **Step 2.5: Re-run full suite to confirm no regressions**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green. Existing `--self-test` Modbus loopback test exercises the default batch path via `InMemoryS7Adapter` / `ModbusTcpAdapter` and must still pass.

- [ ] **Step 2.6: Commit**

```bash
git add src/SiemensS7Demo/Drivers/SiemensS7Client.cs tests/EnviroEquipment.Tests/Drivers/SiemensS7ClientBatchReadTests.cs
git commit -m "feat(client): route ReadTagsAsync through IS7Adapter.ReadRawBatchAsync"
```

---

## Task 3: `Snap7BatchPlan` pure planner

**Files:** Create `src/SiemensS7Demo/Drivers/Snap7BatchPlan.cs`. Create `tests/EnviroEquipment.Tests/Drivers/Snap7BatchPlanTests.cs`.

The planner emits windows. A `Snap7BatchWindow` knows `(AreaCode, DbNumber, StartByte, Length)` plus the list of tags assigned to it with their byte offset within the window.

- [ ] **Step 3.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Drivers/Snap7BatchPlanTests.cs`:

```csharp
using System.Linq;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class Snap7BatchPlanTests
{
    [Fact]
    public void Plan_GroupsByAreaAndDbnumber()
    {
        var tags = new[]
        {
            MakeTag("a", "DB1.DBW0", TagDataType.Int16),
            MakeTag("b", "DB1.DBW2", TagDataType.Int16),
            MakeTag("c", "DB2.DBW0", TagDataType.Int16)
        };

        var plan = Snap7BatchPlan.Plan(tags, maxWindowBytes: 240, mergeSlack: 16);

        plan.Should().HaveCount(2);
        plan.Should().Contain(w => w.DbNumber == 1 && w.StartByte == 0 && w.Length == 4 && w.Tags.Count == 2);
        plan.Should().Contain(w => w.DbNumber == 2 && w.StartByte == 0 && w.Length == 2 && w.Tags.Count == 1);
    }

    [Fact]
    public void Plan_MergesAdjacentTagsIntoSingleWindow()
    {
        var tags = new[]
        {
            MakeTag("a", "DB1.DBW0", TagDataType.Int16),  // bytes 0..1
            MakeTag("b", "DB1.DBW4", TagDataType.Int16),  // bytes 4..5 (gap of 2 bytes — within slack)
            MakeTag("c", "DB1.DBW200", TagDataType.Int16) // far away
        };

        var plan = Snap7BatchPlan.Plan(tags, maxWindowBytes: 240, mergeSlack: 16);

        plan.Should().HaveCount(2);
        var firstWindow = plan.Single(w => w.StartByte == 0);
        firstWindow.Length.Should().Be(6);
        firstWindow.Tags.Should().HaveCount(2);
        plan.Should().Contain(w => w.StartByte == 200 && w.Length == 2);
    }

    [Fact]
    public void Plan_SplitsWhenWindowExceedsMaxBytes()
    {
        var tags = new[]
        {
            MakeTag("a", "DB1.DBW0", TagDataType.Int16),    // 0..1
            MakeTag("b", "DB1.DBW238", TagDataType.Int16)   // 238..239 → end byte 240 = maxWindow
        };

        var plan = Snap7BatchPlan.Plan(tags, maxWindowBytes: 240, mergeSlack: 16);

        // Span 0..240 = 240 bytes — exactly fits.
        plan.Should().HaveCount(1);
        plan[0].Length.Should().Be(240);
    }

    [Fact]
    public void Plan_SplitsWhenSlackExceeded()
    {
        var tags = new[]
        {
            MakeTag("a", "DB1.DBW0", TagDataType.Int16),
            MakeTag("b", "DB1.DBW20", TagDataType.Int16)  // gap of 18 bytes > slack 16
        };

        var plan = Snap7BatchPlan.Plan(tags, maxWindowBytes: 240, mergeSlack: 16);

        plan.Should().HaveCount(2);
    }

    [Fact]
    public void Plan_AssignsTagsToCorrectOffsetWithinWindow()
    {
        var tags = new[]
        {
            MakeTag("a", "DB1.DBW10", TagDataType.Int16),
            MakeTag("b", "DB1.DBW14", TagDataType.Real)
        };

        var plan = Snap7BatchPlan.Plan(tags, maxWindowBytes: 240, mergeSlack: 16);
        plan.Should().HaveCount(1);
        var window = plan[0];
        window.StartByte.Should().Be(10);
        window.Length.Should().Be(8);  // 10..18
        window.Tags.Single(t => t.Tag.Name == "a").OffsetInWindow.Should().Be(0);
        window.Tags.Single(t => t.Tag.Name == "b").OffsetInWindow.Should().Be(4);
    }

    private static TagDefinition MakeTag(string name, string address, TagDataType type) => new()
    {
        Name = name, DisplayName = name, Group = "g",
        Address = address, DataType = type, Unit = ""
    };
}
```

- [ ] **Step 3.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~Snap7BatchPlanTests"
```

Expected: compile error (`Snap7BatchPlan` doesn't exist).

- [ ] **Step 3.3: Create the planner**

Create `src/SiemensS7Demo/Drivers/Snap7BatchPlan.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Drivers;

public sealed record Snap7BatchTagSlot(TagDefinition Tag, int OffsetInWindow);

public sealed record Snap7BatchWindow(
    int AreaCode,
    int DbNumber,
    int StartByte,
    int Length,
    IReadOnlyList<Snap7BatchTagSlot> Tags);

public static class Snap7BatchPlan
{
    public static IReadOnlyList<Snap7BatchWindow> Plan(
        IReadOnlyList<TagDefinition> tags,
        int maxWindowBytes,
        int mergeSlack)
    {
        var slots = tags
            .Select(tag => new
            {
                Tag = tag,
                Address = S7Address.Parse(tag),
                Size = S7Address.Parse(tag).ByteSize(tag.DataType)
            })
            .OrderBy(s => s.Address.AreaCode)
            .ThenBy(s => s.Address.DbNumber)
            .ThenBy(s => s.Address.ByteOffset)
            .ToList();

        var windows = new List<Snap7BatchWindow>();
        var bucket = new List<(TagDefinition Tag, S7Address Address, int Size)>();
        int currentArea = -1;
        int currentDb = -1;

        void Flush()
        {
            if (bucket.Count == 0) return;

            var start = bucket[0].Address.ByteOffset;
            var end = start;
            var segment = new List<(TagDefinition Tag, S7Address Address, int Size)>();

            foreach (var item in bucket)
            {
                var itemEnd = item.Address.ByteOffset + item.Size;
                if (segment.Count == 0)
                {
                    segment.Add(item);
                    end = itemEnd;
                    continue;
                }

                var gap = item.Address.ByteOffset - end;
                var prospectiveLength = itemEnd - segment[0].Address.ByteOffset;
                if (gap <= mergeSlack && prospectiveLength <= maxWindowBytes)
                {
                    segment.Add(item);
                    end = itemEnd;
                }
                else
                {
                    EmitWindow(segment);
                    segment.Clear();
                    segment.Add(item);
                    end = itemEnd;
                }
            }

            if (segment.Count > 0) EmitWindow(segment);
            bucket.Clear();
        }

        void EmitWindow(List<(TagDefinition Tag, S7Address Address, int Size)> segment)
        {
            var startByte = segment[0].Address.ByteOffset;
            var length = segment.Max(s => s.Address.ByteOffset + s.Size) - startByte;
            var slotList = segment
                .Select(s => new Snap7BatchTagSlot(s.Tag, s.Address.ByteOffset - startByte))
                .ToList();
            windows.Add(new Snap7BatchWindow(
                segment[0].Address.AreaCode,
                segment[0].Address.DbNumber,
                startByte,
                length,
                slotList));
        }

        foreach (var s in slots)
        {
            if (s.Address.AreaCode != currentArea || s.Address.DbNumber != currentDb)
            {
                Flush();
                currentArea = s.Address.AreaCode;
                currentDb = s.Address.DbNumber;
            }
            bucket.Add((s.Tag, s.Address, s.Size));
        }
        Flush();

        return windows;
    }
}
```

**Important:** This depends on `S7Address.ByteSize(TagDataType)` and `S7Address.AreaCode` / `DbNumber` / `ByteOffset` already existing on `S7Address`. Verify by reading `src/SiemensS7Demo/Drivers/S7Address.cs` first. If `ByteSize` is private or absent, surface it as `internal static int ByteSize(TagDataType type)` and add an `InternalsVisibleTo` for the tests project (the Gap #1 PR already wires this; if you're working pre-merge, re-add it here).

Also: handle the **Bool case** carefully. A bool tag with `BitIndex` set takes 1 byte for the read but the planner should not try to merge it onto a numeric layout incorrectly. `S7Address.ByteSize(TagDataType.Bool)` should return 1; verify and document.

- [ ] **Step 3.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~Snap7BatchPlanTests"
```

Expected: 5 passing. If any fail, debug — do NOT relax the test thresholds.

- [ ] **Step 3.5: Commit**

```bash
git add src/SiemensS7Demo/Drivers/Snap7BatchPlan.cs tests/EnviroEquipment.Tests/Drivers/Snap7BatchPlanTests.cs
git commit -m "feat(snap7): Snap7BatchPlan greedy windowing planner"
```

---

## Task 4: `Snap7S7Adapter.ReadRawBatchAsync` uses the planner

**Files:** Modify `src/SiemensS7Demo/Drivers/Snap7S7Adapter.cs`.

- [ ] **Step 4.1: Override the batch method**

In `src/SiemensS7Demo/Drivers/Snap7S7Adapter.cs`, add this method (between `ReadRawAsync` and `WriteRawAsync`):

```csharp
    public Task<IReadOnlyDictionary<string, BatchReadResult>> ReadRawBatchAsync(
        IReadOnlyList<TagDefinition> tags,
        CancellationToken cancellationToken)
        => Task.Run<IReadOnlyDictionary<string, BatchReadResult>>(() =>
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            var output = new Dictionary<string, BatchReadResult>(System.StringComparer.OrdinalIgnoreCase);
            if (tags.Count == 0) return output;

            var windows = Snap7BatchPlan.Plan(tags, maxWindowBytes: 240, mergeSlack: 16);

            foreach (var window in windows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] buffer;
                try
                {
                    buffer = ReadBytes(
                        new S7Address(window.AreaCode, window.DbNumber, window.StartByte, BitIndex: null),
                        window.Length);
                }
                catch (System.Exception ex)
                {
                    foreach (var slot in window.Tags)
                    {
                        output[slot.Tag.Name] = BatchReadResult.Bad(ex.Message);
                    }
                    continue;
                }

                foreach (var slot in window.Tags)
                {
                    try
                    {
                        var address = S7Address.Parse(slot.Tag);
                        var size = address.ByteSize(slot.Tag.DataType);
                        var segment = new byte[size];
                        System.Array.Copy(buffer, slot.OffsetInWindow, segment, 0, size);
                        var decoded = Decode(slot.Tag, address, segment);
                        output[slot.Tag.Name] = BatchReadResult.Ok(decoded);
                    }
                    catch (System.Exception ex)
                    {
                        output[slot.Tag.Name] = BatchReadResult.Bad(ex.Message);
                    }
                }
            }

            return output;
        }, cancellationToken);
```

**Note on the `new S7Address(...)` call:** verify the constructor matches the record signature in `src/SiemensS7Demo/Drivers/S7Address.cs`. If `S7Address` exposes a different shape (e.g., a different parameter list), adapt the constructor call accordingly. The intent is "synthesize a window-level address from area/db/startByte with no bit index".

- [ ] **Step 4.2: Build and ensure no regressions**

```bash
dotnet build EnviroEquipmentFinalEdition.sln
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green. The Snap7 path itself isn't unit-tested (no fake snap7.dll), so we rely on `Snap7BatchPlanTests` for the planner and on smoke tests for the integration. The build must succeed.

- [ ] **Step 4.3: Commit**

```bash
git add src/SiemensS7Demo/Drivers/Snap7S7Adapter.cs
git commit -m "feat(snap7): override ReadRawBatchAsync to use Snap7BatchPlan windows"
```

---

## Task 5: Extend `--self-test` with a batch case

**Files:** Modify `src/SiemensS7Demo/Program.cs`.

- [ ] **Step 5.1: Add a batch test inside `RunSelfTestAsync`**

After the existing "mock read and guarded-write block" `RunAsync` call, add a new one:

```csharp
    await RunAsync("mock batch read", async () =>
    {
        var options = new PlcConnectionOptions { Name = "SelfTestBatch", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new InMemoryS7Adapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(cancellationToken);

        var t1 = MakeTag("Batch1", "MW0", TagDataType.Int16, TagAccess.Read);
        var t2 = MakeTag("Batch2", "MW2", TagDataType.Int16, TagAccess.Read);
        var t3 = MakeTag("Batch3", "MD4", TagDataType.DInt, TagAccess.Read);

        await adapter.WriteRawAsync(t1, (short)11, cancellationToken);
        await adapter.WriteRawAsync(t2, (short)22, cancellationToken);
        await adapter.WriteRawAsync(t3, 333, cancellationToken);

        var values = await client.ReadTagsAsync(new[] { t1, t2, t3 }, cancellationToken);
        AssertGood(values, "Batch1");
        AssertGood(values, "Batch2");
        AssertGood(values, "Batch3");
        if (System.Convert.ToInt32(values["Batch1"].Value, System.Globalization.CultureInfo.InvariantCulture) != 11
            || System.Convert.ToInt32(values["Batch2"].Value, System.Globalization.CultureInfo.InvariantCulture) != 22
            || System.Convert.ToInt32(values["Batch3"].Value, System.Globalization.CultureInfo.InvariantCulture) != 333)
        {
            throw new InvalidOperationException("Batch read returned unexpected values.");
        }
    });
```

- [ ] **Step 5.2: Run self-test**

```bash
dotnet run --project src/SiemensS7Demo/SiemensS7Demo.csproj -- --self-test
```

Expected: `[PASS] mock batch read` line, exit code 0.

- [ ] **Step 5.3: Commit**

```bash
git add src/SiemensS7Demo/Program.cs
git commit -m "test(self-test): cover IS7Adapter.ReadRawBatchAsync default path"
```

---

## Task 6: Open the PR

- [ ] **Step 6.1: Push + PR**

```bash
git push -u origin feat/gap8-snap7-batch-read
gh pr create --title "Gap #8: Snap7 batch read with windowed planner" --body "$(cat <<'EOF'
## Summary
- Adds `IS7Adapter.ReadRawBatchAsync` (default interface method) returning per-tag `BatchReadResult`. Mock and Modbus adapters inherit a per-tag-iteration default; Snap7 overrides.
- `Snap7BatchPlan.Plan` greedily merges tags grouped by `(area, dbnumber)` into windows ≤240 bytes with up to 16 bytes of slack between adjacent tags.
- `Snap7S7Adapter` issues one read per window (Cli_DBRead / Cli_ReadArea) then slices results back to per-tag values, preserving per-tag quality on partial failures.
- `SiemensS7Client.ReadTagsAsync` now routes through the batch path; behavior unchanged for callers.

## Test plan
- [x] `dotnet test EnviroEquipmentFinalEdition.sln` — all green; 5 planner tests + 1 client routing test added
- [x] `dotnet run -- --self-test` includes `[PASS] mock batch read`
- [x] Existing Modbus loopback + mock write-guard tests unaffected
- Snap7 real-PLC verification deferred to a separate smoke (no PLC in CI)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 6.2: SendMessage to `team-lead`** with PR URL, planner test count, and any deviations from the plan (e.g., S7Address signature surprises).

---

## Self-Review Checklist

- [ ] Default interface method compiles and runs on `InMemoryS7Adapter` / `ModbusTcpAdapter` without override.
- [ ] `Snap7BatchPlan.Plan` is pure (no I/O, no statics) and deterministic.
- [ ] Per-tag quality preserved across window-level failures (one bad window doesn't poison sibling windows).
- [ ] `--self-test` still exits 0.
- [ ] No emojis, no commented-out code, no `TODO` notes left in.
- [ ] `S7Address` constructor signature matches what the adapter calls — verify before commit.

---

## Known Open Items (Not in This PR)

- Modbus batch optimization (FC03 multi-register read) — separate PR if/when needed.
- Real-PLC integration test on S7-200 SMART — deferred to manual smoke after merge.
- PDU length discovery: we hardcode `maxWindowBytes=240`. Future PR can read `MaxPduLength` from `Cli_GetCpInfo` and pass that in.
