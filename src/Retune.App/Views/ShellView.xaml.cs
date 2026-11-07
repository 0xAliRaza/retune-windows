using System.Windows;
using System.Windows.Controls;
using Retune.Core.Models;
using Retune.Presentation.ViewModels;

namespace Retune.App.Views;

public partial class ShellView : UserControl
{
    public ShellView()
    {
        InitializeComponent();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    public void FocusSearch() => SearchBox.Focus();

    /// <summary>The sort and tile-size menu, built from the section's options.</summary>
    private void OnSortClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SortButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = "Sort by", IsEnabled = false });
        foreach (var sort in Shell.SortOptions)
        {
            menu.Items.Add(new MenuItem { Header = sort.Title(), IsCheckable = true, IsChecked = Shell.Sort == sort, Command = Shell.SetSortCommand, CommandParameter = sort });
        }

        if (Shell.Content.Layout == LayoutMode.Grid)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "Tile size", IsEnabled = false });
            foreach (var size in Enum.GetValues<TileSize>())
            {
                menu.Items.Add(new MenuItem { Header = size.ToString(), IsCheckable = true, IsChecked = Shell.Content.TileSize == size, Command = Shell.SetTileSizeCommand, CommandParameter = size });
            }
        }

        menu.IsOpen = true;
    }
}
