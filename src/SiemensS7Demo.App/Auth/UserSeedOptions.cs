using System.Collections.Generic;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

/// <summary>
/// Options bound from appsettings.json `Users:` array. Plaintext passwords are hashed at startup
/// by <see cref="AppServiceCollectionExtensions.AddPkg4Auth"/> — never persisted in plaintext.
///
/// Once Pkg 3 (EnviroDbContext) lands on main, replace this seeding with a SqliteUserRepository
/// that reads the Users table; this options binder + InMemoryUserRepository becomes a fallback.
/// </summary>
public sealed class UserSeedOptions
{
    public List<UserSeedEntry> Users { get; set; } = new();
}

public sealed class UserSeedEntry
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Role Role { get; set; }

    /// <summary>
    /// Plaintext password. After the M4.6 plaintext-leak fix, this field SHOULD be empty
    /// in <c>appsettings.json</c>; supply credentials via either the
    /// <c>SEED_PASSWORD_{CODE}</c> environment variable or via <see cref="PasswordCipher"/>.
    /// Kept here as a fallback for the headless smoke harness and unit tests.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// DPAPI-protected (base64) password. Decrypted at startup by
    /// <see cref="AppServiceCollectionExtensions.AddPkg4Auth"/> via <see cref="IProtectedStore"/>.
    /// Safe to commit alongside appsettings because the cipher is machine-scoped.
    /// </summary>
    public string? PasswordCipher { get; set; }
}
