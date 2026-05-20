using System;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SiemensS7Demo.Persistence;

/// <summary>
/// Test helpers and prod file factory. <see cref="CreateInMemory"/> returns a context bound
/// to a shared open <see cref="SqliteConnection"/> so the database survives multiple
/// <see cref="EnviroDbContext"/> instances within a single test. Caller owns the returned
/// <see cref="InMemoryHandle"/> and must dispose it to release the connection.
/// </summary>
public static class EnviroDbContextFactory
{
    public sealed class InMemoryHandle : IDisposable
    {
        public SqliteConnection Connection { get; }
        public DbContextOptions<EnviroDbContext> Options { get; }

        internal InMemoryHandle(SqliteConnection c, DbContextOptions<EnviroDbContext> opts)
        {
            Connection = c;
            Options = opts;
        }

        public EnviroDbContext NewContext() => new(Options);

        public void Dispose() => Connection.Dispose();
    }

    public static InMemoryHandle CreateInMemory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<EnviroDbContext>()
            .UseSqlite(connection)
            .Options;
        using (var ctx = new EnviroDbContext(options))
        {
            ctx.Database.EnsureCreated();
        }
        return new InMemoryHandle(connection, options);
    }

    public static DbContextOptions<EnviroDbContext> CreateFileOptions(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath must be non-empty", nameof(filePath));
        return new DbContextOptionsBuilder<EnviroDbContext>()
            .UseSqlite($"Data Source={filePath}")
            .Options;
    }
}

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations add</c>. Points at a temp file so
/// the design tooling can compile without a real connection string.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EnviroDbContext>
{
    public EnviroDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<EnviroDbContext>()
            .UseSqlite("Data Source=enviro-design.db")
            .Options;
        return new EnviroDbContext(opts);
    }
}
