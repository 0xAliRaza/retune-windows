using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Retune.App.Video;
using Retune.Core.Playback;
using Retune.Presentation.Player;
using Wpf.Ui.Controls;

namespace Retune.App.Views;

/// <summary>The player chrome drawn over the video. Behaviour lives in <see cref="PlayerViewModel"/>; this wires input.</summary>
public partial class PlayerView : UserControl
{
    /// <summary>How far one wheel notch scrolls the channel list.</summary>
    private const double ListWheelPixels = 48;

    private readonly DispatcherTimer _singleClick;
    private PlayerViewModel? _player;
    private bool _scrubbing;
    private bool _syncingVolume;

    public PlayerView()
    {
        InitializeComponent();
        _singleClick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Interop.NativeMethods.GetDoubleClickTime()) };
        _singleClick.Tick += (_, _) =>
        {
            _singleClick.Stop();
            _player?.TogglePause();
        };
        DataContextChanged += (_, _) => Attach(DataContext as PlayerViewModel);
    }

    /// <summary>The overlay window hosting this view: allowed to take keyboard focus only while the list's search box is in use.</summary>
    public OverlayHost? Host { get; set; }

    private void Attach(PlayerViewModel? player)
    {
        if (_player is not null)
        {
            _player.PropertyChanged -= OnPlayerChanged;
        }

        _player = player;
        if (player is not null)
        {
            player.PropertyChanged += OnPlayerChanged;
            Sync();
        }
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        Sync();
        if (e.PropertyName == nameof(PlayerViewModel.ListVisible) && Host is not null)
        {
            Host.AllowKeyboardFocus = _player!.ListVisible;
            if (_player.ListVisible)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ListFilterBox.Focus();
                    if (_player.ListRows.FirstOrDefault(r => r.IsCurrent) is { } current)
                    {
                        ListRowsBox.ScrollIntoView(current);
                    }
                }, DispatcherPriority.Input);
            }
        }
    }

    private void Sync()
    {
        if (_player is not { } p)
        {
            return;
        }

        PlayIcon.Symbol = p.State.Paused || p.State.Phase == PlaybackPhase.Ended ? SymbolRegular.Play24 : SymbolRegular.Pause24;
        VolumeIcon.Symbol = p.Muted ? SymbolRegular.SpeakerMute24 : p.Volume < 50 ? SymbolRegular.Speaker124 : SymbolRegular.Speaker224;
        SubtitleIcon.Filled = p.SubtitlesOn;
        Cursor = p.ControlsVisible ? null : Cursors.None;
        if (!_scrubbing)
        {
            Scrubber.Value = p.Position;
        }

        _syncingVolume = true;
        VolumeSlider.Value = p.Volume;
        _syncingVolume = false;
    }

    private void OnActivity(object sender, MouseEventArgs e) => _player?.UserActivity();

    /// <summary>One click on the bare video pauses (after the double-click window); a double click toggles full screen.</summary>
    private void OnSurfaceClick(object sender, MouseButtonEventArgs e)
    {
        _player?.UserActivity();
        if (!ReferenceEquals(e.OriginalSource, Surface))
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            _singleClick.Stop();
            _player?.ToggleFullScreen();
        }
        else
        {
            _singleClick.Start();
        }
    }

    /// <summary>Mouse wheel over the video changes the volume (Ctrl + wheel is the app zoom, handled by the window).</summary>
    private void OnSurfaceWheel(object sender, MouseWheelEventArgs e)
    {
        if (_player is null || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        Wheel(e.Delta);
        e.Handled = true;
    }

    /// <summary>
    /// Wheel input that reached the main window instead of the overlay (it follows keyboard focus): scrolls the
    /// channel list when the pointer is over it, otherwise changes the volume.
    /// </summary>
    public void WheelFromWindow(int delta, Point screenPoint)
    {
        if (_player is null)
        {
            return;
        }

        if (_player.ListVisible && ListRowsBox.IsVisible && new Rect(ListRowsBox.RenderSize).Contains(ListRowsBox.PointFromScreen(screenPoint))
            && FindScroller(ListRowsBox) is { } scroller)
        {
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset - (delta / (double)Mouse.MouseWheelDeltaForOneLine * ListWheelPixels));
            return;
        }

        Wheel(delta);
    }

    private void Wheel(int delta)
    {
        _player!.UserActivity();
        if (delta > 0)
        {
            _player.VolumeUp();
        }
        else if (delta < 0)
        {
            _player.VolumeDown();
        }
    }

    /// <summary>Middle click mutes; the mouse's Back button leaves the player.</summary>
    private void OnSurfaceButton(object sender, MouseButtonEventArgs e)
    {
        if (_player is null)
        {
            return;
        }

        switch (e.ChangedButton)
        {
            case MouseButton.Middle:
                _player.ToggleMute();
                e.Handled = true;
                break;
            case MouseButton.XButton1:
                _player.Close();
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    /// <summary>Right-click on the video: every player action, with its shortcut.</summary>
    private void OnSurfaceMenu(object sender, MouseButtonEventArgs e)
    {
        if (_player is not { } p || IsInside(e.OriginalSource as DependencyObject, ListRowsBox) || IsInside(e.OriginalSource as DependencyObject, ListFilterBox))
        {
            return;
        }

        p.UserActivity();
        var paused = p.State.Paused || p.State.Phase == PlaybackPhase.Ended;
        var items = new List<Control>
        {
            Item(paused ? "Play" : "Pause", p.TogglePause, "Space"),
            Item("Previous", p.Previous, "↑", p.State.CanGoPrevious),
            Item("Next", p.Next, "↓", p.State.CanGoNext),
            new Separator(),
            Item(p.Muted ? "Unmute" : "Mute", p.ToggleMute, "M"),
        };

        if (p.HasAudioChoice)
        {
            items.Add(Submenu("Audio", AudioItems(p)));
        }

        if (p.HasSubtitles)
        {
            items.Add(Submenu("Subtitles", SubtitleItems(p)));
        }

        if (p.State.IsSeekable)
        {
            items.Add(Submenu("Speed", SpeedItems(p)));
        }

        items.Add(new Separator());
        if (p.HasList)
        {
            items.Add(Item(p.IsLive ? "Channel list" : "Up next", p.ToggleList, "L"));
        }

        items.Add(Item("Full screen", p.ToggleFullScreen, "F"));
        items.Add(Item("Stream info", p.ToggleStats, "I"));
        items.Add(Item("Save a screenshot", () => _ = p.ScreenshotAsync(), "S"));
        items.Add(Item("Keyboard shortcuts", p.ToggleShortcuts, "?"));
        items.Add(new Separator());
        items.Add(Item("Back to browsing", p.Close, "Esc"));

        var menu = new ContextMenu { PlacementTarget = Surface, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        Open(menu, items);
        e.Handled = true;
    }

    private static System.Windows.Controls.MenuItem Item(string header, Action action, string gesture, bool enabled = true) =>
        new() { Header = header, InputGestureText = gesture, IsEnabled = enabled, Command = new CommunityToolkit.Mvvm.Input.RelayCommand(action) };

    private static System.Windows.Controls.MenuItem Submenu(string header, IEnumerable<Control> items)
    {
        var menu = new System.Windows.Controls.MenuItem { Header = header };
        foreach (var item in items)
        {
            menu.Items.Add(item);
        }

        return menu;
    }

    private static bool IsInside(DependencyObject? node, DependencyObject container)
    {
        for (; node is not null; node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, container))
            {
                return true;
            }
        }

        return false;
    }

    private static ScrollViewer? FindScroller(DependencyObject root)
    {
        if (root is ScrollViewer scroller)
        {
            return scroller;
        }

        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScroller(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnScrubStart(object sender, MouseButtonEventArgs e) => _scrubbing = true;

    private void OnScrubEnd(object sender, MouseButtonEventArgs e)
    {
        _scrubbing = false;
        _player?.SeekTo(Scrubber.Value);
    }

    private void OnVolumeSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncingVolume)
        {
            _player?.SetVolume(e.NewValue);
        }
    }

    private void OnListFilterKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _player?.Escape();
                e.Handled = true;
                break;
            case Key.Enter when _player is { ListRows.Count: > 0 }:
                _player.JumpTo(_player.ListRows[0]);
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    private void OnAudioMenu(object sender, RoutedEventArgs e)
    {
        if (_player is not { } p)
        {
            return;
        }

        Open(Menu(AudioButton), AudioItems(p));
    }

    private void OnSpeedMenu(object sender, RoutedEventArgs e)
    {
        if (_player is not { } p)
        {
            return;
        }

        Open(Menu(SpeedButton), SpeedItems(p));
    }

    /// <summary>Tracks, size, position, delay, background box and original styling, applied live.</summary>
    private void OnSubtitleMenu(object sender, RoutedEventArgs e)
    {
        if (_player is not { } p)
        {
            return;
        }

        Open(Menu(SubtitleButton), SubtitleItems(p));
    }

    private static List<Control> AudioItems(PlayerViewModel p) =>
        [.. p.AudioTracks.Select(track => new System.Windows.Controls.MenuItem { Header = track.DisplayName, IsCheckable = true, IsChecked = track.IsSelected, Command = p.SelectAudioCommand, CommandParameter = track })];

    private static List<Control> SpeedItems(PlayerViewModel p) =>
        [.. SpeedPresets.All.Select(speed => new System.Windows.Controls.MenuItem { Header = SpeedPresets.Label(speed), IsCheckable = true, IsChecked = Math.Abs(p.Speed - speed) < 0.01, Command = p.SetSpeedCommand, CommandParameter = speed })];

    private static List<Control> SubtitleItems(PlayerViewModel p)
    {
        var items = new List<Control>();
        items.Add(new System.Windows.Controls.MenuItem { Header = "Off", IsCheckable = true, IsChecked = !p.SubtitlesOn, Command = p.SelectSubtitleCommand, CommandParameter = null });
        foreach (var track in p.SubtitleTracks)
        {
            items.Add(new System.Windows.Controls.MenuItem { Header = track.DisplayName, IsCheckable = true, IsChecked = track.IsSelected, Command = p.SelectSubtitleCommand, CommandParameter = track });
        }

        var style = p.SubtitleStyle;
        items.Add(new Separator());
        items.Add(Item($"Larger ({style.Scale * 100:0}%)", () => p.UpdateSubtitleStyle(style with { Scale = style.Scale + 0.1 })));
        items.Add(Item("Smaller", () => p.UpdateSubtitleStyle(style with { Scale = style.Scale - 0.1 })));
        items.Add(Item(style.Position >= 100 ? "Move up (now at bottom)" : $"Move up (↑ {100 - style.Position:0})", () => p.UpdateSubtitleStyle(style with { Position = style.Position - 5 })));
        items.Add(Item("Move down", () => p.UpdateSubtitleStyle(style with { Position = style.Position + 5 })));
        items.Add(Item($"Delay +0.1 s (now {p.SubtitleDelay:+0.0;-0.0;0.0} s)", () => p.NudgeSubtitleDelay(0.1)));
        items.Add(Item("Delay −0.1 s", () => p.NudgeSubtitleDelay(-0.1)));
        items.Add(new System.Windows.Controls.MenuItem { Header = "Background box", IsCheckable = true, IsChecked = style.Background, Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => p.UpdateSubtitleStyle(style with { Background = !style.Background })) });
        items.Add(new System.Windows.Controls.MenuItem { Header = "Keep original styling", IsCheckable = true, IsChecked = style.KeepsOriginalStyle, ToolTip = "Use fonts and sizes embedded in styled (ASS) subtitles. Turn off if subtitles look huge or odd.", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => p.UpdateSubtitleStyle(style with { KeepsOriginalStyle = !style.KeepsOriginalStyle })) });
        items.Add(new Separator());
        items.Add(Item("Reset", p.ResetSubtitles));
        return items;
    }

    private static void Open(ContextMenu menu, IEnumerable<Control> items)
    {
        foreach (var item in items)
        {
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private static System.Windows.Controls.MenuItem Item(string header, Action action) =>
        new() { Header = header, Command = new CommunityToolkit.Mvvm.Input.RelayCommand(action) };

    private static ContextMenu Menu(UIElement target) =>
        new() { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
}
