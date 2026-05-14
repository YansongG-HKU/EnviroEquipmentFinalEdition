using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Drivers;

public sealed class Snap7S7Adapter : IS7Adapter, IDisposable
{
    private const int S7WordLenByte = 0x02;
    private const int ParamRemotePort = 2;
    private const int ParamPingTimeout = 3;
    private const int ParamSendTimeout = 4;
    private const int ParamRecvTimeout = 5;
    private const ushort ConnectionTypePg = 0x0001;
    private const ushort ConnectionTypeOp = 0x0002;
    private const ushort ConnectionTypeBasic = 0x0003;

    private IntPtr _client;
    private bool _disposed;

    static Snap7S7Adapter()
    {
        Snap7NativeLibrary.RegisterResolver();
    }

    public bool IsConnected
    {
        get
        {
            if (_client == IntPtr.Zero)
            {
                return false;
            }

            var connected = 0;
            return Native.Cli_GetConnected(_client, ref connected) == 0 && connected != 0;
        }
    }

    public Task ConnectAsync(PlcConnectionOptions options, CancellationToken cancellationToken)
        => RunNativeAsync(() =>
        {
            ThrowIfDisposed();

            if (_client == IntPtr.Zero)
            {
                try
                {
                    _client = Native.Cli_Create();
                }
                catch (DllNotFoundException ex)
                {
                    throw new InvalidOperationException(
                        $"snap7.dll was not found. Run tools\\ensure-snap7.ps1 or set SNAP7_DLL. Candidate paths:{Environment.NewLine}{Snap7NativeLibrary.DescribeCandidatePaths()}",
                        ex);
                }
                catch (BadImageFormatException ex)
                {
                    throw new InvalidOperationException(
                        "snap7.dll was found but could not be loaded. Use the official win64 snap7.dll with this x64 .NET process.",
                        ex);
                }
            }

            if (_client == IntPtr.Zero)
            {
                throw new InvalidOperationException("Snap7 client could not be created.");
            }

            SetUInt16Param(ParamRemotePort, checked((ushort)options.Port));
            SetInt32Param(ParamPingTimeout, options.ConnectTimeoutMs);
            SetInt32Param(ParamSendTimeout, options.WriteTimeoutMs);
            SetInt32Param(ParamRecvTimeout, options.ReadTimeoutMs);
            SetConnectionType(options.Snap7ConnectionType);

            var result = Native.Cli_ConnectTo(_client, options.IpAddress, options.Rack, options.Slot);
            ThrowIfSnap7Error(result, $"ConnectTo {options.IpAddress}:{options.Port} rack={options.Rack} slot={options.Slot} connectionType={options.Snap7ConnectionType}");
        }, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken)
        => RunNativeAsync(() =>
        {
            if (_client != IntPtr.Zero)
            {
                Native.Cli_Disconnect(_client);
            }
        }, cancellationToken);

    public Task<PlcDeviceInfo> GetDeviceInfoAsync(PlcConnectionOptions options, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            EnsureConnected();

            var info = new PlcDeviceInfo
            {
                TimestampUtc = DateTime.UtcNow,
                IpAddress = options.IpAddress,
                Port = options.Port,
                Rack = options.Rack,
                Slot = options.Slot,
                ConnectionType = options.Snap7ConnectionType,
                ConfiguredCpuType = options.CpuType
            };

            CaptureOrderCode(info);
            CaptureCpuInfo(info);
            CaptureCpInfo(info);
            CapturePlcStatus(info);
            CaptureProtection(info);
            CaptureBlockCounts(info);

            return info;
        }, cancellationToken);

    public Task<object> ReadRawAsync(TagDefinition tag, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            var address = S7Address.Parse(tag);
            var bytes = ReadBytes(address, address.ByteSize(tag.DataType));
            return Decode(tag, address, bytes);
        }, cancellationToken);

    public Task WriteRawAsync(TagDefinition tag, object value, CancellationToken cancellationToken)
        => RunNativeAsync(() =>
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            var address = S7Address.Parse(tag);
            if (tag.DataType == TagDataType.Bool)
            {
                WriteBool(address, Convert.ToBoolean(value, CultureInfo.InvariantCulture));
                return;
            }

            WriteBytes(address, Encode(tag, value));
        }, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_client != IntPtr.Zero)
        {
            Native.Cli_Disconnect(_client);
            Native.Cli_Destroy(ref _client);
        }
    }

    private byte[] ReadBytes(S7Address address, int size)
    {
        EnsureConnected();

        var buffer = new byte[size];
        var result = address.AreaCode == S7Address.AreaDb
            ? Native.Cli_DBRead(_client, address.DbNumber, address.ByteOffset, size, buffer)
            : Native.Cli_ReadArea(_client, address.AreaCode, 0, address.ByteOffset, size, S7WordLenByte, buffer);

        ThrowIfSnap7Error(result, $"Read {size} byte(s) from {Describe(address)}");
        return buffer;
    }

    private void WriteBytes(S7Address address, byte[] buffer)
    {
        EnsureConnected();

        var result = address.AreaCode == S7Address.AreaDb
            ? Native.Cli_DBWrite(_client, address.DbNumber, address.ByteOffset, buffer.Length, buffer)
            : Native.Cli_WriteArea(_client, address.AreaCode, 0, address.ByteOffset, buffer.Length, S7WordLenByte, buffer);

        ThrowIfSnap7Error(result, $"Write {buffer.Length} byte(s) to {Describe(address)}");
    }

    private void WriteBool(S7Address address, bool value)
    {
        if (address.BitIndex is null)
        {
            throw new InvalidOperationException("Bool writes require a bit address.");
        }

        var bytes = ReadBytes(address, 1);
        var mask = (byte)(1 << address.BitIndex.Value);
        bytes[0] = value ? (byte)(bytes[0] | mask) : (byte)(bytes[0] & ~mask);
        WriteBytes(address, bytes);
    }

    private static object Decode(TagDefinition tag, S7Address address, byte[] buffer)
    {
        return tag.DataType switch
        {
            TagDataType.Bool => address.BitIndex is not null && (buffer[0] & (1 << address.BitIndex.Value)) != 0,
            TagDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(buffer),
            TagDataType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(buffer),
            TagDataType.DInt => BinaryPrimitives.ReadInt32BigEndian(buffer),
            TagDataType.Real => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(buffer)),
            _ => throw new NotSupportedException($"Unsupported tag data type '{tag.DataType}'.")
        };
    }

    private static byte[] Encode(TagDefinition tag, object value)
    {
        return tag.DataType switch
        {
            TagDataType.Int16 => EncodeInt16(value),
            TagDataType.UInt16 => EncodeUInt16(value),
            TagDataType.DInt => EncodeInt32(value),
            TagDataType.Real => EncodeReal(value),
            TagDataType.Bool => new[] { Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? (byte)1 : (byte)0 },
            _ => throw new NotSupportedException($"Unsupported tag data type '{tag.DataType}'.")
        };
    }

    private static byte[] EncodeInt16(object value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, Convert.ToInt16(value, CultureInfo.InvariantCulture));
        return buffer;
    }

    private static byte[] EncodeUInt16(object value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, Convert.ToUInt16(value, CultureInfo.InvariantCulture));
        return buffer;
    }

    private static byte[] EncodeInt32(object value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, Convert.ToInt32(value, CultureInfo.InvariantCulture));
        return buffer;
    }

    private static byte[] EncodeReal(object value)
    {
        var buffer = new byte[4];
        var bits = BitConverter.SingleToInt32Bits(Convert.ToSingle(value, CultureInfo.InvariantCulture));
        BinaryPrimitives.WriteInt32BigEndian(buffer, bits);
        return buffer;
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("PLC not connected.");
        }
    }

    private void SetUInt16Param(int paramNumber, ushort value)
    {
        var result = Native.Cli_SetParam_UInt16(_client, paramNumber, ref value);
        ThrowIfSnap7Error(result, $"Set Snap7 ushort param {paramNumber}");
    }

    private void SetInt32Param(int paramNumber, int value)
    {
        var result = Native.Cli_SetParam_Int32(_client, paramNumber, ref value);
        ThrowIfSnap7Error(result, $"Set Snap7 int param {paramNumber}");
    }

    private void SetConnectionType(string connectionType)
    {
        var value = connectionType switch
        {
            "pg" => ConnectionTypePg,
            "op" => ConnectionTypeOp,
            "basic" => ConnectionTypeBasic,
            _ => throw new ArgumentOutOfRangeException(nameof(connectionType), connectionType, "Unsupported Snap7 connection type.")
        };

        var result = Native.Cli_SetConnectionType(_client, value);
        ThrowIfSnap7Error(result, $"Set Snap7 connection type {connectionType}");
    }

    private void CaptureOrderCode(PlcDeviceInfo info)
    {
        var orderCode = new Native.S7OrderCodeNative
        {
            Code = new byte[21]
        };

        var result = Native.Cli_GetOrderCode(_client, ref orderCode);
        if (result != 0)
        {
            AddInfoWarning(info, "GetOrderCode", result);
            return;
        }

        info.OrderCode = CleanFixedString(orderCode.Code);
        info.FirmwareVersion = $"{orderCode.V1}.{orderCode.V2}.{orderCode.V3}";
    }

    private void CaptureCpuInfo(PlcDeviceInfo info)
    {
        var cpuInfo = new Native.S7CpuInfoNative
        {
            ModuleTypeName = new byte[33],
            SerialNumber = new byte[25],
            AsName = new byte[25],
            Copyright = new byte[27],
            ModuleName = new byte[25]
        };

        var result = Native.Cli_GetCpuInfo(_client, ref cpuInfo);
        if (result != 0)
        {
            AddInfoWarning(info, "GetCpuInfo", result);
            return;
        }

        info.ModuleTypeName = CleanFixedString(cpuInfo.ModuleTypeName);
        info.SerialNumber = CleanFixedString(cpuInfo.SerialNumber);
        info.AsName = CleanFixedString(cpuInfo.AsName);
        info.Copyright = CleanFixedString(cpuInfo.Copyright);
        info.ModuleName = CleanFixedString(cpuInfo.ModuleName);
    }

    private void CaptureCpInfo(PlcDeviceInfo info)
    {
        var cpInfo = new Native.S7CpInfoNative();
        var result = Native.Cli_GetCpInfo(_client, ref cpInfo);
        if (result != 0)
        {
            AddInfoWarning(info, "GetCpInfo", result);
            return;
        }

        info.MaxPduLength = cpInfo.MaxPduLength;
        info.MaxConnections = cpInfo.MaxConnections;
        info.MaxMpiRate = cpInfo.MaxMpiRate;
        info.MaxBusRate = cpInfo.MaxBusRate;
    }

    private void CapturePlcStatus(PlcDeviceInfo info)
    {
        var status = 0;
        var result = Native.Cli_GetPlcStatus(_client, ref status);
        if (result != 0)
        {
            AddInfoWarning(info, "GetPlcStatus", result);
            return;
        }

        info.PlcStatusRaw = status;
        info.PlcStatus = status switch
        {
            0x08 => "RUN",
            0x04 => "STOP",
            0x00 => "UNKNOWN",
            _ => $"0x{status:X2}"
        };
    }

    private void CaptureProtection(PlcDeviceInfo info)
    {
        var protection = new Native.S7ProtectionNative();
        var result = Native.Cli_GetProtection(_client, ref protection);
        if (result != 0)
        {
            AddInfoWarning(info, "GetProtection", result);
            return;
        }

        info.ProtectionLevel = protection.Schal;
        info.ProtectionMode = protection.Par;
        info.ProtectionFlags = protection.Rel;
    }

    private void CaptureBlockCounts(PlcDeviceInfo info)
    {
        var blocks = new Native.S7BlocksListNative();
        var result = Native.Cli_ListBlocks(_client, ref blocks);
        if (result != 0)
        {
            AddInfoWarning(info, "ListBlocks", result);
            return;
        }

        info.ObCount = blocks.ObCount;
        info.FbCount = blocks.FbCount;
        info.FcCount = blocks.FcCount;
        info.SfbCount = blocks.SfbCount;
        info.SfcCount = blocks.SfcCount;
        info.DbCount = blocks.DbCount;
        info.SdbCount = blocks.SdbCount;
    }

    private static string? CleanFixedString(byte[]? value)
    {
        if (value is null || value.Length == 0)
        {
            return null;
        }

        var text = Encoding.ASCII.GetString(value);
        var nullIndex = text.IndexOf('\0');
        if (nullIndex >= 0)
        {
            text = text[..nullIndex];
        }

        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    private static void AddInfoWarning(PlcDeviceInfo info, string operation, int result)
    {
        info.Warnings.Add($"{operation} failed: {GetErrorText(result)} (0x{result:X8}).");
    }

    private static string Describe(S7Address address)
    {
        var area = address.AreaCode switch
        {
            S7Address.AreaDb => $"DB{address.DbNumber}",
            S7Address.AreaMarker => "M",
            S7Address.AreaInput => "I",
            S7Address.AreaOutput => "Q",
            _ => $"Area 0x{address.AreaCode:X2}"
        };

        return address.BitIndex is null
            ? $"{area} byte {address.ByteOffset}"
            : $"{area} byte {address.ByteOffset}.{address.BitIndex.Value}";
    }

    private static Task RunNativeAsync(Action action, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
        }, cancellationToken);

    private static void ThrowIfSnap7Error(int result, string operation)
    {
        if (result == 0)
        {
            return;
        }

        throw new InvalidOperationException($"{operation} failed: {GetErrorText(result)} (0x{result:X8}).");
    }

    private static string GetErrorText(int error)
    {
        var buffer = new StringBuilder(1024);
        var result = Native.Cli_ErrorText(error, buffer, buffer.Capacity);
        return result == 0 ? buffer.ToString() : $"Snap7 error {error}";
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static class Native
    {
        private const string DllName = "snap7.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern IntPtr Cli_Create();

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern void Cli_Destroy(ref IntPtr client);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_ConnectTo(
            IntPtr client,
            [MarshalAs(UnmanagedType.LPStr)] string address,
            int rack,
            int slot);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_SetConnectionType(IntPtr client, ushort connectionType);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_Disconnect(IntPtr client);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, EntryPoint = "Cli_SetParam")]
        public static extern int Cli_SetParam_UInt16(IntPtr client, int paramNumber, ref ushort value);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, EntryPoint = "Cli_SetParam")]
        public static extern int Cli_SetParam_Int32(IntPtr client, int paramNumber, ref int value);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_GetConnected(IntPtr client, ref int connected);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct S7OrderCodeNative
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 21)]
            public byte[] Code;
            public byte V1;
            public byte V2;
            public byte V3;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct S7CpuInfoNative
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 33)]
            public byte[] ModuleTypeName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public byte[] SerialNumber;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public byte[] AsName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 27)]
            public byte[] Copyright;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public byte[] ModuleName;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct S7CpInfoNative
        {
            public int MaxPduLength;
            public int MaxConnections;
            public int MaxMpiRate;
            public int MaxBusRate;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct S7ProtectionNative
        {
            public ushort Schal;
            public ushort Par;
            public ushort Rel;
            public ushort BartSch;
            public ushort AnlSch;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct S7BlocksListNative
        {
            public int ObCount;
            public int FbCount;
            public int FcCount;
            public int SfbCount;
            public int SfcCount;
            public int DbCount;
            public int SdbCount;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_GetOrderCode(IntPtr client, ref S7OrderCodeNative info);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_GetCpuInfo(IntPtr client, ref S7CpuInfoNative info);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_GetCpInfo(IntPtr client, ref S7CpInfoNative info);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_GetPlcStatus(IntPtr client, ref int status);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_GetProtection(IntPtr client, ref S7ProtectionNative protection);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_ListBlocks(IntPtr client, ref S7BlocksListNative list);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_DBRead(IntPtr client, int dbNumber, int start, int size, byte[] buffer);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_DBWrite(IntPtr client, int dbNumber, int start, int size, byte[] buffer);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_ReadArea(
            IntPtr client,
            int area,
            int dbNumber,
            int start,
            int amount,
            int wordLen,
            byte[] buffer);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_WriteArea(
            IntPtr client,
            int area,
            int dbNumber,
            int start,
            int amount,
            int wordLen,
            byte[] buffer);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        public static extern int Cli_ErrorText(int error, StringBuilder text, int textLen);
    }
}
