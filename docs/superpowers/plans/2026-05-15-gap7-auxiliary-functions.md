# Gap #7 — Auxiliary Functions (手动辅助功能 / 程序辅助功能) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Represent and load the `手动辅助功能` ("manual auxiliary functions") and `程序辅助功能` ("program auxiliary functions") groups from legacy `addressConfig.xml` files. These groups describe cross-tag relationships: each entry names a control tag and either (a) a paired state tag ("pair mode"), or (b) a bit offset in a status word tag ("bit-offset mode"). Add the `AuxiliaryFunction` record and a transitional carrier `DeviceDefinition.Auxiliaries`, parse the groups from the legacy XML in `TagConfigLoader.LoadLegacy`, and validate the rules from Section 8 of the design spec.

**Architecture:** Add `AuxiliaryFunction` as a `sealed class` (matches spec Section 5 style) in the Models layer. `DeviceDefinition` is the closest existing model to "all tags + metadata for one device" — adding `Auxiliaries` here is the transitional home until Gap #9 introduces `DeviceTemplate`. The `TagConfigLoader.LoadLegacy` method (introduced in Gap #6) is extended to also detect and parse `手动辅助功能`/`程序辅助功能` `<ParamType>` groups. Because those groups don't contribute `TagDefinition` rows (they carry metadata), they are returned separately via a new overload. `ConfigValidationService` gains one new static method `ValidateAuxiliaries` to check the rules. The JSON loader auto-binds `auxiliaries` on `DeviceDefinition` via `System.Text.Json`.

**Transitional placement note:** Gap #9 will introduce `DeviceTemplate` with its own `Auxiliaries` property. For now, `DeviceDefinition.Auxiliaries` is the holder. When Gap #9 lands, a migration step in that plan moves auxiliaries to the template. This plan documents the transitional choice in a code comment.

**Merge order dependency:** This plan depends on Gap #6 (`feat/gap6-address-synthesis`) being merged first, since `TagConfigLoader.LoadLegacy` must exist and the fixtures from Gap #6 will be reused.

**Tech Stack:** C# .NET 8, xunit, FluentAssertions, `System.Xml.Linq`.

**Scope guard:** Only `AuxiliaryFunction` model, `DeviceDefinition.Auxiliaries`, loader parsing, JSON bind, and validation. No UI, no runtime behavior at tag-read time, no changes to `SiemensS7Client`.

**Branch:** `feat/gap7-auxiliary-functions`
**Worktree:** `.claude/worktrees/gap7-auxiliary-functions`
**Base:** `main` after Gap #6 (`feat/gap6-address-synthesis`) is merged.

---

## Legacy XML structure (from spec Section 6 + real file pattern)

```xml
<ParamType GroupName="手动辅助功能">
  <Param ParamName="压缩机">
    <control>压缩机启动</control>
    <state>压缩机运行</state>
  </Param>
  <Param ParamName="加热">
    <control>加热启动</control>
    <state>加热运行</state>
  </Param>
</ParamType>
<ParamType GroupName="程序辅助功能">
  <Param ParamName="段开关量">
    <control>段开关量控制字</control>
    <programbitoffset>3</programbitoffset>
  </Param>
</ParamType>
```

- **Pair mode:** `<control>` + `<state>` → `ControlTagName` + `StateTagName`, `ProgramBitOffset = null`
- **Bit-offset mode:** `<control>` + `<programbitoffset>` → `ControlTagName` + `ProgramBitOffset`, `StateTagName = null`
- `Group` = `GroupName` attribute of `<ParamType>` (Chinese text preserved as-is)

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `src/SiemensS7Demo/Models/AuxiliaryFunction.cs` | `AuxiliaryFunction` sealed class |
| Modify | `src/SiemensS7Demo/Models/DeviceDefinition.cs` | Add `Auxiliaries` list |
| Modify | `src/SiemensS7Demo/Services/TagConfigLoader.cs` | Extend `LoadLegacy` to detect and parse auxiliary groups |
| Modify | `src/SiemensS7Demo/Services/ConfigValidationService.cs` | Add `ValidateAuxiliaries` |
| Create | `tests/EnviroEquipment.Tests/Services/Fixtures/legacy_with_auxiliaries.xml` | Fixture with both ParamType patterns |
| Create | `tests/EnviroEquipment.Tests/Models/AuxiliaryFunctionTests.cs` | Record construction + defaults |
| Create | `tests/EnviroEquipment.Tests/Services/TagConfigLoaderAuxiliaryTests.cs` | Loader parses both modes |
| Create | `tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderAuxiliaryTests.cs` | JSON auto-bind on DeviceDefinition |
| Create | `tests/EnviroEquipment.Tests/Services/ConfigValidationAuxiliaryTests.cs` | Validation rules |

---

## Task 1: `AuxiliaryFunction` model + `DeviceDefinition.Auxiliaries`

**Files:** Create `src/SiemensS7Demo/Models/AuxiliaryFunction.cs`. Modify `src/SiemensS7Demo/Models/DeviceDefinition.cs`. Create `tests/EnviroEquipment.Tests/Models/AuxiliaryFunctionTests.cs`.

- [ ] **Step 1.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Models/AuxiliaryFunctionTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Models;

public class AuxiliaryFunctionTests
{
    [Fact]
    public void PairMode_SetsControlAndStateTagName()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "手动辅助功能",
            ControlTagName = "压缩机启动",
            StateTagName = "压缩机运行"
        };

        aux.Group.Should().Be("手动辅助功能");
        aux.ControlTagName.Should().Be("压缩机启动");
        aux.StateTagName.Should().Be("压缩机运行");
        aux.ProgramBitOffset.Should().BeNull();
    }

    [Fact]
    public void BitOffsetMode_SetsControlAndBitOffset()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "程序辅助功能",
            ControlTagName = "段开关量控制字",
            ProgramBitOffset = 3
        };

        aux.StateTagName.Should().BeNull();
        aux.ProgramBitOffset.Should().Be(3);
    }

    [Fact]
    public void Auxiliaries_DefaultEmptyOnDeviceDefinition()
    {
        var device = new DeviceDefinition();
        device.Auxiliaries.Should().BeEmpty();
    }

    [Fact]
    public void DeviceDefinition_CanCarryAuxiliaries()
    {
        var device = new DeviceDefinition
        {
            Auxiliaries = new System.Collections.Generic.List<AuxiliaryFunction>
            {
                new() { Group = "手动辅助功能", ControlTagName = "X", StateTagName = "Y" }
            }
        };
        device.Auxiliaries.Should().HaveCount(1);
    }
}
```

- [ ] **Step 1.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~AuxiliaryFunctionTests"
```

Expected: compile error — `AuxiliaryFunction` type does not exist; `DeviceDefinition.Auxiliaries` property does not exist.

- [ ] **Step 1.3: Create `AuxiliaryFunction`**

Create `src/SiemensS7Demo/Models/AuxiliaryFunction.cs`:

```csharp
namespace SiemensS7Demo.Models;

/// <summary>
/// Cross-tag metadata for one entry in a legacy XML <c>手动辅助功能</c> or
/// <c>程序辅助功能</c> group.
/// </summary>
/// <remarks>
/// <b>Pair mode</b>: <see cref="ControlTagName"/> + <see cref="StateTagName"/> are both set;
/// <see cref="ProgramBitOffset"/> is null.
/// <b>Bit-offset mode</b>: <see cref="ControlTagName"/> + <see cref="ProgramBitOffset"/> are
/// set; <see cref="StateTagName"/> is null.
///
/// Transitional placement: this class lives on <see cref="DeviceDefinition.Auxiliaries"/>
/// until Gap #9 introduces DeviceTemplate, at which point auxiliaries will migrate to the
/// template. The property name and shape will not change.
/// </remarks>
public sealed class AuxiliaryFunction
{
    /// <summary>GroupName from the legacy XML ParamType, e.g. "手动辅助功能".</summary>
    public required string Group { get; init; }

    /// <summary>Name of the tag that issues the control command (e.g. start/stop).</summary>
    public required string ControlTagName { get; init; }

    /// <summary>Name of the tag that reflects the running state (pair mode only).</summary>
    public string? StateTagName { get; init; }

    /// <summary>Bit offset within a status word tag (bit-offset mode only).</summary>
    public int? ProgramBitOffset { get; init; }
}
```

- [ ] **Step 1.4: Add `Auxiliaries` to `DeviceDefinition`**

In `src/SiemensS7Demo/Models/DeviceDefinition.cs`, after `public List<TagDefinition> Tags { get; init; } = new();` add:

```csharp
    /// <summary>
    /// Auxiliary function metadata loaded from legacy XML 手动辅助功能 / 程序辅助功能 groups.
    /// Transitional home until Gap #9 DeviceTemplate is introduced.
    /// </summary>
    public List<AuxiliaryFunction> Auxiliaries { get; init; } = new();
```

- [ ] **Step 1.5: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~AuxiliaryFunctionTests"
```

Expected: 4 passing.

- [ ] **Step 1.6: Run full suite**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green (no callers of `DeviceDefinition` set `Auxiliaries`; the default empty list is backward-compatible).

- [ ] **Step 1.7: Commit**

```bash
git add src/SiemensS7Demo/Models/AuxiliaryFunction.cs src/SiemensS7Demo/Models/DeviceDefinition.cs tests/EnviroEquipment.Tests/Models/AuxiliaryFunctionTests.cs
git commit -m "feat(models): add AuxiliaryFunction and DeviceDefinition.Auxiliaries"
```

---

## Task 2: Fixture for auxiliary-function parsing

**Files:** Create fixture; update csproj.

- [ ] **Step 2.1: Create fixture**

Create `tests/EnviroEquipment.Tests/Services/Fixtures/legacy_with_auxiliaries.xml`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<root>
  <ParamType GroupName="状态">
    <Param ParamName="压缩机运行">
      <address>9</address>
      <area>v</area>
      <dbnumber>1</dbnumber>
      <type>V</type>
      <deviation>0</deviation>
      <scale>0</scale>
    </Param>
    <Param ParamName="加热运行">
      <address>9</address>
      <area>v</area>
      <dbnumber>1</dbnumber>
      <type>V</type>
      <deviation>1</deviation>
      <scale>0</scale>
    </Param>
    <Param ParamName="段开关量控制字">
      <address>0</address>
      <area>db</area>
      <dbnumber>1</dbnumber>
      <type>HRS（int16）</type>
      <deviation>0</deviation>
      <scale>0</scale>
    </Param>
  </ParamType>
  <ParamType GroupName="输出">
    <Param ParamName="压缩机启动">
      <address>80</address>
      <area></area>
      <dbnumber>0</dbnumber>
      <type>Q</type>
      <deviation>0</deviation>
      <scale>0</scale>
    </Param>
    <Param ParamName="加热启动">
      <address>81</address>
      <area></area>
      <dbnumber>0</dbnumber>
      <type>Q</type>
      <deviation>0</deviation>
      <scale>0</scale>
    </Param>
  </ParamType>
  <ParamType GroupName="手动辅助功能">
    <Param ParamName="压缩机">
      <control>压缩机启动</control>
      <state>压缩机运行</state>
    </Param>
    <Param ParamName="加热">
      <control>加热启动</control>
      <state>加热运行</state>
    </Param>
  </ParamType>
  <ParamType GroupName="程序辅助功能">
    <Param ParamName="段开关量">
      <control>段开关量控制字</control>
      <programbitoffset>3</programbitoffset>
    </Param>
  </ParamType>
</root>
```

- [ ] **Step 2.2: Update csproj**

In `tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj`, add inside the fixtures `<ItemGroup>`:

```xml
    <None Update="Services/Fixtures/legacy_with_auxiliaries.xml" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2.3: Commit fixture**

```bash
git add tests/EnviroEquipment.Tests/Services/Fixtures/legacy_with_auxiliaries.xml tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj
git commit -m "test(fixtures): add legacy XML fixture with auxiliary function groups"
```

---

## Task 3: `TagConfigLoader.LoadLegacy` parses auxiliary groups (TDD)

The loader currently emits only `TagDefinition` rows. Auxiliary groups don't produce tags — they produce `AuxiliaryFunction` entries. To return both without breaking the existing `LoadLegacy(string path)` signature, add an overload `LoadLegacy(string path, out IReadOnlyList<AuxiliaryFunction> auxiliaries)`.

**Files:** Modify `src/SiemensS7Demo/Services/TagConfigLoader.cs`. Create `tests/EnviroEquipment.Tests/Services/TagConfigLoaderAuxiliaryTests.cs`.

- [ ] **Step 3.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Services/TagConfigLoaderAuxiliaryTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class TagConfigLoaderAuxiliaryTests
{
    private static string AuxFixture =>
        Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "legacy_with_auxiliaries.xml");

    [Fact]
    public void LoadLegacy_WithOut_ReturnsFiveTagsAndThreeAuxiliaries()
    {
        var tags = TagConfigLoader.LoadLegacy(AuxFixture, out var auxiliaries);

        tags.Should().HaveCount(5);
        auxiliaries.Should().HaveCount(3);
    }

    [Fact]
    public void LoadLegacy_PairMode_Auxiliary_SetsStateTagName()
    {
        TagConfigLoader.LoadLegacy(AuxFixture, out var auxiliaries);

        var compressor = auxiliaries.Single(a => a.ControlTagName == "压缩机启动");
        compressor.Group.Should().Be("手动辅助功能");
        compressor.StateTagName.Should().Be("压缩机运行");
        compressor.ProgramBitOffset.Should().BeNull();
    }

    [Fact]
    public void LoadLegacy_PairMode_Auxiliary_SecondEntry()
    {
        TagConfigLoader.LoadLegacy(AuxFixture, out var auxiliaries);

        var heater = auxiliaries.Single(a => a.ControlTagName == "加热启动");
        heater.Group.Should().Be("手动辅助功能");
        heater.StateTagName.Should().Be("加热运行");
    }

    [Fact]
    public void LoadLegacy_BitOffsetMode_Auxiliary_SetsProgramBitOffset()
    {
        TagConfigLoader.LoadLegacy(AuxFixture, out var auxiliaries);

        var segment = auxiliaries.Single(a => a.ControlTagName == "段开关量控制字");
        segment.Group.Should().Be("程序辅助功能");
        segment.ProgramBitOffset.Should().Be(3);
        segment.StateTagName.Should().BeNull();
    }

    [Fact]
    public void LoadLegacy_OriginalOverload_SkipsAuxiliaryGroups()
    {
        // The original single-argument overload still returns only TagDefinitions.
        var tags = TagConfigLoader.LoadLegacy(AuxFixture);
        tags.Should().HaveCount(5);
    }
}
```

- [ ] **Step 3.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagConfigLoaderAuxiliaryTests"
```

Expected: compile error — the `out` overload does not exist yet.

- [ ] **Step 3.3: Extend `TagConfigLoader` with auxiliary parsing**

In `src/SiemensS7Demo/Services/TagConfigLoader.cs`, add the new overload and a private helper for auxiliary group detection. Add these methods directly after the existing `LoadLegacy(string configPath)` method:

```csharp
    /// <summary>
    /// Loads a legacy <c>addressConfig.xml</c> file, returning both tag definitions and
    /// auxiliary function metadata from <c>手动辅助功能</c> / <c>程序辅助功能</c> groups.
    /// </summary>
    public static IReadOnlyList<TagDefinition> LoadLegacy(
        string configPath,
        out IReadOnlyList<AuxiliaryFunction> auxiliaries)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Legacy tag configuration file was not found.", configPath);
        }

        var document = XDocument.Load(configPath, LoadOptions.None);
        if (!string.Equals(document.Root?.Name.LocalName, "root", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected legacy XML root element <root>, found <{document.Root?.Name.LocalName}>.");
        }

        var tags = new List<TagDefinition>();
        var auxList = new List<AuxiliaryFunction>();

        foreach (var paramType in document.Root.Elements("ParamType"))
        {
            var groupName = (string?)paramType.Attribute("GroupName") ?? string.Empty;
            if (IsAuxiliaryGroup(groupName))
            {
                auxList.AddRange(ParseAuxiliaryGroup(paramType, groupName));
            }
            else
            {
                foreach (var param in paramType.Elements("Param"))
                {
                    tags.Add(ParseLegacyParam(param, groupName));
                }
            }
        }

        if (tags.Count == 0)
        {
            throw new InvalidOperationException($"No tags found in legacy file '{configPath}'.");
        }

        auxiliaries = auxList;
        return tags;
    }
```

Also update the original single-argument `LoadLegacy` to delegate to the new overload (keeps logic in one place):

```csharp
    public static IReadOnlyList<TagDefinition> LoadLegacy(string configPath)
        => LoadLegacy(configPath, out _);
```

Add these two private helpers after `ParseLegacyParam`:

```csharp
    private static bool IsAuxiliaryGroup(string groupName)
        => groupName.Contains("辅助功能", StringComparison.Ordinal);

    private static IEnumerable<AuxiliaryFunction> ParseAuxiliaryGroup(
        XElement paramType, string groupName)
    {
        foreach (var param in paramType.Elements("Param"))
        {
            var control = param.Element("control")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(control))
            {
                continue; // skip malformed entries silently
            }

            var state = param.Element("state")?.Value?.Trim();
            var bitOffsetText = param.Element("programbitoffset")?.Value?.Trim();
            int? programBitOffset = string.IsNullOrWhiteSpace(bitOffsetText)
                ? null
                : int.Parse(bitOffsetText, CultureInfo.InvariantCulture);

            yield return new AuxiliaryFunction
            {
                Group = groupName,
                ControlTagName = control,
                StateTagName = string.IsNullOrWhiteSpace(state) ? null : state,
                ProgramBitOffset = programBitOffset
            };
        }
    }
```

Add `using System.Collections.Generic;` at the top of the file if not already present (it should be, from the existing `List<TagDefinition>` uses).

- [ ] **Step 3.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagConfigLoaderAuxiliaryTests"
```

Expected: 5 passing.

- [ ] **Step 3.5: Run full suite**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green.

- [ ] **Step 3.6: Commit**

```bash
git add src/SiemensS7Demo/Services/TagConfigLoader.cs tests/EnviroEquipment.Tests/Services/TagConfigLoaderAuxiliaryTests.cs
git commit -m "feat(loader): LoadLegacy overload parses auxiliary function groups"
```

---

## Task 4: JSON auto-bind of `DeviceDefinition.Auxiliaries`

`System.Text.Json` will auto-bind `"auxiliaries": [...]` on `DeviceDefinition` because `ProjectConfigLoader` uses `PropertyNameCaseInsensitive = true`. Pin with a test.

**Files:** Create fixture + test. No production code change.

- [ ] **Step 4.1: Create fixture**

Create `tests/EnviroEquipment.Tests/Services/Fixtures/auxiliaries.project.json`:

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
          "name": "CompressorStart",
          "displayName": "Compressor Start",
          "group": "Output",
          "address": "MW0",
          "dataType": "Bool",
          "unit": ""
        },
        {
          "name": "CompressorRun",
          "displayName": "Compressor Run",
          "group": "Status",
          "address": "MW2",
          "dataType": "Bool",
          "unit": ""
        }
      ],
      "auxiliaries": [
        {
          "group": "手动辅助功能",
          "controlTagName": "CompressorStart",
          "stateTagName": "CompressorRun"
        },
        {
          "group": "程序辅助功能",
          "controlTagName": "CompressorStart",
          "programBitOffset": 3
        }
      ]
    }
  ]
}
```

In `tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj`, add:

```xml
    <None Update="Services/Fixtures/auxiliaries.project.json" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 4.2: Write test**

Create `tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderAuxiliaryTests.cs`:

```csharp
using System.IO;
using FluentAssertions;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ProjectConfigLoaderAuxiliaryTests
{
    [Fact]
    public void Load_BindsAuxiliariesFromJson()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory,
            "Services", "Fixtures", "auxiliaries.project.json");
        var project = ProjectConfigLoader.Load(path);
        var device = project.Devices.Single();

        device.Auxiliaries.Should().HaveCount(2);

        var pairMode = device.Auxiliaries.Single(a => a.StateTagName != null);
        pairMode.Group.Should().Be("手动辅助功能");
        pairMode.ControlTagName.Should().Be("CompressorStart");
        pairMode.StateTagName.Should().Be("CompressorRun");
        pairMode.ProgramBitOffset.Should().BeNull();

        var bitMode = device.Auxiliaries.Single(a => a.ProgramBitOffset.HasValue);
        bitMode.Group.Should().Be("程序辅助功能");
        bitMode.ProgramBitOffset.Should().Be(3);
        bitMode.StateTagName.Should().BeNull();
    }
}
```

- [ ] **Step 4.3: Run, confirm pass on first try**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ProjectConfigLoaderAuxiliaryTests"
```

Expected: 1 passing without any production code change. `System.Text.Json` with `PropertyNameCaseInsensitive = true` binds `auxiliaries` → `Auxiliaries` and `controlTagName` → `ControlTagName` etc. If it fails due to missing `[JsonConstructor]`, add `[JsonInclude]` to the properties on `AuxiliaryFunction`.

- [ ] **Step 4.4: Commit**

```bash
git add tests/EnviroEquipment.Tests/Services/Fixtures/auxiliaries.project.json tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderAuxiliaryTests.cs tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj
git commit -m "test(loader): pin JSON deserialization of DeviceDefinition.Auxiliaries"
```

---

## Task 5: Validation rules

Rules from spec Section 8:
1. At least one of `StateTagName` / `ProgramBitOffset` must be set (or both — implementation allows both but at minimum one is required).
2. Referenced tag names (`ControlTagName`, `StateTagName`) must exist in the provided tag list — **WARNING** (not Error) if missing, because the auxiliary metadata is UI-layer only.
3. `ProgramBitOffset`, if set, must be 0–15 (auxiliary bit-offset mode targets a word-sized status register).

**Files:** Modify `src/SiemensS7Demo/Services/ConfigValidationService.cs`. Create `tests/EnviroEquipment.Tests/Services/ConfigValidationAuxiliaryTests.cs`.

- [ ] **Step 5.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Services/ConfigValidationAuxiliaryTests.cs`:

```csharp
using System.Collections.Generic;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ConfigValidationAuxiliaryTests
{
    private static TagDefinition MakeTag(string name) => new()
    {
        Name = name, DisplayName = name, Group = "g",
        Address = "MW0", DataType = TagDataType.Bool, Unit = ""
    };

    [Fact]
    public void NeitherStateNorBitOffset_IsError()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "手动辅助功能",
            ControlTagName = "CtrlTag"
            // StateTagName = null, ProgramBitOffset = null
        };
        var tags = new[] { MakeTag("CtrlTag") };

        var issues = ConfigValidationService.ValidateAuxiliaries(
            new[] { aux }, tags, "test");

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("StateTagName", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PairMode_BothTagsExist_NoIssues()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "手动辅助功能",
            ControlTagName = "Ctrl",
            StateTagName = "State"
        };
        var tags = new[] { MakeTag("Ctrl"), MakeTag("State") };

        var issues = ConfigValidationService.ValidateAuxiliaries(
            new[] { aux }, tags, "test");

        issues.Should().BeEmpty();
    }

    [Fact]
    public void PairMode_MissingStateTag_IsWarning()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "手动辅助功能",
            ControlTagName = "Ctrl",
            StateTagName = "MissingState"
        };
        var tags = new[] { MakeTag("Ctrl") };  // MissingState not in list

        var issues = ConfigValidationService.ValidateAuxiliaries(
            new[] { aux }, tags, "test");

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Warning &&
            i.Message.Contains("MissingState", System.StringComparison.OrdinalIgnoreCase));
        issues.Should().NotContain(i => i.Severity == ConfigIssueSeverity.Error);
    }

    [Fact]
    public void PairMode_MissingControlTag_IsWarning()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "手动辅助功能",
            ControlTagName = "MissingCtrl",
            StateTagName = "State"
        };
        var tags = new[] { MakeTag("State") };

        var issues = ConfigValidationService.ValidateAuxiliaries(
            new[] { aux }, tags, "test");

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Warning &&
            i.Message.Contains("MissingCtrl", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BitOffsetMode_ValidOffset_NoIssues()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "程序辅助功能",
            ControlTagName = "Ctrl",
            ProgramBitOffset = 7
        };
        var tags = new[] { MakeTag("Ctrl") };

        var issues = ConfigValidationService.ValidateAuxiliaries(
            new[] { aux }, tags, "test");

        issues.Should().NotContain(i => i.Severity == ConfigIssueSeverity.Error);
    }

    [Fact]
    public void BitOffsetMode_OffsetOutOfRange_IsError()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "程序辅助功能",
            ControlTagName = "Ctrl",
            ProgramBitOffset = 16  // 0..15 only
        };
        var tags = new[] { MakeTag("Ctrl") };

        var issues = ConfigValidationService.ValidateAuxiliaries(
            new[] { aux }, tags, "test");

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("ProgramBitOffset", System.StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 5.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ConfigValidationAuxiliaryTests"
```

Expected: compile error — `ConfigValidationService.ValidateAuxiliaries` does not exist yet.

- [ ] **Step 5.3: Add `ValidateAuxiliaries` to `ConfigValidationService`**

In `src/SiemensS7Demo/Services/ConfigValidationService.cs`, add the following public method after `ValidateProject`:

```csharp
    public static IReadOnlyList<ConfigValidationIssue> ValidateAuxiliaries(
        IReadOnlyList<AuxiliaryFunction> auxiliaries,
        IReadOnlyList<TagDefinition> knownTags,
        string scope)
    {
        var issues = new List<ConfigValidationIssue>();
        var tagNames = knownTags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var aux in auxiliaries)
        {
            var auxScope = $"{scope}/{aux.Group}/{aux.ControlTagName}";

            // Rule 1: at least one of StateTagName / ProgramBitOffset must be set.
            if (aux.StateTagName is null && aux.ProgramBitOffset is null)
            {
                issues.Add(Error(auxScope,
                    "AuxiliaryFunction must have at least one of StateTagName or ProgramBitOffset set."));
                continue;
            }

            // Rule 2: bit offset range (0..15 for word-sized status register).
            if (aux.ProgramBitOffset.HasValue &&
                (aux.ProgramBitOffset.Value < 0 || aux.ProgramBitOffset.Value > 15))
            {
                issues.Add(Error(auxScope,
                    $"ProgramBitOffset {aux.ProgramBitOffset.Value} is outside valid range 0..15."));
            }

            // Rule 3: referenced tag names must exist — WARNING if missing (UI metadata only).
            if (!tagNames.Contains(aux.ControlTagName))
            {
                issues.Add(Warning(auxScope,
                    $"ControlTagName '{aux.ControlTagName}' not found in device tag list."));
            }

            if (aux.StateTagName is not null && !tagNames.Contains(aux.StateTagName))
            {
                issues.Add(Warning(auxScope,
                    $"StateTagName '{aux.StateTagName}' not found in device tag list."));
            }
        }

        return issues;
    }
```

- [ ] **Step 5.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ConfigValidationAuxiliaryTests"
```

Expected: 6 passing.

- [ ] **Step 5.5: Run full suite**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green.

- [ ] **Step 5.6: Commit**

```bash
git add src/SiemensS7Demo/Services/ConfigValidationService.cs tests/EnviroEquipment.Tests/Services/ConfigValidationAuxiliaryTests.cs
git commit -m "feat(validation): ValidateAuxiliaries with pair-mode, bit-offset, and tag-ref rules"
```

---

## Task 6: Open the PR

- [ ] **Step 6.1: Full build + test**

```bash
dotnet build EnviroEquipmentFinalEdition.sln
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green.

- [ ] **Step 6.2: Push + PR**

```bash
git push -u origin feat/gap7-auxiliary-functions
gh pr create --title "Gap #7: AuxiliaryFunction model, loader, and validation" --body "$(cat <<'EOF'
## Summary
- Adds `AuxiliaryFunction` sealed class (Group, ControlTagName, StateTagName?, ProgramBitOffset?) per spec Section 5.
- Adds `DeviceDefinition.Auxiliaries` as the transitional carrier (will migrate to DeviceTemplate in Gap #9).
- Extends `TagConfigLoader.LoadLegacy` with an `out` overload that also parses `手动辅助功能` / `程序辅助功能` groups; auxiliary groups are identified by the `辅助功能` substring and produce `AuxiliaryFunction` entries instead of `TagDefinition` rows.
- JSON path: `ProjectConfigLoader` auto-binds `auxiliaries` on `DeviceDefinition` (pinned by test).
- Adds `ConfigValidationService.ValidateAuxiliaries`: error on neither-state-nor-offset; error on bit offset out of 0..15; warning (not error) on missing referenced tag names.

## Merge order
Requires Gap #6 (`feat/gap6-address-synthesis`) merged first (uses `TagConfigLoader.LoadLegacy` and legacy XML fixtures).

## Test plan
- [x] `dotnet test EnviroEquipmentFinalEdition.sln` — all green
- [x] `AuxiliaryFunctionTests` (4): construction, defaults, DeviceDefinition carrier
- [x] `TagConfigLoaderAuxiliaryTests` (5): pair mode, bit-offset mode, group name, tag count, original overload unchanged
- [x] `ProjectConfigLoaderAuxiliaryTests` (1): JSON auto-bind
- [x] `ConfigValidationAuxiliaryTests` (6): neither-set error, both-present ok, missing state warning, missing control warning, valid bit offset ok, out-of-range error

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Self-Review Checklist

- [ ] No emojis in code or commit messages.
- [ ] `AuxiliaryFunction` uses `required` keyword on `Group` and `ControlTagName`; `StateTagName` and `ProgramBitOffset` are nullable.
- [ ] `DeviceDefinition.Auxiliaries` defaults to `new List<AuxiliaryFunction>()` — zero breaking change for existing callers.
- [ ] `IsAuxiliaryGroup` detects by `辅助功能` substring — matches both `手动辅助功能` and `程序辅助功能` and any future variants.
- [ ] Original `LoadLegacy(string)` overload delegates to the new `out` overload via `LoadLegacy(path, out _)`.
- [ ] Validation warnings for missing tag references (not errors) — spec Section 8 explicitly says WARNING.
- [ ] `ValidateAuxiliaries` is a separate public method, not folded into `ValidateTags` — callers invoke it independently with the auxiliary list.
- [ ] JSON `controlTagName` / `stateTagName` / `programBitOffset` bind to the `required` `init`-only properties because `PropertyNameCaseInsensitive = true`.
