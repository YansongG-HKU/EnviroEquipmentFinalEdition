using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

/// <summary>
/// Persistence-shaped store of users. Pkg 4 ships an in-memory implementation seeded
/// from appsettings.json. Pkg 3 (EF Core / SQLite) will provide a SqliteUserRepository
/// drop-in that hits EnviroDbContext once that project lands on main.
/// </summary>
public interface IUserRepository
{
    Task<User?> FindByCodeAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<User>> ListAsync(CancellationToken ct);
}
