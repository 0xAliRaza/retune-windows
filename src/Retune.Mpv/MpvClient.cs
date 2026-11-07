using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Retune.Mpv.Interop;

namespace Retune.Mpv;

/// <summary>
/// One libmpv player instance. Owns the native handle and a dedicated event-loop thread.
/// All members are thread-safe. <see cref="EventReceived"/> fires on the event-loop thread,
/// so UI consumers must marshal to their dispatcher.
/// </summary>
public sealed class MpvClient : IDisposable
{
    private const ulong ObserveUserdata = 1;

    private readonly ILogger _logger;
    private readonly Thread _eventThread;
    private readonly Lock _lifecycleLock = new();
    private nint _handle;
    private long _nextRequestId;
    private volatile bool _stopping;

    private MpvClient(nint handle, ILogger logger)
    {
        _handle = handle;
        _logger = logger;
        _eventThread = new Thread(RunEventLoop) { IsBackground = true, Name = "mpv-events" };
    }

    /// <summary>Raised for every mpv event, on the event-loop thread.</summary>
    public event EventHandler<MpvEvent>? EventReceived;

    /// <summary>The client API version of the loaded libmpv (major.minor).</summary>
    public static Version ApiVersion
    {
        get
        {
            var v = MpvNative.ClientApiVersion();
            return new Version((int)(v >> 16), (int)(v & 0xFFFF));
        }
    }

    /// <summary>
    /// Creates and initialises a player. <paramref name="options"/> are applied in order before
    /// mpv_initialize (some, like <c>wid</c> and <c>vo</c>, only work there).
    /// </summary>
    /// <param name="logLevel">mpv log level forwarded as <see cref="MpvLogMessage"/> events, or null for none.</param>
    public static MpvClient Create(IEnumerable<KeyValuePair<string, string>> options, ILogger? logger = null, string? logLevel = "warn")
    {
        ArgumentNullException.ThrowIfNull(options);

        var handle = MpvNative.Create();
        if (handle == nint.Zero)
        {
            throw new MpvException("mpv_create returned no handle.");
        }

        try
        {
            foreach (var (name, value) in options)
            {
                Check(MpvNative.SetOptionString(handle, name, value), $"Setting option '{name}'");
            }

            Check(MpvNative.Initialize(handle), "mpv_initialize");
            if (logLevel is not null)
            {
                Check(MpvNative.RequestLogMessages(handle, logLevel), "mpv_request_log_messages");
            }
        }
        catch
        {
            MpvNative.TerminateDestroy(handle);
            throw;
        }

        var client = new MpvClient(handle, logger ?? NullLogger.Instance);
        client._eventThread.Start();
        return client;
    }

    public void SetProperty(string name, string value) =>
        Check(MpvNative.SetPropertyString(Handle, name, value), $"Setting '{name}'");

    public void SetProperty(string name, double value) =>
        Check(MpvNative.SetPropertyDouble(Handle, name, MpvFormat.Double, ref value), $"Setting '{name}'");

    public void SetProperty(string name, long value) =>
        Check(MpvNative.SetPropertyInt64(Handle, name, MpvFormat.Int64, ref value), $"Setting '{name}'");

    public void SetProperty(string name, bool value)
    {
        var flag = value ? 1 : 0;
        Check(MpvNative.SetPropertyFlag(Handle, name, MpvFormat.Flag, ref flag), $"Setting '{name}'");
    }

    /// <summary>Reads a property as a string, or null when it is unavailable.</summary>
    public string? GetString(string name)
    {
        var ptr = MpvNative.GetPropertyString(Handle, name);
        if (ptr == nint.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            MpvNative.Free(ptr);
        }
    }

    public double? GetDouble(string name) =>
        MpvNative.GetPropertyDouble(Handle, name, MpvFormat.Double, out var v) >= 0 ? v : null;

    public long? GetInt64(string name) =>
        MpvNative.GetPropertyInt64(Handle, name, MpvFormat.Int64, out var v) >= 0 ? v : null;

    public bool? GetFlag(string name) =>
        MpvNative.GetPropertyFlag(Handle, name, MpvFormat.Flag, out var v) >= 0 ? v != 0 : null;

    /// <summary>Runs a command synchronously. Throws <see cref="MpvException"/> on failure.</summary>
    public void Command(params string[] args)
    {
        using var native = new Utf8StringArray(args);
        Check(MpvNative.Command(Handle, native.Pointer), $"Command '{FirstWord(args)}'");
    }

    /// <summary>
    /// Queues a command without blocking. A failure arrives later as <see cref="MpvCommandFailed"/>
    /// carrying the returned request id.
    /// </summary>
    public ulong CommandAsync(params string[] args)
    {
        var id = (ulong)Interlocked.Increment(ref _nextRequestId) + ObserveUserdata;
        using var native = new Utf8StringArray(args);
        Check(MpvNative.CommandAsync(Handle, id, native.Pointer), $"Command '{FirstWord(args)}'");
        return id;
    }

    /// <summary>Starts observing a property; changes arrive as <see cref="MpvPropertyChanged"/>.</summary>
    public void Observe(string name, MpvPropertyFormat format) =>
        Check(MpvNative.ObserveProperty(Handle, ObserveUserdata, name, ToNative(format)), $"Observing '{name}'");

    /// <summary>
    /// Stops the event loop and destroys the player. Must not run concurrently with other calls on
    /// this instance, and must not be called from an <see cref="EventReceived"/> handler.
    /// </summary>
    public void Dispose()
    {
        if (Thread.CurrentThread == _eventThread)
        {
            throw new InvalidOperationException("MpvClient cannot be disposed from its own event handler; marshal to another thread first.");
        }

        lock (_lifecycleLock)
        {
            if (_handle == nint.Zero)
            {
                return;
            }

            _stopping = true;
            MpvNative.Wakeup(_handle);
            _eventThread.Join();
            MpvNative.TerminateDestroy(_handle);
            _handle = nint.Zero;
        }
    }

    internal static string ErrorText(int error) =>
        Marshal.PtrToStringUTF8(MpvNative.ErrorString(error)) ?? error.ToString(CultureInfo.InvariantCulture);

    private nint Handle
    {
        get
        {
            var h = _handle;
            ObjectDisposedException.ThrowIf(h == nint.Zero, this);
            return h;
        }
    }

    private static void Check(int result, string operation)
    {
        if (result < 0)
        {
            throw new MpvException(operation, result, ErrorText(result));
        }
    }

    private static string FirstWord(string[] args) => args.Length > 0 ? args[0] : "(empty)";

    private static MpvFormat ToNative(MpvPropertyFormat format) => format switch
    {
        MpvPropertyFormat.Text => MpvFormat.String,
        MpvPropertyFormat.Flag => MpvFormat.Flag,
        MpvPropertyFormat.WholeNumber => MpvFormat.Int64,
        MpvPropertyFormat.Number => MpvFormat.Double,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    private void RunEventLoop()
    {
        var handle = _handle;
        while (!_stopping)
        {
            var ptr = MpvNative.WaitEvent(handle, -1);
            var native = Marshal.PtrToStructure<MpvEventNative>(ptr);
            if (native.EventId == MpvEventId.None)
            {
                continue;
            }

            var evt = Translate(native);
            if (evt is not null)
            {
                Dispatch(evt);
            }

            if (native.EventId == MpvEventId.Shutdown)
            {
                return;
            }
        }
    }

    private void Dispatch(MpvEvent evt)
    {
        try
        {
            EventReceived?.Invoke(this, evt);
        }
#pragma warning disable CA1031 // A faulty subscriber must not kill the event loop; it is logged, not swallowed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "An mpv event handler threw while handling {EventType}.", evt.GetType().Name);
        }
    }

    private static MpvEvent? Translate(MpvEventNative native) => native.EventId switch
    {
        MpvEventId.PropertyChange => TranslateProperty(native.Data),
        MpvEventId.StartFile => new MpvFileStarted(),
        MpvEventId.FileLoaded => new MpvFileLoaded(),
        MpvEventId.PlaybackRestart => new MpvPlaybackRestarted(),
        MpvEventId.EndFile => TranslateEndFile(native.Data),
        MpvEventId.CommandReply when native.Error < 0 =>
            new MpvCommandFailed(native.ReplyUserdata, native.Error, ErrorText(native.Error)),
        MpvEventId.LogMessage => TranslateLog(native.Data),
        MpvEventId.Shutdown => new MpvShutdown(),
        _ => null,
    };

    private static MpvPropertyChanged TranslateProperty(nint data)
    {
        var prop = Marshal.PtrToStructure<MpvEventPropertyNative>(data);
        var name = Marshal.PtrToStringUTF8(prop.Name) ?? string.Empty;
        object? value = prop.Format switch
        {
            MpvFormat.String or MpvFormat.OsdString => Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(prop.Data)),
            MpvFormat.Flag => Marshal.ReadInt32(prop.Data) != 0,
            MpvFormat.Int64 => Marshal.ReadInt64(prop.Data),
            MpvFormat.Double => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(prop.Data)),
            _ => null,
        };
        return new MpvPropertyChanged(name, value);
    }

    private static MpvEndFile TranslateEndFile(nint data)
    {
        var end = Marshal.PtrToStructure<MpvEventEndFileNative>(data);
        var reason = Enum.IsDefined(typeof(MpvEndFileReason), end.Reason) ? (MpvEndFileReason)end.Reason : MpvEndFileReason.Unknown;
        return new MpvEndFile(reason, end.Error, end.Error < 0 ? ErrorText(end.Error) : null);
    }

    private static MpvLogMessage TranslateLog(nint data)
    {
        var log = Marshal.PtrToStructure<MpvEventLogMessageNative>(data);
        return new MpvLogMessage(
            Marshal.PtrToStringUTF8(log.Level) ?? string.Empty,
            Marshal.PtrToStringUTF8(log.Prefix) ?? string.Empty,
            (Marshal.PtrToStringUTF8(log.Text) ?? string.Empty).TrimEnd('\n'));
    }
}
