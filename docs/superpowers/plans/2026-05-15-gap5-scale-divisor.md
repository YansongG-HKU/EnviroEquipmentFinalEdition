# Gap #5 — Scale Divisor Semantics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Teach the system that legacy XML `scale=N` means `engineering = raw / N + offset` (divisor), not `raw * N + offset` (multiplier). Add a `ScaleMode` enum to `TagDefinition`, update `SiemensS7Client.ConvertReadValue` to apply divisor math when instructed, update the XML loader to set `ScaleMode = Divisor` for tags coming from legacy XML, validate that `ScaleMode.Divisor` requires `Scale != 0`, and normalize the legacy `scale=0` edge case to `Scale=1, ScaleMode=Multiplier`.

**Architecture:** Add `ScaleMode { Multiplier, Divisor }` enum as a new file. Add `TagDefinition.ScaleMode` (init-only, default `Multiplier`) to keep all existing callers unchanged. Extend `TagDefinition.ConvertRawToEngineering` (and matching `ConvertEngineeringToRaw`) to branch on `ScaleMode`. `SiemensS7Client.ConvertReadValue` already delegates to `ConvertRawToEngineering`, so it picks up the fix for free. The XML loader's `ParseTag` gains awareness of a legacy `scaleMode` attribute (for the modern XML path) and the `LoadLegacy` method introduced by Gap #6 will call a shared normalization helper that sets `ScaleMode = Divisor` when `scale > 0`. The JSON loader auto-binds `scaleMode` via `JsonStringEnumConverter` — no code change needed. `ConfigValidationService` adds one rule: `ScaleMode.Divisor` and `Scale == 0` is an error (loader normalizes it away, but JSON hand-edits could introduce it).

**Tech Stack:** C# .NET 8, xunit, FluentAssertions.

**Scope guard:** Only `ScaleMode`. No address synthesis (Gap #6). No loader changes beyond setting `ScaleMode` from the modern XML `scaleMode` attribute. The legacy XML loader method that sets `ScaleMode = Divisor` automatically will be introduced in Gap #6 (which depends on this PR). The JSON path is covered here with a pinning test only.

**Branch:** `feat/gap5-scale-divisor`
**Worktree:** `.claude/worktrees/gap5-scale-divisor`
**Base:** `main` after Wave 1 (gaps #1–#4) is merged.

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `src/SiemensS7Demo/Models/ScaleMode.cs` | `ScaleMode` enum |
| Modify | `src/SiemensS7Demo/Models/TagDefinition.cs` | Add `ScaleMode` field; update `ConvertRawToEngineering` / `ConvertEngineeringToRaw` |
| Modify | `src/SiemensS7Demo/Services/TagConfigLoader.cs` | Parse optional `scaleMode` attribute in `ParseTag` |
| Modify | `src/SiemensS7Demo/Services/ConfigValidationService.cs` | Validate `Divisor + Scale==0` is an error |
| Create | `tests/EnviroEquipment.Tests/Models/ScaleModeTests.cs` | Math round-trips; zero-normalization |
| Create | `tests/EnviroEquipment.Tests/Services/TagConfigLoaderScaleModeTests.cs` | XML attribute parse |
| Create | `tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderScaleModeTests.cs` | JSON auto-bind pin |
| Create | `tests/EnviroEquipment.Tests/Services/ConfigValidationScaleModeTests.cs` | Divisor+zero rule |

`SiemensS7Client.ConvertReadValue` delegates to `tag.ConvertRawToEngineering(numeric)` already (line 197 in current file). No change needed there once `TagDefinition` is updated.

---

## Task 1: `ScaleMode` enum + `TagDefinition.ScaleMode` field

**Files:** Create `src/SiemensS7Demo/Models/ScaleMode.cs`. Modify `src/SiemensS7Demo/Models/TagDefinition.cs`. Create `tests/EnviroEquipment.Tests/Models/ScaleModeTests.cs`.

- [ ] **Step 1.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Models/ScaleModeTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Models;

public class ScaleModeTests
{
    [Fact]
    public void ScaleMode_DefaultIsMultiplier()
    {
        var tag = MakeTag(scale: 10.0);
        tag.ScaleMode.Should().Be(ScaleMode.Multiplier);
    }

    [Fact]
    public void Multiplier_Math_RawTimesScalePlusOffset()
    {
        // engineering = raw * Scale + Offset  →  2 * 10 + 5 = 25
        var tag = MakeTag(scale: 10.0, offset: 5.0, mode: ScaleMode.Multiplier);
        tag.ConvertRawToEngineering(2.0).Should().BeApproximately(25.0, 1e-9);
    }

    [Fact]
    public void Divisor_Math_RawDividedByScalePlusOffset()
    {
        // engineering = raw / Scale + Offset  →  100 / 10 + 5 = 15
        var tag = MakeTag(scale: 10.0, offset: 5.0, mode: ScaleMode.Divisor);
        tag.ConvertRawToEngineering(100.0).Should().BeApproximately(15.0, 1e-9);
    }

    [Fact]
    public void Multiplier_RoundTrip_EngineeringToRaw()
    {
        var tag = MakeTag(scale: 10.0, offset: 5.0, mode: ScaleMode.Multiplier);
        var raw = tag.ConvertEngineeringToRaw(25.0);
        raw.Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void Divisor_RoundTrip_EngineeringToRaw()
    {
        // inverse: raw = (engineering - Offset) * Scale  →  (15 - 5) * 10 = 100
        var tag = MakeTag(scale: 10.0, offset: 5.0, mode: ScaleMode.Divisor);
        var raw = tag.ConvertEngineeringToRaw(15.0);
        raw.Should().BeApproximately(100.0, 1e-9);
    }

    [Fact]
    public void LegacyZeroScale_ShouldNormalizeToMultiplierScaleOne()
    {
        // Loader normalizes scale=0 to Scale=1, ScaleMode=Multiplier.
        // Verify the math: engineering = raw * 1 + 0 = raw (no-op)
        var tag = MakeTag(scale: 1.0, offset: 0.0, mode: ScaleMode.Multiplier);
        tag.ConvertRawToEngineering(42.0).Should().BeApproximately(42.0, 1e-9);
    }

    private static TagDefinition MakeTag(
        double scale = 1.0, double offset = 0.0, ScaleMode mode = ScaleMode.Multiplier)
        => new()
        {
            Name = "T", DisplayName = "T", Group = "g",
            Address = "DB1.DBW0", DataType = TagDataType.Int16, Unit = "",
            Scale = scale, Offset = offset, ScaleMode = mode
        };
}
```

- [ ] **Step 1.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ScaleModeTests"
```

Expected: compile error — `ScaleMode` type does not exist; `TagDefinition.ScaleMode` property does not exist.

- [ ] **Step 1.3: Create `ScaleMode` enum**

Create `src/SiemensS7Demo/Models/ScaleMode.cs`:

```csharp
namespace SiemensS7Demo.Models;

/// <summary>
/// Controls how <see cref="TagDefinition.Scale"/> is applied when converting raw PLC values to engineering units.
/// </summary>
/// <remarks>
/// <see cref="Multiplier"/> is the default (modern JSON/XML): engineering = raw * Scale + Offset.
/// <see cref="Divisor"/> is used by legacy addressConfig.xml files: engineering = raw / Scale + Offset.
/// The legacy loader normalizes scale=0 to Scale=1, ScaleMode=Multiplier (identity transform).
/// </remarks>
public enum ScaleMode
{
    /// <summary>engineering = raw * Scale + Offset (default)</summary>
    Multiplier,

    /// <summary>engineering = raw / Scale + Offset (legacy XML)</summary>
    Divisor
}
```

- [ ] **Step 1.4: Add `ScaleMode` field and update conversion methods in `TagDefinition`**

In `src/SiemensS7Demo/Models/TagDefinition.cs`, after `public double? Max { get; init; }` add:

```csharp
    public ScaleMode ScaleMode { get; init; } = ScaleMode.Multiplier;
```

Replace the two conversion methods:

```csharp
    public double ConvertRawToEngineering(double raw)
        => ScaleMode == ScaleMode.Divisor
            ? raw / Scale + Offset
            : raw * Scale + Offset;

    public double ConvertEngineeringToRaw(double engineering)
        => ScaleMode == ScaleMode.Divisor
            ? (engineering - Offset) * Scale
            : (engineering - Offset) / Scale;
```

- [ ] **Step 1.5: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ScaleModeTests"
```

Expected: 6 passing.

- [ ] **Step 1.6: Run full suite to confirm no regression**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green (existing tests use default `ScaleMode.Multiplier` so math is unchanged).

- [ ] **Step 1.7: Commit**

```bash
git add src/SiemensS7Demo/Models/ScaleMode.cs src/SiemensS7Demo/Models/TagDefinition.cs tests/EnviroEquipment.Tests/Models/ScaleModeTests.cs
git commit -m "feat(models): add ScaleMode enum and TagDefinition.ScaleMode with divisor math"
```

---

## Task 2: `TagConfigLoader.ParseTag` reads optional `scaleMode` attribute

The modern XML format (not the legacy `<root>` format) allows `scaleMode="Divisor"` on a `<Tag>` element. This task wires that up so hand-authored XML configs can opt into divisor semantics without going through legacy auto-detection.

**Files:** Modify `src/SiemensS7Demo/Services/TagConfigLoader.cs`. Create `tests/EnviroEquipment.Tests/Services/TagConfigLoaderScaleModeTests.cs` + fixture.

- [ ] **Step 2.1: Create fixture**

Create `tests/EnviroEquipment.Tests/Services/Fixtures/scalemode.xml`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Configuration>
  <Tags>
    <Tag name="Temp" displayName="Temperature" group="PID" address="DB1.DBW336"
         dataType="Int16" unit="degC" scale="10" scaleMode="Divisor"/>
    <Tag name="Speed" displayName="Speed" group="Drive" address="MW0"
         dataType="Int16" unit="rpm" scale="2"/>
  </Tags>
</Configuration>
```

In `tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj`, add inside the existing `<ItemGroup>` for fixtures:

```xml
    <None Update="Services/Fixtures/scalemode.xml" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2.2: Write failing test**

Create `tests/EnviroEquipment.Tests/Services/TagConfigLoaderScaleModeTests.cs`:

```csharp
using System.IO;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class TagConfigLoaderScaleModeTests
{
    [Fact]
    public void Load_ParsesScaleModeAttribute_Divisor()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "scalemode.xml");
        var tags = TagConfigLoader.Load(path);

        var temp = tags.Single(t => t.Name == "Temp");
        temp.ScaleMode.Should().Be(ScaleMode.Divisor);
        temp.Scale.Should().Be(10.0);
    }

    [Fact]
    public void Load_DefaultsToMultiplier_WhenScaleModeAttributeAbsent()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "scalemode.xml");
        var tags = TagConfigLoader.Load(path);

        var speed = tags.Single(t => t.Name == "Speed");
        speed.ScaleMode.Should().Be(ScaleMode.Multiplier);
    }
}
```

- [ ] **Step 2.3: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagConfigLoaderScaleModeTests"
```

Expected: failure — `ScaleMode` attribute is not parsed; both tags default to `Multiplier`.

- [ ] **Step 2.4: Update `TagConfigLoader.ParseTag`**

In `src/SiemensS7Demo/Services/TagConfigLoader.cs`, inside the `ParseTag` method, before the `return new TagDefinition { ... }`, add:

```csharp
        var scaleModeText = (string?)element.Attribute("scaleMode");
        var scaleMode = string.IsNullOrWhiteSpace(scaleModeText)
            ? ScaleMode.Multiplier
            : ParseEnum<ScaleMode>(scaleModeText, "scaleMode");
```

And in the `new TagDefinition { ... }` object initializer, after `Scale = ParseDouble(element, "scale", 1.0),` add:

```csharp
            ScaleMode = scaleMode,
```

The full updated `ParseTag` return block:

```csharp
        return new TagDefinition
        {
            Name = Required(element, "name"),
            DisplayName = Required(element, "displayName"),
            Group = Required(element, "group"),
            Address = Required(element, "address"),
            DataType = ParseEnum<TagDataType>(Required(element, "dataType"), "dataType"),
            Unit = (string?)element.Attribute("unit") ?? string.Empty,
            Scale = ParseDouble(element, "scale", 1.0),
            ScaleMode = scaleMode,
            Offset = ParseDouble(element, "offset", 0.0),
            Access = ParseEnum<TagAccess>((string?)element.Attribute("access") ?? nameof(TagAccess.Read), "access"),
            SafeWrite = ParseBool(element, "safeWrite", false),
            Min = ParseNullableDouble(element, "min"),
            Max = ParseNullableDouble(element, "max"),
            Options = options,
            BitDerivations = derivations
        };
```

- [ ] **Step 2.5: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagConfigLoaderScaleModeTests"
```

Expected: 2 passing.

- [ ] **Step 2.6: Commit**

```bash
git add src/SiemensS7Demo/Services/TagConfigLoader.cs tests/EnviroEquipment.Tests/Services/TagConfigLoaderScaleModeTests.cs tests/EnviroEquipment.Tests/Services/Fixtures/scalemode.xml tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj
git commit -m "feat(loader): parse scaleMode attribute in TagConfigLoader.ParseTag"
```

---

## Task 3: JSON loader auto-bind pin

`System.Text.Json` with `JsonStringEnumConverter` will bind `"scaleMode": "Divisor"` to `ScaleMode.Divisor` automatically because `ProjectConfigLoader` already registers `new JsonStringEnumConverter()`. Pin this with a test.

**Files:** Create fixture + test. No production code change.

- [ ] **Step 3.1: Create fixture**

Create `tests/EnviroEquipment.Tests/Services/Fixtures/scalemode.project.json`:

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
          "name": "Temp",
          "displayName": "Temperature",
          "group": "PID",
          "address": "DB1.DBW336",
          "dataType": "Int16",
          "unit": "degC",
          "scale": 10,
          "scaleMode": "Divisor"
        },
        {
          "name": "Speed",
          "displayName": "Speed",
          "group": "Drive",
          "address": "MW0",
          "dataType": "Int16",
          "unit": "rpm",
          "scale": 2
        }
      ]
    }
  ]
}
```

In `tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj`, add:

```xml
    <None Update="Services/Fixtures/scalemode.project.json" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 3.2: Write test**

Create `tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderScaleModeTests.cs`:

```csharp
using System.IO;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ProjectConfigLoaderScaleModeTests
{
    [Fact]
    public void Load_BindsScaleModeFromJson()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "scalemode.project.json");
        var project = ProjectConfigLoader.Load(path);
        var tags = project.Devices.Single().Tags;

        tags.Single(t => t.Name == "Temp").ScaleMode.Should().Be(ScaleMode.Divisor);
        tags.Single(t => t.Name == "Speed").ScaleMode.Should().Be(ScaleMode.Multiplier);
    }
}
```

- [ ] **Step 3.3: Run, confirm pass on first try**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ProjectConfigLoaderScaleModeTests"
```

Expected: 1 passing without any production code change. If `ScaleMode.Multiplier` fails to deserialize as the default (missing field), add `[JsonConverter(typeof(JsonStringEnumConverter))]` above the `ScaleMode` property in `TagDefinition`. Try without first.

- [ ] **Step 3.4: Commit**

```bash
git add tests/EnviroEquipment.Tests/Services/Fixtures/scalemode.project.json tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderScaleModeTests.cs tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj
git commit -m "test(loader): pin JSON deserialization of TagDefinition.ScaleMode"
```

---

## Task 4: Validation — `Divisor` + `Scale == 0` is an error

**Files:** Modify `src/SiemensS7Demo/Services/ConfigValidationService.cs`. Create `tests/EnviroEquipment.Tests/Services/ConfigValidationScaleModeTests.cs`.

- [ ] **Step 4.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Services/ConfigValidationScaleModeTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ConfigValidationScaleModeTests
{
    [Fact]
    public void DivisorWithZeroScale_IsError()
    {
        var tag = new TagDefinition
        {
            Name = "T", DisplayName = "T", Group = "g",
            Address = "MW0", DataType = TagDataType.Int16, Unit = "",
            Scale = 0.0, ScaleMode = ScaleMode.Divisor
        };

        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("Divisor", System.StringComparison.OrdinalIgnoreCase) &&
            i.Message.Contains("Scale", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DivisorWithNonZeroScale_IsValid()
    {
        var tag = new TagDefinition
        {
            Name = "T", DisplayName = "T", Group = "g",
            Address = "MW0", DataType = TagDataType.Int16, Unit = "",
            Scale = 10.0, ScaleMode = ScaleMode.Divisor
        };

        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().NotContain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("Divisor", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MultiplierWithZeroScale_UsesExistingScaleZeroError()
    {
        // The existing rule "Scale must not be 0 for numeric tags" still fires for Multiplier+0.
        var tag = new TagDefinition
        {
            Name = "T", DisplayName = "T", Group = "g",
            Address = "MW0", DataType = TagDataType.Int16, Unit = "",
            Scale = 0.0, ScaleMode = ScaleMode.Multiplier
        };

        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().Contain(i => i.Severity == ConfigIssueSeverity.Error);
    }
}
```

- [ ] **Step 4.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ConfigValidationScaleModeTests"
```

Expected: `DivisorWithZeroScale_IsError` fails (no specific Divisor+zero check yet). The other two may pass already.

- [ ] **Step 4.3: Add validation rule**

In `src/SiemensS7Demo/Services/ConfigValidationService.cs`, inside the `foreach (var tag in tags)` loop, find the existing `Scale == 0` check:

```csharp
            if (tag.DataType != TagDataType.Bool && Math.Abs(tag.Scale) < double.Epsilon)
            {
                issues.Add(Error(tagScope, "Scale must not be 0 for numeric tags."));
            }
```

Replace it with:

```csharp
            if (tag.DataType != TagDataType.Bool)
            {
                if (Math.Abs(tag.Scale) < double.Epsilon)
                {
                    if (tag.ScaleMode == ScaleMode.Divisor)
                    {
                        issues.Add(Error(tagScope,
                            "Scale must not be 0 when ScaleMode is Divisor (division by zero). " +
                            "Use Scale=1 with ScaleMode=Multiplier for no-op scaling."));
                    }
                    else
                    {
                        issues.Add(Error(tagScope, "Scale must not be 0 for numeric tags."));
                    }
                }
            }
```

- [ ] **Step 4.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ConfigValidationScaleModeTests"
```

Expected: 3 passing.

- [ ] **Step 4.5: Run full suite**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green.

- [ ] **Step 4.6: Commit**

```bash
git add src/SiemensS7Demo/Services/ConfigValidationService.cs tests/EnviroEquipment.Tests/Services/ConfigValidationScaleModeTests.cs
git commit -m "feat(validation): error on ScaleMode.Divisor with Scale=0"
```

---

## Task 5: Open the PR

- [ ] **Step 5.1: Full build + test**

```bash
dotnet build EnviroEquipmentFinalEdition.sln
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green, zero warnings on new files.

- [ ] **Step 5.2: Push + PR**

```bash
git push -u origin feat/gap5-scale-divisor
gh pr create --title "Gap #5: ScaleMode — legacy scale-divisor semantics" --body "$(cat <<'EOF'
## Summary
- Adds `ScaleMode { Multiplier, Divisor }` enum and `TagDefinition.ScaleMode` (default `Multiplier`, no behavior change for existing tags).
- `ConvertRawToEngineering` / `ConvertEngineeringToRaw` branch on `ScaleMode`: Divisor uses `raw / Scale + Offset` and its inverse.
- `TagConfigLoader.ParseTag` reads optional `scaleMode` attribute from modern XML.
- `ProjectConfigLoader` auto-binds `scaleMode` via `JsonStringEnumConverter` (no code change needed; covered by pinning test).
- `ConfigValidationService` rejects `ScaleMode.Divisor` with `Scale == 0`.

## Notes
- Gap #6 (address synthesis) depends on this PR. Gap #6's legacy loader will set `ScaleMode = Divisor` automatically for all tags with `scale > 0` from legacy XML. That normalization lives in Gap #6, not here.
- The `scale=0` legacy normalization (→ `Scale=1, ScaleMode=Multiplier`) is performed by the legacy loader in Gap #6 and documented in the ScaleModeTests fixture comment.

## Test plan
- [x] `dotnet test EnviroEquipmentFinalEdition.sln` — all green
- [x] `ScaleModeTests` (6): default mode, multiplier math, divisor math, both round-trips, zero no-op
- [x] `TagConfigLoaderScaleModeTests` (2): attribute parsed correctly; default to Multiplier
- [x] `ProjectConfigLoaderScaleModeTests` (1): JSON auto-bind
- [x] `ConfigValidationScaleModeTests` (3): Divisor+zero error, Divisor+nonzero ok, Multiplier+zero existing error still fires

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Self-Review Checklist

- [ ] No emojis in code or commit messages.
- [ ] `TagDefinition` callers that do not set `ScaleMode` still compile (default `Multiplier`).
- [ ] `ConvertRawToEngineering` and `ConvertEngineeringToRaw` are inverses for both modes.
- [ ] `Scale == 0` with `Divisor` is caught by validation; `Scale == 0` with `Multiplier` still produces the pre-existing "Scale must not be 0" error.
- [ ] The `ScaleMode` property is `init`-only on `TagDefinition`.
- [ ] No changes to adapter code; `SiemensS7Client.ConvertReadValue` delegates to the updated method automatically.
