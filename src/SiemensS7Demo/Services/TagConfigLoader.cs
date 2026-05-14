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
            Max = ParseNullableDouble(element, "max")
        };
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
}
