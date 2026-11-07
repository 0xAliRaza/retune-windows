using Retune.Core.Playback;
using Retune.Presentation.Services;

namespace Retune.App.Services;

/// <summary>
/// Bridges browse to the player. Phase 4 attaches the real player through <see cref="Attach"/>; until then
/// play requests are remembered so the UI can show what would play.
/// </summary>
public sealed class PlayerLauncher : IPlayerLauncher
{
    private IPlayerLauncher? _player;

    public event EventHandler? PlayingItemChanged;

    public string? PlayingItemId => _player?.PlayingItemId;

    public Lineup? LastRequest { get; private set; }

    public void Attach(IPlayerLauncher player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _player.PlayingItemChanged += (s, e) => PlayingItemChanged?.Invoke(this, e);
    }

    public void Play(Lineup lineup, bool fromResumePoint = true)
    {
        if (_player is null)
        {
            LastRequest = lineup; // the player isn't ready yet: it plays this once attached
            return;
        }

        LastRequest = null;
        _player.Play(lineup, fromResumePoint);
    }

    public void StopForPlaylistChange() => _player?.StopForPlaylistChange();
}
