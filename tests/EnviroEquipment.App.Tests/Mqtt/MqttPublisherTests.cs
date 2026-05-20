using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Server;
using SiemensS7Demo.App.Mqtt;
using SiemensS7Demo.Domain.Mqtt;
using Xunit;

namespace EnviroEquipment.App.Tests.Mqtt;

[Trait("Category", "Pkg4")]
public class MqttPublisherTests
{
    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<MqttServer> StartBrokerAsync(int port)
    {
        var factory = new MqttFactory();
        var options = factory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(port)
            .Build();
        var server = factory.CreateMqttServer(options);
        await server.StartAsync();
        return server;
    }

    [Fact]
    public async Task Publish_DeliversPayloadToSubscribedConsumer()
    {
        var port = GetFreePort();
        using var broker = await StartBrokerAsync(port);

        var received = new BlockingCollection<(string Topic, string Payload)>();
        var subFactory = new MqttFactory();
        using var subscriber = subFactory.CreateMqttClient();
        await subscriber.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer("127.0.0.1", port).WithClientId("sub").Build());
        subscriber.ApplicationMessageReceivedAsync += e =>
        {
            received.Add((e.ApplicationMessage.Topic,
                Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment)));
            return Task.CompletedTask;
        };
        await subscriber.SubscribeAsync("envirogw/v1/#");

        var pub = new MqttPublisher(new MqttPublisherOptions
        {
            Host = "127.0.0.1", Port = port, TopicPrefix = "envirogw/v1", ClientId = "pub"
        }, NullLogger<MqttPublisher>.Instance);
        await pub.ConnectAsync(CancellationToken.None);

        await pub.PublishAsync("envirogw/v1/telemetry/TH-01",
            Encoding.UTF8.GetBytes("{\"pv\":85.2}"),
            MqttQos.AtLeastOnce, CancellationToken.None);

        received.TryTake(out var msg, TimeSpan.FromSeconds(3)).Should().BeTrue();
        msg.Topic.Should().Be("envirogw/v1/telemetry/TH-01");
        msg.Payload.Should().Contain("85.2");

        await pub.DisposeAsync();
        await subscriber.DisconnectAsync();
    }

    [Fact]
    public async Task Connect_FailsFast_WithBackoff_WhenBrokerUnreachable()
    {
        // Pick a free port but never start a broker on it. The publisher should retry
        // (4 attempts in the impl) and throw — confirm it doesn't hang and surfaces an
        // InvalidOperationException, never echoing credentials.
        var port = GetFreePort();
        var pub = new MqttPublisher(new MqttPublisherOptions
        {
            Host = "127.0.0.1", Port = port, ClientId = "pub-fail",
            Username = "u", Password = "DO-NOT-LEAK-2EF",
            ReconnectInitialBackoff = TimeSpan.FromMilliseconds(20),
            ReconnectMaxBackoff = TimeSpan.FromMilliseconds(100),
        }, NullLogger<MqttPublisher>.Instance);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var act = async () => await pub.ConnectAsync(CancellationToken.None);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        sw.Stop();

        // 4 connect attempts, each bounded by the TCP dial timeout of MQTTnet's default
        // client (~2s) plus our small backoff. Give generous headroom for CI variance —
        // the assertion exists to catch infinite-hang regressions, not to enforce a tight SLA.
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
        ex.Which.Message.Should().NotContain("DO-NOT-LEAK-2EF");
        await pub.DisposeAsync();
    }

    [Fact]
    public async Task Publish_ReconnectsAndRetries_AfterBrokerRestart()
    {
        // Demonstrates the single-retry behavior: stop the broker, expect a transient
        // publish failure to trigger reconnect, then re-publish successfully.
        var port = GetFreePort();
        var broker = await StartBrokerAsync(port);

        var pub = new MqttPublisher(new MqttPublisherOptions
        {
            Host = "127.0.0.1", Port = port, ClientId = "pub-rc",
            ReconnectInitialBackoff = TimeSpan.FromMilliseconds(20),
            ReconnectMaxBackoff = TimeSpan.FromMilliseconds(100),
        }, NullLogger<MqttPublisher>.Instance);

        await pub.ConnectAsync(CancellationToken.None);
        pub.IsConnected.Should().BeTrue();
        var reconnectsBefore = 0;
        pub.StatusChanged += (_, s) => reconnectsBefore = s.Reconnects;

        // First publish: server up — should succeed.
        await pub.PublishAsync("envirogw/v1/x", Encoding.UTF8.GetBytes("{}"),
            MqttQos.AtMostOnce, CancellationToken.None);

        await broker.StopAsync();
        broker.Dispose();

        // Start a NEW broker on the same port (the old one is gone).
        using var newBroker = await StartBrokerAsync(port);

        // The first re-publish may fail transiently; we accept either an exception or
        // success after the internal single-retry. The point is that .ConnectAsync
        // after the failure increments reconnect count and brings us back online.
        try
        {
            await pub.PublishAsync("envirogw/v1/x", Encoding.UTF8.GetBytes("{}"),
                MqttQos.AtMostOnce, CancellationToken.None);
        }
        catch
        {
            // After failure, an explicit reconnect must succeed against the new broker.
            await pub.ConnectAsync(CancellationToken.None);
            await pub.PublishAsync("envirogw/v1/x", Encoding.UTF8.GetBytes("{}"),
                MqttQos.AtMostOnce, CancellationToken.None);
        }

        pub.IsConnected.Should().BeTrue();
        await pub.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_DisconnectsCleanly_AndIsIdempotent()
    {
        var port = GetFreePort();
        using var broker = await StartBrokerAsync(port);

        var pub = new MqttPublisher(new MqttPublisherOptions
        {
            Host = "127.0.0.1", Port = port, ClientId = "pub-dispose"
        }, NullLogger<MqttPublisher>.Instance);
        await pub.ConnectAsync(CancellationToken.None);

        await pub.DisposeAsync();
        await pub.DisposeAsync(); // second call must not throw

        var act = async () => await pub.PublishAsync("envirogw/v1/x",
            new byte[] { 0 }, MqttQos.AtMostOnce, CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }
}
