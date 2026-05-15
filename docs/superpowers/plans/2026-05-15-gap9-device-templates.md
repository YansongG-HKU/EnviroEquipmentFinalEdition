# Gap #9 — Device Templates (Vendor x Model x Tag Dictionary) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Define a `DeviceTemplate` (vendor x model x tags + auxiliaries) once in the project JSON, then let `DeviceDefinition` entries reference that template by a `"vendor/model"` key. `ProjectConfigLoader.Load` resolves references at load time so every downstream consumer (`SiemensS7Client`, `Snap7BatchPlan`, `ConfigValidationService`, etc.) sees fully-realized `Tags` and `Auxiliaries` arrays — template resolution is transparent to runtime code.

**Architecture:** Add `DeviceTemplate` as a new `sealed class` in the Models layer, with `Vendor`, `Model`, `Tags`, and `Auxiliaries` (defaults to empty — backward-compatible). Add `ProjectDefinition.Templates` (default empty list). Add `DeviceDefinition.TemplateRef` (nullable string). The loader's `Load` method gains a post-process step that, for every device whose `TemplateRef` is non-null, looks up the matching template by `"Vendor/Model"` and copies its tags and auxiliaries into the device (using a synthesized `DeviceDefinition`). `ConfigValidationService` gains `ValidateTemplates` covering template-level tag validation and `TemplateRef` resolvability. Template-to-template references (cycles) are explicitly not supported; the loader rejects any `DeviceTemplate.Tags` that itself contains a `TemplateRef`.

**Conflict policy — REJECT:** If a device sets both `templateRef` and a non-empty `tags` array, `ProjectConfigLoader.Load` throws `InvalidOperationException`. This is the simpler, safer policy: templates are all-or-nothing. Per-device tag overrides are out of scope for this gap. The policy is documented in a code comment and enforced with a dedicated validation path.

**Tech Stack:** C# .NET 8, xunit, FluentAssertions, `System.Text.Json`.

**Scope guard:** Only template definition, reference resolution in `ProjectConfigLoader`, and `ConfigValidationService.ValidateTemplates`. No changes to `SiemensS7Client`, `Snap7BatchPlan`, `TagConfigLoader`, or any adapter. `DeviceDefinition.Auxiliaries` already exists (Gap #7); this plan migrates auxiliaries from template into the resolved device — no shape change needed.

**Branch:** `feat/gap9-device-templates`
**Worktree:** `.claude/worktrees/gap9-device-templates`
**Base:** `main` after Wave 1 + 1.5 + 2 (gaps #1–#8) are merged.

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `src/SiemensS7Demo/Models/DeviceTemplate.cs` | `DeviceTemplate` sealed class |
| Modify | `src/SiemensS7Demo/Models/ProjectDefinition.cs` | Add `Templates` list |
| Modify | `src/SiemensS7Demo/Models/DeviceDefinition.cs` | Add `TemplateRef` nullable string |
| Modify | `src/SiemensS7Demo/Services/ProjectConfigLoader.cs` | Resolve template references post-parse; update zero-tags guard |
| Modify | `src/SiemensS7Demo/Services/ConfigValidationService.cs` | Add `ValidateTemplates`; update `ValidateProject` to call it |
| Create | `tests/EnviroEquipment.Tests/Models/DeviceTemplateTests.cs` | Model construction + defaults |
| Create | `tests/EnviroEquipment.Tests/Services/Fixtures/templates.project.json` | Full project fixture with templates + reference devices |
| Create | `tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderTemplateTests.cs` | Loader resolution, missing-template error, conflict error |
| Create | `tests/EnviroEquipment.Tests/Services/ConfigValidationTemplateTests.cs` | Template-level validation rules |

---

## Task 1: `DeviceTemplate` model

**Files:** Create `src/SiemensS7Demo/Models/DeviceTemplate.cs`. Create `tests/EnviroEquipment.Tests/Models/DeviceTemplateTests.cs`.

- [ ] **Step 1.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Models/DeviceTemplateTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Models;

public class DeviceTemplateTests
{
    [Fact]
    public void DeviceTemplate_RequiredFields_CanBeSet()
    {
        var template = new DeviceTemplate
        {
            Vendor = "Siemens",
            Model = "standardBoxDevice",
            Tags = new List<TagDefinition>
            {
                new()
                {
                    Name = "Temp", DisplayName = "Temperature", Group = "PID",
                    Address = "DB1.DBW336", DataType = TagDataType.Int16, Unit = "degC",
                    Scale = 10.0, ScaleMode = ScaleMode.Divisor
                }
            }
        };

        template.Vendor.Should().Be("Siemens");
        template.Model.Should().Be("standardBoxDevice");
        template.Tags.Should().HaveCount(1);
    }

    [Fact]
    public void DeviceTemplate_Auxiliaries_DefaultEmpty()
    {
        var template = new DeviceTemplate
        {
            Vendor = "Schneider",
            Model = "standardBoxDevice",
            Tags = new List<TagDefinition>()
        };

        template.Auxiliaries.Should().BeEmpty();
    }

    [Fact]
    public void DeviceTemplate_Key_IsVendorSlashModel()
    {
        var template = new DeviceTemplate
        {
            Vendor = "Siemens",
            Model = "TemperatureShockBoxDevice",
            Tags = new List<TagDefinition>()
        };

        template.Key.Should().Be("Siemens/TemperatureShockBoxDevice");
    }

    [Fact]
    public void DeviceTemplate_CanCarryAuxiliaries()
    {
        var template = new DeviceTemplate
        {
            Vendor = "Siemens",
            Model = "standardBoxDevice",
            Tags = new List<TagDefinition>(),
            Auxiliaries = new List<AuxiliaryFunction>
            {
                new() { Group = "手动辅助功能", ControlTagName = "CompressorStart", StateTagName = "CompressorRun" }
            }
        };

        template.Auxiliaries.Should().HaveCount(1);
    }
}
```

- [ ] **Step 1.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~DeviceTemplateTests"
```

Expected: compile error — `DeviceTemplate` type does not exist; `DeviceTemplate.Key` does not exist.

- [ ] **Step 1.3: Create `DeviceTemplate`**

Create `src/SiemensS7Demo/Models/DeviceTemplate.cs`:

```csharp
using System.Collections.Generic;

namespace SiemensS7Demo.Models;

/// <summary>
/// A reusable tag dictionary for one vendor/model combination.
/// Multiple <see cref="DeviceDefinition"/> entries can reference the same template
/// via <see cref="DeviceDefinition.TemplateRef"/> instead of duplicating tag lists.
/// </summary>
/// <remarks>
/// Template resolution is performed by <c>ProjectConfigLoader.Load</c> at load time.
/// After resolution, downstream code (SiemensS7Client, Snap7BatchPlan, etc.) sees
/// fully-populated <see cref="DeviceDefinition.Tags"/> and
/// <see cref="DeviceDefinition.Auxiliaries"/> — templates are transparent to runtime code.
///
/// Template-to-template references are not supported. Cycles are rejected at load time.
/// </remarks>
public sealed class DeviceTemplate
{
    /// <summary>Vendor name, e.g. "Siemens" or "Schneider".</summary>
    public required string Vendor { get; init; }

    /// <summary>Model name, e.g. "standardBoxDevice" or "TemperatureShockBoxDevice".</summary>
    public required string Model { get; init; }

    /// <summary>Tag definitions for this device model.</summary>
    public required List<TagDefinition> Tags { get; init; }

    /// <summary>
    /// Auxiliary function metadata for this device model.
    /// Defaults to an empty list (backward-compatible with devices that have no auxiliaries).
    /// </summary>
    public List<AuxiliaryFunction> Auxiliaries { get; init; } = new();

    /// <summary>
    /// The lookup key used by <see cref="DeviceDefinition.TemplateRef"/>: "Vendor/Model".
    /// </summary>
    public string Key => $"{Vendor}/{Model}";
}
```

- [ ] **Step 1.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~DeviceTemplateTests"
```

Expected: 4 passing.

- [ ] **Step 1.5: Run full suite to confirm no regression**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green.

- [ ] **Step 1.6: Commit**

```bash
git add src/SiemensS7Demo/Models/DeviceTemplate.cs tests/EnviroEquipment.Tests/Models/DeviceTemplateTests.cs
git commit -m "feat(models): add DeviceTemplate with Vendor, Model, Tags, Auxiliaries, and Key"
```

---

## Task 2: `ProjectDefinition.Templates` and `DeviceDefinition.TemplateRef`

Add the two new fields that wire the JSON schema. No loader behavior changes yet — this task is purely the model layer.

**Files:** Modify `src/SiemensS7Demo/Models/ProjectDefinition.cs`. Modify `src/SiemensS7Demo/Models/DeviceDefinition.cs`. Create fixture + test.

- [ ] **Step 2.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Models/DeviceTemplateTests.cs` additions — add these facts to the existing file from Task 1 (or place them in a new partial if preferred; inline is simpler):

The tests below belong in `tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderTemplateTests.cs` (created in Task 3). For Task 2 we only need the model defaults:

Add to `tests/EnviroEquipment.Tests/Models/DeviceTemplateTests.cs` inside the `DeviceTemplateTests` class:

```csharp
    [Fact]
    public void ProjectDefinition_Templates_DefaultEmpty()
    {
        var project = new ProjectDefinition();
        project.Templates.Should().BeEmpty();
    }

    [Fact]
    public void DeviceDefinition_TemplateRef_DefaultNull()
    {
        var device = new DeviceDefinition();
        device.TemplateRef.Should().BeNull();
    }
```

- [ ] **Step 2.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~DeviceTemplateTests"
```

Expected: compile error — `ProjectDefinition.Templates` and `DeviceDefinition.TemplateRef` do not exist.

- [ ] **Step 2.3: Add `Templates` to `ProjectDefinition`**

In `src/SiemensS7Demo/Models/ProjectDefinition.cs`, replace the file with:

```csharp
using System.Collections.Generic;

namespace SiemensS7Demo.Models;

public sealed class ProjectDefinition
{
    public string ProjectId { get; init; } = "default";
    public string ProjectName { get; init; } = "Default Project";

    /// <summary>
    /// Optional shared device templates. Devices may reference a template
    /// via <see cref="DeviceDefinition.TemplateRef"/> to inherit its tag list
    /// and auxiliary functions, avoiding duplication across identical hardware.
    /// </summary>
    public List<DeviceTemplate> Templates { get; init; } = new();

    public List<DeviceDefinition> Devices { get; init; } = new();
}
```

- [ ] **Step 2.4: Add `TemplateRef` to `DeviceDefinition`**

In `src/SiemensS7Demo/Models/DeviceDefinition.cs`, add the following property after the `PollingIntervalMs` line and before `Tags`:

```csharp
    /// <summary>
    /// Optional reference to a <see cref="DeviceTemplate"/> key ("Vendor/Model").
    /// When set, <c>ProjectConfigLoader.Load</c> resolves the template and populates
    /// <see cref="Tags"/> and <see cref="Auxiliaries"/> from the template.
    /// A device must NOT define its own <see cref="Tags"/> when <see cref="TemplateRef"/>
    /// is set (conflict policy: REJECT).
    /// </summary>
    public string? TemplateRef { get; init; }
```

- [ ] **Step 2.5: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~DeviceTemplateTests"
```

Expected: 6 passing (4 from Task 1 + 2 new).

- [ ] **Step 2.6: Run full suite**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green — existing callers never set `Templates` or `TemplateRef`; both default to safe empty/null values.

- [ ] **Step 2.7: Commit**

```bash
git add src/SiemensS7Demo/Models/ProjectDefinition.cs src/SiemensS7Demo/Models/DeviceDefinition.cs tests/EnviroEquipment.Tests/Models/DeviceTemplateTests.cs
git commit -m "feat(models): add ProjectDefinition.Templates and DeviceDefinition.TemplateRef"
```

---

## Task 3: Loader resolves template references

`ProjectConfigLoader.Load` gains a post-parse step. After JSON deserialization, for each device whose `TemplateRef` is non-null the loader: (1) looks up the template by `key == device.TemplateRef`, (2) rejects the device if it also has its own tags (REJECT policy), (3) synthesizes a new `DeviceDefinition` with `Tags` and `Auxiliaries` copied from the template.

The existing guard `if (device.Tags.Count == 0)` must be relaxed: it now only fires when `TemplateRef` is also null (a device with neither tags nor a template reference is still an error).

**Files:** Modify `src/SiemensS7Demo/Services/ProjectConfigLoader.cs`. Create fixture. Create `tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderTemplateTests.cs`.

- [ ] **Step 3.1: Create fixture**

Create `tests/EnviroEquipment.Tests/Services/Fixtures/templates.project.json`:

```json
{
  "projectId": "template-test",
  "projectName": "Template Test Project",
  "templates": [
    {
      "vendor": "Siemens",
      "model": "standardBoxDevice",
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
          "name": "CompressorRun",
          "displayName": "Compressor Running",
          "group": "Status",
          "address": "V9.0",
          "dataType": "Bool",
          "unit": ""
        }
      ],
      "auxiliaries": [
        {
          "group": "手动辅助功能",
          "controlTagName": "CompressorStart",
          "stateTagName": "CompressorRun"
        }
      ]
    }
  ],
  "devices": [
    {
      "id": "box-A",
      "name": "Box A",
      "enabled": true,
      "protocol": "mock",
      "ip": "192.168.1.10",
      "port": 102,
      "cpuType": "S7-200 SMART",
      "pollingIntervalMs": 1000,
      "templateRef": "Siemens/standardBoxDevice"
    },
    {
      "id": "box-B",
      "name": "Box B",
      "enabled": true,
      "protocol": "mock",
      "ip": "192.168.1.11",
      "port": 102,
      "cpuType": "S7-200 SMART",
      "pollingIntervalMs": 1000,
      "templateRef": "Siemens/standardBoxDevice"
    },
    {
      "id": "standalone",
      "name": "Standalone Device",
      "enabled": true,
      "protocol": "mock",
      "ip": "192.168.1.20",
      "port": 102,
      "cpuType": "Mock",
      "pollingIntervalMs": 1000,
      "tags": [
        {
          "name": "Pressure",
          "displayName": "Pressure",
          "group": "Sensors",
          "address": "MW0",
          "dataType": "Int16",
          "unit": "bar"
        }
      ]
    }
  ]
}
```

In `tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj`, add inside the fixtures `<ItemGroup>`:

```xml
    <None Update="Services/Fixtures/templates.project.json" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 3.2: Write failing tests**

Create `tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderTemplateTests.cs`:

```csharp
using System.IO;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ProjectConfigLoaderTemplateTests
{
    private static string TemplateFixture =>
        Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "templates.project.json");

    // ── Resolution happy path ─────────────────────────────────────────────────

    [Fact]
    public void Load_TemplateDevice_ResolvesTwoTagsFromTemplate()
    {
        var project = ProjectConfigLoader.Load(TemplateFixture);

        var boxA = project.Devices.Single(d => d.Id == "box-A");
        boxA.Tags.Should().HaveCount(2);
        boxA.Tags.Select(t => t.Name).Should().BeEquivalentTo(new[] { "Temp", "CompressorRun" });
    }

    [Fact]
    public void Load_TemplateDevice_ResolvesAuxiliariesFromTemplate()
    {
        var project = ProjectConfigLoader.Load(TemplateFixture);

        var boxA = project.Devices.Single(d => d.Id == "box-A");
        boxA.Auxiliaries.Should().HaveCount(1);
        boxA.Auxiliaries[0].Group.Should().Be("手动辅助功能");
        boxA.Auxiliaries[0].ControlTagName.Should().Be("CompressorStart");
    }

    [Fact]
    public void Load_TwoTemplateDevices_BothResolveIndependently()
    {
        var project = ProjectConfigLoader.Load(TemplateFixture);

        var boxA = project.Devices.Single(d => d.Id == "box-A");
        var boxB = project.Devices.Single(d => d.Id == "box-B");

        // Both have 2 tags from the same template.
        boxA.Tags.Should().HaveCount(2);
        boxB.Tags.Should().HaveCount(2);

        // They are separate instances (not shared by reference).
        boxA.Tags.Should().NotBeSameAs(boxB.Tags);
    }

    [Fact]
    public void Load_StandaloneDevice_PreservesOwnTags()
    {
        var project = ProjectConfigLoader.Load(TemplateFixture);

        var standalone = project.Devices.Single(d => d.Id == "standalone");
        standalone.Tags.Should().HaveCount(1);
        standalone.Tags[0].Name.Should().Be("Pressure");
    }

    [Fact]
    public void Load_TemplateDevice_PreservesDeviceIdentityFields()
    {
        var project = ProjectConfigLoader.Load(TemplateFixture);

        var boxA = project.Devices.Single(d => d.Id == "box-A");
        boxA.Id.Should().Be("box-A");
        boxA.Ip.Should().Be("192.168.1.10");
        boxA.Protocol.Should().Be("mock");
    }

    // ── Template tag field fidelity ───────────────────────────────────────────

    [Fact]
    public void Load_TemplateDevice_TagPreservesScaleAndScaleMode()
    {
        var project = ProjectConfigLoader.Load(TemplateFixture);

        var temp = project.Devices.Single(d => d.Id == "box-A")
            .Tags.Single(t => t.Name == "Temp");

        temp.Scale.Should().Be(10.0);
        temp.ScaleMode.Should().Be(ScaleMode.Divisor);
        temp.DataType.Should().Be(TagDataType.Int16);
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Load_MissingTemplate_ThrowsInvalidOperationException()
    {
        var json = """
            {
              "projectId": "err",
              "projectName": "Err",
              "templates": [],
              "devices": [
                {
                  "id": "d1", "name": "D1", "enabled": true,
                  "protocol": "mock", "ip": "127.0.0.1", "port": 102,
                  "cpuType": "Mock", "pollingIntervalMs": 1000,
                  "templateRef": "Siemens/nonExistentModel"
                }
              ]
            }
            """;
        var path = WriteTemp(json);

        var act = () => ProjectConfigLoader.Load(path);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Siemens/nonExistentModel*");
    }

    [Fact]
    public void Load_TemplateRefAndTagsBoth_ThrowsInvalidOperationException()
    {
        var json = """
            {
              "projectId": "err",
              "projectName": "Err",
              "templates": [
                {
                  "vendor": "Siemens",
                  "model": "standardBoxDevice",
                  "tags": [
                    { "name": "T", "displayName": "T", "group": "g",
                      "address": "MW0", "dataType": "Int16", "unit": "" }
                  ]
                }
              ],
              "devices": [
                {
                  "id": "d1", "name": "D1", "enabled": true,
                  "protocol": "mock", "ip": "127.0.0.1", "port": 102,
                  "cpuType": "Mock", "pollingIntervalMs": 1000,
                  "templateRef": "Siemens/standardBoxDevice",
                  "tags": [
                    { "name": "Own", "displayName": "Own", "group": "g",
                      "address": "MW2", "dataType": "Int16", "unit": "" }
                  ]
                }
              ]
            }
            """;
        var path = WriteTemp(json);

        var act = () => ProjectConfigLoader.Load(path);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*templateRef*tags*");
    }

    [Fact]
    public void Load_NoTemplateRefAndNoTags_ThrowsInvalidOperationException()
    {
        var json = """
            {
              "projectId": "err",
              "projectName": "Err",
              "devices": [
                {
                  "id": "d1", "name": "D1", "enabled": true,
                  "protocol": "mock", "ip": "127.0.0.1", "port": 102,
                  "cpuType": "Mock", "pollingIntervalMs": 1000
                }
              ]
            }
            """;
        var path = WriteTemp(json);

        var act = () => ProjectConfigLoader.Load(path);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no tags*");
    }

    private static string WriteTemp(string json)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"gap9-test-{System.Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, json);
        return path;
    }
}
```

- [ ] **Step 3.3: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ProjectConfigLoaderTemplateTests"
```

Expected: most tests fail or error — loader does not resolve templates, zero-tags guard fires for template devices before resolution.

- [ ] **Step 3.4: Update `ProjectConfigLoader.Load`**

Replace `src/SiemensS7Demo/Services/ProjectConfigLoader.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Services;

public static class ProjectConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ProjectDefinition Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Project configuration file was not found.", path);
        }

        var json = File.ReadAllText(path);
        var project = JsonSerializer.Deserialize<ProjectDefinition>(json, Options)
            ?? throw new InvalidOperationException($"Project configuration '{path}' is empty or invalid.");

        if (project.Devices.Count == 0)
        {
            throw new InvalidOperationException($"Project configuration '{path}' contains no devices.");
        }

        // Build a lookup map: "Vendor/Model" -> DeviceTemplate.
        var templateMap = project.Templates
            .ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);

        // Post-process: resolve TemplateRef for each device.
        var resolvedDevices = new List<DeviceDefinition>(project.Devices.Count);
        foreach (var device in project.Devices)
        {
            resolvedDevices.Add(ResolveDevice(device, templateMap));
        }

        // Replace the deserialized devices list with resolved devices.
        // ProjectDefinition.Devices is a List<T> (init-only) — reconstruct the project.
        var resolvedProject = new ProjectDefinition
        {
            ProjectId = project.ProjectId,
            ProjectName = project.ProjectName,
            Templates = project.Templates,
            Devices = resolvedDevices
        };

        return resolvedProject;
    }

    private static DeviceDefinition ResolveDevice(
        DeviceDefinition device,
        Dictionary<string, DeviceTemplate> templateMap)
    {
        if (string.IsNullOrWhiteSpace(device.Id))
        {
            throw new InvalidOperationException("Every device must have an id.");
        }

        if (device.TemplateRef is not null)
        {
            // Conflict policy: REJECT when both templateRef and per-device tags are present.
            // Templates are all-or-nothing; per-device tag overrides are out of scope.
            if (device.Tags.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Device '{device.Id}' sets both 'templateRef' and 'tags'. " +
                    "A device must use either a templateRef or its own tags, not both.");
            }

            if (!templateMap.TryGetValue(device.TemplateRef, out var template))
            {
                throw new InvalidOperationException(
                    $"Device '{device.Id}' references template '{device.TemplateRef}' " +
                    "which was not found in project.templates. " +
                    $"Available templates: [{string.Join(", ", templateMap.Keys)}].");
            }

            // Return a new DeviceDefinition with the template's tags and auxiliaries
            // merged in. All device-identity fields (Id, Ip, Protocol, etc.) are preserved.
            return new DeviceDefinition
            {
                Id = device.Id,
                Name = device.Name,
                Protocol = device.Protocol,
                Ip = device.Ip,
                Port = device.Port,
                Enabled = device.Enabled,
                CpuType = device.CpuType,
                Rack = device.Rack,
                Slot = device.Slot,
                ConnectionType = device.ConnectionType,
                UnitId = device.UnitId,
                PollingIntervalMs = device.PollingIntervalMs,
                TemplateRef = device.TemplateRef,
                // Copy (not share) the template's collections so each resolved device
                // gets an independent list; downstream mutations on one device do not
                // affect another device that uses the same template.
                Tags = new List<TagDefinition>(template.Tags),
                Auxiliaries = new List<AuxiliaryFunction>(template.Auxiliaries)
            };
        }

        // No TemplateRef — device must carry its own tags.
        if (device.Tags.Count == 0)
        {
            throw new InvalidOperationException(
                $"Device '{device.Id}' contains no tags. " +
                "Either define 'tags' directly or set 'templateRef'.");
        }

        return device;
    }
}
```

- [ ] **Step 3.5: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ProjectConfigLoaderTemplateTests"
```

Expected: 9 passing.

- [ ] **Step 3.6: Run full suite**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green. Existing tests use projects with direct `tags` arrays; the new code path only activates when `TemplateRef` is set.

- [ ] **Step 3.7: Commit**

```bash
git add src/SiemensS7Demo/Services/ProjectConfigLoader.cs \
        tests/EnviroEquipment.Tests/Services/Fixtures/templates.project.json \
        tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderTemplateTests.cs \
        tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj
git commit -m "feat(loader): resolve DeviceTemplate references in ProjectConfigLoader.Load"
```

---

## Task 4: Validation — templates and template references

`ConfigValidationService` gains a `ValidateTemplates` method and `ValidateProject` is updated to call it.

Rules:
1. `DeviceTemplate.Vendor` and `DeviceTemplate.Model` must be non-empty (Error).
2. `DeviceTemplate.Tags` must pass `ValidateTags` per template (using the template's implied protocol — `"mock"` is used for protocol-agnostic tag validation; templates do not carry a protocol since they are shared across device instances that each declare their own protocol). The executor should use the device's protocol for per-device validation; template tags are validated against `"mock"` to avoid false address-format errors (template tags use real addresses that are validated when the resolved device is validated). **Implementation note:** call `ValidateTags(template.Tags, "mock", $"template:{template.Key}")` — validation of address format against a specific protocol is deferred to `ValidateProject` which already calls `ValidateTags` on each resolved device.
3. `DeviceDefinition.TemplateRef` must resolve to a known template — Error if not found (the loader already throws, but `ValidateProject` should also catch this to support callers that validate without going through `Load`, e.g., programmatic config construction).
4. Duplicate template keys (same `Vendor/Model`) — Error.
5. No additional validation of `Auxiliaries` within the template: `ValidateAuxiliaries` already exists and is called separately.

**Files:** Modify `src/SiemensS7Demo/Services/ConfigValidationService.cs`. Create `tests/EnviroEquipment.Tests/Services/ConfigValidationTemplateTests.cs`.

- [ ] **Step 4.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Services/ConfigValidationTemplateTests.cs`:

```csharp
using System.Collections.Generic;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ConfigValidationTemplateTests
{
    // ── Helper builders ───────────────────────────────────────────────────────

    private static TagDefinition MakeTag(string name, string address = "MW0") => new()
    {
        Name = name, DisplayName = name, Group = "g",
        Address = address, DataType = TagDataType.Int16, Unit = ""
    };

    private static DeviceTemplate MakeTemplate(
        string vendor = "Siemens",
        string model = "standardBoxDevice",
        List<TagDefinition>? tags = null) => new()
        {
            Vendor = vendor,
            Model = model,
            Tags = tags ?? new List<TagDefinition> { MakeTag("T") }
        };

    private static DeviceDefinition MakeDevice(
        string id = "dev",
        string? templateRef = null,
        List<TagDefinition>? tags = null) => new()
        {
            Id = id, Name = id, Protocol = "mock",
            Ip = "127.0.0.1", Port = 102, PollingIntervalMs = 1000,
            TemplateRef = templateRef,
            Tags = tags ?? new List<TagDefinition>()
        };

    // ── ValidateTemplates ─────────────────────────────────────────────────────

    [Fact]
    public void ValidateTemplates_EmptyList_NoIssues()
    {
        var issues = ConfigValidationService.ValidateTemplates(
            new List<DeviceTemplate>(), new List<DeviceDefinition>());

        issues.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTemplates_ValidTemplate_NoIssues()
    {
        var template = MakeTemplate();
        var device = MakeDevice(templateRef: template.Key,
            tags: new List<TagDefinition>());

        var issues = ConfigValidationService.ValidateTemplates(
            new List<DeviceTemplate> { template },
            new List<DeviceDefinition> { device });

        issues.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTemplates_EmptyVendor_IsError()
    {
        var template = new DeviceTemplate
        {
            Vendor = "",
            Model = "standardBoxDevice",
            Tags = new List<TagDefinition> { MakeTag("T") }
        };

        var issues = ConfigValidationService.ValidateTemplates(
            new List<DeviceTemplate> { template },
            new List<DeviceDefinition>());

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("Vendor", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTemplates_EmptyModel_IsError()
    {
        var template = new DeviceTemplate
        {
            Vendor = "Siemens",
            Model = "",
            Tags = new List<TagDefinition> { MakeTag("T") }
        };

        var issues = ConfigValidationService.ValidateTemplates(
            new List<DeviceTemplate> { template },
            new List<DeviceDefinition>());

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("Model", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTemplates_DuplicateKey_IsError()
    {
        var t1 = MakeTemplate("Siemens", "standardBoxDevice");
        var t2 = MakeTemplate("Siemens", "standardBoxDevice");

        var issues = ConfigValidationService.ValidateTemplates(
            new List<DeviceTemplate> { t1, t2 },
            new List<DeviceDefinition>());

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("Siemens/standardBoxDevice", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTemplates_TagWithInvalidScale_IsError()
    {
        var badTag = new TagDefinition
        {
            Name = "Bad", DisplayName = "Bad", Group = "g",
            Address = "MW0", DataType = TagDataType.Int16, Unit = "",
            Scale = 0.0, ScaleMode = ScaleMode.Divisor
        };
        var template = new DeviceTemplate
        {
            Vendor = "Siemens",
            Model = "standardBoxDevice",
            Tags = new List<TagDefinition> { badTag }
        };

        var issues = ConfigValidationService.ValidateTemplates(
            new List<DeviceTemplate> { template },
            new List<DeviceDefinition>());

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Scope.StartsWith("template:Siemens/standardBoxDevice"));
    }

    // ── TemplateRef resolvability in ValidateProject ──────────────────────────

    [Fact]
    public void ValidateProject_DeviceWithUnresolvableTemplateRef_IsError()
    {
        var project = new ProjectDefinition
        {
            ProjectId = "test",
            Templates = new List<DeviceTemplate>(),
            Devices = new List<DeviceDefinition>
            {
                // Manually construct a device with tags (simulates post-Load state
                // for a project that somehow has a dangling TemplateRef).
                new()
                {
                    Id = "d1", Name = "D1", Protocol = "mock",
                    Ip = "127.0.0.1", Port = 102, PollingIntervalMs = 1000,
                    TemplateRef = "Siemens/missing",
                    Tags = new List<TagDefinition> { MakeTag("T") }
                }
            }
        };

        var issues = ConfigValidationService.ValidateProject(project);

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("Siemens/missing", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProject_ResolvedTemplateDevice_PassesValidation()
    {
        // Simulate the post-Load state: template has been resolved into device.Tags.
        var project = new ProjectDefinition
        {
            ProjectId = "test",
            Templates = new List<DeviceTemplate>
            {
                MakeTemplate()
            },
            Devices = new List<DeviceDefinition>
            {
                new()
                {
                    Id = "d1", Name = "D1", Protocol = "mock",
                    Ip = "127.0.0.1", Port = 102, PollingIntervalMs = 1000,
                    TemplateRef = "Siemens/standardBoxDevice",
                    Tags = new List<TagDefinition> { MakeTag("T") }
                }
            }
        };

        var issues = ConfigValidationService.ValidateProject(project);

        issues.Should().NotContain(i => i.Severity == ConfigIssueSeverity.Error);
    }
}
```

- [ ] **Step 4.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ConfigValidationTemplateTests"
```

Expected: compile error — `ConfigValidationService.ValidateTemplates` does not exist; `ValidateProject` does not check `TemplateRef`.

- [ ] **Step 4.3: Add `ValidateTemplates` and update `ValidateProject`**

In `src/SiemensS7Demo/Services/ConfigValidationService.cs`, add the following public method after the existing `ValidateAuxiliaries` method:

```csharp
    /// <summary>
    /// Validates all <see cref="DeviceTemplate"/> entries in the project and checks that
    /// any <see cref="DeviceDefinition.TemplateRef"/> resolves to a known template.
    /// </summary>
    public static IReadOnlyList<ConfigValidationIssue> ValidateTemplates(
        IReadOnlyList<DeviceTemplate> templates,
        IReadOnlyList<DeviceDefinition> devices)
    {
        var issues = new List<ConfigValidationIssue>();

        // Rule 1: Vendor and Model must be non-empty.
        // Rule 2: Duplicate template keys are errors.
        // Rule 3: Each template's Tags must pass ValidateTags.
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in templates)
        {
            var templateScope = $"template:{template.Key}";

            if (string.IsNullOrWhiteSpace(template.Vendor))
            {
                issues.Add(Error(templateScope, "DeviceTemplate Vendor must not be empty."));
            }
            if (string.IsNullOrWhiteSpace(template.Model))
            {
                issues.Add(Error(templateScope, "DeviceTemplate Model must not be empty."));
            }

            if (!seenKeys.Add(template.Key))
            {
                issues.Add(Error(templateScope,
                    $"Duplicate DeviceTemplate key '{template.Key}'. " +
                    "Each Vendor/Model combination must be unique."));
            }

            // Validate the template's tag list.
            // Protocol is "mock" here: address-format validation is deferred to
            // the resolved device, which carries its own protocol. Structural rules
            // (scale, options, bit derivations) are checked now.
            if (template.Tags.Count > 0)
            {
                issues.AddRange(ValidateTags(template.Tags, "mock", templateScope));
            }
        }

        // Rule 4: each device's TemplateRef, if set, must resolve to a known template.
        var templateKeys = seenKeys; // already populated above
        foreach (var device in devices)
        {
            if (device.TemplateRef is not null &&
                !templateKeys.Contains(device.TemplateRef))
            {
                issues.Add(Error($"device:{device.Id}",
                    $"Device '{device.Id}' references template '{device.TemplateRef}' " +
                    "which was not found in project.templates."));
            }
        }

        return issues;
    }
```

Also update `ValidateProject` to call `ValidateTemplates`. Inside the `ValidateProject` method, after the `foreach (var device in project.Devices)` loop (i.e., just before `return issues;`), add:

```csharp
        issues.AddRange(ValidateTemplates(project.Templates, project.Devices));
```

- [ ] **Step 4.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ConfigValidationTemplateTests"
```

Expected: 9 passing.

- [ ] **Step 4.5: Run full suite**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green.

- [ ] **Step 4.6: Commit**

```bash
git add src/SiemensS7Demo/Services/ConfigValidationService.cs \
        tests/EnviroEquipment.Tests/Services/ConfigValidationTemplateTests.cs
git commit -m "feat(validation): ValidateTemplates checks vendor/model, uniqueness, tags, and TemplateRef resolvability"
```

---

## Task 5: Open the PR

- [ ] **Step 5.1: Full build and test**

```bash
dotnet build EnviroEquipmentFinalEdition.sln
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: zero build warnings on new files; all tests green.

- [ ] **Step 5.2: Push and create PR**

```bash
git push -u origin feat/gap9-device-templates
gh pr create --title "Gap #9: DeviceTemplate — vendor x model x tag dictionary with project-level references" --body "$(cat <<'EOF'
## Summary
- Adds `DeviceTemplate` sealed class (`Vendor`, `Model`, `Tags`, `Auxiliaries`, computed `Key`) to the Models layer per spec Section 5.
- Adds `ProjectDefinition.Templates` (default empty list — zero breaking change for existing projects).
- Adds `DeviceDefinition.TemplateRef` (nullable string — default null, no change for existing devices).
- `ProjectConfigLoader.Load` resolves `TemplateRef` references post-parse: each template device receives a new `DeviceDefinition` with `Tags` and `Auxiliaries` copied (not shared) from the matching template. Runtime code sees fully-realized tag arrays — templates are transparent to `SiemensS7Client`, `Snap7BatchPlan`, etc.
- Conflict policy: **REJECT** when a device sets both `templateRef` and a non-empty `tags` array. Templates are all-or-nothing; per-device tag overrides are out of scope.
- `ConfigValidationService.ValidateTemplates` validates Vendor/Model non-empty, no duplicate keys, per-template `ValidateTags`, and `TemplateRef` resolvability. `ValidateProject` now calls `ValidateTemplates` automatically.

## Conflict policy rationale
REJECT (error on templateRef + tags conflict) was chosen over MERGE because:
- Simpler to reason about: one canonical source of truth for a device's tags.
- No ambiguity on conflict resolution order.
- Per-device overrides can be added in a future gap if needed.

## Notes on codebase alignment
- `AuxiliaryFunction` already landed in Gap #7 as `sealed class` with `required` properties. `DeviceTemplate.Auxiliaries` reuses this type directly — no shape change needed.
- `DeviceDefinition.Auxiliaries` (Gap #7) transitions from the transitional holder to being populated by template resolution; existing devices without `TemplateRef` continue to carry their own auxiliaries as before.
- Template tags are validated against protocol `"mock"` in `ValidateTemplates` to avoid false address-format errors; address validation runs again against the actual device protocol in `ValidateProject`.

## Test plan
- [x] `dotnet build EnviroEquipmentFinalEdition.sln` — zero warnings
- [x] `dotnet test EnviroEquipmentFinalEdition.sln` — all green
- [x] `DeviceTemplateTests` (6): construction, Auxiliaries default, Key computed, TemplateRef default, Templates default
- [x] `ProjectConfigLoaderTemplateTests` (9): happy-path resolution (tags count, auxiliaries, two devices independent, standalone preserved, identity fields, ScaleMode fidelity); missing-template error; conflict error; no-tags-no-ref error
- [x] `ConfigValidationTemplateTests` (9): empty list ok, valid template ok, empty Vendor error, empty Model error, duplicate key error, bad-scale tag error, unresolvable TemplateRef error, resolved device passes, ValidateProject integration

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Self-Review Checklist

- [ ] No emojis in code or commit messages.
- [ ] `DeviceTemplate.Tags` is `List<TagDefinition>` (matches `DeviceDefinition.Tags` convention for JSON deserialization); `DeviceTemplate.Key` is a computed property, not serialized.
- [ ] `ProjectDefinition.Templates` defaults to `new()` — projects without `"templates"` key in JSON deserialize cleanly.
- [ ] `DeviceDefinition.TemplateRef` defaults to `null` — existing devices without `templateRef` in JSON are unaffected.
- [ ] Template resolution copies (not shares) `Tags` and `Auxiliaries` lists so two devices using the same template have independent list instances.
- [ ] `ProjectConfigLoader.Load` error message for missing template includes both the device id and the template key, and lists available templates.
- [ ] `ProjectConfigLoader.Load` error message for the templateRef+tags conflict references both `"templateRef"` and `"tags"` (matched by the test's `WithMessage`).
- [ ] `ProjectConfigLoader.Load` error message for no-tags-no-ref contains `"no tags"` (matched by the existing test pattern).
- [ ] `ConfigValidationService.ValidateTemplates` is a separate public method callable independently; `ValidateProject` calls it automatically.
- [ ] Protocol passed to `ValidateTags` for template tags is `"mock"` — avoids false failures on address format before the resolved device's protocol is known.
- [ ] All new `required` properties on `DeviceTemplate` use `init`-only setters.
- [ ] `DeviceDefinition.TemplateRef` is `init`-only; it is preserved on the resolved `DeviceDefinition` so callers can detect whether a device came from a template.
- [ ] No changes to `SiemensS7Client`, `Snap7BatchPlan`, `TagConfigLoader`, or any adapter — runtime code is unchanged.
