using System.Runtime.InteropServices;

namespace Retune.Mpv.Interop;

internal enum MpvFormat
{
    None = 0,
    String = 1,
    OsdString = 2,
    Flag = 3,
    Int64 = 4,
    Double = 5,
    Node = 6,
}

internal enum MpvEventId
{
    None = 0,
    Shutdown = 1,
    LogMessage = 2,
    GetPropertyReply = 3,
    SetPropertyReply = 4,
    CommandReply = 5,
    StartFile = 6,
    EndFile = 7,
    FileLoaded = 8,
    ClientMessage = 16,
    VideoReconfig = 17,
    AudioReconfig = 18,
    Seek = 20,
    PlaybackRestart = 21,
    PropertyChange = 22,
    QueueOverflow = 24,
    Hook = 25,
}

/// <summary>mpv_event. Layout must match client.h exactly.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventNative
{
    public MpvEventId EventId;
    public int Error;
    public ulong ReplyUserdata;
    public nint Data;
}

/// <summary>mpv_event_property.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventPropertyNative
{
    public nint Name;
    public MpvFormat Format;
    public nint Data;
}

/// <summary>Leading fields of mpv_event_end_file (the rest is not used).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventEndFileNative
{
    public int Reason;
    public int Error;
}

/// <summary>mpv_event_log_message.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventLogMessageNative
{
    public nint Prefix;
    public nint Level;
    public nint Text;
    public int LogLevel;
}
