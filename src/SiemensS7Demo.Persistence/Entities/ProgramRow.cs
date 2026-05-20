using System;

namespace SiemensS7Demo.Persistence.Entities;

/// <summary>
/// One row per saved program. <see cref="JsonBlob"/> holds the full <c>SiemensS7Demo.Domain.Programs.Program</c>
/// serialized via <see cref="System.Text.Json.JsonSerializer"/>. Segments are always edited
/// as a unit so a single JSON column avoids the migration cost of a split table.
/// </summary>
public sealed class ProgramRow
{
    public required string Name { get; set; }
    public required string JsonBlob { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
