using System.IO;
using System.Linq;
using FluentAssertions;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ProjectConfigLoaderBitDerivationsTests
{
    [Fact]
    public void Load_BindsBitDerivationsFromJson()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "deviations.project.json");
        var project = ProjectConfigLoader.Load(path);
        var tag = project.Devices.Single().Tags.Single();

        tag.BitDerivations.Should().HaveCount(2);
        tag.BitDerivations[0].Name.Should().Be("HeatRunning");
        tag.BitDerivations[0].BitOffset.Should().Be(0);
        tag.BitDerivations[1].DisplayName.Should().Be("Cool Running");
    }
}
