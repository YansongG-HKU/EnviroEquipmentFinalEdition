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
