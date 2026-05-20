using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.Domain.Programs.Abstractions;

namespace SiemensS7Demo.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EnviroDbContext"/> against the SQLite file at
    /// <paramref name="sqliteFilePath"/> and the Sqlite-backed repositories that depend on
    /// it. <see cref="SqliteProgramRepository"/> is registered as a singleton
    /// <see cref="IProgramRepository"/>; it builds a short-lived <see cref="EnviroDbContext"/>
    /// per operation via the registered <see cref="IDbContextFactory{TContext}"/>.
    /// Other repositories (<c>SqliteHistoryRepository</c>, <c>SqliteAlarmRepository</c>,
    /// <c>SqliteUserRepository</c>) are registered in later milestones (M3.5+) against the
    /// same factory.
    /// </summary>
    public static IServiceCollection AddSiemensS7DemoPersistence(
        this IServiceCollection services,
        string sqliteFilePath)
    {
        services.AddDbContextFactory<EnviroDbContext>(opt =>
            opt.UseSqlite($"Data Source={sqliteFilePath}"));

        services.AddSingleton<IProgramRepository>(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<EnviroDbContext>>();
            return new SqliteProgramRepository(factory.CreateDbContext);
        });

        return services;
    }
}
