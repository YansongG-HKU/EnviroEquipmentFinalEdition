using System;
using System.Security.Cryptography;
using FluentAssertions;
using SiemensS7Demo.App.Auth;
using Xunit;

namespace EnviroEquipment.App.Tests.Auth;

[Trait("Category", "Pkg4")]
public class ProtectedStoreTests
{
    private const string Secret = "hunter2";

    [Fact]
    public void InMemoryStore_RoundTrips()
    {
        var s = new InMemoryProtectedStore();
        s.Unprotect(s.Protect(Secret)).Should().Be(Secret);
    }

    [Fact]
    public void InMemoryStore_DoesNotContainPlaintext()
    {
        var s = new InMemoryProtectedStore();
        var cipher = s.Protect(Secret);
        cipher.Should().NotContain(Secret);
    }

    [Fact]
    public void InMemoryStore_Unprotect_RejectsGarbledInput_WithoutEchoingPayload()
    {
        var s = new InMemoryProtectedStore();
        var act = () => s.Unprotect("not-base64-!!!");
        var ex = act.Should().Throw<CryptographicException>().Which;
        ex.Message.Should().NotContain("not-base64-!!!");
    }

    [Fact]
    public void DpapiStore_RoundTripsOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // DPAPI is a Windows-only API; the cross-platform fake covers the contract.
        }
        var s = new DpapiProtectedStore();
        s.Unprotect(s.Protect(Secret)).Should().Be(Secret);
    }

    [Fact]
    public void DpapiStore_Ciphertext_DoesNotContainPlaintext()
    {
        if (!OperatingSystem.IsWindows()) return;
        var s = new DpapiProtectedStore();
        var cipher = s.Protect(Secret);
        cipher.Should().NotContain(Secret);
        Convert.FromBase64String(cipher).Length.Should().BeGreaterThan(Secret.Length,
            "DPAPI blobs include IV/HMAC headers, never the raw plaintext.");
    }

    [Fact]
    public void DpapiStore_Unprotect_RejectsTamperedPayload_WithoutEchoingPayload()
    {
        if (!OperatingSystem.IsWindows()) return;
        // Cross-scope is not directionally enforced on all Windows hosts (a logged-in user
        // can decrypt LocalMachine blobs they helped produce). The tamper test below covers
        // the more robust failure mode: a corrupted ciphertext blob must fail closed without
        // leaking either the original plaintext or the corrupted payload through the
        // exception message.
        var s = new DpapiProtectedStore();
        var cipher = s.Protect(Secret);
        var bytes = Convert.FromBase64String(cipher);
        // Flip a byte in the middle of the DPAPI blob (post-header) to corrupt it.
        bytes[bytes.Length / 2] ^= 0xFF;
        var corrupted = Convert.ToBase64String(bytes);

        var act = () => s.Unprotect(corrupted);
        var ex = act.Should().Throw<CryptographicException>().Which;
        ex.Message.Should().NotContain(Secret);
        ex.Message.Should().NotContain(corrupted);
    }
}
