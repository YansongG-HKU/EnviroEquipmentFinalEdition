using System;
using System.Collections.Generic;
using System.Linq;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Services;

public enum ConfigIssueSeverity
{
    Error,
    Warning
}

public sealed record ConfigValidationIssue(
    ConfigIssueSeverity Severity,
    string Scope,
    string Message);

public static class ConfigValidationService
{
    public static IReadOnlyList<ConfigValidationIssue> ValidateTags(
        IReadOnlyList<TagDefinition> tags,
        string protocol,
        string scope)
    {
        var issues = new List<ConfigValidationIssue>();
        var normalizedProtocol = NormalizeProtocol(protocol);

        var duplicateNames = tags
            .GroupBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var name in duplicateNames)
        {
            issues.Add(Error(scope, $"Duplicate tag name '{name}'."));
        }

        // Check derivation names don't collide with sibling tag names.
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
                        $"BitDerivation name '{derivation.Name}' collides with a sibling tag of the same name."));
                }
            }
        }

        foreach (var tag in tags)
        {
            var tagScope = $"{scope}/{tag.Name}";
            if (string.IsNullOrWhiteSpace(tag.Address))
            {
                issues.Add(Error(tagScope, "Address is required."));
                continue;
            }

            if (tag.DataType != TagDataType.Bool && Math.Abs(tag.Scale) < double.Epsilon)
            {
                issues.Add(Error(tagScope, "Scale must not be 0 for numeric tags."));
            }

            if (tag.Min.HasValue && tag.Max.HasValue && tag.Min.Value > tag.Max.Value)
            {
                issues.Add(Error(tagScope, $"Min {tag.Min.Value} is greater than max {tag.Max.Value}."));
            }

            if (tag.SafeWrite && tag.Access == TagAccess.Read)
            {
                issues.Add(Error(tagScope, "safeWrite=true is invalid on a read-only tag."));
            }

            if (tag.Access != TagAccess.Read && !tag.SafeWrite)
            {
                issues.Add(Warning(tagScope, "Write-capable tag is locked because safeWrite=false."));
            }

            try
            {
                ValidateAddress(normalizedProtocol, tag);
            }
            catch (Exception ex)
            {
                issues.Add(Error(tagScope, ex.Message));
            }

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

                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var derivation in tag.BitDerivations)
                {
                    if (maxBit >= 0 && (derivation.BitOffset < 0 || derivation.BitOffset > maxBit))
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

        return issues;
    }

    public static IReadOnlyList<ConfigValidationIssue> ValidateProject(ProjectDefinition project)
    {
        var issues = new List<ConfigValidationIssue>();
        if (string.IsNullOrWhiteSpace(project.ProjectId))
        {
            issues.Add(Error("project", "ProjectId is required."));
        }

        var duplicateDeviceIds = project.Devices
            .GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var id in duplicateDeviceIds)
        {
            issues.Add(Error("project", $"Duplicate device id '{id}'."));
        }

        foreach (var device in project.Devices)
        {
            var scope = $"device:{device.Id}";
            if (string.IsNullOrWhiteSpace(device.Id))
            {
                issues.Add(Error(scope, "Device id is required."));
            }

            if (string.IsNullOrWhiteSpace(device.Ip))
            {
                issues.Add(Error(scope, "Device IP is required."));
            }

            if (device.Port is < 1 or > 65535)
            {
                issues.Add(Error(scope, $"Port {device.Port} is outside 1..65535."));
            }

            if (device.PollingIntervalMs is < 100 or > 600000)
            {
                issues.Add(Error(scope, $"PollingIntervalMs {device.PollingIntervalMs} is outside 100..600000."));
            }

            try
            {
                NormalizeProtocol(device.Protocol);
            }
            catch (Exception ex)
            {
                issues.Add(Error(scope, ex.Message));
            }

            issues.AddRange(ValidateTags(device.Tags, device.Protocol, scope));
        }

        return issues;
    }

    public static bool HasErrors(IReadOnlyList<ConfigValidationIssue> issues)
        => issues.Any(issue => issue.Severity == ConfigIssueSeverity.Error);

    private static void ValidateAddress(string normalizedProtocol, TagDefinition tag)
    {
        switch (normalizedProtocol)
        {
            case "s7":
                S7Address.Parse(tag);
                break;
            case "modbus":
                ModbusAddress.Parse(tag);
                break;
            case "mock":
                break;
            default:
                throw new InvalidOperationException($"Unsupported protocol '{normalizedProtocol}'.");
        }
    }

    private static string NormalizeProtocol(string protocol)
    {
        return protocol.ToLowerInvariant() switch
        {
            "snap7" or "s7" or "siemens" => "s7",
            "modbus" or "modbus-tcp" => "modbus",
            "mock" => "mock",
            _ => throw new InvalidOperationException($"Unsupported protocol '{protocol}'.")
        };
    }

    private static ConfigValidationIssue Error(string scope, string message)
        => new(ConfigIssueSeverity.Error, scope, message);

    private static ConfigValidationIssue Warning(string scope, string message)
        => new(ConfigIssueSeverity.Warning, scope, message);
}
