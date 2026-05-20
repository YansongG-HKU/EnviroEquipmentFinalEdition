using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.App;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.App.Mqtt;
using SiemensS7Demo.Domain.Users;
using Xunit;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class SeedPasswordResolutionTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?> overrides)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(overrides).Build();
    }

    [Fact]
    public async Task EnvVarTakesPriority_OverAppsettingsPlaintext()
    {
        // Plaintext in appsettings says "wrong-pw"; env var says "right-pw". The env var must win.
        Environment.SetEnvironmentVariable("SEED_PASSWORD_AD_TEST1", "right-pw");
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Users:0:code"] = "AD-TEST1",
                ["Users:0:name"] = "Admin",
                ["Users:0:role"] = "Admin",
                ["Users:0:password"] = "wrong-pw",
            });
            var services = new ServiceCollection().AddLogging();
            services.AddPkg4Auth(config);
            await using var sp = services.BuildServiceProvider();

            var auth = sp.GetRequiredService<IAuthService>();
            var result = await auth.SignInAsync("AD-TEST1", "right-pw", Shift.ForLocalNow(), CancellationToken.None);
            result.Success.Should().BeTrue();

            var wrong = await auth.SignInAsync("AD-TEST1", "wrong-pw", Shift.ForLocalNow(), CancellationToken.None);
            wrong.Success.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SEED_PASSWORD_AD_TEST1", null);
        }
    }

    [Fact]
    public async Task PasswordCipherDecrypted_WhenEnvVarMissing()
    {
        // Encrypt a known password via InMemoryProtectedStore (the cross-platform fake) and
        // verify the seed loader uses it. Because the test process registers DPAPI on
        // Windows by default, we override the IProtectedStore in DI to keep this test
        // deterministic across platforms.
        var inMem = new InMemoryProtectedStore();
        var cipher = inMem.Protect("cipher-pw");

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Users:0:code"] = "AD-TEST2",
            ["Users:0:name"] = "Admin",
            ["Users:0:role"] = "Admin",
            ["Users:0:password"] = "",  // plaintext absent
            ["Users:0:passwordCipher"] = cipher,
        });
        var services = new ServiceCollection().AddLogging();
        services.AddPkg4Auth(config);
        // Override the auto-registered store with the in-memory fake so the test is
        // hermetic. (The first AddSingleton call registered DpapiProtectedStore on
        // Windows; we replace it before the user repository factory runs.)
        services.AddSingleton<IProtectedStore>(_ => inMem);
        await using var sp = services.BuildServiceProvider();

        var auth = sp.GetRequiredService<IAuthService>();
        var result = await auth.SignInAsync("AD-TEST2", "cipher-pw", Shift.ForLocalNow(), CancellationToken.None);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task AppsettingsRetainsNoPlaintextPasswords()
    {
        // Lock down the migration: the shipped appsettings.json must not contain any
        // non-empty plaintext password for seeded users. (Locating the file: the WPF
        // host's appsettings is the canonical seed source.)
        var appsettings = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "SiemensS7Demo.Wpf", "appsettings.json"));
        System.IO.File.Exists(appsettings).Should().BeTrue(
            $"expected appsettings.json at {appsettings}");

        var json = await System.IO.File.ReadAllTextAsync(appsettings);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var users = doc.RootElement.GetProperty("Users");
        foreach (var u in users.EnumerateArray())
        {
            if (u.TryGetProperty("password", out var p))
            {
                p.GetString().Should().BeNullOrEmpty(
                    "M4.6 plaintext-leak fix: production appsettings must not include a non-empty 'password' field.");
            }
            if (u.TryGetProperty("Password", out var p2))
            {
                p2.GetString().Should().BeNullOrEmpty(
                    "M4.6 plaintext-leak fix: production appsettings must not include a non-empty 'Password' field.");
            }
        }

        // MQTT broker password must also be empty (use env var or cipher).
        var mqtt = doc.RootElement.GetProperty("Mqtt");
        if (mqtt.TryGetProperty("Password", out var mp))
        {
            mp.GetString().Should().BeNullOrEmpty(
                "M4.6 plaintext-leak fix: production Mqtt.Password must be empty.");
        }
    }
}
