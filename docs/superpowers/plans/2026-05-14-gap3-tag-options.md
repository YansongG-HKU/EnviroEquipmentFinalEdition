# Gap #3 — `TagDefinition.Options` Enum Mapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry the legacy XML `<option name="..." value="..."/>` enum mapping through our type system end-to-end, so every read snapshot includes a human label when the raw value matches a defined option.

**Architecture:** Add a `TagOption` record and an `Options` list on `TagDefinition` (default empty, no behavior change for existing tags). Add `DisplayValue` to `TagValue`. `SiemensS7Client.ReadTagsAsync` consults `Options` when building each `TagValue`. Both XML and JSON loaders learn to parse the new field. `ConfigValidationService` rejects duplicate values and empty labels.

**Tech Stack:** C# .NET 8, xunit, FluentAssertions, `System.Xml.Linq`, `System.Text.Json`.

**Scope guard:** Only Options. `BitDerivations` is Gap #4 (separate plan/PR). Don't touch adapter code paths beyond `SiemensS7Client.ReadTagsAsync`.

**Branch:** `feat/gap3-tag-options`
**Worktree:** `.claude/worktrees/gap3-tag-options`
**Base:** `main` after Wave 0 is merged.

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `src/SiemensS7Demo/Models/TagOption.cs` | `record TagOption(long Value, string Label)` |
| Modify | `src/SiemensS7Demo/Models/TagDefinition.cs` | Add `Options` field + `TryGetOptionLabel(...)` |
| Modify | `src/SiemensS7Demo/Models/TagValue.cs` | Add `DisplayValue` |
| Modify | `src/SiemensS7Demo/Drivers/SiemensS7Client.cs` | Set `DisplayValue` from Options |
| Modify | `src/SiemensS7Demo/Services/TagConfigLoader.cs` | Parse `<Option>` children |
| Modify | `src/SiemensS7Demo/Services/ConfigValidationService.cs` | Validate Options |
| Create | `tests/EnviroEquipment.Tests/Models/TagDefinitionOptionsTests.cs` | TryGetOptionLabel |
| Create | `tests/EnviroEquipment.Tests/Drivers/SiemensS7ClientDisplayValueTests.cs` | Read sets DisplayValue |
| Create | `tests/EnviroEquipment.Tests/Services/TagConfigLoaderOptionsTests.cs` | XML loader |
| Create | `tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderOptionsTests.cs` | JSON loader |
| Create | `tests/EnviroEquipment.Tests/Services/ConfigValidationOptionsTests.cs` | Validation |

`ProjectConfigLoader.cs` requires no code change — `System.Text.Json` auto-binds `options` (camelCase) to `Options` because `PropertyNameCaseInsensitive=true`. We still add a test against the JSON path to prove it.

---

## Task 1: `TagOption` record + `Options` field

**Files:** Create `src/SiemensS7Demo/Models/TagOption.cs`. Modify `src/SiemensS7Demo/Models/TagDefinition.cs`. Create `tests/EnviroEquipment.Tests/Models/TagDefinitionOptionsTests.cs`.

- [ ] **Step 1.1: Write failing test**

Create `tests/EnviroEquipment.Tests/Models/TagDefinitionOptionsTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Models;

public class TagDefinitionOptionsTests
{
    [Fact]
    public void Options_DefaultEmpty()
    {
        var tag = MakeTag();
        tag.Options.Should().BeEmpty();
    }

    [Fact]
    public void TryGetOptionLabel_ReturnsLabelOnMatch()
    {
        var tag = MakeTag() with { };
        // `with` doesn't work on a class; use init instead.
        tag = new TagDefinition
        {
            Name = "Mode", DisplayName = "Mode", Group = "g", Address = "V9.5",
            DataType = TagDataType.Bool, Unit = "",
            Options = new[] { new TagOption(0, "Off"), new TagOption(1, "On") }
        };

        tag.TryGetOptionLabel(1, out var label).Should().BeTrue();
        label.Should().Be("On");
    }

    [Fact]
    public void TryGetOptionLabel_ReturnsFalseWhenNoMatch()
    {
        var tag = new TagDefinition
        {
            Name = "X", DisplayName = "X", Group = "g", Address = "MW0",
            DataType = TagDataType.Int16, Unit = "",
            Options = new[] { new TagOption(0, "Off"), new TagOption(1, "On") }
        };

        tag.TryGetOptionLabel(42, out var label).Should().BeFalse();
        label.Should().BeNull();
    }

    private static TagDefinition MakeTag() => new()
    {
        Name = "T", DisplayName = "T", Group = "g", Address = "MW0",
        DataType = TagDataType.Int16, Unit = ""
    };
}
```

- [ ] **Step 1.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagDefinitionOptionsTests"
```

Expected: compile error (no `TagOption`, no `Options`, no `TryGetOptionLabel`).

- [ ] **Step 1.3: Create `TagOption`**

Create `src/SiemensS7Demo/Models/TagOption.cs`:

```csharp
namespace SiemensS7Demo.Models;

public sealed record TagOption(long Value, string Label);
```

- [ ] **Step 1.4: Add `Options` + `TryGetOptionLabel` to `TagDefinition`**

In `src/SiemensS7Demo/Models/TagDefinition.cs`, after `public double? Max { get; init; }` add:

```csharp
    public IReadOnlyList<TagOption> Options { get; init; } = System.Array.Empty<TagOption>();

    public bool TryGetOptionLabel(long rawValue, out string? label)
    {
        foreach (var option in Options)
        {
            if (option.Value == rawValue)
            {
                label = option.Label;
                return true;
            }
        }

        label = null;
        return false;
    }
```

(Add `using System.Collections.Generic;` at the top if not implicitly available — implicit usings on net8.0 should cover it.)

- [ ] **Step 1.5: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagDefinitionOptionsTests"
```

Expected: 3 passing.

- [ ] **Step 1.6: Commit**

```bash
git add src/SiemensS7Demo/Models/TagOption.cs src/SiemensS7Demo/Models/TagDefinition.cs tests/EnviroEquipment.Tests/Models/TagDefinitionOptionsTests.cs
git commit -m "feat(models): add TagOption record and TagDefinition.Options"
```

---

## Task 2: `TagValue.DisplayValue`

**Files:** Modify `src/SiemensS7Demo/Models/TagValue.cs`.

- [ ] **Step 2.1: Add the field**

In `src/SiemensS7Demo/Models/TagValue.cs`, after `public string? QualityMessage { get; init; }` add:

```csharp
    public string? DisplayValue { get; init; }
```

- [ ] **Step 2.2: Build to confirm no callers break**

```bash
dotnet build EnviroEquipmentFinalEdition.sln
```

Expected: success.

- [ ] **Step 2.3: Commit**

```bash
git add src/SiemensS7Demo/Models/TagValue.cs
git commit -m "feat(models): TagValue.DisplayValue for option-label rendering"
```

---

## Task 3: `SiemensS7Client.ReadTagsAsync` sets `DisplayValue`

**Files:** Modify `src/SiemensS7Demo/Drivers/SiemensS7Client.cs`. Create `tests/EnviroEquipment.Tests/Drivers/SiemensS7ClientDisplayValueTests.cs`.

- [ ] **Step 3.1: Write failing test**

Create `tests/EnviroEquipment.Tests/Drivers/SiemensS7ClientDisplayValueTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class SiemensS7ClientDisplayValueTests
{
    [Fact]
    public async Task ReadTagsAsync_SetsDisplayValueWhenOptionMatches()
    {
        var options = new PlcConnectionOptions { Name = "T", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new InMemoryS7Adapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(CancellationToken.None);

        var tag = new TagDefinition
        {
            Name = "Mode", DisplayName = "Mode", Group = "g", Address = "MW0",
            DataType = TagDataType.Int16, Unit = "",
            Options = new[] { new TagOption(0, "Off"), new TagOption(1, "On") }
        };

        await adapter.WriteRawAsync(tag, (short)1, CancellationToken.None);
        var values = await client.ReadTagsAsync(new[] { tag }, CancellationToken.None);

        values["Mode"].DisplayValue.Should().Be("On");
        values["Mode"].IsQualityGood.Should().BeTrue();
    }

    [Fact]
    public async Task ReadTagsAsync_LeavesDisplayValueNullWhenNoMatch()
    {
        var options = new PlcConnectionOptions { Name = "T", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new InMemoryS7Adapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(CancellationToken.None);

        var tag = new TagDefinition
        {
            Name = "Mode", DisplayName = "Mode", Group = "g", Address = "MW0",
            DataType = TagDataType.Int16, Unit = "",
            Options = new[] { new TagOption(0, "Off"), new TagOption(1, "On") }
        };

        await adapter.WriteRawAsync(tag, (short)7, CancellationToken.None);
        var values = await client.ReadTagsAsync(new[] { tag }, CancellationToken.None);

        values["Mode"].DisplayValue.Should().BeNull();
    }
}
```

- [ ] **Step 3.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~SiemensS7ClientDisplayValueTests"
```

Expected: 2 failures (`DisplayValue` is always null because the client never sets it).

- [ ] **Step 3.3: Update `SiemensS7Client.ReadTagsAsync`**

In `src/SiemensS7Demo/Drivers/SiemensS7Client.cs`, replace the existing TagValue-on-success construction (the `output[tag.Name] = new TagValue { ... };` block in the try) with:

```csharp
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

                    output[tag.Name] = new TagValue
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
```

And add this private helper at the end of the class (next to `ConvertReadValue`):

```csharp
    private static long? ToOptionKey(object raw)
    {
        try
        {
            return raw switch
            {
                bool b => b ? 1L : 0L,
                long l => l,
                int i => i,
                short s => s,
                ushort u => u,
                uint u2 => u2,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
```

(The pattern accepts the three numeric encodings produced by current adapters: bool, short/ushort, int. Floats deliberately don't match — option matching on floats is not the legacy pattern.)

- [ ] **Step 3.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~SiemensS7ClientDisplayValueTests"
```

Expected: 2 passing.

- [ ] **Step 3.5: Commit**

```bash
git add src/SiemensS7Demo/Drivers/SiemensS7Client.cs tests/EnviroEquipment.Tests/Drivers/SiemensS7ClientDisplayValueTests.cs
git commit -m "feat(client): set TagValue.DisplayValue from TagDefinition.Options"
```

---

## Task 4: `TagConfigLoader` parses `<Option>` children

**Files:** Modify `src/SiemensS7Demo/Services/TagConfigLoader.cs`. Create `tests/EnviroEquipment.Tests/Services/TagConfigLoaderOptionsTests.cs` and a fixture XML.

- [ ] **Step 4.1: Create fixture**

Create `tests/EnviroEquipment.Tests/Services/Fixtures/options.xml`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Configuration>
  <Tags>
    <Tag name="Mode" displayName="Mode" group="Probe" address="MW0" dataType="Int16" unit="">
      <Option name="Off" value="0"/>
      <Option name="On" value="1"/>
    </Tag>
    <Tag name="Plain" displayName="Plain" group="Probe" address="MW2" dataType="Int16" unit=""/>
  </Tags>
</Configuration>
```

In `tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj`, ensure the fixture is copied to the output:

```xml
  <ItemGroup>
    <None Update="Services/Fixtures/options.xml" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 4.2: Write failing test**

Create `tests/EnviroEquipment.Tests/Services/TagConfigLoaderOptionsTests.cs`:

```csharp
using System.IO;
using FluentAssertions;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class TagConfigLoaderOptionsTests
{
    [Fact]
    public void Load_ParsesOptionChildren()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "options.xml");
        var tags = TagConfigLoader.Load(path);

        var mode = tags.Single(t => t.Name == "Mode");
        mode.Options.Should().HaveCount(2);
        mode.Options.Should().ContainEquivalentOf(new { Value = 0L, Label = "Off" });
        mode.Options.Should().ContainEquivalentOf(new { Value = 1L, Label = "On" });

        var plain = tags.Single(t => t.Name == "Plain");
        plain.Options.Should().BeEmpty();
    }
}
```

(Add `using System.Linq;` if needed.)

- [ ] **Step 4.3: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagConfigLoaderOptionsTests"
```

Expected: `Mode.Options` is empty.

- [ ] **Step 4.4: Update `TagConfigLoader.ParseTag`**

In `src/SiemensS7Demo/Services/TagConfigLoader.cs`, replace the `ParseTag` body — specifically, build the `TagDefinition` with the `Options` field populated. Insert this just before `return new TagDefinition { ... }`:

```csharp
        var options = element.Elements("Option")
            .Select(opt => new TagOption(
                long.Parse(Required(opt, "value"), CultureInfo.InvariantCulture),
                Required(opt, "name")))
            .ToList();
```

And add `Options = options` to the object initializer.

The full final `ParseTag` looks like:

```csharp
    private static TagDefinition ParseTag(XElement element)
    {
        var options = element.Elements("Option")
            .Select(opt => new TagOption(
                long.Parse(Required(opt, "value"), CultureInfo.InvariantCulture),
                Required(opt, "name")))
            .ToList();

        return new TagDefinition
        {
            Name = Required(element, "name"),
            DisplayName = Required(element, "displayName"),
            Group = Required(element, "group"),
            Address = Required(element, "address"),
            DataType = ParseEnum<TagDataType>(Required(element, "dataType"), "dataType"),
            Unit = (string?)element.Attribute("unit") ?? string.Empty,
            Scale = ParseDouble(element, "scale", 1.0),
            Offset = ParseDouble(element, "offset", 0.0),
            Access = ParseEnum<TagAccess>((string?)element.Attribute("access") ?? nameof(TagAccess.Read), "access"),
            SafeWrite = ParseBool(element, "safeWrite", false),
            Min = ParseNullableDouble(element, "min"),
            Max = ParseNullableDouble(element, "max"),
            Options = options
        };
    }
```

- [ ] **Step 4.5: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagConfigLoaderOptionsTests"
```

Expected: 1 passing.

- [ ] **Step 4.6: Commit**

```bash
git add src/SiemensS7Demo/Services/TagConfigLoader.cs tests/EnviroEquipment.Tests/Services/TagConfigLoaderOptionsTests.cs tests/EnviroEquipment.Tests/Services/Fixtures/options.xml tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj
git commit -m "feat(loader): parse <Option> children into TagDefinition.Options"
```

---

## Task 5: ProjectConfigLoader (JSON) path proof

`System.Text.Json` will auto-bind `"options": [{"value": 1, "label": "On"}]` to `IReadOnlyList<TagOption>` because `TagOption` is a record with positional constructor parameters named `Value` and `Label`. We still write one test to lock this behavior.

**Files:** Create fixture + test. No production code change required.

- [ ] **Step 5.1: Create JSON fixture**

Create `tests/EnviroEquipment.Tests/Services/Fixtures/options.project.json`:

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
          "name": "Mode",
          "displayName": "Mode",
          "group": "g",
          "address": "MW0",
          "dataType": "Int16",
          "unit": "",
          "options": [
            { "value": 0, "label": "Off" },
            { "value": 1, "label": "On" }
          ]
        }
      ]
    }
  ]
}
```

Update `tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj`:
```xml
    <None Update="Services/Fixtures/options.project.json" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 5.2: Write the test**

Create `tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderOptionsTests.cs`:

```csharp
using System.IO;
using FluentAssertions;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ProjectConfigLoaderOptionsTests
{
    [Fact]
    public void Load_BindsTagOptionsFromJson()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "options.project.json");
        var project = ProjectConfigLoader.Load(path);

        var tag = project.Devices.Single().Tags.Single();
        tag.Options.Should().HaveCount(2);
        tag.Options[0].Value.Should().Be(0);
        tag.Options[0].Label.Should().Be("Off");
        tag.Options[1].Value.Should().Be(1);
        tag.Options[1].Label.Should().Be("On");
    }
}
```

- [ ] **Step 5.3: Run, confirm pass on first try**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ProjectConfigLoaderOptionsTests"
```

Expected: 1 passing without any production code change. If it fails, fall back to adding `[JsonConstructor]` on `TagOption` or a `JsonInclude` attribute — but try this first.

- [ ] **Step 5.4: Commit**

```bash
git add tests/EnviroEquipment.Tests/Services/Fixtures/options.project.json tests/EnviroEquipment.Tests/Services/ProjectConfigLoaderOptionsTests.cs tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj
git commit -m "test(loader): pin JSON deserialization of TagDefinition.Options"
```

---

## Task 6: Validation

**Files:** Modify `src/SiemensS7Demo/Services/ConfigValidationService.cs`. Create `tests/EnviroEquipment.Tests/Services/ConfigValidationOptionsTests.cs`.

- [ ] **Step 6.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Services/ConfigValidationOptionsTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ConfigValidationOptionsTests
{
    [Fact]
    public void DuplicateOptionValues_AreFlagged()
    {
        var tag = new TagDefinition
        {
            Name = "X", DisplayName = "X", Group = "g", Address = "MW0",
            DataType = TagDataType.Int16, Unit = "",
            Options = new[] { new TagOption(0, "Off"), new TagOption(0, "Also Off") }
        };

        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().Contain(i => i.Severity == ConfigIssueSeverity.Error && i.Message.Contains("duplicate option value", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EmptyOptionLabel_IsFlagged()
    {
        var tag = new TagDefinition
        {
            Name = "X", DisplayName = "X", Group = "g", Address = "MW0",
            DataType = TagDataType.Int16, Unit = "",
            Options = new[] { new TagOption(0, "") }
        };

        var issues = ConfigValidationService.ValidateTags(new[] { tag }, "mock", "test");
        issues.Should().Contain(i => i.Severity == ConfigIssueSeverity.Error && i.Message.Contains("empty", System.StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 6.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ConfigValidationOptionsTests"
```

Expected: 2 failures.

- [ ] **Step 6.3: Add validation rules**

In `src/SiemensS7Demo/Services/ConfigValidationService.cs`, inside the `foreach (var tag in tags)` loop in `ValidateTags`, after the existing `try { ValidateAddress(...) }` block, add:

```csharp
            if (tag.Options.Count > 0)
            {
                var seenValues = new HashSet<long>();
                foreach (var option in tag.Options)
                {
                    if (string.IsNullOrWhiteSpace(option.Label))
                    {
                        issues.Add(Error(tagScope, $"Option value {option.Value} has an empty label."));
                    }
                    if (!seenValues.Add(option.Value))
                    {
                        issues.Add(Error(tagScope, $"Duplicate option value {option.Value}."));
                    }
                }
            }
```

- [ ] **Step 6.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ConfigValidationOptionsTests"
```

Expected: 2 passing.

- [ ] **Step 6.5: Commit**

```bash
git add src/SiemensS7Demo/Services/ConfigValidationService.cs tests/EnviroEquipment.Tests/Services/ConfigValidationOptionsTests.cs
git commit -m "feat(validation): reject duplicate option values and empty labels"
```

---

## Task 7: Open the PR

- [ ] **Step 7.1: Run full test suite + build**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
dotnet build EnviroEquipmentFinalEdition.sln
```

Expected: all green.

- [ ] **Step 7.2: Push + PR**

```bash
git push -u origin feat/gap3-tag-options
gh pr create --title "Gap #3: TagDefinition.Options enum mapping" --body "$(cat <<'EOF'
## Summary
- Introduces `TagOption(long Value, string Label)` record and `TagDefinition.Options` (default empty, no impact on existing tags).
- Adds `TagValue.DisplayValue`. `SiemensS7Client.ReadTagsAsync` looks up the option label when a tag has options and the raw value matches.
- XML loader parses `<Option name="..." value="..."/>` children; JSON loader binds automatically (covered by test).
- Validation rejects duplicate option values and empty labels.

## Test plan
- [x] `dotnet test EnviroEquipmentFinalEdition.sln` — all green
- [x] New tests cover: default empty, label lookup, ReadTagsAsync sets DisplayValue, XML parse, JSON parse, validation rules
- [x] Existing `--self-test` still passes (unchanged behavior on tags without options)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 7.3: Reply to lead**

SendMessage to `team-lead` with PR URL and the test summary line. Mark task #3 complete via TaskUpdate after merge.

---

## Self-Review Checklist

- [ ] No `using static`, no global mutations, no emojis.
- [ ] `TagDefinition` callers that construct without `Options` still compile (default empty).
- [ ] `Options` field is `IReadOnlyList<TagOption>`, init-only.
- [ ] Validation messages contain searchable keywords ("duplicate option value", "empty").
- [ ] Loaders + validation + client all carry the field; nothing drops it.
