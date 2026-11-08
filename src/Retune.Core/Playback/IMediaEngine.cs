namespace Retune.Core.Playback;

/// <summary>
/// The video engine as the playback rules see it. Implemented over libmpv in Retune.Mpv; faked in tests.
/// Implementations must raise <see cref="EventRaised"/> on the thread that owns the session (the UI thread),
/// and must not raise events for a file after <see cref="StopPlayback"/> or a newer <see cref="Load"/>.
/// </summary>
public interface IMediaEngine
{
    event EventHandler<EngineEvent>? EventRaised;

    /// <summary>Applies the live or on-demand stream profile, then starts <paramref name="url"/> unpaused.</summary>
    /// <param name="startSeconds">On-demand resume point, or null to start at the beginning (live: always null).</param>
    void Load(string url, bool live, double? startSeconds);

    /// <summary>Stops playback and releases the provider connection.</summary>
    void StopPlayback();

    void SetPaused(bool paused);

    void SeekAbsolute(double seconds);
}

/// <summary>Something the engine reports. See <see cref="IMediaEngine"/> for threading.</summary>
public abstract record EngineEvent;

public sealed record EngineFileLoaded : EngineEvent;

/// <summary>Playback (re)started and a frame is on screen (mpv PLAYBACK_RESTART).</summary>
public sealed record EngineFirstFrame : EngineEvent;

/// <summary>The file failed to play (mpv END_FILE with an error).</summary>
public sealed record EngineFailed(string Message) : EngineEvent;

/// <summary>An HTTPS stream's certificate couldn't be verified: the connection was refused before the login was sent.</summary>
public sealed record EngineCertificateRejected : EngineEvent;

/// <summary>The end was reached (mpv eof-reached became true; the last frame stays up).</summary>
public sealed record EngineReachedEnd : EngineEvent;

/// <summary>Playback moved away from the end again (seek back or replay).</summary>
public sealed record EngineLeftEnd : EngineEvent;

public sealed record EnginePosition(double Seconds) : EngineEvent;

public sealed record EngineDuration(double Seconds) : EngineEvent;

public sealed record EnginePaused(bool Paused) : EngineEvent;

public sealed record EngineBuffering(bool Buffering) : EngineEvent;
