using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Services;

public sealed class PlcWriteService
{
    private readonly IPlcClient _plcClient;

    public PlcWriteService(IPlcClient plcClient)
    {
        _plcClient = plcClient;
    }

    public Task WriteAsync(TagDefinition writeTag, object value, CancellationToken cancellationToken)
        => _plcClient.WriteTagAsync(writeTag, value, cancellationToken);
}
