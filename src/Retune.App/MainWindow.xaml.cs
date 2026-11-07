using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Retune.App.Windowing;
using Retune.Mpv;
using Wpf.Ui.Controls;

namespace Retune.App;

/// <summary>
/// Phase 0 spike shell: proves smooth embedded video with WPF controls drawn on top.
/// Replaced by the real browse shell in Phase 3; the video host, overlay host and full-screen
/// controller it uses are the production components.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private const string UserAgent = "Retune/0.1 (Windows; libmpv)";

    private readonly DevOptions _dev;
    private readonly FullScreenController _fullScreen;
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private MpvClient? _mpv;
    private double _position;
    private double _duration;

    public MainWindow(DevOptions dev)
    {
        InitializeComponent();
        _dev = dev;
        ShowActivated = !dev.NoActivate;
        SourceInitialized += (_, _) => ApplyDevBounds();
        ContentRendered += (_, _) => ScheduleDevSteps();
        _fullScreen = new FullScreenController(this);
        _fullScreen.Changed += (_, _) => AppTitleBar.Visibility = _fullScreen.IsFullScreen ? Visibility.Collapsed : Visibility.Visible;

        Video.VideoWindowCreated += OnVideoWindowCreated;
        PreviewKeyDown += OnKeyDown;
        Controls.PlayPauseRequested += (_, _) => TogglePause();
        Controls.FullScreenRequested += (_, _) => _fullScreen.Toggle();
        Controls.BlockUiRequested += (_, _) => BlockUiThread();
        _statsTimer.Tick += (_, _) => RefreshStats();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Order matters: stop mpv before its native window is destroyed.
        _statsTimer.Stop();
        _mpv?.Dispose();
        _mpv = null;
        base.OnClosing(e);
    }

    private void OnVideoWindowCreated(object? sender, EventArgs e)
    {
        _mpv = MpvClient.Create(MpvOptions.ForEmbeddedPlayer(Video.VideoWindow, volume: 100, UserAgent), logLevel: null);
        if (_dev.Silent)
        {
            _mpv.SetProperty("mute", true);
        }

        _mpv.Observe("time-pos", MpvPropertyFormat.Number);
        _mpv.Observe("duration", MpvPropertyFormat.Number);
        _mpv.Observe("pause", MpvPropertyFormat.Flag);
        _mpv.EventReceived += (_, evt) => Dispatcher.BeginInvoke(() => OnMpvEvent(evt));
        _statsTimer.Start();

        if (_dev.MediaPath is not { } path)
        {
            Controls.SetTitle("No file. Start with --play <path>");
            return;
        }

        Controls.SetTitle(System.IO.Path.GetFileName(path));
        _mpv.CommandAsync("loadfile", path, "replace");
    }

    private void ApplyDevBounds()
    {
        if (_dev.WindowBounds is { } b)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Interop.NativeMethods.SetWindowPos(hwnd, 0, b.X, b.Y, b.Width, b.Height,
                Interop.NativeMethods.SwpNoActivate | Interop.NativeMethods.SwpNoZOrder);
        }
    }

    private void ScheduleDevSteps()
    {
        foreach (var step in _dev.Steps)
        {
            var timer = new DispatcherTimer { Interval = step.At };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                RunDevStep(step.Action);
            };
            timer.Start();
        }
    }

    private void RunDevStep(string action)
    {
        switch (action)
        {
            case "block":
                BlockUiThread();
                break;
            case "fullscreen":
                _fullScreen.Toggle();
                break;
            case "maximize":
                WindowState = WindowState.Maximized;
                break;
            case "restore":
                WindowState = WindowState.Normal;
                break;
            case "minimize":
                WindowState = WindowState.Minimized;
                break;
            case "front":
                // Bring forward without taking keyboard focus (unattended runs only).
                Topmost = true;
                Topmost = false;
                break;
            case "resize":
                Width = MinWidth;
                Height = MinHeight;
                break;
            case "quit":
                Close();
                break;
            default:
                Controls.SetTitle($"Unknown dev step '{action}'");
                break;
        }

        Controls.ShowChrome();
    }

    private void OnMpvEvent(MpvEvent evt)
    {
        switch (evt)
        {
            case MpvPropertyChanged { Name: "time-pos" } p:
                _position = p.Value as double? ?? 0;
                break;
            case MpvPropertyChanged { Name: "duration" } p:
                _duration = p.Value as double? ?? 0;
                break;
            case MpvPropertyChanged { Name: "pause" } p:
                Controls.SetPaused(p.Value as bool? ?? false);
                break;
            case MpvEndFile { Reason: MpvEndFileReason.Error } end:
                Controls.SetTitle($"Playback failed: {end.ErrorMessage}");
                break;
            default:
                break;
        }
    }

    private void RefreshStats()
    {
        if (_mpv is null)
        {
            return;
        }

        var decoder = _mpv.GetString("hwdec-current") is { Length: > 0 } hw and not "no" ? $"Hardware ({hw})" : "Software";
        var fps = _mpv.GetDouble("estimated-vf-fps");
        var dropped = _mpv.GetInt64("frame-drop-count") ?? 0;
        var size = _mpv.GetInt64("video-params/w") is { } w && _mpv.GetInt64("video-params/h") is { } h ? $"{w}×{h}" : "—";
        Controls.SetStats(string.Create(CultureInfo.InvariantCulture,
            $"{Clock(_position)} / {Clock(_duration)} · {size} · {fps:0.0} fps · {decoder} · dropped {dropped}"));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                TogglePause();
                break;
            case Key.F or Key.F11:
                _fullScreen.Toggle();
                break;
            case Key.Escape when _fullScreen.IsFullScreen:
                _fullScreen.Exit();
                break;
            case Key.B:
                BlockUiThread();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void TogglePause()
    {
        if (_mpv is not null)
        {
            _mpv.SetProperty("pause", !(_mpv.GetFlag("pause") ?? false));
        }
    }

    /// <summary>Spike check: video must keep playing while the WPF UI thread is blocked.</summary>
    private static void BlockUiThread() => Thread.Sleep(TimeSpan.FromSeconds(3));

    private static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
