using System.IO;
using FluentAssertions;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ProjectConfigLoaderAuxiliaryTests
{
    [Fact]
    public void Load_BindsAuxiliariesFromJson()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory,
            "Services", "Fixtures", "auxiliaries.project.json");
        var project = ProjectConfigLoader.Load(path);
        var device = project.Devices.Single();

        device.Auxiliaries.Should().HaveCount(2);

        var pairMode = device.Auxiliaries.Single(a => a.StateTagName != null);
        pairMode.Group.Should().Be("手动辅助功能");
        pairMode.ControlTagName.Should().Be("CompressorStart");
        pairMode.StateTagName.Should().Be("CompressorRun");
        pairMode.ProgramBitOffset.Should().BeNull();

        var bitMode = device.Auxiliaries.Single(a => a.ProgramBitOffset.HasValue);
        bitMode.Group.Should().Be("程序辅助功能");
        bitMode.ProgramBitOffset.Should().Be(3);
        bitMode.StateTagName.Should().BeNull();
    }
}
