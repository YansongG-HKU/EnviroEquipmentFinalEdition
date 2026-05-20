using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using Xunit;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class AuthServiceTests
{
    private static (IUserRepository repo, PasswordHasher hasher) MakeRepo(params (string code, string name, Role role, string password)[] users)
    {
        var hasher = new PasswordHasher();
        var seed = new System.Collections.Generic.List<User>();
        foreach (var (code, name, role, password) in users)
        {
            seed.Add(new User($"u-{code}", name, role, code, hasher.Hash(password)));
        }
        return (new InMemoryUserRepository(seed), hasher);
    }

    [Fact]
    public async Task SignIn_Succeeds_WithCorrectCredentials()
    {
        var (repo, hasher) = MakeRepo(("OP-1", "Op", Role.Operator, "pw1"));
        var svc = new AuthService(repo, hasher, NullLogger<AuthService>.Instance);
        var shift = Shift.ForLocalNow();

        var result = await svc.SignInAsync("OP-1", "pw1", shift, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.User!.Name.Should().Be("Op");
        result.User!.Role.Should().Be(Role.Operator);
        svc.Current.Should().BeSameAs(result.User);
        svc.CurrentShift.Should().Be(shift);
    }

    [Fact]
    public async Task SignIn_Fails_WithWrongPassword()
    {
        var (repo, hasher) = MakeRepo(("OP-1", "Op", Role.Operator, "pw1"));
        var svc = new AuthService(repo, hasher, NullLogger<AuthService>.Instance);

        var result = await svc.SignInAsync("OP-1", "WRONG", Shift.ForLocalNow(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty().And.MatchEquivalentOf("*invalid*");
        svc.Current.Should().BeNull();
    }

    [Fact]
    public async Task SignIn_Fails_WhenUserUnknown()
    {
        var (repo, hasher) = MakeRepo();
        var svc = new AuthService(repo, hasher, NullLogger<AuthService>.Instance);

        var result = await svc.SignInAsync("NOPE", "pw", Shift.ForLocalNow(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty().And.MatchEquivalentOf("*invalid*");
    }

    [Fact]
    public async Task SignIn_LocksOut_After5FailuresWithin30Seconds()
    {
        var (repo, hasher) = MakeRepo(("OP-1", "Op", Role.Operator, "pw1"));
        var svc = new AuthService(repo, hasher, NullLogger<AuthService>.Instance);

        for (var i = 0; i < 5; i++)
        {
            var r = await svc.SignInAsync("OP-1", "WRONG", Shift.ForLocalNow(), CancellationToken.None);
            r.Success.Should().BeFalse();
        }
        var locked = await svc.SignInAsync("OP-1", "pw1", Shift.ForLocalNow(), CancellationToken.None);

        locked.Success.Should().BeFalse();
        locked.ErrorMessage.Should().NotBeNullOrEmpty().And.MatchEquivalentOf("*locked*");
    }

    [Fact]
    public async Task SignOut_ClearsCurrent()
    {
        var (repo, hasher) = MakeRepo(("OP-1", "Op", Role.Operator, "pw1"));
        var svc = new AuthService(repo, hasher, NullLogger<AuthService>.Instance);

        await svc.SignInAsync("OP-1", "pw1", Shift.ForLocalNow(), CancellationToken.None);
        svc.Current.Should().NotBeNull();

        svc.SignOut();
        svc.Current.Should().BeNull();
        svc.CurrentShift.Should().BeNull();
    }

    [Fact]
    public void Shift_ForLocalNow_PicksBucketByHour()
    {
        var nine = new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.FromHours(8));
        var sixteen = new DateTimeOffset(2026, 5, 20, 16, 0, 0, TimeSpan.FromHours(8));
        var twentyThree = new DateTimeOffset(2026, 5, 20, 23, 0, 0, TimeSpan.FromHours(8));

        Shift.ForLocalNow(nine).Code.Should().Be("DAY-A");
        Shift.ForLocalNow(sixteen).Code.Should().Be("DAY-B");
        Shift.ForLocalNow(twentyThree).Code.Should().Be("NIGHT");
    }
}
