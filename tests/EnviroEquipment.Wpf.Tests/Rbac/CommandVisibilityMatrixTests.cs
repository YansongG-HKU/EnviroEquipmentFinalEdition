using System.Linq;
using System.Reflection;
using FluentAssertions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.Rbac;

[Trait("Category", "Pkg4")]
public class CommandVisibilityMatrixTests
{
    // Bind by method name (RelayCommand source generators produce *Command names from these methods).
    // Existing Pkg 1 method names: WriteSetpointAsync, Run, Pause, Stop, Reset.
    public static TheoryData<string, Role> ExpectedAnnotations()
    {
        var data = new TheoryData<string, Role>
        {
            { "WriteSetpointAsync", Role.Engineer },
            { "Stop", Role.Engineer },
            { "Reset", Role.Operator },
            { "Run", Role.Operator },
            { "Pause", Role.Operator },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(ExpectedAnnotations))]
    public void SingleDeviceViewModel_CommandsCarryExpectedMinimumRole(string methodName, Role expected)
    {
        var method = typeof(SingleDeviceViewModel).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        method.Should().NotBeNull($"{methodName} must exist on SingleDeviceViewModel");
        var attr = method!.GetCustomAttribute<RequiresRoleAttribute>();
        attr.Should().NotBeNull($"{methodName} must declare [RequiresRole]");
        attr!.Minimum.Should().Be(expected);
    }

    [Theory]
    // Engineer-gated write/stop: Operator denied, Engineer/Admin allowed.
    [InlineData(Role.Operator, "WriteSetpointAsync", false)]
    [InlineData(Role.Engineer, "WriteSetpointAsync", true)]
    [InlineData(Role.Admin,    "WriteSetpointAsync", true)]
    [InlineData(Role.Operator, "Stop", false)]
    [InlineData(Role.Engineer, "Stop", true)]
    [InlineData(Role.Admin,    "Stop", true)]
    // Operator-gated reset/run/pause: all 3 roles allowed (Operator is the floor).
    [InlineData(Role.Operator, "Reset", true)]
    [InlineData(Role.Engineer, "Reset", true)]
    [InlineData(Role.Admin,    "Reset", true)]
    public void RbacGuard_AllowsExactlyTheRolesAtOrAboveMinimum(Role role, string methodName, bool expected)
    {
        var method = typeof(SingleDeviceViewModel)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var user = new User("u", "n", role, "c", "h");

        RbacGuard.IsAllowed(user, method).Should().Be(expected);
    }
}
