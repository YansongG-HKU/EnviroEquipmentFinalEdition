using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using Xunit;

namespace EnviroEquipment.App.Tests.Alarms;

[Trait("Category", "Pkg2")]
public class InMemoryAlarmRepositoryTests
{
    private static readonly DeviceId D1 = new("d1");
    private static readonly DeviceId D2 = new("d2");
    private static readonly DateTimeOffset T0 = new(2026, 5, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InsertAsync_StoresEvent_AndQueryNoFilterReturnsAll()
    {
        var repo = new InMemoryAlarmRepository();
        var e1 = MakeEvent("a", D1, AlarmLevel.Warn, T0);
        var e2 = MakeEvent("b", D2, AlarmLevel.Critical, T0.AddSeconds(1));

        await repo.InsertAsync(e1, CancellationToken.None);
        await repo.InsertAsync(e2, CancellationToken.None);

        var all = await repo.QueryAsync(new AlarmFilter(null, null, null, null), CancellationToken.None);
        all.Should().HaveCount(2);
        all.Select(e => e.Id).Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public async Task QueryAsync_FiltersByDevice()
    {
        var repo = new InMemoryAlarmRepository();
        await repo.InsertAsync(MakeEvent("a", D1, AlarmLevel.Warn, T0), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("b", D2, AlarmLevel.Warn, T0), CancellationToken.None);

        var only1 = await repo.QueryAsync(new AlarmFilter(null, null, D1, null), CancellationToken.None);
        only1.Should().HaveCount(1);
        only1[0].DeviceId.Should().Be(D1);
    }

    [Fact]
    public async Task QueryAsync_FiltersByLevel()
    {
        var repo = new InMemoryAlarmRepository();
        await repo.InsertAsync(MakeEvent("a", D1, AlarmLevel.Info, T0), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("b", D1, AlarmLevel.Warn, T0), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("c", D1, AlarmLevel.Critical, T0), CancellationToken.None);

        var crit = await repo.QueryAsync(new AlarmFilter(null, null, null, AlarmLevel.Critical), CancellationToken.None);
        crit.Should().HaveCount(1);
        crit[0].Code.Should().Be("c");
    }

    [Fact]
    public async Task QueryAsync_FiltersByDateRange_InclusiveBounds()
    {
        var repo = new InMemoryAlarmRepository();
        await repo.InsertAsync(MakeEvent("a", D1, AlarmLevel.Warn, T0), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("b", D1, AlarmLevel.Warn, T0.AddMinutes(5)), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("c", D1, AlarmLevel.Warn, T0.AddMinutes(10)), CancellationToken.None);

        var mid = await repo.QueryAsync(
            new AlarmFilter(T0.AddMinutes(1), T0.AddMinutes(9), null, null),
            CancellationToken.None);

        mid.Should().HaveCount(1);
        mid[0].Code.Should().Be("b");
    }

    [Fact]
    public async Task SetAckAsync_SetsAckFlagAndPreservesOtherFields()
    {
        var repo = new InMemoryAlarmRepository();
        var original = MakeEvent("a", D1, AlarmLevel.Critical, T0);
        await repo.InsertAsync(original, CancellationToken.None);

        var ackAt = T0.AddMinutes(2);
        await repo.SetAckAsync("a", ackAt, CancellationToken.None);

        var all = await repo.QueryAsync(new AlarmFilter(null, null, null, null), CancellationToken.None);
        all.Should().HaveCount(1);
        all[0].Ack.Should().BeTrue();
        all[0].Code.Should().Be("a");
        all[0].DeviceId.Should().Be(D1);
    }

    [Fact]
    public async Task SetAckAsync_UnknownId_DoesNotThrow_AndChangesNothing()
    {
        var repo = new InMemoryAlarmRepository();
        await repo.InsertAsync(MakeEvent("a", D1, AlarmLevel.Critical, T0), CancellationToken.None);
        await repo.SetAckAsync("zzz", T0, CancellationToken.None);

        var all = await repo.QueryAsync(new AlarmFilter(null, null, null, null), CancellationToken.None);
        all[0].Ack.Should().BeFalse();
    }

    [Fact]
    public async Task QueryAsync_OrdersByAtDescending()
    {
        var repo = new InMemoryAlarmRepository();
        await repo.InsertAsync(MakeEvent("oldest", D1, AlarmLevel.Warn, T0), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("newest", D1, AlarmLevel.Warn, T0.AddMinutes(10)), CancellationToken.None);
        await repo.InsertAsync(MakeEvent("middle", D1, AlarmLevel.Warn, T0.AddMinutes(5)), CancellationToken.None);

        var all = await repo.QueryAsync(new AlarmFilter(null, null, null, null), CancellationToken.None);
        all.Select(e => e.Code).Should().Equal("newest", "middle", "oldest");
    }

    private static AlarmEvent MakeEvent(string id, DeviceId dev, AlarmLevel level, DateTimeOffset at)
        => new(id, dev, level, Code: id, Message: $"msg-{id}", At: at,
               Ack: false, Reset: false, Muted: false);
}
