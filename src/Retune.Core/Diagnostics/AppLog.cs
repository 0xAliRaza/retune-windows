using System.Text.RegularExpressions;

namespace Retune.Core.Diagnostics;

/// <summary>
/// The app's diagnostic log, for failures nobody is waiting on (a cache save, a background refresh, a crash).
/// Implementations must redact with <see cref="LogRedaction"/>: no URLs, credentials or personal data.
/// </summary>
public interface IAppLog
{
    void Warning(string area, string message, Exception? exception = null);

    void Failure(string area, string message, Exception? exception = null);

    /// <summary>A routine event worth having when diagnosing a report. Discarded unless the log keeps them.</summary>
    void Info(string area, string message)
    {
    }
}

/// <summary>Discards everything (tests and tools that don't care).</summary>
public sealed class NullAppLog : IAppLog
{
    public static NullAppLog Instance { get; } = new();

    public void Warning(string area, string message, Exception? exception = null)
    {
    }

    public void Failure(string area, string message, Exception? exception = null)
    {
    }
}

/// <summary>Strips anything that could identify an account from text bound for the log.</summary>
public static partial class LogRedaction
{
    public const string Url = "<url>";
    public const string Secret = "<redacted>";

    /// <summary>Replaces URLs (any scheme), Xtream stream paths (which carry the login) and credential-looking key=value pairs.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Stream paths first: the URL pass would otherwise consume their start and leave part of a password behind.
        var withoutPaths = XtreamPathPattern().Replace(text, m => $"/{m.Groups["kind"].Value}/{Secret}/{Secret}/");
        var withoutUrls = UrlPattern().Replace(withoutPaths, Url);
        return SecretPattern().Replace(withoutUrls, m => $"{m.Groups["key"].Value}={Secret}");
    }

    /// <summary>"TypeName: redacted message", then the redacted stack trace, then the same for inner exceptions.</summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var lines = new List<string>();
        for (var e = exception; e is not null; e = e.InnerException)
        {
            lines.Add($"{e.GetType().FullName}: {Redact(e.Message)}");
            if (e.StackTrace is { } stack)
            {
                lines.Add(Redact(stack));
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Any scheme, even glued to other text ("1http://…"), up to the next whitespace (quotes included).</summary>
    [GeneratedRegex(@"(?<![a-z0-9+.\-])[a-z][a-z0-9+.\-]*://\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    /// <summary>"/movie/&lt;user&gt;/&lt;password&gt;/…" as mpv sometimes prints it without the scheme and host.</summary>
    [GeneratedRegex(@"/(?<kind>live|movie|series|timeshift)/[^/\s]+/[^/\s]+/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex XtreamPathPattern();

    [GeneratedRegex(@"(?<key>\b(?:username|user|password|pass|pwd|token|auth|pin)\b)\s*[=:]\s*[^\s&;,]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();
}

public static class TaskLogging
{
    /// <summary>Runs <paramref name="task"/> without waiting, but logs it if it fails (never lost silently).</summary>
    public static void Forget(this Task task, IAppLog log, string area)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(log);
        task.ContinueWith(
            t => log.Failure(area, "A background task failed.", t.Exception?.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
