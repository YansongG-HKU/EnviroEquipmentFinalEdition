using System.IO;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class TagConfigLoaderScaleModeTests
{
    [Fact]
    public void Load_ParsesScaleModeAttribute_Divisor()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "scalemode.xml");
        var tags = TagConfigLoader.Load(path);

        var temp = tags.Single(t => t.Name == "Temp");
        temp.ScaleMode.Should().Be(ScaleMode.Divisor);
        temp.Scale.Should().Be(10.0);
    }

    [Fact]
    public void Load_DefaultsToMultiplier_WhenScaleModeAttributeAbsent()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "scalemode.xml");
        var tags = TagConfigLoader.Load(path);

        var speed = tags.Single(t => t.Name == "Speed");
        speed.ScaleMode.Should().Be(ScaleMode.Multiplier);
    }
}
