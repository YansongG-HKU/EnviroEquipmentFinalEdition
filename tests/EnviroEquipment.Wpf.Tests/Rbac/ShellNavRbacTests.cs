using System.Linq;
using FluentAssertions;
using SiemensS7Demo.Domain.Users;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.Rbac;

[Trait("Category", "Pkg4")]
public class ShellNavRbacTests
{
    private static ShellViewModel MakeShell()
    {
        // ShellViewModel needs an OverviewViewModel + SingleDeviceViewModel; neither is RBAC-relevant
        // for nav-visibility, so we resolve them with stub dependencies.
        var overview = new OverviewViewModel();
        var single = new SingleDeviceViewModel();
        return new ShellViewModel(overview, single);
    }

    [Fact]
    public void ApplyRbac_NullUser_HidesEngineerAndAdminEntries()
    {
        var shell = MakeShell();

        shell.ApplyRbac(null);

        // Engineer-gated: program, layout, device, maint. Admin-gated: users, settings.
        shell.NavItems.Single(n => n.Id == "program").IsVisible.Should().BeFalse();
        shell.NavItems.Single(n => n.Id == "users").IsVisible.Should().BeFalse();
        // Public entries stay visible
        shell.NavItems.Single(n => n.Id == "overview").IsVisible.Should().BeTrue();
        shell.NavItems.Single(n => n.Id == "single").IsVisible.Should().BeTrue();
    }

    [Fact]
    public void ApplyRbac_Operator_SeesPublicOnly()
    {
        var shell = MakeShell();
        var op = new User("u", "n", Role.Operator, "OP-1", "h");

        shell.ApplyRbac(op);

        shell.NavItems.Single(n => n.Id == "program").IsVisible.Should().BeFalse();
        shell.NavItems.Single(n => n.Id == "device").IsVisible.Should().BeFalse();
        shell.NavItems.Single(n => n.Id == "users").IsVisible.Should().BeFalse();
        shell.NavItems.Single(n => n.Id == "overview").IsVisible.Should().BeTrue();
    }

    [Fact]
    public void ApplyRbac_Engineer_SeesEngineerEntriesButNotAdmin()
    {
        var shell = MakeShell();
        var eng = new User("u", "n", Role.Engineer, "EN-1", "h");

        shell.ApplyRbac(eng);

        shell.NavItems.Single(n => n.Id == "program").IsVisible.Should().BeTrue();
        shell.NavItems.Single(n => n.Id == "maint").IsVisible.Should().BeTrue();
        shell.NavItems.Single(n => n.Id == "users").IsVisible.Should().BeFalse();
        shell.NavItems.Single(n => n.Id == "settings").IsVisible.Should().BeFalse();
    }

    [Fact]
    public void ApplyRbac_Admin_SeesEverything()
    {
        var shell = MakeShell();
        var admin = new User("u", "n", Role.Admin, "AD-1", "h");

        shell.ApplyRbac(admin);

        shell.NavItems.All(n => n.IsVisible).Should().BeTrue();
        shell.VisibleNavItems.Count().Should().Be(shell.NavItems.Count);
    }
}
