using Microsoft.Extensions.Time.Testing;
using Retune.Infrastructure.Diagnostics;
using Retune.Infrastructure.Tests.Support;

namespace Retune.Infrastructure.Tests;

public sealed class FileAppLogTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-07T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Entries_are_timestamped_and_never_contain_urls_or_credentials()
    {
        var log = new FileAppLog(_temp.Path, _time);

        log.Failure("refresh", "Failed for http://tv.example:8080/player_api.php?username=alice&password=hunter2",
            new IOException("GET https://tv.example/get.php?username=alice&password=hunter2 failed"));

        var text = File.ReadAllText(log.FilePath);
        Assert.StartsWith("2026-11-07T12:00:00.000Z ERROR refresh:", text, StringComparison.Ordinal);
        Assert.Contains("System.IO.IOException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tv.example", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_log_rolls_over_to_one_previous_file()
    {
        var log = new FileAppLog(_temp.Path, _time);
        File.WriteAllText(log.FilePath, new string('x', (int)FileAppLog.MaxBytes + 1));

        log.Warning("test", "after the roll");

        Assert.True(File.Exists(Path.Combine(_temp.Path, "retune.1.log")));
        Assert.Contains("after the roll", File.ReadAllText(log.FilePath), StringComparison.Ordinal);
        Assert.True(new FileInfo(log.FilePath).Length < 200);
    }

    [Fact]
    public void A_locked_log_file_never_throws()
    {
        var log = new FileAppLog(_temp.Path, _time);
        using var held = new FileStream(log.FilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        log.Failure("test", "dropped");
    }
}
