using System.Windows.Input;
using Retune.App.Composition;
using Retune.App.Services;
using Retune.Core.Models;
using Retune.Presentation.ViewModels;
using Wpf.Ui.Controls;

namespace Retune.App;

/// <summary>The single main window: title bar, browse shell and the modal dialog layer. App-wide shortcuts live here.</summary>
public partial class MainWindow : FluentWindow
{
    private readonly DialogLayer _dialogs;

    public MainWindow(AppContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        InitializeComponent();
        Container = container;
        DataContext = container.Shell;
        _dialogs = new DialogLayer(DialogLayer, DialogHost);
        container.Dialogs.Presenter = _dialogs;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnMouseBack;
    }

    public AppContainer Container { get; }

    private ShellViewModel ViewModel => Container.Shell;

    private void OnBackdropClick(object sender, MouseButtonEventArgs e) => _dialogs.Cancel();

    /// <summary>App-wide shortcuts (spec ui-player §3.2, mapped to Windows conventions).</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_dialogs.IsOpen)
        {
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var typing = Keyboard.FocusedElement is System.Windows.Controls.TextBox or System.Windows.Controls.PasswordBox;

        switch (key)
        {
            case Key.D1 or Key.NumPad1 when ctrl && shift:
                ViewModel.SetLayout(LayoutMode.Grid);
                break;
            case Key.D2 or Key.NumPad2 when ctrl && shift:
                ViewModel.SetLayout(LayoutMode.List);
                break;
            case Key.D3 or Key.NumPad3 when ctrl && shift:
                ViewModel.SetLayout(LayoutMode.Compact);
                break;
            case Key.D1 or Key.NumPad1 when ctrl:
                ViewModel.SelectSection(LibrarySection.Live);
                break;
            case Key.D2 or Key.NumPad2 when ctrl:
                ViewModel.SelectSection(LibrarySection.Movies);
                break;
            case Key.D3 or Key.NumPad3 when ctrl:
                ViewModel.SelectSection(LibrarySection.Series);
                break;
            case Key.F when ctrl:
                Shell.FocusSearch();
                break;
            case Key.R when ctrl:
            case Key.F5:
                _ = ViewModel.RefreshAsync();
                break;
            case Key.N when ctrl:
                _ = ViewModel.AddPlaylistAsync();
                break;
            case Key.L when ctrl:
                ViewModel.LockAll();
                break;
            case Key.Left when alt:
            case Key.Back when !typing && ViewModel.CanGoBack:
                ViewModel.Back();
                break;
            case Key.Escape when !typing && ViewModel.CanGoBack:
                ViewModel.Back();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>The mouse's Back button leaves a detail page.</summary>
    private void OnMouseBack(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1 && ViewModel.CanGoBack)
        {
            ViewModel.Back();
            e.Handled = true;
        }
    }
}
