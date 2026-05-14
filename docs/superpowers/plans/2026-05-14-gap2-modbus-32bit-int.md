# Gap #2 — Modbus 32-bit Int (DInt / UInt32) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Match the float work from Gap #1 for 32-bit signed (DInt) and unsigned (UInt32) integers, so Schneider/Siemens holding-register tables carrying `HRU（unsigned int）` and DB DInts round-trip through both Modbus and Snap7 paths.

**Architecture:** Add `UInt32` to `TagDataType`; wire the new type through `S7Address.ByteSize` / `ValidateAddressKind` and `Snap7S7Adapter` encode/decode. Introduce `HRD<n>` (signed) and `HRDU<n>` (unsigned) Modbus address forms. `ModbusTcpAdapter` re-uses Gap #1's `ApplyWordOrder` for both new types on read and write.

**Tech Stack:** C# .NET 8, xunit, FluentAssertions, `System.Buffers.Binary.BinaryPrimitives`.

**Prerequisite:** Gap #1 PR merged on `main`. This plan calls `ModbusTcpAdapter.ApplyWordOrder` and relies on the `WordOrder` swap path being live for `tag.DataType is TagDataType.DInt or TagDataType.Real`. Confirm by `git log --oneline | grep -i "Modbus 32-bit float"` before starting.

**Scope guard:** Only DInt/UInt32. No new bit-derivation, no template work.

**Branch:** `feat/gap2-modbus-32bit-int`
**Base:** `origin/main` (post Gap #1 merge).

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Modify | `src/SiemensS7Demo/Models/TagDefinition.cs` | Add `UInt32` to `TagDataType` enum |
| Modify | `src/SiemensS7Demo/Drivers/S7Address.cs` | `ByteSize` + `ValidateAddressKind` recognize UInt32 |
| Modify | `src/SiemensS7Demo/Drivers/Snap7S7Adapter.cs` | Encode/Decode for UInt32 |
| Modify | `src/SiemensS7Demo/Drivers/ModbusAddress.cs` | Parse `HRD<n>` / `HRDU<n>` |
| Modify | `src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs` | Read/write 32-bit ints with WordOrder |
| Modify | `src/SiemensS7Demo/Testing/ModbusLoopbackServer.cs` | Pre-seed DInt + UInt32 fixtures |
| Modify | `src/SiemensS7Demo/Program.cs` | `--self-test` covers HRD + HRDU round-trips |
| Modify | `src/SiemensS7Demo/Services/ConfigValidationService.cs` (light) | Reject bad combinations (DInt on `HR<n>`, etc.) — should already fall out of ModbusAddress, just add a test |
| Create | `tests/EnviroEquipment.Tests/Models/TagDataTypeTests.cs` | UInt32 enum presence |
| Create | `tests/EnviroEquipment.Tests/Drivers/ModbusAddress32BitTests.cs` | HRD/HRDU parsing rules |
| Create | `tests/EnviroEquipment.Tests/Drivers/ModbusTcpAdapter32BitTests.cs` | Loopback round-trip incl. WordOrder, both signed and unsigned |
| Create | `tests/EnviroEquipment.Tests/Drivers/S7Address32BitTests.cs` | UInt32 byte size + DBD-kind acceptance |

---

## Task 1: Add `UInt32` to `TagDataType`

**Files:** Modify `src/SiemensS7Demo/Models/TagDefinition.cs`. Create `tests/EnviroEquipment.Tests/Models/TagDataTypeTests.cs`.

- [ ] **Step 1.1: Failing test**

Create `tests/EnviroEquipment.Tests/Models/TagDataTypeTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Models;

public class TagDataTypeTests
{
    [Fact]
    public void TagDataType_DeclaresUInt32()
    {
        System.Enum.GetNames(typeof(TagDataType))
            .Should().Contain("UInt32");
    }
}
```

- [ ] **Step 1.2: Confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagDataTypeTests"
```

Expected: 1 failing (`UInt32` not in enum).

- [ ] **Step 1.3: Add the enum member**

In `src/SiemensS7Demo/Models/TagDefinition.cs`, add `UInt32` after `DInt`:

```csharp
public enum TagDataType
{
    Bool,
    Int16,
    UInt16,
    DInt,
    UInt32,
    Real
}
```

- [ ] **Step 1.4: Confirm pass + full build**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagDataTypeTests"
dotnet build EnviroEquipmentFinalEdition.sln
```

Expected: 1 passing + clean build. The build will warn about non-exhaustive switch expressions in `S7Address.ByteSize`, `S7Address.ValidateAddressKind`, `Snap7S7Adapter.Decode`, `Snap7S7Adapter.Encode`, and possibly `ModbusTcpAdapter.ReadRawAsync` / `WriteRawAsync`. Those are the call sites Tasks 2-4 will fix.

- [ ] **Step 1.5: Commit**

```bash
git add src/SiemensS7Demo/Models/TagDefinition.cs tests/EnviroEquipment.Tests/Models/TagDataTypeTests.cs
git commit -m "feat(models): add UInt32 to TagDataType"
```

---

## Task 2: S7 address validates UInt32

**Files:** Modify `src/SiemensS7Demo/Drivers/S7Address.cs`. Create `tests/EnviroEquipment.Tests/Drivers/S7Address32BitTests.cs`.

- [ ] **Step 2.1: Failing tests**

Create `tests/EnviroEquipment.Tests/Drivers/S7Address32BitTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class S7Address32BitTests
{
    [Fact]
    public void UInt32_Parses_DBD_AddressKind()
    {
        var tag = new TagDefinition
        {
            Name = "C", DisplayName = "C", Group = "g",
            Address = "DB1.DBD20", DataType = TagDataType.UInt32, Unit = ""
        };

        // We can't directly call S7Address.Parse from tests (it's internal),
        // so this assertion is indirect: ConfigValidationService runs Parse
        // for the "s7" protocol and reports errors. No error == accepted.
        var issues = SiemensS7Demo.Services.ConfigValidationService.ValidateTags(
            new[] { tag }, "s7", "test");
        issues.Should().NotContain(i => i.Severity == SiemensS7Demo.Services.ConfigIssueSeverity.Error);
    }
}
```

(If `S7Address` later becomes public or InternalsVisibleTo is added for tests, switch to a direct call. For now, validating via the public `ConfigValidationService` API is sufficient.)

- [ ] **Step 2.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~S7Address32BitTests"
```

Expected: failure — either compile-error if `ValidateAddressKind`/`ByteSize` is non-exhaustive, or a runtime `NotSupportedException` saying `Unsupported tag data type 'UInt32'`.

- [ ] **Step 2.3: Update `S7Address`**

In `src/SiemensS7Demo/Drivers/S7Address.cs`:

`ByteSize`: add UInt32 to the switch:
```csharp
    public int ByteSize(TagDataType dataType) => dataType switch
    {
        TagDataType.Bool => 1,
        TagDataType.Int16 => 2,
        TagDataType.UInt16 => 2,
        TagDataType.DInt => 4,
        TagDataType.UInt32 => 4,
        TagDataType.Real => 4,
        _ => throw new NotSupportedException($"Unsupported tag data type '{dataType}'.")
    };
```

`ValidateAddressKind`: extend the DInt branch to also accept UInt32:
```csharp
            TagDataType.Int16 or TagDataType.UInt16 => normalized is "W",
            TagDataType.DInt or TagDataType.UInt32 or TagDataType.Real => normalized is "D",
```

- [ ] **Step 2.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~S7Address32BitTests"
```

Expected: 1 passing.

- [ ] **Step 2.5: Commit**

```bash
git add src/SiemensS7Demo/Drivers/S7Address.cs tests/EnviroEquipment.Tests/Drivers/S7Address32BitTests.cs
git commit -m "feat(s7): accept UInt32 with DBD addressing"
```

---

## Task 3: Snap7 encode/decode UInt32

**Files:** Modify `src/SiemensS7Demo/Drivers/Snap7S7Adapter.cs`. No new test (covered by the smoke test in Task 7; Snap7 path isn't unit-tested in CI).

- [ ] **Step 3.1: Add UInt32 to `Decode`**

In `Snap7S7Adapter.Decode`:

```csharp
        return tag.DataType switch
        {
            TagDataType.Bool => address.BitIndex is not null && (buffer[0] & (1 << address.BitIndex.Value)) != 0,
            TagDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(buffer),
            TagDataType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(buffer),
            TagDataType.DInt => BinaryPrimitives.ReadInt32BigEndian(buffer),
            TagDataType.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(buffer),
            TagDataType.Real => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(buffer)),
            _ => throw new NotSupportedException($"Unsupported tag data type '{tag.DataType}'.")
        };
```

- [ ] **Step 3.2: Add UInt32 to `Encode`** and a new `EncodeUInt32`

```csharp
    private static byte[] Encode(TagDefinition tag, object value)
    {
        return tag.DataType switch
        {
            TagDataType.Int16 => EncodeInt16(value),
            TagDataType.UInt16 => EncodeUInt16(value),
            TagDataType.DInt => EncodeInt32(value),
            TagDataType.UInt32 => EncodeUInt32(value),
            TagDataType.Real => EncodeReal(value),
            TagDataType.Bool => new[] { Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? (byte)1 : (byte)0 },
            _ => throw new NotSupportedException($"Unsupported tag data type '{tag.DataType}'.")
        };
    }

    private static byte[] EncodeUInt32(object value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Convert.ToUInt32(value, CultureInfo.InvariantCulture));
        return buffer;
    }
```

- [ ] **Step 3.3: Build cleanly**

```bash
dotnet build EnviroEquipmentFinalEdition.sln
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3.4: Commit**

```bash
git add src/SiemensS7Demo/Drivers/Snap7S7Adapter.cs
git commit -m "feat(snap7): encode/decode UInt32 over 4 bytes big-endian"
```

---

## Task 4: Modbus address parses `HRD<n>` and `HRDU<n>`

**Files:** Modify `src/SiemensS7Demo/Drivers/ModbusAddress.cs`. Create `tests/EnviroEquipment.Tests/Drivers/ModbusAddress32BitTests.cs`.

- [ ] **Step 4.1: Failing tests**

Create `tests/EnviroEquipment.Tests/Drivers/ModbusAddress32BitTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class ModbusAddress32BitTests
{
    [Fact]
    public void Parse_AcceptsHrdForDInt()
    {
        var tag = MakeTag("X", "HRD100", TagDataType.DInt);
        var address = ModbusAddress.Parse(tag);
        address.Area.Should().Be("HRD");
        address.Offset.Should().Be(100);
        address.IsHoldingRegister.Should().BeTrue();
    }

    [Fact]
    public void Parse_AcceptsHrduForUInt32()
    {
        var tag = MakeTag("X", "HRDU200", TagDataType.UInt32);
        var address = ModbusAddress.Parse(tag);
        address.Area.Should().Be("HRDU");
        address.Offset.Should().Be(200);
        address.IsHoldingRegister.Should().BeTrue();
    }

    [Fact]
    public void Parse_RejectsHrdForInt16()
    {
        var tag = MakeTag("X", "HRD100", TagDataType.Int16);
        var act = () => ModbusAddress.Parse(tag);
        act.Should().Throw<System.FormatException>();
    }

    [Fact]
    public void Parse_RejectsHrForDInt()
    {
        var tag = MakeTag("X", "HR100", TagDataType.DInt);
        var act = () => ModbusAddress.Parse(tag);
        act.Should().Throw<System.FormatException>();
    }

    private static TagDefinition MakeTag(string name, string address, TagDataType type) => new()
    {
        Name = name, DisplayName = name, Group = "g",
        Address = address, DataType = type, Unit = ""
    };
}
```

- [ ] **Step 4.2: Confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ModbusAddress32BitTests"
```

Expected: 4 failing (regex unknown, type-area mismatch logic missing).

- [ ] **Step 4.3: Update `ModbusAddress`**

In `src/SiemensS7Demo/Drivers/ModbusAddress.cs`:

Update the regex to include `HRDU` and `HRD` (longest first so the alternation matches greedily):
```csharp
    private static readonly Regex AddressRegex = new(
        @"^(?<area>COIL|C|DI|HRDU|HRD|HRF|HR|IR)(?<offset>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
```

Add specific type/area pairings. Replace the existing `if (tag.DataType == TagDataType.Real) { ... }` block (added by Gap #1) with a complete dispatch:
```csharp
        switch (tag.DataType)
        {
            case TagDataType.Bool:
                if (area is not ("C" or "COIL" or "DI"))
                {
                    throw new FormatException($"Bool Modbus tag '{tag.Name}' must use C/COIL or DI address.");
                }
                break;

            case TagDataType.Int16:
            case TagDataType.UInt16:
                if (area is not ("HR" or "IR"))
                {
                    throw new FormatException($"16-bit Modbus tag '{tag.Name}' must use HR or IR address (got '{area}').");
                }
                break;

            case TagDataType.DInt:
                if (area != "HRD")
                {
                    throw new FormatException($"Signed 32-bit Modbus tag '{tag.Name}' must use HRD address (got '{area}').");
                }
                break;

            case TagDataType.UInt32:
                if (area != "HRDU")
                {
                    throw new FormatException($"Unsigned 32-bit Modbus tag '{tag.Name}' must use HRDU address (got '{area}').");
                }
                break;

            case TagDataType.Real:
                if (area != "HRF")
                {
                    throw new FormatException($"Float Modbus tag '{tag.Name}' must use HRF address (got '{area}').");
                }
                break;

            default:
                throw new FormatException($"Unsupported tag data type '{tag.DataType}' on Modbus.");
        }
```

Also reject HR-area kinds when the data type doesn't match — the switch above handles this implicitly.

Add `IsDoubleRegister` flag covering both HRD and HRDU (useful in adapter):
```csharp
    public bool IsDoubleRegister => Area is "HRD" or "HRDU";
```

Update `IsHoldingRegister`:
```csharp
    public bool IsHoldingRegister => Area is "HR" or "HRF" or "HRD" or "HRDU";
```

- [ ] **Step 4.4: Pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ModbusAddress32BitTests"
```

Expected: 4 passing.

- [ ] **Step 4.5: Commit**

```bash
git add src/SiemensS7Demo/Drivers/ModbusAddress.cs tests/EnviroEquipment.Tests/Drivers/ModbusAddress32BitTests.cs
git commit -m "feat(modbus): parse HRD and HRDU as 32-bit signed/unsigned forms"
```

---

## Task 5: Modbus adapter read + write 32-bit ints with WordOrder

**Files:** Modify `src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs`, `src/SiemensS7Demo/Testing/ModbusLoopbackServer.cs`. Create `tests/EnviroEquipment.Tests/Drivers/ModbusTcpAdapter32BitTests.cs`.

The good news: Gap #1 already wired the WordOrder swap for the `DInt`/`Real` branches in both read and write paths. UInt32 is a new branch.

- [ ] **Step 5.1: Pre-seed loopback fixtures**

In `src/SiemensS7Demo/Testing/ModbusLoopbackServer.cs`, after the existing `_holdingRegisters[11] = 0x0000;` (added by Gap #1), append:

```csharp
        // DInt at HR12/HR13 = 100000 (0x000186A0) in ABCD layout.
        _holdingRegisters[12] = 0x0001;
        _holdingRegisters[13] = 0x86A0;
        // UInt32 at HR14/HR15 = 0xFFFFFFFE (4294967294) — exercises sign bit.
        _holdingRegisters[14] = 0xFFFF;
        _holdingRegisters[15] = 0xFFFE;
```

- [ ] **Step 5.2: Failing tests**

Create `tests/EnviroEquipment.Tests/Drivers/ModbusTcpAdapter32BitTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using SiemensS7Demo.Testing;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class ModbusTcpAdapter32BitTests
{
    [Fact]
    public async Task ReadRaw_DecodesDIntAbcdFromTwoRegisters()
    {
        var (server, adapter) = await ConnectAsync(WordOrder.ABCD);
        await using var _ = server;
        using var __ = adapter;

        var tag = Make("X", "HRD12", TagDataType.DInt);
        var value = await adapter.ReadRawAsync(tag, CancellationToken.None);
        value.Should().BeOfType<int>().Which.Should().Be(100000);
    }

    [Fact]
    public async Task ReadRaw_DecodesUInt32AbcdFromTwoRegisters()
    {
        var (server, adapter) = await ConnectAsync(WordOrder.ABCD);
        await using var _ = server;
        using var __ = adapter;

        var tag = Make("X", "HRDU14", TagDataType.UInt32);
        var value = await adapter.ReadRawAsync(tag, CancellationToken.None);
        value.Should().BeOfType<uint>().Which.Should().Be(0xFFFFFFFEu);
    }

    [Theory]
    [InlineData(WordOrder.ABCD)]
    [InlineData(WordOrder.CDAB)]
    [InlineData(WordOrder.BADC)]
    [InlineData(WordOrder.DCBA)]
    public async Task WriteThenRead_DIntRoundTrips(WordOrder order)
    {
        var (server, adapter) = await ConnectAsync(order);
        await using var _ = server;
        using var __ = adapter;

        var tag = Make("X", "HRD30", TagDataType.DInt);
        await adapter.WriteRawAsync(tag, -123456, CancellationToken.None);
        var readBack = (int)await adapter.ReadRawAsync(tag, CancellationToken.None);
        readBack.Should().Be(-123456);
    }

    [Theory]
    [InlineData(WordOrder.ABCD)]
    [InlineData(WordOrder.CDAB)]
    [InlineData(WordOrder.BADC)]
    [InlineData(WordOrder.DCBA)]
    public async Task WriteThenRead_UInt32RoundTrips(WordOrder order)
    {
        var (server, adapter) = await ConnectAsync(order);
        await using var _ = server;
        using var __ = adapter;

        var tag = Make("X", "HRDU40", TagDataType.UInt32);
        await adapter.WriteRawAsync(tag, 4000000000u, CancellationToken.None);
        var readBack = (uint)await adapter.ReadRawAsync(tag, CancellationToken.None);
        readBack.Should().Be(4000000000u);
    }

    private static TagDefinition Make(string name, string address, TagDataType type) => new()
    {
        Name = name, DisplayName = name, Group = "g",
        Address = address, DataType = type, Unit = ""
    };

    private static async Task<(ModbusLoopbackServer Server, ModbusTcpAdapter Adapter)> ConnectAsync(WordOrder order)
    {
        var server = ModbusLoopbackServer.Start();
        var adapter = new ModbusTcpAdapter();
        var options = new PlcConnectionOptions
        {
            Name = "T", IpAddress = "127.0.0.1", Port = server.Port,
            Protocol = "modbus", CpuType = "Modbus TCP", UnitId = 1,
            WordOrder = order
        };
        await adapter.ConnectAsync(options, CancellationToken.None);
        return (server, adapter);
    }
}
```

- [ ] **Step 5.3: Confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ModbusTcpAdapter32BitTests"
```

Expected: 10 failing — adapter doesn't know UInt32 (`NotSupportedException`) and the DInt read may fail because the existing logic only knows the generic `HR` path and not the new `HRD` semantics.

- [ ] **Step 5.4: Update adapter read path**

In `src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs`, the `ReadRawAsync` method (modified by Gap #1) currently dispatches on `tag.DataType` to compute `registerCount` and to decode bytes. The 16-bit path used `registerCount=1`, 32-bit path used `registerCount=2`. We keep that. Just extend the switch.

After Gap #1's edits, the decode switch is:
```csharp
        return tag.DataType switch
        {
            TagDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(bytes),
            TagDataType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            TagDataType.DInt => BinaryPrimitives.ReadInt32BigEndian(bytes),
            TagDataType.Real => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes)),
            _ => throw new NotSupportedException($"Unsupported Modbus data type '{tag.DataType}'.")
        };
```

Insert `UInt32` before the default:
```csharp
            TagDataType.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(bytes),
```

Also the WordOrder-swap guard above:
```csharp
        if (tag.DataType is TagDataType.DInt or TagDataType.Real)
        {
            ApplyWordOrder(bytes, _wordOrder, fromWire: true);
        }
```
must include UInt32:
```csharp
        if (tag.DataType is TagDataType.DInt or TagDataType.UInt32 or TagDataType.Real)
        {
            ApplyWordOrder(bytes, _wordOrder, fromWire: true);
        }
```

- [ ] **Step 5.5: Update adapter write path**

In `WriteRawAsync`, after the Int16/UInt16 single-register branches, the existing 32-bit switch:
```csharp
        var registerBytes = tag.DataType switch
        {
            TagDataType.DInt => EncodeInt32(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            TagDataType.Real => EncodeReal(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            _ => throw new NotSupportedException($"Unsupported Modbus write type '{tag.DataType}'.")
        };
```

Add UInt32 + helper:
```csharp
        var registerBytes = tag.DataType switch
        {
            TagDataType.DInt => EncodeInt32(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            TagDataType.UInt32 => EncodeUInt32(Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
            TagDataType.Real => EncodeReal(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            _ => throw new NotSupportedException($"Unsupported Modbus write type '{tag.DataType}'.")
        };
```

Add the helper at the bottom of the class next to `EncodeInt32`:
```csharp
    private static byte[] EncodeUInt32(uint value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        return buffer;
    }
```

Update the WordOrder-swap guard in `WriteRawAsync`:
```csharp
        if (tag.DataType is TagDataType.DInt or TagDataType.UInt32 or TagDataType.Real)
        {
            ApplyWordOrder(registerBytes, _wordOrder, fromWire: false);
        }
```

- [ ] **Step 5.6: Pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ModbusTcpAdapter32BitTests"
```

Expected: 10 passing.

- [ ] **Step 5.7: Commit**

```bash
git add src/SiemensS7Demo/Drivers/ModbusTcpAdapter.cs src/SiemensS7Demo/Testing/ModbusLoopbackServer.cs tests/EnviroEquipment.Tests/Drivers/ModbusTcpAdapter32BitTests.cs
git commit -m "feat(modbus): read/write DInt and UInt32 over two registers with WordOrder"
```

---

## Task 6: Extend `--self-test`

**Files:** Modify `src/SiemensS7Demo/Program.cs`.

- [ ] **Step 6.1: Add DInt + UInt32 cases inside the existing Modbus loopback `RunAsync` block**

Append after the existing HRF write (Gap #1) assertion in `RunSelfTestAsync`:

```csharp
        var hrd = MakeTag("HrDInt", "HRD30", TagDataType.DInt, TagAccess.ReadWrite, safeWrite: true);
        var hrdu = MakeTag("HrUInt32", "HRDU40", TagDataType.UInt32, TagAccess.ReadWrite, safeWrite: true);

        await client.WriteTagAsync(hrd, -987654, cancellationToken);
        await client.WriteTagAsync(hrdu, 3000000000u, cancellationToken);
        var bigReadback = await client.ReadTagsAsync(new[] { hrd, hrdu }, cancellationToken);
        AssertGood(bigReadback, "HrDInt");
        AssertGood(bigReadback, "HrUInt32");
        if (System.Convert.ToInt32(bigReadback["HrDInt"].Value, System.Globalization.CultureInfo.InvariantCulture) != -987654)
        {
            throw new InvalidOperationException("Expected HrDInt=-987654 after Modbus write.");
        }
        if (System.Convert.ToUInt32(bigReadback["HrUInt32"].Value, System.Globalization.CultureInfo.InvariantCulture) != 3000000000u)
        {
            throw new InvalidOperationException("Expected HrUInt32=3000000000 after Modbus write.");
        }
```

- [ ] **Step 6.2: Self-test**

```bash
dotnet build EnviroEquipmentFinalEdition.sln
dotnet run --project src/SiemensS7Demo/SiemensS7Demo.csproj -- --self-test
```

Expected: `[PASS] Modbus loopback read/write` (still one line) + exit 0.

- [ ] **Step 6.3: Commit**

```bash
git add src/SiemensS7Demo/Program.cs
git commit -m "test(self-test): cover Modbus HRD + HRDU round-trips"
```

---

## Task 7: PR

- [ ] **Step 7.1: Push + PR**

```bash
git push -u origin feat/gap2-modbus-32bit-int
gh pr create --title "Gap #2: Modbus DInt and UInt32 (HRD / HRDU)" --body "$(cat <<'EOF'
## Summary
- Adds `TagDataType.UInt32` and wires it through S7Address (ByteSize, ValidateAddressKind) and Snap7S7Adapter (Encode/Decode).
- Adds `HRD<n>` (signed 32-bit) and `HRDU<n>` (unsigned 32-bit) Modbus address forms.
- `ModbusTcpAdapter` reads and writes both new types over two consecutive holding registers, applying `WordOrder` from `PlcConnectionOptions` (re-uses the helper from Gap #1).

## Test plan
- [x] `dotnet test EnviroEquipmentFinalEdition.sln` — all green; 10 new tests in ModbusTcpAdapter32BitTests + 4 in ModbusAddress32BitTests + 1 in TagDataTypeTests + 1 in S7Address32BitTests
- [x] `dotnet run -- --self-test` — `[PASS] Modbus loopback read/write` includes HRD and HRDU cases
- [x] No regressions on Gap #1 float path

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 7.2: SendMessage to `team-lead`** with PR URL + test summary.

---

## Self-Review

- [ ] No `gh pr merge` (lead handles it).
- [ ] No emojis.
- [ ] `Convert.ToUInt32` used (NOT `Convert.ToInt32`) for UInt32 — overflow on signed conversion of 3-4 billion values otherwise.
- [ ] `BinaryPrimitives.ReadUInt32BigEndian` / `WriteUInt32BigEndian` used (NOT Int32 versions).
- [ ] All four word orders covered for both DInt and UInt32 round-trips.
