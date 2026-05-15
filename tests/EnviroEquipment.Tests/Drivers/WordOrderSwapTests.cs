using FluentAssertions;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Drivers;

public class WordOrderSwapTests
{
    // The number 1.0f in IEEE 754 single-precision big-endian (ABCD) is [0x3F, 0x80, 0x00, 0x00].
    private static readonly byte[] Abcd = { 0x3F, 0x80, 0x00, 0x00 };

    [Theory]
    [InlineData(WordOrder.ABCD, new byte[] { 0x3F, 0x80, 0x00, 0x00 })]
    [InlineData(WordOrder.CDAB, new byte[] { 0x00, 0x00, 0x3F, 0x80 })]
    [InlineData(WordOrder.BADC, new byte[] { 0x80, 0x3F, 0x00, 0x00 })]
    [InlineData(WordOrder.DCBA, new byte[] { 0x00, 0x00, 0x80, 0x3F })]
    public void ToWire_ProducesExpectedLayout(WordOrder order, byte[] expected)
    {
        var input = (byte[])Abcd.Clone();
        ModbusTcpAdapter.ApplyWordOrder(input, order, fromWire: false);
        input.Should().Equal(expected);
    }

    [Theory]
    [InlineData(WordOrder.ABCD)]
    [InlineData(WordOrder.CDAB)]
    [InlineData(WordOrder.BADC)]
    [InlineData(WordOrder.DCBA)]
    public void Roundtrip_ToWireAndFromWire_RestoresOriginal(WordOrder order)
    {
        var input = (byte[])Abcd.Clone();
        ModbusTcpAdapter.ApplyWordOrder(input, order, fromWire: false);
        ModbusTcpAdapter.ApplyWordOrder(input, order, fromWire: true);
        input.Should().Equal(Abcd);
    }
}
