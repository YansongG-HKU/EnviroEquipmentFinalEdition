using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Models;

public class TagDataTypeTests
{
    [Fact]
    public void TagDataType_DeclaresUInt32()
    {
        System.Enum.GetNames(typeof(TagDataType))
            .Should().Contain("UInt32");
    }
}
