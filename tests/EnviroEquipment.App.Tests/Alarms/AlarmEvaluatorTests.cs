using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Domain.Alarms;
using Xunit;

namespace EnviroEquipment.App.Tests.Alarms;

[Trait("Category", "Pkg2")]
public class AlarmEvaluatorTests
{
    private static readonly DeviceId Dev = new("dev-1");
    private static readonly DateTimeOffset T = new(2026, 5, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_NoRules_ReturnsEmpty()
    {
        var snap = MakeSnapshot(pv: 25.0);
        var result = AlarmEvaluator.Evaluate(Dev, snap, Array.Empty<AlarmRule>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_SingleMatchingRule_ReturnsCriticalEvent()
    {
        var rule = new AlarmRule(
            Code: "TEMP_HIGH",
            Level: AlarmLevel.Critical,
            Trigger: s => s.Pv.HasValue && s.Pv.Value > 80.0,
            MessageTemplate: "Temperature {Pv:F1}C exceeds 80C limit");

        var snap = MakeSnapshot(pv: 85.5);
        var result = AlarmEvaluator.Evaluate(Dev, snap, new[] { rule }).ToList();

        result.Should().HaveCount(1);
        result[0].Code.Should().Be("TEMP_HIGH");
        result[0].Level.Should().Be(AlarmLevel.Critical);
        result[0].DeviceId.Should().Be(Dev);
        result[0].Message.Should().Contain("85.5");
        result[0].Ack.Should().BeFalse();
        result[0].Reset.Should().BeFalse();
        result[0].Muted.Should().BeFalse();
        result[0].At.Should().Be(snap.At);
        result[0].Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Evaluate_NonMatchingRule_ReturnsEmpty()
    {
        var rule = new AlarmRule(
            "TEMP_HIGH", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0,
            "ignored");

        var snap = MakeSnapshot(pv: 25.0);
        AlarmEvaluator.Evaluate(Dev, snap, new[] { rule }).Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_MultipleRules_FiresAllMatching()
    {
        var ruleHi = new AlarmRule(
            "TEMP_HIGH", AlarmLevel.Critical,
            s => s.Pv.HasValue && s.Pv.Value > 80.0,
            "Over {Pv}");
        var ruleHumid = new AlarmRule(
            "HUMID_HIGH", AlarmLevel.Warn,
            s => s.Humid.HasValue && s.Humid.Value > 90.0,
            "Humid {Humid}");
        var ruleOff = new AlarmRule(
            "DEVICE_OFF", AlarmLevel.Info,
            s => !s.Pv.HasValue,
            "PV missing");

        var snap = MakeSnapshot(pv: 95.0, humid: 95.0);
        var result = AlarmEvaluator.Evaluate(Dev, snap, new[] { ruleHi, ruleHumid, ruleOff }).ToList();

        result.Should().HaveCount(2);
        result.Select(e => e.Code).Should().BeEquivalentTo(new[] { "TEMP_HIGH", "HUMID_HIGH" });
    }

    [Fact]
    public void Evaluate_RuleThrows_DoesNotCorruptOthers()
    {
        var thrower = new AlarmRule(
            "BAD", AlarmLevel.Critical,
            _ => throw new InvalidOperationException("boom"),
            "won't render");
        var good = new AlarmRule(
            "GOOD", AlarmLevel.Warn,
            _ => true,
            "ok");

        var snap = MakeSnapshot(pv: 25.0);
        var result = AlarmEvaluator.Evaluate(Dev, snap, new[] { thrower, good }).ToList();

        result.Should().HaveCount(1);
        result[0].Code.Should().Be("GOOD");
    }

    [Fact]
    public void Evaluate_MessageTemplate_RendersPvAndSvAndHumid()
    {
        var rule = new AlarmRule(
            "ALL", AlarmLevel.Warn,
            _ => true,
            "PV={Pv:F2} SV={Sv:F2} H={Humid:F1}");

        var snap = new ReadingSnapshot(T, Pv: 23.45, Sv: 25.0, Humid: 60.0, HumidSv: null, Press: null, PressSv: null);
        var result = AlarmEvaluator.Evaluate(Dev, snap, new[] { rule }).Single();

        result.Message.Should().Be("PV=23.45 SV=25.00 H=60.0");
    }

    [Fact]
    public void Evaluate_MessageTemplate_MissingFieldRendersDash()
    {
        var rule = new AlarmRule(
            "ALL", AlarmLevel.Warn, _ => true,
            "PV={Pv:F1}");

        var snap = new ReadingSnapshot(T, Pv: null, Sv: null, Humid: null, HumidSv: null, Press: null, PressSv: null);
        var result = AlarmEvaluator.Evaluate(Dev, snap, new[] { rule }).Single();

        result.Message.Should().Be("PV=-");
    }

    private static ReadingSnapshot MakeSnapshot(double? pv = null, double? sv = null,
                                                double? humid = null, double? humidSv = null)
        => new(T, pv, sv, humid, humidSv, Press: null, PressSv: null);
}
