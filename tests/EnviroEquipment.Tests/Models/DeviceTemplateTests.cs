using FluentAssertions;
using SiemensS7Demo.Models;
using Xunit;

namespace EnviroEquipment.Tests.Models;

public class DeviceTemplateTests
{
    [Fact]
    public void DeviceTemplate_RequiredFields_CanBeSet()
    {
        var template = new DeviceTemplate
        {
            Vendor = "Siemens",
            Model = "standardBoxDevice",
            Tags = new List<TagDefinition>
            {
                new()
                {
                    Name = "Temp", DisplayName = "Temperature", Group = "PID",
                    Address = "DB1.DBW336", DataType = TagDataType.Int16, Unit = "degC",
                    Scale = 10.0, ScaleMode = ScaleMode.Divisor
                }
            }
        };

        template.Vendor.Should().Be("Siemens");
        template.Model.Should().Be("standardBoxDevice");
        template.Tags.Should().HaveCount(1);
    }

    [Fact]
    public void DeviceTemplate_Auxiliaries_DefaultEmpty()
    {
        var template = new DeviceTemplate
        {
            Vendor = "Schneider",
            Model = "standardBoxDevice",
            Tags = new List<TagDefinition>()
        };

        template.Auxiliaries.Should().BeEmpty();
    }

    [Fact]
    public void DeviceTemplate_Key_IsVendorSlashModel()
    {
        var template = new DeviceTemplate
        {
            Vendor = "Siemens",
            Model = "TemperatureShockBoxDevice",
            Tags = new List<TagDefinition>()
        };

        template.Key.Should().Be("Siemens/TemperatureShockBoxDevice");
    }

    [Fact]
    public void DeviceTemplate_CanCarryAuxiliaries()
    {
        var template = new DeviceTemplate
        {
            Vendor = "Siemens",
            Model = "standardBoxDevice",
            Tags = new List<TagDefinition>(),
            Auxiliaries = new List<AuxiliaryFunction>
            {
                new() { Group = "手动辅助功能", ControlTagName = "CompressorStart", StateTagName = "CompressorRun" }
            }
        };

        template.Auxiliaries.Should().HaveCount(1);
    }

    [Fact]
    public void ProjectDefinition_Templates_DefaultEmpty()
    {
        var project = new ProjectDefinition();
        project.Templates.Should().BeEmpty();
    }

    [Fact]
    public void DeviceDefinition_TemplateRef_DefaultNull()
    {
        var device = new DeviceDefinition();
        device.TemplateRef.Should().BeNull();
    }
}
