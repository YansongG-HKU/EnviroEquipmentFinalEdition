using System;

namespace SiemensS7Demo.Domain.Lims;

public enum LimsTaskStatus { Todo, Running, Done, Cancelled }

public sealed record LimsTask(
    string Id,
    string DeviceId,
    string ProjectId,
    string Name,
    DateTimeOffset PlanStart,
    DateTimeOffset PlanEnd,
    DateTimeOffset? ActualStart,
    DateTimeOffset? ActualEnd,
    LimsTaskStatus Status);

public sealed record LimsFilter(string? DeviceId, string? ProjectId, LimsTaskStatus? Status);

public sealed record LimsTaskResult(string TaskId, DateTimeOffset At, string PayloadJson);
