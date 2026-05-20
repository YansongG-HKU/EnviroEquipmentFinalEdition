using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace SiemensS7Demo.App.Auth;

/// <summary>
/// Windows DPAPI-backed protected store. Uses <see cref="DataProtectionScope.CurrentUser"/> by
/// default so the ciphertext can only be decrypted under the same Windows user account that
/// produced it. Ciphertext is base64 of the raw DPAPI blob so it is safe to embed in
/// <c>appsettings.json</c>.
///
/// Note: gated with <c>[SupportedOSPlatform("windows")]</c>; on non-Windows the JIT will throw
/// PlatformNotSupportedException — tests under <c>ProtectedStoreTests</c> use
/// <see cref="OperatingSystem.IsWindows"/> to skip on other platforms.
///
/// Failure modes never echo plaintext / ciphertext payload back through the exception, so the
/// plaintext-leak guard in <c>PlaintextLeakTests</c> still holds.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiProtectedStore : IProtectedStore
{
    private readonly DataProtectionScope _scope;

    public DpapiProtectedStore() : this(DataProtectionScope.CurrentUser) { }

    public DpapiProtectedStore(DataProtectionScope scope)
    {
        _scope = scope;
    }

    public string Protect(string plaintext)
    {
        if (plaintext is null) throw new ArgumentNullException(nameof(plaintext));
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            var enc = ProtectedData.Protect(bytes, optionalEntropy: null, _scope);
            return Convert.ToBase64String(enc);
        }
        catch (CryptographicException)
        {
            // Strip any inner message to keep the plaintext out of logs.
            throw new CryptographicException("DPAPI Protect failed for the current scope.");
        }
        finally
        {
            // Best-effort scrub of the byte buffer.
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    public string Unprotect(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            throw new ArgumentException("Ciphertext must not be empty.", nameof(ciphertext));

        byte[] enc;
        try
        {
            enc = Convert.FromBase64String(ciphertext);
        }
        catch (FormatException)
        {
            // Don't include the ciphertext in the message.
            throw new CryptographicException("Failed to unprotect: ciphertext is not valid base64.");
        }

        byte[] plain;
        try
        {
            plain = ProtectedData.Unprotect(enc, optionalEntropy: null, _scope);
        }
        catch (CryptographicException)
        {
            // Wrong scope or tampered payload — strip detail.
            throw new CryptographicException(
                "Failed to unprotect: payload was produced in a different scope, or is corrupt.");
        }

        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            Array.Clear(plain, 0, plain.Length);
        }
    }
}
