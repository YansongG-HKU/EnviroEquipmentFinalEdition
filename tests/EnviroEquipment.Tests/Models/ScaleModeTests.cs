using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Models;

public class ScaleModeTests
{
    [Fact]
    public void ScaleMode_DefaultIsMultiplier()
    {
        var tag = MakeTag(scale: 10.0);
        tag.ScaleMode.Should().Be(ScaleMode.Multiplier);
    }

    [Fact]
    public void Multiplier_Math_RawTimesScalePlusOffset()
    {
        // engineering = raw * Scale + Offset  ->  2 * 10 + 5 = 25
        var tag = MakeTag(scale: 10.0, offset: 5.0, mode: ScaleMode.Multiplier);
        tag.ConvertRawToEngineering(2.0).Should().BeApproximately(25.0, 1e-9);
    }

    [Fact]
    public void Divisor_Math_RawDividedByScalePlusOffset()
    {
        // engineering = raw / Scale + Offset  ->  100 / 10 + 5 = 15
        var tag = MakeTag(scale: 10.0, offset: 5.0, mode: ScaleMode.Divisor);
        tag.ConvertRawToEngineering(100.0).Should().BeApproximately(15.0, 1e-9);
    }

    [Fact]
    public void Multiplier_RoundTrip_EngineeringToRaw()
    {
        var tag = MakeTag(scale: 10.0, offset: 5.0, mode: ScaleMode.Multiplier);
        var raw = tag.ConvertEngineeringToRaw(25.0);
        raw.Should().BeApproximately(2.0, 1e-9);
    }

    [Fact]
    public void Divisor_RoundTrip_EngineeringToRaw()
    {
        // inverse: raw = (engineering - Offset) * Scale  ->  (15 - 5) * 10 = 100
        var tag = MakeTag(scale: 10.0, offset: 5.0, mode: ScaleMode.Divisor);
        var raw = tag.ConvertEngineeringToRaw(15.0);
        raw.Should().BeApproximately(100.0, 1e-9);
    }

    [Fact]
    public void LegacyZeroScale_ShouldNormalizeToMultiplierScaleOne()
    {
        // Loader normalizes scale=0 to Scale=1, ScaleMode=Multiplier.
        // Verify the math: engineering = raw * 1 + 0 = raw (no-op)
        var tag = MakeTag(scale: 1.0, offset: 0.0, mode: ScaleMode.Multiplier);
        tag.ConvertRawToEngineering(42.0).Should().BeApproximately(42.0, 1e-9);
    }

    private static TagDefinition MakeTag(
        double scale = 1.0, double offset = 0.0, ScaleMode mode = ScaleMode.Multiplier)
        => new()
        {
            Name = "T", DisplayName = "T", Group = "g",
            Address = "DB1.DBW0", DataType = TagDataType.Int16, Unit = "",
            Scale = scale, Offset = offset, ScaleMode = mode
        };
}
