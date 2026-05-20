namespace SiemensS7Demo.Domain;

public sealed record DeviceWriteResult(bool Ok, string? ErrorCode, string? ErrorMessage)
{
    public static DeviceWriteResult Success() => new(true, null, null);
    public static DeviceWriteResult Failure(string code, string message) => new(false, code, message);
}
