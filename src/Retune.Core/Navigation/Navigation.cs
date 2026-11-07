namespace Retune.Core.Navigation;

public enum Direction
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>Arrow-key movement in a wrapped grid of tiles (a list is a grid with one column).</summary>
public static class GridNavigation
{
    /// <summary>How many columns of at least <paramref name="minimum"/> fit, with <paramref name="spacing"/> between them.</summary>
    public static int Columns(double width, double minimum, double spacing) =>
        minimum <= 0 ? 1 : Math.Max(1, (int)Math.Floor((width + spacing) / (minimum + spacing)));

    /// <summary>The index focus moves to, or null when the collection is empty.</summary>
    public static int? Move(int? current, Direction direction, int count, int columns)
    {
        if (count <= 0)
        {
            return null;
        }

        if (current is not { } from)
        {
            return 0;
        }

        columns = Math.Max(1, columns);
        from = Math.Clamp(from, 0, count - 1);
        var target = direction switch
        {
            Direction.Left => from - 1,
            Direction.Right => from + 1,
            Direction.Up => from - columns,
            _ => from + columns,
        };

        if (target < 0)
        {
            return direction == Direction.Up ? from : 0;
        }

        if (target >= count)
        {
            var inLastRow = from / columns == (count - 1) / columns;
            return direction == Direction.Down && inLastRow ? from : count - 1;
        }

        return target;
    }

    /// <summary>True when moving that way should leave the grid (to the search box or the category column).</summary>
    public static bool IsAtEdge(int index, Direction direction, int columns) => direction switch
    {
        Direction.Up => index < Math.Max(1, columns),
        Direction.Left => index % Math.Max(1, columns) == 0,
        _ => false,
    };
}

public enum Pane
{
    Sidebar,
    Categories,
    Content,
}

public enum PaneKey
{
    Tab,
    ShiftTab,
    Right,
    Left,
    Enter,
    Escape,
}

/// <summary>What a key press means for focus between the three browse columns.</summary>
public abstract record PaneAction
{
    public static PaneAction PassThrough { get; } = new PassThroughAction();

    public static PaneAction Back { get; } = new BackAction();

    public static PaneAction Focus(Pane pane) => new FocusAction(pane);
}

public sealed record PassThroughAction : PaneAction;

public sealed record BackAction : PaneAction;

public sealed record FocusAction(Pane Pane) : PaneAction;

/// <summary>Sidebar → categories → content, as a pure rule table.</summary>
public static class PaneNavigation
{
    /// <param name="detailShown">A movie or series page is open in the content column.</param>
    /// <param name="contentUsesLeft">The content handles ← itself (a grid or a detail page).</param>
    public static PaneAction Action(Pane pane, PaneKey key, bool detailShown, bool contentUsesLeft) => (pane, key) switch
    {
        (Pane.Sidebar, PaneKey.Tab or PaneKey.Right or PaneKey.Enter) => PaneAction.Focus(Pane.Categories),
        (Pane.Sidebar, PaneKey.ShiftTab) => PaneAction.Focus(Pane.Content),

        (Pane.Categories, PaneKey.Tab or PaneKey.Right or PaneKey.Enter) => PaneAction.Focus(Pane.Content),
        (Pane.Categories, PaneKey.ShiftTab or PaneKey.Left or PaneKey.Escape) => PaneAction.Focus(Pane.Sidebar),

        (Pane.Content, PaneKey.Escape) => detailShown ? PaneAction.Back : PaneAction.Focus(Pane.Categories),
        (Pane.Content, PaneKey.Tab) => PaneAction.Focus(Pane.Sidebar),
        (Pane.Content, PaneKey.ShiftTab) => PaneAction.Focus(Pane.Categories),
        (Pane.Content, PaneKey.Left) => contentUsesLeft ? PaneAction.PassThrough : PaneAction.Focus(Pane.Categories),

        _ => PaneAction.PassThrough,
    };
}
