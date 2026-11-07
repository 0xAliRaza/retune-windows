namespace Retune.Core.Navigation;

/// <summary>One row of the Keyboard Shortcuts sheet: the key caps (alternatives) and what they do.</summary>
public sealed record Shortcut(IReadOnlyList<string> Keys, string Action);

/// <summary>A titled column of the sheet.</summary>
public sealed record ShortcutGroup(string Title, IReadOnlyList<Shortcut> Shortcuts);

/// <summary>
/// The single source of truth for the Keyboard Shortcuts sheet (spec ui-player §2.9, §3.1, §3.2 and PLAN decision 7).
/// The key handlers in the main window implement exactly these rows.
/// </summary>
public static class ShortcutCatalog
{
    public static ShortcutGroup Player { get; } = new("Player",
    [
        new(["Space"], "Play / pause"),
        new(["←", "→"], "Back / forward 10 seconds"),
        new(["Shift+←", "Shift+→"], "Back / forward 1 minute"),
        new(["↑", "↓"], "Previous / next channel or episode"),
        new(["N", "Shift+N"], "Next / previous"),
        new(["0–9"], "Tune to a channel number"),
        new(["Backspace"], "Last channel"),
        new(["L"], "Channel list"),
        new(["[", "]", "\\"], "Slower / faster / normal speed"),
        new(["−", "+"], "Volume down / up"),
        new(["M"], "Mute"),
        new(["C"], "Subtitles on / off"),
        new(["F", "F11"], "Full screen"),
        new(["I"], "Stream info"),
        new(["S"], "Save a screenshot"),
        new(["Esc"], "Close panel / leave the player"),
    ]);

    public static ShortcutGroup App { get; } = new("App",
    [
        new(["Ctrl+1", "Ctrl+2", "Ctrl+3"], "Live TV / Movies / Series"),
        new(["Ctrl+Shift+1", "Ctrl+Shift+2", "Ctrl+Shift+3"], "Grid / List / Compact"),
        new(["Tab", "Shift+Tab"], "Next / previous column"),
        new(["Enter"], "Open or play"),
        new(["Alt+←", "Backspace"], "Back"),
        new(["Ctrl+F"], "Search"),
        new(["Ctrl+R", "F5"], "Refresh playlist"),
        new(["Ctrl+N"], "Add playlist"),
        new(["Ctrl+L"], "Lock categories again"),
        new(["Ctrl+,"], "Settings"),
        new(["F11"], "Full screen"),
        new(["F1", "Ctrl+/", "?"], "Keyboard shortcuts"),
    ]);

    public static IReadOnlyList<ShortcutGroup> All { get; } = [Player, App];
}
