namespace SiemensS7Demo.Models;

/// <summary>
/// Cross-tag metadata for one entry in a legacy XML <c>手动辅助功能</c> or
/// <c>程序辅助功能</c> group.
/// </summary>
/// <remarks>
/// <b>Pair mode</b>: <see cref="ControlTagName"/> + <see cref="StateTagName"/> are both set;
/// <see cref="ProgramBitOffset"/> is null.
/// <b>Bit-offset mode</b>: <see cref="ControlTagName"/> + <see cref="ProgramBitOffset"/> are
/// set; <see cref="StateTagName"/> is null.
///
/// Transitional placement: this class lives on <see cref="DeviceDefinition.Auxiliaries"/>
/// until Gap #9 introduces DeviceTemplate, at which point auxiliaries will migrate to the
/// template. The property name and shape will not change.
/// </remarks>
public sealed class AuxiliaryFunction
{
    /// <summary>GroupName from the legacy XML ParamType, e.g. "手动辅助功能".</summary>
    public required string Group { get; init; }

    /// <summary>Name of the tag that issues the control command (e.g. start/stop).</summary>
    public required string ControlTagName { get; init; }

    /// <summary>Name of the tag that reflects the running state (pair mode only).</summary>
    public string? StateTagName { get; init; }

    /// <summary>Bit offset within a status word tag (bit-offset mode only).</summary>
    public int? ProgramBitOffset { get; init; }
}
