using System;
using System.Collections.Generic;

namespace SiemensS7Demo.Domain.Programs;

/// <summary>
/// Pure-function validator. Returns a list of human-readable error strings; an empty list
/// means the program can be saved. Validator does NOT throw on invalid programs — UI binds
/// to the list and shows it in a panel. Only a <c>null</c> program raises an
/// <see cref="ArgumentNullException"/> because that is a programmer error, not a user error.
/// </summary>
public static class ProgramValidator
{
    public const int MaxSegments = 8;

    public static IReadOnlyList<string> Validate(Program program)
    {
        if (program is null) throw new ArgumentNullException(nameof(program));
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(program.Name))
            errors.Add("Program name must be non-empty.");

        if (program.Segments is null || program.Segments.Count == 0)
        {
            errors.Add("Program must contain at least one segment.");
            return errors;
        }

        if (program.Segments.Count > MaxSegments)
            errors.Add($"Program has {program.Segments.Count} segments; max is {MaxSegments}.");

        for (var i = 0; i < program.Segments.Count; i++)
        {
            var s = program.Segments[i];
            if (s.Index != i)
                errors.Add($"Segment at position {i} has Index={s.Index}; expected {i}.");
            if (s.Duration <= TimeSpan.Zero)
                errors.Add($"Segment {i} duration must be greater than zero.");
            if (s.Cycle is CycleAction.JumpToCycle jmp)
            {
                if (jmp.TargetIndex < 0 || jmp.TargetIndex >= program.Segments.Count)
                    errors.Add($"Segment {i} JMP target {jmp.TargetIndex} is out of range [0, {program.Segments.Count - 1}].");
                if (jmp.TargetIndex >= i)
                    errors.Add($"Segment {i} JMP target {jmp.TargetIndex} must be earlier than the current segment.");
                if (jmp.Count < 1)
                    errors.Add($"Segment {i} JMP count must be at least 1 (got {jmp.Count}).");
            }
        }
        return errors;
    }
}
