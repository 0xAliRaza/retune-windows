using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Retune.Core.Models;
using Retune.Core.Navigation;
using Retune.Presentation.ViewModels;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;

namespace Retune.App.Views;

/// <summary>
/// Shows whichever content state applies and keeps the grid's column count in step with the width.
/// Arrow keys move between tiles using Core's grid rules.
/// </summary>
public partial class ContentView : UserControl
{
    private ShellViewModel? _shell;

    public ContentView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as ShellViewModel);
    }

    private void Attach(ShellViewModel? shell)
    {
        if (_shell is not null)
        {
            _shell.Content.PropertyChanged -= OnContentChanged;
        }

        _shell = shell;
        if (shell is not null)
        {
            shell.Content.PropertyChanged += OnContentChanged;
            UpdateState();
        }
    }

    private void OnContentChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ContentViewModel.State) or nameof(ContentViewModel.Layout) or nameof(ContentViewModel.Items):
                UpdateState();
                break;
            case nameof(ContentViewModel.MinTileWidth):
                _shell?.Content.SetAvailableWidth(Body.ActualWidth);
                break;
            default:
                break;
        }
    }

    private void UpdateState()
    {
        if (_shell is null)
        {
            return;
        }

        var content = _shell.Content;
        var showItems = content.State == ContentState.Items;
        GridItems.Visibility = showItems && content.Layout == LayoutMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        ListItems.Visibility = showItems && content.Layout != LayoutMode.Grid ? Visibility.Visible : Visibility.Collapsed;
        LockedPanel.Visibility = content.State == ContentState.Locked ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = content.State is ContentState.Loading or ContentState.Failed or ContentState.Empty or ContentState.NoPlaylist ? Visibility.Visible : Visibility.Collapsed;
        Spinner.Visibility = content.State == ContentState.Loading ? Visibility.Visible : Visibility.Collapsed;
        StatusIcon.Visibility = content.State == ContentState.Loading ? Visibility.Collapsed : Visibility.Visible;
        FailedActions.Visibility = content.State == ContentState.Failed ? Visibility.Visible : Visibility.Collapsed;
        StatusIcon.Symbol = content.State switch
        {
            ContentState.Failed => SymbolRegular.Warning24,
            ContentState.Empty when content.StatusTitle.StartsWith("No results", StringComparison.Ordinal) => SymbolRegular.Search24,
            ContentState.Empty when content.StatusTitle.StartsWith("No favorites", StringComparison.Ordinal) => SymbolRegular.Star24,
            ContentState.Empty when content.StatusTitle.StartsWith("Nothing in progress", StringComparison.Ordinal) => SymbolRegular.PlayCircle24,
            _ => SymbolRegular.Tv24,
        };

        if (content.State == ContentState.Locked)
        {
            PinBox.Password = string.Empty;
        }

        content.SetAvailableWidth(Body.ActualWidth);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => _shell?.Content.SetAvailableWidth(e.NewSize.Width);

    private void OnPinKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnUnlock(sender, e);
        }
    }

    private void OnUnlock(object sender, RoutedEventArgs e)
    {
        if (_shell is null || PinBox.Password.Length == 0)
        {
            return;
        }

        if (_shell.TryUnlock(PinBox.Password))
        {
            return;
        }

        PinBox.Password = string.Empty;
        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.3) };
        foreach (var (offset, i) in new[] { 10.0, -8, 6, -4, 0 }.Select((o, i) => (o, i)))
        {
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(offset, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.06 * (i + 1)))));
        }

        Shake.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    /// <summary>Arrow keys move between tiles (Core's GridNavigation); Enter opens via the button itself.</summary>
    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (_shell is null || Keyboard.FocusedElement is not Button { DataContext: BrowseItemViewModel focused })
        {
            return;
        }

        var direction = e.Key switch
        {
            Key.Left => Direction.Left,
            Key.Right => Direction.Right,
            Key.Up => Direction.Up,
            Key.Down => Direction.Down,
            _ => (Direction?)null,
        };
        if (direction is not { } d)
        {
            return;
        }

        var items = _shell.Content.Items;
        var columns = _shell.Content.Columns;
        var index = IndexOf(items, focused);
        if (GridNavigation.IsAtEdge(index, d, columns) && d is Direction.Left or Direction.Up)
        {
            return; // let the pane router move focus out of the grid
        }

        if (GridNavigation.Move(index, d, items.Count, columns) is { } target && target != index)
        {
            FocusTile(target, columns);
            e.Handled = true;
        }
    }

    private void FocusTile(int index, int columns)
    {
        var row = index / columns;
        if (GetPanel(GridItems) is VirtualizingStackPanel panel)
        {
            panel.BringIndexIntoViewPublic(row);
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (GridItems.ItemContainerGenerator.ContainerFromIndex(row) is FrameworkElement rowContainer)
            {
                var tile = FindDescendants<Button>(rowContainer).ElementAtOrDefault(index % columns);
                tile?.Focus();
            }
        });
    }

    private static int IndexOf(LazyList<BrowseItemViewModel> items, BrowseItemViewModel item)
    {
        foreach (var realized in items.Realized)
        {
            if (ReferenceEquals(realized, item))
            {
                return items.IndexOf(item);
            }
        }

        return -1;
    }

    private static VirtualizingStackPanel? GetPanel(ItemsControl control) => FindDescendants<VirtualizingStackPanel>(control).FirstOrDefault();

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
