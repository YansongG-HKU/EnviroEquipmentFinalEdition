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
}
