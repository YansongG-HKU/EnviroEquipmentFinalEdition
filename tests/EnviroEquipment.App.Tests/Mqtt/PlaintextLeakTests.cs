using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Server;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.App.Mqtt;
using SiemensS7Demo.Domain.Mqtt;
using Xunit;
using Xunit.Abstractions;

namespace EnviroEquipment.App.Tests.Mqtt;

[Trait("Category", "Pkg4")]
public class PlaintextLeakTests
{
    // A unique, easy-to-grep marker. If any logging path leaks the broker password, this
    // string will appear in stdout, stderr, or the captured log file.
    private const string SuperSecret = "SECRET-PASS-DO-NOT-LOG-9C7A";
    private readonly ITestOutputHelper _output;

    public PlaintextLeakTests(ITestOutputHelper output) { _output = output; }

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }

    [Fact]
    public async Task PublishWithCredentials_NeverLeaksPlaintextPasswordToLogsOrFiles()
    {
        var port = GetFreePort();
        var factory = new MqttFactory();
        using var server = factory.CreateMqttServer(factory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(port).Build());
        await server.StartAsync();

        var stdoutCapture = new StringWriter();
        var stderrCapture = new StringWriter();
        var origOut = Console.Out;
        var origErr = Console.Error;
        Console.SetOut(stdoutCapture);
        Console.SetError(stderrCapture);

        var logFile = Path.Combine(Path.GetTempPath(), $"mqtt-log-{Guid.NewGuid()}.txt");
        try
        {
            using var loggerFactory = LoggerFactory.Create(b =>
                b.AddProvider(new TestFileLoggerProvider(logFile)));
            var logger = loggerFactory.CreateLogger<PlaintextLeakTests>();
            logger.LogInformation("Connecting to MQTT host {Host}:{Port}.", "127.0.0.1", port);

            // Round-trip the secret through the protected store (settings UI usage).
            var protector = new InMemoryProtectedStore();
            var encrypted = protector.Protect(SuperSecret);

            var pubLogger = loggerFactory.CreateLogger<MqttPublisher>();
            var pub = new MqttPublisher(new MqttPublisherOptions
            {
                Host = "127.0.0.1", Port = port,
                Username = "u",
                Password = protector.Unprotect(encrypted),
                TopicPrefix = "envirogw/v1",
                ClientId = "pub"
            }, pubLogger);
            await pub.ConnectAsync(CancellationToken.None);
            await pub.PublishAsync("envirogw/v1/telemetry/X",
                Encoding.UTF8.GetBytes("{}"),
                MqttQos.AtMostOnce, CancellationToken.None);
            await pub.DisposeAsync();

            Console.Out.Flush();
            Console.Error.Flush();
            var stdoutText = stdoutCapture.ToString();
            var stderrText = stderrCapture.ToString();
            var fileText = File.Exists(logFile) ? File.ReadAllText(logFile) : "";

            stdoutText.Should().NotContain(SuperSecret, "MQTT password must not leak to stdout.");
            stderrText.Should().NotContain(SuperSecret, "MQTT password must not leak to stderr.");
            fileText.Should().NotContain(SuperSecret, "MQTT password must not leak to log files.");
            encrypted.Should().NotContain(SuperSecret, "Protected-store output must not contain plaintext.");
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
            if (File.Exists(logFile)) File.Delete(logFile);
            await server.StopAsync();
        }
    }

    private sealed class TestFileLoggerProvider : ILoggerProvider
    {
        private readonly string _path;
        public TestFileLoggerProvider(string path) { _path = path; }
        public ILogger CreateLogger(string categoryName) => new FileLogger(_path);
        public void Dispose() { }

        private sealed class FileLogger : ILogger
        {
            private readonly string _path;
            public FileLogger(string path) { _path = path; }
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                File.AppendAllText(_path, formatter(state, exception) + Environment.NewLine);
            }
            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}
