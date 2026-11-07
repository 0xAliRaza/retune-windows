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

        var menu = Menu(AudioButton);
        foreach (var track in p.AudioTracks)
        {
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = track.DisplayName, IsCheckable = true, IsChecked = track.IsSelected, Command = p.SelectAudioCommand, CommandParameter = track });
        }

        menu.IsOpen = true;
    }

    private void OnSpeedMenu(object sender, RoutedEventArgs e)
    {
        if (_player is not { } p)
        {
            return;
        }

        var menu = Menu(SpeedButton);
        foreach (var speed in SpeedPresets.All)
        {
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = SpeedPresets.Label(speed), IsCheckable = true, IsChecked = Math.Abs(p.Speed - speed) < 0.01, Command = p.SetSpeedCommand, CommandParameter = speed });
        }

        menu.IsOpen = true;
    }

    /// <summary>Tracks, size, position, delay, background box and original styling, applied live.</summary>
    private void OnSubtitleMenu(object sender, RoutedEventArgs e)
    {
        if (_player is not { } p)
        {
            return;
        }

        var menu = Menu(SubtitleButton);
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Off", IsCheckable = true, IsChecked = !p.SubtitlesOn, Command = p.SelectSubtitleCommand, CommandParameter = null });
        foreach (var track in p.SubtitleTracks)
        {
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = track.DisplayName, IsCheckable = true, IsChecked = track.IsSelected, Command = p.SelectSubtitleCommand, CommandParameter = track });
        }

        var style = p.SubtitleStyle;
        menu.Items.Add(new Separator());
        menu.Items.Add(Item($"Larger ({style.Scale * 100:0}%)", () => p.UpdateSubtitleStyle(style with { Scale = style.Scale + 0.1 })));
        menu.Items.Add(Item("Smaller", () => p.UpdateSubtitleStyle(style with { Scale = style.Scale - 0.1 })));
        menu.Items.Add(Item(style.Position >= 100 ? "Move up (now at bottom)" : $"Move up (↑ {100 - style.Position:0})", () => p.UpdateSubtitleStyle(style with { Position = style.Position - 5 })));
        menu.Items.Add(Item("Move down", () => p.UpdateSubtitleStyle(style with { Position = style.Position + 5 })));
        menu.Items.Add(Item($"Delay +0.1 s (now {p.SubtitleDelay:+0.0;-0.0;0.0} s)", () => p.NudgeSubtitleDelay(0.1)));
        menu.Items.Add(Item("Delay −0.1 s", () => p.NudgeSubtitleDelay(-0.1)));
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Background box", IsCheckable = true, IsChecked = style.Background, Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => p.UpdateSubtitleStyle(style with { Background = !style.Background })) });
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Keep original styling", IsCheckable = true, IsChecked = style.KeepsOriginalStyle, ToolTip = "Use fonts and sizes embedded in styled (ASS) subtitles. Turn off if subtitles look huge or odd.", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => p.UpdateSubtitleStyle(style with { KeepsOriginalStyle = !style.KeepsOriginalStyle })) });
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Reset", p.ResetSubtitles));
        menu.IsOpen = true;
    }

    private static System.Windows.Controls.MenuItem Item(string header, Action action) =>
        new() { Header = header, Command = new CommunityToolkit.Mvvm.Input.RelayCommand(action) };

    private static ContextMenu Menu(UIElement target) =>
        new() { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
}
