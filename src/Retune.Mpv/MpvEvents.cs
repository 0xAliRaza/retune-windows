namespace Retune.Mpv;

/// <summary>Why mpv stopped playing a file (mpv_end_file_reason).</summary>
public enum MpvEndFileReason
{
    EndOfFile = 0,
    Stopped = 2,
    Quit = 3,
    Error = 4,
    Redirect = 5,
    Unknown = -1,
}

/// <summary>Typed property formats an observer can ask for.</summary>
public enum MpvPropertyFormat
{
    /// <summary>Delivered as <see cref="string"/>.</summary>
    Text,

    /// <summary>Delivered as <see cref="bool"/>.</summary>
    Flag,

    /// <summary>Delivered as <see cref="long"/>.</summary>
    WholeNumber,

    /// <summary>Delivered as <see cref="double"/>.</summary>
    Number,
}

/// <summary>Events raised by <see cref="MpvClient"/> on its event-loop thread.</summary>
public abstract record MpvEvent;

/// <summary>An observed property changed. <see cref="Value"/> is null when mpv has no value (for example, no file loaded).</summary>
public sealed record MpvPropertyChanged(string Name, object? Value) : MpvEvent;

public sealed record MpvFileStarted : MpvEvent;

public sealed record MpvFileLoaded : MpvEvent;

/// <summary>Playback (re)started after loading or seeking: the first frame is on screen.</summary>
public sealed record MpvPlaybackRestarted : MpvEvent;

public sealed record MpvEndFile(MpvEndFileReason Reason, int ErrorCode, string? ErrorMessage) : MpvEvent;

public sealed record MpvCommandFailed(ulong RequestId, int ErrorCode, string ErrorMessage) : MpvEvent;

public sealed record MpvLogMessage(string Level, string Prefix, string Text) : MpvEvent;

public sealed record MpvShutdown : MpvEvent;
