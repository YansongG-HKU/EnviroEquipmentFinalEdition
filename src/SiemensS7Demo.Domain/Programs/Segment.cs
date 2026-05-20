using System;

namespace SiemensS7Demo.Domain.Programs;

/// <summary>
/// A single program step. <see cref="DigitalOutputs"/> is a fixed-width vector (typically 4
/// channels) representing the auxiliary output state the device should hold during this
/// segment. <see cref="Cycle"/>, when non-null, fires at the end of the segment and either
/// jumps back to an earlier segment (<see cref="CycleAction.JumpToCycle"/>) or ends the
/// program (<see cref="CycleAction.EndCycle"/>).
/// </summary>
public sealed record Segment(
    int Index,
    double TempSetpoint,
    double? HumidSetpoint,
    TimeSpan Duration,
    SegmentMode Mode,
    CycleAction? Cycle,
    bool[] DigitalOutputs,
    string? Note);
