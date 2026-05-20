using System;
using FluentAssertions;
using SiemensS7Demo.Domain.Programs;
using Xunit;

namespace EnviroEquipment.App.Tests.Programs;

[Trait("Category", "Pkg3")]
public class ProgramValidatorTests
{
    private static Segment Seg(int idx, double sp = 25, double secs = 60,
                                SegmentMode mode = SegmentMode.Hold, CycleAction? cycle = null)
        => new(idx, sp, null, TimeSpan.FromSeconds(secs), mode, cycle,
               new bool[4], null);

    [Fact]
    public void Validate_Empty_NoSegments_ReturnsError()
    {
        var p = new Program { Name = "x", Segments = Array.Empty<Segment>() };
        ProgramValidator.Validate(p).Should().ContainSingle()
            .Which.Should().Contain("at least one segment");
    }

    [Fact]
    public void Validate_TooManySegments_ReturnsError()
    {
        var segs = new Segment[ProgramValidator.MaxSegments + 1];
        for (var i = 0; i < segs.Length; i++) segs[i] = Seg(i);
        var p = new Program { Name = "x", Segments = segs };
        ProgramValidator.Validate(p).Should().Contain(e => e.Contains("max is"));
    }

    [Fact]
    public void Validate_BlankName_ReturnsError()
    {
        var p = new Program { Name = "", Segments = new[] { Seg(0) } };
        ProgramValidator.Validate(p).Should().Contain(e => e.Contains("name"));
    }

    [Fact]
    public void Validate_WhitespaceName_ReturnsError()
    {
        var p = new Program { Name = "   ", Segments = new[] { Seg(0) } };
        ProgramValidator.Validate(p).Should().Contain(e => e.Contains("name"));
    }

    [Fact]
    public void Validate_ZeroDuration_ReturnsError()
    {
        var p = new Program
        {
            Name = "x",
            Segments = new[] { Seg(0, secs: 0) }
        };
        ProgramValidator.Validate(p).Should().Contain(e => e.Contains("duration"));
    }

    [Fact]
    public void Validate_NegativeDuration_ReturnsError()
    {
        var p = new Program
        {
            Name = "x",
            Segments = new[]
            {
                new Segment(0, 25, null, TimeSpan.FromSeconds(-1),
                            SegmentMode.Hold, null, new bool[4], null)
            }
        };
        ProgramValidator.Validate(p).Should().Contain(e => e.Contains("duration"));
    }

    [Fact]
    public void Validate_IndexMismatch_ReturnsError()
    {
        var p = new Program
        {
            Name = "x",
            Segments = new[] { Seg(0), Seg(5) }
        };
        ProgramValidator.Validate(p).Should().Contain(e => e.Contains("Index=5"));
    }

    [Fact]
    public void Validate_JmpTargetOutOfRange_ReturnsError()
    {
        var p = new Program
        {
            Name = "x",
            Segments = new[] { Seg(0), Seg(1, cycle: new CycleAction.JumpToCycle(9, 2)) }
        };
        ProgramValidator.Validate(p).Should().Contain(e => e.Contains("out of range"));
    }

    [Fact]
    public void Validate_JmpTargetForward_ReturnsError()
    {
        var p = new Program
        {
            Name = "x",
            Segments = new[] { Seg(0, cycle: new CycleAction.JumpToCycle(1, 2)), Seg(1) }
        };
        ProgramValidator.Validate(p).Should().Contain(e => e.Contains("earlier"));
    }

    [Fact]
    public void Validate_JmpCountZero_ReturnsError()
    {
        var p = new Program
        {
            Name = "x",
            Segments = new[] { Seg(0), Seg(1, cycle: new CycleAction.JumpToCycle(0, 0)) }
        };
        ProgramValidator.Validate(p).Should().Contain(e => e.Contains("at least 1"));
    }

    [Fact]
    public void Validate_JmpCountNegative_ReturnsError()
    {
        var p = new Program
        {
            Name = "x",
            Segments = new[] { Seg(0), Seg(1, cycle: new CycleAction.JumpToCycle(0, -3)) }
        };
        ProgramValidator.Validate(p).Should().Contain(e => e.Contains("at least 1"));
    }

    [Fact]
    public void Validate_EndCycleAction_NoErrors()
    {
        var p = new Program
        {
            Name = "x",
            Segments = new[]
            {
                Seg(0),
                Seg(1, cycle: new CycleAction.EndCycle())
            }
        };
        ProgramValidator.Validate(p).Should().BeEmpty();
    }

    [Fact]
    public void Validate_HappyPath_ReturnsEmpty()
    {
        var p = new Program
        {
            Name = "demo",
            Segments = new[]
            {
                Seg(0, mode: SegmentMode.Ramp),
                Seg(1),
                Seg(2, cycle: new CycleAction.JumpToCycle(0, 3))
            }
        };
        ProgramValidator.Validate(p).Should().BeEmpty();
    }

    [Fact]
    public void Validate_NullProgram_Throws()
    {
        Action act = () => ProgramValidator.Validate(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
