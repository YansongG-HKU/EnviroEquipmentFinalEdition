using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Drivers;

public sealed class ModbusTcpAdapter : IS7Adapter, IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private byte _unitId = 1;
    private ushort _transactionId;
    private WordOrder _wordOrder = WordOrder.ABCD;

    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(PlcConnectionOptions options, CancellationToken cancellationToken)
    {
        _unitId = options.UnitId;
        _wordOrder = options.WordOrder;
        _client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.ConnectTimeoutMs);
        await _client.ConnectAsync(options.IpAddress, options.Port, timeoutCts.Token);
        _stream = _client.GetStream();
        _stream.ReadTimeout = options.ReadTimeoutMs;
        _stream.WriteTimeout = options.WriteTimeoutMs;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _stream?.Dispose();
        _client?.Close();
        _stream = null;
        _client = null;
        return Task.CompletedTask;
    }

    public Task<PlcDeviceInfo> GetDeviceInfoAsync(PlcConnectionOptions options, CancellationToken cancellationToken)
    {
        var info = new PlcDeviceInfo
        {
            TimestampUtc = DateTime.UtcNow,
            IpAddress = options.IpAddress,
            Port = options.Port,
            Rack = 0,
            Slot = 0,
            ConnectionType = $"unit={_unitId}",
            ConfiguredCpuType = "Modbus TCP",
            ModuleTypeName = "Modbus TCP device",
            ModuleName = options.Name,
            PlcStatus = IsConnected ? "Connected" : "Disconnected"
        };
        return Task.FromResult(info);
    }

    public async Task<object> ReadRawAsync(TagDefinition tag, CancellationToken cancellationToken)
    {
        var address = ModbusAddress.Parse(tag);
        if (tag.DataType == TagDataType.Bool)
        {
            var function = address.IsDiscreteInput ? (byte)0x02 : (byte)0x01;
            var payload = await SendRequestAsync(function, address.Offset, 1, Array.Empty<byte>(), cancellationToken);
            return payload.Length >= 2 && (payload[1] & 0x01) != 0;
        }

        var registerCount = tag.DataType is TagDataType.Int16 or TagDataType.UInt16 ? 1 : 2;
        var readFunction = address.IsInputRegister ? (byte)0x04 : (byte)0x03;
        var registers = await SendRequestAsync(readFunction, address.Offset, registerCount, Array.Empty<byte>(), cancellationToken);
        if (registers.Length < 1 + registerCount * 2)
        {
            throw new InvalidOperationException($"Modbus response for '{tag.Name}' was too short.");
        }

        var bytes = registers.AsSpan(1, registerCount * 2).ToArray();
        if (tag.DataType is TagDataType.DInt or TagDataType.Real)
        {
            ApplyWordOrder(bytes, _wordOrder, fromWire: true);
        }

        return tag.DataType switch
        {
            TagDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(bytes),
            TagDataType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            TagDataType.DInt => BinaryPrimitives.ReadInt32BigEndian(bytes),
            TagDataType.Real => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes)),
            _ => throw new NotSupportedException($"Unsupported Modbus data type '{tag.DataType}'.")
        };
    }

    public async Task WriteRawAsync(TagDefinition tag, object value, CancellationToken cancellationToken)
    {
        var address = ModbusAddress.Parse(tag);
        if (tag.DataType == TagDataType.Bool)
        {
            if (!address.IsCoil)
            {
                throw new InvalidOperationException($"Bool write tag '{tag.Name}' must use a coil address.");
            }

            var bytes = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(bytes, Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? (ushort)0xFF00 : (ushort)0x0000);
            await SendRequestAsync(0x05, address.Offset, 0, bytes, cancellationToken);
            return;
        }

        if (!address.IsHoldingRegister)
        {
            throw new InvalidOperationException($"Numeric write tag '{tag.Name}' must use a holding-register address.");
        }

        if (tag.DataType == TagDataType.Int16)
        {
            var bytes = new byte[2];
            BinaryPrimitives.WriteInt16BigEndian(bytes, Convert.ToInt16(value, CultureInfo.InvariantCulture));
            await SendRequestAsync(0x06, address.Offset, 0, bytes, cancellationToken);
            return;
        }

        if (tag.DataType == TagDataType.UInt16)
        {
            var bytes = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(bytes, Convert.ToUInt16(value, CultureInfo.InvariantCulture));
            await SendRequestAsync(0x06, address.Offset, 0, bytes, cancellationToken);
            return;
        }

        var registerBytes = tag.DataType switch
        {
            TagDataType.DInt => EncodeInt32(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            TagDataType.Real => EncodeReal(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            _ => throw new NotSupportedException($"Unsupported Modbus write type '{tag.DataType}'.")
        };

        if (tag.DataType is TagDataType.DInt or TagDataType.Real)
        {
            ApplyWordOrder(registerBytes, _wordOrder, fromWire: false);
        }

        var payload = new byte[5 + registerBytes.Length];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), checked((ushort)address.Offset));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), checked((ushort)(registerBytes.Length / 2)));
        payload[4] = checked((byte)registerBytes.Length);
        registerBytes.CopyTo(payload.AsSpan(5));
        await SendRawPduAsync(0x10, payload, cancellationToken);
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Close();
    }

    /// <summary>
    /// Swap the 4 bytes of a 2-register value according to <paramref name="order"/>.
    /// `fromWire=false`: convert a canonical big-endian (ABCD) buffer into wire layout.
    /// `fromWire=true`:  convert a wire-layout buffer back into canonical big-endian.
    /// CDAB and BADC are their own inverses, so the flag is informational; ABCD and DCBA
    /// also round-trip. The flag exists so future asymmetric orders can be added cleanly.
    /// </summary>
    internal static void ApplyWordOrder(byte[] buffer, WordOrder order, bool fromWire)
    {
        if (buffer.Length != 4)
        {
            throw new ArgumentException("WordOrder swap requires exactly 4 bytes.", nameof(buffer));
        }

        _ = fromWire;
        switch (order)
        {
            case WordOrder.ABCD:
                return;
            case WordOrder.CDAB:
                (buffer[0], buffer[2]) = (buffer[2], buffer[0]);
                (buffer[1], buffer[3]) = (buffer[3], buffer[1]);
                return;
            case WordOrder.BADC:
                (buffer[0], buffer[1]) = (buffer[1], buffer[0]);
                (buffer[2], buffer[3]) = (buffer[3], buffer[2]);
                return;
            case WordOrder.DCBA:
                Array.Reverse(buffer);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(order), order, "Unsupported word order.");
        }
    }

    private async Task<byte[]> SendRequestAsync(byte function, int address, int count, byte[] valueBytes, CancellationToken cancellationToken)
    {
        if (function is 0x05 or 0x06)
        {
            var payload = new byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), checked((ushort)address));
            valueBytes.CopyTo(payload.AsSpan(2));
            return await SendRawPduAsync(function, payload, cancellationToken);
        }

        var readPayload = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(readPayload.AsSpan(0, 2), checked((ushort)address));
        BinaryPrimitives.WriteUInt16BigEndian(readPayload.AsSpan(2, 2), checked((ushort)count));
        return await SendRawPduAsync(function, readPayload, cancellationToken);
    }

    private async Task<byte[]> SendRawPduAsync(byte function, byte[] pduPayload, CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("Modbus TCP not connected.");
        var transactionId = unchecked(++_transactionId);
        var request = new byte[7 + 1 + pduPayload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), checked((ushort)(2 + pduPayload.Length)));
        request[6] = _unitId;
        request[7] = function;
        pduPayload.CopyTo(request.AsSpan(8));

        await stream.WriteAsync(request, cancellationToken);
        var header = await ReadExactAsync(stream, 7, cancellationToken);
        var responseTransactionId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
        if (responseTransactionId != transactionId)
        {
            throw new InvalidOperationException($"Modbus transaction mismatch. Expected {transactionId}, got {responseTransactionId}.");
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        var pdu = await ReadExactAsync(stream, length - 1, cancellationToken);
        var responseFunction = pdu[0];
        if ((responseFunction & 0x80) != 0)
        {
            var exception = pdu.Length > 1 ? pdu[1] : 0;
            throw new InvalidOperationException($"Modbus exception 0x{exception:X2} for function 0x{function:X2}.");
        }

        if (responseFunction != function)
        {
            throw new InvalidOperationException($"Modbus function mismatch. Expected 0x{function:X2}, got 0x{responseFunction:X2}.");
        }

        return pdu[1..];
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                throw new InvalidOperationException("Modbus TCP connection closed.");
            }
            offset += read;
        }
        return buffer;
    }

    private static byte[] EncodeInt32(int value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] EncodeReal(float value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, BitConverter.SingleToInt32Bits(value));
        return buffer;
    }
}
