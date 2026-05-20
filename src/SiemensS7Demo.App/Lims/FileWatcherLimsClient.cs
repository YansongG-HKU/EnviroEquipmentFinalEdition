using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Lims;

namespace SiemensS7Demo.App.Lims;

/// <summary>
/// File-mode LIMS client (the unrecoverable-protocol fallback documented in the spike notes).
/// LIMS exports tasks.json into a known directory; we read the latest snapshot and write per-task
/// results next to it as &lt;TaskId&gt;.result.json.
/// </summary>
public sealed class FileWatcherLimsClient : ILimsClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    private readonly LimsClientOptions _opts;

    public FileWatcherLimsClient(LimsClientOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.WatchDirectory))
            throw new ArgumentException("FileWatcherLimsClient requires WatchDirectory.", nameof(opts));
        _opts = opts;
    }

    public async Task<IReadOnlyList<LimsTask>> ListTasksAsync(LimsFilter filter, CancellationToken ct)
    {
        var path = Path.Combine(_opts.WatchDirectory!, "tasks.json");
        if (!File.Exists(path)) return Array.Empty<LimsTask>();
        await using var stream = File.OpenRead(path);
        var all = await JsonSerializer.DeserializeAsync<List<LimsTask>>(stream, Json, ct)
                  ?? new List<LimsTask>();
        IEnumerable<LimsTask> q = all;
        if (filter.Status is not null) q = q.Where(t => t.Status == filter.Status);
        if (!string.IsNullOrEmpty(filter.DeviceId)) q = q.Where(t => t.DeviceId == filter.DeviceId);
        if (!string.IsNullOrEmpty(filter.ProjectId)) q = q.Where(t => t.ProjectId == filter.ProjectId);
        return q.ToList();
    }

    public async Task UploadResultAsync(LimsTaskResult result, CancellationToken ct)
    {
        Directory.CreateDirectory(_opts.WatchDirectory!);
        var file = Path.Combine(_opts.WatchDirectory!, $"{result.TaskId}.result.json");
        var json = JsonSerializer.Serialize(result, Json);
        await File.WriteAllTextAsync(file, json, ct);
    }
}
