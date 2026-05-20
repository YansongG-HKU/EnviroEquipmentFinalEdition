using System.Collections.Generic;

namespace SiemensS7Demo.Domain.Programs;

/// <summary>
/// A named ordered sequence of <see cref="Segment"/>s. Editor and execution engine both
/// treat the program as immutable; mutation creates a new instance. <see cref="ProgramValidator"/>
/// is the single source of truth for shape validity.
/// </summary>
/// <remarks>
/// Lives under <c>SiemensS7Demo.Domain.Programs</c> to avoid colliding with the BCL
/// <c>System.Program</c> entry-point type.
/// </remarks>
public sealed class Program
{
    public required string Name { get; init; }
    public required IReadOnlyList<Segment> Segments { get; init; }
}
