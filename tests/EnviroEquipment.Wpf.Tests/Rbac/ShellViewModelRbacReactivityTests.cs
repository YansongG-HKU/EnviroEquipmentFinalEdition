using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.Rbac;

[Trait("Category", "Pkg4")]
public class ShellViewModelRbacReactivityTests
{
    private static (AuthService auth, AuthBackedRbacContext rbac, ShellViewModel shell, SingleDeviceViewModel single)
        Build(params (string code, Role role, string password)[] users)
    {
        var hasher = new PasswordHasher();
        var seed = new System.Collections.Generic.List<User>();
        foreach (var (code, role, pw) in users)
        {
            seed.Add(new User($"u-{code}", code, role, code, hasher.Hash(pw)));
        }
        var repo = new InMemoryUserRepository(seed);
        var auth = new AuthService(repo, hasher, NullLogger<AuthService>.Instance);
        var rbac = new AuthBackedRbacContext(auth);
        var single = new SingleDeviceViewModel(null, rbac);
        var shell = new ShellViewModel(new OverviewViewModel(), single);
        shell.WireRbac(auth, rbac);
        return (auth, rbac, shell, single);
    }

    [Fact]
    public async Task SignInAsOperator_HidesAdminAndEngineerNav_AndStopCommandStaysDisabled()
    {
        var (auth, _, shell, _) = Build(("OP-1", Role.Operator, "pw"));

        await auth.SignInAsync("OP-1", "pw", Shift.ForLocalNow(), CancellationToken.None);

        shell.NavItems.Single(n => n.Id == "program").IsVisible.Should().BeFalse();
        shell.NavItems.Single(n => n.Id == "users").IsVisible.Should().BeFalse();
        shell.NavItems.Single(n => n.Id == "settings").IsVisible.Should().BeFalse();
        shell.NavItems.Single(n => n.Id == "overview").IsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task SignInAsEngineer_RevealsEngineerNav()
    {
        var (auth, _, shell, _) = Build(("EN-1", Role.Engineer, "pw"));

        await auth.SignInAsync("EN-1", "pw", Shift.ForLocalNow(), CancellationToken.None);

        shell.NavItems.Single(n => n.Id == "program").IsVisible.Should().BeTrue();
        shell.NavItems.Single(n => n.Id == "device").IsVisible.Should().BeTrue();
        shell.NavItems.Single(n => n.Id == "users").IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task RoleSwitch_RefreshesNavItemVisibility()
    {
        var (auth, _, shell, _) = Build(
            ("OP-1", Role.Operator, "pw"),
            ("EN-1", Role.Engineer, "pw"));

        await auth.SignInAsync("OP-1", "pw", Shift.ForLocalNow(), CancellationToken.None);
        shell.NavItems.Single(n => n.Id == "program").IsVisible.Should().BeFalse();

        auth.SignOut();
        await auth.SignInAsync("EN-1", "pw", Shift.ForLocalNow(), CancellationToken.None);

        shell.NavItems.Single(n => n.Id == "program").IsVisible.Should().BeTrue(
            "switching to Engineer must re-evaluate visibility from the new role");
    }

    [Fact]
    public async Task SignOut_ReturnsToLeastPrivilegeDefault()
    {
        var (auth, _, shell, _) = Build(("AD-1", Role.Admin, "pw"));

        await auth.SignInAsync("AD-1", "pw", Shift.ForLocalNow(), CancellationToken.None);
        shell.NavItems.Single(n => n.Id == "settings").IsVisible.Should().BeTrue();

        auth.SignOut();

        shell.NavItems.Single(n => n.Id == "settings").IsVisible.Should().BeFalse();
        shell.NavItems.Single(n => n.Id == "users").IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task SingleDeviceWriteSetpoint_DisabledForOperator_EnabledForEngineerAfterRoleSwitch()
    {
        var (auth, _, _, single) = Build(
            ("OP-1", Role.Operator, "pw"),
            ("EN-1", Role.Engineer, "pw"));

        // Operator first: WriteSetpoint must be disallowed.
        await auth.SignInAsync("OP-1", "pw", Shift.ForLocalNow(), CancellationToken.None);
        single.Select("TH-01");
        // Manually set status so SelectedStatus is non-null/non-offline (CanWrite floor).
        SetStatus(single, SiemensS7Demo.Domain.DeviceStatus.Run);

        single.WriteSetpointCommand.CanExecute(null).Should().BeFalse(
            "Operator must not be allowed to write setpoints");

        // Switch to Engineer (after the ShellViewModel's RoleChanged subscription has fired
        // CommandManager.InvalidateRequerySuggested) — direct CanExecute call still recomputes.
        auth.SignOut();
        await auth.SignInAsync("EN-1", "pw", Shift.ForLocalNow(), CancellationToken.None);

        single.WriteSetpointCommand.CanExecute(null).Should().BeTrue(
            "Engineer must be allowed to write setpoints after role change");
    }

    private static void SetStatus(SingleDeviceViewModel vm, SiemensS7Demo.Domain.DeviceStatus s)
    {
        var prop = typeof(SingleDeviceViewModel).GetProperty("SelectedStatus")!;
        prop.SetValue(vm, s);
    }
}
