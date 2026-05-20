using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Lims;

namespace SiemensS7Demo.App.Lims;

/// <summary>
/// HTTP+JSON LIMS client. Wire contract derived from the 202604 BackStageLims spike:
///   GET  /api/v1/tasks?status=&amp;deviceId=&amp;projectId=  →  [LimsTask, ...]
///   POST /api/v1/tasks/{id}/result  body { at, payloadJson }  →  204
/// </summary>
public sealed class HttpLimsClient : ILimsClient
{
    private readonly HttpClient _http;
    private readonly LimsClientOptions _opts;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public HttpLimsClient(HttpClient http, LimsClientOptions opts)
    {
        _http = http;
        _opts = opts;
        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_opts.BaseUrl))
            _http.BaseAddress = new Uri(_opts.BaseUrl);
        if (!string.IsNullOrEmpty(_opts.ApiToken))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiToken);
    }

    public async Task<IReadOnlyList<LimsTask>> ListTasksAsync(LimsFilter filter, CancellationToken ct)
    {
        var qs = new List<string>();
        if (filter.Status is not null) qs.Add($"status={Uri.EscapeDataString(filter.Status.ToString()!)}");
        if (!string.IsNullOrEmpty(filter.DeviceId)) qs.Add($"deviceId={Uri.EscapeDataString(filter.DeviceId)}");
        if (!string.IsNullOrEmpty(filter.ProjectId)) qs.Add($"projectId={Uri.EscapeDataString(filter.ProjectId)}");
        var url = "api/v1/tasks" + (qs.Count == 0 ? string.Empty : "?" + string.Join('&', qs));
        var list = await _http.GetFromJsonAsync<List<LimsTask>>(url, JsonOpts, ct).ConfigureAwait(false);
        return list ?? new List<LimsTask>();
    }

    public async Task UploadResultAsync(LimsTaskResult result, CancellationToken ct)
    {
        var body = new { at = result.At, payloadJson = result.PayloadJson };
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(
            $"api/v1/tasks/{Uri.EscapeDataString(result.TaskId)}/result", content, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }
}
