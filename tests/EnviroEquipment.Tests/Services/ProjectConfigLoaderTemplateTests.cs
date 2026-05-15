using System.IO;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ProjectConfigLoaderTemplateTests
{
    private static string TemplateFixture =>
        Path.Combine(System.AppContext.BaseDirectory, "Services", "Fixtures", "templates.project.json");

    // ── Resolution happy path ─────────────────────────────────────────────────

    [Fact]
    public void Load_TemplateDevice_ResolvesTwoTagsFromTemplate()
    {
        var project = ProjectConfigLoader.Load(TemplateFixture);

        var boxA = project.Devices.Single(d => d.Id == "box-A");
        boxA.Tags.Should().HaveCount(2);
        boxA.Tags.Select(t => t.Name).Should().BeEquivalentTo(new[] { "Temp", "CompressorRun" });
    }

    [Fact]
    public void Load_TemplateDevice_ResolvesAuxiliariesFromTemplate()
    {
        var project = ProjectConfigLoader.Load(TemplateFixture);

        var boxA = project.Devices.Single(d => d.Id == "box-A");
        boxA.Auxiliaries.Should().HaveCount(1);
        boxA.Auxiliaries[0].Group.Should().Be("手动辅助功能");
        boxA.Auxiliaries[0].ControlTagName.Should().Be("CompressorStart");
    }

    [Fact]
    public void Load_TwoTemplateDevices_BothResolveIndependently()
    {
        var project = ProjectConfigLoader.Load(TemplateFixture);

        var boxA = project.Devices.Single(d => d.Id == "box-A");
        var boxB = project.Devices.Single(d => d.Id == "box-B");

        // Both have 2 tags from the same template.
        boxA.Tags.Should().HaveCount(2);
        boxB.Tags.Should().HaveCount(2);

        // They are separate instances (not shared by reference).
        boxA.Tags.Should().NotBeSameAs(boxB.Tags);
    }

    [Fact]
    public void Load_StandaloneDevice_PreservesOwnTags()
    {
        var project = ProjectConfigLoader.Load(TemplateFixture);

        var standalone = project.Devices.Single(d => d.Id == "standalone");
        standalone.Tags.Should().HaveCount(1);
        standalone.Tags[0].Name.Should().Be("Pressure");
    }

    [Fact]
    public void Load_TemplateDevice_PreservesDeviceIdentityFields()
    {
        var project = ProjectConfigLoader.Load(TemplateFixture);

        var boxA = project.Devices.Single(d => d.Id == "box-A");
        boxA.Id.Should().Be("box-A");
        boxA.Ip.Should().Be("192.168.1.10");
        boxA.Protocol.Should().Be("mock");
    }

    // ── Template tag field fidelity ───────────────────────────────────────────

    [Fact]
    public void Load_TemplateDevice_TagPreservesScaleAndScaleMode()
    {
        var project = ProjectConfigLoader.Load(TemplateFixture);

        var temp = project.Devices.Single(d => d.Id == "box-A")
            .Tags.Single(t => t.Name == "Temp");

        temp.Scale.Should().Be(10.0);
        temp.ScaleMode.Should().Be(ScaleMode.Divisor);
        temp.DataType.Should().Be(TagDataType.Int16);
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Load_MissingTemplate_ThrowsInvalidOperationException()
    {
        var json = """
            {
              "projectId": "err",
              "projectName": "Err",
              "templates": [],
              "devices": [
                {
                  "id": "d1", "name": "D1", "enabled": true,
                  "protocol": "mock", "ip": "127.0.0.1", "port": 102,
                  "cpuType": "Mock", "pollingIntervalMs": 1000,
                  "templateRef": "Siemens/nonExistentModel"
                }
              ]
            }
            """;
        var path = WriteTemp(json);

        var act = () => ProjectConfigLoader.Load(path);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Siemens/nonExistentModel*");
    }

    [Fact]
    public void Load_TemplateRefAndTagsBoth_ThrowsInvalidOperationException()
    {
        var json = """
            {
              "projectId": "err",
              "projectName": "Err",
              "templates": [
                {
                  "vendor": "Siemens",
                  "model": "standardBoxDevice",
                  "tags": [
                    { "name": "T", "displayName": "T", "group": "g",
                      "address": "MW0", "dataType": "Int16", "unit": "" }
                  ]
                }
              ],
              "devices": [
                {
                  "id": "d1", "name": "D1", "enabled": true,
                  "protocol": "mock", "ip": "127.0.0.1", "port": 102,
                  "cpuType": "Mock", "pollingIntervalMs": 1000,
                  "templateRef": "Siemens/standardBoxDevice",
                  "tags": [
                    { "name": "Own", "displayName": "Own", "group": "g",
                      "address": "MW2", "dataType": "Int16", "unit": "" }
                  ]
                }
              ]
            }
            """;
        var path = WriteTemp(json);

        var act = () => ProjectConfigLoader.Load(path);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*templateRef*tags*");
    }

    [Fact]
    public void Load_NoTemplateRefAndNoTags_ThrowsInvalidOperationException()
    {
        var json = """
            {
              "projectId": "err",
              "projectName": "Err",
              "devices": [
                {
                  "id": "d1", "name": "D1", "enabled": true,
                  "protocol": "mock", "ip": "127.0.0.1", "port": 102,
                  "cpuType": "Mock", "pollingIntervalMs": 1000
                }
              ]
            }
            """;
        var path = WriteTemp(json);

        var act = () => ProjectConfigLoader.Load(path);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no tags*");
    }

    private static string WriteTemp(string json)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"gap9-test-{System.Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, json);
        return path;
    }
}
