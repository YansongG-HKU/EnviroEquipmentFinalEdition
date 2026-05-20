using System;
using System.Text.RegularExpressions;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Drivers;

internal sealed record S7Address(
    int AreaCode,
    int DbNumber,
    int ByteOffset,
    int? BitIndex)
{
    public const int AreaInput = 0x81;
    public const int AreaOutput = 0x82;
    public const int AreaMarker = 0x83;
    public const int AreaDb = 0x84;

    public static S7Address Parse(TagDefinition tag)
    {
        var address = tag.Address.Trim();
        var dbMatch = DbAddressRegex.Match(address);
        if (dbMatch.Success)
        {
            return FromMatch(
                AreaDb,
                int.Parse(dbMatch.Groups["db"].Value),
                dbMatch.Groups["kind"].Value,
                dbMatch.Groups["byte"].Value,
                dbMatch.Groups["bit"].Value,
                tag);
        }

        var areaMatch = AreaAddressRegex.Match(address);
        if (areaMatch.Success)
        {
            var areaCode = areaMatch.Groups["area"].Value.ToUpperInvariant() switch
            {
                "M" => AreaMarker,
                "I" or "E" => AreaInput,
                "Q" or "A" => AreaOutput,
                "V" => AreaDb,
                _ => throw new FormatException($"Unsupported S7 area in address '{address}'.")
            };

            return FromMatch(
                areaCode,
                areaCode == AreaDb ? 1 : 0,
                areaMatch.Groups["kind"].Value,
                areaMatch.Groups["byte"].Value,
                areaMatch.Groups["bit"].Value,
                tag);
        }

        throw new FormatException(
            $"Unsupported S7 address '{address}'. Examples: DB100.DBD10, DB100.DBX100.0, M10.0, MW20, V10.0, VW20, VD24.");
    }

    public int ByteSize(TagDataType dataType) => dataType switch
    {
        TagDataType.Bool => 1,
        TagDataType.Int16 => 2,
        TagDataType.UInt16 => 2,
        TagDataType.DInt => 4,
        TagDataType.UInt32 => 4,
        TagDataType.Real => 4,
        _ => throw new NotSupportedException($"Unsupported tag data type '{dataType}'.")
    };

    private static S7Address FromMatch(
        int areaCode,
        int dbNumber,
        string kind,
        string byteOffsetText,
        string bitIndexText,
        TagDefinition tag)
    {
        var byteOffset = int.Parse(byteOffsetText);
        int? bitIndex = string.IsNullOrWhiteSpace(bitIndexText) ? null : int.Parse(bitIndexText);

        if (tag.DataType == TagDataType.Bool && bitIndex is null)
        {
            throw new FormatException($"Bool tag '{tag.Name}' must use a bit address such as DB100.DBX10.0 or M10.0.");
        }

        if (bitIndex is < 0 or > 7)
        {
            throw new FormatException($"Bit index must be 0..7 in address '{tag.Address}'.");
        }

        if (tag.DataType != TagDataType.Bool && bitIndex is not null)
        {
            throw new FormatException($"Non-bool tag '{tag.Name}' cannot use bit address '{tag.Address}'.");
        }

        if (!string.IsNullOrEmpty(kind))
        {
            ValidateAddressKind(kind, tag);
        }

        return new S7Address(areaCode, dbNumber, byteOffset, bitIndex);
    }

    private static void ValidateAddressKind(string kind, TagDefinition tag)
    {
        var normalized = kind.ToUpperInvariant();
        var valid = tag.DataType switch
        {
            TagDataType.Bool => normalized is "X",
            TagDataType.Int16 or TagDataType.UInt16 => normalized is "W",
            TagDataType.DInt or TagDataType.UInt32 or TagDataType.Real => normalized is "D",
            _ => false
        };

        if (!valid)
        {
            throw new FormatException(
                $"Address '{tag.Address}' does not match tag '{tag.Name}' data type '{tag.DataType}'.");
        }
    }

    private static readonly Regex DbAddressRegex = new(
        @"^DB(?<db>\d+)\.DB(?<kind>[XBWD])(?<byte>\d+)(?:\.(?<bit>[0-7]))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AreaAddressRegex = new(
        @"^(?<area>[MIEQAV])(?:(?<kind>[BWD])?)(?<byte>\d+)(?:\.(?<bit>[0-7]))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
