namespace Retune.Infrastructure.Storage;

/// <summary>
/// Where Retune keeps its files. Data (playlists, library, settings, catalog caches) is roamable user data;
/// artwork is a disposable machine-local cache. <c>RETUNE_DATA_DIR</c> isolates everything for tests and dev runs.
/// </summary>
public sealed record AppPaths(string DataDirectory, string CacheDirectory)
{
    public const string DataDirectoryVariable = "RETUNE_DATA_DIR";

    public string CatalogDirectory => Path.Combine(DataDirectory, "Catalogs");

    public string ArtworkDirectory => Path.Combine(CacheDirectory, "Artwork");

    /// <summary>%APPDATA%\Retune and %LOCALAPPDATA%\Retune\Cache, unless <c>RETUNE_DATA_DIR</c> is set.</summary>
    public static AppPaths ForCurrentUser()
    {
        if (Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } overridden)
        {
            var root = Path.GetFullPath(overridden);
            return new AppPaths(root, Path.Combine(root, "Cache"));
        }

        return new AppPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Retune"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Retune", "Cache"));
    }
}
