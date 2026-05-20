using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Lims;

namespace SiemensS7Demo.App.Lims;

public interface ILimsClient
{
    Task<IReadOnlyList<LimsTask>> ListTasksAsync(LimsFilter filter, CancellationToken ct);
    Task UploadResultAsync(LimsTaskResult result, CancellationToken ct);
}
