using System;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

public interface IAuthService
{
    User? Current { get; }
    Shift? CurrentShift { get; }

    /// <summary>
    /// Raised after <see cref="Current"/> changes (sign-in or sign-out). Subscribers should
    /// re-evaluate any role-gated UI state.
    /// </summary>
    event EventHandler? CurrentChanged;

    Task<AuthResult> SignInAsync(string code, string password, Shift shift, CancellationToken ct);

    /// <summary>
    /// Verify a credential pair WITHOUT mutating <see cref="Current"/> or raising
    /// <see cref="CurrentChanged"/>. Same lockout/timing-oracle semantics as
    /// <see cref="SignInAsync"/>: an unknown user still pays the Argon2 cost, failed verifies
    /// still count toward the lockout window. Used by the LoginViewModel to validate the password
    /// step before the user picks a shift, so the persistent session only begins when
    /// <see cref="SignInAsync"/> is called from the shift-confirm step.
    /// </summary>
    Task<bool> VerifyPasswordAsync(string code, string password, CancellationToken ct);

    void SignOut();
}
