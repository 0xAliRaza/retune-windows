using Retune.Infrastructure.Secrets;
using Retune.Infrastructure.Storage;
using Retune.Infrastructure.Tests.Support;

namespace Retune.Infrastructure.Tests;

[Collection(CredentialManagerTests.Name)]
public sealed class DataPurgeTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly CredentialManagerSecretStore _secrets = new($"RetuneTest-{Guid.NewGuid():N}/");

    public void Dispose()
    {
        _secrets.RemoveAll();
        _temp.Dispose();
    }

    [Fact]
    public void Removes_saved_passwords_data_cache_and_logs()
    {
        var paths = new AppPaths(Path.Combine(_temp.Path, "Roaming", "Retune"), Path.Combine(_temp.Path, "Local", "Retune", "Cache"));
        Directory.CreateDirectory(paths.CatalogDirectory);
        Directory.CreateDirectory(paths.ArtworkDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        File.WriteAllText(Path.Combine(paths.DataDirectory, "playlists.json"), "{}");
        _secrets.Store(Guid.NewGuid(), "secret");

        var result = DataPurge.Run(paths, _secrets);

        Assert.Equal(1, result.Credentials);
        Assert.False(Directory.Exists(paths.DataDirectory));
        Assert.False(Directory.Exists(Path.Combine(_temp.Path, "Local", "Retune")));
        Assert.Equal(2, result.RemovedFolders.Count);
    }

    [Fact]
    public void Nothing_to_remove_is_fine()
    {
        var paths = new AppPaths(Path.Combine(_temp.Path, "none", "Retune"), Path.Combine(_temp.Path, "none-local", "Retune", "Cache"));

        var result = DataPurge.Run(paths, _secrets);

        Assert.Equal(0, result.Credentials);
        Assert.Empty(result.RemovedFolders);
    }

    [Fact]
    public void Refuses_drive_roots_and_profile_folders()
    {
        Assert.Throws<InvalidOperationException>(() => DataPurge.EnsureSafeToDelete(Path.GetPathRoot(_temp.Path)!));
        Assert.Throws<InvalidOperationException>(() => DataPurge.EnsureSafeToDelete(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.Throws<InvalidOperationException>(() => DataPurge.EnsureSafeToDelete(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + Path.DirectorySeparatorChar));
        Assert.Throws<InvalidOperationException>(() => DataPurge.EnsureSafeToDelete(Path.Combine(_temp.Path, "Documents")));
        DataPurge.EnsureSafeToDelete(Path.Combine(_temp.Path, "Retune"));
    }
}