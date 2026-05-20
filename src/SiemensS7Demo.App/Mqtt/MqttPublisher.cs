using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using SiemensS7Demo.Domain.Mqtt;

namespace SiemensS7Demo.App.Mqtt;

/// <summary>
/// MQTTnet-backed publisher with single-attempt reconnect on a failed publish and
/// exponential-backoff retry on initial connect. Credentials live in
/// <see cref="MqttPublisherOptions"/> and are never logged.
/// </summary>
public sealed class MqttPublisher : IMqttPublisher, IDisposable
{
    private readonly MqttPublisherOptions _opts;
    private readonly IMqttClient _client;
    private readonly ILogger<MqttPublisher>? _log;
    private readonly object _gate = new();
    private int _reconnects;
    private long _publishCount;
    private bool _disposed;

    public MqttPublisher(MqttPublisherOptions opts, ILogger<MqttPublisher>? log = null)
    {
        _opts = opts ?? throw new ArgumentNullException(nameof(opts));
        _log = log;
        _client = new MqttFactory().CreateMqttClient();
    }

    public bool IsConnected => _client.IsConnected;

    public event EventHandler<MqttPublisherStatus>? StatusChanged;

    public async Task ConnectAsync(CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_client.IsConnected) return;

        var backoff = _opts.ReconnectInitialBackoff;
        Exception? lastError = null;
        // Single connect call still attempts a small retry sequence so transient broker
        // restarts during integration tests don't fail the publisher outright. We cap at
        // 4 attempts so the call still returns promptly when the broker really is gone.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var builder = new MqttClientOptionsBuilder()
                    .WithTcpServer(_opts.Host, _opts.Port)
                    .WithClientId(_opts.ClientId)
                    .WithCleanSession(true);
                if (!string.IsNullOrEmpty(_opts.Username))
                {
                    builder = builder.WithCredentials(_opts.Username, _opts.Password ?? string.Empty);
                }
                if (_opts.UseTls)
                {
                    builder = builder.WithTlsOptions(o => o.UseTls());
                }

                await _client.ConnectAsync(builder.Build(), ct).ConfigureAwait(false);
                lastError = null;
                if (attempt > 0)
                {
                    Interlocked.Increment(ref _reconnects);
                }
                Emit(connected: true, error: null);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                // Log only the host:port and exception type — never any credential material.
                _log?.LogWarning("MQTT connect attempt {Attempt} to {Host}:{Port} failed: {Error}",
                    attempt + 1, _opts.Host, _opts.Port, ex.GetType().Name);
                try { await Task.Delay(backoff, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _opts.ReconnectMaxBackoff.Ticks));
            }
        }

        Emit(connected: false, error: lastError?.GetType().Name);
        throw new InvalidOperationException(
            $"MQTT publisher failed to connect to {_opts.Host}:{_opts.Port} after retries.");
    }

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, MqttQos qos, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(topic)) throw new ArgumentException("Topic required.", nameof(topic));
        if (!_client.IsConnected)
        {
            await ConnectAsync(ct).ConfigureAwait(false);
        }

        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload.ToArray())
            .WithQualityOfServiceLevel((MQTTnet.Protocol.MqttQualityOfServiceLevel)(int)qos)
            .Build();

        try
        {
            await _client.PublishAsync(msg, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _publishCount);
            Emit(connected: true, error: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Single reconnect-and-retry on transient publish failure.
            _log?.LogInformation("MQTT publish failed; reconnecting once. ({Error})", ex.GetType().Name);
            try
            {
                try { await _client.DisconnectAsync().ConfigureAwait(false); } catch { /* best-effort */ }
                await ConnectAsync(ct).ConfigureAwait(false);
                await _client.PublishAsync(msg, ct).ConfigureAwait(false);
                Interlocked.Increment(ref _publishCount);
                Emit(connected: true, error: null);
            }
            catch (Exception retryEx)
            {
                Emit(connected: _client.IsConnected, error: retryEx.GetType().Name);
                throw;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // best-effort disconnect during shutdown.
        }
        _client.Dispose();
    }

    /// <summary>
    /// Synchronous dispose for the DI container, which only knows how to call
    /// <see cref="IDisposable.Dispose"/> when resolving singletons via the
    /// non-async <c>ServiceProvider.Dispose</c> path. Best-effort: skips the
    /// graceful broker disconnect (the OS will tear down the TCP socket on
    /// process exit).
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _client.Dispose(); } catch { /* shutdown best-effort */ }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MqttPublisher));
    }

    private void Emit(bool connected, string? error)
    {
        var snapshot = new MqttPublisherStatus(
            Connected: connected,
            Reconnects: _reconnects,
            PublishCount: Interlocked.Read(ref _publishCount),
            At: DateTimeOffset.UtcNow,
            LastError: error);
        try
        {
            StatusChanged?.Invoke(this, snapshot);
        }
        catch
        {
            // observer faults must never bring down the publisher.
        }
    }
}
