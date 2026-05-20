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
