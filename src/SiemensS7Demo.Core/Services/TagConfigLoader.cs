using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Services;

public static class TagConfigLoader
{
    /// <summary>
    /// Loads a legacy <c>addressConfig.xml</c> file with root element <c>&lt;root&gt;</c>.
    /// Synthesizes <see cref="TagDefinition.Address"/>, <see cref="TagDefinition.DataType"/>,
    /// and <see cref="TagDefinition.ScaleMode"/> from the child elements
    /// <c>&lt;address&gt;</c>, <c>&lt;area&gt;</c>, <c>&lt;dbnumber&gt;</c>,
    /// <c>&lt;type&gt;</c>, <c>&lt;deviation&gt;</c>, <c>&lt;scale&gt;</c>.
    /// </summary>
    public static IReadOnlyList<TagDefinition> LoadLegacy(string configPath)
        => LoadLegacy(configPath, out _);

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
                $"Expected legacy XML root element <root>, found <{document.Root?.Name.LocalName}>. " +
                "Use TagConfigLoader.Load() for modern <Configuration> files.");
        }

        var tags = new List<TagDefinition>();
        var auxList = new List<AuxiliaryFunction>();

        foreach (var paramType in document.Root!.Elements("ParamType"))
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

    public static IReadOnlyList<TagDefinition> Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Tag configuration file was not found.", configPath);
        }

        var document = XDocument.Load(configPath);
        var tags = document.Root?
            .Element("Tags")?
            .Elements("Tag")
            .Select(ParseTag)
            .ToList();

        if (tags is null || tags.Count == 0)
        {
            throw new InvalidOperationException($"No tags found in '{configPath}'.");
        }

        return tags;
    }

    private static TagDefinition ParseTag(XElement element)
    {
        var options = element.Elements("Option")
            .Select(opt => new TagOption(
                long.Parse(Required(opt, "value"), CultureInfo.InvariantCulture),
                Required(opt, "name")))
            .ToList();

        var derivations = element.Elements("DeviationList")
            .Select(d => new BitDerivation(
                Required(d, "name"),
                int.Parse(Required(d, "deviation"), CultureInfo.InvariantCulture),
                (string?)d.Attribute("displayName")))
            .ToList();

        var scaleModeText = (string?)element.Attribute("scaleMode");
        var scaleMode = string.IsNullOrWhiteSpace(scaleModeText)
            ? ScaleMode.Multiplier
            : ParseEnum<ScaleMode>(scaleModeText, "scaleMode");

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
    }

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

    private static string Required(XElement element, string name)
    {
        var value = (string?)element.Attribute(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Tag is missing required '{name}' attribute.");
        }

        return value;
    }

    private static double ParseDouble(XElement element, string name, double fallback)
    {
        var text = (string?)element.Attribute(name);
        return string.IsNullOrWhiteSpace(text)
            ? fallback
            : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static double? ParseNullableDouble(XElement element, string name)
    {
        var text = (string?)element.Attribute(name);
        return string.IsNullOrWhiteSpace(text)
            ? null
            : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static bool ParseBool(XElement element, string name, bool fallback)
    {
        var text = (string?)element.Attribute(name);
        return string.IsNullOrWhiteSpace(text) ? fallback : bool.Parse(text);
    }

    private static TEnum ParseEnum<TEnum>(string value, string attributeName)
        where TEnum : struct
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Unsupported {attributeName} value '{value}'.");
    }

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
}
