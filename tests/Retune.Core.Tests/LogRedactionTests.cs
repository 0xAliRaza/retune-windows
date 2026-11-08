using Retune.Core.Diagnostics;

namespace Retune.Core.Tests;

public sealed class LogRedactionTests
{
    [Theory]
    [InlineData("see http://host:8080/live/u/p/1.ts now", "see <url> now")]
    [InlineData("rtmp://a.b/c and https://x.y/z?q=1", "<url> and <url>")]
    [InlineData("username=alice&password=s3cret", "username=<redacted>&password=<redacted>")]
    [InlineData("Token: abc123, pin=1234", "Token=<redacted>, pin=<redacted>")]
    [InlineData("Cannot open '/movie/alice/s3cret/123.mkv'", "Cannot open '/movie/<redacted>/<redacted>/123.mkv'")]
    [InlineData("GET /live/bob/pw/9.ts failed", "GET /live/<redacted>/<redacted>/9.ts failed")]
    [InlineData("nothing to hide", "nothing to hide")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Redact(string? input, string expected) => Assert.Equal(expected, LogRedaction.Redact(input));

    [Fact]
    public void Describe_includes_inner_exceptions_redacted()
    {
        var error = new InvalidOperationException("outer http://secret.example/a", new IOException("inner password=x"));

        var text = LogRedaction.Describe(error);

        Assert.Contains("System.InvalidOperationException: outer <url>", text, StringComparison.Ordinal);
        Assert.Contains("System.IO.IOException: inner password=<redacted>", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forget_logs_a_failed_task()
    {
        var log = new RecordingLog();

        Task.FromException(new IOException("boom")).Forget(log, "cache");
        await Task.Yield();

        var entry = Assert.Single(log.Entries);
        Assert.Equal("cache", entry.Area);
        Assert.IsType<IOException>(entry.Exception);
    }

    [Fact]
    public void Forget_ignores_a_successful_task()
    {
        var log = new RecordingLog();

        Task.CompletedTask.Forget(log, "cache");

        Assert.Empty(log.Entries);
    }

    private sealed class RecordingLog : IAppLog
    {
        public List<(string Area, Exception? Exception)> Entries { get; } = [];

        public void Warning(string area, string message, Exception? exception = null) => Entries.Add((area, exception));

        public void Failure(string area, string message, Exception? exception = null) => Entries.Add((area, exception));
    }
}
