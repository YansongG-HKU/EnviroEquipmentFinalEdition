using System;
using System.Text.RegularExpressions;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Drivers;

internal sealed record ModbusAddress(string Area, int Offset)
{
    public static ModbusAddress Parse(TagDefinition tag)
    {
        var text = tag.Address.Trim();
        var match = AddressRegex.Match(text);
        if (!match.Success)
        {
            throw new FormatException($"Unsupported Modbus address '{text}'. Examples: C0, DI0, HR0, IR0.");
        }

        var area = match.Groups["area"].Value.ToUpperInvariant();
        var offset = int.Parse(match.Groups["offset"].Value);

        if (tag.DataType == TagDataType.Bool && area is not ("C" or "COIL" or "DI"))
        {
            throw new FormatException($"Bool Modbus tag '{tag.Name}' must use C/COIL or DI address.");
        }

        if (tag.DataType != TagDataType.Bool && area is not ("HR" or "IR"))
        {
            throw new FormatException($"Numeric Modbus tag '{tag.Name}' must use HR or IR address.");
        }

        return new ModbusAddress(area, offset);
    }

    public bool IsCoil => Area is "C" or "COIL";
    public bool IsDiscreteInput => Area == "DI";
    public bool IsHoldingRegister => Area == "HR";
    public bool IsInputRegister => Area == "IR";

    private static readonly Regex AddressRegex = new(
        @"^(?<area>COIL|C|DI|HR|IR)(?<offset>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
