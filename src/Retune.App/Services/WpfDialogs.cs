using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Retune.App.Composition;
using Retune.App.Dialogs;
using Retune.Core.Models;
using Retune.Presentation.Services;
using Retune.Presentation.ViewModels;

namespace Retune.App.Services;

/// <summary>Hosts modal dialogs as an in-window overlay (dimmed backdrop + card), one at a time.</summary>
public interface IDialogPresenter
{
    /// <summary>Shows <paramref name="content"/> until it calls the close action it was given.</summary>
    Task<bool> ShowAsync(Func<Action<bool>, FrameworkElement> content);
}

public sealed class WpfDialogs(AppContainer container) : IDialogs
{
    public IDialogPresenter? Presenter { get; set; }

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive) =>
        Show(close => new ConfirmDialog(title, message, confirmLabel, destructive, close));

    public async Task ShowPlaylistEditorAsync(Playlist? playlist)
    {
        var editor = new PlaylistEditorViewModel(container.Playlists, container.Time, playlist);
        await Show(close =>
        {
            editor.Saved += (_, _) => close(true);
            return new PlaylistEditorDialog(editor, close);
        }).ConfigureAwait(true);
    }

    public Task<bool> VerifyPinAsync(string reason) =>
        Show(close => new PinDialog(new VerifyPinViewModel(container.Library, reason), close));

    public Task<bool> CreatePinAsync() =>
        Show(close => new PinDialog(new CreatePinViewModel(container.Library), close));

    public void OpenInBrowser(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private Task<bool> Show(Func<Action<bool>, FrameworkElement> content) =>
        Presenter?.ShowAsync(content) ?? Task.FromResult(false);
}

/// <summary>The overlay layer inside the main window.</summary>
public sealed class DialogLayer(Grid layer, ContentControl host) : IDialogPresenter
{
    private TaskCompletionSource<bool>? _open;

    public Task<bool> ShowAsync(Func<Action<bool>, FrameworkElement> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _open?.TrySetResult(false);
        var completion = new TaskCompletionSource<bool>();
        _open = completion;

        void Close(bool result)
        {
            if (ReferenceEquals(_open, completion))
            {
                layer.Visibility = Visibility.Collapsed;
                host.Content = null;
                _open = null;
            }

            completion.TrySetResult(result);
        }

        host.Content = content(Close);
        layer.Visibility = Visibility.Visible;
        host.Dispatcher.BeginInvoke(() => (host.Content as FrameworkElement)?.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First)));
        return completion.Task;
    }

    public bool IsOpen => _open is not null;

    public void Cancel() => _open?.TrySetResult(false);
}
