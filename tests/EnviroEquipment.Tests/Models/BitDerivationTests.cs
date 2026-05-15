using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Models;

public class BitDerivationTests
{
    [Fact]
    public void BitDerivations_DefaultEmpty()
    {
        var tag = new TagDefinition
        {
            Name = "S", DisplayName = "S", Group = "g",
            Address = "MW0", DataType = TagDataType.UInt16, Unit = ""
        };
        tag.BitDerivations.Should().BeEmpty();
    }

    [Fact]
    public void BitDerivation_PreservesNameOffsetDisplayName()
    {
        var bd = new BitDerivation("RunStatus", 3, "Run Status");
        bd.Name.Should().Be("RunStatus");
        bd.BitOffset.Should().Be(3);
        bd.DisplayName.Should().Be("Run Status");
    }

    [Fact]
    public void BitDerivation_DisplayNameDefaultsNull()
    {
        var bd = new BitDerivation("X", 0);
        bd.DisplayName.Should().BeNull();
    }
}
