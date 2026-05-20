using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SiemensS7Demo.App.Logging;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

public sealed class AuthService : IAuthService
{
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromSeconds(30);
    private const int LockoutThreshold = 5;

    private readonly IUserRepository _users;
    private readonly PasswordHasher _hasher;
    private readonly ILogger<AuthService> _log;
    private readonly ConcurrentDictionary<string, FailureBucket> _failures = new(StringComparer.OrdinalIgnoreCase);

    public AuthService(IUserRepository users, PasswordHasher hasher, ILogger<AuthService> log)
    {
        _users = users;
        _hasher = hasher;
        _log = log;
    }

    public User? Current { get; private set; }
    public Shift? CurrentShift { get; private set; }

    public event EventHandler? CurrentChanged;

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

        var user = await _users.FindByCodeAsync(code, ct).ConfigureAwait(false);
        if (user is null)
        {
            RecordFailure(code);
            return AuthResult.Fail("Invalid credentials.");
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            RecordFailure(code);
            _log.LogInformation("Sign-in failed for {Code}.", code);
            return AuthResult.Fail("Invalid credentials.");
        }

        _failures.TryRemove(code, out _);
        Current = user;
        CurrentShift = shift;
        // Plaintext-leak guard: ensure neither the password we received nor the user's
        // PasswordHash sneaks into the log args. The first arg deliberately passes the
        // raw code (which is a public identifier, not a secret); the second arg passes
        // the role. If a future change adds e.g. `user.PasswordHash`, the Assert throws
        // here at the call site instead of leaking to stdout / file sinks.
        LogScrubber.Assert(("Code", code), ("Role", user.Role));
        _log.LogInformation("Sign-in succeeded: {Code} role={Role}.", code, user.Role);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return AuthResult.Ok(user);
    }

    public void SignOut()
    {
        Current = null;
        CurrentShift = null;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
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
