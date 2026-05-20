using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.App.Mqtt;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddSiemensS7DemoApp(this IServiceCollection services)
    {
        services.AddSingleton<IRbacContext, AdminRbacContext>();
        services.AddSingleton(sp => BuildDefaultProjectConfig());
        services.AddSingleton<IDeviceSessionManager, DeviceSessionManager>();
        return services;
    }

    /// <summary>
    /// Wires the Pkg 4 authentication stack:
    ///   - <see cref="PasswordHasher"/> (Argon2id)
    ///   - <see cref="InMemoryUserRepository"/> seeded from <c>Users:</c> in configuration
    ///   - <see cref="AuthService"/> as the singleton <see cref="IAuthService"/>
    ///
    /// Plaintext passwords from <c>appsettings.json</c> are hashed once at startup and discarded;
    /// only the Argon2id-encoded hash survives in memory. When Pkg 3 M3.1 lands, swap
    /// <see cref="InMemoryUserRepository"/> for a SQLite-backed implementation — <see cref="AuthService"/>
    /// is unchanged.
    /// </summary>
    public static IServiceCollection AddPkg4Auth(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<PasswordHasher>();
        // Register a default IProtectedStore so seeded passwords supplied via PasswordCipher
        // can be decrypted at seed time. Windows hosts use DPAPI by default; non-Windows
        // hosts fall back to the in-memory store (which provides NO real protection — but
        // the seed loader also prefers env-var or plaintext password if those are present).
        services.AddSingleton<IProtectedStore>(_ =>
            OperatingSystem.IsWindows()
                ? (IProtectedStore)new DpapiProtectedStore()
                : new InMemoryProtectedStore());
        services.AddSingleton<IUserRepository>(sp =>
        {
            var hasher = sp.GetRequiredService<PasswordHasher>();
            var protect = sp.GetRequiredService<IProtectedStore>();
            var seedSection = config.GetSection("Users");
            var entries = seedSection.Get<List<UserSeedEntry>>() ?? new List<UserSeedEntry>();
            var users = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Code))
                .Select(e => new User(
                    Id: $"u-{e.Code}",
                    Name: string.IsNullOrEmpty(e.Name) ? e.Code : e.Name,
                    Role: e.Role,
                    Code: e.Code,
                    PasswordHash: hasher.Hash(ResolveSeedPassword(e, protect))));
            return new InMemoryUserRepository(users);
        });
        services.AddSingleton<IAuthService, AuthService>();
        return services;
    }

    /// <summary>
    /// Resolves the plaintext password for a seeded user, in priority order:
    ///   1. <c>SEED_PASSWORD_{CODE}</c> environment variable (CODE uppercased, '-' -> '_').
    ///      Recommended for CI and production — keeps secrets out of <c>appsettings.json</c>.
    ///   2. <see cref="UserSeedEntry.PasswordCipher"/> — DPAPI/base64 ciphertext, decrypted
    ///      via <see cref="IProtectedStore"/>. Suitable for production hosts where DPAPI
    ///      can scope to the service account.
    ///   3. <see cref="UserSeedEntry.Password"/> — plaintext fallback. Kept for the
    ///      headless smoke and unit tests; production configs MUST leave it empty.
    ///   4. Empty string — final fallback. The user record will exist but no valid
    ///      sign-in is possible until an admin provisions one of the above.
    ///
    /// Note: the resolved plaintext is hashed by Argon2id immediately by the caller and
    /// then discarded; the only durable state in memory is the hash.
    /// </summary>
    private static string ResolveSeedPassword(UserSeedEntry entry, IProtectedStore protect)
    {
        var envKey = "SEED_PASSWORD_" + entry.Code.ToUpperInvariant().Replace('-', '_');
        var envVal = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrEmpty(envVal)) return envVal;

        if (!string.IsNullOrEmpty(entry.PasswordCipher))
        {
            try { return protect.Unprotect(entry.PasswordCipher); }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Fall through to plaintext / empty. We deliberately do NOT log the cipher —
                // an admin will notice the missing user on sign-in.
            }
        }

        return entry.Password ?? string.Empty;
    }

    /// <summary>
    /// Wires the Pkg 4 MQTT publisher:
    ///   - <see cref="MqttPublisherOptions"/> bound from <c>Mqtt:</c> in configuration.
    ///   - Password resolution order: <c>MQTT_PASSWORD</c> env var first; if missing, the
    ///     <see cref="MqttPublisherOptions.PasswordCipher"/> field is decrypted via the
    ///     registered <see cref="IProtectedStore"/>; the <c>Password</c> field in
    ///     appsettings.json is a final fallback retained only for the headless smoke.
    ///   - <see cref="IMqttPublisher"/> registered as a singleton (one broker session per host).
    /// </summary>
    public static IServiceCollection AddPkg4Mqtt(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(sp =>
        {
            var opts = new MqttPublisherOptions();
            config.GetSection("Mqtt").Bind(opts);
            var protect = sp.GetService<IProtectedStore>();
            opts.Password = ResolveMqttPassword(opts, protect);
            return opts;
        });
        services.AddSingleton<IMqttPublisher>(sp =>
        {
            var opts = sp.GetRequiredService<MqttPublisherOptions>();
            var logger = sp.GetService<ILogger<MqttPublisher>>();
            return new MqttPublisher(opts, logger);
        });
        return services;
    }

    private static string? ResolveMqttPassword(MqttPublisherOptions opts, IProtectedStore? protect)
    {
        var env = Environment.GetEnvironmentVariable("MQTT_PASSWORD");
        if (!string.IsNullOrEmpty(env)) return env;
        if (!string.IsNullOrEmpty(opts.PasswordCipher) && protect is not null)
        {
            try { return protect.Unprotect(opts.PasswordCipher); }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Same policy as for users: silent fallback to plaintext / empty,
                // never log the cipher.
            }
        }
        return string.IsNullOrEmpty(opts.Password) ? null : opts.Password;
    }

    private static DeviceProvisioning Provision(
        string id, string bay, DeviceType type, DeviceSeed seed) =>
        new(id, bay, type, "127.0.0.1", 102, "Mock", 0, 1,
            "Pv", "Sv", "DB100.DBD10", "DB100.DBD14", UseInMemoryAdapter: true, Seed: seed);

    /// <summary>
    /// Default demo project — 9 InMemory devices in a 3x3 floor with mixed statuses, mirroring
    /// INITIAL_DEVICES in 温箱202605/mock-data.jsx so the overview reproduces the locked design.
    /// The WPF host resolves this; tests that need a different shape build their own ProjectConfig.
    /// </summary>
    public static ProjectConfig BuildDefaultProjectConfig()
    {
        var devs = new[]
        {
            // TH-01 · run · 高温高湿老化 (temp + humidity)
            Provision("TH-01", "A-01", DeviceType.Standard, new DeviceSeed(
                DeviceStatus.Run, Temp: 85.2, TempSet: 85.0, Humid: 65.0, HumidSet: 65.0,
                ProgName: "高温高湿老化 V3", Seg: 3, SegTotal: 8, Cycle: 2, CycleTotal: 5,
                RemainSec: 7235, Progress: 0.42)),
            // TH-02 · run · 低温存储 (temp only)
            Provision("TH-02", "A-02", DeviceType.Standard, new DeviceSeed(
                DeviceStatus.Run, Temp: -18.4, TempSet: -20.0,
                ProgName: "低温存储 72h", Seg: 1, SegTotal: 3, Cycle: 1, CycleTotal: 1,
                RemainSec: 41280, Progress: 0.18)),
            // TH-03 · alarm · 高温烘烤 — temperature over-limit
            Provision("TH-03", "A-03", DeviceType.Standard1500, new DeviceSeed(
                DeviceStatus.Alarm, Temp: 152.8, TempSet: 150.0,
                ProgName: "高温烘烤", Seg: 2, SegTotal: 4, Cycle: 3, CycleTotal: 10,
                RemainSec: 1820, Progress: 0.72,
                AlarmCode: "E-1108", AlarmMessage: "温度上限超限")),
            // LP-01 · run · 低压 (treat as temp; pressure UI is Pkg 3)
            Provision("LP-01", "B-01", DeviceType.LowPressure, new DeviceSeed(
                DeviceStatus.Run, Temp: 25.0, TempSet: 25.0,
                ProgName: "高空低压 DO-160", Seg: 4, SegTotal: 6, Cycle: 1, CycleTotal: 3,
                RemainSec: 14400, Progress: 0.55)),
            // TH-04 · pause · 常温稳定
            Provision("TH-04", "B-02", DeviceType.Standard, new DeviceSeed(
                DeviceStatus.Paused, Temp: 40.1, TempSet: 40.0, Humid: 50.2, HumidSet: 50.0,
                ProgName: "常温稳定", Seg: 2, SegTotal: 5, Cycle: 1, CycleTotal: 1,
                RemainSec: 3600, Progress: 0.35, Note: "人工暂停 · 张工")),
            // SH-01 · run · 温冲
            Provision("SH-01", "B-03", DeviceType.Shock, new DeviceSeed(
                DeviceStatus.Run, Temp: -40.2, TempSet: -40.0,
                ProgName: "两箱式温冲 100 循环", Seg: 1, SegTotal: 2, Cycle: 37, CycleTotal: 100,
                RemainSec: 92400, Progress: 0.37)),
            // TH-05 · scheduled · 预约
            Provision("TH-05", "C-01", DeviceType.Standard, new DeviceSeed(
                DeviceStatus.Scheduled, Temp: 23.5, Humid: 48.0,
                ProgName: "预约 · 电池快充循环", SegTotal: 6, CycleTotal: 1,
                Note: "明日 08:00")),
            // TH-06 · idle / ready
            Provision("TH-06", "C-02", DeviceType.Standard, new DeviceSeed(
                DeviceStatus.Idle, Temp: 23.1, Humid: 47.5)),
            // TH-07 · offline
            Provision("TH-07", "C-03", DeviceType.Standard1500, new DeviceSeed(
                DeviceStatus.Offline, Note: "昨日 18:42 · 通讯超时")),
        };
        return new ProjectConfig(devs);
    }
}
