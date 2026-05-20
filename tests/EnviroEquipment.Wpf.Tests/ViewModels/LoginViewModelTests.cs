using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg4")]
public class LoginViewModelTests
{
    private static (IUserRepository repo, AuthService auth) MakeAuth()
    {
        var hasher = new PasswordHasher();
        var seed = new List<User>
        {
            new("u-op", "Op", Role.Operator, "OP-1", hasher.Hash("pw")),
        };
        var repo = new InMemoryUserRepository(seed);
        return (repo, new AuthService(repo, hasher, NullLogger<AuthService>.Instance));
    }

    [Fact]
    public void InitialStep_IsSelectAccount()
    {
        var (_, auth) = MakeAuth();
        var vm = new LoginViewModel(auth);

        vm.Step.Should().Be(LoginStep.SelectAccount);
        vm.SubmitPasswordCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SelectingUser_AdvancesToPassword()
    {
        var (_, auth) = MakeAuth();
        var vm = new LoginViewModel(auth);

        vm.SelectUser("OP-1");

        vm.Step.Should().Be(LoginStep.EnterPassword);
        vm.SelectedCode.Should().Be("OP-1");
    }

    [Fact]
    public async Task EnteringCorrectPassword_AdvancesToShift()
    {
        var (_, auth) = MakeAuth();
        var vm = new LoginViewModel(auth);
        vm.SelectUser("OP-1");
        vm.Password = "pw";

        await vm.SubmitPasswordAsync(CancellationToken.None);

        vm.Step.Should().Be(LoginStep.ConfirmShift);
        vm.ErrorMessage.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task WrongPassword_ShowsError_AndStaysOnPasswordStep()
    {
        var (_, auth) = MakeAuth();
        var vm = new LoginViewModel(auth);
        vm.SelectUser("OP-1");
        vm.Password = "NOPE";

        await vm.SubmitPasswordAsync(CancellationToken.None);

        vm.Step.Should().Be(LoginStep.EnterPassword);
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DefaultShift_MatchesLocalTimeBucket()
    {
        var (_, auth) = MakeAuth();
        var vm = new LoginViewModel(auth);

        var expected = Shift.ForLocalNow();
        vm.SelectedShift.Should().NotBeNull();
        vm.SelectedShift!.Code.Should().Be(expected.Code);
    }

    [Fact]
    public async Task ConfirmingShift_SignsInUserOnAuthService()
    {
        var (_, auth) = MakeAuth();
        var vm = new LoginViewModel(auth);
        vm.SelectUser("OP-1");
        vm.Password = "pw";
        await vm.SubmitPasswordAsync(CancellationToken.None);

        await vm.ConfirmShiftAsync(CancellationToken.None);

        auth.Current.Should().NotBeNull();
        auth.Current!.Code.Should().Be("OP-1");
        vm.Step.Should().Be(LoginStep.SignedIn);
    }

    [Fact]
    public void Back_FromPassword_ReturnsToSelectAccount_AndClearsPassword()
    {
        var (_, auth) = MakeAuth();
        var vm = new LoginViewModel(auth);
        vm.SelectUser("OP-1");
        vm.Password = "pw";

        vm.BackCommand.Execute(null);

        vm.Step.Should().Be(LoginStep.SelectAccount);
        vm.Password.Should().BeEmpty();
    }

    [Fact]
    public void Back_FromShift_ReturnsToPassword_Step()
    {
        var (_, auth) = MakeAuth();
        var vm = new LoginViewModel(auth);
        vm.SelectUser("OP-1");
        vm.Step = LoginStep.ConfirmShift;

        vm.BackCommand.Execute(null);

        vm.Step.Should().Be(LoginStep.EnterPassword);
    }
}
