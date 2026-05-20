using System.Reflection;
using FluentAssertions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using Xunit;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class RbacGuardTests
{
    private sealed class Sample
    {
        [RequiresRole(Role.Operator)]
        public void OperatorOk() { }

        [RequiresRole(Role.Engineer)]
        public void EngineerOnly() { }

        [RequiresRole(Role.Admin)]
        public void AdminOnly() { }

        public void Unannotated() { }
    }

    private static MethodInfo M(string name) => typeof(Sample).GetMethod(name)!;

    [Theory]
    [InlineData(Role.Operator, "OperatorOk", true)]
    [InlineData(Role.Operator, "EngineerOnly", false)]
    [InlineData(Role.Operator, "AdminOnly", false)]
    [InlineData(Role.Engineer, "OperatorOk", true)]
    [InlineData(Role.Engineer, "EngineerOnly", true)]
    [InlineData(Role.Engineer, "AdminOnly", false)]
    [InlineData(Role.Admin,    "OperatorOk", true)]
    [InlineData(Role.Admin,    "EngineerOnly", true)]
    [InlineData(Role.Admin,    "AdminOnly", true)]
    public void IsAllowed_RoleMatrix(Role role, string method, bool expected)
    {
        var user = new User("u", "n", role, "c", "h");
        RbacGuard.IsAllowed(user, M(method)).Should().Be(expected);
    }

    [Fact]
    public void IsAllowed_UnannotatedMethod_IsAllowedForEveryone()
    {
        var user = new User("u", "n", Role.Operator, "c", "h");
        RbacGuard.IsAllowed(user, M("Unannotated")).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_NullUser_IsAlwaysFalseForAnnotatedMethod()
    {
        RbacGuard.IsAllowed(null, M("OperatorOk")).Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_NullUser_IsAllowedForUnannotatedMethod()
    {
        RbacGuard.IsAllowed(null, M("Unannotated")).Should().BeTrue();
    }

    [Fact]
    public void MinimumFor_ReturnsAttributeRole_OrNull()
    {
        RbacGuard.MinimumFor(M("EngineerOnly")).Should().Be(Role.Engineer);
        RbacGuard.MinimumFor(M("Unannotated")).Should().BeNull();
    }
}
