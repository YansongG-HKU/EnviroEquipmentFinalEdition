using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using SiemensS7Demo.Testing;

Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;

DemoRunOptions runOptions;
try
{
    runOptions = DemoRunOptions.Parse(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(DemoRunOptions.HelpText());
    return 2;
}

if (runOptions.Help)
{
    Console.WriteLine(DemoRunOptions.HelpText());
    return 0;
}

if (runOptions.Capabilities)
{
    PrintCapabilities();
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

if (runOptions.SelfTest)
{
    try
    {
        return await RunSelfTestAsync(runOptions, cts.Token);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"SelfTest failed: {ex.Message}");
        return 1;
    }
}

if (runOptions.ValidateConfig)
{
    try
    {
        return ValidateConfiguration(runOptions);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Config validation failed: {ex.Message}");
        return 1;
    }
}

if (runOptions.ProjectPath is not null)
{
    try
    {
        return await RunProjectAsync(runOptions, cts.Token);
    }
    catch (OperationCanceledException)
    {
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

var connectionOptions = runOptions.ToConnectionOptions();
var snapshotSink = CreateSnapshotSink(runOptions.RunLogPath, runOptions.Name);

IS7Adapter adapter = CreateAdapter(runOptions.Adapter);

var plcClient = new SiemensS7Client(connectionOptions, adapter);

try
{
    Console.WriteLine($"Adapter: {runOptions.Adapter}");
    Console.WriteLine($"Target: {connectionOptions}");

    await plcClient.ConnectAsync(cts.Token);
    Console.WriteLine("Connected.");

    if (runOptions.DeviceInfo)
    {
        var deviceInfo = await plcClient.GetDeviceInfoAsync(cts.Token);
        PrintDeviceInfo(deviceInfo);
    }

    if (runOptions.ConnectOnly)
    {
        Console.WriteLine("Connection handshake succeeded. No tag read/write was attempted.");
        return 0;
    }

    if (runOptions.DeviceInfo && !runOptions.Once && runOptions.Writes.Count == 0)
    {
        Console.WriteLine("Device information read completed. No tag read/write was attempted.");
        return 0;
    }

    var tags = TagConfigLoader.Load(runOptions.ConfigPath);
    var validationIssues = ConfigValidationService.ValidateTags(tags, ValidationProtocolFor(runOptions), runOptions.ConfigPath);
    PrintValidationIssues(validationIssues);
    if (ConfigValidationService.HasErrors(validationIssues))
    {
        throw new InvalidOperationException("Tag configuration has validation errors.");
    }

    var readTags = tags.Where(t => t.Access != TagAccess.Write).ToList();
    var pollingService = new PlcPollingService(plcClient);
    var writeService = new PlcWriteService(plcClient);

    Console.WriteLine($"Config: {runOptions.ConfigPath}");

    foreach (var write in runOptions.Writes)
    {
        var tag = tags.SingleOrDefault(t => string.Equals(t.Name, write.TagName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Tag '{write.TagName}' was not found in config.");

        EnsureWriteAllowed(runOptions, tag);
        var rawWriteValue = ConvertWriteValue(tag, write.Value);
        await writeService.WriteAsync(tag, rawWriteValue, cts.Token);
        Console.WriteLine($"Wrote {tag.Name} engineering={write.Value} raw={rawWriteValue}");

        if (tag.Access == TagAccess.ReadWrite)
        {
            var readback = await plcClient.ReadTagsAsync(new[] { tag }, cts.Token);
            snapshotSink(readback);
            if (readback.Values.Any(value => !value.IsQualityGood))
            {
                throw new InvalidOperationException($"Write readback failed for tag '{tag.Name}'.");
            }
        }
        else
        {
            Console.WriteLine($"Readback skipped for {tag.Name}: tag access is {tag.Access}.");
        }
    }

    if (runOptions.Once)
    {
        await PrintOneSnapshot(plcClient, readTags, cts.Token);
        if (runOptions.RunLogPath is not null)
        {
            var values = await plcClient.ReadTagsAsync(readTags, cts.Token);
            AppendSnapshot(runOptions.RunLogPath, runOptions.Name, values);
            ThrowIfBadQuality(values, runOptions.FailOnBadQuality);
        }
        return 0;
    }

    if (readTags.Count == 0)
    {
        Console.WriteLine("No readable tags configured. Exiting.");
        return 0;
    }

    if (runOptions.Cycles is int cycles)
    {
        await RunPollingCyclesAsync(
            plcClient,
            readTags,
            TimeSpan.FromSeconds(runOptions.IntervalSeconds),
            cycles,
            snapshotSink,
            runOptions.FailOnBadQuality,
            cts.Token);
        return 0;
    }

    Console.WriteLine("Polling started. Press ENTER or Ctrl+C to stop.");
    var pollTask = pollingService.RunAsync(
        readTags,
        TimeSpan.FromSeconds(runOptions.IntervalSeconds),
        snapshotSink,
        cts.Token);

    Console.ReadLine();
    cts.Cancel();

    await pollTask;
    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
finally
{
    try
    {
        await plcClient.DisconnectAsync(CancellationToken.None);
    }
    catch
    {
        // Shutdown path only.
    }

    if (adapter is IDisposable disposable)
    {
        disposable.Dispose();
    }
}

static async Task PrintOneSnapshot(
    IPlcClient plcClient,
    IReadOnlyList<TagDefinition> readTags,
    CancellationToken cancellationToken)
{
    if (readTags.Count == 0)
    {
        Console.WriteLine("No readable tags configured.");
        return;
    }

    var values = await plcClient.ReadTagsAsync(readTags, cancellationToken);
    PrintSnapshot(values);
}

static async Task RunPollingCyclesAsync(
    IPlcClient plcClient,
    IReadOnlyList<TagDefinition> readTags,
    TimeSpan interval,
    int cycles,
    Action<IReadOnlyDictionary<string, TagValue>> onSnapshot,
    bool failOnBadQuality,
    CancellationToken cancellationToken)
{
    if (readTags.Count == 0)
    {
        Console.WriteLine("No readable tags configured.");
        return;
    }

    Console.WriteLine($"Polling cycles started: cycles={cycles}, interval={interval.TotalSeconds:0.###}s");
    for (var i = 1; i <= cycles; i++)
    {
        Console.WriteLine($"Cycle {i}/{cycles}");
        var values = await plcClient.ReadTagsAsync(readTags, cancellationToken);
        onSnapshot(values);
        ThrowIfBadQuality(values, failOnBadQuality);

        if (i < cycles)
        {
            await Task.Delay(interval, cancellationToken);
        }
    }
}

static void PrintSnapshot(IReadOnlyDictionary<string, TagValue> snapshot)
{
    foreach (var item in snapshot.Values.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
    {
        var displayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Name : $"{item.Name} ({item.DisplayName})";
        var address = string.IsNullOrWhiteSpace(item.Address) ? string.Empty : $" address={item.Address}";
        if (!item.IsQualityGood)
        {
            Console.WriteLine($"[{item.TimestampUtc:O}] BAD  {displayName}{address} error={item.QualityMessage}");
            continue;
        }

        var unit = string.IsNullOrWhiteSpace(item.Unit) ? string.Empty : $" {item.Unit}";
        var raw = item.RawValue is null || Equals(item.RawValue, item.Value) ? string.Empty : $" raw={item.RawValue}";
        Console.WriteLine($"[{item.TimestampUtc:O}] GOOD {displayName}{address} value={item.Value}{unit}{raw}");
    }
}

static Action<IReadOnlyDictionary<string, TagValue>> CreateSnapshotSink(string? runLogPath, string deviceId)
{
    return snapshot =>
    {
        PrintSnapshot(snapshot);
        if (!string.IsNullOrWhiteSpace(runLogPath))
        {
            AppendSnapshot(runLogPath, deviceId, snapshot);
        }
    };
}

static void AppendSnapshot(string runLogPath, string deviceId, IReadOnlyDictionary<string, TagValue> snapshot)
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(runLogPath));
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var record = new
    {
        type = "snapshot",
        timestampUtc = DateTime.UtcNow,
        deviceId,
        good = snapshot.Values.Count(value => value.IsQualityGood),
        bad = snapshot.Values.Count(value => !value.IsQualityGood),
        values = snapshot.Values.Select(value => new
        {
            value.Name,
            value.DisplayName,
            value.Address,
            value.Value,
            value.RawValue,
            value.Unit,
            value.IsQualityGood,
            value.QualityMessage,
            value.TimestampUtc
        })
    };

    File.AppendAllText(runLogPath, JsonSerializer.Serialize(record) + Environment.NewLine);
}

static void ThrowIfBadQuality(IReadOnlyDictionary<string, TagValue> snapshot, bool failOnBadQuality)
{
    if (!failOnBadQuality)
    {
        return;
    }

    var bad = snapshot.Values.Where(value => !value.IsQualityGood).ToArray();
    if (bad.Length > 0)
    {
        throw new InvalidOperationException($"{bad.Length} tag(s) returned BAD quality.");
    }
}

static void PrintDeviceInfo(PlcDeviceInfo info)
{
    Console.WriteLine("Device information:");
    Console.WriteLine($"  TimestampUtc:      {info.TimestampUtc:O}");
    Console.WriteLine($"  Target:            {info.IpAddress}:{info.Port}");
    Console.WriteLine($"  ConfiguredCpuType: {info.ConfiguredCpuType}");
    Console.WriteLine($"  Snap7:             connectionType={info.ConnectionType}, rack={info.Rack}, slot={info.Slot}");
    PrintOptional("  OrderCode", info.OrderCode);
    PrintOptional("  Firmware", info.FirmwareVersion);
    PrintOptional("  ModuleTypeName", info.ModuleTypeName);
    PrintOptional("  ModuleName", info.ModuleName);
    PrintOptional("  SerialNumber", info.SerialNumber);
    PrintOptional("  ASName", info.AsName);
    PrintOptional("  Copyright", info.Copyright);
    PrintOptional("  PlcStatus", info.PlcStatus);
    if (info.PlcStatusRaw is not null)
    {
        Console.WriteLine($"  PlcStatusRaw:      0x{info.PlcStatusRaw.Value:X2}");
    }

    PrintOptional("  MaxPduLength", info.MaxPduLength);
    PrintOptional("  MaxConnections", info.MaxConnections);
    PrintOptional("  MaxMpiRate", info.MaxMpiRate);
    PrintOptional("  MaxBusRate", info.MaxBusRate);
    PrintOptional("  ProtectionLevel", info.ProtectionLevel);
    PrintOptional("  ProtectionMode", info.ProtectionMode);
    PrintOptional("  ProtectionFlags", info.ProtectionFlags);

    if (info.ObCount is not null || info.FbCount is not null || info.FcCount is not null || info.DbCount is not null)
    {
        Console.WriteLine("  BlockCounts:");
        PrintOptional("    OB", info.ObCount);
        PrintOptional("    FB", info.FbCount);
        PrintOptional("    FC", info.FcCount);
        PrintOptional("    DB", info.DbCount);
        PrintOptional("    SFB", info.SfbCount);
        PrintOptional("    SFC", info.SfcCount);
        PrintOptional("    SDB", info.SdbCount);
    }

    if (info.Warnings.Count > 0)
    {
        Console.WriteLine("  Warnings:");
        foreach (var warning in info.Warnings)
        {
            Console.WriteLine($"    - {warning}");
        }
    }
}

static void PrintOptional(string label, object? value)
{
    if (value is null)
    {
        return;
    }

    var text = Convert.ToString(value, CultureInfo.InvariantCulture);
    if (!string.IsNullOrWhiteSpace(text))
    {
        Console.WriteLine($"{label}: {text}");
    }
}

static int ValidateConfiguration(DemoRunOptions runOptions)
{
    IReadOnlyList<ConfigValidationIssue> issues;
    if (runOptions.ProjectPath is not null)
    {
        var project = ProjectConfigLoader.Load(runOptions.ProjectPath);
        issues = ConfigValidationService.ValidateProject(project);
        Console.WriteLine($"Validated project: {runOptions.ProjectPath}");
    }
    else
    {
        var tags = TagConfigLoader.Load(runOptions.ConfigPath);
        issues = ConfigValidationService.ValidateTags(tags, ValidationProtocolFor(runOptions), runOptions.ConfigPath);
        Console.WriteLine($"Validated tag config: {runOptions.ConfigPath}");
    }

    PrintValidationIssues(issues);
    return ConfigValidationService.HasErrors(issues) ? 1 : 0;
}

static void PrintValidationIssues(IReadOnlyList<ConfigValidationIssue> issues)
{
    if (issues.Count == 0)
    {
        Console.WriteLine("Config validation: OK");
        return;
    }

    foreach (var issue in issues)
    {
        Console.WriteLine($"[{issue.Severity}] {issue.Scope}: {issue.Message}");
    }
}

static string ValidationProtocolFor(DemoRunOptions runOptions)
{
    return runOptions.Adapter switch
    {
        "modbus" => "modbus",
        _ => "s7"
    };
}

static object ConvertWriteValue(TagDefinition tag, string value)
{
    return tag.DataType switch
    {
        TagDataType.Bool => bool.Parse(value),
        TagDataType.Int16 => ConvertEngineeringNumber<short>(tag, value, raw => checked((short)Math.Round(raw))),
        TagDataType.UInt16 => ConvertEngineeringNumber<ushort>(tag, value, raw => checked((ushort)Math.Round(raw))),
        TagDataType.DInt => ConvertEngineeringNumber<int>(tag, value, raw => checked((int)Math.Round(raw))),
        TagDataType.UInt32 => ConvertEngineeringNumber<uint>(tag, value, raw => checked((uint)Math.Round(raw))),
        TagDataType.Real => ConvertEngineeringNumber<float>(tag, value, raw => checked((float)raw)),
        _ => throw new NotSupportedException($"Unsupported tag data type '{tag.DataType}'.")
    };
}

static IS7Adapter CreateAdapter(string adapter)
{
    return adapter.ToLowerInvariant() switch
    {
        "snap7" or "s7" or "siemens" => new Snap7S7Adapter(),
        "modbus" or "modbus-tcp" => new ModbusTcpAdapter(),
        "mock" => new InMemoryS7Adapter(),
        _ => throw new InvalidOperationException($"Unsupported adapter '{adapter}'.")
    };
}

static string NormalizeProtocol(string protocol)
{
    return protocol.ToLowerInvariant() switch
    {
        "snap7" or "s7" or "siemens" => "snap7",
        "modbus" or "modbus-tcp" => "modbus",
        "mock" => "mock",
        _ => throw new InvalidOperationException($"Unsupported protocol '{protocol}'.")
    };
}

static async Task<int> RunProjectAsync(DemoRunOptions runOptions, CancellationToken cancellationToken)
{
    if (runOptions.Writes.Count > 0)
    {
        throw new InvalidOperationException("--write is intentionally disabled in --project mode. Use a single-device config with --allow-write for write tests.");
    }

    var project = ProjectConfigLoader.Load(runOptions.ProjectPath!);
    var validationIssues = ConfigValidationService.ValidateProject(project);
    PrintValidationIssues(validationIssues);
    if (ConfigValidationService.HasErrors(validationIssues))
    {
        return 1;
    }

    var enabledDevices = project.Devices.Where(device => device.Enabled).ToArray();
    if (enabledDevices.Length == 0)
    {
        throw new InvalidOperationException($"Project '{runOptions.ProjectPath}' has no enabled devices.");
    }

    var failures = 0;
    Console.WriteLine($"Project: {project.ProjectName}");
    Console.WriteLine($"Enabled devices: {enabledDevices.Length}");

    if (runOptions.Cycles is int cycles && !runOptions.DeviceInfo && !runOptions.ConnectOnly)
    {
        return await RunProjectPollingCyclesAsync(enabledDevices, runOptions.IntervalSeconds, cycles, runOptions.RunLogPath, runOptions.FailOnBadQuality, cancellationToken);
    }

    foreach (var device in enabledDevices)
    {
        var adapterName = NormalizeProtocol(device.Protocol);
        Console.WriteLine();
        Console.WriteLine($"[{device.Id}] {device.Name} {device.Protocol} {device.Ip}:{device.Port}");

        var adapter = CreateAdapter(adapterName);
        var client = new SiemensS7Client(device.ToConnectionOptions(), adapter);

        try
        {
            await client.ConnectAsync(cancellationToken);
            Console.WriteLine("Connect: OK");

            if (runOptions.DeviceInfo || runOptions.ConnectOnly)
            {
                var deviceInfo = await client.GetDeviceInfoAsync(cancellationToken);
                PrintDeviceInfo(deviceInfo);
            }

            var shouldRead = runOptions.Once || (!runOptions.DeviceInfo && !runOptions.ConnectOnly);
            if (shouldRead)
            {
                var readableTags = device.Tags.Where(tag => tag.Access != TagAccess.Write).ToArray();
                if (readableTags.Length == 0)
                {
                    Console.WriteLine("Read: skipped, no readable tags configured.");
                }
                else
                {
                    var snapshot = await client.ReadTagsAsync(readableTags, cancellationToken);
                    var sink = CreateSnapshotSink(runOptions.RunLogPath, device.Id);
                    sink(snapshot);
                    ThrowIfBadQuality(snapshot, runOptions.FailOnBadQuality);
                }
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.Error.WriteLine($"[{device.Id}] FAILED: {ex.Message}");
        }
        finally
        {
            try
            {
                await client.DisconnectAsync(CancellationToken.None);
            }
            catch
            {
                // Shutdown path only.
            }

            if (adapter is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    return failures == 0 ? 0 : 1;
}

static async Task<int> RunProjectPollingCyclesAsync(
    IReadOnlyList<DeviceDefinition> devices,
    int fallbackIntervalSeconds,
    int cycles,
    string? runLogPath,
    bool failOnBadQuality,
    CancellationToken cancellationToken)
{
    Console.WriteLine($"Project polling cycles started: devices={devices.Count}, cycles={cycles}");
    var tasks = devices.Select(device => RunProjectDevicePollingCyclesAsync(device, fallbackIntervalSeconds, cycles, runLogPath, failOnBadQuality, cancellationToken)).ToArray();
    var results = await Task.WhenAll(tasks);
    return results.All(result => result) ? 0 : 1;
}

static async Task<bool> RunProjectDevicePollingCyclesAsync(
    DeviceDefinition device,
    int fallbackIntervalSeconds,
    int cycles,
    string? runLogPath,
    bool failOnBadQuality,
    CancellationToken cancellationToken)
{
    var adapterName = NormalizeProtocol(device.Protocol);
    var adapter = CreateAdapter(adapterName);
    var client = new SiemensS7Client(device.ToConnectionOptions(), adapter);

    try
    {
        Console.WriteLine($"[{device.Id}] Connecting {device.Protocol} {device.Ip}:{device.Port}");
        await client.ConnectAsync(cancellationToken);
        Console.WriteLine($"[{device.Id}] Connect: OK");

        var readableTags = device.Tags.Where(tag => tag.Access != TagAccess.Write).ToArray();
        if (readableTags.Length == 0)
        {
            Console.WriteLine($"[{device.Id}] Read: skipped, no readable tags configured.");
            return true;
        }

        var interval = device.PollingIntervalMs > 0
            ? TimeSpan.FromMilliseconds(device.PollingIntervalMs)
            : TimeSpan.FromSeconds(fallbackIntervalSeconds);

        for (var i = 1; i <= cycles; i++)
        {
            Console.WriteLine($"[{device.Id}] Cycle {i}/{cycles}");
            var snapshot = await client.ReadTagsAsync(readableTags, cancellationToken);
            var sink = CreateSnapshotSink(runLogPath, device.Id);
            sink(snapshot);
            ThrowIfBadQuality(snapshot, failOnBadQuality);

            if (i < cycles)
            {
                await Task.Delay(interval, cancellationToken);
            }
        }

        return true;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{device.Id}] FAILED: {ex.Message}");
        return false;
    }
    finally
    {
        try
        {
            await client.DisconnectAsync(CancellationToken.None);
        }
        catch
        {
            // Shutdown path only.
        }

        if (adapter is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

static async Task<int> RunSelfTestAsync(DemoRunOptions runOptions, CancellationToken cancellationToken)
{
    var failures = 0;

    void Pass(string name) => Console.WriteLine($"[PASS] {name}");
    void Fail(string name, Exception ex)
    {
        failures++;
        Console.WriteLine($"[FAIL] {name}: {ex.Message}");
    }

    async Task RunAsync(string name, Func<Task> action)
    {
        try
        {
            await action();
            Pass(name);
        }
        catch (Exception ex)
        {
            Fail(name, ex);
        }
    }

    await RunAsync("default S7 XML validates", () =>
    {
        var tags = TagConfigLoader.Load(runOptions.ConfigPath);
        var issues = ConfigValidationService.ValidateTags(tags, "s7", runOptions.ConfigPath);
        if (ConfigValidationService.HasErrors(issues))
        {
            throw new InvalidOperationException(string.Join("; ", issues.Select(issue => issue.Message)));
        }

        return Task.CompletedTask;
    });

    await RunAsync("project JSON validates", () =>
    {
        var projectPath = runOptions.ProjectPath ?? "src\\SiemensS7Demo\\Config\\project.sample.json";
        var project = ProjectConfigLoader.Load(projectPath);
        var issues = ConfigValidationService.ValidateProject(project);
        if (ConfigValidationService.HasErrors(issues))
        {
            throw new InvalidOperationException(string.Join("; ", issues.Select(issue => issue.Message)));
        }

        return Task.CompletedTask;
    });

    await RunAsync("mock read and guarded-write block", async () =>
    {
        var options = new PlcConnectionOptions { Name = "SelfTestMock", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new InMemoryS7Adapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(cancellationToken);

        var readTag = MakeTag("MockWord", "MW0", TagDataType.Int16, TagAccess.Read);
        var writeTag = MakeTag("MockWrite", "MW2", TagDataType.Int16, TagAccess.ReadWrite, safeWrite: true, min: 0, max: 1000);
        var values = await client.ReadTagsAsync(new[] { readTag }, cancellationToken);
        AssertGood(values, "MockWord");

        var blocked = false;
        try
        {
            EnsureWriteAllowed(runOptions, writeTag);
        }
        catch (InvalidOperationException)
        {
            blocked = true;
        }

        if (!blocked)
        {
            throw new InvalidOperationException("Write guard did not block a write without --allow-write.");
        }
    });

    await RunAsync("mock batch read", async () =>
    {
        var options = new PlcConnectionOptions { Name = "SelfTestBatch", IpAddress = "127.0.0.1", CpuType = "Mock" };
        var adapter = new InMemoryS7Adapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(cancellationToken);

        var t1 = MakeTag("Batch1", "MW0", TagDataType.Int16, TagAccess.Read);
        var t2 = MakeTag("Batch2", "MW2", TagDataType.Int16, TagAccess.Read);
        var t3 = MakeTag("Batch3", "MD4", TagDataType.DInt, TagAccess.Read);

        await adapter.WriteRawAsync(t1, (short)11, cancellationToken);
        await adapter.WriteRawAsync(t2, (short)22, cancellationToken);
        await adapter.WriteRawAsync(t3, 333, cancellationToken);

        var values = await client.ReadTagsAsync(new[] { t1, t2, t3 }, cancellationToken);
        AssertGood(values, "Batch1");
        AssertGood(values, "Batch2");
        AssertGood(values, "Batch3");
        if (System.Convert.ToInt32(values["Batch1"].Value, System.Globalization.CultureInfo.InvariantCulture) != 11
            || System.Convert.ToInt32(values["Batch2"].Value, System.Globalization.CultureInfo.InvariantCulture) != 22
            || System.Convert.ToInt32(values["Batch3"].Value, System.Globalization.CultureInfo.InvariantCulture) != 333)
        {
            throw new InvalidOperationException("Batch read returned unexpected values.");
        }
    });

    await RunAsync("Modbus loopback read/write", async () =>
    {
        await using var server = ModbusLoopbackServer.Start();
        var options = new PlcConnectionOptions
        {
            Name = "ModbusLoopback",
            Protocol = "modbus",
            IpAddress = "127.0.0.1",
            Port = server.Port,
            CpuType = "Modbus TCP",
            UnitId = 1
        };

        using var adapter = new ModbusTcpAdapter();
        var client = new SiemensS7Client(options, adapter);
        await client.ConnectAsync(cancellationToken);

        var coil0 = MakeTag("Coil0", "C0", TagDataType.Bool, TagAccess.Read);
        var hr0 = MakeTag("HoldingRegister0", "HR0", TagDataType.Int16, TagAccess.Read);
        var hr1 = MakeTag("HoldingRegister1", "HR1", TagDataType.UInt16, TagAccess.Read);
        var values = await client.ReadTagsAsync(new[] { coil0, hr0, hr1 }, cancellationToken);
        AssertGood(values, "Coil0");
        AssertGood(values, "HoldingRegister0");
        AssertGood(values, "HoldingRegister1");
        if (!Convert.ToBoolean(values["Coil0"].Value, CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("Expected C0=true from Modbus loopback.");
        }

        if (Math.Abs(Convert.ToDouble(values["HoldingRegister0"].Value, CultureInfo.InvariantCulture) - 123) > 0.001)
        {
            throw new InvalidOperationException("Expected HR0=123 from Modbus loopback.");
        }

        if (Math.Abs(Convert.ToDouble(values["HoldingRegister1"].Value, CultureInfo.InvariantCulture) - 65000) > 0.001)
        {
            throw new InvalidOperationException("Expected HR1=65000 from Modbus loopback.");
        }

        var coil1 = MakeTag("Coil1", "C1", TagDataType.Bool, TagAccess.ReadWrite, safeWrite: true);
        var hr2 = MakeTag("HoldingRegister2", "HR2", TagDataType.Int16, TagAccess.ReadWrite, safeWrite: true, min: 0, max: 1000);
        var hr3 = MakeTag("HoldingRegister3", "HR3", TagDataType.UInt16, TagAccess.ReadWrite, safeWrite: true, min: 0, max: 65535);
        await client.WriteTagAsync(coil1, true, cancellationToken);
        await client.WriteTagAsync(hr2, (short)321, cancellationToken);
        await client.WriteTagAsync(hr3, (ushort)65001, cancellationToken);

        var readback = await client.ReadTagsAsync(new[] { coil1, hr2, hr3 }, cancellationToken);
        AssertGood(readback, "Coil1");
        AssertGood(readback, "HoldingRegister2");
        AssertGood(readback, "HoldingRegister3");
        if (!Convert.ToBoolean(readback["Coil1"].Value, CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("Expected C1=true after Modbus write.");
        }

        if (Math.Abs(Convert.ToDouble(readback["HoldingRegister2"].Value, CultureInfo.InvariantCulture) - 321) > 0.001)
        {
            throw new InvalidOperationException("Expected HR2=321 after Modbus write.");
        }

        if (Math.Abs(Convert.ToDouble(readback["HoldingRegister3"].Value, CultureInfo.InvariantCulture) - 65001) > 0.001)
        {
            throw new InvalidOperationException("Expected HR3=65001 after Modbus write.");
        }

        var hrf = MakeTag("HrFloat", "HRF20", TagDataType.Real, TagAccess.ReadWrite, safeWrite: true);
        await client.WriteTagAsync(hrf, 12.5f, cancellationToken);
        var floatReadback = await client.ReadTagsAsync(new[] { hrf }, cancellationToken);
        AssertGood(floatReadback, "HrFloat");
        if (Math.Abs(Convert.ToDouble(floatReadback["HrFloat"].Value, CultureInfo.InvariantCulture) - 12.5) > 0.0001)
        {
            throw new InvalidOperationException("Expected HrFloat=12.5 after Modbus write.");
        }

        var hrd = MakeTag("HrDInt", "HRD30", TagDataType.DInt, TagAccess.ReadWrite, safeWrite: true);
        var hrdu = MakeTag("HrUInt32", "HRDU40", TagDataType.UInt32, TagAccess.ReadWrite, safeWrite: true);

        await client.WriteTagAsync(hrd, -987654, cancellationToken);
        await client.WriteTagAsync(hrdu, 3000000000u, cancellationToken);
        var bigReadback = await client.ReadTagsAsync(new[] { hrd, hrdu }, cancellationToken);
        AssertGood(bigReadback, "HrDInt");
        AssertGood(bigReadback, "HrUInt32");
        if (System.Convert.ToInt32(bigReadback["HrDInt"].Value, System.Globalization.CultureInfo.InvariantCulture) != -987654)
        {
            throw new InvalidOperationException("Expected HrDInt=-987654 after Modbus write.");
        }
        if (System.Convert.ToUInt32(bigReadback["HrUInt32"].Value, System.Globalization.CultureInfo.InvariantCulture) != 3000000000u)
        {
            throw new InvalidOperationException("Expected HrUInt32=3000000000 after Modbus write.");
        }
    });

    Console.WriteLine(failures == 0 ? "SelfTest: OK" : $"SelfTest: {failures} failure(s)");
    return failures == 0 ? 0 : 1;
}

static TagDefinition MakeTag(
    string name,
    string address,
    TagDataType dataType,
    TagAccess access,
    bool safeWrite = false,
    double? min = null,
    double? max = null)
{
    return new TagDefinition
    {
        Name = name,
        DisplayName = name,
        Group = "SelfTest",
        Address = address,
        DataType = dataType,
        Unit = string.Empty,
        Access = access,
        SafeWrite = safeWrite,
        Min = min,
        Max = max
    };
}

static void AssertGood(IReadOnlyDictionary<string, TagValue> values, string tagName)
{
    if (!values.TryGetValue(tagName, out var value))
    {
        throw new InvalidOperationException($"Missing value for {tagName}.");
    }

    if (!value.IsQualityGood)
    {
        throw new InvalidOperationException($"{tagName} quality is bad: {value.QualityMessage}");
    }
}

static void EnsureWriteAllowed(DemoRunOptions runOptions, TagDefinition tag)
{
    if (!runOptions.AllowWrite)
    {
        throw new InvalidOperationException("Write blocked. Add --allow-write only after confirming the target tag, address, value, and equipment state.");
    }

    if (tag.Access == TagAccess.Read)
    {
        throw new InvalidOperationException($"Write blocked. Tag '{tag.Name}' is configured as read-only.");
    }

    if (!tag.SafeWrite)
    {
        throw new InvalidOperationException($"Write blocked. Tag '{tag.Name}' must set safeWrite=\"true\" in the config before it can be written.");
    }
}

static T ConvertEngineeringNumber<T>(TagDefinition tag, string value, Func<double, T> rawConverter)
{
    var engineering = double.Parse(value, CultureInfo.InvariantCulture);
    if (tag.Min.HasValue && engineering < tag.Min.Value)
    {
        throw new InvalidOperationException($"Write value {engineering} is below min {tag.Min.Value} for tag '{tag.Name}'.");
    }

    if (tag.Max.HasValue && engineering > tag.Max.Value)
    {
        throw new InvalidOperationException($"Write value {engineering} is above max {tag.Max.Value} for tag '{tag.Name}'.");
    }

    return rawConverter(tag.ConvertEngineeringToRaw(engineering));
}

static void PrintCapabilities()
{
    Console.WriteLine("Implemented capabilities");
    Console.WriteLine("- Snap7 S7 TCP connect for S7-200 SMART, S7-1200, S7-1500 style endpoints.");
    Console.WriteLine("- S7 read: I/Q/M/DB plus S7-200 SMART V memory mapped as DB1.");
    Console.WriteLine("- Per-tag read quality: one bad point no longer fails the whole snapshot.");
    Console.WriteLine("- Device information probe with unsupported Snap7 calls reported as warnings.");
    Console.WriteLine("- Modbus TCP adapter for coils, discrete inputs, holding registers, and input registers.");
    Console.WriteLine("- Project JSON mode for sequential multi-device connect/read/device-info checks.");
    Console.WriteLine("- Guarded writes: --allow-write plus per-tag safeWrite=true, bounds check, and readback when possible.");
    Console.WriteLine();
    Console.WriteLine("Still pending for production");
    Console.WriteLine("- Real point table from the site device/program instead of sample V/M addresses.");
    Console.WriteLine("- Long-running multi-device scheduler, retry policy, storage, and alarm/event model.");
    Console.WriteLine("- Service deployment, operator UI, audit log, and permission model.");
    Console.WriteLine("- Field verification for Modbus devices and all write points.");
}
