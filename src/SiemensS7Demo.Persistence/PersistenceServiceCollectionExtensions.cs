using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SiemensS7Demo.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EnviroDbContext"/> against the SQLite file at
    /// <paramref name="sqliteFilePath"/>. The repositories themselves
    /// (<c>SqliteProgramRepository</c>, <c>SqliteHistoryRepository</c>, etc.) are
    /// registered in later tasks (M3.2 + M3.5) against the App project's interfaces.
    /// </summary>
    public static IServiceCollection AddSiemensS7DemoPersistence(
        this IServiceCollection services,
        string sqliteFilePath)
    {
        services.AddDbContext<EnviroDbContext>(opt =>
            opt.UseSqlite($"Data Source={sqliteFilePath}"));
        return services;
    }
}
