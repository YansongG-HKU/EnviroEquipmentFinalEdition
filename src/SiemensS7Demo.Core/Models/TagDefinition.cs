namespace SiemensS7Demo.Models;

public enum TagDataType
{
    Bool,
    Int16,
    UInt16,
    DInt,
    UInt32,
    Real
}

public enum TagAccess
{
    Read,
    Write,
    ReadWrite
}

public sealed class TagDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Group { get; init; }
    public required string Address { get; init; }
    public required TagDataType DataType { get; init; }
    public required string Unit { get; init; }
    public double Scale { get; init; } = 1.0;
    public double Offset { get; init; } = 0.0;
    public TagAccess Access { get; init; } = TagAccess.Read;
    public bool SafeWrite { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public ScaleMode ScaleMode { get; init; } = ScaleMode.Multiplier;
    public IReadOnlyList<TagOption> Options { get; init; } = System.Array.Empty<TagOption>();
    public IReadOnlyList<BitDerivation> BitDerivations { get; init; } = System.Array.Empty<BitDerivation>();

    public double ConvertRawToEngineering(double raw)
        => ScaleMode == ScaleMode.Divisor
            ? raw / Scale + Offset
            : raw * Scale + Offset;

    public double ConvertEngineeringToRaw(double engineering)
        => ScaleMode == ScaleMode.Divisor
            ? (engineering - Offset) * Scale
            : (engineering - Offset) / Scale;

    public bool TryGetOptionLabel(long rawValue, out string? label)
    {
        foreach (var option in Options)
        {
            if (option.Value == rawValue)
            {
                label = option.Label;
                return true;
            }
        }

        label = null;
        return false;
    }
}
