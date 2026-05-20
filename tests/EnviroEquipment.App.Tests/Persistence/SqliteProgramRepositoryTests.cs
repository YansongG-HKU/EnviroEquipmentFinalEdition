using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.Domain.Programs;
using SiemensS7Demo.Persistence;
using Xunit;

namespace EnviroEquipment.App.Tests.Persistence;

[Trait("Category", "Pkg3")]
public class SqliteProgramRepositoryTests
{
    private static Segment Seg(int idx, double sp = 25, double secs = 60,
                                SegmentMode mode = SegmentMode.Hold, CycleAction? cycle = null)
        => new(idx, sp, null, TimeSpan.FromSeconds(secs), mode, cycle, new bool[4], null);

    [Fact]
    public async Task SaveAsync_Then_GetAsync_RoundTrips_TheProgram()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);
        var original = new Program
        {
            Name = "demo",
            Segments = new[]
            {
                Seg(0, sp: 23, mode: SegmentMode.Ramp),
                Seg(1, sp: 85, secs: 1800, mode: SegmentMode.Hold),
                Seg(2, sp: 23, cycle: new CycleAction.JumpToCycle(0, 3))
            }
        };
        await repo.SaveAsync(original, CancellationToken.None);

        var loaded = await repo.GetAsync("demo", CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("demo");
        loaded.Segments.Should().HaveCount(3);
        loaded.Segments[1].TempSetpoint.Should().Be(85);
        loaded.Segments[1].Duration.Should().Be(TimeSpan.FromSeconds(1800));
        loaded.Segments[1].Mode.Should().Be(SegmentMode.Hold);
        loaded.Segments[2].Cycle.Should().BeOfType<CycleAction.JumpToCycle>();
        ((CycleAction.JumpToCycle)loaded.Segments[2].Cycle!).TargetIndex.Should().Be(0);
        ((CycleAction.JumpToCycle)loaded.Segments[2].Cycle!).Count.Should().Be(3);
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingProgramOfSameName()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);
        await repo.SaveAsync(new Program
        {
            Name = "demo",
            Segments = new[] { Seg(0) }
        }, CancellationToken.None);
        await repo.SaveAsync(new Program
        {
            Name = "demo",
            Segments = new[] { Seg(0, sp: 99), Seg(1, sp: 99) }
        }, CancellationToken.None);

        var loaded = await repo.GetAsync("demo", CancellationToken.None);
        loaded!.Segments.Should().HaveCount(2);
        loaded.Segments[0].TempSetpoint.Should().Be(99);
    }

    [Fact]
    public async Task SaveAsync_IsIdempotent_TwoIdenticalSavesYieldOneRow()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);
        var prog = new Program { Name = "demo", Segments = new[] { Seg(0) } };

        await repo.SaveAsync(prog, CancellationToken.None);
        await repo.SaveAsync(prog, CancellationToken.None);

        var names = await repo.ListAsync(CancellationToken.None);
        names.Should().HaveCount(1);
        names.Single().Should().Be("demo");
    }

    [Fact]
    public async Task SaveAsync_Concurrent_SameName_LeavesOneRow()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);

        var t1 = repo.SaveAsync(new Program { Name = "demo", Segments = new[] { Seg(0, sp: 1) } },
                                CancellationToken.None);
        var t2 = repo.SaveAsync(new Program { Name = "demo", Segments = new[] { Seg(0, sp: 2) } },
                                CancellationToken.None);
        await Task.WhenAll(t1, t2);

        var names = await repo.ListAsync(CancellationToken.None);
        names.Should().HaveCount(1);
        var loaded = await repo.GetAsync("demo", CancellationToken.None);
        loaded!.Segments[0].TempSetpoint.Should().BeOneOf(1, 2);
    }

    [Fact]
    public async Task GetAsync_UnknownName_ReturnsNull()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);
        (await repo.GetAsync("missing", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task SaveDraftAsync_StoredSeparatelyFromCommittedRow()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);
        await repo.SaveAsync(new Program
        {
            Name = "demo",
            Segments = new[] { Seg(0, sp: 25) }
        }, CancellationToken.None);
        await repo.SaveDraftAsync(new Program
        {
            Name = "demo",
            Segments = new[] { Seg(0, sp: 99) }
        }, CancellationToken.None);

        var committed = await repo.GetAsync("demo", CancellationToken.None);
        var draft = await repo.GetDraftAsync("demo", CancellationToken.None);

        committed!.Segments[0].TempSetpoint.Should().Be(25);
        draft!.Segments[0].TempSetpoint.Should().Be(99);
    }

    [Fact]
    public async Task GetDraftAsync_UnknownName_ReturnsNull()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);
        await repo.SaveAsync(new Program { Name = "demo", Segments = new[] { Seg(0) } },
                             CancellationToken.None);

        (await repo.GetDraftAsync("demo", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_OmitsDrafts_AndOrdersAlpha()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);
        await repo.SaveAsync(new Program { Name = "zeta", Segments = new[] { Seg(0) } }, CancellationToken.None);
        await repo.SaveAsync(new Program { Name = "alpha", Segments = new[] { Seg(0) } }, CancellationToken.None);
        await repo.SaveDraftAsync(new Program { Name = "draftedThing", Segments = new[] { Seg(0) } }, CancellationToken.None);

        var names = await repo.ListAsync(CancellationToken.None);
        names.Should().Equal("alpha", "zeta");
    }

    [Fact]
    public async Task ListAsync_Empty_Returns_EmptyList()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);

        var names = await repo.ListAsync(CancellationToken.None);
        names.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_RemovesCommittedAndDraft()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);
        await repo.SaveAsync(new Program { Name = "demo", Segments = new[] { Seg(0) } }, CancellationToken.None);
        await repo.SaveDraftAsync(new Program { Name = "demo", Segments = new[] { Seg(0) } }, CancellationToken.None);

        await repo.DeleteAsync("demo", CancellationToken.None);

        (await repo.GetAsync("demo", CancellationToken.None)).Should().BeNull();
        (await repo.GetDraftAsync("demo", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnknownName_IsNoOp()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);

        var act = async () => await repo.DeleteAsync("missing", CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveAsync_EndCycleAction_RoundTripsCorrectly()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);
        var original = new Program
        {
            Name = "endcyc",
            Segments = new[]
            {
                Seg(0),
                Seg(1, cycle: new CycleAction.EndCycle())
            }
        };
        await repo.SaveAsync(original, CancellationToken.None);

        var loaded = await repo.GetAsync("endcyc", CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Segments[1].Cycle.Should().BeOfType<CycleAction.EndCycle>();
    }

    [Fact]
    public async Task SaveAsync_PreservesDigitalOutputs_AndNote_AndHumidSetpoint()
    {
        using var h = EnviroDbContextFactory.CreateInMemory();
        var repo = new SqliteProgramRepository(h.NewContext);
        var seg = new Segment(0, 25.5, 60.5, TimeSpan.FromSeconds(120),
                              SegmentMode.Ramp, null,
                              new[] { true, false, true, true }, "hello note");
        var original = new Program { Name = "rich", Segments = new[] { seg } };

        await repo.SaveAsync(original, CancellationToken.None);
        var loaded = await repo.GetAsync("rich", CancellationToken.None);

        loaded.Should().NotBeNull();
        var s = loaded!.Segments[0];
        s.TempSetpoint.Should().Be(25.5);
        s.HumidSetpoint.Should().Be(60.5);
        s.DigitalOutputs.Should().Equal(new[] { true, false, true, true });
        s.Note.Should().Be("hello note");
    }
}
