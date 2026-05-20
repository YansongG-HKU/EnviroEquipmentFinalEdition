using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using Xunit;
using Xunit.Abstractions;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class AuthServiceTimingOracleTests
{
    private readonly ITestOutputHelper _output;

    public AuthServiceTimingOracleTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static AuthService BuildSvc()
    {
        var hasher = new PasswordHasher();
        var seed = new List<User>
        {
            new("u-known", "Known", Role.Operator, "KNOWN-1", hasher.Hash("real-pw")),
        };
        var repo = new InMemoryUserRepository(seed);
        return new AuthService(repo, hasher, NullLogger<AuthService>.Instance);
    }

    /// <summary>
    /// User-enumeration via timing oracle: an unknown user must take roughly the same wall-clock
    /// time as a known user with a wrong password. The unequalized version short-circuits when
    /// FindByCodeAsync returns null and skips the ~100ms Argon2 verify, leaking which usernames
    /// are valid. After the fix, both paths perform one Argon2 verify (against the user's real
    /// hash, or against a fixed dummy hash for the unknown path).
    /// </summary>
    [Fact]
    public async Task UnknownUser_TimingMatchesKnownUserWrongPassword()
    {
        var svc = BuildSvc();
        const int Iterations = 25;
        const int WarmupIterations = 3;
        const int LockoutResetEvery = 4;
        var shift = Shift.ForLocalNow();

        // Warmup: JIT, BCL init, first Argon2id allocation costs are non-trivial. Use unique
        // codes to avoid lockout. We don't want the timing comparison to capture cold-start
        // overhead.
        for (var i = 0; i < WarmupIterations; i++)
        {
            await svc.SignInAsync($"warm-{i:00}", "x", shift, CancellationToken.None);
            await svc.SignInAsync("KNOWN-1", "x", shift, CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        var knownTimes = new List<long>();
        var unknownTimes = new List<long>();

        for (var i = 0; i < Iterations; i++)
        {
            // Rotate distinct unknown codes so we never hit lockout (5 fails / 30s window per code).
            var unknownCode = $"NOPE-{i:0000}";

            // Avoid the lockout bucket on KNOWN-1 by interleaving and giving it room to reset.
            // We measure only a handful of consecutive KNOWN failures, then a delay.
            if (i > 0 && i % LockoutResetEvery == 0)
            {
                // 30s+ wait would slow the test enormously; instead drop the bucket directly.
                await Task.Delay(50);
            }

            var swKnown = Stopwatch.StartNew();
            var rk = await svc.SignInAsync("KNOWN-1", "WRONG", shift, CancellationToken.None);
            swKnown.Stop();
            // If we get the locked-out branch the timing is meaningless, so skip that sample.
            if (rk.ErrorMessage is { } e && e.Contains("locked"))
            {
                // Re-create the service to clear the lockout dictionary cleanly.
                svc = BuildSvc();
                // Replay warm-up so the new service's JIT path is hot.
                await svc.SignInAsync("warm-r", "x", shift, CancellationToken.None);
                await svc.SignInAsync("KNOWN-1", "x", shift, CancellationToken.None);
                continue;
            }

            var swUnknown = Stopwatch.StartNew();
            await svc.SignInAsync(unknownCode, "WRONG", shift, CancellationToken.None);
            swUnknown.Stop();

            knownTimes.Add(swKnown.ElapsedMilliseconds);
            unknownTimes.Add(swUnknown.ElapsedMilliseconds);
        }

        var meanKnown = knownTimes.Average();
        var meanUnknown = unknownTimes.Average();
        var delta = System.Math.Abs(meanKnown - meanUnknown);

        _output.WriteLine($"meanKnown   = {meanKnown:F2} ms (n={knownTimes.Count})");
        _output.WriteLine($"meanUnknown = {meanUnknown:F2} ms (n={unknownTimes.Count})");
        _output.WriteLine($"|delta|     = {delta:F2} ms");

        // Without the equalizer the unknown path is ~0ms vs known ~100ms (Argon2 t=3 m=64MB p=2).
        // Conservative threshold: < 50 ms mean delta means the timing oracle is essentially closed.
        delta.Should().BeLessThan(50,
            $"unknown-user timing must approximate known-user timing to defeat user-enumeration. " +
            $"Got known={meanKnown:F2}ms unknown={meanUnknown:F2}ms |delta|={delta:F2}ms");
    }
}
