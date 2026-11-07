using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Retune.Presentation.ViewModels;

namespace Retune.App.Dialogs;

/// <summary>Esc cancels, the default button confirms: shared by every dialog.</summary>
public abstract class DialogBase : UserControl
{
    protected DialogBase(Action<bool> close)
    {
        Close = close;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(false);
            }
        };
    }

    protected Action<bool> Close { get; }

    protected void OnCancel(object sender, RoutedEventArgs e) => Close(false);
}

public partial class ConfirmDialog : DialogBase
{
    public ConfirmDialog(string title, string message, string confirmLabel, bool destructive, Action<bool> close)
        : base(close)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
        ConfirmButton.Appearance = destructive ? Wpf.Ui.Controls.ControlAppearance.Danger : Wpf.Ui.Controls.ControlAppearance.Primary;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => Close(true);
}

/// <summary>Keyboard Shortcuts (F1, Ctrl+/, ?). Done or Esc closes it.</summary>
public partial class ShortcutsDialog : DialogBase
{
    public ShortcutsDialog(Action<bool> close)
        : base(close) => InitializeComponent();
}

public partial class PlaylistEditorDialog : DialogBase
{
    public PlaylistEditorDialog(PlaylistEditorViewModel editor, Action<bool> close)
        : base(close)
    {
        InitializeComponent();
        DataContext = editor;
        PasswordInput.PasswordChanged += (_, _) => editor.Password = PasswordInput.Password;
        if (!string.IsNullOrEmpty(editor.Password))
        {
            PasswordInput.Password = editor.Password;
        }

        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlaylistEditorViewModel.Password) && PasswordInput.Password != editor.Password)
            {
                PasswordInput.Password = editor.Password;
            }
        };
    }

    private PlaylistEditorViewModel Editor => (PlaylistEditorViewModel)DataContext;

    private void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Playlists (*.m3u;*.m3u8;*.txt)|*.m3u;*.m3u8;*.txt|All files (*.*)|*.*", Title = "Choose a playlist file" };
        if (picker.ShowDialog(Window.GetWindow(this)) == true)
        {
            Editor.FilePath = picker.FileName;
        }
    }

    private void OnKind(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PlaylistKind kind })
        {
            Editor.Kind = kind;
        }
    }
}

public partial class PinDialog : DialogBase
{
    private readonly Func<bool> _submit;

    public PinDialog(CreatePinViewModel create, Action<bool> close)
        : base(close)
    {
        InitializeComponent();
        DataContext = create;
        TitleText.Text = "Create a PIN";
        MessageText.Text = "Locked categories need this 4–8 digit PIN to open. Keep it somewhere safe.";
        PinInput.PasswordChanged += (_, _) => create.Pin = PinInput.Password;
        ConfirmInput.PasswordChanged += (_, _) => create.Confirm = ConfirmInput.Password;
        _submit = create.Submit;
    }

    public PinDialog(VerifyPinViewModel verify, Action<bool> close)
        : base(close)
    {
        InitializeComponent();
        DataContext = verify;
        TitleText.Text = "Enter PIN";
        MessageText.Text = verify.Reason;
        ConfirmInput.Visibility = Visibility.Collapsed;
        PinInput.PasswordChanged += (_, _) => verify.Pin = PinInput.Password;
        _submit = () =>
        {
            var ok = verify.Submit();
            if (!ok)
            {
                PinInput.Password = string.Empty;
            }

            return ok;
        };
    }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        if (_submit())
        {
            Close(true);
        }
    }
}
