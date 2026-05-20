using System;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

/// <summary>
/// RBAC context backed by <see cref="IAuthService"/>. Reflects the currently signed-in user's
/// role, defaulting to <see cref="Role.Operator"/> when no one is signed in
/// (principle of least privilege — never surface Admin to an unauthenticated session).
///
/// Subscribes to <see cref="IAuthService.CurrentChanged"/> once at construction and re-raises a
/// <see cref="RoleChanged"/> event so view-models can refresh <c>CanExecute</c> state without
/// holding a direct reference to <see cref="IAuthService"/>.
///
/// This replaces <see cref="AdminRbacContext"/> in the WPF host's DI graph (see
/// <see cref="AppServiceCollectionExtensions.AddSiemensS7DemoApp"/>); the AdminRbacContext
/// remains in the codebase only for tests that need a hard-coded Admin context.
/// </summary>
public sealed class AuthBackedRbacContext : IRbacContext
{
    private readonly IAuthService _auth;

    public AuthBackedRbacContext(IAuthService auth)
    {
        _auth = auth;
        _auth.CurrentChanged += OnCurrentChanged;
    }

    public Role Current => _auth.Current?.Role ?? Role.Operator;

    public bool IsAtLeast(Role minimum) => (int)Current >= (int)minimum;

    /// <summary>
    /// Raised whenever the underlying <see cref="IAuthService.CurrentChanged"/> fires (i.e. on
    /// sign-in and sign-out). Consumers should re-evaluate visibility and <c>CanExecute</c> state.
    /// </summary>
    public event EventHandler? RoleChanged;

    private void OnCurrentChanged(object? sender, EventArgs e)
        => RoleChanged?.Invoke(this, EventArgs.Empty);
}
