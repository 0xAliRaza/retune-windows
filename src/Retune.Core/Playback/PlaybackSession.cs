using Retune.Core.Models;
using Retune.Core.Policies;

namespace Retune.Core.Playback;

/// <summary>
/// The player's rules, independent of any UI or video engine: TV-remote zapping with a 350 ms debounce,
/// last-channel jump, automatic reconnects (1/2/4/6 s), dropped-connection detection, progress reporting
/// and next-episode autoplay. Single-threaded: call it, and deliver engine events, on one thread; timer
/// callbacks are marshalled there through <c>post</c>. Spec: docs/spec/ui-player.md §3.1, §4.10–4.11.
/// </summary>
public sealed class PlaybackSession : IDisposable
{
    public static readonly TimeSpan ZapDebounce = TimeSpan.FromMilliseconds(350);
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(5);
    public const int AutoplayCountdownSeconds = 8;

    public const string UnsupportedAddressMessage = "This stream uses an address type Retune doesn't play.";
    public const string LiveStoppedMessage = "The live stream stopped.";
    public const string ConnectionDroppedMessage = "The connection dropped.";

    private readonly IMediaEngine _engine;
    private readonly IPlaybackObserver _observer;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;

    private Lineup? _lineup;
    private int _index = -1;
    private int? _previousIndex;
    private double? _requestedStart;
    private double? _previousEarlyEnd;
    private int _generation;
    private bool _loadIssued;
    private ITimer? _zapTimer;
    private ITimer? _retryTimer;
    private ITimer? _progressTimer;
    private ITimer? _autoplayTimer;

    /// <param name="post">Runs an action on the session's thread (the UI dispatcher in the app; inline in tests).</param>
    public PlaybackSession(IMediaEngine engine, IPlaybackObserver observer, TimeProvider time, Action<Action> post)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _engine.EventRaised += OnEngineEvent;
    }

    public event EventHandler? StateChanged;

    public PlaybackState State { get; private set; } = PlaybackState.Idle;

    /// <summary>Roll into the next episode of the same series when one ends (setting, default on).</summary>
    public bool AutoplayEnabled { get; set; } = true;

    public Lineup? Lineup => _lineup;

    /// <summary>Plays a new lineup at its start index. <paramref name="resume"/> false means "Start Over".</summary>
    public void Start(Lineup lineup, bool resume = true)
    {
        ArgumentNullException.ThrowIfNull(lineup);
        FinishCurrent();
        _lineup = lineup;
        _previousIndex = null;
        _index = lineup.StartIndex;
        LoadCurrent(resume ? StartPoint.SavedResume : StartPoint.Beginning);
    }

    /// <summary>
    /// ↑/↓, PgUp/PgDn, N. Stops the current stream at once (freeing the provider's single connection) and
    /// shows the target, but only connects once the user stops pressing for 350 ms.
    /// </summary>
    public void Step(int delta)
    {
        if (_lineup is null || State.Item is null || Lineup.Target(_index, delta, _lineup.Items.Count, State.IsLive) is not { } target)
        {
            return;
        }

        FinishCurrent();
        _engine.StopPlayback();
        MoveTo(target);
        ShowTuning(_lineup.Items[target]);
        _zapTimer = Schedule(ZapDebounce, () => LoadCurrent(StartPoint.SavedResume));
    }

    /// <summary>Picks an item from the list panel: immediate, no debounce.</summary>
    public void JumpTo(int index)
    {
        if (_lineup is null || index < 0 || index >= _lineup.Items.Count)
        {
            return;
        }

        FinishCurrent();
        MoveTo(index);
        LoadCurrent(StartPoint.SavedResume);
    }

    /// <summary>Backspace: flips between the last two channels (live only).</summary>
    public void LastChannel()
    {
        if (State.IsLive && _previousIndex is { } previous)
        {
            JumpTo(previous);
        }
    }

    /// <summary>"Try Again": a fresh round of retries from the current resume point.</summary>
    public void Retry()
    {
        if (_lineup is null || State.Item is null)
        {
            return;
        }

        CancelTimers();
        var resumeAt = RetryPosition();
        Update(State with { ReconnectAttempt = 0 });
        LoadCurrent(StartPoint.Explicit, resumeAt);
    }

    /// <summary>Space. At the end of a file, play starts again from the beginning.</summary>
    public void TogglePause()
    {
        switch (State.Phase)
        {
            case PlaybackPhase.Ended:
                CancelAutoplay();
                _engine.SeekAbsolute(0);
                _engine.SetPaused(false);
                break;
            case PlaybackPhase.Playing or PlaybackPhase.Tuning:
                _engine.SetPaused(!State.Paused);
                break;
            default:
                break;
        }
    }

    /// <summary>"Play Now" on the up-next card.</summary>
    public void PlayNextNow()
    {
        if (State.UpNextSecondsLeft is not null)
        {
            JumpTo(_index + 1);
        }
    }

    public void CancelAutoplay()
    {
        DisposeTimer(ref _autoplayTimer);
        if (State.UpNextSecondsLeft is not null)
        {
            Update(State with { UpNextSecondsLeft = null });
        }
    }

    /// <summary>Leaves the player: saves the final progress and releases the stream.</summary>
    public void Stop()
    {
        FinishCurrent();
        _loadIssued = false;
        _engine.StopPlayback();
        _lineup = null;
        _index = -1;
        _previousIndex = null;
        Update(PlaybackState.Idle);
    }

    public void Dispose()
    {
        CancelTimers();
        _engine.EventRaised -= OnEngineEvent;
    }

    // ---- loading -------------------------------------------------------------------------------

    private enum StartPoint
    {
        SavedResume,
        Beginning,
        Explicit,
    }

    private void LoadCurrent(StartPoint startPoint, double? explicitStart = null)
    {
        var item = _lineup!.Items[_index];
        _generation++;
        CancelTimers();

        if (!UrlPolicy.IsAllowedStream(item.Url))
        {
            // Whatever played before must not keep running behind the error panel.
            _loadIssued = false;
            _engine.StopPlayback();
            Update(Fresh(item) with { Phase = PlaybackPhase.Failed, ErrorMessage = UnsupportedAddressMessage });
            return;
        }

        var start = item.IsLive ? null : startPoint switch
        {
            StartPoint.SavedResume => _observer.ResumePositionFor(item),
            StartPoint.Explicit => explicitStart,
            _ => null,
        };

        var attempt = startPoint == StartPoint.Explicit && State.Item == item ? State.ReconnectAttempt : 0;
        _requestedStart = start;
        Update(Fresh(item) with { ReconnectAttempt = attempt });

        if (item.IsLive)
        {
            _observer.LiveChannelStarted(item);
        }
        else
        {
            var generation = _generation;
            _progressTimer = Schedule(ProgressInterval, () => ReportProgress(isFinal: false), ProgressInterval, generation);
        }

        _loadIssued = true;
        _engine.Load(item.Url, item.IsLive, start);
    }

    private void ShowTuning(PlaybackItem item)
    {
        _generation++;
        _loadIssued = false;
        Update(Fresh(item));
    }

    private PlaybackState Fresh(PlaybackItem item) => State with
    {
        Phase = PlaybackPhase.Tuning,
        Item = item,
        Index = _index,
        Count = _lineup?.Items.Count ?? 0,
        Wraps = item.IsLive,
        Position = 0,
        Duration = 0,
        Buffering = true,
        HasVideoFrame = false,
        ReconnectAttempt = 0,
        ErrorMessage = null,
        UpNextSecondsLeft = null,
        LineupTitle = _lineup?.Title,
    };

    private void MoveTo(int index)
    {
        if (index != _index)
        {
            _previousIndex = _index;
        }

        _index = index;
        _previousEarlyEnd = null;
    }

    // ---- engine events -------------------------------------------------------------------------

    private void OnEngineEvent(object? sender, EngineEvent e)
    {
        // While nothing is loading (zap debounce, retry wait, failed) every event belongs to a stream we already
        // stopped; attributing a late failure to the newly tuned item would burn its retries and skip the debounce.
        if (State.Item is null || State.Phase is PlaybackPhase.Idle || !_loadIssued)
        {
            return;
        }

        switch (e)
        {
            case EngineFirstFrame when State.Phase is PlaybackPhase.Tuning or PlaybackPhase.Playing or PlaybackPhase.Ended:
                Update(State with
                {
                    Phase = State.Phase == PlaybackPhase.Ended ? PlaybackPhase.Ended : PlaybackPhase.Playing,
                    HasVideoFrame = true,
                    Buffering = false,
                    ReconnectAttempt = 0,
                });
                break;
            case EnginePosition p:
                Update(State with { Position = p.Seconds });
                break;
            case EngineDuration d:
                Update(State with { Duration = d.Seconds });
                break;
            case EnginePaused p:
                Update(State with { Paused = p.Paused });
                break;
            case EngineBuffering b:
                Update(State with { Buffering = b.Buffering });
                break;
            case EngineFailed f when State.Phase is PlaybackPhase.Tuning or PlaybackPhase.Playing:
                HandleFailure($"This stream couldn't be played ({f.Message}). The channel may be offline or your connection limit reached.");
                break;
            case EngineReachedEnd when State.Phase is PlaybackPhase.Playing or PlaybackPhase.Tuning:
                HandleEnd();
                break;
            case EngineLeftEnd when State.Phase == PlaybackPhase.Ended:
                CancelAutoplay();
                Update(State with { Phase = PlaybackPhase.Playing });
                StartProgressTimer();
                break;
            default:
                break;
        }
    }

    private void HandleEnd()
    {
        if (State.IsLive)
        {
            HandleFailure(LiveStoppedMessage);
            return;
        }

        if (EarlyEnd.IsDroppedConnection(State.Position, State.Duration, _previousEarlyEnd))
        {
            _previousEarlyEnd = State.Position;
            HandleFailure(ConnectionDroppedMessage);
            return;
        }

        ReportProgress(isFinal: true);
        DisposeTimer(ref _progressTimer);
        Update(State with { Phase = PlaybackPhase.Ended, Buffering = false });

        var next = _index + 1 < _lineup!.Items.Count ? _lineup.Items[_index + 1] : null;
        if (Lineup.ShouldAutoplay(State.Item!, next, AutoplayEnabled))
        {
            StartAutoplayCountdown();
        }
    }

    private void HandleFailure(string message)
    {
        ReportProgress(isFinal: true);
        var attempt = State.ReconnectAttempt + 1;
        _loadIssued = false;
        _engine.StopPlayback();
        CancelTimers();

        if (ReconnectPolicy.DelayBefore(attempt) is not { } delay)
        {
            Update(State with { Phase = PlaybackPhase.Failed, ErrorMessage = message, Buffering = false });
            return;
        }

        var resumeAt = RetryPosition();
        Update(State with { Phase = PlaybackPhase.Reconnecting, ReconnectAttempt = attempt, Buffering = true });
        _retryTimer = Schedule(delay, () => LoadCurrent(StartPoint.Explicit, resumeAt));
    }

    /// <summary>On-demand: where playback got to (once a frame showed), else where it was asked to start. Live: the live edge.</summary>
    private double? RetryPosition() =>
        State.IsLive ? null : State.HasVideoFrame && State.Position > 0 ? State.Position : _requestedStart;

    // ---- progress and autoplay -----------------------------------------------------------------

    private void ReportProgress(bool isFinal)
    {
        if (State is { Item: { IsLive: false } item, HasVideoFrame: true, Duration: > 0 })
        {
            _observer.ReportProgress(item, State.Position, State.Duration, isFinal);
        }
    }

    private void StartProgressTimer()
    {
        if (!State.IsLive)
        {
            DisposeTimer(ref _progressTimer);
            _progressTimer = Schedule(ProgressInterval, () => ReportProgress(isFinal: false), ProgressInterval);
        }
    }

    private void StartAutoplayCountdown()
    {
        Update(State with { UpNextSecondsLeft = AutoplayCountdownSeconds });
        _autoplayTimer = Schedule(TimeSpan.FromSeconds(1), () =>
        {
            var left = (State.UpNextSecondsLeft ?? 1) - 1;
            if (left > 0)
            {
                Update(State with { UpNextSecondsLeft = left });
                return;
            }

            CancelAutoplay();
            Step(+1);
        }, TimeSpan.FromSeconds(1));
    }

    /// <summary>Saves the final progress of what's playing and cancels every pending timer.</summary>
    private void FinishCurrent()
    {
        ReportProgress(isFinal: true);
        CancelTimers();
        if (State.UpNextSecondsLeft is not null)
        {
            Update(State with { UpNextSecondsLeft = null });
        }
    }

    // ---- timers --------------------------------------------------------------------------------

    /// <summary>A timer whose callback runs on the session thread and is ignored once a newer load started.</summary>
    private ITimer Schedule(TimeSpan due, Action action, TimeSpan? period = null, int? generation = null)
    {
        var expected = generation ?? _generation;
        return _time.CreateTimer(
            _ => _post(() =>
            {
                if (expected == _generation)
                {
                    action();
                }
            }),
            state: null,
            due,
            period ?? Timeout.InfiniteTimeSpan);
    }

    private void CancelTimers()
    {
        DisposeTimer(ref _zapTimer);
        DisposeTimer(ref _retryTimer);
        DisposeTimer(ref _progressTimer);
        DisposeTimer(ref _autoplayTimer);
    }

    private static void DisposeTimer(ref ITimer? timer)
    {
        timer?.Dispose();
        timer = null;
    }

    private void Update(PlaybackState state)
    {
        if (state == State)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
