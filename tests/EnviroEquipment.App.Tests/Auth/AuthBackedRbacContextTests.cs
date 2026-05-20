using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using Xunit;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class AuthBackedRbacContextTests
{
    private static (AuthService auth, AuthBackedRbacContext rbac, PasswordHasher hasher, IUserRepository repo)
        Build(params (string code, Role role, string password)[] users)
    {
        var hasher = new PasswordHasher();
        var seed = new List<User>();
        foreach (var (code, role, pw) in users)
        {
            seed.Add(new User($"u-{code}", code, role, code, hasher.Hash(pw)));
        }
        var repo = new InMemoryUserRepository(seed);
        var auth = new AuthService(repo, hasher, NullLogger<AuthService>.Instance);
        var rbac = new AuthBackedRbacContext(auth);
        return (auth, rbac, hasher, repo);
    }

    [Fact]
    public void Current_WhenSignedOut_IsOperator_LeastPrivilege()
    {
        var (_, rbac, _, _) = Build();

        rbac.Current.Should().Be(Role.Operator,
            "the safe default for an unauthenticated session is the lowest role, not Admin");
        rbac.IsAtLeast(Role.Engineer).Should().BeFalse();
        rbac.IsAtLeast(Role.Admin).Should().BeFalse();
        rbac.IsAtLeast(Role.Operator).Should().BeTrue();
    }

    [Fact]
    public async System.Threading.Tasks.Task Current_AfterSignIn_ReflectsUserRole()
    {
        var (auth, rbac, _, _) = Build(("EN-1", Role.Engineer, "pw"));

        await auth.SignInAsync("EN-1", "pw", Shift.ForLocalNow(), CancellationToken.None);

        rbac.Current.Should().Be(Role.Engineer);
        rbac.IsAtLeast(Role.Engineer).Should().BeTrue();
        rbac.IsAtLeast(Role.Admin).Should().BeFalse();
    }

    [Fact]
    public async System.Threading.Tasks.Task RoleChanged_FiresOnSignInAndSignOut()
    {
        var (auth, rbac, _, _) = Build(("AD-1", Role.Admin, "pw"));
        var raised = 0;
        rbac.RoleChanged += (_, _) => raised++;

        await auth.SignInAsync("AD-1", "pw", Shift.ForLocalNow(), CancellationToken.None);
        var afterSignIn = raised;
        auth.SignOut();

        afterSignIn.Should().Be(1, "RoleChanged must fire when CurrentChanged fires on sign-in");
        raised.Should().Be(2, "RoleChanged must also fire when CurrentChanged fires on sign-out");
    }

    [Fact]
    public async System.Threading.Tasks.Task Current_AfterSignOut_FallsBackToOperator()
    {
        var (auth, rbac, _, _) = Build(("AD-1", Role.Admin, "pw"));
        await auth.SignInAsync("AD-1", "pw", Shift.ForLocalNow(), CancellationToken.None);
        rbac.Current.Should().Be(Role.Admin);

        auth.SignOut();

        rbac.Current.Should().Be(Role.Operator,
            "signing out returns to the unauthenticated least-privilege default");
    }
}
