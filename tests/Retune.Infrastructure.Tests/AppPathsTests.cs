using Retune.Infrastructure.Storage;

namespace Retune.Infrastructure.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void Normal_runs_use_the_real_folders_and_credentials()
    {
        var paths = AppPaths.From(_ => null);

        Assert.Equal(AppPaths.CredentialPrefix, paths.SecretPrefix);
        Assert.EndsWith(Path.Combine("Retune"), paths.DataDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("Retune", "Logs"), paths.LogDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Isolated_runs_never_touch_real_files_or_saved_passwords()
    {
        var root = Path.Combine(Path.GetTempPath(), "retune-isolated");

        var paths = AppPaths.From(name => name == AppPaths.DataDirectoryVariable ? root : null);

        Assert.Equal(AppPaths.IsolatedCredentialPrefix, paths.SecretPrefix);
        Assert.NotEqual(AppPaths.CredentialPrefix, paths.SecretPrefix);
        Assert.StartsWith(root, paths.DataDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(root, paths.CacheDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(root, paths.LogDirectory, StringComparison.OrdinalIgnoreCase);
    }
}