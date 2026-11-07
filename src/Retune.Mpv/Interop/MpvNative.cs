using System.Runtime.InteropServices;

namespace Retune.Mpv.Interop;

/// <summary>
/// Raw libmpv client API (mpv/client.h, API 2.x). Everything here is thread-safe except
/// <see cref="WaitEvent"/>, which only the event-loop thread may call.
/// </summary>
internal static partial class MpvNative
{
    private const string Library = "libmpv-2.dll";

    [LibraryImport(Library, EntryPoint = "mpv_client_api_version")]
    internal static partial ulong ClientApiVersion();

    [LibraryImport(Library, EntryPoint = "mpv_create")]
    internal static partial nint Create();

    [LibraryImport(Library, EntryPoint = "mpv_initialize")]
    internal static partial int Initialize(nint ctx);

    [LibraryImport(Library, EntryPoint = "mpv_terminate_destroy")]
    internal static partial void TerminateDestroy(nint ctx);

    [LibraryImport(Library, EntryPoint = "mpv_error_string")]
    internal static partial nint ErrorString(int error);

    [LibraryImport(Library, EntryPoint = "mpv_free")]
    internal static partial void Free(nint data);

    [LibraryImport(Library, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SetOptionString(nint ctx, string name, string data);

    [LibraryImport(Library, EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SetPropertyString(nint ctx, string name, string data);

    [LibraryImport(Library, EntryPoint = "mpv_set_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SetPropertyDouble(nint ctx, string name, MpvFormat format, ref double data);

    [LibraryImport(Library, EntryPoint = "mpv_set_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SetPropertyInt64(nint ctx, string name, MpvFormat format, ref long data);

    [LibraryImport(Library, EntryPoint = "mpv_set_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SetPropertyFlag(nint ctx, string name, MpvFormat format, ref int data);

    [LibraryImport(Library, EntryPoint = "mpv_get_property_string", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetPropertyString(nint ctx, string name);

    [LibraryImport(Library, EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int GetPropertyDouble(nint ctx, string name, MpvFormat format, out double data);

    [LibraryImport(Library, EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int GetPropertyInt64(nint ctx, string name, MpvFormat format, out long data);

    [LibraryImport(Library, EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int GetPropertyFlag(nint ctx, string name, MpvFormat format, out int data);

    [LibraryImport(Library, EntryPoint = "mpv_command")]
    internal static partial int Command(nint ctx, nint args);

    [LibraryImport(Library, EntryPoint = "mpv_command_async")]
    internal static partial int CommandAsync(nint ctx, ulong replyUserdata, nint args);

    [LibraryImport(Library, EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int ObserveProperty(nint ctx, ulong replyUserdata, string name, MpvFormat format);

    [LibraryImport(Library, EntryPoint = "mpv_request_log_messages", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int RequestLogMessages(nint ctx, string minLevel);

    [LibraryImport(Library, EntryPoint = "mpv_wait_event")]
    internal static partial nint WaitEvent(nint ctx, double timeout);

    [LibraryImport(Library, EntryPoint = "mpv_wakeup")]
    internal static partial void Wakeup(nint ctx);
}
