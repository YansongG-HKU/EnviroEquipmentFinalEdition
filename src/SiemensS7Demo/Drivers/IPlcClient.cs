using SiemensS7Demo.Models;

namespace SiemensS7Demo.Drivers;

public interface IPlcClient
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    bool IsConnected { get; }

    Task<IReadOnlyDictionary<string, TagValue>> ReadTagsAsync(
        IReadOnlyList<TagDefinition> tags,
        CancellationToken cancellationToken);

    Task WriteTagAsync(
        TagDefinition tag,
        object value,
        CancellationToken cancellationToken);
}
