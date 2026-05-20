using System;
using System.Collections.Generic;
using FluentAssertions;
using SiemensS7Demo.App.Logging;
using Xunit;

namespace EnviroEquipment.App.Tests.Logging;

[Trait("Category", "Pkg4")]
public class LogScrubberTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("PWD")]
    [InlineData("passwd")]
    [InlineData("Secret")]
    [InlineData("Cipher")]
    [InlineData("token")]
    [InlineData("ApiKey")]
    [InlineData("api_key")]
    [InlineData("Bearer")]
    [InlineData("Credential")]
    [InlineData("credentials")]
    public void IsSensitiveName_FlagsForbiddenNames(string name)
    {
        LogScrubber.IsSensitiveName(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("Code")]
    [InlineData("Role")]
    [InlineData("Host")]
    [InlineData("Port")]
    [InlineData("UserId")]
    public void IsSensitiveName_AllowsBenignNames(string name)
    {
        LogScrubber.IsSensitiveName(name).Should().BeFalse();
    }

    [Fact]
    public void Assert_Throws_WhenNamedSlotIsPassword_AndValueNonEmpty()
    {
        var act = () => LogScrubber.Assert(("Password", "hunter2"));
        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("sensitive name");
        ex.Message.Should().Contain("Password");
    }

    [Fact]
    public void Assert_Throws_WhenValueLooksLikeArgon2Hash()
    {
        const string hash = "$argon2id$v=19$m=65536,t=3,p=4$ZGVhZGJlZWY$abcdef";
        var act = () => LogScrubber.Assert(("UserPassword", hash));
        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("Argon2");
        ex.Message.Should().Contain("hash");
    }

    [Fact]
    public void Assert_AllowsEmptyPasswordSlot()
    {
        // Empty placeholder is a no-op (the formatter would print "" anyway). This keeps
        // the guard from breaking legitimate "log this field, which may or may not exist".
        var act = () => LogScrubber.Assert(("Password", ""));
        act.Should().NotThrow();
    }

    [Fact]
    public void Assert_AllowsCodeAndRolePairs()
    {
        var act = () => LogScrubber.Assert(("Code", "AD-0001"), ("Role", "Admin"));
        act.Should().NotThrow();
    }

    [Fact]
    public void Assert_DoesNotThrow_OnNullArrayOrPairs()
    {
        // Defensive: a caller chained from a null collection should not blow up.
        var act1 = () => LogScrubber.Assert();
        act1.Should().NotThrow();

        var act2 = () => LogScrubber.Assert((null!, null));
        act2.Should().NotThrow();
    }

    [Fact]
    public void Redact_MasksSensitiveNames_AndArgon2Values()
    {
        var pairs = new[]
        {
            new KeyValuePair<string, object?>("Code", "AD-0001"),
            new KeyValuePair<string, object?>("Password", "hunter2"),
            new KeyValuePair<string, object?>("Hash", "$argon2id$v=19$abc"),
        };
        var redacted = LogScrubber.Redact(pairs);
        redacted["Code"].Should().Be("AD-0001");
        redacted["Password"].Should().Be("***REDACTED***");
        redacted["Hash"].Should().Be("***REDACTED-HASH***");
    }

    [Fact]
    public void LooksLikeSecret_FlagsArgon2_ButNotPlainStrings()
    {
        LogScrubber.LooksLikeSecret("$argon2id$x").Should().BeTrue();
        LogScrubber.LooksLikeSecret("AD-0001").Should().BeFalse();
        LogScrubber.LooksLikeSecret(null).Should().BeFalse();
        LogScrubber.LooksLikeSecret(42).Should().BeFalse();
    }
}
