using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Retune.Infrastructure.Net;

/// <summary>
/// Writes the certificates Windows trusts to a PEM bundle for libmpv, whose TLS library ships without any. Streams are
/// then verified against the same roots as the rest of Windows, kept current by Windows Update: trusted roots,
/// third-party roots, and cached intermediates (some servers don't send their full chain).
/// </summary>
public static class SystemTrustStore
{
    private static readonly StoreName[] Stores = [StoreName.Root, StoreName.AuthRoot, StoreName.CertificateAuthority];

    /// <summary>Writes the bundle atomically and returns how many certificates it holds.</summary>
    /// <exception cref="IOException">The file couldn't be written.</exception>
    public static int WriteBundle(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pem = new StringBuilder();
        foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            foreach (var name in Stores)
            {
                using var store = new X509Store(name, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                foreach (var certificate in store.Certificates)
                {
                    using (certificate)
                    {
                        if (seen.Add(certificate.Thumbprint))
                        {
                            pem.AppendLine(certificate.ExportCertificatePem());
                        }
                    }
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, pem.ToString(), Encoding.ASCII);
        File.Move(temp, path, overwrite: true);
        return seen.Count;
    }
}
