using System;

namespace SiemensS7Demo.App.Mqtt;

/// <summary>
/// Broker-connection options for <see cref="MqttPublisher"/>. Bound from
/// <c>appsettings.json</c> "Mqtt" section. Password is set in-process at startup from either
/// an env var (<c>MQTT_PASSWORD</c>) or by decrypting <see cref="PasswordCipher"/> via
/// <see cref="SiemensS7Demo.App.Auth.IProtectedStore"/>; the field is excluded from logging
/// by the <see cref="SiemensS7Demo.App.Logging.LogScrubber"/> guard.
/// </summary>
public sealed class MqttPublisherOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string? Username { get; set; }

    /// <summary>
    /// Plaintext password (in-memory only). Treat as secret. Never log, never serialise.
    /// Populated at startup from <see cref="PasswordCipher"/> or env var; the appsettings
    /// field is empty by default after the M4.6 plaintext-leak fix.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Optional DPAPI-protected (base64) ciphertext of the broker password. The composition
    /// root decrypts this through <see cref="SiemensS7Demo.App.Auth.IProtectedStore"/> and
    /// places the plaintext in <see cref="Password"/>; the cipher itself is safe to commit
    /// alongside <c>appsettings.json</c> because it's machine-scoped.
    /// </summary>
    public string? PasswordCipher { get; set; }

    public string TopicPrefix { get; set; } = "envirogw/v1";
    public string ClientId { get; set; } = "envirogw-client";

    /// <summary>Enable TLS to the broker. When true, MqttPublisher uses an SSL connection.</summary>
    public bool UseTls { get; set; }

    /// <summary>Backoff base for reconnect attempts. Doubles each failure up to <see cref="ReconnectMaxBackoff"/>.</summary>
    public TimeSpan ReconnectInitialBackoff { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan ReconnectMaxBackoff { get; set; } = TimeSpan.FromSeconds(30);
}
