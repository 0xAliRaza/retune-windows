using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Retune.Core.Playback;
using Retune.Presentation.Player;
using Windows.Media;

namespace Retune.App.Services;

/// <summary>
/// Connects the player to the Windows system media controls: the keyboard's media keys and the media flyout
/// (PLAN decision 4). Shows the title only; artwork is never handed to Windows, because provider URLs can carry
/// account details. Button presses arrive on a system thread and are run on the UI thread.
/// </summary>
internal sealed class SystemMediaControls : IDisposable
{
    private readonly SystemMediaTransportControls _controls;
    private readonly PlayerViewModel _player;
    private readonly Dispatcher _dispatcher;
    private MediaSessionInfo? _shown;

    private SystemMediaControls(SystemMediaTransportControls controls, PlayerViewModel player, Dispatcher dispatcher)
    {
        _controls = controls;
        _player = player;
        _dispatcher = dispatcher;
        _controls.IsStopEnabled = true;
        _controls.ButtonPressed += OnButtonPressed;
        _player.PropertyChanged += OnPlayerChanged;
        Update();
    }

    /// <summary>Null when Windows can't provide the controls for this window (the player works without them).</summary>
    public static SystemMediaControls? TryCreate(nint window, PlayerViewModel player, Dispatcher dispatcher)
    {
        try
        {
            return new SystemMediaControls(SystemMediaTransportControlsInterop.GetForWindow(window), player, dispatcher);
        }
        catch (COMException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _player.PropertyChanged -= OnPlayerChanged;
        _controls.ButtonPressed -= OnButtonPressed;
        _controls.DisplayUpdater.ClearAll();
        _controls.IsEnabled = false;
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        var info = _player.MediaSession;
        if (info == _shown)
        {
            return;
        }

        var titleChanged = info.Title != _shown?.Title || info.Subtitle != _shown?.Subtitle;
        _shown = info;
        _controls.IsEnabled = info.Status != MediaSessionStatus.Closed;
        _controls.IsPlayEnabled = info.CanPause;
        _controls.IsPauseEnabled = info.CanPause;
        _controls.IsNextEnabled = info.CanNext;
        _controls.IsPreviousEnabled = info.CanPrevious;
        _controls.PlaybackStatus = info.Status switch
        {
            MediaSessionStatus.Playing => MediaPlaybackStatus.Playing,
            MediaSessionStatus.Paused => MediaPlaybackStatus.Paused,
            MediaSessionStatus.Changing => MediaPlaybackStatus.Changing,
            _ => MediaPlaybackStatus.Closed,
        };

        if (titleChanged)
        {
            var display = _controls.DisplayUpdater;
            display.ClearAll();
            display.Type = MediaPlaybackType.Video;
            display.VideoProperties.Title = info.Title;
            display.VideoProperties.Subtitle = info.Subtitle ?? string.Empty;
            display.Update();
        }
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var button = args.Button;
        _dispatcher.BeginInvoke(() => Press(button));
    }

    private void Press(SystemMediaTransportControlsButton button)
    {
        if (!_player.IsPresented)
        {
            return;
        }

        var state = _player.State;
        var paused = state.Paused || state.Phase == PlaybackPhase.Ended;
        switch (button)
        {
            case SystemMediaTransportControlsButton.Play when paused:
            case SystemMediaTransportControlsButton.Pause when !paused:
                _player.TogglePause();
                break;
            case SystemMediaTransportControlsButton.Next:
                _player.Next();
                break;
            case SystemMediaTransportControlsButton.Previous:
                _player.Previous();
                break;
            case SystemMediaTransportControlsButton.Stop:
                _player.Close();
                break;
            default:
                break;
        }
    }
}
