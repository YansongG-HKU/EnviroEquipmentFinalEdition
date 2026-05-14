# Gap #4 — `DeviationList` Bit-Derivation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry the legacy XML `<DeviationList name="..." deviation="N"/>` semantics through our type system end-to-end. One word read produces N+1 `TagValue`s: the raw host value plus one Bool per derivation.

**Architecture:** Add `BitDerivation(Name, BitOffset, DisplayName?)` record and `TagDefinition.BitDerivations` field. `SiemensS7Client.ReadTagsAsync` detects non-empty `BitDerivations` and emits derived `TagValue`s with names equal to `BitDerivation.Name` and `IsQualityGood = host.IsQualityGood`. Loaders parse `<DeviationList>` (XML) and `bitDerivations` (JSON). Validation rejects out-of-range bits and name collisions.

**Tech Stack:** C# .NET 8, xunit, FluentAssertions, `System.Xml.Linq`, `System.Text.Json`.

**Prerequisite:** Gap #3 PR (Options) merged. We re-use the `SiemensS7Client.ReadTagsAsync` extension point that Gap #3 already created. If Gap #3 is not on `main` yet, rebase onto its branch.

**Scope guard:** Only BitDerivations. No address parsing changes, no adapter changes. Host tag must be a `UInt16`/`Int16` (and `UInt32`/`DInt` if Gap #2 is also merged — extend the bit-offset range to 0–31 in that case).

**Branch:** `feat/gap4-bit-derivations`

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `src/SiemensS7Demo/Models/BitDerivation.cs` | `record BitDerivation(string Name, int BitOffset, string? DisplayName = null)` |
| Modify | `src/SiemensS7Demo/Models/TagDefinition.cs` | Add `BitDerivations` field |
| Modify | `src/SiemensS7Demo/Drivers/SiemensS7Client.cs` | Fan-out derived TagValues in ReadTagsAsync |
| Modify | `src/SiemensS7Demo/Services/TagConfigLoader.cs` | Parse `<DeviationList>` children |
| Modify | `src/SiemensS7Demo/Services/ConfigValidationService.cs` | Validate derivations |
| Create | `tests/EnviroEquipment.Tests/Models/BitDerivationTests.cs` | Default empty, deconstruction |
| Create | `tests/EnviroEquipment.Tests/Drivers/SiemensS7ClientBitDerivationsTests.cs` | Fan-out behavior |
| Create | `tests/EnviroEquipment.Tests/Services/TagConfigLoaderBitDerivationsTests.cs` | XML parse |
| Create | `tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderBitDerivationsTests.cs` | JSON parse |
| Create | `tests/EnviroEquipment.Tests/Services/ConfigValidationBitDerivationsTests.cs` | Validation rules |

---

## Task 1: `BitDerivation` record + `TagDefinition.BitDerivations`

**Files:** Create `src/SiemensS7Demo/Models/BitDerivation.cs`. Modify `src/SiemensS7Demo/Models/TagDefinition.cs`. Create `tests/EnviroEquipment.Tests/Models/BitDerivationTests.cs`.

- [ ] **Step 1.1: Failing test**

Create `tests/EnviroEquipment.Tests/Models/BitDerivationTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Models;

public class BitDerivationTests
{
    [Fact]
    public void BitDerivations_DefaultEmpty()
    {
        var tag = new TagDefinition
        {
            Name = "S", DisplayName = "S", Group = "g",
            Address = "MW0", DataType = TagDataType.UInt16, Unit = ""
        };
        tag.BitDerivations.Should().BeEmpty();
    }

    [Fact]
    public void BitDerivation_PreservesNameOffsetDisplayName()
    {
        var bd = new BitDerivation("RunStatus", 3, "Run Status");
        bd.Name.Should().Be("RunStatus");
        bd.BitOffset.Should().Be(3);
        bd.DisplayName.Should().Be("Run Status");
    }

    [Fact]
    public void BitDerivation_DisplayNameDefaultsNull()
    {
        var bd = new BitDerivation("X", 0);
        bd.DisplayName.Should().BeNull();
    }
}
```

- [ ] **Step 1.2: Confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~BitDerivationTests"
```

Expected: compile error.

- [ ] **Step 1.3: Create `BitDerivation`**

Create `src/SiemensS7Demo/Models/BitDerivation.cs`:

```csharp
namespace SiemensS7Demo.Models;

public sealed record BitDerivation(string Name, int BitOffset, string? DisplayName = null);
```

- [ ] **Step 1.4: Extend `TagDefinition`**

In `src/SiemensS7Demo/Models/TagDefinition.cs`, after `public IReadOnlyList<TagOption> Options { get; init; } = ...` (added by Gap #3), append:

```csharp
    public IReadOnlyList<BitDerivation> BitDerivations { get; init; } = System.Array.Empty<BitDerivation>();
```

- [ ] **Step 1.5: Pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~BitDerivationTests"
```

Expected: 3 passing.

- [ ] **Step 1.6: Commit**

```bash
git add src/SiemensS7Demo/Models/BitDerivation.cs src/SiemensS7Demo/Models/TagDefinition.cs tests/EnviroEquipment.Tests/Models/BitDerivationTests.cs
git commit -m "feat(models): add BitDerivation record and TagDefinition.BitDerivations"
```

---

## Task 2: `SiemensS7Client.ReadTagsAsync` fans out derived bits

**Files:** Modify `src/SiemensS7Demo/Drivers/SiemensS7Client.cs`. Create `tests/EnviroEquipment.Tests/Drivers/SiemensS7ClientBitDerivationsTests.cs`.

- [ ] **Step 2.1: Failing tests**

Create `tests/EnviroEquipment.Tests/Drivers/SiemensS7ClientBitDerivationsTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class SiemensS7ClientBitDerivationsTests
{
    [Fact]
    public async Task ReadTagsAsync_EmitsDerivedBoolForEachBitDerivation()
    {
        var options = new PlcConnectionOptions { Name = "T", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new InMemoryS7Adapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(CancellationToken.None);

        // Host word with bits 0 and 3 set.
        var host = new TagDefinition
        {
            Name = "Status", DisplayName = "Status", Group = "g",
            Address = "MW0", DataType = TagDataType.UInt16, Unit = "",
            BitDerivations = new[]
            {
                new BitDerivation("HeatRunning", 0),
                new BitDerivation("CoolRunning", 3, "Cool Running")
            }
        };

        await adapter.WriteRawAsync(host, (ushort)0b0000_0000_0000_1001, CancellationToken.None);

        var values = await client.ReadTagsAsync(new[] { host }, CancellationToken.None);

        values.Should().ContainKeys("Status", "HeatRunning", "CoolRunning");
        values["Status"].Value.Should().Be((double)9); // raw 9 carried as engineering after Scale=1.0
        values["HeatRunning"].Value.Should().Be(true);
        values["HeatRunning"].IsQualityGood.Should().BeTrue();
        values["CoolRunning"].Value.Should().Be(true);
        values["CoolRunning"].DisplayName.Should().Be("Cool Running");
    }

    [Fact]
    public async Task ReadTagsAsync_DerivedBoolIsFalseWhenBitNotSet()
    {
        var options = new PlcConnectionOptions { Name = "T", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new InMemoryS7Adapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(CancellationToken.None);

        var host = new TagDefinition
        {
            Name = "Status", DisplayName = "Status", Group = "g",
            Address = "MW0", DataType = TagDataType.UInt16, Unit = "",
            BitDerivations = new[] { new BitDerivation("Bit2", 2) }
        };
        await adapter.WriteRawAsync(host, (ushort)0, CancellationToken.None);

        var values = await client.ReadTagsAsync(new[] { host }, CancellationToken.None);
        values["Bit2"].Value.Should().Be(false);
        values["Bit2"].IsQualityGood.Should().BeTrue();
    }

    [Fact]
    public async Task ReadTagsAsync_DerivedBoolPropagatesBadQualityFromHost()
    {
        var options = new PlcConnectionOptions { Name = "T", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new ThrowingAdapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(CancellationToken.None);

        var host = new TagDefinition
        {
            Name = "Status", DisplayName = "Status", Group = "g",
            Address = "MW0", DataType = TagDataType.UInt16, Unit = "",
            BitDerivations = new[] { new BitDerivation("Bit0", 0) }
        };

        var values = await client.ReadTagsAsync(new[] { host }, CancellationToken.None);
        values["Status"].IsQualityGood.Should().BeFalse();
        values["Bit0"].IsQualityGood.Should().BeFalse();
        values["Bit0"].QualityMessage.Should().NotBeNullOrEmpty();
    }

    private sealed class ThrowingAdapter : IS7Adapter
    {
        public bool IsConnected => true;
        public Task ConnectAsync(PlcConnectionOptions options, CancellationToken ct) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<PlcDeviceInfo> GetDeviceInfoAsync(PlcConnectionOptions options, CancellationToken ct)
            => Task.FromResult(new PlcDeviceInfo { TimestampUtc = System.DateTime.UtcNow, IpAddress = "", Port = 0, Rack = 0, Slot = 0, ConnectionType = "", ConfiguredCpuType = "" });
        public Task<object> ReadRawAsync(TagDefinition tag, CancellationToken ct)
            => throw new System.IO.IOException("simulated failure");
        public Task WriteRawAsync(TagDefinition tag, object value, CancellationToken ct) => Task.CompletedTask;
    }
}
```

- [ ] **Step 2.2: Confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~SiemensS7ClientBitDerivationsTests"
```

Expected: 3 failing (no derivations are emitted).

- [ ] **Step 2.3: Update `SiemensS7Client.ReadTagsAsync`**

In `src/SiemensS7Demo/Drivers/SiemensS7Client.cs`, after the existing host `output[tag.Name] = new TagValue { ... }` blocks (both success and exception branches), add a fan-out helper. The new logic per tag:

```csharp
                foreach (var derivation in tag.BitDerivations)
                {
                    var derived = BuildDerivedTagValue(tag, derivation, output[tag.Name]);
                    output[derivation.Name] = derived;
                }
```

Place this AFTER both the try-success path AND the catch-bad-quality path so it runs in both. The cleanest way: extract a helper.

Replace the inner `foreach (var tag in tags)` loop body in `ReadTagsAsync` (after Gap #3 changes) with:

```csharp
            foreach (var tag in tags)
            {
                TagValue hostValue;
                try
                {
                    var raw = await _adapter.ReadRawAsync(tag, cancellationToken);
                    var converted = ConvertReadValue(tag, raw);
                    string? displayValue = null;
                    if (tag.Options.Count > 0)
                    {
                        var rawLong = ToOptionKey(raw);
                        if (rawLong.HasValue && tag.TryGetOptionLabel(rawLong.Value, out var label))
                        {
                            displayValue = label;
                        }
                    }
                    hostValue = new TagValue
                    {
                        Name = tag.Name,
                        DisplayName = tag.DisplayName,
                        Address = tag.Address,
                        Unit = tag.Unit,
                        RawValue = raw,
                        Value = converted,
                        DisplayValue = displayValue,
                        TimestampUtc = DateTime.UtcNow,
                        IsQualityGood = true
                    };
                }
                catch (Exception ex)
                {
                    hostValue = new TagValue
                    {
                        Name = tag.Name,
                        DisplayName = tag.DisplayName,
                        Address = tag.Address,
                        Unit = tag.Unit,
                        Value = string.Empty,
                        TimestampUtc = DateTime.UtcNow,
                        IsQualityGood = false,
                        QualityMessage = ex.Message
                    };
                }

                output[tag.Name] = hostValue;
                foreach (var derivation in tag.BitDerivations)
                {
                    output[derivation.Name] = BuildDerivedTagValue(tag, derivation, hostValue);
                }
            }
```

Add the helper at the bottom of the class:

```csharp
    private static TagValue BuildDerivedTagValue(TagDefinition host, BitDerivation derivation, TagValue hostValue)
    {
        if (!hostValue.IsQualityGood)
        {
            return new TagValue
            {
                Name = derivation.Name,
                DisplayName = derivation.DisplayName ?? derivation.Name,
                Address = $"{host.Address}.{derivation.BitOffset}",
                Unit = string.Empty,
                Value = string.Empty,
                TimestampUtc = hostValue.TimestampUtc,
                IsQualityGood = false,
                QualityMessage = hostValue.QualityMessage
            };
        }

        var rawLong = ToOptionKey(hostValue.RawValue ?? 0) ?? 0;
        var bit = ((rawLong >> derivation.BitOffset) & 1L) == 1L;

        return new TagValue
        {
            Name = derivation.Name,
            DisplayName = derivation.DisplayName ?? derivation.Name,
            Address = $"{host.Address}.{derivation.BitOffset}",
            Unit = string.Empty,
            RawValue = bit,
            Value = bit,
            TimestampUtc = hostValue.TimestampUtc,
            IsQualityGood = true
        };
    }
```

Reuses `ToOptionKey` from Gap #3 (it accepts ushort/short/int/uint/long → long). If Gap #3 isn't merged yet, you need to add that helper here.

**Note:** if the host tag and a derivation share a name, the derivation overwrites the host in the output dictionary. Validation (Task 5) catches this case before runtime, but the runtime is permissive.

- [ ] **Step 2.4: Pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~SiemensS7ClientBitDerivationsTests"
```

Expected: 3 passing.

- [ ] **Step 2.5: Commit**

```bash
git add src/SiemensS7Demo/Drivers/SiemensS7Client.cs tests/EnviroEquipment.Tests/Drivers/SiemensS7ClientBitDerivationsTests.cs
git commit -m "feat(client): fan out BitDerivations into derived TagValue per host read"
```

---

## Task 3: XML loader parses `<DeviationList>`

**Files:** Modify `src/SiemensS7Demo/Services/TagConfigLoader.cs`. Create `tests/EnviroEquipment.Tests/Services/TagConfigLoaderBitDerivationsTests.cs` + fixture.

- [ ] **Step 3.1: Fixture**

Create `tests/EnviroEquipment.Tests/Services/Fixtures/deviations.xml`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Configuration>
  <Tags>
    <Tag name="StatusWord" displayName="Status Word" group="Status" address="MW0" dataType="UInt16" unit="">
      <DeviationList name="HeatRunning" deviation="0"/>
      <DeviationList name="CoolRunning" deviation="3" displayName="Cool Running"/>
    </Tag>
  </Tags>
</Configuration>
```

In `tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj`, ensure the fixture is copied (the csproj's `<ItemGroup>` for `<None Update>` should already pattern-match `Services/Fixtures/*.xml`; if not, add):

```xml
    <None Update="Services/Fixtures/deviations.xml" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 3.2: Failing test**

Create `tests/EnviroEquipment.Tests/Services/TagConfigLoaderBitDerivationsTests.cs`:

```csharp
using System.IO;
using System.Linq;
using FluentAssertions;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class TagConfigLoaderBitDerivationsTests
{
    [Fact]
    public void Load_ParsesDeviationListChildren()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "deviations.xml");
        var tag = TagConfigLoader.Load(path).Single();

        tag.BitDerivations.Should().HaveCount(2);
        tag.BitDerivations[0].Name.Should().Be("HeatRunning");
        tag.BitDerivations[0].BitOffset.Should().Be(0);
        tag.BitDerivations[0].DisplayName.Should().BeNull();
        tag.BitDerivations[1].Name.Should().Be("CoolRunning");
        tag.BitDerivations[1].BitOffset.Should().Be(3);
        tag.BitDerivations[1].DisplayName.Should().Be("Cool Running");
    }
}
```

- [ ] **Step 3.3: Confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagConfigLoaderBitDerivationsTests"
```

Expected: failure (BitDerivations empty).

- [ ] **Step 3.4: Update `TagConfigLoader.ParseTag`**

In `src/SiemensS7Demo/Services/TagConfigLoader.cs`, alongside the `options = element.Elements("Option")...` block added by Gap #3, add:

```csharp
        var derivations = element.Elements("DeviationList")
            .Select(d => new BitDerivation(
                Required(d, "name"),
                int.Parse(Required(d, "deviation"), CultureInfo.InvariantCulture),
                (string?)d.Attribute("displayName")))
            .ToList();
```

And in the `new TagDefinition { ... }` initializer, add `BitDerivations = derivations`.

- [ ] **Step 3.5: Pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagConfigLoaderBitDerivationsTests"
```

Expected: 1 passing.

- [ ] **Step 3.6: Commit**

```bash
git add src/SiemensS7Demo/Services/TagConfigLoader.cs tests/EnviroEquipment.Tests/Services/TagConfigLoaderBitDerivationsTests.cs tests/EnviroEquipment.Tests/Services/Fixtures/deviations.xml tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj
git commit -m "feat(loader): parse <DeviationList> into TagDefinition.BitDerivations"
```

---

## Task 4: JSON loader path proof

`System.Text.Json` should bind `"bitDerivations": [{"name": "...", "bitOffset": N, "displayName": "..."}]` to `IReadOnlyList<BitDerivation>` because `BitDerivation` is a positional record with parameter names `Name`, `BitOffset`, `DisplayName`. Pin it with a test.

- [ ] **Step 4.1: Fixture**

Create `tests/EnviroEquipment.Tests/Services/Fixtures/deviations.project.json`:

```json
{
  "projectId": "test",
  "projectName": "test",
  "devices": [
    {
      "id": "dev",
      "name": "Dev",
      "enabled": true,
      "protocol": "mock",
      "ip": "127.0.0.1",
      "port": 102,
      "cpuType": "Mock",
      "pollingIntervalMs": 1000,
      "tags": [
        {
          "name": "StatusWord",
          "displayName": "Status Word",
          "group": "Status",
          "address": "MW0",
          "dataType": "UInt16",
          "unit": "",
          "bitDerivations": [
            { "name": "HeatRunning", "bitOffset": 0 },
            { "name": "CoolRunning", "bitOffset": 3, "displayName": "Cool Running" }
          ]
        }
      ]
    }
  ]
}
```

Add to csproj:
```xml
    <None Update="Services/Fixtures/deviations.project.json" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 4.2: Test**

Create `tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderBitDerivationsTests.cs`:

```csharp
using System.IO;
using System.Linq;
using FluentAssertions;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ProjectConfigLoaderBitDerivationsTests
{
    [Fact]
    public void Load_BindsBitDerivationsFromJson()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "deviations.project.json");
        var project = ProjectConfigLoader.Load(path);
        var tag = project.Devices.Single().Tags.Single();

        tag.BitDerivations.Should().HaveCount(2);
        tag.BitDerivations[0].Name.Should().Be("HeatRunning");
        tag.BitDerivations[0].BitOffset.Should().Be(0);
        tag.BitDerivations[1].DisplayName.Should().Be("Cool Running");
    }
}
```

- [ ] **Step 4.3: Run, expect pass on first try**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ProjectConfigLoaderBitDerivationsTests"
```

If it fails, add `[JsonPropertyName]` attributes to `BitDerivation` or `JsonInclude`. Try without first.

- [ ] **Step 4.4: Commit**

```bash
git add tests/EnviroEquipment.Tests/Services/Fixtures/deviations.project.json tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderBitDerivationsTests.cs tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj
git commit -m "test(loader): pin JSON binding of TagDefinition.BitDerivations"
```

---

## Task 5: Validation rules

**Files:** Modify `src/SiemensS7Demo/Services/ConfigValidationService.cs`. Create `tests/EnviroEquipment.Tests/Services/ConfigValidationBitDerivationsTests.cs`.

Rules:
- Bit offset must be 0..15 for UInt16/Int16 host, 0..31 for UInt32/DInt host, otherwise reject.
- Names must be unique within a tag.
- Names must not collide with any sibling tag in the same device's tag list.

- [ ] **Step 5.1: Failing tests**

Create `tests/EnviroEquipment.Tests/Services/ConfigValidationBitDerivationsTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ConfigValidationBitDerivationsTests
{
    [Fact]
    public void BitOffsetOutOfRange_ForUInt16_IsRejected()
    {
        var tag = Make("X", "MW0", TagDataType.UInt16,
            new[] { new BitDerivation("Bad", 16) });
        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().Contain(i => i.Severity == ConfigIssueSeverity.Error && i.Message.Contains("BitOffset", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BitOffset_AllowedUpTo31_ForUInt32_Host()
    {
        var tag = Make("X", "MD0", TagDataType.UInt32,
            new[] { new BitDerivation("HighBit", 31) });
        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().NotContain(i => i.Severity == ConfigIssueSeverity.Error);
    }

    [Fact]
    public void DuplicateDerivationNames_AreFlagged()
    {
        var tag = Make("X", "MW0", TagDataType.UInt16,
            new[] { new BitDerivation("Same", 0), new BitDerivation("Same", 1) });
        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().Contain(i => i.Severity == ConfigIssueSeverity.Error && i.Message.Contains("duplicate", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DerivationNameCollidingWithSiblingTag_IsFlagged()
    {
        var host = Make("X", "MW0", TagDataType.UInt16,
            new[] { new BitDerivation("Sibling", 0) });
        var sibling = Make("Sibling", "MW2", TagDataType.UInt16, System.Array.Empty<BitDerivation>());
        var issues = ConfigValidationService.ValidateTags(new[] { host, sibling }, "mock", "test");
        issues.Should().Contain(i => i.Severity == ConfigIssueSeverity.Error && i.Message.Contains("collides", System.StringComparison.OrdinalIgnoreCase));
    }

    private static TagDefinition Make(string name, string address, TagDataType type, BitDerivation[] derivations) => new()
    {
        Name = name, DisplayName = name, Group = "g",
        Address = address, DataType = type, Unit = "",
        BitDerivations = derivations
    };
}
```

- [ ] **Step 5.2: Confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ConfigValidationBitDerivationsTests"
```

Expected: 4 failing (no validation logic yet).

- [ ] **Step 5.3: Add validation**

In `ConfigValidationService.ValidateTags`, after the Options-validation block from Gap #3, add:

```csharp
            if (tag.BitDerivations.Count > 0)
            {
                var maxBit = tag.DataType switch
                {
                    TagDataType.Int16 or TagDataType.UInt16 => 15,
                    TagDataType.DInt or TagDataType.UInt32 => 31,
                    _ => -1
                };
                if (maxBit < 0)
                {
                    issues.Add(Error(tagScope, $"BitDerivations are only valid on 16-bit or 32-bit integer host tags; '{tag.Name}' is {tag.DataType}."));
                }
                else
                {
                    var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var derivation in tag.BitDerivations)
                    {
                        if (derivation.BitOffset < 0 || derivation.BitOffset > maxBit)
                        {
                            issues.Add(Error(tagScope, $"BitOffset {derivation.BitOffset} out of range 0..{maxBit}."));
                        }
                        if (string.IsNullOrWhiteSpace(derivation.Name))
                        {
                            issues.Add(Error(tagScope, "Empty BitDerivation name."));
                        }
                        else if (!seenNames.Add(derivation.Name))
                        {
                            issues.Add(Error(tagScope, $"Duplicate BitDerivation name '{derivation.Name}'."));
                        }
                    }
                }
            }
```

For the sibling-collision check, before the `foreach (var tag in tags)` loop, gather all tag names and all derivation names; flag intersections. Add this block at the start of `ValidateTags`, right after the existing duplicate-name check:

```csharp
        var siblingNames = tags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            foreach (var derivation in tag.BitDerivations)
            {
                if (!string.IsNullOrWhiteSpace(derivation.Name)
                    && !derivation.Name.Equals(tag.Name, StringComparison.OrdinalIgnoreCase)
                    && siblingNames.Contains(derivation.Name))
                {
                    issues.Add(Error($"{scope}/{tag.Name}",
                        $"BitDerivation name '{derivation.Name}' collides with sibling tag '{derivation.Name}'."));
                }
            }
        }
```

- [ ] **Step 5.4: Pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ConfigValidationBitDerivationsTests"
```

Expected: 4 passing.

- [ ] **Step 5.5: Commit**

```bash
git add src/SiemensS7Demo/Services/ConfigValidationService.cs tests/EnviroEquipment.Tests/Services/ConfigValidationBitDerivationsTests.cs
git commit -m "feat(validation): bound-check and uniqueness for BitDerivations"
```

---

## Task 6: PR

- [ ] **Step 6.1: Verify**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
dotnet build EnviroEquipmentFinalEdition.sln
```

Expected: all green.

- [ ] **Step 6.2: Push + PR**

```bash
git push -u origin feat/gap4-bit-derivations
gh pr create --title "Gap #4: BitDerivations — one word fans out into N derived bool tags" --body "$(cat <<'EOF'
## Summary
- Adds `BitDerivation(Name, BitOffset, DisplayName?)` record and `TagDefinition.BitDerivations` (default empty).
- `SiemensS7Client.ReadTagsAsync` fans out: every host read on a tag with non-empty `BitDerivations` produces one host `TagValue` plus one derived bool `TagValue` per derivation. Quality propagates from the host.
- XML loader parses `<DeviationList name="..." deviation="N"/>` children; JSON loader binds `bitDerivations` array.
- Validation: bit offset in range (0..15 for 16-bit hosts, 0..31 for 32-bit hosts), names unique within tag, names don't collide with sibling tag names.

## Test plan
- [x] `dotnet test EnviroEquipmentFinalEdition.sln` — all green
- [x] New tests: BitDerivationTests (3), SiemensS7ClientBitDerivationsTests (3), TagConfigLoaderBitDerivationsTests (1), ProjectConfigLoaderBitDerivationsTests (1), ConfigValidationBitDerivationsTests (4)
- [x] Legacy `addressConfig.xml` `<DeviationList>` semantics now round-trip through the Loader

## Compatibility
- Tags without BitDerivations: zero behavior change.
- Tags with BitDerivations: ReadTagsAsync now returns `len(tags) + sum(BitDerivations)` values instead of `len(tags)`. Callers iterating `values.Values` will see additional entries; this is intentional.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 6.3: SendMessage to `team-lead`** with PR URL.

---

## Self-Review

- [ ] No emojis. No `gh pr merge`. No `--force`.
- [ ] Derived TagValues carry `IsQualityGood == hostValue.IsQualityGood`.
- [ ] Derived TagValue address is `"{host.Address}.{bit}"` so users can visually trace it.
- [ ] Validation runs before any read; bad derivations are caught at config-load time, not at runtime.
- [ ] `ToOptionKey` accepts ushort/short/int/uint/long — confirm before relying on it; if Gap #3 is not merged, you may need to duplicate it as a private helper.

---

## Open Items (not in this PR)

- Bit-offset 0..63 for UInt64 hosts — not currently a supported data type.
- Writing a single bit back to the host word (read-modify-write) — currently writes go through the host tag as a UInt16/UInt32; per-bit writes are not exposed.
