using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Retune.Presentation.ViewModels;

namespace Retune.App.Views;

/// <summary>The Home page: rows of Continue Watching, recent channels and favourites.</summary>
public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    private void OnSeeAll(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell && sender is FrameworkElement { DataContext: HomeRow row })
        {
            shell.SeeAll(row);
        }
    }

    /// <summary>
    /// A row scrolls sideways with Shift + wheel; the plain wheel scrolls the page (a horizontal-only scroller would
    /// otherwise swallow it and the page would stop scrolling under the pointer).
    /// </summary>
    private void OnRowWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer row)
        {
            return;
        }

        e.Handled = true;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            row.ScrollToHorizontalOffset(row.HorizontalOffset - e.Delta);
        }
        else
        {
            Page.ScrollToVerticalOffset(Page.VerticalOffset - e.Delta);
        }
    }
}
