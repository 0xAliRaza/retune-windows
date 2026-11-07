using System.Windows;
using System.Windows.Controls;
using Retune.Presentation.ViewModels;

namespace Retune.App.Controls;

/// <summary>Picks the grid tile look from the item's artwork style.</summary>
public sealed class TileTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Poster { get; set; }

    public DataTemplate? Logo { get; set; }

    public DataTemplate? Landscape { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => (item as BrowseItemViewModel)?.Style switch
    {
        ArtworkStyle.Logo => Logo,
        ArtworkStyle.Landscape => Landscape,
        _ => Poster,
    };
}
