using System.Globalization;
using Retune.Core.Playback;

namespace Retune.Mpv;

/// <summary>
/// <see cref="IPlayerEngine"/> over libmpv. mpv's events arrive on its own thread and are re-raised through
/// <c>post</c> on the owner's (UI) thread. Spec: docs/spec/ui-player.md §4.3.
/// </summary>
public sealed class MpvEngine : IPlayerEngine, IDisposable
{
    private readonly MpvClient _mpv;
    private readonly Action<Action> _post;
    private bool _pausedForCache;
    private bool _seeking;
    private static readonly TimeSpan ScreenshotShutdownWait = TimeSpan.FromSeconds(5);

    private static readonly string[] CertificateFailureMarkers =
    [
        "certificate verification failed",
        "certificate verify failed",
        "SSL peer certificate",
        "unable to get local issuer certificate",
        "self signed certificate",
        "self-signed certificate",
        "certificate has expired",
        "SEC_E_UNTRUSTED_ROOT",
        "SEC_E_CERT_EXPIRED",
        "CERT_E_UNTRUSTEDROOT",
    ];

    private volatile bool _certificateRejected;
    private IReadOnlyList<MediaTrack> _tracks = [];
    private Task<bool>? _screenshot;

    public MpvEngine(MpvClient mpv, Action<Action> post)
    {
        _mpv = mpv ?? throw new ArgumentNullException(nameof(mpv));
        _post = post ?? throw new ArgumentNullException(nameof(post));
        foreach (var (name, format) in Observed)
        {
            _mpv.Observe(name, format);
        }

        _mpv.EventReceived += OnMpvEvent;
        Volume = _mpv.GetDouble("volume") ?? VolumeLevel.Default;
    }

    public event EventHandler<EngineEvent>? EventRaised;

    public double Volume { get; private set; }

    public bool Muted { get; private set; }

    public double Speed { get; private set; } = 1;

    public IReadOnlyList<MediaTrack> Tracks => _tracks;

    private static (string Name, MpvPropertyFormat Format)[] Observed { get; } =
    [
        ("pause", MpvPropertyFormat.Flag),
        ("time-pos", MpvPropertyFormat.Number),
        ("duration", MpvPropertyFormat.Number),
        ("paused-for-cache", MpvPropertyFormat.Flag),
        ("seeking", MpvPropertyFormat.Flag),
        ("eof-reached", MpvPropertyFormat.Flag),
        ("volume", MpvPropertyFormat.Number),
        ("mute", MpvPropertyFormat.Flag),
        ("speed", MpvPropertyFormat.Number),
        ("track-list/count", MpvPropertyFormat.WholeNumber),
        ("aid", MpvPropertyFormat.Text),
        ("sid", MpvPropertyFormat.Text),
        ("video-params/w", MpvPropertyFormat.WholeNumber),
        ("video-params/h", MpvPropertyFormat.WholeNumber),
    ];

    public void Load(string url, bool live, double? startSeconds)
    {
        ArgumentNullException.ThrowIfNull(url);
        foreach (var (name, value) in StreamProfile.For(live))
        {
            _mpv.SetProperty(name, value);
        }

        if (StreamProfile.ForcedSpeed(live) is { } speed)
        {
            _mpv.SetProperty("speed", speed);
        }

        _mpv.SetProperty("start", startSeconds is { } s && s > 0 ? s.ToString("0", CultureInfo.InvariantCulture) : "none");
        _mpv.SetProperty("sub-delay", 0.0);
        _mpv.CommandAsync("loadfile", url, "replace");
        _mpv.SetProperty("pause", false);
    }

    public void StopPlayback() => _mpv.CommandAsync("stop");

    public void SetPaused(bool paused) => _mpv.SetProperty("pause", paused);

    public void SeekAbsolute(double seconds) =>
        _mpv.CommandAsync("seek", Math.Max(0, seconds).ToString("0.###", CultureInfo.InvariantCulture), "absolute+keyframes");

    public void SeekRelative(double seconds) =>
        _mpv.CommandAsync("seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "relative+exact");

    public bool CanSeekTo(double seconds) =>
        _mpv.GetFlag("seekable") != false
        || seconds <= (_mpv.GetDouble("time-pos") ?? 0)
        || seconds <= (_mpv.GetDouble("demuxer-cache-time") ?? 0);

    public void SetVerifyCertificates(bool verify) => _mpv.SetProperty("tls-verify", verify);

    public void SetVolume(double volume) => _mpv.SetProperty("volume", VolumeLevel.Clamped(volume));

    public void SetMuted(bool muted) => _mpv.SetProperty("mute", muted);

    public void SetSpeed(double speed) => _mpv.SetProperty("speed", speed);

    public void SelectAudio(long id) => _mpv.SetProperty("aid", id);

    public void SelectSubtitle(long? id)
    {
        if (id is { } track)
        {
            _mpv.SetProperty("sid", track);
        }
        else
        {
            _mpv.SetProperty("sid", "no");
        }
    }

    public void ApplySubtitleStyle(SubtitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        foreach (var (name, value) in style.MpvProperties())
        {
            _mpv.SetProperty(name, value);
        }
    }

    public void SetSubtitleDelay(double seconds) => _mpv.SetProperty("sub-delay", seconds);

    public PlaybackStats ReadStats()
    {
        var gamma = _mpv.GetString("video-params/gamma");
        var hwdec = _mpv.GetString("hwdec-current");
        return new PlaybackStats(
            Width: _mpv.GetInt64("video-params/w"),
            Height: _mpv.GetInt64("video-params/h"),
            VideoCodec: _mpv.GetString("video-codec"),
            PixelFormat: _mpv.GetString("video-params/pixelformat"),
            Hdr: gamma is "pq" or "hlg",
            HardwareDecoder: hwdec is null or "no" or "" ? null : hwdec,
            SourceFps: _mpv.GetDouble("container-fps"),
            RenderedFps: _mpv.GetDouble("estimated-vf-fps"),
            DroppedFrames: _mpv.GetInt64("frame-drop-count"),
            DecoderDroppedFrames: _mpv.GetInt64("decoder-frame-drop-count"),
            VideoBitrate: _mpv.GetDouble("video-bitrate"),
            AudioCodec: _mpv.GetString("audio-codec-name"),
            AudioChannels: _mpv.GetString("audio-params/hr-channels"),
            AudioSampleRate: _mpv.GetDouble("audio-params/samplerate"),
            AudioBitrate: _mpv.GetDouble("audio-bitrate"),
            BufferSeconds: _mpv.GetDouble("demuxer-cache-duration"),
            NetworkBytesPerSecond: _mpv.GetDouble("cache-speed"),
            AvSyncSeconds: _mpv.GetDouble("avsync"));
    }

    public Task<bool> ScreenshotAsync(string path)
    {
        // Kept so Dispose can wait for it: mpv must not be destroyed while it is still writing the picture.
        var shot = Task.Run(() =>
        {
            try
            {
                _mpv.Command("screenshot-to-file", path, "video");
                return File.Exists(path);
            }
            catch (MpvException)
            {
                return false;
            }
        });
        _screenshot = shot;
        return shot;
    }

    /// <summary>Waits briefly for a screenshot in progress, since mpv is destroyed right after.</summary>
    public void Dispose()
    {
        _mpv.EventReceived -= OnMpvEvent;
        _screenshot?.Wait(ScreenshotShutdownWait);
    }

    private void OnMpvEvent(object? sender, MpvEvent e)
    {
        // Runs on the mpv thread: translate here (reading track lists is safe from any thread), raise on the owner's.
        var translated = Translate(e);
        if (translated is { Count: > 0 })
        {
            _post(() =>
            {
                foreach (var evt in translated)
                {
                    EventRaised?.Invoke(this, evt);
                }
            });
        }
    }

    private List<EngineEvent>? Translate(MpvEvent e)
    {
        switch (e)
        {
            case MpvFileStarted:
                _certificateRejected = false;
                return [new EngineBuffering(true)];
            case MpvFileLoaded:
                _tracks = ReadTracks();
                return [new EngineFileLoaded(), new EngineTracksChanged()];
            case MpvPlaybackRestarted:
                return [new EngineFirstFrame(), new EngineBuffering(IsBuffering())];
            case MpvLogMessage log when IsCertificateFailure(log.Text):
                // mpv only reports "loading failed"; its log says why, so remember it for the end-file that follows.
                _certificateRejected = true;
                return null;
            case MpvEndFile { Reason: MpvEndFileReason.Error } end:
                return _certificateRejected ? [new EngineCertificateRejected()] : [new EngineFailed(end.ErrorMessage ?? "unknown error")];
            case MpvPropertyChanged p:
                return TranslateProperty(p);
            default:
                return null;
        }
    }

    private List<EngineEvent>? TranslateProperty(MpvPropertyChanged p)
    {
        switch (p.Name)
        {
            case "time-pos" when p.Value is double position:
                return [new EnginePosition(position)];
            case "duration" when p.Value is double duration:
                return [new EngineDuration(duration)];
            case "pause" when p.Value is bool paused:
                return [new EnginePaused(paused)];
            case "paused-for-cache":
                _pausedForCache = p.Value is true;
                return [new EngineBuffering(IsBuffering())];
            case "seeking":
                _seeking = p.Value is true;
                return [new EngineBuffering(IsBuffering())];
            case "eof-reached":
                return p.Value is true ? [new EngineReachedEnd()] : p.Value is false ? [new EngineLeftEnd()] : null;
            case "volume" when p.Value is double volume:
                Volume = volume;
                return [new EngineVolume(Volume, Muted)];
            case "mute" when p.Value is bool muted:
                Muted = muted;
                return [new EngineVolume(Volume, Muted)];
            case "speed" when p.Value is double speed:
                Speed = speed;
                return [new EngineSpeed(speed)];
            case "track-list/count" or "aid" or "sid":
                _tracks = ReadTracks();
                return [new EngineTracksChanged()];
            case "video-params/w" or "video-params/h":
                return _mpv.GetInt64("video-params/w") is { } w && _mpv.GetInt64("video-params/h") is { } h ? [new EngineVideoSize(w, h)] : null;
            default:
                return null;
        }
    }

    private bool IsBuffering() => _pausedForCache || _seeking;

    /// <summary>The wording of curl's, OpenSSL's and Schannel's certificate errors as mpv logs them.</summary>
    private static bool IsCertificateFailure(string text) =>
        CertificateFailureMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private List<MediaTrack> ReadTracks()
    {
        var count = _mpv.GetInt64("track-list/count") ?? 0;
        var tracks = new List<MediaTrack>((int)Math.Clamp(count, 0, 256));
        for (var i = 0; i < Math.Min(count, 256); i++)
        {
            var prefix = $"track-list/{i.ToString(CultureInfo.InvariantCulture)}/";
            var type = _mpv.GetString(prefix + "type") switch
            {
                "audio" => TrackType.Audio,
                "sub" => TrackType.Subtitle,
                "video" => TrackType.Video,
                _ => (TrackType?)null,
            };
            if (type is not { } t || _mpv.GetInt64(prefix + "id") is not { } id)
            {
                continue;
            }

            tracks.Add(new MediaTrack(
                id,
                t,
                _mpv.GetString(prefix + "lang"),
                _mpv.GetString(prefix + "title"),
                _mpv.GetString(prefix + "codec"),
                (int?)_mpv.GetInt64(prefix + "demux-channel-count"),
                _mpv.GetFlag(prefix + "selected") == true));
        }

        return tracks;
    }
}
