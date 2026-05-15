using System.Collections.Generic;
using FluentAssertions;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;
using Xunit;

namespace EnviroEquipment.Tests.Services;

public class ConfigValidationAuxiliaryTests
{
    private static TagDefinition MakeTag(string name) => new()
    {
        Name = name, DisplayName = name, Group = "g",
        Address = "MW0", DataType = TagDataType.Bool, Unit = ""
    };

    [Fact]
    public void NeitherStateNorBitOffset_IsError()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "手动辅助功能",
            ControlTagName = "CtrlTag"
            // StateTagName = null, ProgramBitOffset = null
        };
        var tags = new[] { MakeTag("CtrlTag") };

        var issues = ConfigValidationService.ValidateAuxiliaries(
            new[] { aux }, tags, "test");

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("StateTagName", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PairMode_BothTagsExist_NoIssues()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "手动辅助功能",
            ControlTagName = "Ctrl",
            StateTagName = "State"
        };
        var tags = new[] { MakeTag("Ctrl"), MakeTag("State") };

        var issues = ConfigValidationService.ValidateAuxiliaries(
            new[] { aux }, tags, "test");

        issues.Should().BeEmpty();
    }

    [Fact]
    public void PairMode_MissingStateTag_IsWarning()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "手动辅助功能",
            ControlTagName = "Ctrl",
            StateTagName = "MissingState"
        };
        var tags = new[] { MakeTag("Ctrl") };  // MissingState not in list

        var issues = ConfigValidationService.ValidateAuxiliaries(
            new[] { aux }, tags, "test");

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Warning &&
            i.Message.Contains("MissingState", System.StringComparison.OrdinalIgnoreCase));
        issues.Should().NotContain(i => i.Severity == ConfigIssueSeverity.Error);
    }

    [Fact]
    public void PairMode_MissingControlTag_IsWarning()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "手动辅助功能",
            ControlTagName = "MissingCtrl",
            StateTagName = "State"
        };
        var tags = new[] { MakeTag("State") };

        var issues = ConfigValidationService.ValidateAuxiliaries(
            new[] { aux }, tags, "test");

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Warning &&
            i.Message.Contains("MissingCtrl", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BitOffsetMode_ValidOffset_NoIssues()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "程序辅助功能",
            ControlTagName = "Ctrl",
            ProgramBitOffset = 7
        };
        var tags = new[] { MakeTag("Ctrl") };

        var issues = ConfigValidationService.ValidateAuxiliaries(
            new[] { aux }, tags, "test");

        issues.Should().NotContain(i => i.Severity == ConfigIssueSeverity.Error);
    }

    [Fact]
    public void BitOffsetMode_OffsetOutOfRange_IsError()
    {
        var aux = new AuxiliaryFunction
        {
            Group = "程序辅助功能",
            ControlTagName = "Ctrl",
            ProgramBitOffset = 16  // 0..15 only
        };
        var tags = new[] { MakeTag("Ctrl") };

        var issues = ConfigValidationService.ValidateAuxiliaries(
            new[] { aux }, tags, "test");

        issues.Should().Contain(i =>
            i.Severity == ConfigIssueSeverity.Error &&
            i.Message.Contains("ProgramBitOffset", System.StringComparison.OrdinalIgnoreCase));
    }
}
