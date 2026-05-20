using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SiemensS7Demo.Persistence;
using Xunit;

namespace EnviroEquipment.App.Tests.Persistence;

[Trait("Category", "Pkg3")]
public class MigrationIdempotencyTests
{
    [Fact]
    public void Forward_ThenRevert_ThenForward_LeavesIdenticalSchema()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"enviro-migration-test-{System.Guid.NewGuid():N}.db");
        try
        {
            var opts = EnviroDbContextFactory.CreateFileOptions(dbPath);

            // Forward
            using (var ctx = new EnviroDbContext(opts))
            {
                ctx.Database.Migrate();
            }
            var afterForward = ReadSchema(opts);

            // Revert to 0 (drop all)
            using (var ctx = new EnviroDbContext(opts))
            {
                var migrator = ctx.GetService<IMigrator>();
                migrator.Migrate(Migration.InitialDatabase);
            }
            var afterRevert = ReadSchema(opts);
            afterRevert.Should().BeEmpty("revert to InitialDatabase removes all migration-created tables");

            // Forward again
            using (var ctx = new EnviroDbContext(opts))
            {
                ctx.Database.Migrate();
            }
            var afterReforward = ReadSchema(opts);

            afterReforward.Should().BeEquivalentTo(afterForward,
                "schema after forward → revert → forward must match the first forward");
        }
        finally
        {
            // Sqlite client pools connections by connection string; closing the DbContext
            // does not release the file handle. Clear the pool so File.Delete succeeds.
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    private static System.Collections.Generic.List<string> ReadSchema(DbContextOptions<EnviroDbContext> opts)
    {
        using var ctx = new EnviroDbContext(opts);
        return ctx.Database
            .SqlQueryRaw<string>(
                "SELECT type || '|' || name || '|' || tbl_name || '|' || COALESCE(sql,'') AS Value " +
                "FROM sqlite_master " +
                "WHERE name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory' " +
                "ORDER BY type, name")
            .ToList();
    }
}
