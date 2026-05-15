# Gap #6 — Address Synthesis from Legacy XML Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Teach `TagConfigLoader` to ingest the legacy `addressConfig.xml` files shipped in `addressProtocol/`. Each file's root element is `<root>` (not `<Configuration>`), and tags are grouped as `<ParamType GroupName="...">` / `<Param ParamName="...">` with child elements `<address>`, `<area>`, `<dbnumber>`, `<type>`, `<deviation>`, `<scale>`. The loader synthesizes an `Address` string, a `DataType`, and `ScaleMode = Divisor` from those fields, producing a fully-formed `TagDefinition` list that passes validation with no further processing.

**Architecture:** Add a new static method `TagConfigLoader.LoadLegacy(string path)` that reads the `<root>/<ParamType>/<Param>` structure. A private `SynthesizeFromLegacyParam` helper encapsulates the address-synthesis logic per the table below. All seven type tokens are handled (HRS/int16, HRF/RF/Real, V/bit, Q/coil, HR, HRD, HRU/HRDU). Scale normalization: if legacy `<scale>` element text parses to 0 → `Scale=1, ScaleMode=Multiplier`; if > 0 → `Scale=N, ScaleMode=Divisor`; if absent → `Scale=1, ScaleMode=Multiplier`. The public `Load(string path)` method is unchanged — it reads the modern `<Configuration>/<Tags>/<Tag>` format. Legacy vs. modern is distinguished by the root element name (`root` → legacy, `Configuration` → modern). A single dispatch helper `LoadAuto(string path)` branches on root element and can be used when callers don't know which format they have.

**Merge order dependency:** This plan depends on Gap #5 (`feat/gap5-scale-divisor`) being merged first. `TagDefinition.ScaleMode` must exist before this PR compiles. If Gap #5 is not yet on `main`, rebase onto `feat/gap5-scale-divisor`.

**Tech Stack:** C# .NET 8, xunit, FluentAssertions, `System.Xml.Linq`, `System.Text.Encoding`.

**Scope guard:** Only the legacy XML loader path and address synthesis logic. No changes to adapters, drivers, or the modern `Load(path)` path. `ProjectConfigLoader` is not modified.

**Branch:** `feat/gap6-address-synthesis`
**Worktree:** `.claude/worktrees/gap6-address-synthesis`
**Base:** `main` after Gap #5 (`feat/gap5-scale-divisor`) is merged.

---

## Address Synthesis Table

| Legacy `<area>` | Legacy `<type>` | Synthesized `Address` | Synthesized `DataType` |
|---|---|---|---|
| `db` | `HRS` / `HRS（int16）` | `DB{dbnumber}.DBW{address}` | `Int16` |
| `db` | `HRF` / `RF` / `Real` / `HRF（Real）` | `DB{dbnumber}.DBD{address}` | `Real` |
| *(any)* | `V` | `V{address}.{deviation}` | `Bool` |
| *(none/Schneider)* | `Q` | `C{address}` | `Bool` |
| *(none/Schneider)* | `HR` / `HRS` | `HR{address}` | `Int16` |
| *(none/Schneider)* | `HRF` | `HRF{address}` | `Real` |
| *(none/Schneider)* | `HRD` | `HRD{address}` | `DInt` |
| *(none/Schneider)* | `HRU` / `HRDU` | `HRDU{address}` | `UInt32` |

Notes:
- When `<area>` is `db` (Siemens DB), `<dbnumber>` is used; when absent (Schneider), it is ignored.
- V-area addresses (`type=V`) use `{address}.{deviation}` as the bit index — this maps to the `V{byte}.{bit}` notation already accepted by `S7Address.Parse` (e.g. `V9.6`).
- Type tokens are matched case-insensitively and the parenthetical suffix `（int16）` / `（Real）` is stripped.
- If a `<type>` token does not match any known value, throw `InvalidOperationException` with the tag name and raw type string.

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Modify | `src/SiemensS7Demo/Services/TagConfigLoader.cs` | Add `LoadLegacy`, `LoadAuto`, `ParseLegacyParam`, `SynthesizeLegacyAddress` |
| Create | `tests/EnviroEquipment.Tests/Services/Fixtures/legacy_siemens_sample.xml` | Minimal Siemens legacy fixture (3 tags) |
| Create | `tests/EnviroEquipment.Tests/Services/Fixtures/legacy_schneider_sample.xml` | Minimal Schneider legacy fixture (4 tags) |
| Create | `tests/EnviroEquipment.Tests/Services/TagConfigLoaderLegacyTests.cs` | Full fixture round-trip tests |
| Create | `tests/EnviroEquipment.Tests/Drivers/S7AddressSynthesisTests.cs` | Unit tests for synthesized address strings accepted by `S7Address.Parse` |

---

## Task 1: Siemens fixture + address synthesis unit tests (pure-function, no loader yet)

These tests drive the synthesis table before the actual loader exists. They call `TagConfigLoader.SynthesizeLegacyAddress` (which will be `internal static`).

**Files:** Create `tests/EnviroEquipment.Tests/Drivers/S7AddressSynthesisTests.cs`.

- [ ] **Step 1.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Drivers/S7AddressSynthesisTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

/// <summary>
/// Verifies that addresses synthesized by TagConfigLoader.SynthesizeLegacyAddress
/// round-trip through the appropriate parser (S7Address or ModbusAddress) without error.
/// </summary>
public class S7AddressSynthesisTests
{
    // --- Siemens DB int16 ---
    [Fact]
    public void Siemens_DbInt16_SynthesizesDBW()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "db", dbnumber: 1, rawAddress: 336, type: "HRS（int16）", deviation: 0);
        address.Should().Be("DB1.DBW336");
        dataType.Should().Be(TagDataType.Int16);
        // Must parse without throwing.
        S7Address.Parse(MakeTag(address, dataType));
    }

    // --- Siemens DB real ---
    [Fact]
    public void Siemens_DbReal_SynthesizesDBD()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "db", dbnumber: 1, rawAddress: 340, type: "HRF（Real）", deviation: 0);
        address.Should().Be("DB1.DBD340");
        dataType.Should().Be(TagDataType.Real);
        S7Address.Parse(MakeTag(address, dataType));
    }

    // --- Siemens V bit ---
    [Fact]
    public void Siemens_VBit_SynthesizesVDotBit()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "v", dbnumber: 0, rawAddress: 9, type: "V", deviation: 6);
        address.Should().Be("V9.6");
        dataType.Should().Be(TagDataType.Bool);
        S7Address.Parse(MakeTag(address, dataType));
    }

    // --- Schneider coil ---
    [Fact]
    public void Schneider_Coil_SynthesizesCAddress()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "", dbnumber: 0, rawAddress: 80, type: "Q", deviation: 0);
        address.Should().Be("C80");
        dataType.Should().Be(TagDataType.Bool);
    }

    // --- Schneider HR int16 ---
    [Fact]
    public void Schneider_HrInt16_SynthesizesHRAddress()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "", dbnumber: 0, rawAddress: 100, type: "HRS", deviation: 0);
        address.Should().Be("HR100");
        dataType.Should().Be(TagDataType.Int16);
    }

    // --- Schneider HR float ---
    [Fact]
    public void Schneider_HrFloat_SynthesizesHRFAddress()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "", dbnumber: 0, rawAddress: 200, type: "HRF", deviation: 0);
        address.Should().Be("HRF200");
        dataType.Should().Be(TagDataType.Real);
    }

    // --- Schneider HR dint ---
    [Fact]
    public void Schneider_HrDint_SynthesizesHRDAddress()
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "", dbnumber: 0, rawAddress: 300, type: "HRD", deviation: 0);
        address.Should().Be("HRD300");
        dataType.Should().Be(TagDataType.DInt);
    }

    // --- Schneider HR uint32 ---
    [Theory]
    [InlineData("HRU")]
    [InlineData("HRDU")]
    public void Schneider_HrUint32_SynthesizesHRDUAddress(string type)
    {
        var (address, dataType) = TagConfigLoader.SynthesizeLegacyAddress(
            area: "", dbnumber: 0, rawAddress: 400, type: type, deviation: 0);
        address.Should().Be("HRDU400");
        dataType.Should().Be(TagDataType.UInt32);
    }

    // --- Unknown type throws ---
    [Fact]
    public void UnknownType_Throws()
    {
        var act = () => TagConfigLoader.SynthesizeLegacyAddress(
            area: "db", dbnumber: 1, rawAddress: 0, type: "BOGUS", deviation: 0);
        act.Should().Throw<System.InvalidOperationException>()
            .WithMessage("*BOGUS*");
    }

    private static TagDefinition MakeTag(string address, TagDataType dt) => new()
    {
        Name = "T", DisplayName = "T", Group = "g", Address = address, DataType = dt, Unit = ""
    };
}
```

- [ ] **Step 1.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~S7AddressSynthesisTests"
```

Expected: compile error — `TagConfigLoader.SynthesizeLegacyAddress` does not exist yet.

- [ ] **Step 1.3: Add `SynthesizeLegacyAddress` to `TagConfigLoader`**

In `src/SiemensS7Demo/Services/TagConfigLoader.cs`, add this `internal static` method at the end of the class (after `ParseEnum`). It must be `internal` so the test assembly can reach it via `InternalsVisibleTo` (already declared in `SiemensS7Demo.csproj` for Gap #1).

```csharp
    /// <summary>
    /// Pure synthesis function: given legacy XML fields, produces an (Address, DataType) pair.
    /// Called by <see cref="LoadLegacy"/> and directly testable.
    /// </summary>
    internal static (string Address, TagDataType DataType) SynthesizeLegacyAddress(
        string area, int dbnumber, int rawAddress, string type, int deviation)
    {
        // Normalize: strip parenthetical suffixes like "（int16）" or "（Real）"
        var t = NormalizeLegacyType(type);
        var isDb = string.Equals(area, "db", StringComparison.OrdinalIgnoreCase);

        return t switch
        {
            // Siemens DB int16
            "HRS" when isDb => ($"DB{dbnumber}.DBW{rawAddress}", TagDataType.Int16),

            // Siemens DB real (HRF, RF, Real all mean float in a DB)
            "HRF" or "RF" or "REAL" when isDb => ($"DB{dbnumber}.DBD{rawAddress}", TagDataType.Real),

            // Siemens V-area bit (type=V anywhere, includes V-area PLCs)
            "V" => ($"V{rawAddress}.{deviation}", TagDataType.Bool),

            // Schneider coil (Q = discrete output coil)
            "Q" => ($"C{rawAddress}", TagDataType.Bool),

            // Schneider holding register int16
            "HR" or "HRS" => ($"HR{rawAddress}", TagDataType.Int16),

            // Schneider float
            "HRF" or "RF" or "REAL" => ($"HRF{rawAddress}", TagDataType.Real),

            // Schneider DInt (32-bit signed)
            "HRD" => ($"HRD{rawAddress}", TagDataType.DInt),

            // Schneider UInt32 (32-bit unsigned) — both legacy names map to HRDU
            "HRU" or "HRDU" => ($"HRDU{rawAddress}", TagDataType.UInt32),

            _ => throw new InvalidOperationException(
                $"Unknown legacy type token '{type}' (normalized: '{t}'). " +
                "Supported: HRS, HRF, RF, Real, V, Q, HR, HRD, HRU, HRDU.")
        };
    }

    private static string NormalizeLegacyType(string raw)
    {
        // Strip parenthetical suffix: "HRS（int16）" → "HRS", "HRF（Real）" → "HRF"
        var idx = raw.IndexOf('（');
        var core = idx >= 0 ? raw[..idx] : raw;
        // Also handle ASCII parenthesis just in case: "HRS(int16)" → "HRS"
        idx = core.IndexOf('(');
        core = idx >= 0 ? core[..idx] : core;
        return core.Trim().ToUpperInvariant();
    }
```

- [ ] **Step 1.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~S7AddressSynthesisTests"
```

Expected: 10 passing.

- [ ] **Step 1.5: Commit**

```bash
git add src/SiemensS7Demo/Services/TagConfigLoader.cs tests/EnviroEquipment.Tests/Drivers/S7AddressSynthesisTests.cs
git commit -m "feat(loader): SynthesizeLegacyAddress maps legacy type/area to Address+DataType"
```

---

## Task 2: Fixtures for the legacy loader

**Files:** Create two XML fixture files; update csproj.

- [ ] **Step 2.1: Create Siemens fixture**

Create `tests/EnviroEquipment.Tests/Services/Fixtures/legacy_siemens_sample.xml`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<root>
  <ParamType GroupName="PID参数">
    <Param ParamName="温度加热T0">
      <address>336</address>
      <area>db</area>
      <dbnumber>1</dbnumber>
      <type>HRS（int16）</type>
      <deviation>0</deviation>
      <scale>10</scale>
    </Param>
    <Param ParamName="温度加热T0实际值">
      <address>340</address>
      <area>db</area>
      <dbnumber>1</dbnumber>
      <type>HRF（Real）</type>
      <deviation>0</deviation>
      <scale>0</scale>
    </Param>
  </ParamType>
  <ParamType GroupName="状态">
    <Param ParamName="加热运行">
      <address>9</address>
      <area>v</area>
      <dbnumber>1</dbnumber>
      <type>V</type>
      <deviation>6</deviation>
      <scale>0</scale>
    </Param>
  </ParamType>
</root>
```

- [ ] **Step 2.2: Create Schneider fixture**

Create `tests/EnviroEquipment.Tests/Services/Fixtures/legacy_schneider_sample.xml`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<root>
  <ParamType GroupName="输出">
    <Param ParamName="压缩机启动">
      <address>80</address>
      <area></area>
      <dbnumber>0</dbnumber>
      <type>Q</type>
      <deviation>0</deviation>
      <scale>0</scale>
    </Param>
  </ParamType>
  <ParamType GroupName="模拟量">
    <Param ParamName="回气压力">
      <address>100</address>
      <area></area>
      <dbnumber>0</dbnumber>
      <type>HRS</type>
      <deviation>0</deviation>
      <scale>10</scale>
    </Param>
    <Param ParamName="排气温度">
      <address>200</address>
      <area></area>
      <dbnumber>0</dbnumber>
      <type>HRF</type>
      <deviation>0</deviation>
      <scale>0</scale>
    </Param>
    <Param ParamName="总电量">
      <address>400</address>
      <area></area>
      <dbnumber>0</dbnumber>
      <type>HRDU</type>
      <deviation>0</deviation>
      <scale>0</scale>
    </Param>
  </ParamType>
</root>
```

- [ ] **Step 2.3: Update csproj**

In `tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj`, add inside the fixtures `<ItemGroup>`:

```xml
    <None Update="Services/Fixtures/legacy_siemens_sample.xml" CopyToOutputDirectory="PreserveNewest" />
    <None Update="Services/Fixtures/legacy_schneider_sample.xml" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2.4: Commit fixtures**

```bash
git add tests/EnviroEquipment.Tests/Services/Fixtures/legacy_siemens_sample.xml tests/EnviroEquipment.Tests/Services/Fixtures/legacy_schneider_sample.xml tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj
git commit -m "test(fixtures): add legacy Siemens and Schneider addressConfig sample fixtures"
```

---

## Task 3: `TagConfigLoader.LoadLegacy` (TDD)

**Files:** Modify `src/SiemensS7Demo/Services/TagConfigLoader.cs`. Create `tests/EnviroEquipment.Tests/Services/TagConfigLoaderLegacyTests.cs`.

- [ ] **Step 3.1: Write failing tests**

Create `tests/EnviroEquipment.Tests/Services/TagConfigLoaderLegacyTests.cs`:

```csharp
using System.IO;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class TagConfigLoaderLegacyTests
{
    private static string SiemensFixture =>
        Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "legacy_siemens_sample.xml");

    private static string SchneiderFixture =>
        Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "legacy_schneider_sample.xml");

    // --- Siemens DB int16 with scale=10 ---
    [Fact]
    public void LoadLegacy_Siemens_DbInt16_ScaleDivisor10()
    {
        var tags = TagConfigLoader.LoadLegacy(SiemensFixture);
        var tag = tags.Single(t => t.Name == "温度加热T0");

        tag.Address.Should().Be("DB1.DBW336");
        tag.DataType.Should().Be(TagDataType.Int16);
        tag.Scale.Should().Be(10.0);
        tag.ScaleMode.Should().Be(ScaleMode.Divisor);
        tag.Group.Should().Be("PID参数");
    }

    // --- Siemens DB real with scale=0 → normalized to Scale=1, Multiplier ---
    [Fact]
    public void LoadLegacy_Siemens_DbReal_ZeroScaleNormalized()
    {
        var tags = TagConfigLoader.LoadLegacy(SiemensFixture);
        var tag = tags.Single(t => t.Name == "温度加热T0实际值");

        tag.Address.Should().Be("DB1.DBD340");
        tag.DataType.Should().Be(TagDataType.Real);
        tag.Scale.Should().Be(1.0);
        tag.ScaleMode.Should().Be(ScaleMode.Multiplier);
    }

    // --- Siemens V bit ---
    [Fact]
    public void LoadLegacy_Siemens_VBit_SynthesizesVAddress()
    {
        var tags = TagConfigLoader.LoadLegacy(SiemensFixture);
        var tag = tags.Single(t => t.Name == "加热运行");

        tag.Address.Should().Be("V9.6");
        tag.DataType.Should().Be(TagDataType.Bool);
        tag.ScaleMode.Should().Be(ScaleMode.Multiplier);
    }

    // --- Total tag count from Siemens fixture ---
    [Fact]
    public void LoadLegacy_Siemens_ReturnsAllThreeTags()
    {
        var tags = TagConfigLoader.LoadLegacy(SiemensFixture);
        tags.Should().HaveCount(3);
    }

    // --- Group name is carried from <ParamType GroupName="..."> ---
    [Fact]
    public void LoadLegacy_SetsGroupFromParamTypeName()
    {
        var tags = TagConfigLoader.LoadLegacy(SiemensFixture);
        tags.Single(t => t.Name == "加热运行").Group.Should().Be("状态");
    }

    // --- Schneider coil ---
    [Fact]
    public void LoadLegacy_Schneider_CoilSynthesizesCAddress()
    {
        var tags = TagConfigLoader.LoadLegacy(SchneiderFixture);
        var tag = tags.Single(t => t.Name == "压缩机启动");

        tag.Address.Should().Be("C80");
        tag.DataType.Should().Be(TagDataType.Bool);
    }

    // --- Schneider HR int16 with scale=10 ---
    [Fact]
    public void LoadLegacy_Schneider_HrInt16_ScaleDivisor10()
    {
        var tags = TagConfigLoader.LoadLegacy(SchneiderFixture);
        var tag = tags.Single(t => t.Name == "回气压力");

        tag.Address.Should().Be("HR100");
        tag.DataType.Should().Be(TagDataType.Int16);
        tag.Scale.Should().Be(10.0);
        tag.ScaleMode.Should().Be(ScaleMode.Divisor);
    }

    // --- Schneider HRF (float) with scale=0 ---
    [Fact]
    public void LoadLegacy_Schneider_HrFloat_ZeroScaleNormalized()
    {
        var tags = TagConfigLoader.LoadLegacy(SchneiderFixture);
        var tag = tags.Single(t => t.Name == "排气温度");

        tag.Address.Should().Be("HRF200");
        tag.DataType.Should().Be(TagDataType.Real);
        tag.Scale.Should().Be(1.0);
        tag.ScaleMode.Should().Be(ScaleMode.Multiplier);
    }

    // --- Schneider HRDU (uint32) ---
    [Fact]
    public void LoadLegacy_Schneider_HrduUint32()
    {
        var tags = TagConfigLoader.LoadLegacy(SchneiderFixture);
        var tag = tags.Single(t => t.Name == "总电量");

        tag.Address.Should().Be("HRDU400");
        tag.DataType.Should().Be(TagDataType.UInt32);
    }
}
```

- [ ] **Step 3.2: Run, confirm failure**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagConfigLoaderLegacyTests"
```

Expected: compile error — `TagConfigLoader.LoadLegacy` does not exist yet.

- [ ] **Step 3.3: Implement `LoadLegacy` and `LoadAuto` in `TagConfigLoader`**

In `src/SiemensS7Demo/Services/TagConfigLoader.cs`, add the following public methods and private helpers at the top of the class body (before `Load`), and the private helpers after `ParseTag`:

```csharp
    /// <summary>
    /// Loads a legacy <c>addressConfig.xml</c> file with root element <c>&lt;root&gt;</c>.
    /// Synthesizes <see cref="TagDefinition.Address"/>, <see cref="TagDefinition.DataType"/>,
    /// and <see cref="TagDefinition.ScaleMode"/> from the child elements
    /// <c>&lt;address&gt;</c>, <c>&lt;area&gt;</c>, <c>&lt;dbnumber&gt;</c>,
    /// <c>&lt;type&gt;</c>, <c>&lt;deviation&gt;</c>, <c>&lt;scale&gt;</c>.
    /// </summary>
    public static IReadOnlyList<TagDefinition> LoadLegacy(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Legacy tag configuration file was not found.", configPath);
        }

        var document = XDocument.Load(configPath, LoadOptions.None);
        if (!string.Equals(document.Root?.Name.LocalName, "root", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected legacy XML root element <root>, found <{document.Root?.Name.LocalName}>. " +
                "Use TagConfigLoader.Load() for modern <Configuration> files.");
        }

        var tags = new List<TagDefinition>();
        foreach (var paramType in document.Root.Elements("ParamType"))
        {
            var groupName = (string?)paramType.Attribute("GroupName") ?? string.Empty;
            foreach (var param in paramType.Elements("Param"))
            {
                tags.Add(ParseLegacyParam(param, groupName));
            }
        }

        if (tags.Count == 0)
        {
            throw new InvalidOperationException($"No tags found in legacy file '{configPath}'.");
        }

        return tags;
    }

    /// <summary>
    /// Loads a tag configuration file of either format by inspecting the root element.
    /// </summary>
    public static IReadOnlyList<TagDefinition> LoadAuto(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Tag configuration file was not found.", configPath);
        }

        var root = XDocument.Load(configPath, LoadOptions.None).Root?.Name.LocalName;
        return string.Equals(root, "root", StringComparison.OrdinalIgnoreCase)
            ? LoadLegacy(configPath)
            : Load(configPath);
    }
```

Add the private `ParseLegacyParam` method after `ParseTag`:

```csharp
    private static TagDefinition ParseLegacyParam(XElement param, string groupName)
    {
        var paramName = (string?)param.Attribute("ParamName")
            ?? throw new InvalidOperationException("Legacy <Param> is missing required ParamName attribute.");

        var rawAddress = int.Parse(
            param.Element("address")?.Value?.Trim() ?? "0",
            CultureInfo.InvariantCulture);
        var area = param.Element("area")?.Value?.Trim() ?? string.Empty;
        var dbnumber = int.Parse(
            param.Element("dbnumber")?.Value?.Trim() ?? "0",
            CultureInfo.InvariantCulture);
        var type = param.Element("type")?.Value?.Trim()
            ?? throw new InvalidOperationException(
                $"Legacy <Param ParamName=\"{paramName}\"> is missing <type> element.");
        var deviation = int.Parse(
            param.Element("deviation")?.Value?.Trim() ?? "0",
            CultureInfo.InvariantCulture);
        var scaleText = param.Element("scale")?.Value?.Trim();
        var scaleRaw = string.IsNullOrWhiteSpace(scaleText)
            ? 0.0
            : double.Parse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture);

        // Normalize scale: 0 → identity (Scale=1, Multiplier); >0 → Divisor
        double scale;
        ScaleMode scaleMode;
        if (Math.Abs(scaleRaw) < double.Epsilon)
        {
            scale = 1.0;
            scaleMode = ScaleMode.Multiplier;
        }
        else
        {
            scale = scaleRaw;
            scaleMode = ScaleMode.Divisor;
        }

        var (address, dataType) = SynthesizeLegacyAddress(area, dbnumber, rawAddress, type, deviation);

        return new TagDefinition
        {
            Name = paramName,
            DisplayName = paramName,
            Group = groupName,
            Address = address,
            DataType = dataType,
            Unit = string.Empty,
            Scale = scale,
            ScaleMode = scaleMode,
            Offset = 0.0,
            Access = TagAccess.Read,
            SafeWrite = false
        };
    }
```

- [ ] **Step 3.4: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagConfigLoaderLegacyTests"
```

Expected: 9 passing.

- [ ] **Step 3.5: Run full suite to confirm no regression**

```bash
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green.

- [ ] **Step 3.6: Commit**

```bash
git add src/SiemensS7Demo/Services/TagConfigLoader.cs tests/EnviroEquipment.Tests/Services/TagConfigLoaderLegacyTests.cs
git commit -m "feat(loader): LoadLegacy and LoadAuto parse legacy <root> addressConfig.xml"
```

---

## Task 4: Validation passes for synthesized tags

Confirm `ConfigValidationService.ValidateTags` accepts the synthesized tags from both fixtures. This proves the Address strings, DataTypes, and ScaleModes produced are all valid. Uses `mock` protocol to avoid triggering the Modbus/S7 address parsers on the synthesized addresses (the legacy loader is format-agnostic; protocol is determined at device connection time, not load time).

**Files:** Add tests to `tests/EnviroEquipment.Tests/Services/TagConfigLoaderLegacyTests.cs`.

- [ ] **Step 4.1: Append validation tests**

Append to the existing `TagConfigLoaderLegacyTests` class:

```csharp
    [Fact]
    public void LoadLegacy_Siemens_TagsPassValidationWithMockProtocol()
    {
        var tags = TagConfigLoader.LoadLegacy(SiemensFixture);
        var issues = ConfigValidationService.ValidateTags(tags, "mock", "test");
        issues.Should().NotContain(i => i.Severity == ConfigIssueSeverity.Error);
    }

    [Fact]
    public void LoadLegacy_Schneider_TagsPassValidationWithMockProtocol()
    {
        var tags = TagConfigLoader.LoadLegacy(SchneiderFixture);
        var issues = ConfigValidationService.ValidateTags(tags, "mock", "test");
        issues.Should().NotContain(i => i.Severity == ConfigIssueSeverity.Error);
    }
```

(Add `using SiemensS7Demo.Services;` if not already present at the top of the file.)

- [ ] **Step 4.2: Run, confirm pass**

```bash
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TagConfigLoaderLegacyTests"
```

Expected: 11 passing.

- [ ] **Step 4.3: Commit**

```bash
git add tests/EnviroEquipment.Tests/Services/TagConfigLoaderLegacyTests.cs
git commit -m "test(loader): validate legacy-loaded tags pass ConfigValidationService"
```

---

## Task 5: Open the PR

- [ ] **Step 5.1: Full build + test**

```bash
dotnet build EnviroEquipmentFinalEdition.sln
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected: all green.

- [ ] **Step 5.2: Push + PR**

```bash
git push -u origin feat/gap6-address-synthesis
gh pr create --title "Gap #6: address synthesis from legacy addressConfig.xml" --body "$(cat <<'EOF'
## Summary
- Adds `TagConfigLoader.LoadLegacy(path)` that reads the legacy `<root>/<ParamType>/<Param>` XML format.
- Synthesizes `Address`, `DataType`, and `ScaleMode` from legacy fields `area/dbnumber/address/type/deviation/scale` per the synthesis table.
- Adds `TagConfigLoader.LoadAuto(path)` that dispatches to `Load` or `LoadLegacy` by root element name.
- Scale normalization: `scale=0` → `Scale=1, ScaleMode=Multiplier`; `scale>0` → `Scale=N, ScaleMode=Divisor` (depends on Gap #5).
- Adds `TagConfigLoader.SynthesizeLegacyAddress` as an `internal static` pure function, tested independently.

## Merge order
This PR requires Gap #5 (`feat/gap5-scale-divisor`) to be merged first. `TagDefinition.ScaleMode` must exist in `main` before this branch compiles.

## Supported type tokens
`HRS`/`HRS（int16）` → DBW / HR; `HRF`/`RF`/`Real`/`HRF（Real）` → DBD / HRF; `V` → V{byte}.{bit}; `Q` → C{n}; `HRD` → HRD{n}; `HRU`/`HRDU` → HRDU{n}.

## Test plan
- [x] `dotnet test EnviroEquipmentFinalEdition.sln` — all green
- [x] `S7AddressSynthesisTests` (10): all 8 type tokens + unknown-throws; synthesized addresses round-trip through `S7Address.Parse`
- [x] `TagConfigLoaderLegacyTests` (11): Siemens fixture (3 tags × address/datatype/scale/group assertions) + Schneider fixture (4 tags) + validation passes for both
- [x] Modern `Load()` path unaffected

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Self-Review Checklist

- [ ] No emojis in code or commit messages.
- [ ] `Load()` and `ParseTag()` are unchanged — zero impact on existing modern-XML callers.
- [ ] `LoadLegacy` throws `FileNotFoundException` if path missing, `InvalidOperationException` if root element wrong.
- [ ] `SynthesizeLegacyAddress` is `internal static` (not `private`) so tests can reach it via `InternalsVisibleTo`.
- [ ] Scale normalization: `scale=0` → `Scale=1, Multiplier`; `scale>0` → `Scale=N, Divisor`.
- [ ] `NormalizeLegacyType` strips both fullwidth `（` and ASCII `(` parenthetical suffixes.
- [ ] All synthesized addresses for Siemens DB round-trip through `S7Address.Parse` (covered by `S7AddressSynthesisTests`).
- [ ] `LoadAuto` dispatches by root element name, case-insensitive.
