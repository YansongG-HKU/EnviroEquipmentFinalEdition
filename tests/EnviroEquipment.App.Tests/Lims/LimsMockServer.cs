using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Lims;

namespace EnviroEquipment.App.Tests.Lims;

/// <summary>
/// Tiny self-contained HTTP server (HttpListener-based) that serves the agreed LIMS contract.
/// Not Kestrel-based — pulling AspNetCore into App.Tests would be overkill for &lt;100 LOC mock.
/// Bound to a free ephemeral port so parallel test runs don't collide.
/// </summary>
public sealed class LimsMockServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public Uri BaseUri { get; }
    public List<LimsTaskResult> ReceivedResults { get; } = new();
    public List<LimsTask> Tasks { get; }

    public static LimsMockServer Start(IEnumerable<LimsTask>? seed = null)
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        return new LimsMockServer(prefix, seed);
    }

    private LimsMockServer(string prefix, IEnumerable<LimsTask>? seed)
    {
        BaseUri = new Uri(prefix);
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();
        Tasks = seed is null ? new List<LimsTask>() : new List<LimsTask>(seed);
        _loop = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }

            try { await HandleAsync(ctx).ConfigureAwait(false); }
            catch { try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* shutdown race */ } }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url!.AbsolutePath;
        if (req.HttpMethod == "GET" && path == "/api/v1/tasks")
        {
            var status = req.QueryString["status"];
            var deviceId = req.QueryString["deviceId"];
            var projectId = req.QueryString["projectId"];
            IEnumerable<LimsTask> data = Tasks;
            if (!string.IsNullOrEmpty(status))
                data = data.Where(t => string.Equals(t.Status.ToString(), status, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(deviceId)) data = data.Where(t => t.DeviceId == deviceId);
            if (!string.IsNullOrEmpty(projectId)) data = data.Where(t => t.ProjectId == projectId);
            var payload = JsonSerializer.Serialize(data, Json);
            await WriteAsync(ctx.Response, 200, "application/json", payload);
            return;
        }
        if (req.HttpMethod == "POST" && path.StartsWith("/api/v1/tasks/") && path.EndsWith("/result"))
        {
            var id = path.Substring("/api/v1/tasks/".Length);
            id = id.Substring(0, id.Length - "/result".Length);
            using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
            var body = await sr.ReadToEndAsync().ConfigureAwait(false);
            var doc = JsonDocument.Parse(body);
            var at = doc.RootElement.GetProperty("at").GetDateTimeOffset();
            var payloadJson = doc.RootElement.GetProperty("payloadJson").GetString() ?? string.Empty;
            ReceivedResults.Add(new LimsTaskResult(Uri.UnescapeDataString(id), at, payloadJson));
            await WriteAsync(ctx.Response, 204, "application/json", string.Empty);
            return;
        }
        await WriteAsync(ctx.Response, 404, "text/plain", "not found");
    }

    private static async Task WriteAsync(HttpListenerResponse resp, int code, string contentType, string body)
    {
        resp.StatusCode = code;
        resp.ContentType = contentType;
        var bytes = Encoding.UTF8.GetBytes(body);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        resp.OutputStream.Close();
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* already stopped */ }
        try { await _loop.ConfigureAwait(false); } catch { /* expected after Stop */ }
        _listener.Close();
        _cts.Dispose();
    }
}
