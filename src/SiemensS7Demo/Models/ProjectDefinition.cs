using System.Collections.Generic;

namespace SiemensS7Demo.Models;

public sealed class ProjectDefinition
{
    public string ProjectId { get; init; } = "default";
    public string ProjectName { get; init; } = "Default Project";
    public List<DeviceDefinition> Devices { get; init; } = new();
}
