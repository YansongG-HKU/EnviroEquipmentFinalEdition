using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

public sealed class AuthService : IAuthService
{
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromSeconds(30);
    private const int LockoutThreshold = 5;

    /// <summary>
    /// Constant Argon2id hash used as a dummy target when <see cref="FindByCodeAsync"/> returns
    /// null. Verifying against this in the unknown-user branch equalizes the wall-clock cost of
    /// the SignInAsync path with the known-user-wrong-password branch (~100ms Argon2id work),
    /// closing the user-enumeration timing oracle. The dummy password "::dummy::" intentionally
    /// cannot match any real account (real codes seed from configuration with their own salts).
    /// </summary>
    private static readonly string DummyHash = new PasswordHasher().Hash("::dummy::");

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
            // Equalize timing with the known-user-wrong-password branch by running a real Argon2id
            // verify against the static DummyHash. Without this, an attacker can distinguish valid
            // vs invalid usernames by measuring response latency (~100ms hit vs ~0ms miss).
            _hasher.Verify(password, DummyHash);
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

    /// <summary>
    /// Verify a credential pair without mutating <see cref="Current"/>. Shares the lockout
    /// dictionary and the timing-equalising dummy-hash branch with <see cref="SignInAsync"/>
    /// so a failed verify is indistinguishable from a failed sign-in (both to attackers measuring
    /// wall-clock cost and to the lockout window). LoginViewModel uses this in its password step
    /// to avoid the previous probe-and-release pattern that transiently set Current and ran
    /// Argon2 twice per login.
    /// </summary>
    public async Task<bool> VerifyPasswordAsync(string code, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || password is null) return false;
        if (IsLockedOut(code))
        {
            _log.LogWarning("Verify blocked: {Code} locked out.", code);
            return false;
        }

        var user = await _users.FindByCodeAsync(code, ct).ConfigureAwait(false);
        if (user is null)
        {
            // Same dummy-hash verify as SignInAsync — see Fix 3 / DummyHash for rationale.
            _hasher.Verify(password, DummyHash);
            RecordFailure(code);
            return false;
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            RecordFailure(code);
            _log.LogInformation("Verify failed for {Code}.", code);
            return false;
        }

        // Successful verify does NOT clear the lockout counter (that happens only on a real
        // SignInAsync success) and does NOT touch Current / CurrentChanged.
        return true;
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
