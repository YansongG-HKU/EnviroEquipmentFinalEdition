using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App;

/// <summary>
/// Hard-coded Admin <see cref="IRbacContext"/>. Pkg 4 supersedes this in the live DI graph with
/// <c>AuthBackedRbacContext</c>; <see cref="AdminRbacContext"/> is retained only for unit tests
/// (and legacy parameterless ctors) that need a pre-baked Admin context without standing up an
/// <c>IAuthService</c>. Do not register this in production hosts.
/// </summary>
public sealed class AdminRbacContext : IRbacContext
{
    public Role Current => Role.Admin;
    public bool IsAtLeast(Role minimum) => true;
}
