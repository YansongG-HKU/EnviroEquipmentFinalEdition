using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Drivers;

public sealed class SiemensS7Client : IPlcClient
{
    private readonly PlcConnectionOptions _options;
    private readonly IS7Adapter _adapter;
    private readonly SemaphoreSlim _requestLock = new(1, 1);

    public SiemensS7Client(PlcConnectionOptions options, IS7Adapter adapter)
    {
        _options = options;
        _adapter = adapter;
    }

    public bool IsConnected => _adapter.IsConnected;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                return;
            }

            await _adapter.ConnectAsync(_options, cancellationToken);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsConnected)
            {
                return;
            }

            await _adapter.DisconnectAsync(cancellationToken);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async Task<PlcDeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("PLC not connected.");
        }

        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            return await _adapter.GetDeviceInfoAsync(_options, cancellationToken);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, TagValue>> ReadTagsAsync(
        IReadOnlyList<TagDefinition> tags,
        CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("PLC not connected.");
        }

        var output = new Dictionary<string, TagValue>(StringComparer.OrdinalIgnoreCase);

        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            var batch = await _adapter.ReadRawBatchAsync(tags, cancellationToken);
            foreach (var tag in tags)
            {
                TagValue hostValue;
                if (!batch.TryGetValue(tag.Name, out var result))
                {
                    hostValue = new TagValue
                    {
                        Name = tag.Name,
                        DisplayName = tag.DisplayName,
                        Address = tag.Address,
                        Unit = tag.Unit,
                        Value = string.Empty,
                        TimestampUtc = DateTime.UtcNow,
                        IsQualityGood = false,
                        QualityMessage = "Adapter omitted this tag from the batch result."
                    };
                }
                else if (!result.IsGood)
                {
                    hostValue = new TagValue
                    {
                        Name = tag.Name,
                        DisplayName = tag.DisplayName,
                        Address = tag.Address,
                        Unit = tag.Unit,
                        Value = string.Empty,
                        TimestampUtc = DateTime.UtcNow,
                        IsQualityGood = false,
                        QualityMessage = result.Error
                    };
                }
                else
                {
                    var raw = result.Value!;
                    var converted = ConvertReadValue(tag, raw);
                    string? displayValue = null;
                    if (tag.Options.Count > 0)
                    {
                        var rawLong = ToOptionKey(raw);
                        if (rawLong.HasValue && tag.TryGetOptionLabel(rawLong.Value, out var label))
                        {
                            displayValue = label;
                        }
                    }

                    hostValue = new TagValue
                    {
                        Name = tag.Name,
                        DisplayName = tag.DisplayName,
                        Address = tag.Address,
                        Unit = tag.Unit,
                        RawValue = raw,
                        Value = converted,
                        DisplayValue = displayValue,
                        TimestampUtc = DateTime.UtcNow,
                        IsQualityGood = true
                    };
                }

                output[tag.Name] = hostValue;
                foreach (var derivation in tag.BitDerivations)
                {
                    output[derivation.Name] = BuildDerivedTagValue(tag, derivation, hostValue);
                }
            }
        }
        finally
        {
            _requestLock.Release();
        }

        return output;
    }

    public async Task WriteTagAsync(TagDefinition tag, object value, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("PLC not connected.");
        }

        if (tag.Access == TagAccess.Read)
        {
            throw new InvalidOperationException($"Tag {tag.Name} is read-only.");
        }

        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            await _adapter.WriteRawAsync(tag, value, cancellationToken);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static object ConvertReadValue(TagDefinition tag, object raw)
    {
        if (tag.DataType == TagDataType.Bool)
        {
            return raw;
        }

        var numeric = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
        return tag.ConvertRawToEngineering(numeric);
    }

    private static long? ToOptionKey(object raw) => raw switch
    {
        bool b => b ? 1L : 0L,
        long l => l,
        int i => i,
        short s => s,
        ushort u => u,
        uint u2 => u2,
        _ => null
    };

    private static TagValue BuildDerivedTagValue(TagDefinition host, BitDerivation derivation, TagValue hostValue)
    {
        if (!hostValue.IsQualityGood)
        {
            return new TagValue
            {
                Name = derivation.Name,
                DisplayName = derivation.DisplayName ?? derivation.Name,
                Address = $"{host.Address}.{derivation.BitOffset}",
                Unit = string.Empty,
                Value = string.Empty,
                TimestampUtc = hostValue.TimestampUtc,
                IsQualityGood = false,
                QualityMessage = hostValue.QualityMessage
            };
        }

        var rawLong = ToOptionKey(hostValue.RawValue ?? 0) ?? 0L;
        var bit = ((rawLong >> derivation.BitOffset) & 1L) == 1L;

        return new TagValue
        {
            Name = derivation.Name,
            DisplayName = derivation.DisplayName ?? derivation.Name,
            Address = $"{host.Address}.{derivation.BitOffset}",
            Unit = string.Empty,
            RawValue = bit,
            Value = bit,
            TimestampUtc = hostValue.TimestampUtc,
            IsQualityGood = true
        };
    }
}
