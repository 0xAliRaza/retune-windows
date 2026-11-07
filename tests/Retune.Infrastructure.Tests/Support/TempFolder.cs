namespace Retune.Infrastructure.Tests.Support;

/// <summary>A unique temporary folder (with a space in its name, like the real user profile) removed on dispose.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = Directory.CreateTempSubdirectory("retune tests ").FullName;
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still open by a failing test; the OS temp cleaner will get it.
        }
    }
}
