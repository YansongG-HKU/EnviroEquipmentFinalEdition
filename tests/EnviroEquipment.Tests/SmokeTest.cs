using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests;

public class SmokeTest
{
    [Fact]
    public void TagDefinition_CanConstructWithRequiredFields()
    {
        var tag = new TagDefinition
        {
            Name = "TemperaturePV",
            DisplayName = "Current Temperature",
            Group = "Acquire",
            Address = "DB100.DBD10",
            DataType = TagDataType.Real,
            Unit = "C",
            Access = TagAccess.Read
        };

        tag.Name.Should().Be("TemperaturePV");
        tag.DataType.Should().Be(TagDataType.Real);
        tag.Access.Should().Be(TagAccess.Read);
        tag.Scale.Should().Be(1.0);
        tag.Offset.Should().Be(0.0);
    }
}
