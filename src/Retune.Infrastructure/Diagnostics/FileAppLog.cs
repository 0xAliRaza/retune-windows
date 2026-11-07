using System.Globalization;
using System.Text;
using Retune.Core.Diagnostics;

namespace Retune.Infrastructure.Diagnostics;

/// <summary>
/// Appends redacted lines to <c>logs\retune.log</c> in the private data folder, keeping one previous file once it
/// passes <see cref="MaxBytes"/>. Safe from any thread.
/// </summary>
public sealed class FileAppLog : IAppLog
{
    public const long MaxBytes = 1L << 20;

    private readonly Lock _gate = new();
    private readonly string _path;
    private readonly string _previous;
    private readonly TimeProvider _time;

    public FileAppLog(string directory, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "retune.log");
        _previous = Path.Combine(directory, "retune.1.log");
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public string FilePath => _path;

    public void Warning(string area, string message, Exception? exception = null) => Write("WARN", area, message, exception);

    public void Failure(string area, string message, Exception? exception = null) => Write("ERROR", area, message, exception);

    private void Write(string level, string area, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(_time.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
            .Append(' ').Append(level).Append(' ').Append(LogRedaction.Redact(area)).Append(": ").Append(LogRedaction.Redact(message));
        if (exception is not null)
        {
            line.AppendLine().Append(LogRedaction.Describe(exception));
        }

        line.AppendLine();
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                {
                    File.Move(_path, _previous, overwrite: true);
                }

                File.AppendAllText(_path, line.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Deliberately dropped: logging must never take the app down, and there is nowhere else to report it.
            }
        }
    }
}
