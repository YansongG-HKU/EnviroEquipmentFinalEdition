namespace SiemensS7Demo.Domain;

/// <summary>
/// Runtime program / progress metadata attached to a <see cref="Device"/>.
/// In Pkg 1 this is populated from demo seed data by the session manager; in Pkg 3
/// the real program engine will own these fields. All members are optional so a device
/// with no active program (idle / offline) can carry an empty instance.
/// </summary>
public sealed record DeviceProgram(
    string? Name = null,
    int Seg = 0,
    int SegTotal = 0,
    int Cycle = 0,
    int CycleTotal = 0,
    int RemainSec = 0,
    double Progress = 0,
    string? AlarmCode = null,
    string? AlarmMessage = null,
    string? Note = null)
{
    public static readonly DeviceProgram Empty = new();
}
