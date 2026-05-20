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

        // Path traversal guard: TaskId is server-supplied (LIMS), so we cannot trust it to be a
        // safe filename. Two layers of defense:
        //   1. Reject any TaskId containing a path separator or traversal segment up-front, so
        //      we never create unintended subdirectories under WatchDirectory (e.g. "foo/bar"
        //      would otherwise resolve to <root>/foo/bar.result.json — inside the root, but in
        //      a subdir the LIMS shouldn't be dictating).
        //   2. Even if the up-front check is bypassed (e.g. by a Unicode trick on some FS),
        //      verify the canonicalized path is strictly inside WatchDirectory before writing.
        if (string.IsNullOrWhiteSpace(result.TaskId)
            || result.TaskId.IndexOfAny(InvalidTaskIdChars) >= 0
            || result.TaskId.Contains(".."))
        {
            throw new ArgumentException(
                $"LIMS TaskId '{result.TaskId}' contains invalid path characters.", nameof(result));
        }

        var watchRoot = Path.GetFullPath(_opts.WatchDirectory!);
        if (!watchRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            watchRoot += Path.DirectorySeparatorChar;
        }

        var combined = Path.Combine(_opts.WatchDirectory!, $"{result.TaskId}.result.json");
        var fullPath = Path.GetFullPath(combined);
        if (!fullPath.StartsWith(watchRoot, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"LIMS TaskId '{result.TaskId}' escapes the watch directory.", nameof(result));
        }

        var json = JsonSerializer.Serialize(result, Json);
        await File.WriteAllTextAsync(fullPath, json, ct);
    }

    private static readonly char[] InvalidTaskIdChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' };
}
