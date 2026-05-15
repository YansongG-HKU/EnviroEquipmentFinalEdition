using System.Collections.Generic;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ConfigValidationTemplateTests
{
    // ── Helper builders ───────────────────────────────────────────────────────

    private static TagDefinition MakeTag(string name, string address = "MW0") => new()
    {
        Name = name, DisplayName = name, Group = "g",
        Address = address, DataType = TagDataType.Int16, Unit = ""
    };

    private static DeviceTemplate MakeTemplate(
        string vendor = "Siemens",
        string model = "standardBoxDevice",
        List<TagDefinition>? tags = null) => new()
        {
            Vendor = vendor,
            Model = model,
            Tags = tags ?? new List<TagDefinition> { MakeTag("T") }
        };

    private static DeviceDefinition MakeDevice(
        string id = "dev",
        string? templateRef = null,
        List<TagDefinition>? tags = null) => new()
        {
            Id = id, Name = id, Protocol = "mock",
            Ip = "127.0.0.1", Port = 102, PollingIntervalMs = 1000,
            TemplateRef = templateRef,
            Tags = tags ?? new List<TagDefinition>()
        };

    // ── ValidateTemplates ─────────────────────────────────────────────────────

    [Fact]
    public void ValidateTemplates_EmptyList_NoIssues()
    {
        var issues = ConfigValidationService.ValidateTemplates(
            new List<DeviceTemplate>(), new List<DeviceDefinition>());

        issues.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTemplates_ValidTemplate_NoIssues()
    {
        var template = MakeTemplate();
        var device = MakeDevice(templateRef: template.Key,
            tags: new List<TagDefinition>());

        var issues = ConfigValidationService.ValidateTemplates(
            new List<DeviceTemplate> { template },
            new List<DeviceDefinition> { device });

        issues.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTemplates_EmptyVendor_IsError()
    {
        var template = new DeviceTemplate
        {
            Vendor = "",
            Model = "standardBoxDevice",
            Tags = new List<TagDefinition> { MakeTag("T") }
        };

        var issues = ConfigValidationService.ValidateTemplates(
            new List<DeviceTemplate> { template },
            new List<DeviceDefinition>());

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("Vendor", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTemplates_EmptyModel_IsError()
    {
        var template = new DeviceTemplate
        {
            Vendor = "Siemens",
            Model = "",
            Tags = new List<TagDefinition> { MakeTag("T") }
        };

        var issues = ConfigValidationService.ValidateTemplates(
            new List<DeviceTemplate> { template },
            new List<DeviceDefinition>());

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("Model", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTemplates_DuplicateKey_IsError()
    {
        var t1 = MakeTemplate("Siemens", "standardBoxDevice");
        var t2 = MakeTemplate("Siemens", "standardBoxDevice");

        var issues = ConfigValidationService.ValidateTemplates(
            new List<DeviceTemplate> { t1, t2 },
            new List<DeviceDefinition>());

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("Siemens/standardBoxDevice", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateTemplates_TagWithInvalidScale_IsError()
    {
        var badTag = new TagDefinition
        {
            Name = "Bad", DisplayName = "Bad", Group = "g",
            Address = "MW0", DataType = TagDataType.Int16, Unit = "",
            Scale = 0.0, ScaleMode = ScaleMode.Divisor
        };
        var template = new DeviceTemplate
        {
            Vendor = "Siemens",
            Model = "standardBoxDevice",
            Tags = new List<TagDefinition> { badTag }
        };

        var issues = ConfigValidationService.ValidateTemplates(
            new List<DeviceTemplate> { template },
            new List<DeviceDefinition>());

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Scope.StartsWith("template:Siemens/standardBoxDevice"));
    }

    // ── TemplateRef resolvability in ValidateProject ──────────────────────────

    [Fact]
    public void ValidateProject_DeviceWithUnresolvableTemplateRef_IsError()
    {
        var project = new ProjectDefinition
        {
            ProjectId = "test",
            Templates = new List<DeviceTemplate>(),
            Devices = new List<DeviceDefinition>
            {
                // Manually construct a device with tags (simulates post-Load state
                // for a project that somehow has a dangling TemplateRef).
                new()
                {
                    Id = "d1", Name = "D1", Protocol = "mock",
                    Ip = "127.0.0.1", Port = 102, PollingIntervalMs = 1000,
                    TemplateRef = "Siemens/missing",
                    Tags = new List<TagDefinition> { MakeTag("T") }
                }
            }
        };

        var issues = ConfigValidationService.ValidateProject(project);

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("Siemens/missing", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateProject_ResolvedTemplateDevice_PassesValidation()
    {
        // Simulate the post-Load state: template has been resolved into device.Tags.
        var project = new ProjectDefinition
        {
            ProjectId = "test",
            Templates = new List<DeviceTemplate>
            {
                MakeTemplate()
            },
            Devices = new List<DeviceDefinition>
            {
                new()
                {
                    Id = "d1", Name = "D1", Protocol = "mock",
                    Ip = "127.0.0.1", Port = 102, PollingIntervalMs = 1000,
                    TemplateRef = "Siemens/standardBoxDevice",
                    Tags = new List<TagDefinition> { MakeTag("T") }
                }
            }
        };

        var issues = ConfigValidationService.ValidateProject(project);

        issues.Should().NotContain(i => i.Severity == ConfigIssueSeverity.Error);
    }
}
