using System.IO;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ProjectConfigLoaderScaleModeTests
{
    [Fact]
    public void Load_BindsScaleModeFromJson()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "scalemode.project.json");
        var project = ProjectConfigLoader.Load(path);
        var tags = project.Devices.Single().Tags;

        tags.Single(t => t.Name == "Temp").ScaleMode.Should().Be(ScaleMode.Divisor);
        tags.Single(t => t.Name == "Speed").ScaleMode.Should().Be(ScaleMode.Multiplier);
    }
}
