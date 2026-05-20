namespace SiemensS7Demo.Domain.Programs;

/// <summary>
/// Optional segment-level cycle directive. <see cref="JumpToCycle"/> sends execution back to
/// <see cref="JumpToCycle.TargetIndex"/> until <see cref="JumpToCycle.Count"/> iterations
/// have been completed; <see cref="EndCycle"/> immediately ends the program at this segment.
/// A segment with no <c>Cycle</c> simply advances to the next segment when its duration
/// elapses.
/// </summary>
public abstract record CycleAction
{
    public sealed record JumpToCycle(int TargetIndex, int Count) : CycleAction;
    public sealed record EndCycle() : CycleAction;
}
