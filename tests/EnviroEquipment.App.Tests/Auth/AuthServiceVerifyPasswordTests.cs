using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using Xunit;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class AuthServiceVerifyPasswordTests
{
    private static AuthService BuildSvc(params (string code, Role role, string password)[] users)
    {
        var hasher = new PasswordHasher();
        var seed = new List<User>();
        foreach (var (code, role, pw) in users)
        {
            seed.Add(new User($"u-{code}", code, role, code, hasher.Hash(pw)));
        }
        var repo = new InMemoryUserRepository(seed);
        return new AuthService(repo, hasher, NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task VerifyPasswordAsync_ValidCredentials_ReturnsTrue_AndLeavesCurrentNull()
    {
        var svc = BuildSvc(("OP-1", Role.Operator, "pw"));
        var raised = 0;
        svc.CurrentChanged += (_, _) => raised++;

        var ok = await svc.VerifyPasswordAsync("OP-1", "pw", CancellationToken.None);

        ok.Should().BeTrue();
        svc.Current.Should().BeNull("VerifyPasswordAsync must not mutate the active session");
        svc.CurrentShift.Should().BeNull();
        raised.Should().Be(0, "VerifyPasswordAsync must not raise CurrentChanged");
    }

    [Fact]
    public async Task VerifyPasswordAsync_WrongPassword_ReturnsFalse_AndLeavesCurrentNull()
    {
        var svc = BuildSvc(("OP-1", Role.Operator, "pw"));
        var raised = 0;
        svc.CurrentChanged += (_, _) => raised++;

        var ok = await svc.VerifyPasswordAsync("OP-1", "WRONG", CancellationToken.None);

        ok.Should().BeFalse();
        svc.Current.Should().BeNull();
        raised.Should().Be(0);
    }

    [Fact]
    public async Task VerifyPasswordAsync_UnknownUser_ReturnsFalse_AndDoesNotMutateState()
    {
        var svc = BuildSvc();
        var raised = 0;
        svc.CurrentChanged += (_, _) => raised++;

        var ok = await svc.VerifyPasswordAsync("NOPE", "WRONG", CancellationToken.None);

        ok.Should().BeFalse();
        svc.Current.Should().BeNull();
        raised.Should().Be(0);
    }

    [Fact]
    public async Task VerifyPasswordAsync_DoesNotResetLockoutCounter()
    {
        var svc = BuildSvc(("OP-1", Role.Operator, "pw"));

        // Burn 3 wrong attempts via VerifyPasswordAsync, then 2 via SignInAsync to hit 5 strikes.
        for (var i = 0; i < 3; i++)
        {
            await svc.VerifyPasswordAsync("OP-1", "WRONG", CancellationToken.None);
        }
        for (var i = 0; i < 2; i++)
        {
            await svc.SignInAsync("OP-1", "WRONG", Shift.ForLocalNow(), CancellationToken.None);
        }
        var lockedOut = await svc.SignInAsync("OP-1", "pw", Shift.ForLocalNow(), CancellationToken.None);

        lockedOut.Success.Should().BeFalse();
        lockedOut.ErrorMessage.Should().Contain("locked",
            "VerifyPasswordAsync failures must count toward the lockout window like SignInAsync failures");
    }
}
