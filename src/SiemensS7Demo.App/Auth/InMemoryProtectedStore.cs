using System;
using System.Text;

namespace SiemensS7Demo.App.Auth;

/// <summary>
/// Test-only protected-store fake. Base64-encodes the plaintext — provides NO actual
/// encryption. Lets tests run identically on Windows / Linux / macOS without depending
/// on DPAPI's machine context. NEVER register this in a production composition root.
///
/// The <c>Protect</c> output never contains the plaintext substring directly, because
/// base64 obscures the bytes; the plaintext-leak test (<c>PlaintextLeakTests</c>) verifies
/// that.
/// </summary>
public sealed class InMemoryProtectedStore : IProtectedStore
{
    public string Protect(string plaintext) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

    public string Unprotect(string ciphertext)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext));
        }
        catch (FormatException)
        {
            // Re-throw without echoing the ciphertext; callers must not leak it.
            throw new System.Security.Cryptography.CryptographicException(
                "Failed to unprotect: ciphertext is not valid base64.");
        }
    }
}
