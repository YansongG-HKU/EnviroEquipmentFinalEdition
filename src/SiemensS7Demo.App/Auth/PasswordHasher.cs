using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace SiemensS7Demo.App.Auth;

/// <summary>
/// Argon2id password hasher. Output format: $argon2id$v=19$m=65536,t=3,p=2$&lt;saltB64&gt;$&lt;hashB64&gt;.
/// </summary>
public sealed class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemoryKb = 65536;
    private const int Iterations = 3;
    private const int Parallelism = 2;

    public string Hash(string password)
    {
        if (password is null) throw new ArgumentNullException(nameof(password));
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt);
        return $"$argon2id$v=19$m={MemoryKb},t={Iterations},p={Parallelism}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encoded)
    {
        if (password is null) return false;
        if (string.IsNullOrEmpty(encoded)) return false;

        try
        {
            var parts = encoded.Split('$', StringSplitOptions.None);
            // Expected layout: ["", "argon2id", "v=19", "m=...,t=...,p=...", saltB64, hashB64]
            if (parts.Length != 6) return false;
            if (parts[1] != "argon2id") return false;

            var paramSegment = parts[3];
            var (mem, it, par) = ParseParams(paramSegment);
            var salt = Convert.FromBase64String(parts[4]);
            var expected = Convert.FromBase64String(parts[5]);
            var actual = Derive(password, salt, mem, it, par, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt,
                                 int memoryKb = MemoryKb, int iterations = Iterations,
                                 int parallelism = Parallelism, int hashSize = HashSize)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password));
        argon.Salt = salt;
        argon.DegreeOfParallelism = parallelism;
        argon.MemorySize = memoryKb;
        argon.Iterations = iterations;
        return argon.GetBytes(hashSize);
    }

    private static (int memoryKb, int iterations, int parallelism) ParseParams(string segment)
    {
        int mem = MemoryKb, it = Iterations, par = Parallelism;
        foreach (var kv in segment.Split(','))
        {
            var pair = kv.Split('=', 2);
            if (pair.Length != 2) continue;
            var value = int.Parse(pair[1], CultureInfo.InvariantCulture);
            switch (pair[0])
            {
                case "m": mem = value; break;
                case "t": it = value; break;
                case "p": par = value; break;
            }
        }
        return (mem, it, par);
    }
}
