namespace SiemensS7Demo.Persistence.Entities;

/// <summary>
/// Reserved for Pkg 4. <see cref="Role"/> stored as INT (0=Operator, 1=Engineer, 2=Admin).
/// Pkg 3 ships only the table schema and the repository round-trip — no behavior, no seed.
/// M3.x will populate behaviorally.
/// </summary>
public sealed class UserRow
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public int Role { get; set; }
    public required string Code { get; set; }
    public required string PasswordHash { get; set; }
}
