using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.App;
using SiemensS7Demo.App.Auth;
using SiemensS7Demo.Domain.Users;
using Xunit;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class UserSeedingTests
{
    [Fact]
    public async Task AddPkg4Auth_SeedsRepositoryFromUsersConfig_AndHashesPlaintext()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Users:0:code"] = "AD-0001",
                ["Users:0:name"] = "Admin",
                ["Users:0:role"] = "Admin",
                ["Users:0:password"] = "admin",
                ["Users:1:code"] = "OP-1042",
                ["Users:1:name"] = "李工",
                ["Users:1:role"] = "Operator",
                ["Users:1:password"] = "operator",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddPkg4Auth(config);
        await using var sp = services.BuildServiceProvider();

        var repo = sp.GetRequiredService<IUserRepository>();
        var all = await repo.ListAsync(CancellationToken.None);

        all.Should().HaveCount(2);
        var admin = await repo.FindByCodeAsync("AD-0001", CancellationToken.None);
        admin.Should().NotBeNull();
        admin!.Role.Should().Be(Role.Admin);
        admin.PasswordHash.Should().StartWith("$argon2id$", "plaintext must be hashed at startup");
        admin.PasswordHash.Should().NotContain("admin");
    }

    [Fact]
    public async Task AuthService_ResolvedFromDi_CanSignInWithSeededPassword()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Users:0:code"] = "AD-0001",
                ["Users:0:name"] = "Admin",
                ["Users:0:role"] = "Admin",
                ["Users:0:password"] = "admin",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPkg4Auth(config);
        await using var sp = services.BuildServiceProvider();

        var auth = sp.GetRequiredService<IAuthService>();
        var result = await auth.SignInAsync("AD-0001", "admin", Shift.ForLocalNow(), CancellationToken.None);

        result.Success.Should().BeTrue();
        auth.Current!.Code.Should().Be("AD-0001");
    }
}
