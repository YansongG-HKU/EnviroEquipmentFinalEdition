using System.IO;
using System.Linq;
using FluentAssertions;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class TagConfigLoaderBitDerivationsTests
{
    [Fact]
    public void Load_ParsesDeviationListChildren()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "deviations.xml");
        var tag = TagConfigLoader.Load(path).Single();

        tag.BitDerivations.Should().HaveCount(2);
        tag.BitDerivations[0].Name.Should().Be("HeatRunning");
        tag.BitDerivations[0].BitOffset.Should().Be(0);
        tag.BitDerivations[0].DisplayName.Should().BeNull();
        tag.BitDerivations[1].Name.Should().Be("CoolRunning");
        tag.BitDerivations[1].BitOffset.Should().Be(3);
        tag.BitDerivations[1].DisplayName.Should().Be("Cool Running");
    }
}
