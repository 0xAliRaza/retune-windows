using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace Retune.App.Spike;

/// <summary>Phase 0 spike controls drawn over the video. Replaced by the real player chrome in Phase 4.</summary>
public partial class SpikeOverlay : UserControl
{
    private static readonly TimeSpan AutoHideDelay = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _hideTimer = new() { Interval = AutoHideDelay };
    private bool _paused;

    public SpikeOverlay()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => SetChromeVisible(_paused);
        Root.MouseMove += (_, _) => ShowChrome();
        Root.MouseLeftButtonDown += OnRootMouseDown;
        ShowChrome();
    }

    public event EventHandler? PlayPauseRequested;

    public event EventHandler? FullScreenRequested;

    public event EventHandler? BlockUiRequested;

    public void SetTitle(string title) => TitleText.Text = title;

    public void SetStats(string stats) => StatsText.Text = stats;

    public void SetPaused(bool paused)
    {
        _paused = paused;
        PlayPauseIcon.Symbol = paused ? SymbolRegular.Play24 : SymbolRegular.Pause24;
        ShowChrome();
    }

    private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Only clicks on the bare video count; clicks anywhere on the chrome (buttons, bar padding) must not pause.
        if (!ReferenceEquals(e.OriginalSource, Root))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            FullScreenRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            PlayPauseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ShowChrome()
    {
        SetChromeVisible(true);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void SetChromeVisible(bool visible)
    {
        Chrome.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        Cursor = visible ? null : Cursors.None;
    }

    private void OnPlayPause(object sender, RoutedEventArgs e) => PlayPauseRequested?.Invoke(this, EventArgs.Empty);

    private void OnFullScreen(object sender, RoutedEventArgs e) => FullScreenRequested?.Invoke(this, EventArgs.Empty);

    private void OnBlockUi(object sender, RoutedEventArgs e) => BlockUiRequested?.Invoke(this, EventArgs.Empty);
}
