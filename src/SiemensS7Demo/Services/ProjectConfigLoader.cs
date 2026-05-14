using System;
using System.IO;
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

        foreach (var device in project.Devices)
        {
            if (string.IsNullOrWhiteSpace(device.Id))
            {
                throw new InvalidOperationException("Every device must have an id.");
            }

            if (device.Tags.Count == 0)
            {
                throw new InvalidOperationException($"Device '{device.Id}' contains no tags.");
            }
        }

        return project;
    }
}
