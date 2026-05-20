using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.App;

public sealed class DeviceSessionManager : IDeviceSessionManager, IAsyncDisposable
{
    private readonly ProjectConfig _config;
    private readonly ILogger<DeviceSessionManager> _logger;
    private readonly TimeSpan _pollingInterval;
    private readonly BehaviorSubject<Device> _subject =
        new(new Device { Id = new DeviceId("__sentinel__"), Bay = string.Empty, Type = DeviceType.Standard });

    private readonly ConcurrentDictionary<string, DeviceSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _connectLock = new();
    private bool _connected;
    private CancellationTokenSource? _runCts;

    public DeviceSessionManager(
        ProjectConfig config,
        ILogger<DeviceSessionManager> logger)
        : this(config, logger, TimeSpan.FromSeconds(1)) { }

    public DeviceSessionManager(
        ProjectConfig config,
        ILogger<DeviceSessionManager> logger,
        TimeSpan pollingInterval)
    {
        _config = config;
        _logger = logger;
        _pollingInterval = pollingInterval;
    }

    public IObservable<Device> Devices =>
        _subject.Where(d => d.Id.Value != "__sentinel__");

    public Task ConnectAllAsync(CancellationToken ct)
    {
        lock (_connectLock)
        {
            if (_connected) return Task.CompletedTask;
            _connected = true;
        }

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        foreach (var prov in _config.Devices)
        {
            var session = new DeviceSession(prov, _logger);
            if (_sessions.TryAdd(prov.Id, session))
            {
                _ = Task.Run(() => session.RunAsync(_pollingInterval, _subject, _runCts.Token));
            }
        }
        return Task.CompletedTask;
    }

    public async Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(id.Value, out var session))
        {
            return DeviceWriteResult.Failure("UNKNOWN_DEVICE", $"Unknown device id '{id.Value}'.");
        }

        try
        {
            await session.WriteSetpointAsync(sp, ct);
            return DeviceWriteResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setpoint write failed for {DeviceId}", id.Value);
            return DeviceWriteResult.Failure("WRITE_FAILED", ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _runCts?.Cancel(); } catch { /* ignore */ }
        var tasks = _sessions.Values.Select(s => s.DisposeAsync().AsTask()).ToArray();
        await Task.WhenAll(tasks);
        _sessions.Clear();
        _subject.OnCompleted();
        _subject.Dispose();
        _runCts?.Dispose();
    }

    private sealed class DeviceSession : IAsyncDisposable
    {
        private readonly DeviceProvisioning _prov;
        private readonly ILogger _logger;
        private readonly SiemensS7Client _client;
        private readonly Device _state;
        private readonly TagDefinition _pvTag;
        private readonly TagDefinition _svTag;
        private bool _connected;

        public DeviceSession(DeviceProvisioning prov, ILogger logger)
        {
            _prov = prov;
            _logger = logger;
            var opts = new PlcConnectionOptions
            {
                Name = prov.Id,
                IpAddress = prov.IpAddress,
                Port = prov.Port,
                CpuType = prov.CpuType,
                Rack = prov.Rack,
                Slot = prov.Slot,
            };
            IS7Adapter adapter = prov.UseInMemoryAdapter
                ? new InMemoryS7Adapter()
                : new InMemoryS7Adapter(); // Real Snap7 adapter is a Pkg 1+ follow-up; InMemory keeps tests offline-safe.
            _client = new SiemensS7Client(opts, adapter);

            _pvTag = new TagDefinition
            {
                Name = prov.PvTagName, DisplayName = prov.PvTagName, Group = "Pv",
                Address = prov.PvAddress, DataType = TagDataType.Real, Unit = "C",
                Access = TagAccess.Read,
            };
            _svTag = new TagDefinition
            {
                Name = prov.SvTagName, DisplayName = prov.SvTagName, Group = "Sv",
                Address = prov.SvAddress, DataType = TagDataType.Real, Unit = "C",
                Access = TagAccess.ReadWrite,
            };
            _state = new Device
            {
                Id = new DeviceId(prov.Id),
                Bay = prov.Bay,
                Type = prov.Type,
                Status = DeviceStatus.Idle,
            };
        }

        public async Task RunAsync(TimeSpan interval, BehaviorSubject<Device> sink, CancellationToken ct)
        {
            try
            {
                await _client.ConnectAsync(ct);
                _connected = true;
                _state.Status = DeviceStatus.Idle;
                sink.OnNext(_state);

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var snap = await _client.ReadTagsAsync(new[] { _pvTag, _svTag }, ct);
                        var pv = snap.TryGetValue(_pvTag.Name, out var pvV)
                            ? Convert.ToDouble(pvV.Value, System.Globalization.CultureInfo.InvariantCulture)
                            : (double?)null;
                        var sv = snap.TryGetValue(_svTag.Name, out var svV)
                            ? Convert.ToDouble(svV.Value, System.Globalization.CultureInfo.InvariantCulture)
                            : (double?)null;

                        _state.LastReading = new ReadingSnapshot(
                            DateTimeOffset.UtcNow, pv, sv, null, null, null, null);
                        _state.Setpoints = new Setpoints(sv, null, null);
                        _state.Status = DeviceStatus.Run;
                        sink.OnNext(_state);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Polling failed for {DeviceId}; marking offline.", _prov.Id);
                        _state.Status = DeviceStatus.Offline;
                        sink.OnNext(_state);
                    }

                    try { await Task.Delay(interval, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
            catch (OperationCanceledException) { /* expected on shutdown */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Session loop crashed for {DeviceId}", _prov.Id);
            }
        }

        public async Task WriteSetpointAsync(Setpoints sp, CancellationToken ct)
        {
            if (sp.Temp is double t)
            {
                // The polling loop connects asynchronously; a write issued right after
                // ConnectAllAsync may arrive before the loop's connect completes. ConnectAsync
                // is idempotent, so ensure the client is connected before writing.
                if (!_client.IsConnected)
                {
                    await _client.ConnectAsync(ct);
                    _connected = true;
                }
                await _client.WriteTagAsync(_svTag, (float)t, ct);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_connected)
                {
                    await _client.DisconnectAsync(CancellationToken.None);
                }
            }
            catch
            {
                // best-effort disconnect during shutdown.
            }
        }
    }
}
