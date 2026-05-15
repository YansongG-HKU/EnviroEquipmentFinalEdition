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

        switch (tag.DataType)
        {
            case TagDataType.Bool:
                if (area is not ("C" or "COIL" or "DI"))
                {
                    throw new FormatException($"Bool Modbus tag '{tag.Name}' must use C/COIL or DI address.");
                }
                break;

            case TagDataType.Int16:
            case TagDataType.UInt16:
                if (area is not ("HR" or "IR"))
                {
                    throw new FormatException($"16-bit Modbus tag '{tag.Name}' must use HR or IR address (got '{area}').");
                }
                break;

            case TagDataType.DInt:
                if (area != "HRD")
                {
                    throw new FormatException($"Signed 32-bit Modbus tag '{tag.Name}' must use HRD address (got '{area}').");
                }
                break;

            case TagDataType.UInt32:
                if (area != "HRDU")
                {
                    throw new FormatException($"Unsigned 32-bit Modbus tag '{tag.Name}' must use HRDU address (got '{area}').");
                }
                break;

            case TagDataType.Real:
                if (area != "HRF")
                {
                    throw new FormatException($"Float Modbus tag '{tag.Name}' must use HRF address (got '{area}').");
                }
                break;

            default:
                throw new FormatException($"Unsupported tag data type '{tag.DataType}' on Modbus.");
        }

        return new ModbusAddress(area, offset);
    }

    public bool IsCoil => Area is "C" or "COIL";
    public bool IsDiscreteInput => Area == "DI";
    public bool IsHoldingRegister => Area is "HR" or "HRF" or "HRD" or "HRDU";
    public bool IsInputRegister => Area == "IR";
    public bool IsDoubleRegister => Area is "HRD" or "HRDU";

    private static readonly Regex AddressRegex = new(
        @"^(?<area>COIL|C|DI|HRDU|HRD|HRF|HR|IR)(?<offset>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
