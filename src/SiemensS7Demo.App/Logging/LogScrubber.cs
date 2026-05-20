using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SiemensS7Demo.App.Logging;

/// <summary>
/// Plaintext-leak guard for log statements that touch credential material.
///
/// Two surfaces:
///   1. <see cref="Assert"/> — call before passing structured-logging arguments to
///      <c>ILogger.Log*</c>. Throws <see cref="InvalidOperationException"/> when any
///      value looks like a secret (Argon2 hash, password, cipher) or when the named
///      parameter slot is "Password" / "Cipher" / "Secret" with a non-empty value.
///   2. <see cref="Redact"/> — best-effort masking for opportunistic logging where a
///      throw would be too aggressive (e.g. diagnostic dumps from caught exceptions).
///
/// How a future dev notices a leak attempt:
///   - In <see cref="SiemensS7Demo.App.Auth.AuthService"/>, the sign-in log statements
///     route their args through <c>Assert</c> before <c>ILogger.LogInformation</c>.
///     Passing <c>user.PasswordHash</c> by accident throws at the call site (visible
///     in tests, immediately reproducible).
///   - The runtime PlaintextLeakTests in tests/.../Mqtt/PlaintextLeakTests.cs additionally
///     verifies that no MQTT password value lands in stdout/stderr/log files.
///   - The unit tests in tests/.../Logging/LogScrubberTests.cs document the matcher
///     surface — adding a new field name to the deny-list is a 1-line change in
///     <see cref="ForbiddenNamePattern"/>.
/// </summary>
public static class LogScrubber
{
    // Field name pattern: matched against structured-logging placeholder names. Case-insensitive.
    // Add new sensitive field names here; the test suite locks the existing set down.
    private static readonly Regex ForbiddenNamePattern = new(
        @"^(password|pwd|passwd|secret|cipher|token|apikey|api_key|bearer|credential|credentials)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Value pattern: matched against any string value. Argon2 hash leaks are the most common
    // accidental leak (logging the User entity directly).
    private static readonly Regex ForbiddenValuePattern = new(
        @"^\$argon2(id|i|d)\$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Throw if any of the supplied (name, value) pairs would leak a secret if passed to
    /// a structured logger. Intended call site:
    /// <code>
    /// LogScrubber.Assert(
    ///     ("Code", code),
    ///     ("Role", user.Role));
    /// _log.LogInformation("Sign-in succeeded: {Code} role={Role}.", code, user.Role);
    /// </code>
    /// </summary>
    public static void Assert(params (string Name, object? Value)[] pairs)
    {
        if (pairs is null) return;
        foreach (var (name, value) in pairs)
        {
            if (name is null) continue;
            if (ForbiddenNamePattern.IsMatch(name) && value is not null && !IsEmptyString(value))
            {
                throw new InvalidOperationException(
                    $"LogScrubber: refused to log a non-empty value under the sensitive name '{name}'. " +
                    "Mask or omit before logging.");
            }
            if (value is string s && ForbiddenValuePattern.IsMatch(s))
            {
                throw new InvalidOperationException(
                    "LogScrubber: refused to log a value that looks like an Argon2 password hash. " +
                    "Pass the user Code or Id instead.");
            }
        }
    }

    /// <summary>
    /// Replace secret-bearing values with a marker, returning the masked sequence. Use this
    /// when you are about to log a free-form bag (e.g. an exception ToString()) and want a
    /// best-effort sanitisation without throwing.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Redact(IEnumerable<KeyValuePair<string, object?>> pairs)
    {
        var result = new Dictionary<string, object?>();
        foreach (var p in pairs ?? Array.Empty<KeyValuePair<string, object?>>())
        {
            if (p.Key is null) continue;
            if (ForbiddenNamePattern.IsMatch(p.Key))
            {
                result[p.Key] = IsEmptyString(p.Value) ? string.Empty : "***REDACTED***";
            }
            else if (p.Value is string s && ForbiddenValuePattern.IsMatch(s))
            {
                result[p.Key] = "***REDACTED-HASH***";
            }
            else
            {
                result[p.Key] = p.Value;
            }
        }
        return result;
    }

    private static bool IsEmptyString(object? v) => v is string s && s.Length == 0;

    /// <summary>True if the given placeholder name is on the deny-list.</summary>
    public static bool IsSensitiveName(string name) => name is not null && ForbiddenNamePattern.IsMatch(name);

    /// <summary>True if the given value looks like a credential payload.</summary>
    public static bool LooksLikeSecret(object? value) =>
        value is string s && ForbiddenValuePattern.IsMatch(s);
}
