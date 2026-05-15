using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class TagConfigLoaderAuxiliaryTests
{
    private static string AuxFixture =>
        Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "legacy_with_auxiliaries.xml");

    [Fact]
    public void LoadLegacy_WithOut_ReturnsFiveTagsAndThreeAuxiliaries()
    {
        var tags = TagConfigLoader.LoadLegacy(AuxFixture, out var auxiliaries);

        tags.Should().HaveCount(5);
        auxiliaries.Should().HaveCount(3);
    }

    [Fact]
    public void LoadLegacy_PairMode_Auxiliary_SetsStateTagName()
    {
        TagConfigLoader.LoadLegacy(AuxFixture, out var auxiliaries);

        var compressor = auxiliaries.Single(a => a.ControlTagName == "压缩机启动");
        compressor.Group.Should().Be("手动辅助功能");
        compressor.StateTagName.Should().Be("压缩机运行");
        compressor.ProgramBitOffset.Should().BeNull();
    }

    [Fact]
    public void LoadLegacy_PairMode_Auxiliary_SecondEntry()
    {
        TagConfigLoader.LoadLegacy(AuxFixture, out var auxiliaries);

        var heater = auxiliaries.Single(a => a.ControlTagName == "加热启动");
        heater.Group.Should().Be("手动辅助功能");
        heater.StateTagName.Should().Be("加热运行");
    }

    [Fact]
    public void LoadLegacy_BitOffsetMode_Auxiliary_SetsProgramBitOffset()
    {
        TagConfigLoader.LoadLegacy(AuxFixture, out var auxiliaries);

        var segment = auxiliaries.Single(a => a.ControlTagName == "段开关量控制字");
        segment.Group.Should().Be("程序辅助功能");
        segment.ProgramBitOffset.Should().Be(3);
        segment.StateTagName.Should().BeNull();
    }

    [Fact]
    public void LoadLegacy_OriginalOverload_SkipsAuxiliaryGroups()
    {
        // The original single-argument overload still returns only TagDefinitions.
        var tags = TagConfigLoader.LoadLegacy(AuxFixture);
        tags.Should().HaveCount(5);
    }
}
