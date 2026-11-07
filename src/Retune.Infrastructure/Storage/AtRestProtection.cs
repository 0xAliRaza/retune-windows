using System.Security.Cryptography;

namespace Retune.Infrastructure.Storage;

/// <summary>
/// Encrypts files that can contain provider credentials (M3U links, M3U stream URLs) with Windows DPAPI,
/// bound to the current Windows user: a copied file is unreadable for anyone else or on another PC.
/// Plain JSON written by older builds is still read, and re-encrypted on the next save.
/// </summary>
internal static class AtRestProtection
{
    /// <summary>"RTN1" marks an encrypted file.</summary>
    private static ReadOnlySpan<byte> Magic => "RTN1"u8;

    private static readonly byte[] Entropy = "Retune.AtRest.v1"u8.ToArray();

    public static byte[] Protect(byte[] plain)
    {
        ArgumentNullException.ThrowIfNull(plain);
        var sealedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        return [.. Magic, .. sealedBytes];
    }

    /// <exception cref="FormatException">The file was encrypted for another user or is damaged.</exception>
    public static byte[] Unprotect(byte[] stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        if (!stored.AsSpan().StartsWith(Magic))
        {
            return stored;
        }

        try
        {
            return ProtectedData.Unprotect(stored[Magic.Length..], Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new FormatException("The file is encrypted for a different Windows user or is damaged.", ex);
        }
    }

    public static bool IsProtected(byte[] stored) => stored.AsSpan().StartsWith(Magic);
}
