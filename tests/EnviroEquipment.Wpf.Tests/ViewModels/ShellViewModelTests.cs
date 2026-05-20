using System.Linq;
using FluentAssertions;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg1")]
public class ShellViewModelTests
{
    private static ShellViewModel Build() => new(new OverviewViewModel(), new SingleDeviceViewModel());

    [Fact]
    public void Ctor_DefaultsToOverview()
    {
        var vm = Build();
        vm.ActiveScreen.Should().Be("overview");
        vm.ActiveScreenViewModel.Should().BeOfType<OverviewViewModel>();
    }

    [Fact]
    public void NavigateCommand_ChangesActiveScreen()
    {
        var vm = Build();
        vm.NavigateCommand.Execute("single");
        vm.ActiveScreen.Should().Be("single");
        vm.ActiveScreenViewModel.Should().BeOfType<SingleDeviceViewModel>();
    }

    [Fact]
    public void NavItems_AreOrderedAsInDesign()
    {
        var vm = Build();
        vm.NavItems.Should().HaveCountGreaterThanOrEqualTo(2);
        vm.NavItems[0].Id.Should().Be("overview");
        vm.NavItems[1].Id.Should().Be("single");
    }

    [Fact]
    public void NavItems_IncludeFullDesignListWithLaterPackagesDisabled()
    {
        var vm = Build();
        // The full 202605 left-nav list.
        vm.NavItems.Select(n => n.Id).Should().ContainInOrder(
            "overview", "single", "program", "history", "alarm", "lims",
            "layout", "device", "maint", "users", "settings");

        // Only overview + single are wired in Pkg 1.
        vm.NavItems.Single(n => n.Id == "overview").IsEnabled.Should().BeTrue();
        vm.NavItems.Single(n => n.Id == "single").IsEnabled.Should().BeTrue();
        // Everything else is a disabled placeholder.
        vm.NavItems.Where(n => n.Id is not ("overview" or "single"))
            .Should().OnlyContain(n => n.IsEnabled == false);
    }

    [Fact]
    public void NavigateCommand_IgnoresDisabledPlaceholders()
    {
        var vm = Build();
        vm.NavigateCommand.Execute("program"); // disabled
        vm.ActiveScreen.Should().Be("overview", "disabled nav entries are not navigable");
    }

    [Fact]
    public void Clock_IsPopulatedAtConstruction()
    {
        var vm = Build();
        vm.Clock.Should().MatchRegex(@"^\d{2}:\d{2}:\d{2}$");
        vm.ClockDate.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
    }

    [Fact]
    public void BrandAndBreadcrumb_MatchDesignDefaults()
    {
        var vm = Build();
        vm.BrandTitle.Should().Be("温箱");
        vm.LabName.Should().Be("环境可靠性 3F");
        vm.Shift.Should().Be("白班 B");
    }
}
