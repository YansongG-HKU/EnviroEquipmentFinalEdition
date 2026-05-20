using System;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Mqtt;

namespace SiemensS7Demo.App.Mqtt;

/// <summary>
/// Outbound MQTT publisher with cancellable connect + publish and a status notification
/// for observers (e.g. the WPF settings panel, telemetry samplers).
/// Implementations MUST:
///   - reconnect with exponential backoff on disconnect (publish triggers a reconnect)
///   - never echo the broker password through logs or exceptions
///   - dispose cleanly via <see cref="IAsyncDisposable.DisposeAsync"/>
/// </summary>
public interface IMqttPublisher : IAsyncDisposable
{
    /// <summary>
    /// True after a successful <see cref="ConnectAsync"/> while the broker session is alive.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Fires on connect, disconnect, and publish-complete. Subscribers should not block.
    /// </summary>
    event EventHandler<MqttPublisherStatus>? StatusChanged;

    /// <summary>Establish (or reuse) the broker session. Idempotent and safe to call repeatedly.</summary>
    Task ConnectAsync(CancellationToken ct);

    /// <summary>
    /// Publish <paramref name="payload"/> to <paramref name="topic"/>. If the client is
    /// disconnected, the publisher will attempt a single reconnect first.
    /// </summary>
    Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, MqttQos qos, CancellationToken ct);
}

/// <summary>
/// Status snapshot emitted by <see cref="IMqttPublisher.StatusChanged"/>. Intentionally
/// excludes any credential material.
/// </summary>
public sealed record MqttPublisherStatus(
    bool Connected,
    int Reconnects,
    long PublishCount,
    DateTimeOffset At,
    string? LastError);
