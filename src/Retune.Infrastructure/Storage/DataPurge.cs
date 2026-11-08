using Retune.Infrastructure.Secrets;

namespace Retune.Infrastructure.Storage;

/// <summary>
/// Removes everything Retune keeps for this Windows user: saved passwords in Credential Manager, the data folder
/// (playlists, library, settings, catalog caches) and the machine-local cache and logs. Offered by the uninstaller.
/// </summary>
public static class DataPurge
{
    public sealed record Result(int Credentials, IReadOnlyList<string> RemovedFolders);

    public static Result Run(AppPaths paths, CredentialManagerSecretStore secrets)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(secrets);
        var credentials = secrets.RemoveAll();
        var localRoot = Path.GetDirectoryName(paths.CacheDirectory) ?? paths.CacheDirectory;
        var removed = new List<string>();
        foreach (var folder in new[] { paths.DataDirectory, localRoot }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            EnsureSafeToDelete(folder);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
                removed.Add(folder);
            }
        }

        return new Result(credentials, removed);
    }

    /// <summary>Only folders named Retune, and never a drive, a profile or a system folder, whatever the paths say.</summary>
    internal static void EnsureSafeToDelete(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? string.Empty);
        var protectedFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        }.Where(p => p.Length > 0).Select(Path.TrimEndingDirectorySeparator);

        if (full.Length <= root.Length || protectedFolders.Contains(full, StringComparer.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(full), "Retune", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete a drive root or a system folder.");
        }
    }
}