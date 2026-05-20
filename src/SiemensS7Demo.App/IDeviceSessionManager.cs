using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.App;

public interface IDeviceSessionManager
{
    IObservable<Device> Devices { get; }
    Task ConnectAllAsync(CancellationToken ct);
    Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct);

    /// <summary>
    /// Returns the most recent <see cref="Device"/> snapshot for every device the manager is
    /// polling. Used by Pkg 4's <c>TelemetrySamplerService</c> to take a per-tick capture without
    /// having to subscribe to <see cref="Devices"/> and buffer events. Empty before the first poll.
    /// </summary>
    IReadOnlyList<Device> CurrentSnapshots();
}
