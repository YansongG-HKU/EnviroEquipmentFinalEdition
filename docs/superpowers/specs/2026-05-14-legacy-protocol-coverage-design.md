# Legacy Protocol Coverage — Design

- **Status**: Approved
- **Date**: 2026-05-14
- **Owner**: lead (opus-4-7-1m)
- **Scope**: 9 gaps identified after auditing `EnviroEquipmentFinalEdition_202604/Code/Bin/Release/addressProtocol/`
- **Out of scope**: WPF/UI, real-PLC field verification, deployment, alarm/event subsystem

## 1. Context

The legacy reference codebase ships a complete `addressProtocol/` directory:

```
addressProtocol/
├── Siemens/{standardBoxDevice, standardBoxDevice(1500), TemperatureShockBoxDevice, lowAirPressureDevice}/addressConfig.xml
└── Schneider/{standardBoxDevice, TemperatureShockBoxDevice, lowAirPressureDevice}/addressConfig.xml
```

13,213 lines total. Each file is a `<root>` containing `<ParamType GroupName="...">` groups; each group contains `<Param ParamName="...">` entries with fields `address / area / dbnumber / type / deviation / scale / option / DeviationList`.

The current `SiemensS7Demo` project models a flat `TagDefinition` with `Name, Address, DataType, Scale (multiplier), Offset, Access, SafeWrite, Min, Max`. It supports Snap7 (Siemens) and Modbus TCP (Schneider/generic) via `IS7Adapter`.

Gap analysis (see Section 3) identified 9 areas where the legacy XML cannot round-trip through our current model.

## 2. Goals & Non-Goals

**Goals**
- Native C# `TagConfigLoader` can ingest the legacy `addressConfig.xml` files without preprocessing.
- All 7 legacy device configs validate against `ConfigValidationService` after loading.
- Modbus TCP adapter supports 32-bit float and 32-bit int over two consecutive holding registers.
- Snap7 adapter performs batched DB reads (≤PDU window) instead of one-tag-per-roundtrip.
- All work covered by xunit tests in a new `tests/EnviroEquipment.Tests` project; CI-friendly (no real PLC dependency).

**Non-Goals**
- UI consumers. The new `Options` / `BitDerivation` / `AuxiliaryFunction` data are surfaced through models and serializers only.
- Field verification on real Schneider hardware (we have an S7-200 SMART, not a Schneider PLC).
- Rewriting `addressProtocol/*.xml` into our `project.json` shape. The XML is the source of truth; we adapt to it.

## 3. The 9 Gaps

| # | Gap | Layer |
|---|-----|-------|
| 1 | Modbus float (HRF) — 32-bit over two registers | Drivers |
| 2 | Modbus DInt / UInt32 — 32-bit int over two registers | Drivers |
| 3 | `TagDefinition.Options` — enum value→label mapping | Models + Loader |
| 4 | `DeviationList` — single word fans out into N derived bool tags | Models + Drivers |
| 5 | `Scale` divisor semantics — legacy `scale=10` means `raw/10 = engineering` | Models + Loader |
| 6 | Byte-address synthesis — `area=db, dbnumber=1, address=336, type=HRS` → `DB1.DBW336` | Loader |
| 7 | Cross-tag metadata (`手动辅助功能 / 程序辅助功能`) — control/state pairing | Models |
| 8 | Snap7 batch read — merge connected DB regions into one round-trip | Drivers |
| 9 | Device templates — vendor × model × tag dictionary | Models + Project |

## 4. Architecture

Additive only. No existing public type/method signature changes except where noted (one new method on `IS7Adapter`, with a default fall-back implementation in `SiemensS7Client`).

```
┌──────────────────────────────────────────────────────┐
│ Program.cs (unchanged surface)                       │
├──────────────────────────────────────────────────────┤
│ Services                                             │
│   TagConfigLoader        ← legacy XML + modern XML   │
│   ProjectConfigLoader    ← template references       │
│   ConfigValidationService ← validates new fields     │
├──────────────────────────────────────────────────────┤
│ Drivers                                              │
│   IS7Adapter                                         │
│   ├ ReadRawAsync     (existing)                      │
│   └ ReadRawBatchAsync (new, default = foreach)       │
│   SiemensS7Client.ReadTagsAsync                      │
│   └ now expands BitDerivations into multiple values  │
│   Snap7S7Adapter.ReadRawBatchAsync (optimized)       │
│   ModbusTcpAdapter (HRF / HRD support)               │
│   ModbusAddress    (new HRF, HRD, HRDU forms)        │
├──────────────────────────────────────────────────────┤
│ Models                                               │
│   TagDefinition (+ ScaleMode, Options, BitDerivations)│
│   TagOption (new)                                    │
│   BitDerivation (new)                                │
│   AuxiliaryFunction (new)                            │
│   DeviceTemplate (new)                               │
│   PlcConnectionOptions (+ WordOrder)                 │
└──────────────────────────────────────────────────────┘
```

## 5. Data Model

```csharp
public enum ScaleMode { Multiplier, Divisor }   // legacy XML = Divisor

public sealed record TagOption(long Value, string Label);

public sealed record BitDerivation(string Name, int BitOffset, string? DisplayName = null);

public sealed class TagDefinition {
    // existing fields preserved
    public ScaleMode ScaleMode { get; init; } = ScaleMode.Multiplier;
    public IReadOnlyList<TagOption> Options { get; init; } = Array.Empty<TagOption>();
    public IReadOnlyList<BitDerivation> BitDerivations { get; init; } = Array.Empty<BitDerivation>();
}

public sealed class AuxiliaryFunction {
    public required string Group { get; init; }     // "手动辅助功能" / "程序辅助功能"
    public required string ControlTagName { get; init; }
    public string? StateTagName { get; init; }      // pair mode
    public int? ProgramBitOffset { get; init; }     // bit-offset mode (段开关量 word)
}

public sealed class DeviceTemplate {
    public required string Vendor { get; init; }    // "Siemens" / "Schneider"
    public required string Model { get; init; }     // "standardBoxDevice" etc.
    public required IReadOnlyList<TagDefinition> Tags { get; init; }
    public IReadOnlyList<AuxiliaryFunction> Auxiliaries { get; init; } = Array.Empty<AuxiliaryFunction>();
}

public sealed class PlcConnectionOptions {
    // existing fields preserved
    public WordOrder WordOrder { get; init; } = WordOrder.ABCD;
}

public enum WordOrder { ABCD, CDAB, BADC, DCBA }
```

### TagValue fan-out

When `tag.BitDerivations` is non-empty, `SiemensS7Client.ReadTagsAsync` returns:

- one `TagValue` keyed by the tag's own name (the raw word/uint16)
- one `TagValue` per `BitDerivation`, keyed by `BitDerivation.Name`, `DataType=Bool`, value extracted by `(raw >> BitOffset) & 1`

`Options`: when a tag has options and the raw value matches, the corresponding `TagValue.DisplayValue` is set to the option label; otherwise `DisplayValue == Value.ToString()`.

### Scale semantics

- `ScaleMode.Multiplier` (default, existing): `engineering = raw * Scale + Offset`
- `ScaleMode.Divisor` (legacy XML): `engineering = raw / Scale + Offset`; if legacy `scale == 0`, loader stores `Scale=1, ScaleMode=Multiplier` (no scaling)

## 6. Address Synthesis (Gap #6)

Legacy XML for a Siemens int16 PID parameter:

```xml
<Param ParamName="温度加热T0">
  <address>336</address>
  <area>db</area>
  <dbnumber>1</dbnumber>
  <type>HRS（int16）</type>
  <deviation>0</deviation>
  <scale>10</scale>
</Param>
```

Loader synthesizes:

- `Address = "DB1.DBW336"`
- `DataType = Int16`
- `Scale = 10`, `ScaleMode = Divisor`

Legacy XML for a Siemens bit:

```xml
<type>V</type>
<address>9</address>
<deviation>6</deviation>
```

→ `Address = "V9.6"`, `DataType = Bool`. (S7-200 SMART V maps to DB1, already handled by `Snap7S7Adapter`.)

Schneider XML uses `<type>Q</type>` plus a raw register number, no `<area>` / `<dbnumber>`. Loader synthesizes Modbus coil/HR address strings:

- `type=Q, address=80` → `C80` (coil)
- `type=HRS, address=100` → `HR100`
- `type=HRF, address=200` → `HRF200`
- `type=HRU (unsigned int 32-bit), address=400` → `HRDU400`

## 7. Batch Read (Gap #8)

`IS7Adapter` gains:

```csharp
Task<IReadOnlyDictionary<string, object>> ReadRawBatchAsync(
    IReadOnlyList<TagDefinition> tags,
    CancellationToken cancellationToken);
```

Default implementation (in a new `S7AdapterBase` or extension method) loops `ReadRawAsync` so existing adapters keep working without code change.

`Snap7S7Adapter` overrides with the **batch plan algorithm**:

1. Group tags by `(area, dbnumber)` (V is mapped to db1).
2. Within each group, sort by byte offset.
3. Greedy-merge into windows ≤ `MaxPduLength - header (~240 bytes)`; allow up to 16 bytes of slack between adjacent reads (cheaper than two round-trips).
4. Issue one `DBRead` per window.
5. Slice and decode each tag from the appropriate window.
6. Quality is per-tag: a tag whose window read failed gets BAD; other windows still succeed.

`ModbusTcpAdapter` follows the same plan but per Modbus function code (FC01 for coils, FC03 for holding registers), with max 125 registers (FC03 limit) or 2000 coils (FC01 limit).

## 8. Validation

`ConfigValidationService` adds checks:

- `Options`: values must be unique; labels non-empty.
- `BitDerivations`: bit offsets 0–15 (for UInt16 host) or 0–31 (for UInt32 host); names unique within a tag.
- `ScaleMode = Divisor` requires `Scale != 0` (loader normalizes legacy `0` to `Multiplier/1`).
- `AuxiliaryFunction`: at least one of `StateTagName` / `ProgramBitOffset` set; referenced tag names must exist in the device's tag list (warning if missing, not error — UI metadata).

## 9. Testing Strategy

```
tests/EnviroEquipment.Tests/
├── EnviroEquipment.Tests.csproj    (net8.0, xunit, FluentAssertions)
├── Drivers/
│   ├── ModbusAddressTests.cs        Gap #1 #2 — HRF / HRD / HRDU parsing
│   ├── ModbusTcpAdapterTests.cs     Gap #1 #2 — loopback round-trip incl. word order
│   ├── S7AddressTests.cs            Gap #6 — DBW / V / M synthesis
│   ├── Snap7BatchPlanTests.cs       Gap #8 — pure algorithm
│   └── BatchReadIntegrationTests.cs Gap #8 — InMemory adapter, batch path
├── Services/
│   ├── TagConfigLoaderLegacyTests.cs Gap #5 #6 — full addressConfig.xml sample
│   ├── ProjectConfigLoaderTests.cs   Gap #9 — template references
│   └── ConfigValidationServiceTests.cs
└── Models/
    ├── TagDefinitionTests.cs         Gap #3 #4 — Options + BitDerivations
    ├── ScaleModeTests.cs             Gap #5 — divisor semantics, 0-case
    └── AuxiliaryFunctionTests.cs     Gap #7
```

- Strict TDD per gap: failing test → minimal pass → refactor. Each PR shows the red→green sequence in commits.
- `dotnet test` runs in <30 s, no real PLC required.
- The existing `Program.cs --self-test` is preserved; we add a float/dword round-trip case and a batch-read case so smoke catches integration regressions.

## 10. Team & Workflow

| Wave | Agent name | Model | Tasks |
|------|------------|-------|-------|
| 0 | `setup-engineer` | opus-4-7 | #10 — create tests project, wire up sln, one passing smoke test |
| 1 | `modbus-engineer` | opus-4-7 | #1 → #2 |
| 1 | `models-engineer` | opus-4-7 | #3 → #4 |
| 1 | `snap7-engineer` | opus-4-7 | #8 |
| 2 | `loader-engineer` | opus-4-7 | #5 → #6 (after `models-engineer` PRs merged) |
| 2 | `aux-engineer` | opus-4-7 | #7 (after `models-engineer` PRs merged) |
| 3 | `template-engineer` | opus-4-7 | #9 (after Wave 1 + 2 PRs merged) |

- Each teammate works in its own git worktree under `.claude/worktrees/`.
- Each task = one feature branch = one PR. Branch naming: `feat/gap-N-<slug>`.
- Lead reviews every PR with the `code-reviewer` agent before merging; merges are squash-style on `main`.
- Tracked via TaskList; task #10 blocks #1–#9; task #11 (this spec + plans) blocks task #10.

## 11. Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Word-order for Schneider HRF/HRD is wrong | Expose `WordOrder` knob on connection options; default ABCD; loopback tests cover all four. |
| Snap7 batch plan returns wrong data when DB layout has gaps | Algorithm read-only at first; cycle tests on InMemory + S7-200 SMART smoke before declaring "done". |
| `BitDerivations` confusing TagValue keys | Document key namespace clearly; validation rejects collisions with sibling tag names. |
| Legacy XML encoding (UTF-8 BOM, CJK names) breaks Loader | Loader explicitly `Encoding.UTF8`; tests use a real legacy file fixture. |
| Per-gap PR churn | Lead controls merge order; importer/template tasks blocked until model tasks land. |

## 12. Open Questions

None remaining. (Initial open questions on isolation, verification, and merge cadence were resolved during brainstorm; see commit history for trail.)
