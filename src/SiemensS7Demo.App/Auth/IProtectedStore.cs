namespace SiemensS7Demo.App.Auth;

/// <summary>
/// Symmetric, machine- (or user-) scoped secret protection.
/// Implementations:
///   - <see cref="DpapiProtectedStore"/> wraps Windows DPAPI (CurrentUser scope) — production.
///   - <see cref="InMemoryProtectedStore"/> base64-only, NEVER for production — keeps tests
///     cross-platform and deterministic.
///
/// Both round-trip <c>Unprotect(Protect(x)) == x</c>. Both surfaces deliberately do NOT
/// include the plaintext in any exception they throw — that would defeat the plaintext-leak
/// guard validated by <c>PlaintextLeakTests</c>.
/// </summary>
public interface IProtectedStore
{
    /// <summary>
    /// Encrypt <paramref name="plaintext"/> and return a transport-safe (base64) string.
    /// Throws <see cref="System.Security.Cryptography.CryptographicException"/> on platform failure;
    /// the exception message MUST NOT contain the plaintext.
    /// </summary>
    string Protect(string plaintext);

    /// <summary>
    /// Reverse of <see cref="Protect"/>. Throws on tampering / wrong scope; the exception
    /// message MUST NOT contain any portion of the ciphertext payload.
    /// </summary>
    string Unprotect(string ciphertext);
}
