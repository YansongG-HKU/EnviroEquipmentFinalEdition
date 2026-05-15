namespace SiemensS7Demo.Models;

/// <summary>
/// Controls how <see cref="TagDefinition.Scale"/> is applied when converting raw PLC values to engineering units.
/// </summary>
/// <remarks>
/// <see cref="Multiplier"/> is the default (modern JSON/XML): engineering = raw * Scale + Offset.
/// <see cref="Divisor"/> is used by legacy addressConfig.xml files: engineering = raw / Scale + Offset.
/// The legacy loader normalizes scale=0 to Scale=1, ScaleMode=Multiplier (identity transform).
/// </remarks>
public enum ScaleMode
{
    /// <summary>engineering = raw * Scale + Offset (default)</summary>
    Multiplier,

    /// <summary>engineering = raw / Scale + Offset (legacy XML)</summary>
    Divisor
}
