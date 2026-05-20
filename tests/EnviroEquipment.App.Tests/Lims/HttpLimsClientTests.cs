using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App.Lims;
using SiemensS7Demo.Domain.Lims;
using Xunit;

namespace EnviroEquipment.App.Tests.Lims;

[Trait("Category", "Pkg4")]
public class HttpLimsClientTests
{
    private static LimsTask MakeTask(string id, LimsTaskStatus status, string device = "TH-01", string project = "P") => new(
        Id: id, DeviceId: device, ProjectId: project, Name: "n",
        PlanStart: DateTimeOffset.UtcNow, PlanEnd: DateTimeOffset.UtcNow.AddHours(1),
        ActualStart: null, ActualEnd: null, Status: status);

    [Fact]
    public async Task ListTasks_ReturnsAllSeededTasks()
    {
        await using var server = LimsMockServer.Start(new[]
        {
            MakeTask("L-1", LimsTaskStatus.Todo),
            MakeTask("L-2", LimsTaskStatus.Running),
            MakeTask("L-3", LimsTaskStatus.Done),
        });
        using var http = new HttpClient { BaseAddress = server.BaseUri };
        var client = new HttpLimsClient(http,
            new LimsClientOptions { Mode = LimsClientMode.Http, BaseUrl = server.BaseUri.ToString() });

        var tasks = await client.ListTasksAsync(new LimsFilter(null, null, null), CancellationToken.None);

        tasks.Should().HaveCount(3);
        tasks.Select(t => t.Id).Should().BeEquivalentTo(new[] { "L-1", "L-2", "L-3" });
    }

    [Fact]
    public async Task ListTasks_FiltersByStatus()
    {
        await using var server = LimsMockServer.Start(new[]
        {
            MakeTask("L-1", LimsTaskStatus.Todo),
            MakeTask("L-2", LimsTaskStatus.Running)
        });
        using var http = new HttpClient { BaseAddress = server.BaseUri };
        var client = new HttpLimsClient(http,
            new LimsClientOptions { Mode = LimsClientMode.Http, BaseUrl = server.BaseUri.ToString() });

        var tasks = await client.ListTasksAsync(new LimsFilter(null, null, LimsTaskStatus.Running), CancellationToken.None);

        tasks.Should().ContainSingle().Which.Id.Should().Be("L-2");
    }

    [Fact]
    public async Task ListTasks_FiltersByDevice()
    {
        await using var server = LimsMockServer.Start(new[]
        {
            MakeTask("L-1", LimsTaskStatus.Todo, device: "TH-01"),
            MakeTask("L-2", LimsTaskStatus.Todo, device: "TH-02"),
        });
        using var http = new HttpClient { BaseAddress = server.BaseUri };
        var client = new HttpLimsClient(http,
            new LimsClientOptions { Mode = LimsClientMode.Http, BaseUrl = server.BaseUri.ToString() });

        var tasks = await client.ListTasksAsync(new LimsFilter("TH-02", null, null), CancellationToken.None);

        tasks.Should().ContainSingle().Which.Id.Should().Be("L-2");
    }

    [Fact]
    public async Task UploadResult_PostsToServer()
    {
        await using var server = LimsMockServer.Start();
        using var http = new HttpClient { BaseAddress = server.BaseUri };
        var client = new HttpLimsClient(http,
            new LimsClientOptions { Mode = LimsClientMode.Http, BaseUrl = server.BaseUri.ToString() });

        var when = DateTimeOffset.UtcNow;
        await client.UploadResultAsync(new LimsTaskResult("L-9", when, "{\"v\":1}"), CancellationToken.None);

        server.ReceivedResults.Should().ContainSingle();
        var received = server.ReceivedResults[0];
        received.TaskId.Should().Be("L-9");
        received.PayloadJson.Should().Be("{\"v\":1}");
        received.At.Should().BeCloseTo(when, TimeSpan.FromSeconds(1));
    }
}
