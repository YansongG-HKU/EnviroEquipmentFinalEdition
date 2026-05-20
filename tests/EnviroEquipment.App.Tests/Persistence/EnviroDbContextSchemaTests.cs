using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SiemensS7Demo.Persistence;
using SiemensS7Demo.Persistence.Entities;
using Xunit;

namespace EnviroEquipment.App.Tests.Persistence;

[Trait("Category", "Pkg3")]
public class EnviroDbContextSchemaTests
{
    [Fact]
    public void CreateInMemory_HasAllFourTables()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        using var ctx = h.NewContext();

        var tables = ctx.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")
            .ToList();

        tables.Should().Contain(new[] { "Programs", "HistoryPoints", "AlarmEvents", "Users" });
    }

    [Fact]
    public void CreateInMemory_HasCompositeIndexOnHistoryPoints_DeviceIdAt()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        using var ctx = h.NewContext();

        var indexes = ctx.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='HistoryPoints'")
            .ToList();

        indexes.Should().Contain("IX_HistoryPoints_DeviceId_At");
    }

    [Fact]
    public void CreateInMemory_HasCompositeIndexOnAlarmEvents_DeviceIdAt()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        using var ctx = h.NewContext();

        var indexes = ctx.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='AlarmEvents'")
            .ToList();

        indexes.Should().Contain("IX_AlarmEvents_DeviceId_At");
    }

    [Fact]
    public void Insert_ProgramRow_RoundTrips_AcrossContexts()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        using (var ctx = h.NewContext())
        {
            ctx.Programs.Add(new ProgramRow
            {
                Name = "demo",
                JsonBlob = "{\"segments\":[]}",
                UpdatedAt = new System.DateTimeOffset(2026, 5, 20, 9, 0, 0, System.TimeSpan.Zero)
            });
            ctx.SaveChanges();
        }
        using (var ctx = h.NewContext())
        {
            var loaded = ctx.Programs.Single(p => p.Name == "demo");
            loaded.JsonBlob.Should().Contain("segments");
            loaded.UpdatedAt.Year.Should().Be(2026);
        }
    }
}
