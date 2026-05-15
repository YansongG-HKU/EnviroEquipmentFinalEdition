using System.IO;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class TagConfigLoaderLegacyTests
{
    private static string SiemensFixture =>
        Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "legacy_siemens_sample.xml");

    private static string SchneiderFixture =>
        Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "legacy_schneider_sample.xml");

    // --- Siemens DB int16 with scale=10 ---
    [Fact]
    public void LoadLegacy_Siemens_DbInt16_ScaleDivisor10()
    {
        var tags = TagConfigLoader.LoadLegacy(SiemensFixture);
        var tag = tags.Single(t => t.Name == "温度加热T0");

        tag.Address.Should().Be("DB1.DBW336");
        tag.DataType.Should().Be(TagDataType.Int16);
        tag.Scale.Should().Be(10.0);
        tag.ScaleMode.Should().Be(ScaleMode.Divisor);
        tag.Group.Should().Be("PID参数");
    }

    // --- Siemens DB real with scale=0 → normalized to Scale=1, Multiplier ---
    [Fact]
    public void LoadLegacy_Siemens_DbReal_ZeroScaleNormalized()
    {
        var tags = TagConfigLoader.LoadLegacy(SiemensFixture);
        var tag = tags.Single(t => t.Name == "温度加热T0实际值");

        tag.Address.Should().Be("DB1.DBD340");
        tag.DataType.Should().Be(TagDataType.Real);
        tag.Scale.Should().Be(1.0);
        tag.ScaleMode.Should().Be(ScaleMode.Multiplier);
    }

    // --- Siemens V bit ---
    [Fact]
    public void LoadLegacy_Siemens_VBit_SynthesizesVAddress()
    {
        var tags = TagConfigLoader.LoadLegacy(SiemensFixture);
        var tag = tags.Single(t => t.Name == "加热运行");

        tag.Address.Should().Be("V9.6");
        tag.DataType.Should().Be(TagDataType.Bool);
        tag.ScaleMode.Should().Be(ScaleMode.Multiplier);
    }

    // --- Total tag count from Siemens fixture ---
    [Fact]
    public void LoadLegacy_Siemens_ReturnsAllThreeTags()
    {
        var tags = TagConfigLoader.LoadLegacy(SiemensFixture);
        tags.Should().HaveCount(3);
    }

    // --- Group name is carried from <ParamType GroupName="..."> ---
    [Fact]
    public void LoadLegacy_SetsGroupFromParamTypeName()
    {
        var tags = TagConfigLoader.LoadLegacy(SiemensFixture);
        tags.Single(t => t.Name == "加热运行").Group.Should().Be("状态");
    }

    // --- Schneider coil ---
    [Fact]
    public void LoadLegacy_Schneider_CoilSynthesizesCAddress()
    {
        var tags = TagConfigLoader.LoadLegacy(SchneiderFixture);
        var tag = tags.Single(t => t.Name == "压缩机启动");

        tag.Address.Should().Be("C80");
        tag.DataType.Should().Be(TagDataType.Bool);
    }

    // --- Schneider HR int16 with scale=10 ---
    [Fact]
    public void LoadLegacy_Schneider_HrInt16_ScaleDivisor10()
    {
        var tags = TagConfigLoader.LoadLegacy(SchneiderFixture);
        var tag = tags.Single(t => t.Name == "回气压力");

        tag.Address.Should().Be("HR100");
        tag.DataType.Should().Be(TagDataType.Int16);
        tag.Scale.Should().Be(10.0);
        tag.ScaleMode.Should().Be(ScaleMode.Divisor);
    }

    // --- Schneider HRF (float) with scale=0 ---
    [Fact]
    public void LoadLegacy_Schneider_HrFloat_ZeroScaleNormalized()
    {
        var tags = TagConfigLoader.LoadLegacy(SchneiderFixture);
        var tag = tags.Single(t => t.Name == "排气温度");

        tag.Address.Should().Be("HRF200");
        tag.DataType.Should().Be(TagDataType.Real);
        tag.Scale.Should().Be(1.0);
        tag.ScaleMode.Should().Be(ScaleMode.Multiplier);
    }

    // --- Schneider HRDU (uint32) ---
    [Fact]
    public void LoadLegacy_Schneider_HrduUint32()
    {
        var tags = TagConfigLoader.LoadLegacy(SchneiderFixture);
        var tag = tags.Single(t => t.Name == "总电量");

        tag.Address.Should().Be("HRDU400");
        tag.DataType.Should().Be(TagDataType.UInt32);
    }

    [Fact]
    public void LoadLegacy_Siemens_TagsPassValidationWithMockProtocol()
    {
        var tags = TagConfigLoader.LoadLegacy(SiemensFixture);
        var issues = ConfigValidationService.ValidateTags(tags, "mock", "test");
        issues.Should().NotContain(i => i.Severity == ConfigIssueSeverity.Error);
    }

    [Fact]
    public void LoadLegacy_Schneider_TagsPassValidationWithMockProtocol()
    {
        var tags = TagConfigLoader.LoadLegacy(SchneiderFixture);
        var issues = ConfigValidationService.ValidateTags(tags, "mock", "test");
        issues.Should().NotContain(i => i.Severity == ConfigIssueSeverity.Error);
    }
}
