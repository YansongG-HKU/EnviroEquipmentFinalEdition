using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Models;

public class AuxiliaryFunctionTests
{
    [Fact]
    public void PairMode_SetsControlAndStateTagName()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "手动辅助功能",
            ControlTagName = "压缩机启动",
            StateTagName = "压缩机运行"
        };

        aux.Group.Should().Be("手动辅助功能");
        aux.ControlTagName.Should().Be("压缩机启动");
        aux.StateTagName.Should().Be("压缩机运行");
        aux.ProgramBitOffset.Should().BeNull();
    }

    [Fact]
    public void BitOffsetMode_SetsControlAndBitOffset()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "程序辅助功能",
            ControlTagName = "段开关量控制字",
            ProgramBitOffset = 3
        };

        aux.StateTagName.Should().BeNull();
        aux.ProgramBitOffset.Should().Be(3);
    }

    [Fact]
    public void Auxiliaries_DefaultEmptyOnDeviceDefinition()
    {
        var device = new DeviceDefinition();
        device.Auxiliaries.Should().BeEmpty();
    }

    [Fact]
    public void DeviceDefinition_CanCarryAuxiliaries()
    {
        var device = new DeviceDefinition
        {
            Auxiliaries = new System.Collections.Generic.List<AuxiliaryFunction>
            {
                new() { Group = "手动辅助功能", ControlTagName = "X", StateTagName = "Y" }
            }
        };
        device.Auxiliaries.Should().HaveCount(1);
    }
}
