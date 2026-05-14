using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SiemensS7Demo.Models;

public sealed class DemoRunOptions
{
    public string Adapter { get; private set; } = "mock";
    public string Name { get; private set; } = "DemoPLC";
    public string IpAddress { get; private set; } = "192.168.2.180";
    public string CpuType { get; private set; } = "S7-200 SMART";
    public int Port { get; private set; } = 102;
    public short Rack { get; private set; } = 0;
    public short Slot { get; private set; } = 0;
    public string Snap7ConnectionType { get; private set; } = "basic";
    public byte UnitId { get; private set; } = 1;
    public int IntervalSeconds { get; private set; } = 1;
    public bool Once { get; private set; }
    public bool ConnectOnly { get; private set; }
    public bool DeviceInfo { get; private set; }
    public bool AllowWrite { get; private set; }
    public bool Capabilities { get; private set; }
    public bool ValidateConfig { get; private set; }
    public bool SelfTest { get; private set; }
    public bool FailOnBadQuality { get; private set; }
    public bool Help { get; private set; }
    public int? Cycles { get; private set; }
    public string ConfigPath { get; private set; } = ResolveDefaultConfigPath();
    public string? ProjectPath { get; private set; }
    public string? RunLogPath { get; private set; }
    public List<WriteRequest> Writes { get; } = new();

    public PlcConnectionOptions ToConnectionOptions() => new()
    {
        Name = Name,
        IpAddress = IpAddress,
        CpuType = CpuType,
        Port = Port,
        Rack = Rack,
        Slot = Slot,
        Snap7ConnectionType = Snap7ConnectionType,
        UnitId = UnitId,
        Protocol = Adapter == "modbus" ? "modbus" : "s7"
    };

    public static DemoRunOptions Parse(string[] args)
    {
        var options = new DemoRunOptions();
        var portProvided = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var valueFromEquals = SplitEquals(arg, out var key);

            switch (key)
            {
                case "--help":
                case "-h":
                    options.Help = true;
                    break;
                case "--real":
                    options.Adapter = "snap7";
                    break;
                case "--mock":
                    options.Adapter = "mock";
                    break;
                case "--adapter":
                    options.Adapter = ReadValue(args, ref i, valueFromEquals).ToLowerInvariant();
                    break;
                case "--name":
                    options.Name = ReadValue(args, ref i, valueFromEquals);
                    break;
                case "--ip":
                    options.IpAddress = ReadValue(args, ref i, valueFromEquals);
                    break;
                case "--cpu":
                    options.CpuType = ReadValue(args, ref i, valueFromEquals);
                    break;
                case "--port":
                    options.Port = int.Parse(ReadValue(args, ref i, valueFromEquals), CultureInfo.InvariantCulture);
                    portProvided = true;
                    break;
                case "--rack":
                    options.Rack = short.Parse(ReadValue(args, ref i, valueFromEquals), CultureInfo.InvariantCulture);
                    break;
                case "--slot":
                    options.Slot = short.Parse(ReadValue(args, ref i, valueFromEquals), CultureInfo.InvariantCulture);
                    break;
                case "--connection-type":
                    options.Snap7ConnectionType = ReadValue(args, ref i, valueFromEquals).ToLowerInvariant();
                    break;
                case "--unit-id":
                    options.UnitId = byte.Parse(ReadValue(args, ref i, valueFromEquals), CultureInfo.InvariantCulture);
                    break;
                case "--interval":
                    options.IntervalSeconds = int.Parse(ReadValue(args, ref i, valueFromEquals), CultureInfo.InvariantCulture);
                    break;
                case "--config":
                    options.ConfigPath = ReadValue(args, ref i, valueFromEquals);
                    break;
                case "--project":
                    options.ProjectPath = ReadValue(args, ref i, valueFromEquals);
                    break;
                case "--run-log":
                    options.RunLogPath = ReadValue(args, ref i, valueFromEquals);
                    break;
                case "--once":
                case "--read-once":
                    options.Once = true;
                    break;
                case "--connect-only":
                    options.ConnectOnly = true;
                    break;
                case "--device-info":
                case "--info":
                    options.DeviceInfo = true;
                    break;
                case "--allow-write":
                    options.AllowWrite = true;
                    break;
                case "--capabilities":
                    options.Capabilities = true;
                    break;
                case "--validate-config":
                    options.ValidateConfig = true;
                    break;
                case "--self-test":
                    options.SelfTest = true;
                    break;
                case "--fail-on-bad-quality":
                    options.FailOnBadQuality = true;
                    break;
                case "--cycles":
                    options.Cycles = int.Parse(ReadValue(args, ref i, valueFromEquals), CultureInfo.InvariantCulture);
                    break;
                case "--write":
                    options.Writes.Add(WriteRequest.Parse(ReadValue(args, ref i, valueFromEquals)));
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{arg}'. Use --help to view supported options.");
            }
        }

        if (options.Adapter is not ("mock" or "snap7" or "modbus"))
        {
            throw new ArgumentException("--adapter must be 'mock', 'snap7', or 'modbus'.");
        }

        if (options.Snap7ConnectionType is not ("pg" or "op" or "basic"))
        {
            throw new ArgumentException("--connection-type must be 'pg', 'op', or 'basic'.");
        }

        if (options.Adapter == "modbus" && !portProvided)
        {
            options.Port = 502;
        }

        if (options.Cycles is <= 0)
        {
            throw new ArgumentException("--cycles must be greater than 0.");
        }

        return options;
    }

    public static string HelpText() =>
        """
        Siemens S7 demo runner

        Mock mode:
          dotnet run --project src\SiemensS7Demo\SiemensS7Demo.csproj -- --once
          .\tools\run-s7-demo.ps1 --once

        Real PLC mode:
          dotnet run --project src\SiemensS7Demo\SiemensS7Demo.csproj -- --adapter snap7 --ip 192.168.2.180 --cpu "S7-200 SMART" --rack 0 --slot 0 --connection-type basic --connect-only
          .\tools\run-s7-demo.ps1 --adapter snap7 --ip 192.168.2.180 --cpu "S7-200 SMART" --rack 0 --slot 0 --connection-type basic --connect-only

        Project mode:
          .\tools\run-s7-demo.ps1 --project src\SiemensS7Demo\Config\project.sample.json --read-once

        Options:
          --adapter mock|snap7|modbus
                                    Select in-memory mock, real Snap7, or Modbus TCP adapter.
          --real                    Shortcut for --adapter snap7.
          --mock                    Shortcut for --adapter mock.
          --ip <address>            PLC IP address, default 192.168.2.180.
          --cpu <name>              PLC model label, default S7-200 SMART.
          --rack <number>           PLC rack, default 0.
          --slot <number>           PLC slot, default 0.
          --connection-type <type>  Snap7 connection type: pg, op, or basic. Default basic.
          --unit-id <number>        Modbus TCP unit id, default 1.
          --port <number>           PLC S7 ISO-on-TCP port, default 102.
          --config <path>           Tag XML, default Config\siemens_s7_200_smart_sample.xml.
          --project <path>          Project JSON with multiple devices and inline tags.
          --run-log <path>          Append structured JSONL snapshots/events to a runtime log.
          --interval <seconds>      Polling interval, default 1.
          --cycles <number>         Run a finite number of polling cycles, then exit.
          --capabilities            Print implemented and pending operation capability list.
          --validate-config         Validate XML tag config or project JSON, then exit.
          --self-test               Run safe local self-tests, including Modbus loopback.
          --fail-on-bad-quality     Exit non-zero if any read tag returns BAD quality.
          --device-info             Read PLC CPU/order/status/communication info after connecting.
          --connect-only            Only verify the Snap7 connection handshake, then exit.
          --once, --read-once       Read configured tags once and exit.
          --write Tag=value         Write a tag by name. Can be repeated.
          --allow-write             Required for any real write. Tag must also set safeWrite=true.
          --help                    Show this help.
        """;

    private static string? SplitEquals(string arg, out string key)
    {
        var equals = arg.IndexOf('=');
        if (equals < 0)
        {
            key = arg;
            return null;
        }

        key = arg[..equals];
        return arg[(equals + 1)..];
    }

    private static string ReadValue(string[] args, ref int index, string? valueFromEquals)
    {
        if (!string.IsNullOrEmpty(valueFromEquals))
        {
            return valueFromEquals;
        }

        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for '{args[index]}'.");
        }

        index++;
        return args[index];
    }

    private static string ResolveDefaultConfigPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Config", "siemens_s7_200_smart_sample.xml"),
            Path.Combine(Environment.CurrentDirectory, "Config", "siemens_s7_200_smart_sample.xml"),
            Path.Combine(Environment.CurrentDirectory, "src", "SiemensS7Demo", "Config", "siemens_s7_200_smart_sample.xml"),
            Path.Combine(AppContext.BaseDirectory, "Config", "siemens_s7_sample.xml"),
            Path.Combine(Environment.CurrentDirectory, "Config", "siemens_s7_sample.xml"),
            Path.Combine(Environment.CurrentDirectory, "src", "SiemensS7Demo", "Config", "siemens_s7_sample.xml")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}

public sealed record WriteRequest(string TagName, string Value)
{
    public static WriteRequest Parse(string text)
    {
        var equals = text.IndexOf('=');
        if (equals <= 0 || equals == text.Length - 1)
        {
            throw new ArgumentException("--write value must look like TagName=value.");
        }

        return new WriteRequest(text[..equals], text[(equals + 1)..]);
    }
}
