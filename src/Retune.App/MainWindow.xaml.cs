using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using Retune.App.Composition;
using Retune.App.Interop;
using Retune.App.Services;
using Retune.App.Windowing;
using Retune.Core.Models;
using Retune.Presentation.Player;
using Retune.Presentation.ViewModels;
using Wpf.Ui.Controls;

namespace Retune.App;

/// <summary>
/// The single main window: browse shell, the player layer (video + chrome) and the dialog layer.
/// Routes the keyboard to the player while it is up, otherwise to the app shortcuts.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly DialogLayer _dialogs;
    private readonly FullScreenController _fullScreen;
    private PlayerViewModel? _player;
    private SystemMediaControls? _mediaControls;
    private bool _preparedForExit;
    private readonly ScaleTransform _zoom = new();
    private bool _playerEnteredFullScreen;

    public MainWindow(AppContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        InitializeComponent();
        Container = container;
        DataContext = container.Shell;
        _dialogs = new DialogLayer(DialogLayer, DialogHost);
        container.Dialogs.Presenter = _dialogs;
        _fullScreen = new FullScreenController(this);
        _fullScreen.Changed += (_, _) => AppTitleBar.Visibility = _fullScreen.IsFullScreen ? Visibility.Collapsed : Visibility.Visible;
        PlayerChrome.Host = Overlay;
        PlayerLayer.Visibility = Visibility.Visible; // so the video window is created at startup; hidden once it exists
        Video.VideoWindowCreated += OnVideoWindowCreated;
        if (!container.Paths.IsIsolated)
        {
            SourceInitialized += (_, _) => RestorePlacement(container.Settings.Current.Window);
        }
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnMouseBack;
        Loaded += (_, _) => Controls.TitleBarAccessibility.NameCaptionButtons(AppTitleBar);

        // App-wide zoom: the browse UI, dialogs and player controls (the video itself always fills the window).
        ShellPane.LayoutTransform = _zoom;
        DialogHost.LayoutTransform = _zoom;
        PlayerChrome.LayoutTransform = _zoom;
        ApplyZoom();
        container.Shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ShellViewModel.Zoom))
            {
                ApplyZoom();
            }
        };
        PreviewMouseWheel += OnZoomWheel;
    }

    public AppContainer Container { get; }

    private ShellViewModel Shell => Container.Shell;

    protected override void OnClosing(CancelEventArgs e)
    {
        PrepareForExit();
        base.OnClosing(e);
    }

    /// <summary>
    /// Saves where the window is and shuts the player down (final progress first, mpv before its window). Runs on close
    /// and when Windows ends the session, where Closing isn't raised. Safe to call twice.
    /// </summary>
    public void PrepareForExit()
    {
        if (_preparedForExit)
        {
            return;
        }

        _preparedForExit = true;
        if (!Container.Paths.IsIsolated && CurrentPlacement() is { } placement)
        {
            Container.Settings.Update(s => s with { Window = placement });
        }

        _mediaControls?.Dispose();
        _mediaControls = null;
        Container.ShutdownPlayer();
    }

    /// <summary>
    /// Reopens where Retune was left. Windows' own placement call keeps the window on a connected screen and handles
    /// per-monitor DPI and maximised state.
    /// </summary>
    private void RestorePlacement(WindowPlacement? saved)
    {
        if (saved is null || !saved.IsValid(MinWidth, MinHeight))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        NativeMethods.SetPlacement(new WindowInteropHelper(this).Handle, saved);
    }

    /// <summary>The normal bounds and maximised state to reopen with (full screen is left first, so it counts as what it replaced).</summary>
    private WindowPlacement? CurrentPlacement()
    {
        if (_fullScreen.IsFullScreen)
        {
            _fullScreen.Exit();
        }

        return NativeMethods.GetPlacement(new WindowInteropHelper(this).Handle);
    }
    private void OnVideoWindowCreated(object? sender, EventArgs e)
    {
        try
        {
            _player = Container.AttachVideo(Video.VideoWindow, Dispatcher);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or Mpv.MpvException)
        {
            // Browsing still works; playing can't. Say so once instead of crashing at startup.
            Container.Log.Failure("player", "The video engine failed to start.", ex);
            _ = Container.Dialogs.ConfirmAsync("The video engine failed to start", "Retune can browse your playlists but can't play video. Reinstalling Retune usually fixes this.", "OK", destructive: false);
            return;
        }

        PlayerChrome.DataContext = _player;
        _mediaControls = SystemMediaControls.TryCreate(new WindowInteropHelper(this).Handle, _player, Dispatcher);
        Video.Width = double.NaN;
        Video.Height = double.NaN;
        Video.HorizontalAlignment = HorizontalAlignment.Stretch;
        Video.VerticalAlignment = VerticalAlignment.Stretch;
        Overlay.Visibility = Visibility.Visible;
        _player.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(PlayerViewModel.IsPresented) or "")
            {
                UpdatePlayerLayer();
            }
        };
        _player.FullScreenToggleRequested += (_, _) => ToggleFullScreen();
        _player.Closed += (_, _) =>
        {
            if (_playerEnteredFullScreen)
            {
                _fullScreen.Exit();
                _playerEnteredFullScreen = false;
            }

            // No rebuild here: the browse list refreshes in place (PlayingItemChanged), so it keeps its scroll position.
        };
        UpdatePlayerLayer();
    }

    private void UpdatePlayerLayer()
    {
        var presented = _player?.IsPresented == true;
        PlayerLayer.Visibility = presented ? Visibility.Visible : Visibility.Collapsed;
        if (presented)
        {
            // Out of the browse controls (search box, tiles) now hidden behind the video.
            Keyboard.Focus(this);
        }
    }

    private void ToggleFullScreen()
    {
        if (_fullScreen.IsFullScreen)
        {
            _fullScreen.Exit();
            _playerEnteredFullScreen = false;
        }
        else
        {
            _fullScreen.Enter();
            _playerEnteredFullScreen = _player?.IsPresented == true;
        }
    }

    private void OnBackdropClick(object sender, MouseButtonEventArgs e) => _dialogs.Cancel();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_dialogs.IsOpen)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (_player?.IsPresented == true)
        {
            // Keys the player doesn't use must not reach the hidden browse controls (typing into the search box,
            // Tab moving focus behind the video). Alt combinations stay with Windows (Alt+F4, Alt+Space).
            e.Handled = HandlePlayerKey(_player, key, mods) || !mods.HasFlag(ModifierKeys.Alt);
        }
        else
        {
            e.Handled = HandleAppKey(key, mods);
        }
    }

    /// <summary>Player shortcuts (spec ui-player §3.1). Ctrl combinations fall through to the app.</summary>
    private bool HandlePlayerKey(PlayerViewModel p, Key key, ModifierKeys mods)
    {
        if (mods.HasFlag(ModifierKeys.Alt))
        {
            return false; // AltGr (Ctrl+Alt) characters and Windows' Alt shortcuts are not player keys
        }

        if (mods.HasFlag(ModifierKeys.Control))
        {
            return HandleAppKey(key, mods);
        }

        p.UserActivity();
        var shift = mods.HasFlag(ModifierKeys.Shift);
        switch (key)
        {
            case Key.Space: p.TogglePause(); break;
            case Key.Left: p.Seek(shift ? -60 : -10); break;
            case Key.Right: p.Seek(shift ? 60 : 10); break;
            case Key.Up or Key.PageUp: p.Previous(); break;
            case Key.Down or Key.PageDown: p.Next(); break;
            case Key.N when shift: p.Previous(); break;
            case Key.N: p.Next(); break;
            case Key.Back: p.LastChannel(); break;
            case Key.L: p.ToggleList(); break;
            case Key.OemOpenBrackets: p.Slower(); break;
            case Key.OemCloseBrackets: p.Faster(); break;
            case Key.Oem5: p.NormalSpeed(); break;
            case Key.OemMinus or Key.Subtract: p.VolumeDown(); break;
            case Key.OemPlus or Key.Add: p.VolumeUp(); break;
            case Key.M: p.ToggleMute(); break;
            case Key.C: p.ToggleSubtitles(); break;
            case Key.F or Key.F11: ToggleFullScreen(); break;
            case Key.I: p.ToggleStats(); break;
            case Key.S: _ = p.ScreenshotAsync(); break;
            case Key.Enter: p.CommitNumber(); break;
            case Key.F1:
            case Key.OemQuestion when shift:
                p.ToggleShortcuts();
                break;
            case Key.Escape: p.Escape(); break;
            case >= Key.D0 and <= Key.D9 when !shift: p.Digit(key - Key.D0); break;
            case >= Key.NumPad0 and <= Key.NumPad9: p.Digit(key - Key.NumPad0); break;
            default: return false;
        }

        return true;
    }

    /// <summary>App-wide shortcuts (spec ui-player §3.2, mapped to Windows conventions).</summary>
    private bool HandleAppKey(Key key, ModifierKeys mods)
    {
        // AltGr arrives as Ctrl+Alt: typing "}" or "~" on a German layout must not zoom or switch sections.
        var alt = mods.HasFlag(ModifierKeys.Alt);
        var ctrl = mods.HasFlag(ModifierKeys.Control) && !alt;
        var shift = mods.HasFlag(ModifierKeys.Shift);
        var typing = Keyboard.FocusedElement is System.Windows.Controls.TextBox or System.Windows.Controls.PasswordBox;
        var playing = _player?.IsPresented == true;

        switch (key)
        {
            case Key.D1 or Key.NumPad1 when ctrl && shift: Shell.SetLayout(LayoutMode.Grid); break;
            case Key.D2 or Key.NumPad2 when ctrl && shift: Shell.SetLayout(LayoutMode.List); break;
            case Key.D3 or Key.NumPad3 when ctrl && shift: Shell.SetLayout(LayoutMode.Compact); break;
            case Key.D1 or Key.NumPad1 when ctrl && !playing: Shell.SelectSection(LibrarySection.Live); break;
            case Key.D2 or Key.NumPad2 when ctrl && !playing: Shell.SelectSection(LibrarySection.Movies); break;
            case Key.D3 or Key.NumPad3 when ctrl && !playing: Shell.SelectSection(LibrarySection.Series); break;
            case Key.F when ctrl && !playing: ShellPane.FocusSearch(); break;
            case Key.R when ctrl:
            case Key.F5:
                _ = Shell.RefreshAsync();
                break;
            case Key.N when ctrl && !playing: _ = Shell.AddPlaylistAsync(); break; // a dialog would open under the video
            case Key.L when ctrl: Shell.LockAll(); break;
            case Key.B when ctrl && !playing: Shell.ToggleSidebar(); break;
            case Key.OemPlus or Key.Add when ctrl: Shell.ZoomIn(); break;
            case Key.OemMinus or Key.Subtract when ctrl: Shell.ZoomOut(); break;
            case Key.D0 or Key.NumPad0 when ctrl: Shell.ResetZoom(); break;
            case Key.OemComma when ctrl && !playing: Shell.OpenSettings(); break;
            case Key.F11: ToggleFullScreen(); break;
            case Key.F1:
            case Key.OemQuestion when ctrl:
            case Key.OemQuestion when shift && !typing:
                ShowShortcuts();
                break;
            case Key.Left when alt && !playing:
            case Key.Back when !typing && !playing && Shell.CanGoBack:
            case Key.Escape when !typing && !playing && Shell.CanGoBack:
                Shell.Back();
                break;
            default:
                return false;
        }

        return true;
    }

    /// <summary>The sheet goes in the player's overlay while it is up (a dialog would sit under the video).</summary>
    private void ShowShortcuts()
    {
        if (_player?.IsPresented == true)
        {
            _player.ToggleShortcuts();
        }
        else
        {
            _ = Container.Dialogs.ShowShortcutsAsync();
        }
    }

    private void ApplyZoom() => _zoom.ScaleX = _zoom.ScaleY = Shell.Zoom;

    /// <summary>Ctrl + mouse wheel zooms, like a browser; in the player the plain wheel zaps channels or sets the volume.</summary>
    private void OnZoomWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _player?.IsPresented == true)
        {
            // The wheel follows keyboard focus (this window), not the pointer over the player's overlay.
            PlayerChrome.WheelFromWindow(e.Delta);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !_dialogs.IsOpen)
        {
            if (e.Delta > 0)
            {
                Shell.ZoomIn();
            }
            else
            {
                Shell.ZoomOut();
            }

            e.Handled = true;
        }
    }

    /// <summary>The mouse's Back button leaves a detail page.</summary>
    private void OnMouseBack(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1 && _player?.IsPresented != true && Shell.CanGoBack)
        {
            Shell.Back();
            e.Handled = true;
        }
    }
}
