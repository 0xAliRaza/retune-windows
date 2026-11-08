namespace Retune.Core.Diagnostics;

/// <summary>
/// Lets at most <paramref name="maxPerWindow"/> messages through per window. A broken stream can make mpv print
/// hundreds of warnings a second; without a limit they would flood the log and slow mpv's event thread. Thread-safe.
/// </summary>
public sealed class LogRateLimiter(int maxPerWindow, TimeSpan window, TimeProvider time)
{
    private readonly Lock _gate = new();
    private DateTimeOffset _windowStart = DateTimeOffset.MinValue;
    private int _count;
    private int _suppressed;

    /// <summary>
    /// True when this message may be logged. <paramref name="suppressedBefore"/> is how many were dropped in the previous
    /// window (reported once, with the first message of the next).
    /// </summary>
    public bool Allow(out int suppressedBefore)
    {
        lock (_gate)
        {
            suppressedBefore = 0;
            var now = time.GetUtcNow();
            if (now - _windowStart >= window)
            {
                suppressedBefore = _suppressed;
                _windowStart = now;
                _count = 0;
                _suppressed = 0;
            }

            if (_count < maxPerWindow)
            {
                _count++;
                return true;
            }

            _suppressed++;
            return false;
        }
    }
}
