namespace SiemensS7Demo.Domain;

public sealed class Device
{
    public required DeviceId Id { get; init; }
    public required string Bay { get; init; }
    public required DeviceType Type { get; init; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Offline;
    public Setpoints Setpoints { get; set; } = new(null, null, null);
    public ReadingSnapshot? LastReading { get; set; }

    /// <summary>Runtime program / progress metadata (segment, cycle, remaining, alarm). Never null.</summary>
    public DeviceProgram Program { get; set; } = DeviceProgram.Empty;
}
