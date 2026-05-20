using FluentAssertions;
using SiemensS7Demo.App.Auth;
using Xunit;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class PasswordHasherTests
{
    [Fact]
    public void Hash_ProducesArgon2idEncodedString()
    {
        var hasher = new PasswordHasher();

        var hash = hasher.Hash("hunter2");

        hash.Should().StartWith("$argon2id$");
        hash.Length.Should().BeGreaterThan(60);
    }

    [Fact]
    public void Verify_ReturnsTrue_ForMatchingPassword()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("hunter2");

        hasher.Verify("hunter2", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalse_ForWrongPassword()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("hunter2");

        hasher.Verify("hunter3", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_ProducesDistinctSaltedHashes()
    {
        var hasher = new PasswordHasher();

        var a = hasher.Hash("hunter2");
        var b = hasher.Hash("hunter2");

        a.Should().NotBe(b, "Argon2id must apply a random salt per hash.");
    }

    [Fact]
    public void Verify_ReturnsFalse_ForGarbageHash()
    {
        var hasher = new PasswordHasher();

        hasher.Verify("anything", "not-a-real-hash").Should().BeFalse();
    }
}
