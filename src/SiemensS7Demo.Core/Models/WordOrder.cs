namespace SiemensS7Demo.Models;

/// <summary>
/// 32-bit register layout for two-register Modbus values.
/// ABCD is "big-endian both bytes and words" (most Siemens / Schneider M580 default).
/// CDAB swaps the word pair (common on older Schneider Quantum).
/// BADC swaps byte pairs inside each word.
/// DCBA is fully reversed (little-endian both).
/// </summary>
public enum WordOrder
{
    ABCD,
    CDAB,
    BADC,
    DCBA
}
