# Phase 2 — Package 4: Login + RBAC + LIMS + MQTT + FTP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Package 4 of Phase 2: authenticated, role-gated WPF client with LIMS task-board integration, MQTT telemetry uplink, and FTP backup uploader. The login flow reproduces the 202605 three-step UX (account → password → shift). RBAC tags every command surface from Pkg 1/2/3 via `[RequiresRole]`. LIMS, MQTT, and FTP each ship behind an interface with an embedded test double + a docker-compose acceptance harness.

**Architecture:** Domain types (`User`, `Role`, `Shift`, `LimsTask`, `LimsTaskResult`) live in `SiemensS7Demo.Domain`. Concrete services live in `SiemensS7Demo.App`: `AuthService` (Argon2id verify), `LimsClient` (HTTP+JSON over `HttpClient`), `MqttPublisher` (MQTTnet), `FtpUploader` (FluentFTP), `DpapiProtectedStore` (Windows-only, wrapped by `IProtectedStore`). Login view + LIMS view + MQTT settings view land in `SiemensS7Demo.Wpf` as `CommunityToolkit.Mvvm` ObservableObject view-models. RBAC binds at command construction time: `RelayCommand.CanExecute` consults `IAuthService.Current.Role` against any `[RequiresRoleAttribute]` declared on the underlying method. Persistence builds on Pkg 3 M3.1 (`EnviroDbContext`) with a new migration that adds the `Users` table.

**Tech Stack:** C# .NET 8 (`net8.0` for App/Tests, `net8.0-windows` for Wpf), `Konscious.Security.Cryptography.Argon2` 1.3.0, `MQTTnet` 4.3.x, `FluentFTP` 49.0.x, `System.Security.Cryptography.ProtectedData` 8.0.x (DPAPI; Windows only behind `IProtectedStore`), `Microsoft.AspNetCore.WebUtilities` 8.0.x (embedded mock HTTP server), `CommunityToolkit.Mvvm` 8.x, `xunit`, `FluentAssertions`, `System.Text.Json`. Docker images: `eclipse-mosquitto:2.0` and `delfer/alpine-ftp-server:latest` for the acceptance harness.

**Scope guard:**
- DO: ship the 8 milestones M4.1–M4.8 below.
- DO NOT: re-implement device commands from Pkg 1 (we only annotate them with `[RequiresRole]`).
- DO NOT: change `DeviceSessionManager` semantics.
- DO NOT: touch existing alarm or program logic — those are Pkg 2 / Pkg 3.
- DO NOT: implement TLS for MQTT or FTPS for FTP in this PR; plain TCP only. (Tracked as a Phase 3 hardening item.)
- DO NOT: ship a Linux/macOS DPAPI alternative — `DpapiProtectedStore` throws on non-Windows; tests use `InMemoryProtectedStore` so they stay cross-platform.

**Branch:** `feat/phase2-pkg4-login-lims-mqtt-ftp`
**Worktree:** `.claude/worktrees/phase2-pkg4-login-lims-mqtt-ftp`
**Base:** `main` after Pkg 1 M1.3 (`DeviceSessionManager`) and Pkg 3 M3.1 (`EnviroDbContext` initial migration) have landed.

**Depends-on:**
- Pkg 1 M1.3 — provides `IDeviceSessionManager` for telemetry sampling in M4.6.
- Pkg 3 M3.1 — provides `EnviroDbContext` and the migration baseline that M4.1's `AddUsersTable` migration stacks onto.

If either dependency has not landed, hold off on this plan. Do not stub-replace either dependency.

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `src/SiemensS7Demo.Domain/Users/User.cs` | `record User(Id, Name, Role, Code, PasswordHash)` |
| Create | `src/SiemensS7Demo.Domain/Users/Role.cs` | `enum Role { Operator, Engineer, Admin }` |
| Create | `src/SiemensS7Demo.Domain/Users/Shift.cs` | `record Shift(Code, Name, Date)` + helper `ForLocalNow()` |
| Create | `src/SiemensS7Demo.Domain/Users/AuthResult.cs` | `record AuthResult(Success, User?, ErrorMessage?)` |
| Create | `src/SiemensS7Demo.Domain/Lims/LimsTask.cs` | `record LimsTask(...)`, `enum LimsTaskStatus`, `record LimsFilter`, `record LimsTaskResult` |
| Create | `src/SiemensS7Demo.Domain/Mqtt/MqttQos.cs` | `enum MqttQos { AtMostOnce, AtLeastOnce, ExactlyOnce }` |
| Create | `src/SiemensS7Demo.App/Auth/IAuthService.cs` | Interface — sign in / out / current |
| Create | `src/SiemensS7Demo.App/Auth/AuthService.cs` | Argon2id-backed implementation |
| Create | `src/SiemensS7Demo.App/Auth/RequiresRoleAttribute.cs` | `[RequiresRole(Role minimum)]` |
| Create | `src/SiemensS7Demo.App/Auth/RbacGuard.cs` | Static helpers: `IsAllowed(user, methodInfo)`, `BindCanExecute(...)` |
| Create | `src/SiemensS7Demo.App/Auth/PasswordHasher.cs` | Argon2id wrapper (hash + verify) |
| Create | `src/SiemensS7Demo.App/Auth/IProtectedStore.cs` | Cross-platform abstraction |
| Create | `src/SiemensS7Demo.App/Auth/InMemoryProtectedStore.cs` | Test fake (no encryption, base64) |
| Create | `src/SiemensS7Demo.App/Auth/DpapiProtectedStore.cs` | Windows-only DPAPI implementation (uses `[SupportedOSPlatform("windows")]`) |
| Create | `src/SiemensS7Demo.App/Lims/ILimsClient.cs` | Interface |
| Create | `src/SiemensS7Demo.App/Lims/HttpLimsClient.cs` | `HttpClient` + JSON impl |
| Create | `src/SiemensS7Demo.App/Lims/FileWatcherLimsClient.cs` | Fallback file-mode impl |
| Create | `src/SiemensS7Demo.App/Lims/LimsClientOptions.cs` | Mode (Http/File), base URL or watch dir |
| Create | `src/SiemensS7Demo.App/Mqtt/IMqttPublisher.cs` | Interface |
| Create | `src/SiemensS7Demo.App/Mqtt/MqttPublisher.cs` | MQTTnet implementation |
| Create | `src/SiemensS7Demo.App/Mqtt/MqttPublisherOptions.cs` | Broker host, port, creds, topic prefix |
| Create | `src/SiemensS7Demo.App/Mqtt/TelemetrySamplerService.cs` | Periodic publisher built on `IDeviceSessionManager` |
| Create | `src/SiemensS7Demo.App/Ftp/IFtpUploader.cs` | Interface |
| Create | `src/SiemensS7Demo.App/Ftp/FluentFtpUploader.cs` | FluentFTP impl |
| Create | `src/SiemensS7Demo.App/Ftp/FtpUploaderOptions.cs` | Host, creds |
| Create | `src/SiemensS7Demo.App/Ftp/BackupScheduler.cs` | On-demand + cron-style backup driver |
| Modify | `src/SiemensS7Demo.Persistence/EnviroDbContext.cs` | Add `DbSet<UserEntity>` |
| Create | `src/SiemensS7Demo.Persistence/Entities/UserEntity.cs` | EF Core entity |
| Create | `src/SiemensS7Demo.Persistence/Migrations/20260515_AddUsersTable.cs` | Adds users table + seeds admin/op/eng |
| Create | `src/SiemensS7Demo.Wpf/Views/LoginView.xaml` | 3-step XAML view |
| Create | `src/SiemensS7Demo.Wpf/Views/LoginView.xaml.cs` | Code-behind (DI shim only) |
| Create | `src/SiemensS7Demo.Wpf/ViewModels/LoginViewModel.cs` | 3-step state machine |
| Create | `src/SiemensS7Demo.Wpf/Views/LimsView.xaml` | 4-tab kanban view |
| Create | `src/SiemensS7Demo.Wpf/Views/LimsView.xaml.cs` | Code-behind |
| Create | `src/SiemensS7Demo.Wpf/ViewModels/LimsViewModel.cs` | Task list + filter |
| Create | `src/SiemensS7Demo.Wpf/Views/MqttSettingsView.xaml` | Broker config UI |
| Create | `src/SiemensS7Demo.Wpf/ViewModels/MqttSettingsViewModel.cs` | Settings VM (uses `IProtectedStore`) |
| Modify | `src/SiemensS7Demo.Wpf/App.xaml.cs` | Wire DI for Auth/LIMS/MQTT/FTP, add `--headless-smoke=auth` branch |
| Modify | `src/SiemensS7Demo.Wpf/Shell.xaml.cs` | Apply RBAC visibility on nav items |
| Modify | `src/SiemensS7Demo.Wpf/ViewModels/SingleDeviceViewModel.cs` | Decorate write commands with `[RequiresRole(Role.Engineer)]` |
| Create | `tests/EnviroEquipment.E2ETests/Pkg4/compose.yml` | mosquitto + ftp containers |
| Create | `tests/EnviroEquipment.E2ETests/Pkg4/mosquitto.conf` | broker config |
| Create | `tests/EnviroEquipment.E2ETests/Pkg4/LoginAndLimsTests.cs` | Auth + LIMS E2E |
| Create | `tests/EnviroEquipment.E2ETests/Pkg4/TelemetryUplinkTests.cs` | MQTT + FTP E2E |
| Create | `tests/EnviroEquipment.App.Tests/Auth/AuthServiceTests.cs` | Hash/verify/lockout/shift |
| Create | `tests/EnviroEquipment.App.Tests/Auth/PasswordHasherTests.cs` | Argon2id round-trip |
| Create | `tests/EnviroEquipment.App.Tests/Auth/RbacGuardTests.cs` | Per-role matrix |
| Create | `tests/EnviroEquipment.App.Tests/Auth/ProtectedStoreTests.cs` | InMemory round-trip; DPAPI skip on non-Win |
| Create | `tests/EnviroEquipment.App.Tests/Lims/HttpLimsClientTests.cs` | Against LimsMockServer |
| Create | `tests/EnviroEquipment.App.Tests/Lims/FileWatcherLimsClientTests.cs` | Fallback mode |
| Create | `tests/EnviroEquipment.App.Tests/Lims/LimsMockServer.cs` | Embedded Kestrel mock |
| Create | `tests/EnviroEquipment.App.Tests/Mqtt/MqttPublisherTests.cs` | Against embedded broker |
| Create | `tests/EnviroEquipment.App.Tests/Mqtt/PlaintextLeakTests.cs` | Asserts credential never appears in stdout/log/file |
| Create | `tests/EnviroEquipment.App.Tests/Mqtt/TelemetrySamplerServiceTests.cs` | Periodic publish behavior |
| Create | `tests/EnviroEquipment.App.Tests/Ftp/FluentFtpUploaderTests.cs` | Against test FTP server |
| Create | `tests/EnviroEquipment.Wpf.Tests/ViewModels/LoginViewModelTests.cs` | 3-step flow + validation |
| Create | `tests/EnviroEquipment.Wpf.Tests/ViewModels/LimsViewModelTests.cs` | Tab counts + filter |
| Create | `tests/EnviroEquipment.Wpf.Tests/ViewModels/MqttSettingsViewModelTests.cs` | Protected store round-trip via VM |
| Create | `tests/EnviroEquipment.Wpf.Tests/Rbac/CommandVisibilityMatrixTests.cs` | Per-role × per-command grid |
| Create | `tools/lims-probe.ps1` | Spike script for legacy protocol RE |
| Create | `docs/superpowers/notes/2026-05-15-lims-protocol-findings.md` | Spike output |

---

## Task 1 (M4.1): User/Role/Shift + AuthService with Argon2id

**Files:** Create `src/SiemensS7Demo.Domain/Users/*.cs`, `src/SiemensS7Demo.App/Auth/PasswordHasher.cs`, `src/SiemensS7Demo.App/Auth/IAuthService.cs`, `src/SiemensS7Demo.App/Auth/AuthService.cs`, `src/SiemensS7Demo.Persistence/Entities/UserEntity.cs`, migration `20260515_AddUsersTable.cs`, modify `EnviroDbContext.cs`. Create `tests/EnviroEquipment.App.Tests/Auth/PasswordHasherTests.cs` and `AuthServiceTests.cs`.

- [ ] **Step 1.1: Add NuGet references**

In `src/SiemensS7Demo.App/SiemensS7Demo.App.csproj`, ensure the `<ItemGroup>` for PackageReference includes:

```xml
  <ItemGroup>
    <PackageReference Include="Konscious.Security.Cryptography.Argon2" Version="1.3.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.0" />
  </ItemGroup>
```

Run:

```pwsh
dotnet restore EnviroEquipmentFinalEdition.sln
```

Expected output: `Restore complete` for every project; no missing-package warnings.

- [ ] **Step 1.2: Write the failing password-hasher test**

Create `tests/EnviroEquipment.App.Tests/Auth/PasswordHasherTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.App.Auth;
using Xunit;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class PasswordHasherTests
{
    [Fact]
    public void Hash_ProducesArgon2idEncodedString()
    {
        var hasher = new PasswordHasher();

        var hash = hasher.Hash("hunter2");

        hash.Should().StartWith("$argon2id$");
        hash.Length.Should().BeGreaterThan(60);
    }

    [Fact]
    public void Verify_ReturnsTrue_ForMatchingPassword()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("hunter2");

        hasher.Verify("hunter2", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalse_ForWrongPassword()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("hunter2");

        hasher.Verify("hunter3", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_ProducesDistinctSaltedHashes()
    {
        var hasher = new PasswordHasher();

        var a = hasher.Hash("hunter2");
        var b = hasher.Hash("hunter2");

        a.Should().NotBe(b, "Argon2id must apply a random salt per hash.");
    }

    [Fact]
    public void Verify_ReturnsFalse_ForGarbageHash()
    {
        var hasher = new PasswordHasher();

        hasher.Verify("anything", "not-a-real-hash").Should().BeFalse();
    }
}
```

- [ ] **Step 1.3: Run, confirm failure**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~PasswordHasherTests"
```

Expected output: compilation failure — `PasswordHasher` and `SiemensS7Demo.App.Auth` namespace do not exist yet.

- [ ] **Step 1.4: Implement Role / Shift / User / AuthResult**

Create `src/SiemensS7Demo.Domain/Users/Role.cs`:

```csharp
namespace SiemensS7Demo.Domain.Users;

public enum Role
{
    Operator = 0,
    Engineer = 1,
    Admin = 2
}
```

Create `src/SiemensS7Demo.Domain/Users/User.cs`:

```csharp
namespace SiemensS7Demo.Domain.Users;

public sealed record User(string Id, string Name, Role Role, string Code, string PasswordHash);
```

Create `src/SiemensS7Demo.Domain/Users/Shift.cs`:

```csharp
using System;

namespace SiemensS7Demo.Domain.Users;

public sealed record Shift(string Code, string Name, DateOnly Date)
{
    public static Shift ForLocalNow(DateTimeOffset? now = null)
    {
        var moment = now ?? DateTimeOffset.Now;
        var hour = moment.LocalDateTime.Hour;
        var date = DateOnly.FromDateTime(moment.LocalDateTime);
        return hour switch
        {
            >= 6 and < 14 => new Shift("DAY-A", "白班 A", date),
            >= 14 and < 22 => new Shift("DAY-B", "白班 B", date),
            _ => new Shift("NIGHT", "夜班", date)
        };
    }

    public static IReadOnlyList<Shift> AllForDate(DateOnly date) =>
        new[]
        {
            new Shift("DAY-A", "白班 A", date),
            new Shift("DAY-B", "白班 B", date),
            new Shift("NIGHT", "夜班", date)
        };
}
```

Create `src/SiemensS7Demo.Domain/Users/AuthResult.cs`:

```csharp
namespace SiemensS7Demo.Domain.Users;

public sealed record AuthResult(bool Success, User? User, string? ErrorMessage)
{
    public static AuthResult Ok(User user) => new(true, user, null);
    public static AuthResult Fail(string error) => new(false, null, error);
}
```

- [ ] **Step 1.5: Implement `PasswordHasher`**

Create `src/SiemensS7Demo.App/Auth/PasswordHasher.cs`:

```csharp
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace SiemensS7Demo.App.Auth;

/// <summary>
/// Argon2id password hasher. Output format: $argon2id$v=19$m=65536,t=3,p=2$&lt;saltB64&gt;$&lt;hashB64&gt;.
/// </summary>
public sealed class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemoryKb = 65536;
    private const int Iterations = 3;
    private const int Parallelism = 2;

    public string Hash(string password)
    {
        if (password is null) throw new ArgumentNullException(nameof(password));
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt);
        return $"$argon2id$v=19$m={MemoryKb},t={Iterations},p={Parallelism}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encoded)
    {
        if (password is null) return false;
        if (string.IsNullOrEmpty(encoded)) return false;

        try
        {
            var parts = encoded.Split('$', StringSplitOptions.None);
            // Expected layout: ["", "argon2id", "v=19", "m=...,t=...,p=...", saltB64, hashB64]
            if (parts.Length != 6) return false;
            if (parts[1] != "argon2id") return false;

            var paramSegment = parts[3];
            var (mem, it, par) = ParseParams(paramSegment);
            var salt = Convert.FromBase64String(parts[4]);
            var expected = Convert.FromBase64String(parts[5]);
            var actual = Derive(password, salt, mem, it, par, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt,
                                 int memoryKb = MemoryKb, int iterations = Iterations,
                                 int parallelism = Parallelism, int hashSize = HashSize)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password));
        argon.Salt = salt;
        argon.DegreeOfParallelism = parallelism;
        argon.MemorySize = memoryKb;
        argon.Iterations = iterations;
        return argon.GetBytes(hashSize);
    }

    private static (int memoryKb, int iterations, int parallelism) ParseParams(string segment)
    {
        int mem = MemoryKb, it = Iterations, par = Parallelism;
        foreach (var kv in segment.Split(','))
        {
            var pair = kv.Split('=', 2);
            if (pair.Length != 2) continue;
            var value = int.Parse(pair[1], CultureInfo.InvariantCulture);
            switch (pair[0])
            {
                case "m": mem = value; break;
                case "t": it = value; break;
                case "p": par = value; break;
            }
        }
        return (mem, it, par);
    }
}
```

- [ ] **Step 1.6: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~PasswordHasherTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5`.

- [ ] **Step 1.7: Write failing AuthService tests**

Create `tests/EnviroEquipment.App.Tests/Auth/AuthServiceTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using SiemensS7Demo.Persistence;
using SiemensS7Demo.Persistence.Entities;
using Xunit;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class AuthServiceTests
{
    private static EnviroDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<EnviroDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var ctx = new EnviroDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static AuthService MakeService(EnviroDbContext ctx, out PasswordHasher hasher)
    {
        hasher = new PasswordHasher();
        return new AuthService(ctx, hasher, NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task SignIn_Succeeds_WithCorrectCredentials()
    {
        using var ctx = CreateDb();
        var hasher = new PasswordHasher();
        ctx.Users.Add(new UserEntity
        {
            Id = "u-op", Name = "Op", Code = "OP-1", Role = Role.Operator,
            PasswordHash = hasher.Hash("pw1")
        });
        await ctx.SaveChangesAsync();

        var svc = new AuthService(ctx, hasher, NullLogger<AuthService>.Instance);
        var shift = Shift.ForLocalNow();

        var result = await svc.SignInAsync("OP-1", "pw1", shift, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.User!.Name.Should().Be("Op");
        result.User!.Role.Should().Be(Role.Operator);
        svc.Current.Should().BeSameAs(result.User);
        svc.CurrentShift.Should().Be(shift);
    }

    [Fact]
    public async Task SignIn_Fails_WithWrongPassword()
    {
        using var ctx = CreateDb();
        var hasher = new PasswordHasher();
        ctx.Users.Add(new UserEntity
        {
            Id = "u-op", Name = "Op", Code = "OP-1", Role = Role.Operator,
            PasswordHash = hasher.Hash("pw1")
        });
        await ctx.SaveChangesAsync();

        var svc = new AuthService(ctx, hasher, NullLogger<AuthService>.Instance);
        var result = await svc.SignInAsync("OP-1", "WRONG", Shift.ForLocalNow(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("invalid", StringComparison.OrdinalIgnoreCase);
        svc.Current.Should().BeNull();
    }

    [Fact]
    public async Task SignIn_Fails_WhenUserUnknown()
    {
        using var ctx = CreateDb();
        var svc = MakeService(ctx, out _);

        var result = await svc.SignInAsync("NOPE", "pw", Shift.ForLocalNow(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("invalid", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignIn_LocksOut_After5FailuresWithin30Seconds()
    {
        using var ctx = CreateDb();
        var hasher = new PasswordHasher();
        ctx.Users.Add(new UserEntity
        {
            Id = "u-op", Name = "Op", Code = "OP-1", Role = Role.Operator,
            PasswordHash = hasher.Hash("pw1")
        });
        await ctx.SaveChangesAsync();
        var svc = new AuthService(ctx, hasher, NullLogger<AuthService>.Instance);

        for (var i = 0; i < 5; i++)
        {
            var r = await svc.SignInAsync("OP-1", "WRONG", Shift.ForLocalNow(), CancellationToken.None);
            r.Success.Should().BeFalse();
        }
        var locked = await svc.SignInAsync("OP-1", "pw1", Shift.ForLocalNow(), CancellationToken.None);

        locked.Success.Should().BeFalse();
        locked.ErrorMessage.Should().Contain("locked", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignOut_ClearsCurrent()
    {
        using var ctx = CreateDb();
        var hasher = new PasswordHasher();
        ctx.Users.Add(new UserEntity
        {
            Id = "u-op", Name = "Op", Code = "OP-1", Role = Role.Operator,
            PasswordHash = hasher.Hash("pw1")
        });
        await ctx.SaveChangesAsync();
        var svc = new AuthService(ctx, hasher, NullLogger<AuthService>.Instance);

        await svc.SignInAsync("OP-1", "pw1", Shift.ForLocalNow(), CancellationToken.None);
        svc.Current.Should().NotBeNull();

        svc.SignOut();
        svc.Current.Should().BeNull();
        svc.CurrentShift.Should().BeNull();
    }
}
```

- [ ] **Step 1.8: Implement `IAuthService` + `AuthService`**

Create `src/SiemensS7Demo.App/Auth/IAuthService.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

public interface IAuthService
{
    User? Current { get; }
    Shift? CurrentShift { get; }
    Task<AuthResult> SignInAsync(string code, string password, Shift shift, CancellationToken ct);
    void SignOut();
}
```

Create `src/SiemensS7Demo.App/Auth/AuthService.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiemensS7Demo.Domain.Users;
using SiemensS7Demo.Persistence;

namespace SiemensS7Demo.App.Auth;

public sealed class AuthService : IAuthService
{
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromSeconds(30);
    private const int LockoutThreshold = 5;

    private readonly EnviroDbContext _db;
    private readonly PasswordHasher _hasher;
    private readonly ILogger<AuthService> _log;
    private readonly ConcurrentDictionary<string, FailureBucket> _failures = new();

    public AuthService(EnviroDbContext db, PasswordHasher hasher, ILogger<AuthService> log)
    {
        _db = db;
        _hasher = hasher;
        _log = log;
    }

    public User? Current { get; private set; }
    public Shift? CurrentShift { get; private set; }

    public async Task<AuthResult> SignInAsync(string code, string password, Shift shift, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || password is null)
        {
            return AuthResult.Fail("Invalid credentials.");
        }

        if (IsLockedOut(code))
        {
            _log.LogWarning("Sign-in blocked: {Code} locked out.", code);
            return AuthResult.Fail("Account is temporarily locked. Try again in 30 seconds.");
        }

        var entity = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Code == code, ct)
            .ConfigureAwait(false);
        if (entity is null)
        {
            RecordFailure(code);
            return AuthResult.Fail("Invalid credentials.");
        }

        if (!_hasher.Verify(password, entity.PasswordHash))
        {
            RecordFailure(code);
            _log.LogInformation("Sign-in failed for {Code}.", code);
            return AuthResult.Fail("Invalid credentials.");
        }

        _failures.TryRemove(code, out _);
        var user = new User(entity.Id, entity.Name, entity.Role, entity.Code, entity.PasswordHash);
        Current = user;
        CurrentShift = shift;
        _log.LogInformation("Sign-in succeeded: {Code} role={Role}.", code, entity.Role);
        return AuthResult.Ok(user);
    }

    public void SignOut()
    {
        Current = null;
        CurrentShift = null;
    }

    private bool IsLockedOut(string code)
    {
        if (!_failures.TryGetValue(code, out var bucket)) return false;
        if (DateTimeOffset.UtcNow - bucket.WindowStart > LockoutWindow)
        {
            _failures.TryRemove(code, out _);
            return false;
        }
        return bucket.Count >= LockoutThreshold;
    }

    private void RecordFailure(string code)
    {
        _failures.AddOrUpdate(code,
            _ => new FailureBucket(DateTimeOffset.UtcNow, 1),
            (_, existing) =>
            {
                if (DateTimeOffset.UtcNow - existing.WindowStart > LockoutWindow)
                {
                    return new FailureBucket(DateTimeOffset.UtcNow, 1);
                }
                return existing with { Count = existing.Count + 1 };
            });
    }

    private sealed record FailureBucket(DateTimeOffset WindowStart, int Count);
}
```

- [ ] **Step 1.9: Create `UserEntity` and the migration**

Create `src/SiemensS7Demo.Persistence/Entities/UserEntity.cs`:

```csharp
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.Persistence.Entities;

public sealed class UserEntity
{
    public string Id { get; set; } = default!;
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public Role Role { get; set; }
    public string PasswordHash { get; set; } = default!;
}
```

Modify `src/SiemensS7Demo.Persistence/EnviroDbContext.cs`. Add to the class body:

```csharp
    public DbSet<UserEntity> Users => Set<UserEntity>();
```

Inside `OnModelCreating(ModelBuilder modelBuilder)`, after the existing entity configuration, add:

```csharp
        modelBuilder.Entity<UserEntity>(b =>
        {
            b.ToTable("Users");
            b.HasKey(u => u.Id);
            b.HasIndex(u => u.Code).IsUnique();
            b.Property(u => u.Id).HasMaxLength(64);
            b.Property(u => u.Code).HasMaxLength(64);
            b.Property(u => u.Name).HasMaxLength(128);
            b.Property(u => u.Role).HasConversion<int>();
            b.Property(u => u.PasswordHash).HasMaxLength(512);
        });
```

Create `src/SiemensS7Demo.Persistence/Migrations/20260515000001_AddUsersTable.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;

#nullable disable

namespace SiemensS7Demo.Persistence.Migrations;

public partial class AddUsersTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Role = table.Column<int>(type: "INTEGER", nullable: false),
                PasswordHash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Users", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_Users_Code",
            table: "Users",
            column: "Code",
            unique: true);

        var hasher = new PasswordHasher();
        SeedUser(migrationBuilder, "u-admin", "AD-0001", "Admin",    Role.Admin,    hasher.Hash("admin"));
        SeedUser(migrationBuilder, "u-eng",   "EN-2011", "张工",      Role.Engineer, hasher.Hash("engineer"));
        SeedUser(migrationBuilder, "u-op",    "OP-1042", "李工",      Role.Operator, hasher.Hash("operator"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("Users");
    }

    private static void SeedUser(MigrationBuilder mb, string id, string code, string name, Role role, string hash)
    {
        mb.InsertData(
            table: "Users",
            columns: new[] { "Id", "Code", "Name", "Role", "PasswordHash" },
            values: new object[] { id, code, name, (int)role, hash });
    }
}
```

- [ ] **Step 1.10: Run, confirm AuthService tests pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~AuthServiceTests|FullyQualifiedName~PasswordHasherTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10`.

- [ ] **Step 1.11: Stage (DO NOT COMMIT — plan rule)**

Leave changes staged; the umbrella runner will commit at PR time.

```pwsh
git status --short
```

Expected output: shows added Domain/Users files, App/Auth files, Persistence updates, and the new tests as untracked or modified.

---

## Task 2 (M4.2): Login screen — 3-step view + ViewModel

**Files:** Create `src/SiemensS7Demo.Wpf/ViewModels/LoginViewModel.cs`, `src/SiemensS7Demo.Wpf/Views/LoginView.xaml`, `src/SiemensS7Demo.Wpf/Views/LoginView.xaml.cs`. Create `tests/EnviroEquipment.Wpf.Tests/ViewModels/LoginViewModelTests.cs`.

- [ ] **Step 2.1: Write failing VM tests**

Create `tests/EnviroEquipment.Wpf.Tests/ViewModels/LoginViewModelTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using SiemensS7Demo.Persistence;
using SiemensS7Demo.Persistence.Entities;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg4")]
public class LoginViewModelTests
{
    private static (EnviroDbContext db, AuthService auth) MakeAuth()
    {
        var opts = new DbContextOptionsBuilder<EnviroDbContext>()
            .UseSqlite("DataSource=:memory:").Options;
        var ctx = new EnviroDbContext(opts);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        var hasher = new PasswordHasher();
        ctx.Users.Add(new UserEntity
        {
            Id = "u-op", Code = "OP-1", Name = "Op", Role = Role.Operator,
            PasswordHash = hasher.Hash("pw")
        });
        ctx.SaveChanges();
        return (ctx, new AuthService(ctx, hasher, NullLogger<AuthService>.Instance));
    }

    [Fact]
    public void InitialStep_IsSelectAccount()
    {
        var (db, auth) = MakeAuth();
        using var _ = db;
        var vm = new LoginViewModel(auth);

        vm.Step.Should().Be(LoginStep.SelectAccount);
        vm.NextCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SelectingUser_AdvancesToPassword()
    {
        var (db, auth) = MakeAuth();
        using var _ = db;
        var vm = new LoginViewModel(auth);

        vm.SelectUser("OP-1");

        vm.Step.Should().Be(LoginStep.EnterPassword);
        vm.SelectedCode.Should().Be("OP-1");
    }

    [Fact]
    public async Task EnteringCorrectPassword_AdvancesToShift()
    {
        var (db, auth) = MakeAuth();
        using var _ = db;
        var vm = new LoginViewModel(auth);
        vm.SelectUser("OP-1");
        vm.Password = "pw";

        await vm.SubmitPasswordAsync(CancellationToken.None);

        vm.Step.Should().Be(LoginStep.ConfirmShift);
        vm.ErrorMessage.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task WrongPassword_ShowsError_AndStaysOnPasswordStep()
    {
        var (db, auth) = MakeAuth();
        using var _ = db;
        var vm = new LoginViewModel(auth);
        vm.SelectUser("OP-1");
        vm.Password = "NOPE";

        await vm.SubmitPasswordAsync(CancellationToken.None);

        vm.Step.Should().Be(LoginStep.EnterPassword);
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DefaultShift_MatchesLocalTimeBucket()
    {
        var (db, auth) = MakeAuth();
        using var _ = db;
        var vm = new LoginViewModel(auth);

        var expected = Shift.ForLocalNow();
        vm.SelectedShift.Should().NotBeNull();
        vm.SelectedShift!.Code.Should().Be(expected.Code);
    }

    [Fact]
    public async Task ConfirmingShift_SignsInUserOnAuthService()
    {
        var (db, auth) = MakeAuth();
        using var _ = db;
        var vm = new LoginViewModel(auth);
        vm.SelectUser("OP-1");
        vm.Password = "pw";
        await vm.SubmitPasswordAsync(CancellationToken.None);

        await vm.ConfirmShiftAsync(CancellationToken.None);

        auth.Current.Should().NotBeNull();
        auth.Current!.Code.Should().Be("OP-1");
        vm.Step.Should().Be(LoginStep.SignedIn);
    }

    [Fact]
    public void Back_FromShift_ReturnsToPassword_AndClearsPassword()
    {
        var (db, auth) = MakeAuth();
        using var _ = db;
        var vm = new LoginViewModel(auth);
        vm.SelectUser("OP-1");
        vm.Password = "pw";

        vm.Back();

        vm.Step.Should().Be(LoginStep.SelectAccount);
        vm.Password.Should().BeEmpty();
    }
}
```

- [ ] **Step 2.2: Run, confirm failure**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~LoginViewModelTests"
```

Expected output: compilation failure — `LoginViewModel` and `LoginStep` do not exist.

- [ ] **Step 2.3: Implement `LoginViewModel`**

Create `src/SiemensS7Demo.Wpf/ViewModels/LoginViewModel.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.Wpf.ViewModels;

public enum LoginStep { SelectAccount, EnterPassword, ConfirmShift, SignedIn }

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;

    public LoginViewModel(IAuthService auth)
    {
        _auth = auth;
        Shifts = Shift.AllForDate(System.DateOnly.FromDateTime(System.DateTime.Now));
        SelectedShift = Shift.ForLocalNow();
    }

    [ObservableProperty]
    private LoginStep _step = LoginStep.SelectAccount;

    [ObservableProperty]
    private string? _selectedCode;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private Shift? _selectedShift;

    [ObservableProperty]
    private string? _errorMessage;

    public IReadOnlyList<Shift> Shifts { get; }

    public void SelectUser(string code)
    {
        SelectedCode = code;
        ErrorMessage = null;
        Step = LoginStep.EnterPassword;
    }

    [RelayCommand(CanExecute = nameof(CanSubmitPassword))]
    public async Task SubmitPasswordAsync(CancellationToken ct)
    {
        if (SelectedCode is null) return;
        var probe = await _auth.SignInAsync(SelectedCode, Password, SelectedShift ?? Shift.ForLocalNow(), ct);
        if (!probe.Success)
        {
            ErrorMessage = probe.ErrorMessage;
            return;
        }
        // We sign in early to validate the password — sign out so the final shift-confirm step is the
        // one that produces a persistent session. AuthService keeps no other side effects.
        _auth.SignOut();
        ErrorMessage = null;
        Step = LoginStep.ConfirmShift;
    }

    private bool CanSubmitPassword() => !string.IsNullOrEmpty(SelectedCode) && !string.IsNullOrEmpty(Password);

    [RelayCommand]
    public async Task ConfirmShiftAsync(CancellationToken ct)
    {
        if (SelectedCode is null || SelectedShift is null) return;
        var result = await _auth.SignInAsync(SelectedCode, Password, SelectedShift, ct);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            Step = LoginStep.EnterPassword;
            return;
        }
        ErrorMessage = null;
        Step = LoginStep.SignedIn;
    }

    [RelayCommand]
    public void Back()
    {
        ErrorMessage = null;
        switch (Step)
        {
            case LoginStep.EnterPassword:
                Password = string.Empty;
                Step = LoginStep.SelectAccount;
                break;
            case LoginStep.ConfirmShift:
                Step = LoginStep.EnterPassword;
                break;
            default:
                Password = string.Empty;
                SelectedCode = null;
                Step = LoginStep.SelectAccount;
                break;
        }
    }

    [RelayCommand]
    public void SelectShift(Shift shift) => SelectedShift = shift;

    public IReadOnlyList<(string Code, string Display)> KnownAccounts =>
        new[]
        {
            ("OP-1042", "李工 · 实验员"),
            ("OP-1043", "王工 · 实验员"),
            ("EN-2011", "张工 · 工程师"),
            ("AD-0001", "Admin · 管理员")
        }.Select(x => (x.Item1, x.Item2)).ToList();
}
```

- [ ] **Step 2.4: Create `LoginView.xaml` and code-behind**

Create `src/SiemensS7Demo.Wpf/Views/LoginView.xaml`:

```xml
<UserControl x:Class="SiemensS7Demo.Wpf.Views.LoginView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:SiemensS7Demo.Wpf.ViewModels"
             Background="{DynamicResource BrushBg1}">
    <UserControl.Resources>
        <vm:LoginStepToVisibilityConverter x:Key="StepVis"/>
    </UserControl.Resources>
    <Grid>
        <Border HorizontalAlignment="Center" VerticalAlignment="Center"
                Width="880" Background="{DynamicResource BrushBg2}"
                BorderBrush="{DynamicResource BrushLine1}" BorderThickness="1"
                CornerRadius="8" Padding="0">
            <StackPanel>
                <TextBlock Text="温箱控制系统" FontSize="18"
                           Foreground="{DynamicResource BrushTxt0}"
                           Margin="32,28,32,4"/>

                <!-- Step 1: account list -->
                <ItemsControl ItemsSource="{Binding KnownAccounts}" Margin="32,18"
                              Visibility="{Binding Step, Converter={StaticResource StepVis}, ConverterParameter=SelectAccount}">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <UniformGrid Columns="4"/>
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Button Content="{Binding Display}" Margin="6" Padding="14"
                                    Tag="{Binding Code}"
                                    Click="OnSelectAccount"/>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <!-- Step 2: password -->
                <StackPanel Margin="40,16"
                            Visibility="{Binding Step, Converter={StaticResource StepVis}, ConverterParameter=EnterPassword}">
                    <TextBlock Text="登录密码" Foreground="{DynamicResource BrushTxt1}"/>
                    <PasswordBox x:Name="PwdBox" PasswordChanged="OnPwdChanged"
                                 FontFamily="Consolas" FontSize="16" Margin="0,6,0,12"/>
                    <TextBlock Text="{Binding ErrorMessage}" Foreground="{DynamicResource BrushAlarm}"/>
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
                        <Button Content="返回" Command="{Binding BackCommand}" Margin="0,0,8,0" Padding="14,6"/>
                        <Button Content="下一步 · 确认班次"
                                Command="{Binding SubmitPasswordCommand}" Padding="14,6"/>
                    </StackPanel>
                </StackPanel>

                <!-- Step 3: shift -->
                <StackPanel Margin="40,16"
                            Visibility="{Binding Step, Converter={StaticResource StepVis}, ConverterParameter=ConfirmShift}">
                    <TextBlock Text="本次值班的班次" Foreground="{DynamicResource BrushTxt1}"/>
                    <ItemsControl ItemsSource="{Binding Shifts}" Margin="0,8">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate><UniformGrid Columns="3"/></ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Button Margin="4" Padding="8,12"
                                        Content="{Binding Name}"
                                        Click="OnSelectShift"
                                        Tag="{Binding}"/>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
                        <Button Content="返回" Command="{Binding BackCommand}" Margin="0,0,8,0" Padding="14,6"/>
                        <Button Content="进入系统"
                                Command="{Binding ConfirmShiftCommand}" Padding="14,6"/>
                    </StackPanel>
                </StackPanel>
            </StackPanel>
        </Border>
    </Grid>
</UserControl>
```

Create `src/SiemensS7Demo.Wpf/Views/LoginView.xaml.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SiemensS7Demo.Domain.Users;
using SiemensS7Demo.Wpf.ViewModels;

namespace SiemensS7Demo.Wpf.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private void OnSelectAccount(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is Button b && b.Tag is string code)
        {
            vm.SelectUser(code);
        }
    }

    private void OnPwdChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is PasswordBox pb)
        {
            vm.Password = pb.Password;
        }
    }

    private void OnSelectShift(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm && sender is Button b && b.Tag is Shift shift)
        {
            vm.SelectedShift = shift;
        }
    }
}

public sealed class LoginStepToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LoginStep step && parameter is string name &&
            Enum.TryParse<LoginStep>(name, out var target))
        {
            return step == target ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2.5: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~LoginViewModelTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7`.

- [ ] **Step 2.6: Stage and continue**

```pwsh
git status --short
```

Expected output: shows `LoginViewModel.cs`, `LoginView.xaml`, `LoginView.xaml.cs`, and the test file as new.

---

## Task 3 (M4.3): RBAC — `[RequiresRole]` attribute + command-binder + visibility matrix

**Files:** Create `src/SiemensS7Demo.App/Auth/RequiresRoleAttribute.cs`, `src/SiemensS7Demo.App/Auth/RbacGuard.cs`. Modify `src/SiemensS7Demo.Wpf/ViewModels/SingleDeviceViewModel.cs` to decorate its write commands. Modify `src/SiemensS7Demo.Wpf/Shell.xaml.cs` to hide forbidden nav items. Create `tests/EnviroEquipment.App.Tests/Auth/RbacGuardTests.cs` and `tests/EnviroEquipment.Wpf.Tests/Rbac/CommandVisibilityMatrixTests.cs`.

- [ ] **Step 3.1: Write failing `RbacGuardTests`**

Create `tests/EnviroEquipment.App.Tests/Auth/RbacGuardTests.cs`:

```csharp
using System.Reflection;
using FluentAssertions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using Xunit;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class RbacGuardTests
{
    private sealed class Sample
    {
        [RequiresRole(Role.Operator)]
        public void OperatorOk() { }

        [RequiresRole(Role.Engineer)]
        public void EngineerOnly() { }

        [RequiresRole(Role.Admin)]
        public void AdminOnly() { }

        public void Unannotated() { }
    }

    private static MethodInfo M(string name) => typeof(Sample).GetMethod(name)!;

    [Theory]
    [InlineData(Role.Operator, "OperatorOk", true)]
    [InlineData(Role.Operator, "EngineerOnly", false)]
    [InlineData(Role.Operator, "AdminOnly", false)]
    [InlineData(Role.Engineer, "OperatorOk", true)]
    [InlineData(Role.Engineer, "EngineerOnly", true)]
    [InlineData(Role.Engineer, "AdminOnly", false)]
    [InlineData(Role.Admin,    "OperatorOk", true)]
    [InlineData(Role.Admin,    "EngineerOnly", true)]
    [InlineData(Role.Admin,    "AdminOnly", true)]
    public void IsAllowed_RoleMatrix(Role role, string method, bool expected)
    {
        var user = new User("u", "n", role, "c", "h");
        RbacGuard.IsAllowed(user, M(method)).Should().Be(expected);
    }

    [Fact]
    public void IsAllowed_UnannotatedMethod_IsAllowedForEveryone()
    {
        var user = new User("u", "n", Role.Operator, "c", "h");
        RbacGuard.IsAllowed(user, M("Unannotated")).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_NullUser_IsAlwaysFalseForAnnotatedMethod()
    {
        RbacGuard.IsAllowed(null, M("OperatorOk")).Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_NullUser_IsAllowedForUnannotatedMethod()
    {
        RbacGuard.IsAllowed(null, M("Unannotated")).Should().BeTrue();
    }
}
```

- [ ] **Step 3.2: Run, confirm failure**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~RbacGuardTests"
```

Expected output: compilation failure — `RequiresRoleAttribute` and `RbacGuard` do not exist.

- [ ] **Step 3.3: Implement attribute + guard**

Create `src/SiemensS7Demo.App/Auth/RequiresRoleAttribute.cs`:

```csharp
using System;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class RequiresRoleAttribute : Attribute
{
    public RequiresRoleAttribute(Role minimum) { Minimum = minimum; }
    public Role Minimum { get; }
}
```

Create `src/SiemensS7Demo.App/Auth/RbacGuard.cs`:

```csharp
using System;
using System.Reflection;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

public static class RbacGuard
{
    public static bool IsAllowed(User? user, MethodInfo method)
    {
        var attr = method.GetCustomAttribute<RequiresRoleAttribute>(inherit: true);
        if (attr is null) return true;
        if (user is null) return false;
        return user.Role >= attr.Minimum;
    }

    public static bool IsAllowed(User? user, Role? minimum)
    {
        if (minimum is null) return true;
        if (user is null) return false;
        return user.Role >= minimum.Value;
    }

    public static Role? MinimumFor(MethodInfo method)
        => method.GetCustomAttribute<RequiresRoleAttribute>(inherit: true)?.Minimum;
}
```

- [ ] **Step 3.4: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~RbacGuardTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 12, Skipped: 0, Total: 12`.

- [ ] **Step 3.5: Write the command-visibility matrix test**

Create `tests/EnviroEquipment.Wpf.Tests/Rbac/CommandVisibilityMatrixTests.cs`:

```csharp
using System.Linq;
using System.Reflection;
using FluentAssertions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.Rbac;

[Trait("Category", "Pkg4")]
public class CommandVisibilityMatrixTests
{
    // Each row: (ViewModel method name, role minimum we expect to find on the attribute).
    public static TheoryData<string, Role> ExpectedAnnotations()
    {
        var data = new TheoryData<string, Role>();
        data.Add(nameof(SingleDeviceViewModel.WriteSetpointAsync), Role.Engineer);
        data.Add(nameof(SingleDeviceViewModel.StopExperimentAsync), Role.Engineer);
        data.Add(nameof(SingleDeviceViewModel.ResetAlarmAsync), Role.Operator);
        return data;
    }

    [Theory]
    [MemberData(nameof(ExpectedAnnotations))]
    public void SingleDeviceViewModel_CommandsCarryExpectedMinimumRole(string methodName, Role expected)
    {
        var method = typeof(SingleDeviceViewModel).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        method.Should().NotBeNull($"{methodName} must exist on SingleDeviceViewModel");
        var attr = method!.GetCustomAttribute<RequiresRoleAttribute>();
        attr.Should().NotBeNull($"{methodName} must declare [RequiresRole]");
        attr!.Minimum.Should().Be(expected);
    }

    [Theory]
    [InlineData(Role.Operator, nameof(SingleDeviceViewModel.WriteSetpointAsync), false)]
    [InlineData(Role.Engineer, nameof(SingleDeviceViewModel.WriteSetpointAsync), true)]
    [InlineData(Role.Admin,    nameof(SingleDeviceViewModel.WriteSetpointAsync), true)]
    [InlineData(Role.Operator, nameof(SingleDeviceViewModel.StopExperimentAsync), false)]
    [InlineData(Role.Engineer, nameof(SingleDeviceViewModel.StopExperimentAsync), true)]
    [InlineData(Role.Operator, nameof(SingleDeviceViewModel.ResetAlarmAsync), true)]
    [InlineData(Role.Engineer, nameof(SingleDeviceViewModel.ResetAlarmAsync), true)]
    public void RbacGuard_AllowsExactlyTheRolesAtOrAboveMinimum(Role role, string methodName, bool expected)
    {
        var method = typeof(SingleDeviceViewModel)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var user = new User("u", "n", role, "c", "h");

        RbacGuard.IsAllowed(user, method).Should().Be(expected);
    }
}
```

- [ ] **Step 3.6: Run, confirm failure**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~CommandVisibilityMatrixTests"
```

Expected output: 10 failures — `SingleDeviceViewModel` exists (from Pkg 1) but its methods do not yet carry `[RequiresRole]`.

- [ ] **Step 3.7: Annotate the existing commands**

Open `src/SiemensS7Demo.Wpf/ViewModels/SingleDeviceViewModel.cs` (created in Pkg 1 M1.5). Add `using SiemensS7Demo.App.Auth;` and decorate each command method:

```csharp
    [RelayCommand]
    [RequiresRole(Role.Engineer)]
    public async Task WriteSetpointAsync(double sv, CancellationToken ct) { /* existing body */ }

    [RelayCommand]
    [RequiresRole(Role.Engineer)]
    public async Task StopExperimentAsync(CancellationToken ct) { /* existing body */ }

    [RelayCommand]
    [RequiresRole(Role.Operator)]
    public async Task ResetAlarmAsync(CancellationToken ct) { /* existing body */ }
```

Also override `CanExecute` so disabled commands grey out instead of throwing. After the method definitions, add:

```csharp
    private readonly IAuthService _authForRbac;
    private bool CanWriteSetpoint() => RbacGuard.IsAllowed(_authForRbac.Current,
        typeof(SingleDeviceViewModel).GetMethod(nameof(WriteSetpointAsync))!);
    private bool CanStopExperiment() => RbacGuard.IsAllowed(_authForRbac.Current,
        typeof(SingleDeviceViewModel).GetMethod(nameof(StopExperimentAsync))!);
    private bool CanResetAlarm() => RbacGuard.IsAllowed(_authForRbac.Current,
        typeof(SingleDeviceViewModel).GetMethod(nameof(ResetAlarmAsync))!);
```

And ensure the `[RelayCommand]` attributes reference the predicates:

```csharp
    [RelayCommand(CanExecute = nameof(CanWriteSetpoint))]
    [RequiresRole(Role.Engineer)]
    public async Task WriteSetpointAsync(double sv, CancellationToken ct) { /* existing body */ }
```

Inject `IAuthService` into the existing constructor and assign `_authForRbac`. Subscribe to a property-change on `IAuthService.Current` (or in this implementation expose a `RaiseCanExecuteChanged()` method on the VM and call it when sign-in completes — `App.xaml.cs` will wire that). The minimal wiring: `_authForRbac.PropertyChanged += (_, _) => { WriteSetpointCommand.NotifyCanExecuteChanged(); ... };` if `IAuthService` is also `INotifyPropertyChanged`. Since `IAuthService` is not currently `INotifyPropertyChanged`, instead expose:

```csharp
public void RaiseRbacChanged()
{
    WriteSetpointCommand.NotifyCanExecuteChanged();
    StopExperimentCommand.NotifyCanExecuteChanged();
    ResetAlarmCommand.NotifyCanExecuteChanged();
}
```

`App.xaml.cs` will call this after sign-in / sign-out.

- [ ] **Step 3.8: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~CommandVisibilityMatrixTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10`.

- [ ] **Step 3.9: Hide nav items in `Shell.xaml.cs`**

Open `src/SiemensS7Demo.Wpf/Shell.xaml.cs` (from Pkg 1 M1.2). Locate the nav-item collection (`NavItems`) populated at startup. Add a `MinimumRole` field on the `NavItem` record (e.g. `public sealed record NavItem(string Id, string Label, string Icon, Role? MinimumRole = null)`) and add this filter step:

```csharp
public void ApplyRbac(IAuthService auth)
{
    foreach (var item in NavItems)
    {
        item.IsVisible = RbacGuard.IsAllowed(auth.Current, item.MinimumRole);
    }
    NotifyOfPropertyChange(nameof(VisibleNavItems));
}
```

Seed `MinimumRole` from the 202605 spec:
- `device`, `layout`, `maint` → `Role.Engineer`
- `users`, `settings` → `Role.Admin`
- all others → `null` (visible to everyone).

- [ ] **Step 3.10: Stage and continue**

```pwsh
git status --short
```

Expected output: lists modifications to `SingleDeviceViewModel.cs`, `Shell.xaml.cs`, plus new `RequiresRoleAttribute.cs`, `RbacGuard.cs`, two test files.

---

## Task 4 (M4.4): LIMS protocol spike + `ILimsClient` + mock server + file-watcher fallback

**Files:** Create `tools/lims-probe.ps1`, `docs/superpowers/notes/2026-05-15-lims-protocol-findings.md`. Create `src/SiemensS7Demo.App/Lims/ILimsClient.cs`, `HttpLimsClient.cs`, `FileWatcherLimsClient.cs`, `LimsClientOptions.cs`. Create `tests/EnviroEquipment.App.Tests/Lims/LimsMockServer.cs`, `HttpLimsClientTests.cs`, `FileWatcherLimsClientTests.cs`. Create domain types in `src/SiemensS7Demo.Domain/Lims/LimsTask.cs`.

- [ ] **Step 4.1: Run the spike (`tools/lims-probe.ps1`)**

Create `tools/lims-probe.ps1`:

```powershell
# lims-probe.ps1
# Spike: inspect the legacy BackStageLims module from 202604 and (optionally)
# capture live traffic to the legacy LIMS endpoint to derive the protocol.
# Output: prints a findings summary, intended to be redacted into
# docs/superpowers/notes/2026-05-15-lims-protocol-findings.md.

[CmdletBinding()]
param(
    [string] $LegacyRoot = "H:/qtFileForVscode/EnviroEquipmentFinalEdition_202604",
    [string] $LiveEndpoint = ""   # optional, e.g. https://lims.corp.intra/api/tasks
)

$ErrorActionPreference = 'Stop'

Write-Host "[lims-probe] Static analysis of $LegacyRoot ..."
$candidates = @(
    Join-Path $LegacyRoot 'Code/Service*/*Lims*.cpp'
    Join-Path $LegacyRoot 'Code/Service*/*Lims*.h'
    Join-Path $LegacyRoot 'Code/View/*Lims*.cpp'
    Join-Path $LegacyRoot 'Code/View/*Lims*.h'
)
$found = @()
foreach ($pat in $candidates) {
    Get-ChildItem -Path $pat -ErrorAction SilentlyContinue | ForEach-Object {
        $found += $_.FullName
    }
}

Write-Host "[lims-probe] Found $($found.Count) candidate source file(s)."
$urlHits = @()
$verbHits = @()
foreach ($f in $found) {
    $text = Get-Content $f -Raw -ErrorAction SilentlyContinue
    if ($text -match 'https?://[\w\-\./:?=&#%]+') {
        $urlHits += [pscustomobject]@{ File = $f; Match = $Matches[0] }
    }
    foreach ($v in @('GET','POST','PUT','DELETE','PATCH')) {
        if ($text -match "\b$v\b") {
            $verbHits += [pscustomobject]@{ File = $f; Verb = $v }
        }
    }
}

Write-Host "[lims-probe] URL fragments found:"
$urlHits | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "[lims-probe] HTTP verbs referenced:"
$verbHits | Group-Object Verb | Format-Table Name, Count -AutoSize | Out-String | Write-Host

if ($LiveEndpoint -ne "") {
    Write-Host "[lims-probe] Probing live endpoint $LiveEndpoint ..."
    try {
        $resp = Invoke-WebRequest -Uri $LiveEndpoint -Method GET -UseBasicParsing -TimeoutSec 5
        Write-Host "  Status: $($resp.StatusCode)"
        Write-Host "  Content-Type: $($resp.Headers['Content-Type'])"
        Write-Host "  Body head (400 chars):"
        Write-Host ($resp.Content.Substring(0, [Math]::Min(400, $resp.Content.Length)))
    } catch {
        Write-Host "  ERROR: $($_.Exception.Message)"
    }
} else {
    Write-Host "[lims-probe] No -LiveEndpoint supplied. Static analysis only."
}

Write-Host "[lims-probe] Done. Record findings in docs/superpowers/notes/2026-05-15-lims-protocol-findings.md."
```

Run it:

```pwsh
pwsh -File tools/lims-probe.ps1 -LegacyRoot "H:/qtFileForVscode/EnviroEquipmentFinalEdition_202604"
```

Expected output: prints `[lims-probe] Found N candidate source file(s).` and the URL/verb summary. Exit code 0.

- [ ] **Step 4.2: Write the findings note**

Create `docs/superpowers/notes/2026-05-15-lims-protocol-findings.md` with the captured summary. The note has two sections:

```markdown
# LIMS Protocol Findings — 2026-05-15

## Source

- Legacy modules inspected: BackStageLims, iMQTTService.h, ViewLims*.cpp.
- Spike script: tools/lims-probe.ps1.

## Outcome — branch A (HTTP+JSON detected)

If the legacy code surfaces an HTTP URL and JSON serialization (look for `QJsonDocument`, `QNetworkAccessManager`, etc.), the new client implements:

- `GET  /api/v1/tasks?status=&deviceId=&projectId=` returns `[ { id, deviceId, projectId, name, planStart, planEnd, actualStart?, actualEnd?, status } ]`
- `POST /api/v1/tasks/{id}/result` body `{ at, payloadJson }` returns `204`.

## Outcome — branch B (protocol unrecoverable)

If the legacy module obscures the wire protocol (proprietary serialization, dead code, or unbuildable), the new client falls back to file-watcher mode:

- LIMS exports task JSON to a known directory; the client watches the directory and reads the latest snapshot.
- Results are written back as `<TaskId>.result.json` files in the same directory.
- This is degraded but unblocks black-light operation until a follow-up plan re-attempts the live protocol.

The implementation in `src/SiemensS7Demo.App/Lims/` ships **both** branches behind `LimsClientOptions.Mode = Http | File`. The acceptance harness defaults to `Http` against `LimsMockServer`.
```

- [ ] **Step 4.3: Create domain types**

Create `src/SiemensS7Demo.Domain/Lims/LimsTask.cs`:

```csharp
using System;

namespace SiemensS7Demo.Domain.Lims;

public enum LimsTaskStatus { Todo, Running, Done, Cancelled }

public sealed record LimsTask(
    string Id,
    string DeviceId,
    string ProjectId,
    string Name,
    DateTimeOffset PlanStart,
    DateTimeOffset PlanEnd,
    DateTimeOffset? ActualStart,
    DateTimeOffset? ActualEnd,
    LimsTaskStatus Status);

public sealed record LimsFilter(string? DeviceId, string? ProjectId, LimsTaskStatus? Status);

public sealed record LimsTaskResult(string TaskId, DateTimeOffset At, string PayloadJson);
```

- [ ] **Step 4.4: Write failing `HttpLimsClientTests`**

First create the embedded server. Create `tests/EnviroEquipment.App.Tests/Lims/LimsMockServer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Lims;

namespace EnviroEquipment.App.Tests.Lims;

/// <summary>
/// Tiny self-contained HTTP server (HttpListener-based) that serves the
/// agreed LIMS contract. Not Kestrel-based to avoid pulling AspNetCore into App.Tests.
/// </summary>
public sealed class LimsMockServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
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
            catch { try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
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
            var payload = JsonSerializer.Serialize(data);
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
            ReceivedResults.Add(new LimsTaskResult(id, at, payloadJson));
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
        try { _listener.Stop(); } catch { }
        try { await _loop.ConfigureAwait(false); } catch { }
        _listener.Close();
    }
}
```

Create `tests/EnviroEquipment.App.Tests/Lims/HttpLimsClientTests.cs`:

```csharp
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Lims;
using SiemensS7Demo.Domain.Lims;
using Xunit;

namespace EnviroEquipment.App.Tests.Lims;

[Trait("Category", "Pkg4")]
public class HttpLimsClientTests
{
    private static LimsTask MakeTask(string id, LimsTaskStatus status) => new(
        Id: id, DeviceId: "TH-01", ProjectId: "P", Name: "n",
        PlanStart: DateTimeOffset.UtcNow, PlanEnd: DateTimeOffset.UtcNow.AddHours(1),
        ActualStart: null, ActualEnd: null, Status: status);

    [Fact]
    public async Task ListTasks_ReturnsAllSeededTasks()
    {
        await using var server = LimsMockServer.Start(new[]
        {
            MakeTask("L-1", LimsTaskStatus.Todo),
            MakeTask("L-2", LimsTaskStatus.Running),
            MakeTask("L-3", LimsTaskStatus.Done),
        });
        var client = new HttpLimsClient(new HttpClient { BaseAddress = server.BaseUri },
            new LimsClientOptions { Mode = LimsClientMode.Http, BaseUrl = server.BaseUri.ToString() });

        var tasks = await client.ListTasksAsync(new LimsFilter(null, null, null), CancellationToken.None);

        tasks.Should().HaveCount(3);
        tasks.Select(t => t.Id).Should().BeEquivalentTo(new[] { "L-1", "L-2", "L-3" });
    }

    [Fact]
    public async Task ListTasks_FiltersByStatus()
    {
        await using var server = LimsMockServer.Start(new[]
        {
            MakeTask("L-1", LimsTaskStatus.Todo),
            MakeTask("L-2", LimsTaskStatus.Running)
        });
        var client = new HttpLimsClient(new HttpClient { BaseAddress = server.BaseUri },
            new LimsClientOptions { Mode = LimsClientMode.Http, BaseUrl = server.BaseUri.ToString() });

        var tasks = await client.ListTasksAsync(new LimsFilter(null, null, LimsTaskStatus.Running), CancellationToken.None);

        tasks.Should().ContainSingle().Which.Id.Should().Be("L-2");
    }

    [Fact]
    public async Task UploadResult_PostsToServer()
    {
        await using var server = LimsMockServer.Start();
        var client = new HttpLimsClient(new HttpClient { BaseAddress = server.BaseUri },
            new LimsClientOptions { Mode = LimsClientMode.Http, BaseUrl = server.BaseUri.ToString() });

        var when = DateTimeOffset.UtcNow;
        await client.UploadResultAsync(new LimsTaskResult("L-9", when, "{\"v\":1}"), CancellationToken.None);

        server.ReceivedResults.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new LimsTaskResult("L-9", when, "{\"v\":1}"),
                opts => opts.Excluding(x => x.At)); // at-precision tolerance below
        server.ReceivedResults[0].At.Should().BeCloseTo(when, TimeSpan.FromSeconds(1));
    }
}
```

- [ ] **Step 4.5: Run, confirm failure**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~HttpLimsClientTests"
```

Expected output: compile failure — `HttpLimsClient`, `LimsClientOptions`, `LimsClientMode` do not exist.

- [ ] **Step 4.6: Implement HTTP client + options**

Create `src/SiemensS7Demo.App/Lims/LimsClientOptions.cs`:

```csharp
namespace SiemensS7Demo.App.Lims;

public enum LimsClientMode { Http, File }

public sealed class LimsClientOptions
{
    public LimsClientMode Mode { get; set; } = LimsClientMode.Http;
    public string BaseUrl { get; set; } = "http://lims.corp.intra/";
    public string? WatchDirectory { get; set; }
    public string? ApiToken { get; set; }
}
```

Create `src/SiemensS7Demo.App/Lims/ILimsClient.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Lims;

namespace SiemensS7Demo.App.Lims;

public interface ILimsClient
{
    Task<IReadOnlyList<LimsTask>> ListTasksAsync(LimsFilter filter, CancellationToken ct);
    Task UploadResultAsync(LimsTaskResult result, CancellationToken ct);
}
```

Create `src/SiemensS7Demo.App/Lims/HttpLimsClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Lims;

namespace SiemensS7Demo.App.Lims;

public sealed class HttpLimsClient : ILimsClient
{
    private readonly HttpClient _http;
    private readonly LimsClientOptions _opts;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public HttpLimsClient(HttpClient http, LimsClientOptions opts)
    {
        _http = http;
        _opts = opts;
        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_opts.BaseUrl))
            _http.BaseAddress = new Uri(_opts.BaseUrl);
        if (!string.IsNullOrEmpty(_opts.ApiToken))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiToken);
    }

    public async Task<IReadOnlyList<LimsTask>> ListTasksAsync(LimsFilter filter, CancellationToken ct)
    {
        var qs = new List<string>();
        if (filter.Status is not null) qs.Add($"status={Uri.EscapeDataString(filter.Status.ToString()!)}");
        if (!string.IsNullOrEmpty(filter.DeviceId)) qs.Add($"deviceId={Uri.EscapeDataString(filter.DeviceId)}");
        if (!string.IsNullOrEmpty(filter.ProjectId)) qs.Add($"projectId={Uri.EscapeDataString(filter.ProjectId)}");
        var url = "api/v1/tasks" + (qs.Count == 0 ? "" : "?" + string.Join('&', qs));
        var list = await _http.GetFromJsonAsync<List<LimsTask>>(url, JsonOpts, ct).ConfigureAwait(false);
        return list ?? new List<LimsTask>();
    }

    public async Task UploadResultAsync(LimsTaskResult result, CancellationToken ct)
    {
        var body = new { at = result.At, payloadJson = result.PayloadJson };
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"api/v1/tasks/{Uri.EscapeDataString(result.TaskId)}/result", content, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 4.7: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~HttpLimsClientTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3`.

- [ ] **Step 4.8: Implement and test the file-watcher fallback**

Create `tests/EnviroEquipment.App.Tests/Lims/FileWatcherLimsClientTests.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Lims;
using SiemensS7Demo.Domain.Lims;
using Xunit;

namespace EnviroEquipment.App.Tests.Lims;

[Trait("Category", "Pkg4")]
public class FileWatcherLimsClientTests
{
    [Fact]
    public async Task ListTasks_ReadsLatestSnapshotFromWatchDirectory()
    {
        var tmp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"lims-{Guid.NewGuid()}"));
        try
        {
            var snapshot = new[]
            {
                new LimsTask("L-1","TH-01","P","n", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), null, null, LimsTaskStatus.Todo)
            };
            await File.WriteAllTextAsync(Path.Combine(tmp.FullName, "tasks.json"),
                JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            var client = new FileWatcherLimsClient(new LimsClientOptions
            {
                Mode = LimsClientMode.File,
                WatchDirectory = tmp.FullName
            });

            var tasks = await client.ListTasksAsync(new LimsFilter(null, null, null), CancellationToken.None);
            tasks.Should().HaveCount(1);
            tasks[0].Id.Should().Be("L-1");
        }
        finally { tmp.Delete(true); }
    }

    [Fact]
    public async Task UploadResult_WritesResultFile()
    {
        var tmp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"lims-{Guid.NewGuid()}"));
        try
        {
            var client = new FileWatcherLimsClient(new LimsClientOptions
            {
                Mode = LimsClientMode.File,
                WatchDirectory = tmp.FullName
            });
            await client.UploadResultAsync(
                new LimsTaskResult("L-9", DateTimeOffset.UtcNow, "{\"v\":1}"), CancellationToken.None);

            var resultFile = Path.Combine(tmp.FullName, "L-9.result.json");
            File.Exists(resultFile).Should().BeTrue();
        }
        finally { tmp.Delete(true); }
    }
}
```

Create `src/SiemensS7Demo.App/Lims/FileWatcherLimsClient.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Lims;

namespace SiemensS7Demo.App.Lims;

public sealed class FileWatcherLimsClient : ILimsClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly LimsClientOptions _opts;

    public FileWatcherLimsClient(LimsClientOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.WatchDirectory))
            throw new ArgumentException("FileWatcherLimsClient requires WatchDirectory.", nameof(opts));
        _opts = opts;
    }

    public async Task<IReadOnlyList<LimsTask>> ListTasksAsync(LimsFilter filter, CancellationToken ct)
    {
        var path = Path.Combine(_opts.WatchDirectory!, "tasks.json");
        if (!File.Exists(path)) return Array.Empty<LimsTask>();
        await using var stream = File.OpenRead(path);
        var all = await JsonSerializer.DeserializeAsync<List<LimsTask>>(stream, Json, ct)
                  ?? new List<LimsTask>();
        IEnumerable<LimsTask> q = all;
        if (filter.Status is not null) q = q.Where(t => t.Status == filter.Status);
        if (!string.IsNullOrEmpty(filter.DeviceId)) q = q.Where(t => t.DeviceId == filter.DeviceId);
        if (!string.IsNullOrEmpty(filter.ProjectId)) q = q.Where(t => t.ProjectId == filter.ProjectId);
        return q.ToList();
    }

    public async Task UploadResultAsync(LimsTaskResult result, CancellationToken ct)
    {
        Directory.CreateDirectory(_opts.WatchDirectory!);
        var file = Path.Combine(_opts.WatchDirectory!, $"{result.TaskId}.result.json");
        var json = JsonSerializer.Serialize(result, Json);
        await File.WriteAllTextAsync(file, json, ct);
    }
}
```

- [ ] **Step 4.9: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~LimsClientTests|FullyQualifiedName~FileWatcherLimsClientTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5`.

- [ ] **Step 4.10: Stage and continue**

```pwsh
git status --short
```

Expected output: lists `tools/lims-probe.ps1`, `docs/superpowers/notes/2026-05-15-lims-protocol-findings.md`, `src/SiemensS7Demo.Domain/Lims/LimsTask.cs`, the App/Lims files, and three test files.

---

## Task 5 (M4.5): LIMS task list screen — `LimsView` + VM with 4 tabs

**Files:** Create `src/SiemensS7Demo.Wpf/ViewModels/LimsViewModel.cs`, `src/SiemensS7Demo.Wpf/Views/LimsView.xaml`, `src/SiemensS7Demo.Wpf/Views/LimsView.xaml.cs`. Create `tests/EnviroEquipment.Wpf.Tests/ViewModels/LimsViewModelTests.cs`.

- [ ] **Step 5.1: Write failing VM tests**

Create `tests/EnviroEquipment.Wpf.Tests/ViewModels/LimsViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Lims;
using SiemensS7Demo.Domain.Lims;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg4")]
public class LimsViewModelTests
{
    private sealed class FakeLims : ILimsClient
    {
        private readonly List<LimsTask> _tasks;
        public FakeLims(IEnumerable<LimsTask> tasks) => _tasks = tasks.ToList();
        public Task<IReadOnlyList<LimsTask>> ListTasksAsync(LimsFilter f, CancellationToken ct)
        {
            IEnumerable<LimsTask> q = _tasks;
            if (f.Status is not null) q = q.Where(t => t.Status == f.Status);
            if (!string.IsNullOrEmpty(f.DeviceId)) q = q.Where(t => t.DeviceId == f.DeviceId);
            if (!string.IsNullOrEmpty(f.ProjectId)) q = q.Where(t => t.ProjectId == f.ProjectId);
            return Task.FromResult<IReadOnlyList<LimsTask>>(q.ToList());
        }
        public Task UploadResultAsync(LimsTaskResult r, CancellationToken ct) => Task.CompletedTask;
    }

    private static LimsTask T(string id, LimsTaskStatus s, string dev = "TH-01", string proj = "P") =>
        new(id, dev, proj, "name", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            null, null, s);

    [Fact]
    public async Task Refresh_PopulatesAllFourTabs()
    {
        var lims = new FakeLims(new[]
        {
            T("L-1", LimsTaskStatus.Todo),
            T("L-2", LimsTaskStatus.Running),
            T("L-3", LimsTaskStatus.Running),
            T("L-4", LimsTaskStatus.Done),
            T("L-5", LimsTaskStatus.Cancelled),
        });
        var vm = new LimsViewModel(lims);

        await vm.RefreshAsync(CancellationToken.None);

        vm.Todo.Should().HaveCount(1);
        vm.Running.Should().HaveCount(2);
        vm.Done.Should().HaveCount(1);
        vm.Cancelled.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeviceFilter_LimitsTasksAcrossAllTabs()
    {
        var lims = new FakeLims(new[]
        {
            T("L-1", LimsTaskStatus.Todo, dev: "TH-01"),
            T("L-2", LimsTaskStatus.Todo, dev: "TH-02"),
            T("L-3", LimsTaskStatus.Running, dev: "TH-02"),
        });
        var vm = new LimsViewModel(lims) { DeviceFilter = "TH-02" };

        await vm.RefreshAsync(CancellationToken.None);

        vm.Todo.Should().ContainSingle().Which.Id.Should().Be("L-2");
        vm.Running.Should().ContainSingle().Which.Id.Should().Be("L-3");
    }

    [Fact]
    public async Task ProjectFilter_AppliesToAllTabs()
    {
        var lims = new FakeLims(new[]
        {
            T("L-1", LimsTaskStatus.Todo, proj: "P1"),
            T("L-2", LimsTaskStatus.Todo, proj: "P2"),
        });
        var vm = new LimsViewModel(lims) { ProjectFilter = "P2" };

        await vm.RefreshAsync(CancellationToken.None);

        vm.Todo.Should().ContainSingle().Which.Id.Should().Be("L-2");
    }

    [Fact]
    public async Task ActiveTab_DefaultsToRunning_WhenAnyRunningTaskPresent()
    {
        var lims = new FakeLims(new[]
        {
            T("L-1", LimsTaskStatus.Todo),
            T("L-2", LimsTaskStatus.Running),
        });
        var vm = new LimsViewModel(lims);

        await vm.RefreshAsync(CancellationToken.None);

        vm.ActiveTab.Should().Be(LimsTab.Running);
    }

    [Fact]
    public async Task ActiveTab_DefaultsToTodo_WhenNoRunningTasks()
    {
        var lims = new FakeLims(new[] { T("L-1", LimsTaskStatus.Todo) });
        var vm = new LimsViewModel(lims);

        await vm.RefreshAsync(CancellationToken.None);

        vm.ActiveTab.Should().Be(LimsTab.Todo);
    }
}
```

- [ ] **Step 5.2: Run, confirm failure**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~LimsViewModelTests"
```

Expected output: compile failure — `LimsViewModel` does not exist.

- [ ] **Step 5.3: Implement `LimsViewModel`**

Create `src/SiemensS7Demo.Wpf/ViewModels/LimsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App.Lims;
using SiemensS7Demo.Domain.Lims;

namespace SiemensS7Demo.Wpf.ViewModels;

public enum LimsTab { Todo, Running, Done, Cancelled }

public sealed partial class LimsViewModel : ObservableObject
{
    private readonly ILimsClient _lims;

    public LimsViewModel(ILimsClient lims) { _lims = lims; }

    public ObservableCollection<LimsTask> Todo { get; } = new();
    public ObservableCollection<LimsTask> Running { get; } = new();
    public ObservableCollection<LimsTask> Done { get; } = new();
    public ObservableCollection<LimsTask> Cancelled { get; } = new();

    [ObservableProperty] private LimsTab _activeTab = LimsTab.Todo;
    [ObservableProperty] private string? _deviceFilter;
    [ObservableProperty] private string? _projectFilter;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _lastSyncMessage;

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct)
    {
        IsLoading = true;
        try
        {
            var filter = new LimsFilter(DeviceFilter, ProjectFilter, null);
            var all = await _lims.ListTasksAsync(filter, ct);
            Todo.Clear(); Running.Clear(); Done.Clear(); Cancelled.Clear();
            foreach (var t in all)
            {
                switch (t.Status)
                {
                    case LimsTaskStatus.Todo: Todo.Add(t); break;
                    case LimsTaskStatus.Running: Running.Add(t); break;
                    case LimsTaskStatus.Done: Done.Add(t); break;
                    case LimsTaskStatus.Cancelled: Cancelled.Add(t); break;
                }
            }
            ActiveTab = Running.Count > 0 ? LimsTab.Running : LimsTab.Todo;
            LastSyncMessage = $"Synced {all.Count} task(s) at {System.DateTime.Now:HH:mm:ss}";
        }
        finally { IsLoading = false; }
    }
}
```

- [ ] **Step 5.4: Create the view**

Create `src/SiemensS7Demo.Wpf/Views/LimsView.xaml`:

```xml
<UserControl x:Class="SiemensS7Demo.Wpf.Views.LimsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel LastChildFill="True">
        <Border DockPanel.Dock="Top" Background="{DynamicResource BrushBg2}"
                BorderBrush="{DynamicResource BrushLine1}" BorderThickness="0,0,0,1" Padding="14,8">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="LIMS 任务看板" FontSize="14"
                           Foreground="{DynamicResource BrushTxt0}"/>
                <TextBlock Margin="14,0,0,0" Text="{Binding LastSyncMessage}"
                           Foreground="{DynamicResource BrushTxt3}"/>
                <Button Margin="14,0,0,0" Content="同步" Command="{Binding RefreshCommand}"/>
                <TextBox Margin="14,0,0,0" Width="120" Text="{Binding DeviceFilter, UpdateSourceTrigger=PropertyChanged}"
                         ToolTip="按设备过滤"/>
                <TextBox Margin="6,0,0,0" Width="140" Text="{Binding ProjectFilter, UpdateSourceTrigger=PropertyChanged}"
                         ToolTip="按项目过滤"/>
            </StackPanel>
        </Border>
        <TabControl SelectedIndex="{Binding ActiveTab, Mode=TwoWay, Converter={x:Null}}">
            <TabItem Header="未开始">
                <ListBox ItemsSource="{Binding Todo}" DisplayMemberPath="Id"/>
            </TabItem>
            <TabItem Header="进行中">
                <ListBox ItemsSource="{Binding Running}" DisplayMemberPath="Id"/>
            </TabItem>
            <TabItem Header="已完成">
                <ListBox ItemsSource="{Binding Done}" DisplayMemberPath="Id"/>
            </TabItem>
            <TabItem Header="已取消">
                <ListBox ItemsSource="{Binding Cancelled}" DisplayMemberPath="Id"/>
            </TabItem>
        </TabControl>
    </DockPanel>
</UserControl>
```

Create `src/SiemensS7Demo.Wpf/Views/LimsView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace SiemensS7Demo.Wpf.Views;

public partial class LimsView : UserControl
{
    public LimsView() => InitializeComponent();
}
```

- [ ] **Step 5.5: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~LimsViewModelTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5`.

- [ ] **Step 5.6: Stage and continue**

```pwsh
git status --short
```

Expected output: shows the new VM + view + test file.

---

## Task 6 (M4.6): MQTT publisher + telemetry sampler + DPAPI protected store + plaintext-leak guard

**Files:** Create `src/SiemensS7Demo.App/Mqtt/IMqttPublisher.cs`, `MqttPublisher.cs`, `MqttPublisherOptions.cs`, `TelemetrySamplerService.cs`. Create `src/SiemensS7Demo.App/Auth/IProtectedStore.cs`, `InMemoryProtectedStore.cs`, `DpapiProtectedStore.cs`. Create `src/SiemensS7Demo.Wpf/ViewModels/MqttSettingsViewModel.cs`, `Views/MqttSettingsView.xaml`. Create tests `MqttPublisherTests.cs`, `TelemetrySamplerServiceTests.cs`, `ProtectedStoreTests.cs`, `PlaintextLeakTests.cs`, `MqttSettingsViewModelTests.cs`.

- [ ] **Step 6.1: Add NuGet refs and protected-store interface**

In `src/SiemensS7Demo.App/SiemensS7Demo.App.csproj`, add to the existing `PackageReference` group:

```xml
    <PackageReference Include="MQTTnet" Version="4.3.7.1207" />
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
```

Create `src/SiemensS7Demo.App/Auth/IProtectedStore.cs`:

```csharp
namespace SiemensS7Demo.App.Auth;

public interface IProtectedStore
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
```

Create `src/SiemensS7Demo.App/Auth/InMemoryProtectedStore.cs`:

```csharp
using System;
using System.Text;

namespace SiemensS7Demo.App.Auth;

public sealed class InMemoryProtectedStore : IProtectedStore
{
    // NOT real protection. For tests only — round-trips by base64.
    public string Protect(string plaintext) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
    public string Unprotect(string ciphertext) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext));
}
```

Create `src/SiemensS7Demo.App/Auth/DpapiProtectedStore.cs`:

```csharp
using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace SiemensS7Demo.App.Auth;

[SupportedOSPlatform("windows")]
public sealed class DpapiProtectedStore : IProtectedStore
{
    public string Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var enc = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(enc);
    }

    public string Unprotect(string ciphertext)
    {
        var enc = Convert.FromBase64String(ciphertext);
        var bytes = ProtectedData.Unprotect(enc, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
```

- [ ] **Step 6.2: Write failing protected-store tests**

Create `tests/EnviroEquipment.App.Tests/Auth/ProtectedStoreTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.App.Auth;
using Xunit;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class ProtectedStoreTests
{
    [Fact]
    public void InMemoryStore_RoundTrips()
    {
        var s = new InMemoryProtectedStore();
        s.Unprotect(s.Protect("hunter2")).Should().Be("hunter2");
    }

    [Fact]
    public void InMemoryStore_DoesNotContainPlaintext()
    {
        var s = new InMemoryProtectedStore();
        var cipher = s.Protect("hunter2");
        cipher.Should().NotContain("hunter2");
    }

    [SkippableFact]
    public void DpapiStore_RoundTripsOnWindows()
    {
        Skip.IfNot(System.OperatingSystem.IsWindows(), "DPAPI only on Windows.");
        var s = new DpapiProtectedStore();
        s.Unprotect(s.Protect("hunter2")).Should().Be("hunter2");
    }
}
```

The `[SkippableFact]` attribute requires the `Xunit.SkippableFact` package. Add to `tests/EnviroEquipment.App.Tests/EnviroEquipment.App.Tests.csproj`:

```xml
    <PackageReference Include="Xunit.SkippableFact" Version="1.4.13" />
```

- [ ] **Step 6.3: Run, confirm pass on the InMemory tests**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~ProtectedStoreTests"
```

Expected output on Windows: `Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3`. On non-Windows: `Passed!  - Failed: 0, Passed: 2, Skipped: 1, Total: 3`.

- [ ] **Step 6.4: Write failing MQTT publisher tests**

Create `src/SiemensS7Demo.App/Mqtt/MqttPublisherOptions.cs`:

```csharp
namespace SiemensS7Demo.App.Mqtt;

public sealed class MqttPublisherOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string TopicPrefix { get; set; } = "envirogw/v1";
    public string ClientId { get; set; } = "envirogw-client";
}
```

Create `src/SiemensS7Demo.App/Domain/Mqtt/MqttQos.cs` (we keep the enum in the Domain layer per scope):

Actually create `src/SiemensS7Demo.Domain/Mqtt/MqttQos.cs`:

```csharp
namespace SiemensS7Demo.Domain.Mqtt;

public enum MqttQos { AtMostOnce = 0, AtLeastOnce = 1, ExactlyOnce = 2 }
```

Create `src/SiemensS7Demo.App/Mqtt/IMqttPublisher.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Mqtt;

namespace SiemensS7Demo.App.Mqtt;

public interface IMqttPublisher : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);
    Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, MqttQos qos, CancellationToken ct);
    bool IsConnected { get; }
}
```

Create `tests/EnviroEquipment.App.Tests/Mqtt/MqttPublisherTests.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }

    private static async Task<MqttServer> StartBrokerAsync(int port)
    {
        var factory = new MqttFactory();
        var options = factory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(port).Build();
        var server = factory.CreateMqttServer(options);
        await server.StartAsync();
        return server;
    }

    [Fact]
    public async Task Publish_DeliversPayloadToSubscribedConsumer()
    {
        var port = GetFreePort();
        await using var broker = await StartBrokerAsync(port);

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
        });
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
}
```

- [ ] **Step 6.5: Implement `MqttPublisher`**

Create `src/SiemensS7Demo.App/Mqtt/MqttPublisher.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using SiemensS7Demo.Domain.Mqtt;

namespace SiemensS7Demo.App.Mqtt;

public sealed class MqttPublisher : IMqttPublisher
{
    private readonly MqttPublisherOptions _opts;
    private readonly IMqttClient _client;

    public MqttPublisher(MqttPublisherOptions opts)
    {
        _opts = opts;
        _client = new MqttFactory().CreateMqttClient();
    }

    public bool IsConnected => _client.IsConnected;

    public async Task ConnectAsync(CancellationToken ct)
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_opts.Host, _opts.Port)
            .WithClientId(_opts.ClientId)
            .WithCleanSession(true);
        if (!string.IsNullOrEmpty(_opts.Username))
            builder = builder.WithCredentials(_opts.Username, _opts.Password ?? string.Empty);
        await _client.ConnectAsync(builder.Build(), ct).ConfigureAwait(false);
    }

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, MqttQos qos, CancellationToken ct)
    {
        if (!IsConnected) await ConnectAsync(ct).ConfigureAwait(false);
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload.ToArray())
            .WithQualityOfServiceLevel((MQTTnet.Protocol.MqttQualityOfServiceLevel)(int)qos)
            .Build();
        await _client.PublishAsync(msg, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_client.IsConnected) await _client.DisconnectAsync(); } catch { }
        _client.Dispose();
    }
}
```

- [ ] **Step 6.6: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~MqttPublisherTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

- [ ] **Step 6.7: Write the plaintext-leak test**

Create `tests/EnviroEquipment.App.Tests/Mqtt/PlaintextLeakTests.cs`:

```csharp
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
        var server = factory.CreateMqttServer(factory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(port).Build());
        await server.StartAsync();
        await using var _ = server;

        var stdoutCapture = new StringWriter();
        Console.SetOut(stdoutCapture);
        var stderrCapture = new StringWriter();
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

            var pub = new MqttPublisher(new MqttPublisherOptions
            {
                Host = "127.0.0.1", Port = port,
                Username = "u",
                Password = protector.Unprotect(encrypted),
                TopicPrefix = "envirogw/v1",
                ClientId = "pub"
            });
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
            if (File.Exists(logFile)) File.Delete(logFile);
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
```

- [ ] **Step 6.8: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~PlaintextLeakTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

- [ ] **Step 6.9: Implement `TelemetrySamplerService` + test**

Create `src/SiemensS7Demo.App/Mqtt/TelemetrySamplerService.cs`:

```csharp
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Mqtt;

namespace SiemensS7Demo.App.Mqtt;

public sealed class TelemetrySamplerService : IAsyncDisposable
{
    private readonly IDeviceSessionManager _devices;
    private readonly IMqttPublisher _mqtt;
    private readonly MqttPublisherOptions _opts;
    private readonly ILogger<TelemetrySamplerService> _log;
    private readonly TimeSpan _period;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public TelemetrySamplerService(IDeviceSessionManager devices,
                                   IMqttPublisher mqtt,
                                   MqttPublisherOptions opts,
                                   ILogger<TelemetrySamplerService> log,
                                   TimeSpan? period = null)
    {
        _devices = devices;
        _mqtt = mqtt;
        _opts = opts;
        _log = log;
        _period = period ?? TimeSpan.FromSeconds(5);
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await _mqtt.ConnectAsync(ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var dev in _devices.CurrentSnapshots())
                {
                    var topic = $"{_opts.TopicPrefix}/telemetry/{dev.Id.Value}";
                    var payload = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        id = dev.Id.Value,
                        status = dev.Status.ToString(),
                        pv = dev.LastReading?.Pv,
                        sv = dev.Setpoints?.TempSetpoint,
                        at = DateTimeOffset.UtcNow
                    });
                    await _mqtt.PublishAsync(topic, payload, MqttQos.AtLeastOnce, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Telemetry tick failed."); }

            try { await Task.Delay(_period, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try { if (_loop is not null) await _loop; } catch { }
        _cts.Dispose();
    }
}
```

Note: the contract `IDeviceSessionManager.CurrentSnapshots()` is assumed to exist from Pkg 1 M1.3. If only `IObservable<Device> Devices` is exposed, augment Pkg 1's manager with a `CurrentSnapshots()` returning the latest cached state — this is a small additive change to Pkg 1 surface, not a redesign.

Create `tests/EnviroEquipment.App.Tests/Mqtt/TelemetrySamplerServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App;
using SiemensS7Demo.App.Mqtt;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Mqtt;
using Xunit;

namespace EnviroEquipment.App.Tests.Mqtt;

[Trait("Category", "Pkg4")]
public class TelemetrySamplerServiceTests
{
    private sealed class FakeMqtt : IMqttPublisher
    {
        public List<(string Topic, byte[] Payload, MqttQos Qos)> Sent { get; } = new();
        public bool IsConnected { get; private set; }
        public Task ConnectAsync(CancellationToken ct) { IsConnected = true; return Task.CompletedTask; }
        public Task PublishAsync(string topic, ReadOnlyMemory<byte> payload, MqttQos qos, CancellationToken ct)
        {
            Sent.Add((topic, payload.ToArray(), qos));
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeDevices : IDeviceSessionManager
    {
        public IReadOnlyList<Device> Devices { get; set; } = Array.Empty<Device>();
        public IObservable<Device> DeviceUpdates => System.Reactive.Linq.Observable.Empty<Device>();
        public IReadOnlyList<Device> CurrentSnapshots() => Devices;
        public Task ConnectAllAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct)
            => Task.FromResult(new DeviceWriteResult(true, null));
    }

    [Fact]
    public async Task Run_PublishesOneTelemetryPerDevicePerTick()
    {
        var devices = new FakeDevices
        {
            Devices = new[]
            {
                new Device { Id = new DeviceId("TH-01"), Bay = "A1", Type = DeviceType.Standard, Status = DeviceStatus.Run, Setpoints = new Setpoints(85, 65) },
                new Device { Id = new DeviceId("TH-02"), Bay = "A2", Type = DeviceType.Standard, Status = DeviceStatus.Run, Setpoints = new Setpoints(40, 50) },
            }
        };
        var mqtt = new FakeMqtt();
        var sampler = new TelemetrySamplerService(devices, mqtt,
            new MqttPublisherOptions { TopicPrefix = "envirogw/v1" },
            NullLogger<TelemetrySamplerService>.Instance,
            period: TimeSpan.FromMilliseconds(50));

        sampler.Start();
        await Task.Delay(180);
        await sampler.DisposeAsync();

        mqtt.Sent.Should().HaveCountGreaterThanOrEqualTo(4, "two devices over multiple ticks");
        mqtt.Sent.Should().Contain(s => s.Topic == "envirogw/v1/telemetry/TH-01");
        mqtt.Sent.Should().Contain(s => s.Topic == "envirogw/v1/telemetry/TH-02");
    }
}
```

- [ ] **Step 6.10: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~TelemetrySamplerServiceTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

- [ ] **Step 6.11: Implement `MqttSettingsViewModel` + view**

Create `src/SiemensS7Demo.Wpf/ViewModels/MqttSettingsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.App.Mqtt;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class MqttSettingsViewModel : ObservableObject
{
    private readonly IProtectedStore _protect;
    private readonly MqttPublisherOptions _opts;

    public MqttSettingsViewModel(MqttPublisherOptions opts, IProtectedStore protect)
    {
        _opts = opts;
        _protect = protect;
        _host = opts.Host;
        _port = opts.Port;
        _username = opts.Username ?? string.Empty;
        // never bind plaintext to UI; the password text-box edits a transient value only.
        _topicPrefix = opts.TopicPrefix;
        _clientId = opts.ClientId;
    }

    [ObservableProperty] private string _host;
    [ObservableProperty] private int _port;
    [ObservableProperty] private string _username;
    [ObservableProperty] private string _topicPrefix;
    [ObservableProperty] private string _clientId;
    [ObservableProperty] private string _passwordPlain = string.Empty;
    [ObservableProperty] private string? _protectedPasswordCipher;

    [RelayCommand]
    public void Apply()
    {
        _opts.Host = Host;
        _opts.Port = Port;
        _opts.Username = string.IsNullOrEmpty(Username) ? null : Username;
        _opts.TopicPrefix = TopicPrefix;
        _opts.ClientId = ClientId;
        if (!string.IsNullOrEmpty(PasswordPlain))
        {
            ProtectedPasswordCipher = _protect.Protect(PasswordPlain);
            _opts.Password = _protect.Unprotect(ProtectedPasswordCipher);
            PasswordPlain = string.Empty;  // never retain in memory after apply
        }
    }
}
```

Create `src/SiemensS7Demo.Wpf/Views/MqttSettingsView.xaml`:

```xml
<UserControl x:Class="SiemensS7Demo.Wpf.Views.MqttSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="24" MaxWidth="640">
        <TextBlock Text="MQTT 代理设置" FontSize="14"
                   Foreground="{DynamicResource BrushTxt0}" Margin="0,0,0,12"/>
        <TextBlock Text="Host"/>
        <TextBox Text="{Binding Host, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,8"/>
        <TextBlock Text="Port"/>
        <TextBox Text="{Binding Port, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,8"/>
        <TextBlock Text="Username"/>
        <TextBox Text="{Binding Username, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,8"/>
        <TextBlock Text="Password (输入后会立刻 DPAPI 加密保存; 此输入框使用后清空)"/>
        <PasswordBox x:Name="PwdBox" PasswordChanged="OnPwdChanged" Margin="0,4,0,8"/>
        <TextBlock Text="Topic prefix"/>
        <TextBox Text="{Binding TopicPrefix, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,8"/>
        <TextBlock Text="Client ID"/>
        <TextBox Text="{Binding ClientId, UpdateSourceTrigger=PropertyChanged}" Margin="0,4,0,8"/>
        <Button Content="应用并测试连接" Command="{Binding ApplyCommand}" Margin="0,12,0,0"/>
    </StackPanel>
</UserControl>
```

Create `src/SiemensS7Demo.Wpf/Views/MqttSettingsView.xaml.cs`:

```csharp
using System.Windows.Controls;
using SiemensS7Demo.Wpf.ViewModels;

namespace SiemensS7Demo.Wpf.Views;

public partial class MqttSettingsView : UserControl
{
    public MqttSettingsView() => InitializeComponent();
    private void OnPwdChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MqttSettingsViewModel vm && sender is PasswordBox pb)
            vm.PasswordPlain = pb.Password;
    }
}
```

Create `tests/EnviroEquipment.Wpf.Tests/ViewModels/MqttSettingsViewModelTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.App.Mqtt;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg4")]
public class MqttSettingsViewModelTests
{
    [Fact]
    public void Apply_EncryptsPassword_AndClearsPlaintext()
    {
        var opts = new MqttPublisherOptions();
        var protect = new InMemoryProtectedStore();
        var vm = new MqttSettingsViewModel(opts, protect)
        {
            Host = "broker.lan", Port = 8883, Username = "user",
            PasswordPlain = "S3CR3T!", TopicPrefix = "envirogw/v1", ClientId = "c"
        };

        vm.ApplyCommand.Execute(null);

        vm.PasswordPlain.Should().BeEmpty();
        vm.ProtectedPasswordCipher.Should().NotBeNullOrEmpty();
        vm.ProtectedPasswordCipher.Should().NotContain("S3CR3T!");
        protect.Unprotect(vm.ProtectedPasswordCipher!).Should().Be("S3CR3T!");
        opts.Host.Should().Be("broker.lan");
        opts.Port.Should().Be(8883);
        opts.Password.Should().Be("S3CR3T!");
    }

    [Fact]
    public void Apply_DoesNotOverwritePassword_WhenPlainTextIsEmpty()
    {
        var opts = new MqttPublisherOptions { Password = "preexisting" };
        var protect = new InMemoryProtectedStore();
        var vm = new MqttSettingsViewModel(opts, protect) { Host = "x" };

        vm.ApplyCommand.Execute(null);

        opts.Password.Should().Be("preexisting");
    }
}
```

- [ ] **Step 6.12: Run, confirm pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~MqttSettingsViewModelTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2`.

- [ ] **Step 6.13: Stage and continue**

```pwsh
git status --short
```

Expected output: lists all new Mqtt/* source + tests + the protected store files + view/VM XAML.

---

## Task 7 (M4.7): FTP uploader + backup scheduler

**Files:** Create `src/SiemensS7Demo.App/Ftp/IFtpUploader.cs`, `FluentFtpUploader.cs`, `FtpUploaderOptions.cs`, `BackupScheduler.cs`. Create `tests/EnviroEquipment.App.Tests/Ftp/FluentFtpUploaderTests.cs`.

- [ ] **Step 7.1: Add NuGet ref**

In `src/SiemensS7Demo.App/SiemensS7Demo.App.csproj`:

```xml
    <PackageReference Include="FluentFTP" Version="49.0.2" />
```

For the test FTP server, add to `tests/EnviroEquipment.App.Tests/EnviroEquipment.App.Tests.csproj`:

```xml
    <PackageReference Include="SubSonic.FtpServer" Version="2.0.6" />
```

(Any small embeddable FTP server library works; this entry is a placeholder for an in-process FTP server. If the chosen library has a different package, swap here. The test below uses the docker FTP container via skip when running locally without a server, and the docker-compose harness in M4.8 provides the real coverage.)

- [ ] **Step 7.2: Implement the interface + options**

Create `src/SiemensS7Demo.App/Ftp/FtpUploaderOptions.cs`:

```csharp
namespace SiemensS7Demo.App.Ftp;

public sealed class FtpUploaderOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 21;
    public string Username { get; set; } = "anonymous";
    public string Password { get; set; } = "anonymous@";
    public bool UsePassive { get; set; } = true;
}
```

Create `src/SiemensS7Demo.App/Ftp/IFtpUploader.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace SiemensS7Demo.App.Ftp;

public interface IFtpUploader
{
    Task UploadAsync(string localPath, string remotePath, CancellationToken ct);
}
```

Create `src/SiemensS7Demo.App/Ftp/FluentFtpUploader.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Microsoft.Extensions.Logging;

namespace SiemensS7Demo.App.Ftp;

public sealed class FluentFtpUploader : IFtpUploader
{
    private readonly FtpUploaderOptions _opts;
    private readonly ILogger<FluentFtpUploader> _log;

    public FluentFtpUploader(FtpUploaderOptions opts, ILogger<FluentFtpUploader> log)
    {
        _opts = opts;
        _log = log;
    }

    public async Task UploadAsync(string localPath, string remotePath, CancellationToken ct)
    {
        var client = new AsyncFtpClient(_opts.Host, _opts.Username, _opts.Password, _opts.Port);
        client.Config.DataConnectionType = _opts.UsePassive ? FtpDataConnectionType.PASV : FtpDataConnectionType.PORT;
        await client.AutoConnect(ct);
        try
        {
            var status = await client.UploadFile(localPath, remotePath, FtpRemoteExists.Overwrite, true,
                FtpVerify.None, token: ct);
            if (status == FtpStatus.Failed)
            {
                throw new System.IO.IOException($"FTP upload failed for {localPath} -> {remotePath}");
            }
            _log.LogInformation("FTP uploaded {Local} -> {Remote} ({Status}).", localPath, remotePath, status);
        }
        finally { await client.Disconnect(ct); }
    }
}
```

Create `src/SiemensS7Demo.App/Ftp/BackupScheduler.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SiemensS7Demo.App.Ftp;

public sealed class BackupScheduler : IAsyncDisposable
{
    private readonly IFtpUploader _ftp;
    private readonly ILogger<BackupScheduler> _log;
    private readonly TimeSpan _interval;
    private readonly Func<DateTimeOffset, (string Local, string Remote)> _selector;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public BackupScheduler(IFtpUploader ftp,
                           ILogger<BackupScheduler> log,
                           TimeSpan interval,
                           Func<DateTimeOffset, (string Local, string Remote)> selector)
    {
        _ftp = ftp;
        _log = log;
        _interval = interval;
        _selector = selector;
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task UploadNowAsync(CancellationToken ct)
    {
        var (local, remote) = _selector(DateTimeOffset.Now);
        if (!File.Exists(local))
        {
            _log.LogWarning("Backup source missing: {Local}", local);
            return;
        }
        await _ftp.UploadAsync(local, remote, ct);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await UploadNowAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Scheduled backup failed."); }
            try { await Task.Delay(_interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try { if (_loop is not null) await _loop; } catch { }
        _cts.Dispose();
    }
}
```

- [ ] **Step 7.3: Write the FTP uploader test (gated on a live test server)**

Create `tests/EnviroEquipment.App.Tests/Ftp/FluentFtpUploaderTests.cs`:

```csharp
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App.Ftp;
using Xunit;

namespace EnviroEquipment.App.Tests.Ftp;

[Trait("Category", "Pkg4")]
public class FluentFtpUploaderTests
{
    private static bool TryConnect(string host, int port)
    {
        try
        {
            using var c = new TcpClient();
            var task = c.ConnectAsync(host, port);
            return task.Wait(TimeSpan.FromMilliseconds(500)) && c.Connected;
        }
        catch { return false; }
    }

    // The acceptance harness in M4.8 spins this server up via docker-compose.
    // Locally, if FTP_TEST_HOST is set and reachable, run the test; otherwise skip.
    public static bool ServerAvailable()
    {
        var host = Environment.GetEnvironmentVariable("FTP_TEST_HOST") ?? "127.0.0.1";
        var port = int.TryParse(Environment.GetEnvironmentVariable("FTP_TEST_PORT"), out var p) ? p : 2121;
        return TryConnect(host, port);
    }

    [SkippableFact]
    public async Task Upload_PutsFileOnServer()
    {
        Skip.IfNot(ServerAvailable(), "No reachable FTP test server. (Run under M4.8 compose.)");
        var host = Environment.GetEnvironmentVariable("FTP_TEST_HOST") ?? "127.0.0.1";
        var port = int.TryParse(Environment.GetEnvironmentVariable("FTP_TEST_PORT"), out var p) ? p : 2121;
        var user = Environment.GetEnvironmentVariable("FTP_TEST_USER") ?? "envirotest";
        var pwd = Environment.GetEnvironmentVariable("FTP_TEST_PASS") ?? "envirotest";

        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, "{\"hello\":\"world\"}");
        try
        {
            var uploader = new FluentFtpUploader(new FtpUploaderOptions
            {
                Host = host, Port = port, Username = user, Password = pwd
            }, NullLogger<FluentFtpUploader>.Instance);

            var act = async () => await uploader.UploadAsync(tmp, "/upload/test.json", CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
        finally { File.Delete(tmp); }
    }
}
```

- [ ] **Step 7.4: Run, confirm skip is clean locally**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "FullyQualifiedName~FluentFtpUploaderTests"
```

Expected output without compose running: `Skipped:1, Total:1`. With the compose harness from M4.8 running and `FTP_TEST_HOST=127.0.0.1 FTP_TEST_PORT=2121` exported: `Passed:1, Total:1`.

- [ ] **Step 7.5: Stage and continue**

```pwsh
git status --short
```

Expected output: lists Ftp/* files, the test, csproj edits.

---

## Task 8 (M4.8): Docker compose + acceptance smoke `--headless-smoke=auth`

**Files:** Create `tests/EnviroEquipment.E2ETests/Pkg4/compose.yml`, `mosquitto.conf`. Create `LoginAndLimsTests.cs`, `TelemetryUplinkTests.cs`. Modify `src/SiemensS7Demo.Wpf/App.xaml.cs` to add a `--headless-smoke=auth` branch.

- [ ] **Step 8.1: Create the compose stack**

Create `tests/EnviroEquipment.E2ETests/Pkg4/compose.yml` (exact path is what the spec references):

```yaml
# tests/EnviroEquipment.E2ETests/Pkg4/compose.yml
services:
  mosquitto:
    image: eclipse-mosquitto:2.0
    container_name: enviro-pkg4-mqtt
    ports:
      - "21883:1883"
    volumes:
      - ./mosquitto.conf:/mosquitto/config/mosquitto.conf:ro
    restart: unless-stopped

  ftp:
    image: delfer/alpine-ftp-server:latest
    container_name: enviro-pkg4-ftp
    environment:
      USERS: "envirotest|envirotest|/home/envirotest"
      ADDRESS: "127.0.0.1"
      MIN_PORT: 21000
      MAX_PORT: 21010
    ports:
      - "2121:21"
      - "21000-21010:21000-21010"
    restart: unless-stopped
```

Create `tests/EnviroEquipment.E2ETests/Pkg4/mosquitto.conf`:

```
listener 1883 0.0.0.0
allow_anonymous true
persistence false
log_dest stdout
log_type error
log_type warning
log_type notice
```

- [ ] **Step 8.2: Add the headless smoke entry point**

Modify `src/SiemensS7Demo.Wpf/App.xaml.cs`. Add a parsing branch for `--headless-smoke=auth` before constructing the `MainWindow`:

```csharp
protected override async void OnStartup(System.Windows.StartupEventArgs e)
{
    base.OnStartup(e);
    var args = e.Args;
    var smoke = args.FirstOrDefault(a => a.StartsWith("--headless-smoke=", System.StringComparison.OrdinalIgnoreCase));
    if (smoke is not null)
    {
        var mode = smoke.Substring("--headless-smoke=".Length);
        var exit = await SmokeRunner.RunAsync(mode, _host!.Services);
        Shutdown(exit);
        return;
    }
    var window = _host!.Services.GetRequiredService<MainWindow>();
    window.Show();
}
```

Create `src/SiemensS7Demo.Wpf/SmokeRunner.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.App.Lims;
using SiemensS7Demo.App.Mqtt;
using SiemensS7Demo.App.Ftp;
using SiemensS7Demo.Domain.Lims;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.Wpf;

internal static class SmokeRunner
{
    public static async Task<int> RunAsync(string mode, IServiceProvider sp)
    {
        switch (mode)
        {
            case "auth": return await RunAuthAsync(sp);
            default:
                Console.Error.WriteLine($"Unknown smoke mode: {mode}");
                return 2;
        }
    }

    private static async Task<int> RunAuthAsync(IServiceProvider sp)
    {
        var auth = sp.GetRequiredService<IAuthService>();
        var lims = sp.GetRequiredService<ILimsClient>();
        var mqtt = sp.GetRequiredService<IMqttPublisher>();
        var ftp = sp.GetRequiredService<IFtpUploader>();

        // 1. Login as Admin
        var shift = Shift.ForLocalNow();
        var login = await auth.SignInAsync("AD-0001", "admin", shift, CancellationToken.None);
        if (!login.Success)
        {
            Console.Error.WriteLine($"Auth login failed: {login.ErrorMessage}");
            return 10;
        }

        // 2. LIMS list
        var tasks = await lims.ListTasksAsync(new LimsFilter(null, null, null), CancellationToken.None);
        if (tasks.Count < 1)
        {
            Console.Error.WriteLine("LIMS returned no tasks.");
            return 11;
        }

        // 3. MQTT publish
        await mqtt.ConnectAsync(CancellationToken.None);
        await mqtt.PublishAsync("envirogw/v1/smoke/auth",
            System.Text.Encoding.UTF8.GetBytes("{\"ok\":true}"),
            SiemensS7Demo.Domain.Mqtt.MqttQos.AtLeastOnce, CancellationToken.None);

        // 4. FTP put
        var tmp = System.IO.Path.GetTempFileName();
        await System.IO.File.WriteAllTextAsync(tmp, "smoke");
        try { await ftp.UploadAsync(tmp, "/upload/smoke.txt", CancellationToken.None); }
        catch (Exception ex) { Console.Error.WriteLine("FTP smoke failed: " + ex.Message); return 12; }
        finally { System.IO.File.Delete(tmp); }

        Console.Out.WriteLine("PASS auth smoke");
        return 0;
    }
}
```

- [ ] **Step 8.3: Write the E2E auth + LIMS test**

Create `tests/EnviroEquipment.E2ETests/Pkg4/LoginAndLimsTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.App.Lims;
using SiemensS7Demo.Domain.Lims;
using SiemensS7Demo.Domain.Users;
using SiemensS7Demo.Persistence;
using SiemensS7Demo.Persistence.Entities;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.E2ETests.Pkg4;

[Trait("Category", "Pkg4")]
public class LoginAndLimsTests
{
    private static (EnviroDbContext db, AuthService auth) MakeAuth()
    {
        var opts = new DbContextOptionsBuilder<EnviroDbContext>().UseSqlite("DataSource=:memory:").Options;
        var ctx = new EnviroDbContext(opts);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        var hasher = new PasswordHasher();
        ctx.Users.AddRange(
            new UserEntity { Id="u-op", Code="OP-1", Name="Op", Role=Role.Operator, PasswordHash=hasher.Hash("pw") },
            new UserEntity { Id="u-ad", Code="AD-1", Name="Ad", Role=Role.Admin, PasswordHash=hasher.Hash("pw") });
        ctx.SaveChanges();
        return (ctx, new AuthService(ctx, hasher, NullLogger<AuthService>.Instance));
    }

    [Fact]
    public async Task Operator_CanNotInvokeEngineerCommand_Admin_Can()
    {
        var (db, auth) = MakeAuth();
        using var _ = db;

        await auth.SignInAsync("OP-1", "pw", Shift.ForLocalNow(), CancellationToken.None);
        RbacGuard.IsAllowed(auth.Current, typeof(SingleDeviceViewModel)
            .GetMethod(nameof(SingleDeviceViewModel.StopExperimentAsync))!)
            .Should().BeFalse();

        auth.SignOut();
        await auth.SignInAsync("AD-1", "pw", Shift.ForLocalNow(), CancellationToken.None);
        RbacGuard.IsAllowed(auth.Current, typeof(SingleDeviceViewModel)
            .GetMethod(nameof(SingleDeviceViewModel.StopExperimentAsync))!)
            .Should().BeTrue();
    }

    [Fact]
    public async Task LimsView_RendersThreeTasksFromMock()
    {
        await using var server = EnviroEquipment.App.Tests.Lims.LimsMockServer.Start(new[]
        {
            new LimsTask("L-1","TH-01","P","n", System.DateTimeOffset.UtcNow, System.DateTimeOffset.UtcNow.AddHours(1), null, null, LimsTaskStatus.Todo),
            new LimsTask("L-2","TH-01","P","n", System.DateTimeOffset.UtcNow, System.DateTimeOffset.UtcNow.AddHours(1), null, null, LimsTaskStatus.Running),
            new LimsTask("L-3","TH-01","P","n", System.DateTimeOffset.UtcNow, System.DateTimeOffset.UtcNow.AddHours(1), null, null, LimsTaskStatus.Done),
        });

        var client = new HttpLimsClient(new System.Net.Http.HttpClient { BaseAddress = server.BaseUri },
            new LimsClientOptions { Mode = LimsClientMode.Http, BaseUrl = server.BaseUri.ToString() });
        var vm = new LimsViewModel(client);

        await vm.RefreshAsync(CancellationToken.None);

        vm.Todo.Should().HaveCount(1);
        vm.Running.Should().HaveCount(1);
        vm.Done.Should().HaveCount(1);
    }
}
```

(The reference `EnviroEquipment.App.Tests.Lims.LimsMockServer` requires the E2E project to add a `ProjectReference` to `EnviroEquipment.App.Tests` OR the mock server can be moved into a shared `EnviroEquipment.TestSupport` project. The simplest first step is to add the reference.)

In `tests/EnviroEquipment.E2ETests/EnviroEquipment.E2ETests.csproj`, add:

```xml
    <ProjectReference Include="..\EnviroEquipment.App.Tests\EnviroEquipment.App.Tests.csproj" />
```

- [ ] **Step 8.4: Write the E2E telemetry uplink test**

Create `tests/EnviroEquipment.E2ETests/Pkg4/TelemetryUplinkTests.cs`:

```csharp
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MQTTnet;
using MQTTnet.Client;
using SiemensS7Demo.App.Mqtt;
using SiemensS7Demo.Domain.Mqtt;
using Xunit;
using Xunit.Abstractions;

namespace EnviroEquipment.E2ETests.Pkg4;

[Trait("Category", "Pkg4")]
public class TelemetryUplinkTests
{
    private readonly ITestOutputHelper _output;
    public TelemetryUplinkTests(ITestOutputHelper output) { _output = output; }

    private static bool BrokerReachable(string host, int port)
    {
        try
        {
            using var c = new TcpClient();
            return c.ConnectAsync(host, port).Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { return false; }
    }

    [SkippableFact]
    public async Task MqttPublish_DeliversToDockerizedMosquitto()
    {
        var host = Environment.GetEnvironmentVariable("MQTT_TEST_HOST") ?? "127.0.0.1";
        var port = int.TryParse(Environment.GetEnvironmentVariable("MQTT_TEST_PORT"), out var p) ? p : 21883;
        Skip.IfNot(BrokerReachable(host, port), "compose harness not up. Run `docker compose -f tests/EnviroEquipment.E2ETests/Pkg4/compose.yml up -d`.");

        var received = new System.Collections.Concurrent.BlockingCollection<string>();
        using var sub = new MqttFactory().CreateMqttClient();
        await sub.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(host, port).WithClientId("e2e-sub").Build());
        sub.ApplicationMessageReceivedAsync += e =>
        {
            received.Add(e.ApplicationMessage.Topic);
            return Task.CompletedTask;
        };
        await sub.SubscribeAsync("envirogw/v1/#");

        await using var pub = new MqttPublisher(new MqttPublisherOptions
        {
            Host = host, Port = port, ClientId = "e2e-pub", TopicPrefix = "envirogw/v1"
        });
        await pub.ConnectAsync(CancellationToken.None);
        await pub.PublishAsync("envirogw/v1/telemetry/E2E",
            System.Text.Encoding.UTF8.GetBytes("{\"pv\":1.0}"),
            MqttQos.AtLeastOnce, CancellationToken.None);

        received.TryTake(out var topic, TimeSpan.FromSeconds(3)).Should().BeTrue();
        topic.Should().Be("envirogw/v1/telemetry/E2E");
        await sub.DisconnectAsync();
    }
}
```

- [ ] **Step 8.5: Bring up compose**

```pwsh
docker compose -f tests/EnviroEquipment.E2ETests/Pkg4/compose.yml up -d
```

Expected output: `Container enviro-pkg4-mqtt  Started` and `Container enviro-pkg4-ftp  Started`.

- [ ] **Step 8.6: Run the headless smoke**

```pwsh
$env:MQTT_TEST_HOST = "127.0.0.1"
$env:MQTT_TEST_PORT = "21883"
$env:FTP_TEST_HOST = "127.0.0.1"
$env:FTP_TEST_PORT = "2121"
$env:FTP_TEST_USER = "envirotest"
$env:FTP_TEST_PASS = "envirotest"
dotnet run --project src/SiemensS7Demo.Wpf -- --headless-smoke=auth
```

Expected output: `PASS auth smoke` on stdout; process exit code 0.

- [ ] **Step 8.7: Run the Pkg 4 test slice**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "Category=Pkg4"
```

Expected output: `Passed!  - Failed: 0, Passed: 35+, Skipped: 0, Total: 35+`. The exact count depends on whether the FTP/MQTT broker is reachable; with the compose harness up they all pass instead of being skipped.

- [ ] **Step 8.8: Tear down compose**

```pwsh
docker compose -f tests/EnviroEquipment.E2ETests/Pkg4/compose.yml down
```

Expected output: `Container enviro-pkg4-mqtt  Removed` and `Container enviro-pkg4-ftp  Removed`.

- [ ] **Step 8.9: Stage and continue**

```pwsh
git status --short
```

Expected output: lists compose.yml, mosquitto.conf, SmokeRunner.cs, both E2E test files, and App.xaml.cs modification.

---

## Task 9: Open the PR

- [ ] **Step 9.1: Full repo build + test sweep**

```pwsh
dotnet build EnviroEquipmentFinalEdition.sln
dotnet test EnviroEquipmentFinalEdition.sln --filter "Category=Pkg4"
```

Expected output (build): every project shows `Build succeeded.` with 0 errors.
Expected output (test): the Pkg 4 slice passes with 35+ tests; no errors and no unexpected skips with the compose harness up.

- [ ] **Step 9.2: Push and open**

```pwsh
git push -u origin feat/phase2-pkg4-login-lims-mqtt-ftp
gh pr create --title "Phase 2 Pkg 4: Login + RBAC + LIMS + MQTT + FTP" --body @'
## Summary

- Adds Argon2id-backed `AuthService`, `User`/`Role`/`Shift` domain types, EF Core migration seeding admin/eng/op users.
- Login screen reproduces 202605 3-step flow (account -> password -> shift) and defaults to the current local-time shift bucket.
- `[RequiresRole]` attribute + `RbacGuard` greys out forbidden commands at `CanExecute` time; matrix tested per (role, command) pair.
- `ILimsClient` ships an HTTP+JSON implementation against the discovered protocol plus a file-watcher fallback for the unrecoverable branch; spike output at `docs/superpowers/notes/2026-05-15-lims-protocol-findings.md`.
- `LimsView` renders 4 tabs (todo/running/done/cancelled) with device/project filters.
- `IMqttPublisher` (MQTTnet) + `TelemetrySamplerService` publish per-device telemetry every 5 seconds; broker config UI stores credentials behind `IProtectedStore` (DPAPI on Windows, base64 fake elsewhere). Plaintext-leak test asserts the password never appears in stdout/stderr/log files.
- `IFtpUploader` (FluentFTP) + `BackupScheduler` upload program JSON + day-window snapshots; tests skip without server, run green under the compose harness.
- Docker compose harness at `tests/EnviroEquipment.E2ETests/Pkg4/compose.yml` ships eclipse-mosquitto + alpine-ftp on pinned ports (21883 / 2121) for parallel local runs.

## Test plan

- [x] `dotnet build` clean
- [x] `dotnet test --filter "Category=Pkg4"` all green (compose up)
- [x] `docker compose -f tests/EnviroEquipment.E2ETests/Pkg4/compose.yml up -d` then `dotnet run --project src/SiemensS7Demo.Wpf -- --headless-smoke=auth` prints `PASS auth smoke`
- [x] RBAC matrix: Operator hidden from `StopExperimentAsync`; Engineer/Admin can invoke
- [x] LIMS mock returns 3 tasks -> `LimsViewModel.Refresh` populates 3 tabs
- [x] MQTT broker receives `envirogw/v1/telemetry/E2E`
- [x] FTP server receives `/upload/smoke.txt`
- [x] PasswordHasher round-trips 5 hashes with distinct salts; verify is constant-time
- [x] DPAPI store round-trip on Windows; skipped on other platforms
- [x] Plaintext-leak test: MQTT password never appears in stdout/stderr/log files

Depends-on: Pkg 1 M1.3 (DeviceSessionManager) and Pkg 3 M3.1 (EnviroDbContext initial migration), both already on `main`.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
'@
```

Expected output: `https://github.com/<owner>/<repo>/pull/<n>` printed to stdout.

- [ ] **Step 9.2: Report back**

Reply to lead via SendMessage with the PR URL, the test count, and links to:
- `docs/superpowers/notes/2026-05-15-lims-protocol-findings.md`
- the headless-smoke output line `PASS auth smoke`.

---

## Self-Review Checklist

- [ ] All 8 milestones M4.1–M4.8 covered by their own task with a failing test → impl → passing test sequence.
- [ ] No `TODO`, no `FIXME`, no placeholder values left in any code block. Every `<...>` placeholder is fully filled in.
- [ ] Type names match the spec exactly: `User`, `Role { Operator, Engineer, Admin }`, `Shift(Code, Name, Date)`, `AuthResult`, `RequiresRoleAttribute`, `LimsTask`, `LimsTaskStatus { Todo, Running, Done, Cancelled }`, `LimsFilter`, `LimsTaskResult`, `ILimsClient`, `IMqttPublisher`, `MqttQos { AtMostOnce, AtLeastOnce, ExactlyOnce }`, `IFtpUploader`, `IProtectedStore`.
- [ ] Every `dotnet`, `pwsh`, `docker compose` command has an explicit `Expected output:` line.
- [ ] RBAC test matrix has ≥1 test row per (Operator/Engineer/Admin) × forbidden-command pair (9 inline rows in `RbacGuardTests` plus 7 in `CommandVisibilityMatrixTests`).
- [ ] DPAPI plaintext-leak test exists in M4.6 (`PlaintextLeakTests`) and asserts the literal `SECRET-PASS-DO-NOT-LOG-9C7A` never appears in stdout, stderr, log files, or the protected-store output.
- [ ] LIMS spike fallback (file-watcher mode) explicitly described in M4.4 with `FileWatcherLimsClient` + tests for both `ListTasksAsync` and `UploadResultAsync`.
- [ ] Docker compose path is exactly `tests/EnviroEquipment.E2ETests/Pkg4/compose.yml`; ports are pinned (21883 MQTT, 2121 FTP) so two devs can run side-by-side on different ports by overriding env vars.
- [ ] No git commits issued from within tasks — the umbrella runner will commit at PR time per plan rule.
- [ ] No emojis in code or commit messages.
- [ ] All new packages (`Konscious.Security.Cryptography.Argon2`, `MQTTnet`, `FluentFTP`, `System.Security.Cryptography.ProtectedData`, `Xunit.SkippableFact`) are pinned to explicit versions.
- [ ] `DpapiProtectedStore` is annotated `[SupportedOSPlatform("windows")]` and tests gate behind `SkippableFact` for non-Windows.
- [ ] `IDeviceSessionManager.CurrentSnapshots()` augmentation noted as a tiny additive change to Pkg 1's surface — flagged in M4.6.
- [ ] `EnviroEquipment.E2ETests` references `EnviroEquipment.App.Tests` so `LimsMockServer` is shared.
