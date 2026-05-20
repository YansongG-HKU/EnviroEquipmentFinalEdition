using System;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.App;

public interface IDeviceSessionManager
{
    IObservable<Device> Devices { get; }
    Task ConnectAllAsync(CancellationToken ct);
    Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct);
}
