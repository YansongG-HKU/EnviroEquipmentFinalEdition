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
            foreach (var tag in tags)
            {
                var raw = await _adapter.ReadRawAsync(tag, cancellationToken);
                output[tag.Name] = new TagValue
                {
                    Name = tag.Name,
                    Value = raw,
                    TimestampUtc = DateTime.UtcNow,
                    IsQualityGood = true
                };
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
}
