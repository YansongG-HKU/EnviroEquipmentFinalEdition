using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

/// <summary>
/// In-memory user repository. Seeded once from a snapshot of <see cref="User"/> records — typical
/// callers build the snapshot from appsettings.json (see <see cref="AppServiceCollectionExtensions"/>)
/// and hash plaintext passwords with <see cref="PasswordHasher"/> before constructing this store.
///
/// Replaces the planned SqliteUserRepository (EnviroDbContext) until Pkg 3 M3.1 lands on main; once
/// it does, swap the registration in DI without touching <see cref="AuthService"/>.
/// </summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly Dictionary<string, User> _byCode;

    public InMemoryUserRepository(IEnumerable<User> seed)
    {
        _byCode = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in seed) _byCode[u.Code] = u;
    }

    public Task<User?> FindByCodeAsync(string code, CancellationToken ct)
    {
        _byCode.TryGetValue(code, out var user);
        return Task.FromResult<User?>(user);
    }

    public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<User>>(_byCode.Values.ToList());
}
