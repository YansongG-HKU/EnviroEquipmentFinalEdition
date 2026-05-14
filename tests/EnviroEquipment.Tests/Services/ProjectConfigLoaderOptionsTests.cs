using System.IO;
using System.Linq;
using FluentAssertions;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ProjectConfigLoaderOptionsTests
{
    [Fact]
    public void Load_BindsTagOptionsFromJson()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "options.project.json");
        var project = ProjectConfigLoader.Load(path);

        var tag = project.Devices.Single().Tags.Single();
        tag.Options.Should().HaveCount(2);
        tag.Options[0].Value.Should().Be(0);
        tag.Options[0].Label.Should().Be("Off");
        tag.Options[1].Value.Should().Be(1);
        tag.Options[1].Label.Should().Be("On");
    }
}
