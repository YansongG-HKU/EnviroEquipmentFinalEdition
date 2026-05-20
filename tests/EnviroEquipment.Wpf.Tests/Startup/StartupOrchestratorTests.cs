using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using SiemensS7Demo.Wpf.Startup;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.Startup;

[Trait("Category", "Pkg4")]
public class StartupOrchestratorTests
{
    private static (AuthService auth, StartupOrchestrator orch, FakeShellHost host) Build(
        params (string code, Role role, string password)[] users)
    {
        var hasher = new PasswordHasher();
        var seed = new System.Collections.Generic.List<User>();
        foreach (var (code, role, pw) in users)
        {
            seed.Add(new User($"u-{code}", code, role, code, hasher.Hash(pw)));
        }
        var repo = new InMemoryUserRepository(seed);
        var auth = new AuthService(repo, hasher, NullLogger<AuthService>.Instance);
        var host = new FakeShellHost();
        var orch = new StartupOrchestrator(auth, host);
        return (auth, orch, host);
    }

    [Fact]
    public void Begin_ShowsLogin_BeforeAnyShell()
    {
        var (_, orch, host) = Build();

        orch.Begin();

        host.ShowLoginCalls.Should().Be(1, "the login gate must be the first surface shown");
        host.ShowShellCalls.Should().Be(0, "the shell must not appear before a successful sign-in");
    }

    [Fact]
    public async Task SuccessfulSignIn_TransitionsToShell()
    {
        var (auth, orch, host) = Build(("OP-1", Role.Operator, "pw"));
        orch.Begin();

        await auth.SignInAsync("OP-1", "pw", Shift.ForLocalNow(), CancellationToken.None);

        host.CloseLoginCalls.Should().Be(1);
        host.ShowShellCalls.Should().Be(1);
    }

    [Fact]
    public async Task SignOut_ReopensLogin_AndClosesShell()
    {
        var (auth, orch, host) = Build(("OP-1", Role.Operator, "pw"));
        orch.Begin();
        await auth.SignInAsync("OP-1", "pw", Shift.ForLocalNow(), CancellationToken.None);
        host.ShowShellCalls.Should().Be(1);

        auth.SignOut();

        host.CloseShellCalls.Should().Be(1);
        host.ShowLoginCalls.Should().Be(2,
            "after sign-out the login surface must be shown again for the next user");
    }

    [Fact]
    public void Begin_IsIdempotent_OnlyShowsLoginOnce()
    {
        var (_, orch, host) = Build();

        orch.Begin();
        orch.Begin();

        host.ShowLoginCalls.Should().Be(1, "Begin must guard against double-arming");
    }

    private sealed class FakeShellHost : IShellHost
    {
        public int ShowLoginCalls { get; private set; }
        public int CloseLoginCalls { get; private set; }
        public int ShowShellCalls { get; private set; }
        public int CloseShellCalls { get; private set; }

        public void ShowLogin() => ShowLoginCalls++;
        public void CloseLogin() => CloseLoginCalls++;
        public void ShowShell() => ShowShellCalls++;
        public void CloseShell() => CloseShellCalls++;
    }
}
