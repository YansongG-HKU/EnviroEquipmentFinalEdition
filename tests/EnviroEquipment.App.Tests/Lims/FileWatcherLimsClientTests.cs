using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Lims;
using SiemensS7Demo.Domain.Lims;
using Xunit;

namespace EnviroEquipment.App.Tests.Lims;

[Trait("Category", "Pkg4")]
public class FileWatcherLimsClientTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Fact]
    public async Task ListTasks_ReadsLatestSnapshotFromWatchDirectory()
    {
        var tmp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"lims-{Guid.NewGuid()}"));
        try
        {
            var snapshot = new[]
            {
                new LimsTask("L-1","TH-01","P","n",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
                    null, null, LimsTaskStatus.Todo)
            };
            await File.WriteAllTextAsync(Path.Combine(tmp.FullName, "tasks.json"),
                JsonSerializer.Serialize(snapshot, Json));

            var client = new FileWatcherLimsClient(new LimsClientOptions
            {
                Mode = LimsClientMode.File,
                WatchDirectory = tmp.FullName
            });

            var tasks = await client.ListTasksAsync(new LimsFilter(null, null, null), CancellationToken.None);
            tasks.Should().HaveCount(1);
            tasks[0].Id.Should().Be("L-1");
        }
        finally { tmp.Delete(true); }
    }

    [Fact]
    public async Task ListTasks_EmptyDirectory_ReturnsEmpty()
    {
        var tmp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"lims-empty-{Guid.NewGuid()}"));
        try
        {
            var client = new FileWatcherLimsClient(new LimsClientOptions
            {
                Mode = LimsClientMode.File,
                WatchDirectory = tmp.FullName
            });

            var tasks = await client.ListTasksAsync(new LimsFilter(null, null, null), CancellationToken.None);
            tasks.Should().BeEmpty();
        }
        finally { tmp.Delete(true); }
    }

    [Fact]
    public async Task UploadResult_WritesResultFile()
    {
        var tmp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"lims-{Guid.NewGuid()}"));
        try
        {
            var client = new FileWatcherLimsClient(new LimsClientOptions
            {
                Mode = LimsClientMode.File,
                WatchDirectory = tmp.FullName
            });
            await client.UploadResultAsync(
                new LimsTaskResult("L-9", DateTimeOffset.UtcNow, "{\"v\":1}"), CancellationToken.None);

            var resultFile = Path.Combine(tmp.FullName, "L-9.result.json");
            File.Exists(resultFile).Should().BeTrue();
            var contents = await File.ReadAllTextAsync(resultFile);
            contents.Should().Contain("L-9");
            // System.Text.Json default-escapes ASCII `"` to ". Both forms are valid JSON for "v":1.
            (contents.Contains("\"v\":1") || contents.Contains("\\u0022v\\u0022:1"))
                .Should().BeTrue("the result file must serialize the payload's v=1 pair");
        }
        finally { tmp.Delete(true); }
    }

    [Fact]
    public void Constructor_Throws_WhenWatchDirectoryMissing()
    {
        var act = () => new FileWatcherLimsClient(new LimsClientOptions { Mode = LimsClientMode.File });
        act.Should().Throw<ArgumentException>();
    }
}
