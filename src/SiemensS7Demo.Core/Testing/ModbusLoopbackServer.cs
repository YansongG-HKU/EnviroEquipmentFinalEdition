using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SiemensS7Demo.Testing;

public sealed class ModbusLoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly bool[] _coils = new bool[256];
    private readonly bool[] _discreteInputs = new bool[256];
    private readonly ushort[] _holdingRegisters = new ushort[256];
    private readonly ushort[] _inputRegisters = new ushort[256];
    private readonly Task _acceptTask;

    private ModbusLoopbackServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _coils[0] = true;
        _discreteInputs[0] = true;
        _holdingRegisters[0] = 123;
        _holdingRegisters[1] = 65000;
        // Pre-seed a float at HR10/HR11 = 1.0f in ABCD layout (0x3F800000).
        _holdingRegisters[10] = 0x3F80;
        _holdingRegisters[11] = 0x0000;
        // DInt at HR12/HR13 = 100000 (0x000186A0) in ABCD layout.
        _holdingRegisters[12] = 0x0001;
        _holdingRegisters[13] = 0x86A0;
        // UInt32 at HR14/HR15 = 0xFFFFFFFE (4294967294) — exercises sign bit.
        _holdingRegisters[14] = 0xFFFF;
        _holdingRegisters[15] = 0xFFFE;
        _inputRegisters[0] = 456;

        _acceptTask = AcceptLoopAsync(_cts.Token);
    }

    public int Port { get; }

    public static ModbusLoopbackServer Start() => new();

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        try
        {
            await _acceptTask;
        }
        catch
        {
            // Test shutdown path only.
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var stream = client.GetStream();
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] header;
                try
                {
                    header = await ReadExactAsync(stream, 7, cancellationToken);
                }
                catch
                {
                    return;
                }

                var transactionId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
                var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
                var unitId = header[6];
                if (length < 2)
                {
                    return;
                }

                var pdu = await ReadExactAsync(stream, length - 1, cancellationToken);
                var responsePdu = HandlePdu(pdu);
                var response = new byte[7 + responsePdu.Length];
                BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(0, 2), transactionId);
                BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), 0);
                BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(4, 2), checked((ushort)(responsePdu.Length + 1)));
                response[6] = unitId;
                responsePdu.CopyTo(response.AsSpan(7));
                await stream.WriteAsync(response, cancellationToken);
            }
        }
    }

    private byte[] HandlePdu(byte[] pdu)
    {
        var function = pdu[0];
        return function switch
        {
            0x01 => ReadBits(function, pdu, _coils),
            0x02 => ReadBits(function, pdu, _discreteInputs),
            0x03 => ReadRegisters(function, pdu, _holdingRegisters),
            0x04 => ReadRegisters(function, pdu, _inputRegisters),
            0x05 => WriteSingleCoil(function, pdu),
            0x06 => WriteSingleRegister(function, pdu),
            0x10 => WriteMultipleRegisters(function, pdu),
            _ => new[] { (byte)(function | 0x80), (byte)0x01 }
        };
    }

    private static byte[] ReadBits(byte function, byte[] pdu, bool[] values)
    {
        var address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
        var byteCount = (count + 7) / 8;
        var response = new byte[2 + byteCount];
        response[0] = function;
        response[1] = checked((byte)byteCount);

        for (var i = 0; i < count; i++)
        {
            if (values[address + i])
            {
                response[2 + (i / 8)] |= checked((byte)(1 << (i % 8)));
            }
        }

        return response;
    }

    private static byte[] ReadRegisters(byte function, byte[] pdu, ushort[] values)
    {
        var address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
        var response = new byte[2 + count * 2];
        response[0] = function;
        response[1] = checked((byte)(count * 2));

        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2 + i * 2, 2), values[address + i]);
        }

        return response;
    }

    private byte[] WriteSingleCoil(byte function, byte[] pdu)
    {
        var address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        var value = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
        _coils[address] = value == 0xFF00;
        return pdu[..5];
    }

    private byte[] WriteSingleRegister(byte function, byte[] pdu)
    {
        var address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        var value = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
        _holdingRegisters[address] = value;
        return pdu[..5];
    }

    private byte[] WriteMultipleRegisters(byte function, byte[] pdu)
    {
        var address = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1, 2));
        var count = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3, 2));
        for (var i = 0; i < count; i++)
        {
            _holdingRegisters[address + i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(6 + i * 2, 2));
        }

        var response = new byte[5];
        response[0] = function;
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(1, 2), address);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(3, 2), count);
        return response;
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
                throw new InvalidOperationException("Connection closed.");
            }

            offset += read;
        }

        return buffer;
    }
}
