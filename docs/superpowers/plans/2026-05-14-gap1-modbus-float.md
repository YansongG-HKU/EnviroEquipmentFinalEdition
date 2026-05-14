# Gap #1 — Modbus 32-bit Float (HRF) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 32-bit float (IEEE 754, two-register) read/write support to the Modbus TCP adapter with a configurable word-order knob, including a loopback round-trip test.

**Architecture:** Introduce a new `HRF<n>` address form parsed by `ModbusAddress`. Add `WordOrder` enum + `PlcConnectionOptions.WordOrder` to control byte/word swap. `ModbusTcpAdapter` reads/writes 2 consecutive holding registers (4 bytes) and applies the configured swap when decoding/encoding floats. `ModbusLoopbackServer` keeps the same wire protocol; we just pre-seed `_holdingRegisters[10..11]` with a known float so tests can read it.

**Tech Stack:** C# .NET 8, xunit, FluentAssertions, `System.Buffers.Binary.BinaryPrimitives`.

**Scope guard:** This plan covers **only HRF (float)**. `HRD` / `HRDU` (32-bit int) is Gap #2 (separate plan/PR). Don't touch S7-side code. Don't touch Snap7 adapter.

**Branch:** `feat/gap1-modbus-float`
**Worktree:** `.claude/worktrees/gap1-modbus-float` (auto-created by `isolation: "worktree"`)
**Base:** `main` after Wave 0 (`feat/wave0-tests-project`) is merged.

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Modify | `src/SiemensS7Demo/Models/PlcConnectionOptions.cs` | Add `WordOrder` enum import + property |
| Create | `src/SiemensS7Demo/Models/WordOrder.cs` | The 4-value enum |
| Modify | `src/SiemensS7Demo/Drivers/ModbusAddress.cs` | Recognize `HRF<n>` area |
| Modify | `src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs` | Read/write 4-byte float with word-order swap |
| Modify | `src/SiemensS7Demo/Testing/ModbusLoopbackServer.cs` | Pre-seed `_holdingRegisters[10..11]` with a known float |
| Modify | `src/SiemensS7Demo/Program.cs` | Extend `RunSelfTestAsync` with a float round-trip case |
| Create | `tests/EnviroEquipment.Tests/Drivers/ModbusAddressTests.cs` | HRF parsing |
| Create | `tests/EnviroEquipment.Tests/Drivers/WordOrderSwapTests.cs` | Pure-function swap correctness (ABCD/CDAB/BADC/DCBA) |
| Create | `tests/EnviroEquipment.Tests/Drivers/ModbusTcpAdapterFloatTests.cs` | Loopback round-trip incl. word-order |

---

## Task 1: Add `WordOrder` enum

**Files:** Create `src/SiemensS7Demo/Models/WordOrder.cs`. Modify `src/SiemensS7Demo/Models/PlcConnectionOptions.cs`.

- [ ] **Step 1.1: Create the enum file**

```csharp
namespace SiemensS7Demo.Models;

/// <summary>
/// 32-bit register layout for two-register Modbus values.
/// ABCD is "big-endian both bytes and words" (most Siemens / Schneider M580 default).
/// CDAB swaps the word pair (common on older Schneider Quantum).
/// BADC swaps byte pairs inside each word.
/// DCBA is fully reversed (little-endian both).
/// </summary>
public enum WordOrder
{
    ABCD,
    CDAB,
    BADC,
    DCBA
}
```

- [ ] **Step 1.2: Add `WordOrder` to `PlcConnectionOptions`**

Open `src/SiemensS7Demo/Models/PlcConnectionOptions.cs`. After the `WriteTimeoutMs` line, add:

```csharp
    public WordOrder WordOrder { get; init; } = WordOrder.ABCD;
```

- [ ] **Step 1.3: Commit**

```bash
git add src/SiemensS7Demo/Models/WordOrder.cs src/SiemensS7Demo/Models/PlcConnectionOptions.cs
git commit -m "feat(models): add WordOrder enum and PlcConnectionOptions.WordOrder"
```

---

## Task 2: HRF address parsing (TDD)

**Files:** Create `tests/EnviroEquipment.Tests/Drivers/ModbusAddressTests.cs`. Modify `src/SiemensS7Demo/Drivers/ModbusAddress.cs`.

- [ ] **Step 2.1: Write failing test**

Create `tests/EnviroEquipment.Tests/Drivers/ModbusAddressTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class ModbusAddressTests
{
    [Fact]
    public void Parse_AcceptsHrfForReal()
    {
        var tag = MakeTag("Temp", "HRF200", TagDataType.Real);
        var address = ModbusAddress.Parse(tag);

        address.Area.Should().Be("HRF");
        address.Offset.Should().Be(200);
        address.IsHoldingRegister.Should().BeTrue();
    }

    [Fact]
    public void Parse_RejectsHrfForInt16()
    {
        var tag = MakeTag("Bad", "HRF200", TagDataType.Int16);
        var act = () => ModbusAddress.Parse(tag);
        act.Should().Throw<System.FormatException>();
    }

    [Fact]
    public void Parse_RejectsHrForReal()
    {
        var tag = MakeTag("Bad", "HR200", TagDataType.Real);
        var act = () => ModbusAddress.Parse(tag);
        act.Should().Throw<System.FormatException>();
    }

    private static TagDefinition MakeTag(string name, string address, TagDataType type) => new()
    {
        Name = name,
        DisplayName = name,
        Group = "Test",
        Address = address,
        DataType = type,
        Unit = string.Empty
    };
}
```

- [ ] **Step 2.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ModbusAddressTests"
```

Expected: 3 failures (regex doesn't match `HRF`, and existing code rejects non-HR for floats).

- [ ] **Step 2.3: Implement HRF in `ModbusAddress`**

In `src/SiemensS7Demo/Drivers/ModbusAddress.cs`:

Replace the regex line:
```csharp
    private static readonly Regex AddressRegex = new(
        @"^(?<area>COIL|C|DI|HR|IR)(?<offset>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
```
with:
```csharp
    private static readonly Regex AddressRegex = new(
        @"^(?<area>COIL|C|DI|HRF|HR|IR)(?<offset>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
```

Replace the numeric-area check:
```csharp
        if (tag.DataType != TagDataType.Bool && area is not ("HR" or "IR"))
        {
            throw new FormatException($"Numeric Modbus tag '{tag.Name}' must use HR or IR address.");
        }
```
with:
```csharp
        if (tag.DataType == TagDataType.Real)
        {
            if (area != "HRF")
            {
                throw new FormatException($"Float Modbus tag '{tag.Name}' must use HRF address (got '{area}').");
            }
        }
        else if (tag.DataType != TagDataType.Bool && area is not ("HR" or "IR"))
        {
            throw new FormatException($"Numeric Modbus tag '{tag.Name}' must use HR or IR address.");
        }
        else if (tag.DataType != TagDataType.Real && area == "HRF")
        {
            throw new FormatException($"HRF address on tag '{tag.Name}' is reserved for Real data type.");
        }
```

Add an `IsFloatRegister` accessor next to the existing area accessors:
```csharp
    public bool IsFloatRegister => Area == "HRF";
```

Update `IsHoldingRegister` so HRF also returns true (HRF lives in the same Modbus address space as HR — it's just a 2-register float view):
```csharp
    public bool IsHoldingRegister => Area is "HR" or "HRF";
```

- [ ] **Step 2.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ModbusAddressTests"
```

Expected: 3 passing.

- [ ] **Step 2.5: Commit**

```bash
git add src/SiemensS7Demo/Drivers/ModbusAddress.cs tests/EnviroEquipment.Tests/Drivers/ModbusAddressTests.cs
git commit -m "feat(modbus): parse HRF<n> as 32-bit float holding-register address"
```

---

## Task 3: Word-order swap helper (TDD, pure function)

**Files:** Create `tests/EnviroEquipment.Tests/Drivers/WordOrderSwapTests.cs`. Modify `src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs` (add private helper).

- [ ] **Step 3.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Drivers/WordOrderSwapTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class WordOrderSwapTests
{
    // The number 1.0f in IEEE 754 single-precision big-endian (ABCD) is [0x3F, 0x80, 0x00, 0x00].
    private static readonly byte[] Abcd = { 0x3F, 0x80, 0x00, 0x00 };

    [Theory]
    [InlineData(WordOrder.ABCD, new byte[] { 0x3F, 0x80, 0x00, 0x00 })]
    [InlineData(WordOrder.CDAB, new byte[] { 0x00, 0x00, 0x3F, 0x80 })]
    [InlineData(WordOrder.BADC, new byte[] { 0x80, 0x3F, 0x00, 0x00 })]
    [InlineData(WordOrder.DCBA, new byte[] { 0x00, 0x00, 0x80, 0x3F })]
    public void ToWire_ProducesExpectedLayout(WordOrder order, byte[] expected)
    {
        var input = (byte[])Abcd.Clone();
        ModbusTcpAdapter.ApplyWordOrder(input, order, fromWire: false);
        input.Should().Equal(expected);
    }

    [Theory]
    [InlineData(WordOrder.ABCD)]
    [InlineData(WordOrder.CDAB)]
    [InlineData(WordOrder.BADC)]
    [InlineData(WordOrder.DCBA)]
    public void Roundtrip_ToWireAndFromWire_RestoresOriginal(WordOrder order)
    {
        var input = (byte[])Abcd.Clone();
        ModbusTcpAdapter.ApplyWordOrder(input, order, fromWire: false);
        ModbusTcpAdapter.ApplyWordOrder(input, order, fromWire: true);
        input.Should().Equal(Abcd);
    }
}
```

- [ ] **Step 3.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~WordOrderSwapTests"
```

Expected: compile error (`ApplyWordOrder` doesn't exist).

- [ ] **Step 3.3: Implement helper in `ModbusTcpAdapter`**

In `src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs`, add `using SiemensS7Demo.Models;` if not already imported, then add this `internal static` method (keep it `internal` so tests can reach it):

```csharp
    /// <summary>
    /// Swap the 4 bytes of a 2-register value according to <paramref name="order"/>.
    /// `fromWire=false`: convert a canonical big-endian (ABCD) buffer into wire layout.
    /// `fromWire=true`:  convert a wire-layout buffer back into canonical big-endian.
    /// CDAB and BADC are their own inverses, so the flag is informational; ABCD and DCBA
    /// also round-trip. The flag exists so future asymmetric orders can be added cleanly.
    /// </summary>
    internal static void ApplyWordOrder(byte[] buffer, WordOrder order, bool fromWire)
    {
        if (buffer.Length != 4)
        {
            throw new System.ArgumentException("WordOrder swap requires exactly 4 bytes.", nameof(buffer));
        }

        _ = fromWire;
        switch (order)
        {
            case WordOrder.ABCD:
                return;
            case WordOrder.CDAB:
                (buffer[0], buffer[2]) = (buffer[2], buffer[0]);
                (buffer[1], buffer[3]) = (buffer[3], buffer[1]);
                return;
            case WordOrder.BADC:
                (buffer[0], buffer[1]) = (buffer[1], buffer[0]);
                (buffer[2], buffer[3]) = (buffer[3], buffer[2]);
                return;
            case WordOrder.DCBA:
                System.Array.Reverse(buffer);
                return;
            default:
                throw new System.ArgumentOutOfRangeException(nameof(order), order, "Unsupported word order.");
        }
    }
```

To let the test reach this `internal` from a separate assembly, add an `InternalsVisibleTo` declaration. Edit `src/SiemensS7Demo/SiemensS7Demo.csproj` and add inside `<Project>`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="EnviroEquipment.Tests" />
  </ItemGroup>
```

- [ ] **Step 3.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~WordOrderSwapTests"
```

Expected: 8 passing (4 layout + 4 round-trip).

- [ ] **Step 3.5: Commit**

```bash
git add src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs src/SiemensS7Demo/SiemensS7Demo.csproj tests/EnviroEquipment.Tests/Drivers/WordOrderSwapTests.cs
git commit -m "feat(modbus): ApplyWordOrder helper for ABCD/CDAB/BADC/DCBA swaps"
```

---

## Task 4: HRF read path (TDD via loopback)

**Files:** Modify `src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs`, `src/SiemensS7Demo/Testing/ModbusLoopbackServer.cs`. Create `tests/EnviroEquipment.Tests/Drivers/ModbusTcpAdapterFloatTests.cs`.

- [ ] **Step 4.1: Pre-seed a float in the loopback server**

In `src/SiemensS7Demo/Testing/ModbusLoopbackServer.cs`, find the constructor body where it seeds `_holdingRegisters`. After `_holdingRegisters[1] = 65000;` add:

```csharp
        // Pre-seed a float at HR10/HR11 = 1.0f in ABCD layout (0x3F800000).
        _holdingRegisters[10] = 0x3F80;
        _holdingRegisters[11] = 0x0000;
```

- [ ] **Step 4.2: Write failing read test**

Create `tests/EnviroEquipment.Tests/Drivers/ModbusTcpAdapterFloatTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using SiemensS7Demo.Testing;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class ModbusTcpAdapterFloatTests
{
    [Fact]
    public async Task ReadRaw_DecodesAbcdFloatFromTwoRegisters()
    {
        await using var server = ModbusLoopbackServer.Start();
        using var adapter = new ModbusTcpAdapter();
        var options = new PlcConnectionOptions
        {
            Name = "T", IpAddress = "127.0.0.1", Port = server.Port,
            Protocol = "modbus", CpuType = "Modbus TCP", UnitId = 1,
            WordOrder = WordOrder.ABCD
        };
        await adapter.ConnectAsync(options, CancellationToken.None);

        var tag = new TagDefinition
        {
            Name = "F", DisplayName = "F", Group = "g",
            Address = "HRF10", DataType = TagDataType.Real, Unit = ""
        };

        var value = await adapter.ReadRawAsync(tag, CancellationToken.None);
        value.Should().BeOfType<float>().Which.Should().Be(1.0f);
    }
}
```

- [ ] **Step 4.3: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ModbusTcpAdapterFloatTests"
```

Expected: failure — adapter does not consult `WordOrder` and may also misread because Real currently reads via the generic 2-register path with no `HRF` knowledge.

- [ ] **Step 4.4: Update `ModbusTcpAdapter.ReadRawAsync` to handle HRF**

In `src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs`, replace the entire `ReadRawAsync` body (lines 59–85 in current file) with a version that uses `WordOrder`. The adapter needs the options to know `WordOrder`. Store it on connect.

Add a private field near the other fields:
```csharp
    private WordOrder _wordOrder = WordOrder.ABCD;
```

In `ConnectAsync`, after `_unitId = options.UnitId;` add:
```csharp
        _wordOrder = options.WordOrder;
```

Replace the existing `ReadRawAsync` decode block (the `tag.DataType switch` returning a value) with:

```csharp
        var bytes = registers.AsSpan(1, registerCount * 2).ToArray();
        if (tag.DataType is TagDataType.DInt or TagDataType.Real)
        {
            ApplyWordOrder(bytes, _wordOrder, fromWire: true);
        }

        return tag.DataType switch
        {
            TagDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(bytes),
            TagDataType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            TagDataType.DInt => BinaryPrimitives.ReadInt32BigEndian(bytes),
            TagDataType.Real => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes)),
            _ => throw new NotSupportedException($"Unsupported Modbus data type '{tag.DataType}'.")
        };
```

(The intent: pull the bytes out of the response, optionally word-swap, then decode big-endian.)

- [ ] **Step 4.5: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ModbusTcpAdapterFloatTests"
```

Expected: 1 passing.

- [ ] **Step 4.6: Add a CDAB read test to lock the swap behavior**

Append to `ModbusTcpAdapterFloatTests.cs`:

```csharp
    [Fact]
    public async Task ReadRaw_DecodesCdabFloatFromTwoRegisters()
    {
        await using var server = ModbusLoopbackServer.Start();
        using var adapter = new ModbusTcpAdapter();
        var options = new PlcConnectionOptions
        {
            Name = "T", IpAddress = "127.0.0.1", Port = server.Port,
            Protocol = "modbus", CpuType = "Modbus TCP", UnitId = 1,
            WordOrder = WordOrder.CDAB
        };
        await adapter.ConnectAsync(options, CancellationToken.None);

        // HR10/HR11 wire bytes = 0x3F80, 0x0000.  Under CDAB the device-published bytes for
        // 1.0f would be 0x0000, 0x3F80 (i.e. the same registers but interpreted as CDAB).
        // Our loopback always serves 0x3F80, 0x0000, so this test should NOT yield 1.0f under
        // CDAB — it yields whatever the CDAB swap of [3F,80,00,00] decodes to, which is
        // BinaryPrimitives.ReadSingleBigEndian of [00,00,3F,80] = 4.591774e-41f (denormal).
        var tag = new TagDefinition
        {
            Name = "F", DisplayName = "F", Group = "g",
            Address = "HRF10", DataType = TagDataType.Real, Unit = ""
        };
        var value = (float)await adapter.ReadRawAsync(tag, CancellationToken.None);

        // We only assert it's NOT 1.0f, proving the swap was applied.
        value.Should().NotBe(1.0f);
    }
```

Run and confirm 2 passing:
```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ModbusTcpAdapterFloatTests"
```

- [ ] **Step 4.7: Commit**

```bash
git add src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs src/SiemensS7Demo/Testing/ModbusLoopbackServer.cs tests/EnviroEquipment.Tests/Drivers/ModbusTcpAdapterFloatTests.cs
git commit -m "feat(modbus): read HRF<n> as float honoring PlcConnectionOptions.WordOrder"
```

---

## Task 5: HRF write path (TDD via loopback)

**Files:** Modify `src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs`. Extend `tests/EnviroEquipment.Tests/Drivers/ModbusTcpAdapterFloatTests.cs`.

- [ ] **Step 5.1: Write failing round-trip test**

Append to `ModbusTcpAdapterFloatTests.cs`:

```csharp
    [Theory]
    [InlineData(WordOrder.ABCD)]
    [InlineData(WordOrder.CDAB)]
    [InlineData(WordOrder.BADC)]
    [InlineData(WordOrder.DCBA)]
    public async Task WriteRaw_ThenReadRaw_RoundTripsFloat(WordOrder order)
    {
        await using var server = ModbusLoopbackServer.Start();
        using var adapter = new ModbusTcpAdapter();
        var options = new PlcConnectionOptions
        {
            Name = "T", IpAddress = "127.0.0.1", Port = server.Port,
            Protocol = "modbus", CpuType = "Modbus TCP", UnitId = 1,
            WordOrder = order
        };
        await adapter.ConnectAsync(options, CancellationToken.None);

        var tag = new TagDefinition
        {
            Name = "F", DisplayName = "F", Group = "g",
            Address = "HRF20", DataType = TagDataType.Real, Unit = ""
        };

        await adapter.WriteRawAsync(tag, 3.14159f, CancellationToken.None);
        var readBack = (float)await adapter.ReadRawAsync(tag, CancellationToken.None);

        readBack.Should().BeApproximately(3.14159f, 0.0001f);
    }
```

- [ ] **Step 5.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ModbusTcpAdapterFloatTests"
```

Expected: 4 new failures (write path doesn't apply WordOrder).

- [ ] **Step 5.3: Update `WriteRawAsync` to apply WordOrder for Real**

In `src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs`, replace the `EncodeReal` helper to take WordOrder, OR simpler: post-process the `registerBytes` for 32-bit types before sending. After the `var registerBytes = tag.DataType switch { ... };` block in `WriteRawAsync`, add:

```csharp
        if (tag.DataType is TagDataType.DInt or TagDataType.Real)
        {
            ApplyWordOrder(registerBytes, _wordOrder, fromWire: false);
        }
```

- [ ] **Step 5.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ModbusTcpAdapterFloatTests"
```

Expected: all 6 passing (read x2 + round-trip x4).

- [ ] **Step 5.5: Commit**

```bash
git add src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs tests/EnviroEquipment.Tests/Drivers/ModbusTcpAdapterFloatTests.cs
git commit -m "feat(modbus): write HRF<n> honoring WordOrder + round-trip tests"
```

---

## Task 6: Extend `--self-test` smoke

**Files:** Modify `src/SiemensS7Demo/Program.cs`.

- [ ] **Step 6.1: Add a float case inside `RunSelfTestAsync`**

In `src/SiemensS7Demo/Program.cs`, locate the existing `await RunAsync("Modbus loopback read/write", ...)` block. Inside it, after the existing HR2/HR3 round-trip assertions and before the closing `});`, append:

```csharp
        var hrf = MakeTag("HrFloat", "HRF20", TagDataType.Real, TagAccess.ReadWrite, safeWrite: true);
        await client.WriteTagAsync(hrf, 12.5f, cancellationToken);
        var floatReadback = await client.ReadTagsAsync(new[] { hrf }, cancellationToken);
        AssertGood(floatReadback, "HrFloat");
        if (System.Math.Abs(System.Convert.ToDouble(floatReadback["HrFloat"].Value, System.Globalization.CultureInfo.InvariantCulture) - 12.5) > 0.0001)
        {
            throw new InvalidOperationException("Expected HrFloat=12.5 after Modbus write.");
        }
```

- [ ] **Step 6.2: Build and run self-test**

```bash
dotnet build EnviroEquipmentFinalEdition.sln
dotnet run --project src/SiemensS7Demo/SiemensS7Demo.csproj -- --self-test
```

Expected: `[PASS] Modbus loopback read/write` line in output, exit code 0.

- [ ] **Step 6.3: Commit**

```bash
git add src/SiemensS7Demo/Program.cs
git commit -m "test(self-test): cover Modbus HRF round-trip in --self-test"
```

---

## Task 7: Open the PR

- [ ] **Step 7.1: Push branch + open PR**

```bash
git push -u origin feat/gap1-modbus-float
gh pr create --title "Gap #1: Modbus 32-bit float (HRF) with WordOrder" --body "$(cat <<'EOF'
## Summary
- Add `HRF<n>` Modbus address form for 32-bit IEEE 754 floats over two consecutive holding registers.
- Add `WordOrder` enum (`ABCD`/`CDAB`/`BADC`/`DCBA`) on `PlcConnectionOptions`; default `ABCD`.
- `ModbusTcpAdapter` applies the configured swap on both read and write paths for Real (and any future DInt — see Gap #2).

## Test plan
- [x] `dotnet test EnviroEquipmentFinalEdition.sln` — all green
- [x] `dotnet run --project src/SiemensS7Demo/SiemensS7Demo.csproj -- --self-test` — `[PASS] Modbus loopback read/write` includes new HRF case
- [x] Loopback round-trips 3.14159f across all four WordOrder modes
- [x] CDAB read of an ABCD-seeded register correctly differs from ABCD read (proves swap applies)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 7.2: Mark task #1 in-progress complete via TaskUpdate**

Reply to lead (`team-lead`) via SendMessage with PR URL and test summary. Wait for review before merging.

---

## Self-Review Checklist

Before declaring task done:

- [ ] All tests in `dotnet test` pass.
- [ ] `--self-test` exits 0 and shows the new float case.
- [ ] No emojis in code or commit messages.
- [ ] No changes to S7 / Snap7 / SiemensS7Client / Models other than the listed files.
- [ ] `InternalsVisibleTo` only added once.
- [ ] PR description follows the template above.
