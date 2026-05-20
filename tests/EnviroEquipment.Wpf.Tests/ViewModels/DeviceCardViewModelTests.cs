using System;
using FluentAssertions;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg1")]
public class DeviceCardViewModelTests
{
    private static Device Make(
        DeviceStatus status = DeviceStatus.Run,
        double? pv = 25.0,
        double? sv = 25.0,
        double? humid = null,
        DeviceProgram? program = null)
        => new()
        {
            Id = new DeviceId("TH-01"),
            Bay = "A-01",
            Type = DeviceType.Standard,
            Status = status,
            Setpoints = new Setpoints(sv, humid is null ? null : humid + 0, null),
            LastReading = new ReadingSnapshot(DateTimeOffset.UtcNow, pv, sv, humid, null, null, null),
            Program = program ?? DeviceProgram.Empty,
        };

    [Fact]
    public void Apply_CopiesCoreAndProgramFields()
    {
        var vm = new DeviceCardViewModel();
        vm.Apply(Make(
            status: DeviceStatus.Run, pv: 85.2, sv: 85.0, humid: 65.0,
            program: new DeviceProgram("高温高湿老化 V3", 3, 8, 2, 5, 7235, 0.42)));

        vm.Id.Should().Be("TH-01");
        vm.Bay.Should().Be("A-01");
        vm.Status.Should().Be(DeviceStatus.Run);
        vm.Pv.Should().Be(85.2);
        vm.Sv.Should().Be(85.0);
        vm.Humidity.Should().Be(65.0);
        vm.HasHumidity.Should().BeTrue();
        vm.ProgramName.Should().Be("高温高湿老化 V3");
        vm.SegmentDisplay.Should().Be("3/8");
        vm.CycleDisplay.Should().Be("2/5");
        vm.Online.Should().BeTrue();
    }

    [Fact]
    public void NoHumidity_HidesHumidityBlock()
    {
        var vm = new DeviceCardViewModel();
        vm.Apply(Make(humid: null));
        vm.HasHumidity.Should().BeFalse();
    }

    [Fact]
    public void OfflineDevice_IsNotOnline()
    {
        var vm = new DeviceCardViewModel();
        vm.Apply(Make(status: DeviceStatus.Offline, pv: null, sv: null));
        vm.Online.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, "—")]
    [InlineData(-5, "—")]
    [InlineData(45, "00:45")]
    [InlineData(125, "02:05")]
    [InlineData(7235, "2h 00m")]   // 2h 0m 35s -> "2h 00m"
    [InlineData(41280, "11h 28m")]
    public void FormatRemain_MatchesDesignFmtDuration(int seconds, string expected)
    {
        DeviceCardViewModel.FormatRemain(seconds).Should().Be(expected);
    }

    [Fact]
    public void RemainDisplay_UsesFormatRemain()
    {
        var vm = new DeviceCardViewModel();
        vm.Apply(Make(program: new DeviceProgram(RemainSec: 125)));
        vm.RemainDisplay.Should().Be("02:05");
    }

    [Fact]
    public void SegmentAndCycle_BlankWhenNoProgram()
    {
        var vm = new DeviceCardViewModel();
        vm.Apply(Make(program: DeviceProgram.Empty));
        vm.SegmentDisplay.Should().Be("—");
        vm.CycleDisplay.Should().Be("—");
    }

    [Fact]
    public void Trend_BufferIsCappedAtCapacity()
    {
        var vm = new DeviceCardViewModel();
        for (var i = 0; i < DeviceCardViewModel.TrendCapacity + 25; i++)
        {
            vm.PushTrend(i);
        }
        vm.TrendBuffer.Count.Should().Be(DeviceCardViewModel.TrendCapacity);
        // Oldest entries dropped: the buffer holds the most recent TrendCapacity samples.
        vm.TrendBuffer[0].Should().Be(25);
        vm.TrendBuffer[^1].Should().Be(DeviceCardViewModel.TrendCapacity + 24);
    }

    [Fact]
    public void Trend_ApplyAppendsPvSample()
    {
        var vm = new DeviceCardViewModel();
        vm.Apply(Make(pv: 50.0));
        vm.Apply(Make(pv: 51.0));
        vm.TrendBuffer.Count.Should().Be(2);
        vm.TrendBuffer[0].Should().Be(50.0);
        vm.TrendBuffer[1].Should().Be(51.0);
    }

    [Fact]
    public void Trend_BuildsPolylineGeometryOnceTwoSamplesExist()
    {
        var vm = new DeviceCardViewModel();
        vm.PushTrend(10);
        vm.TrendPoints.Count.Should().Be(0, "a single point cannot form a line");
        vm.PushTrend(20);
        vm.TrendPoints.Count.Should().Be(2);
        // X spans the full sparkline viewport; Y is inverted (high value near the top).
        vm.TrendPoints[0].X.Should().Be(0);
        vm.TrendPoints[^1].X.Should().Be(DeviceCardViewModel.SparkWidth);
        vm.TrendPoints[1].Y.Should().BeLessThan(vm.TrendPoints[0].Y);
    }

    [Fact]
    public void HasAlarm_TrueOnlyForAlarmStatusWithMessage()
    {
        var alarm = new DeviceCardViewModel();
        alarm.Apply(Make(status: DeviceStatus.Alarm,
            program: new DeviceProgram(AlarmCode: "E-1108", AlarmMessage: "温度上限超限")));
        alarm.HasAlarm.Should().BeTrue();

        var run = new DeviceCardViewModel();
        run.Apply(Make(status: DeviceStatus.Run));
        run.HasAlarm.Should().BeFalse();
    }
}
