namespace SiemensS7Demo.App;

/// <summary>
/// Stub RBAC context used by Pkg 1. Always reports Admin. The real implementation
/// arrives with Package 4 (Auth/Login).
/// </summary>
public sealed class AdminRbacContext : IRbacContext
{
    public Role Current => Role.Admin;
    public bool IsAtLeast(Role minimum) => true;
}
