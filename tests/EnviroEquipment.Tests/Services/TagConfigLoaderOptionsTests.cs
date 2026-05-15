using System.IO;
using System.Linq;
using FluentAssertions;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class TagConfigLoaderOptionsTests
{
    [Fact]
    public void Load_ParsesOptionChildren()
    {
        var path = Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "options.xml");
        var tags = TagConfigLoader.Load(path);

        var mode = tags.Single(t => t.Name == "Mode");
        mode.Options.Should().HaveCount(2);
        mode.Options.Should().ContainEquivalentOf(new { Value = 0L, Label = "Off" });
        mode.Options.Should().ContainEquivalentOf(new { Value = 1L, Label = "On" });

        var plain = tags.Single(t => t.Name == "Plain");
        plain.Options.Should().BeEmpty();
    }
}
