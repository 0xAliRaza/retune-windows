using System.Globalization;

namespace Retune.Core.Playback;

public enum TrackType
{
    Video,
    Audio,
    Subtitle,
}

/// <summary>An audio, video or subtitle track of the playing file.</summary>
public sealed record MediaTrack(long Id, TrackType Type, string? Language, string? Title, string? Codec, int? Channels, bool IsSelected)
{
    /// <summary>"English · Commentary (AAC, 5.1)" — spec ui-player §4.6.</summary>
    public string DisplayName
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Language))
            {
                parts.Add(LanguageName(Language));
            }

            if (!string.IsNullOrWhiteSpace(Title) && !parts.Contains(Title.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(Title.Trim());
            }

            var name = parts.Count > 0 ? string.Join(" · ", parts) : $"Track {Id}";
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(Codec))
            {
                details.Add(Codec.ToUpperInvariant());
            }

            if (Type == TrackType.Audio && Channels is { } ch && ch > 0)
            {
                details.Add(ch switch { 6 => "5.1", 8 => "7.1", 2 => "stereo", 1 => "mono", _ => $"{ch}ch" });
            }

            return details.Count > 0 ? $"{name} ({string.Join(", ", details)})" : name;
        }
    }

    private static string LanguageName(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code.Trim());
            return culture.ThreeLetterISOLanguageName == "ivl" ? code.ToUpperInvariant() : culture.NativeName.Split(" (")[0];
        }
        catch (CultureNotFoundException)
        {
            return code.ToUpperInvariant();
        }
    }
}

/// <summary>The "stats for nerds" panel, read from mpv once a second.</summary>
public sealed record PlaybackStats(
    long? Width = null,
    long? Height = null,
    string? VideoCodec = null,
    string? PixelFormat = null,
    bool Hdr = false,
    string? HardwareDecoder = null,
    double? SourceFps = null,
    double? RenderedFps = null,
    long? DroppedFrames = null,
    long? DecoderDroppedFrames = null,
    double? VideoBitrate = null,
    string? AudioCodec = null,
    string? AudioChannels = null,
    double? AudioSampleRate = null,
    double? AudioBitrate = null,
    double? BufferSeconds = null,
    double? NetworkBytesPerSecond = null,
    double? AvSyncSeconds = null)
{
    public string? QualityLabel => Width is { } w && Height is { } h ? VideoQuality.Label(w, h) : null;

    /// <summary>"1080p · 25 fps" for the control bar.</summary>
    public string? Readout
    {
        get
        {
            var fps = SourceFps ?? RenderedFps;
            var parts = new[] { QualityLabel, fps is { } f && f > 0 ? $"{f.ToString("0.##", CultureInfo.InvariantCulture)} fps" : null }.Where(p => p is not null);
            var text = string.Join(" · ", parts);
            return text.Length > 0 ? text : null;
        }
    }
}

/// <summary>
/// Everything the player UI can ask of the video engine beyond the playback lifecycle in <see cref="IMediaEngine"/>.
/// Same threading rule: events on the owner's thread.
/// </summary>
public interface IPlayerEngine : IMediaEngine
{
    double Volume { get; }

    bool Muted { get; }

    double Speed { get; }

    IReadOnlyList<MediaTrack> Tracks { get; }

    void SetVolume(double volume);

    void SetMuted(bool muted);

    void SetSpeed(double speed);

    void SeekRelative(double seconds);

    void SelectAudio(long id);

    /// <param name="id">Null turns subtitles off.</param>
    void SelectSubtitle(long? id);

    void ApplySubtitleStyle(SubtitleStyle style);

    void SetSubtitleDelay(double seconds);

    PlaybackStats ReadStats();

    /// <summary>Saves the current video frame (no overlays) at source resolution. False when no frame is available.</summary>
    Task<bool> ScreenshotAsync(string path);
}

/// <summary>Tracks were added, removed or switched.</summary>
public sealed record EngineTracksChanged : EngineEvent;

public sealed record EngineVolume(double Volume, bool Muted) : EngineEvent;

public sealed record EngineSpeed(double Speed) : EngineEvent;

public sealed record EngineVideoSize(long Width, long Height) : EngineEvent;
