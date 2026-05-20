using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Lims;
using SiemensS7Demo.Domain.Lims;
using Xunit;

namespace EnviroEquipment.App.Tests.Lims;

[Trait("Category", "Pkg4")]
public class FileWatcherLimsClientPathTraversalTests
{
    private static FileWatcherLimsClient MakeClient(string watchDir)
        => new(new LimsClientOptions { Mode = LimsClientMode.File, WatchDirectory = watchDir });

    /// <summary>
    /// Defense against a malicious LIMS-server-supplied TaskId trying to climb out of the
    /// configured WatchDirectory. The pre-fix code blindly Path.Combine'd TaskId and wrote
    /// `&lt;TaskId&gt;.result.json`, so a TaskId like "../../etc/passwd" would write outside
    /// the directory. Post-fix, the resolved path must be confined to WatchDirectory.
    /// </summary>
    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("../../etc/passwd")]
    [InlineData("foo/bar")]
    [InlineData("foo\\bar")]
    public async Task UploadResult_RejectsTaskIdsThatEscapeWatchDirectory(string hostileTaskId)
    {
        var tmp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"lims-trav-{Guid.NewGuid()}"));
        try
        {
            var client = MakeClient(tmp.FullName);
            var result = new LimsTaskResult(hostileTaskId, DateTimeOffset.UtcNow, "{}");

            Func<Task> act = () => client.UploadResultAsync(result, CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentException>(
                $"TaskId '{hostileTaskId}' contains path separators or traversal segments and must be rejected");
        }
        finally
        {
            tmp.Delete(true);
        }
    }

    [Fact]
    public async Task UploadResult_AcceptsNormalTaskIds()
    {
        var tmp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"lims-ok-{Guid.NewGuid()}"));
        try
        {
            var client = MakeClient(tmp.FullName);
            var result = new LimsTaskResult("normaltask", DateTimeOffset.UtcNow, "{}");

            await client.UploadResultAsync(result, CancellationToken.None);

            var expected = Path.Combine(tmp.FullName, "normaltask.result.json");
            File.Exists(expected).Should().BeTrue();
        }
        finally
        {
            tmp.Delete(true);
        }
    }
}
