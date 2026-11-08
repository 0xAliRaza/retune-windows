namespace Retune.Infrastructure.Storage;

/// <summary>
/// Where Retune keeps its files. Data (playlists, library, settings, catalog caches) is roamable user data;
/// artwork is a disposable machine-local cache. <c>RETUNE_DATA_DIR</c> isolates everything for tests and dev runs,
/// including saved passwords.
/// </summary>
public sealed record AppPaths(string DataDirectory, string CacheDirectory)
{
    public const string DataDirectoryVariable = "RETUNE_DATA_DIR";
    public const string CredentialPrefix = "Retune/";
    public const string IsolatedCredentialPrefix = "Retune-Dev/";

    /// <summary>Where saved passwords go in Credential Manager. Isolated (dev and test) runs never touch the real ones.</summary>
    public string SecretPrefix { get; init; } = CredentialPrefix;

    /// <summary>A dev or test run under <c>RETUNE_DATA_DIR</c>: nothing it does may touch the user's real Retune.</summary>
    public bool IsIsolated => SecretPrefix == IsolatedCredentialPrefix;

    public string CatalogDirectory => Path.Combine(DataDirectory, "Catalogs");

    public string ArtworkDirectory => Path.Combine(CacheDirectory, "Artwork");

    /// <summary>Machine-local, beside the cache: %LOCALAPPDATA%\Retune\Logs.</summary>
    public string LogDirectory => Path.Combine(Path.GetDirectoryName(CacheDirectory) ?? CacheDirectory, "Logs");

    /// <summary>%APPDATA%\Retune and %LOCALAPPDATA%\Retune\Cache, unless <c>RETUNE_DATA_DIR</c> is set.</summary>
    public static AppPaths ForCurrentUser() => From(Environment.GetEnvironmentVariable);

    /// <summary>The real folders, ignoring <c>RETUNE_DATA_DIR</c> (the uninstaller's purge must clean what the user actually has).</summary>
    public static AppPaths ForRealUser() => From(_ => null);

    internal static AppPaths From(Func<string, string?> environment)
    {
        if (environment(DataDirectoryVariable) is { Length: > 0 } overridden)
        {
            var root = Path.GetFullPath(overridden);
            return new AppPaths(root, Path.Combine(root, "Cache")) { SecretPrefix = IsolatedCredentialPrefix };
        }

        return new AppPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Retune"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Retune", "Cache"));
    }
}
