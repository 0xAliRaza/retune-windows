using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Retune.App.Interop;

namespace Retune.App.Video;

/// <summary>
/// Hosts WPF content in a per-pixel-transparent native child window placed above the
/// <see cref="VideoHost"/>. This solves WPF's "airspace" limit (WPF can't draw over a native
/// window) without a separate top-level window, so the overlay moves, resizes, minimises and
/// goes full screen with the main window automatically.
/// </summary>
/// <remarks>
/// Layered child windows need Windows 8 or later. Fully transparent pixels let mouse input through;
/// give the content a near-transparent background (alpha 1) wherever it should receive the mouse.
/// </remarks>
[ContentProperty(nameof(Content))]
public sealed class OverlayHost : HwndHost
{
    private HwndSource? _source;
    private UIElement? _content;
    private nint _parent;

    /// <summary>The WPF content shown above the video.</summary>
    public UIElement? Content
    {
        get => _content;
        set
        {
            _content = value;
            if (_source is not null)
            {
                _source.RootVisual = value;
            }
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var parameters = new HwndSourceParameters("RetuneOverlay")
        {
            ParentWindow = hwndParent.Handle,
            WindowStyle = NativeMethods.WsChild | NativeMethods.WsVisible | NativeMethods.WsClipSiblings,
            UsesPerPixelTransparency = true,
            PositionX = 0,
            PositionY = 0,
            Width = 1,
            Height = 1,
        };

        _source = new HwndSource(parameters)
        {
            SizeToContent = SizeToContent.Manual,
            RootVisual = _content,
        };
        _source.CompositionTarget.BackgroundColor = Colors.Transparent;
        _source.AddHook(KeepKeyboardInParent);
        _parent = hwndParent.Handle;
        return new HandleRef(this, _source.Handle);
    }

    /// <summary>
    /// The overlay never owns keyboard focus, so every key reaches the main window exactly once and
    /// shortcuts work the same whether or not the user just clicked a control.
    /// </summary>
    /// <summary>
    /// Let the overlay own keyboard focus (while a text box in it is in use). Turning it off hands focus back
    /// to the main window.
    /// </summary>
    public bool AllowKeyboardFocus
    {
        get => _allowFocus;
        set
        {
            _allowFocus = value;
            if (!value && _parent != 0)
            {
                NativeMethods.SetFocus(_parent);
            }
        }
    }

    private bool _allowFocus;

    private nint KeepKeyboardInParent(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmSetFocus && _parent != 0 && !_allowFocus)
        {
            NativeMethods.SetFocus(_parent);
            handled = true;
        }

        return 0;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        BringToFront();
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _source?.Dispose();
        _source = null;
    }

    /// <summary>Keeps the overlay above its sibling video window in z-order.</summary>
    private void BringToFront()
    {
        if (_source is not null)
        {
            NativeMethods.SetWindowPos(_source.Handle, NativeMethods.HwndTop, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
        }
    }
}
