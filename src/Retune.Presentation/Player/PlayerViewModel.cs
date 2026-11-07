using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Retune.Core.Errors;
using Retune.Core.Formatting;
using Retune.Core.Models;
using Retune.Core.Playback;
using Retune.Core.Search;
using Retune.Presentation.Services;

namespace Retune.Presentation.Player;

/// <summary>A row in the channel / up-next list panel.</summary>
public sealed record LineupRow(int Index, PlaybackItem Item, bool IsCurrent)
{
    public string Label => Item.Kind == ContentKind.Episode && Item.Subtitle is { } s ? s : Item.Title;

    public string? Number => Item.Number?.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A label/value row in the stats panel.</summary>
public sealed record StatRow(string Label, string Value);

/// <summary>
/// The player screen: playback through <see cref="PlaybackSession"/>, plus everything around it — controls that
/// auto-hide, OSD flashes, tracks, subtitles, speed, volume, stats, list panel, number entry, screenshots, EPG.
/// UI thread only. Spec: docs/spec/ui-player.md §3.1 and §4.
/// </summary>
public sealed partial class PlayerViewModel : ObservableObject, IPlayerLauncher, IDisposable
{
    public static readonly TimeSpan ControlsTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan FlashDuration = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan SpinnerDelay = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan NumberEntryTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly IPlayerEngine _engine;
    private readonly PlaybackSession _session;
    private readonly PlaylistController _playlists;
    private readonly SettingsService _settings;
    private readonly IKeepAwake _keepAwake;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ITimer _statsTimer;
    private ITimer? _hideTimer;
    private ITimer? _flashTimer;
    private ITimer? _spinnerTimer;
    private ITimer? _numberTimer;
    private ITimer? _epgTimer;
    private CancellationTokenSource? _epg;
    private IReadOnlyList<EpgEntry> _guide = [];
    private long? _lastSubtitle;
    private PlaybackItem? _shownItem;

    public PlayerViewModel(IPlayerEngine engine, PlaybackSession session, PlaylistController playlists, SettingsService settings, IKeepAwake keepAwake, TimeProvider time, Action<Action> post)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _playlists = playlists;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _keepAwake = keepAwake;
        _time = time;
        _post = post;

        var stored = settings.Current;
        SubtitleStyle = stored.SubtitleStyle.Clamped();
        _session.AutoplayEnabled = stored.AutoplayNext;
        _engine.SetVolume(stored.Volume);
        _engine.ApplySubtitleStyle(SubtitleStyle);

        _settings.Changed += OnSettingsChanged;
        _session.StateChanged += (_, _) => OnStateChanged();
        _engine.EventRaised += OnEngineEvent;
        _statsTimer = time.CreateTimer(_ => _post(RefreshStats), null, StatsInterval, StatsInterval);
        RefreshEngineState();
    }

    public event EventHandler? PlayingItemChanged;

    /// <summary>F / F11 / double-click: the view owns the window.</summary>
    public event EventHandler? FullScreenToggleRequested;

    /// <summary>The player closed: the view leaves full screen (if it entered it) and refocuses browse.</summary>
    public event EventHandler? Closed;

    public PlaybackState State => _session.State;

    public string? PlayingItemId => IsPresented ? State.Item?.Id : null;

    [ObservableProperty]
    public partial bool IsPresented { get; private set; }

    [ObservableProperty]
    public partial bool ControlsVisible { get; private set; } = true;

    [ObservableProperty]
    public partial bool ListVisible { get; private set; }

    [ObservableProperty]
    public partial string ListFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<LineupRow> ListRows { get; private set; } = [];

    [ObservableProperty]
    public partial bool StatsVisible { get; private set; }

    /// <summary>The Keyboard Shortcuts panel (?, F1, Ctrl+/). Drawn in the player's overlay so it sits above the video.</summary>
    [ObservableProperty]
    public partial bool ShortcutsVisible { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<StatRow> StatsRows { get; private set; } = [];

    [ObservableProperty]
    public partial string? Readout { get; private set; }

    [ObservableProperty]
    public partial string? QualityLabel { get; private set; }

    [ObservableProperty]
    public partial string? FlashText { get; private set; }

    [ObservableProperty]
    public partial bool ShowsSpinner { get; private set; }

    [ObservableProperty]
    public partial string? NumberEntry { get; private set; }

    [ObservableProperty]
    public partial string? NowText { get; private set; }

    [ObservableProperty]
    public partial string? NextText { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<MediaTrack> AudioTracks { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<MediaTrack> SubtitleTracks { get; private set; } = [];

    [ObservableProperty]
    public partial SubtitleStyle SubtitleStyle { get; private set; }

    [ObservableProperty]
    public partial double SubtitleDelay { get; private set; }

    public string Title => State.Item?.Title ?? string.Empty;

    public string? Subtitle => NowText ?? State.Item?.Subtitle;

    public Uri? Artwork => State.Item?.Artwork;

    public bool IsLive => State.IsLive;

    public bool NoAudio => State.HasVideoFrame && _engine.Tracks.Count > 0 && _engine.Tracks.All(t => t.Type != TrackType.Audio);

    public double Position => State.Position;

    public double Duration => State.Duration;

    public string PositionText => Format.Clock(State.Position);

    public string RemainingText => $"−{Format.Clock(Math.Max(0, State.Duration - State.Position))}";

    public double Volume => _engine.Volume;

    public bool Muted => _engine.Muted || _engine.Volume <= 0;

    public string VolumeText => VolumeLevel.Label(_engine.Volume);

    public double Speed => _engine.Speed;

    public string SpeedText => SpeedPresets.Label(_engine.Speed);

    public bool SubtitlesOn => SubtitleTracks.Any(t => t.IsSelected);

    public bool HasSubtitles => SubtitleTracks.Count > 0;

    public bool HasAudioChoice => AudioTracks.Count > 1;

    public bool HasList => State.Count > 1;

    public bool ShowsTuning => State.Phase is PlaybackPhase.Tuning or PlaybackPhase.Reconnecting && !State.HasVideoFrame;

    public bool ShowsError => State.Phase == PlaybackPhase.Failed;

    public bool CanNextChannel => ShowsError && State.IsLive && State.Count > 1;

    public string TuningStatus => State.Phase == PlaybackPhase.Reconnecting
        ? $"Reconnecting… ({State.ReconnectAttempt}/{ReconnectPolicy.MaxAttempts})"
        : State.IsLive ? "Tuning…" : "Loading…";

    public int? UpNextSeconds => State.UpNextSecondsLeft;

    public PlaybackItem? UpNextItem => State.UpNextSecondsLeft is not null && State.Index + 1 < State.Count ? _session.Lineup?.Items[State.Index + 1] : null;

    // ---- IPlayerLauncher --------------------------------------------------------------------------

    public void Play(Lineup lineup, bool fromResumePoint = true)
    {
        ArgumentNullException.ThrowIfNull(lineup);
        ListVisible = false;
        StatsVisible = false;
        IsPresented = true;
        _session.Start(lineup, fromResumePoint);
        UserActivity();
    }

    public void StopForPlaylistChange()
    {
        if (IsPresented)
        {
            Close();
        }
    }

    // ---- commands ---------------------------------------------------------------------------------

    [RelayCommand]
    public void Close()
    {
        if (!IsPresented)
        {
            return;
        }

        _session.Stop();
        IsPresented = false;
        ListVisible = false;
        StatsVisible = false;
        ShortcutsVisible = false;
        NumberEntry = null;
        _epg?.Cancel();
        _keepAwake.KeepAwake(false);
        _settings.Flush();
        Closed?.Invoke(this, EventArgs.Empty);
        PlayingItemChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Esc: close the list, else the stats, else the player.</summary>
    public void Escape()
    {
        if (ShortcutsVisible)
        {
            ShortcutsVisible = false;
        }
        else if (ListVisible)
        {
            ListVisible = false;
        }
        else if (StatsVisible)
        {
            StatsVisible = false;
        }
        else
        {
            Close();
        }
    }

    [RelayCommand]
    public void TogglePause()
    {
        _session.TogglePause();
        UserActivity();
    }

    [RelayCommand]
    public void Seek(double seconds)
    {
        if (!State.IsSeekable)
        {
            return;
        }

        _engine.SeekRelative(seconds);
        Flash(seconds < 0 ? $"−{-seconds:0}s" : $"+{seconds:0}s");
    }

    [RelayCommand]
    public void SeekBack() => Seek(-10);

    [RelayCommand]
    public void SeekForward() => Seek(10);

    /// <summary>The scrubber: absolute position, released.</summary>
    public void SeekTo(double seconds)
    {
        if (State.IsSeekable)
        {
            _engine.SeekAbsolute(Math.Clamp(seconds, 0, State.Duration));
        }
    }

    [RelayCommand]
    public void Next() => _session.Step(+1);

    [RelayCommand]
    public void Previous() => _session.Step(-1);

    [RelayCommand]
    public void LastChannel() => _session.LastChannel();

    [RelayCommand]
    public void ToggleList()
    {
        if (!HasList)
        {
            return;
        }

        ListVisible = !ListVisible;
        ListFilter = string.Empty;
        RebuildList();
        UserActivity();
    }

    [RelayCommand]
    public void JumpTo(LineupRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        ListVisible = false;
        _session.JumpTo(row.Index);
    }

    [RelayCommand]
    public void Faster() => ChangeSpeed(SpeedPresets.Step(_engine.Speed, faster: true));

    [RelayCommand]
    public void Slower() => ChangeSpeed(SpeedPresets.Step(_engine.Speed, faster: false));

    [RelayCommand]
    public void NormalSpeed() => ChangeSpeed(1);

    [RelayCommand]
    public void SetSpeed(double speed) => ChangeSpeed(speed);

    [RelayCommand]
    public void VolumeUp() => ChangeVolume(VolumeLevel.Stepped(_engine.Volume, up: true), flash: true);

    [RelayCommand]
    public void VolumeDown() => ChangeVolume(VolumeLevel.Stepped(_engine.Volume, up: false), flash: true);

    /// <summary>The slider.</summary>
    public void SetVolume(double volume) => ChangeVolume(VolumeLevel.Clamped(volume), flash: false);

    [RelayCommand]
    public void ToggleMute()
    {
        var muted = !_engine.Muted;
        _engine.SetMuted(muted);
        Flash(muted ? "Muted" : "Sound on");
    }

    /// <summary>C: off, or back to the last used track, else the first.</summary>
    [RelayCommand]
    public void ToggleSubtitles()
    {
        if (SubtitleTracks.Count == 0)
        {
            Flash("No subtitles in this stream");
            return;
        }

        if (SubtitleTracks.FirstOrDefault(t => t.IsSelected) is { } on)
        {
            _lastSubtitle = on.Id;
            _engine.SelectSubtitle(null);
            Flash("Subtitles off");
        }
        else
        {
            _engine.SelectSubtitle(SubtitleTracks.FirstOrDefault(t => t.Id == _lastSubtitle)?.Id ?? SubtitleTracks[0].Id);
            Flash("Subtitles on");
        }
    }

    [RelayCommand]
    public void SelectAudio(MediaTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        _engine.SelectAudio(track.Id);
    }

    [RelayCommand]
    public void SelectSubtitle(MediaTrack? track) => _engine.SelectSubtitle(track?.Id);

    public void UpdateSubtitleStyle(SubtitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        SubtitleStyle = style.Clamped();
        _engine.ApplySubtitleStyle(SubtitleStyle);
        _settings.Update(s => s with { SubtitleStyle = SubtitleStyle }, debounce: true);
    }

    [RelayCommand]
    public void NudgeSubtitleDelay(double seconds)
    {
        SubtitleDelay = Math.Round(SubtitleDelay + seconds, 1);
        _engine.SetSubtitleDelay(SubtitleDelay);
    }

    [RelayCommand]
    public void ResetSubtitles()
    {
        UpdateSubtitleStyle(SubtitleStyle.Default);
        SubtitleDelay = 0;
        _engine.SetSubtitleDelay(0);
    }

    [RelayCommand]
    public void ToggleStats()
    {
        StatsVisible = !StatsVisible;
        RefreshStats();
    }

    [RelayCommand]
    public void ToggleShortcuts()
    {
        ShortcutsVisible = !ShortcutsVisible;
        UserActivity();
    }

    [RelayCommand]
    public void ToggleFullScreen() => FullScreenToggleRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void Retry() => _session.Retry();

    [RelayCommand]
    public void NextChannel() => _session.Step(+1);

    [RelayCommand]
    public void PlayNextNow() => _session.PlayNextNow();

    [RelayCommand]
    public void CancelUpNext() => _session.CancelAutoplay();

    /// <summary>S: saves the frame on screen as Pictures\Retune\Retune &lt;date&gt; at &lt;time&gt;.png.</summary>
    [RelayCommand]
    public async Task ScreenshotAsync()
    {
        if (!State.HasVideoFrame)
        {
            Flash("Couldn't save the screenshot");
            return;
        }

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Retune");
        Directory.CreateDirectory(folder);
        var path = ScreenshotFile.UniquePath(folder, _time.GetLocalNow().DateTime, File.Exists);
        Flash(await _engine.ScreenshotAsync(path).ConfigureAwait(true) ? @"Screenshot saved to Pictures\Retune" : "Couldn't save the screenshot");
    }

    /// <summary>Typing a channel number tunes to it after 1.5 s (or Enter). Live lineups only.</summary>
    public void Digit(int digit)
    {
        if (!State.IsLive || digit is < 0 or > 9 || (NumberEntry?.Length ?? 0) >= 5)
        {
            return;
        }

        NumberEntry = (NumberEntry ?? string.Empty) + digit.ToString(CultureInfo.InvariantCulture);
        _numberTimer?.Dispose();
        _numberTimer = _time.CreateTimer(_ => _post(CommitNumber), null, NumberEntryTimeout, Timeout.InfiniteTimeSpan);
        UserActivity();
    }

    public void CommitNumber()
    {
        _numberTimer?.Dispose();
        var entry = NumberEntry;
        NumberEntry = null;
        if (entry is null || !int.TryParse(entry, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || _session.Lineup is not { } lineup)
        {
            return;
        }

        var index = -1;
        for (var i = 0; i < lineup.Items.Count; i++)
        {
            if (lineup.Items[i].Number == number)
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            _session.JumpTo(index);
        }
        else
        {
            Flash($"No channel {number} in this list");
        }
    }

    /// <summary>Any key, click or mouse move: show the controls and restart the 3 s hide timer.</summary>
    public void UserActivity()
    {
        ControlsVisible = true;
        _hideTimer?.Dispose();
        _hideTimer = _time.CreateTimer(_ => _post(HideControlsIfIdle), null, ControlsTimeout, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _statsTimer.Dispose();
        _hideTimer?.Dispose();
        _flashTimer?.Dispose();
        _spinnerTimer?.Dispose();
        _numberTimer?.Dispose();
        _epgTimer?.Dispose();
        _epg?.Cancel();
        _epg?.Dispose();
        _engine.EventRaised -= OnEngineEvent;
        _settings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => _session.AutoplayEnabled = _settings.Current.AutoplayNext;

    partial void OnListFilterChanged(string value) => RebuildList();

    // ---- internals --------------------------------------------------------------------------------

    private void HideControlsIfIdle()
    {
        if (!State.Paused && !ListVisible && !ShortcutsVisible && State.Phase == PlaybackPhase.Playing)
        {
            ControlsVisible = false;
        }
    }

    private void Flash(string text)
    {
        FlashText = text;
        _flashTimer?.Dispose();
        _flashTimer = _time.CreateTimer(_ => _post(() => FlashText = null), null, FlashDuration, Timeout.InfiniteTimeSpan);
    }

    private void ChangeSpeed(double speed)
    {
        if (State.IsLive)
        {
            return;
        }

        _engine.SetSpeed(speed);
        Flash($"Speed {SpeedPresets.Label(speed).Replace("Normal", "1×", StringComparison.Ordinal)}");
    }

    private void ChangeVolume(double volume, bool flash)
    {
        _engine.SetVolume(volume);
        if (volume > 0 && _engine.Muted)
        {
            _engine.SetMuted(false);
        }

        _settings.Update(s => s with { Volume = volume }, debounce: true);
        if (flash)
        {
            Flash($"Volume {VolumeLevel.Label(volume)}");
        }
    }

    private void OnStateChanged()
    {
        if (!ReferenceEquals(_shownItem, State.Item))
        {
            _shownItem = State.Item;
            _guide = [];
            NowText = NextText = null;
            QualityLabel = null;
            Readout = null;
            AudioTracks = SubtitleTracks = [];
            SubtitleDelay = 0;
            LoadGuide();
            RebuildList();
            PlayingItemChanged?.Invoke(this, EventArgs.Empty);
        }

        UpdateSpinner();
        _keepAwake.KeepAwake(IsPresented && State.Phase == PlaybackPhase.Playing && !State.Paused);
        if (State.Paused || State.Phase != PlaybackPhase.Playing)
        {
            ControlsVisible = true;
        }

        OnPropertyChanged(string.Empty);
    }

    private void OnEngineEvent(object? sender, EngineEvent e)
    {
        switch (e)
        {
            case EngineTracksChanged:
                AudioTracks = _engine.Tracks.Where(t => t.Type == TrackType.Audio).ToList();
                SubtitleTracks = _engine.Tracks.Where(t => t.Type == TrackType.Subtitle).ToList();
                OnPropertyChanged(nameof(SubtitlesOn));
                OnPropertyChanged(nameof(HasSubtitles));
                OnPropertyChanged(nameof(HasAudioChoice));
                OnPropertyChanged(nameof(NoAudio));
                break;
            case EngineVideoSize size:
                QualityLabel = VideoQuality.Label(size.Width, size.Height);
                break;
            case EngineVolume or EngineSpeed:
                RefreshEngineState();
                break;
            default:
                break;
        }
    }

    private void RefreshEngineState()
    {
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(Muted));
        OnPropertyChanged(nameof(VolumeText));
        OnPropertyChanged(nameof(Speed));
        OnPropertyChanged(nameof(SpeedText));
    }

    /// <summary>The spinner only appears for buffering that lasts over 250 ms after the first frame (no flicker on quick seeks).</summary>
    private void UpdateSpinner()
    {
        var buffering = State.Buffering && State.HasVideoFrame && State.Phase == PlaybackPhase.Playing;
        if (!buffering)
        {
            _spinnerTimer?.Dispose();
            _spinnerTimer = null;
            ShowsSpinner = false;
            return;
        }

        _spinnerTimer ??= _time.CreateTimer(_ => _post(() => ShowsSpinner = State.Buffering && State.HasVideoFrame), null, SpinnerDelay, Timeout.InfiniteTimeSpan);
    }

    private void RefreshStats()
    {
        if (!IsPresented || !State.HasVideoFrame)
        {
            return;
        }

        var s = _engine.ReadStats();
        Readout = s.Readout;
        QualityLabel = s.QualityLabel ?? QualityLabel;
        RefreshGuideText();
        if (!StatsVisible)
        {
            return;
        }

        string Or(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v;
        StatsRows =
        [
            new("Resolution", s.Width is { } w && s.Height is { } h ? $"{w}×{h} ({s.QualityLabel})" : "—"),
            new("Video", Or(string.Join(" · ", new[] { s.VideoCodec, s.PixelFormat, s.Hdr ? "HDR" : null }.Where(p => p is not null)))),
            new("Decoder", s.HardwareDecoder is { } hw ? $"Hardware ({hw})" : "Software (CPU)"),
            new("Frame rate", s.SourceFps is { } src ? s.RenderedFps is { } r ? $"{src:0.###} source · {r:0.##} rendered" : $"{src:0.###} source" : "—"),
            new("Dropped", $"{s.DroppedFrames ?? 0} output · {s.DecoderDroppedFrames ?? 0} decoder"),
            new("Video bitrate", s.VideoBitrate is { } vb ? Format.Bitrate(vb) : "—"),
            new("Audio", Or(string.Join(" · ", new[] { s.AudioCodec, s.AudioChannels, s.AudioSampleRate is { } sr ? $"{sr / 1000:0.#} kHz" : null, s.AudioBitrate is { } ab ? Format.Bitrate(ab) : null }.Where(p => p is not null)))),
            new("Buffer", s.BufferSeconds is { } b ? $"{b:0.0} s ahead" : "—"),
            new("Network", s.NetworkBytesPerSecond is { } n ? Format.Bitrate(n * 8) : "—"),
            new("A/V sync", s.AvSyncSeconds is { } av ? $"{av * 1000:+0;-0;0} ms" : "—"),
        ];
    }

    private void RebuildList()
    {
        if (_session.Lineup is not { } lineup)
        {
            ListRows = [];
            return;
        }

        var terms = SearchText.Terms(ListFilter);
        ListRows = lineup.Items
            .Select((item, i) => new LineupRow(i, item, i == State.Index))
            .Where(r => terms.Length == 0 || SearchText.Matches(SearchText.Normalize($"{r.Number} {r.Label} {r.Item.Title}"), terms))
            .ToList();
    }

    /// <summary>Short EPG for Xtream live channels; refetched when the programme changes (60 s – 6 h).</summary>
    private async void LoadGuide()
    {
        _epg?.Cancel();
        _epgTimer?.Dispose();
        if (State.Item is not { IsLive: true, StreamId: { } streamId } item || _playlists.Client is not { } client)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _epg = cts;
        try
        {
            var guide = await client.ShortEpgAsync(streamId, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested || !ReferenceEquals(State.Item, item))
            {
                return;
            }

            _guide = guide;
            RefreshGuideText();
            if (EpgRefresh.Delay(guide, _time.GetUtcNow()) is { } wait)
            {
                _epgTimer = _time.CreateTimer(_ => _post(LoadGuide), null, wait, Timeout.InfiniteTimeSpan);
            }
        }
        catch (RetuneException)
        {
            _guide = [];
        }
        catch (OperationCanceledException)
        {
            // The channel changed.
        }
    }

    private void RefreshGuideText()
    {
        var now = NowNext.At(_guide, _time.GetUtcNow());
        NowText = now.Now is { } n ? $"Now: {n.Title} · {Format.TimeRange(n.Start, n.End)}" : null;
        NextText = now.Next is { } x ? $"Next: {x.Title} at {Format.Time(x.Start)}" : null;
        OnPropertyChanged(nameof(Subtitle));
    }
}
