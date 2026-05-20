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

    public IReadOnlyList<Device> CurrentSnapshots() =>
        _sessions.Values
            .Select(s => s.LatestSnapshot)
            .Where(d => d.Id.Value != "__sentinel__")
            .ToList();

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

        // Public read-only snapshot for IDeviceSessionManager.CurrentSnapshots() — same instance
        // the polling loop mutates. Consumers that need a frozen value should copy what they read.
        public Device LatestSnapshot => _state;
        private readonly TagDefinition _pvTag;
        private readonly TagDefinition _svTag;
        private readonly DeviceSeed? _seed;
        private readonly int _phase;
        private bool _connected;
        private int _tick;
        // Once the operator writes a setpoint, the seed's TempSet no longer drives the SV — the
        // live readback does. Keeps the headless smoke (write 77.5 -> read 77.5) honest.
        private volatile bool _svOverridden;

        public DeviceSession(DeviceProvisioning prov, ILogger logger)
        {
            _prov = prov;
            _logger = logger;
            _seed = prov.Seed;
            // Deterministic per-device phase so each card's simulated wave is offset (mirrors the
            // seeded sparklines in components-core.jsx genSeries()).
            _phase = 0;
            foreach (var ch in prov.Id) _phase += ch;
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

            // Seed the InMemory backing store so the very first read returns realistic values
            // instead of 0.0. The simulated wobble is applied per-poll on top of the SV.
            if (_seed is not null)
            {
                if (_seed.TempSet is double ts)
                {
                    _ = _client.WriteTagAsync(_svTag, (float)ts, CancellationToken.None);
                }
                _state.Program = ToProgram(_seed);
            }
        }

        private static DeviceProgram ToProgram(DeviceSeed s) => new(
            Name: s.ProgName,
            Seg: s.Seg,
            SegTotal: s.SegTotal,
            Cycle: s.Cycle,
            CycleTotal: s.CycleTotal,
            RemainSec: s.RemainSec,
            Progress: s.Progress,
            AlarmCode: s.AlarmCode,
            AlarmMessage: s.AlarmMessage,
            Note: s.Note);

        public async Task RunAsync(TimeSpan interval, BehaviorSubject<Device> sink, CancellationToken ct)
        {
            try
            {
                // A seeded offline device never connects — it reports Offline forever (mirrors the
                // 离线 card in the design, which shows no live readings).
                if (_seed is { Status: DeviceStatus.Offline })
                {
                    _state.Status = DeviceStatus.Offline;
                    _state.LastReading = null;
                    sink.OnNext(_state);
                    return;
                }

                await _client.ConnectAsync(ct);
                _connected = true;
                _state.Status = _seed?.Status ?? DeviceStatus.Idle;
                sink.OnNext(_state);

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var snap = await _client.ReadTagsAsync(new[] { _pvTag, _svTag }, ct);
                        var svRaw = snap.TryGetValue(_svTag.Name, out var svV)
                            ? Convert.ToDouble(svV.Value, System.Globalization.CultureInfo.InvariantCulture)
                            : (double?)null;
                        var pvRaw = snap.TryGetValue(_pvTag.Name, out var pvV)
                            ? Convert.ToDouble(pvV.Value, System.Globalization.CultureInfo.InvariantCulture)
                            : (double?)null;

                        // Effective setpoint: the seeded SV until the operator writes one, then the
                        // live readback wins. Unseeded devices always use the readback.
                        var sv = (_seed?.TempSet is double seedSv && !_svOverridden) ? seedSv : svRaw;
                        // Simulate a wavy PV around the setpoint for seeded devices so cards/sparklines
                        // animate like the design. Unseeded devices keep raw readback behavior.
                        double? pv;
                        double? humid = null;
                        if (_seed is not null)
                        {
                            _tick++;
                            var wobble = Math.Sin((_tick + _phase) * 0.30) * 0.6 + Math.Sin((_tick + _phase) * 0.11) * 0.4;
                            var basePv = _svOverridden ? (sv ?? 0) : (_seed.Temp ?? sv ?? 0);
                            pv = Math.Round(basePv + wobble, 2);
                            if (_seed.Humid is double h)
                            {
                                var hWobble = Math.Sin((_tick + _phase) * 0.27 + 1.0) * 0.5;
                                humid = Math.Round(h + hWobble, 2);
                            }
                        }
                        else
                        {
                            pv = pvRaw;
                        }

                        _state.LastReading = new ReadingSnapshot(
                            DateTimeOffset.UtcNow, pv, sv, humid, _seed?.HumidSet, null, null);
                        _state.Setpoints = new Setpoints(sv, _seed?.HumidSet, null);
                        // Honor the seeded status; unseeded devices report Run while polling succeeds.
                        _state.Status = _seed?.Status ?? DeviceStatus.Run;
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
                _svOverridden = true;
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
