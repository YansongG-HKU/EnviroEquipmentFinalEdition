using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddSiemensS7DemoApp(this IServiceCollection services)
    {
        // RBAC is wired to whatever IAuthService is registered (Pkg 4's AddPkg4Auth). If no
        // IAuthService is present (e.g. legacy headless tests), the resolver falls back to
        // AdminRbacContext so existing Pkg 1/2 callsites keep working.
        services.AddSingleton<IRbacContext>(sp =>
        {
            var auth = sp.GetService<IAuthService>();
            return auth is null
                ? new AdminRbacContext()
                : new AuthBackedRbacContext(auth);
        });
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
        services.AddSingleton<IUserRepository>(sp =>
        {
            var hasher = sp.GetRequiredService<PasswordHasher>();
            var seedSection = config.GetSection("Users");
            var entries = seedSection.Get<List<UserSeedEntry>>() ?? new List<UserSeedEntry>();
            var users = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Code))
                .Select(e => new User(
                    Id: $"u-{e.Code}",
                    Name: string.IsNullOrEmpty(e.Name) ? e.Code : e.Name,
                    Role: e.Role,
                    Code: e.Code,
                    PasswordHash: hasher.Hash(e.Password ?? string.Empty)));
            return new InMemoryUserRepository(users);
        });
        services.AddSingleton<IAuthService, AuthService>();
        return services;
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
