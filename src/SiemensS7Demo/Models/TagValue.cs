namespace SiemensS7Demo.Models;

public sealed class TagValue
{
    public required string Name { get; init; }
    public required object Value { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required bool IsQualityGood { get; init; }
    public string? QualityMessage { get; init; }
}
