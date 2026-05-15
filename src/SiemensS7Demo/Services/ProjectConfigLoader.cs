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
