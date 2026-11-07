using Retune.Core.Models;
using Retune.Core.Playback;

namespace Retune.Presentation.Services;

/// <summary>Things only the UI layer can do, behind interfaces so view models stay testable.</summary>
public interface IDialogs
{
    /// <summary>A yes/no confirmation; <paramref name="destructive"/> styles the confirm button as dangerous.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive);

    /// <summary>Opens the playlist editor; null playlist means "add".</summary>
    Task ShowPlaylistEditorAsync(Playlist? playlist);

    /// <summary>Asks for the PIN; returns true when verified (the dialog handles wrong entries and lockout).</summary>
    Task<bool> VerifyPinAsync(string reason);

    /// <summary>Lets the user create a PIN; returns true when one was set.</summary>
    Task<bool> CreatePinAsync();

    void OpenInBrowser(Uri url);
}

/// <summary>Starts playback (implemented by the player in Phase 4).</summary>
public interface IPlayerLauncher
{
    void Play(Lineup lineup, bool fromResumePoint = true);

    /// <summary>Stops playback so the final progress is saved to the right playlist.</summary>
    void StopForPlaylistChange();

    /// <summary>The id of the item playing now, to mark it in lists.</summary>
    string? PlayingItemId { get; }

    event EventHandler? PlayingItemChanged;
}
