namespace SiemensS7Demo.Models;

public enum TagDataType
{
    Bool,
    Int16,
    DInt,
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

    public double ConvertRawToEngineering(double raw) => raw * Scale + Offset;
    public double ConvertEngineeringToRaw(double engineering) => (engineering - Offset) / Scale;
}
