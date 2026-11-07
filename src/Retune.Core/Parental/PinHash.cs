using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Retune.Core.Parental;

public enum PinCheck
{
    Wrong,
    Correct,

    /// <summary>Correct against an old-style hash: replace it with <see cref="PinHash.Create"/> now.</summary>
    CorrectLegacy,
}

/// <summary>
/// Parental PIN hashing: PBKDF2-HMAC-SHA256, 310,000 iterations, 16-byte random salt, 32-byte output,
/// stored as <c>pbkdf2-sha256$310000$&lt;salt hex&gt;$&lt;hash hex&gt;</c>. A family deterrent, not a security
/// boundary, but still never stored or compared carelessly.
/// </summary>
public static class PinHash
{
    public const int MinLength = 4;
    public const int MaxLength = 8;
    public const int Iterations = 310_000;

    private const string Scheme = "pbkdf2-sha256";
    private const string LegacySalt = "lumen.parental.";
    /// <summary>A stored hash claiming more work than this is malformed (a corrupt file must not hang the UI).</summary>
    private const uint MaxIterations = Iterations * 10;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>4 to 8 ASCII digits.</summary>
    public static bool IsValidFormat(string? pin) =>
        pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);

    public static string Create(string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Create(CultureInfo.InvariantCulture, $"{Scheme}${Iterations}${Convert.ToHexStringLower(salt)}${Convert.ToHexStringLower(hash)}");
    }

    public static PinCheck Verify(string pin, string? stored)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (string.IsNullOrEmpty(stored))
        {
            return PinCheck.Wrong;
        }

        return stored.StartsWith(Scheme + "$", StringComparison.Ordinal)
            ? VerifyPbkdf2(pin, stored) ? PinCheck.Correct : PinCheck.Wrong
            : VerifyLegacy(pin, stored) ? PinCheck.CorrectLegacy : PinCheck.Wrong;
    }

    private static bool VerifyPbkdf2(string pin, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4
            || !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations == 0 || iterations > MaxIterations
            || TryHex(parts[2]) is not { } salt
            || TryHex(parts[3]) is not { } expected)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, (int)iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Hex SHA-256 of "lumen.parental." + PIN (early macOS builds; kept for completeness).</summary>
    private static bool VerifyLegacy(string pin, string stored)
    {
        var actual = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(LegacySalt + pin))));
        var expected = Encoding.ASCII.GetBytes(stored);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[]? TryHex(string text)
    {
        if (text.Length == 0 || text.Length % 2 != 0)
        {
            return null;
        }

        try
        {
            return Convert.FromHexString(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// Slows down guessing: after 5 wrong PINs, entry pauses for 30 s, then 1 min, then 5 min.
/// A correct PIN resets it. In-memory only (PLAN.md decision 8).
/// </summary>
public sealed class PinAttemptLimiter(TimeProvider time)
{
    public const int FreeAttempts = 5;

    private static readonly TimeSpan[] Lockouts = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)];

    private int _failures;
    private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;

    /// <summary>How long until another attempt is allowed (zero when allowed now).</summary>
    public TimeSpan RemainingLockout => _lockedUntil > time.GetUtcNow() ? _lockedUntil - time.GetUtcNow() : TimeSpan.Zero;

    public bool CanAttempt => RemainingLockout == TimeSpan.Zero;

    public void RecordFailure()
    {
        _failures++;
        if (_failures >= FreeAttempts)
        {
            var step = Math.Min(_failures - FreeAttempts, Lockouts.Length - 1);
            _lockedUntil = time.GetUtcNow() + Lockouts[step];
        }
    }

    public void RecordSuccess()
    {
        _failures = 0;
        _lockedUntil = DateTimeOffset.MinValue;
    }
}
