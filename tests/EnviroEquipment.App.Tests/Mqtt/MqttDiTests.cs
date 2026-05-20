using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.App;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.App.Mqtt;
using Xunit;

namespace EnviroEquipment.App.Tests.Mqtt;

[Trait("Category", "Pkg4")]
public class MqttDiTests
{
    [Fact]
    public void AddPkg4Mqtt_BindsOptionsFromConfig()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mqtt:Host"] = "broker.lan",
            ["Mqtt:Port"] = "8883",
            ["Mqtt:Username"] = "u",
            ["Mqtt:TopicPrefix"] = "envirogw/v1",
            ["Mqtt:ClientId"] = "c-1",
            ["Mqtt:UseTls"] = "true",
        }).Build();

        var services = new ServiceCollection().AddLogging();
        services.AddPkg4Mqtt(config);
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<MqttPublisherOptions>();
        opts.Host.Should().Be("broker.lan");
        opts.Port.Should().Be(8883);
        opts.Username.Should().Be("u");
        opts.TopicPrefix.Should().Be("envirogw/v1");
        opts.UseTls.Should().BeTrue();

        var pub = sp.GetRequiredService<IMqttPublisher>();
        pub.Should().BeOfType<MqttPublisher>();
        pub.IsConnected.Should().BeFalse("DI registration must not auto-connect.");
    }

    [Fact]
    public void AddPkg4Mqtt_UsesEnvVarOverAppsettingsPassword()
    {
        Environment.SetEnvironmentVariable("MQTT_PASSWORD", "from-env-2F8");
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mqtt:Host"] = "x",
                ["Mqtt:Password"] = "from-appsettings-DO-NOT-USE",
            }).Build();

            var services = new ServiceCollection().AddLogging();
            services.AddPkg4Mqtt(config);
            using var sp = services.BuildServiceProvider();

            var opts = sp.GetRequiredService<MqttPublisherOptions>();
            opts.Password.Should().Be("from-env-2F8");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MQTT_PASSWORD", null);
        }
    }

    [Fact]
    public void AddPkg4Mqtt_UsesPasswordCipher_WhenEnvVarMissing()
    {
        var store = new InMemoryProtectedStore();
        var cipher = store.Protect("from-cipher-9A1");

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mqtt:Host"] = "x",
            ["Mqtt:PasswordCipher"] = cipher,
            ["Mqtt:Password"] = "", // plaintext absent
        }).Build();

        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IProtectedStore>(_ => store);
        services.AddPkg4Mqtt(config);
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<MqttPublisherOptions>();
        opts.Password.Should().Be("from-cipher-9A1");
    }
}
