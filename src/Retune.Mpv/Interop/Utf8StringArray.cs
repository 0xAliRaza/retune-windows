using System.Runtime.InteropServices;

namespace Retune.Mpv.Interop;

/// <summary>
/// A NULL-terminated <c>const char**</c> of UTF-8 strings in unmanaged memory,
/// as mpv_command expects. Freed on dispose.
/// </summary>
internal sealed class Utf8StringArray : IDisposable
{
    private readonly nint[] _strings;

    public Utf8StringArray(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _strings = new nint[values.Count];
        Pointer = Marshal.AllocHGlobal(nint.Size * (values.Count + 1));
        for (var i = 0; i < values.Count; i++)
        {
            _strings[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
            Marshal.WriteIntPtr(Pointer, i * nint.Size, _strings[i]);
        }

        Marshal.WriteIntPtr(Pointer, values.Count * nint.Size, nint.Zero);
    }

    public nint Pointer { get; }

    public void Dispose()
    {
        foreach (var s in _strings)
        {
            Marshal.FreeCoTaskMem(s);
        }

        Marshal.FreeHGlobal(Pointer);
    }
}
