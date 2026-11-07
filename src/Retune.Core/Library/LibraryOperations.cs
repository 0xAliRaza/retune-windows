using System.Collections.Immutable;
using Retune.Core.Models;
using Retune.Core.Xtream;

namespace Retune.Core.Library;

/// <summary>A resume point with the key it is stored under.</summary>
public sealed record KeyedProgress(string Key, WatchProgress Progress);

/// <summary>
/// Every library rule as a pure function: each returns a new <see cref="LibraryState"/>.
/// Spec: docs/spec/core.md §5.
/// </summary>
public static class LibraryOperations
{
    // ---- favourites ----------------------------------------------------------------------------

    public static bool IsFavorite(this LibraryState state, string key) => state.Favorites.Contains(key);

    public static LibraryState ToggleFavorite(this LibraryState state, string key) =>
        state with { Favorites = Toggle(state.Favorites, key) };

    /// <summary>The raw ids favourited for one kind in one playlist.</summary>
    public static IReadOnlySet<string> FavoriteIds(this LibraryState state, Guid playlistId, ContentKind kind) =>
        IdsWithPrefix(state.Favorites, $"{LibraryKeys.PlaylistPrefix(playlistId)}{kind.ToKey()}/").ToHashSet(StringComparer.Ordinal);

    // ---- recent channels -----------------------------------------------------------------------

    /// <summary>Moves the key to the front and keeps the 30 most recent (one cap shared by all playlists).</summary>
    public static LibraryState RecordRecentChannel(this LibraryState state, string key)
    {
        var list = state.RecentChannels.Remove(key).Insert(0, key);
        return state with { RecentChannels = list.Count > LibraryState.RecentChannelLimit ? list.GetRange(0, LibraryState.RecentChannelLimit) : list };
    }

    public static LibraryState RemoveRecentChannel(this LibraryState state, string key) =>
        state with { RecentChannels = state.RecentChannels.Remove(key) };

    public static LibraryState ClearRecentChannels(this LibraryState state, Guid playlistId)
    {
        var prefix = LibraryKeys.PlaylistPrefix(playlistId);
        return state with { RecentChannels = state.RecentChannels.RemoveAll(k => k.StartsWith(prefix, StringComparison.Ordinal)) };
    }

    /// <summary>Recent channel ids of a playlist, most recent first.</summary>
    public static IReadOnlyList<string> RecentChannelIds(this LibraryState state, Guid playlistId) =>
        IdsWithPrefix(state.RecentChannels, $"{LibraryKeys.PlaylistPrefix(playlistId)}{ContentKind.Live.ToKey()}/").ToList();

    // ---- progress ------------------------------------------------------------------------------

    public static WatchProgress? ProgressFor(this LibraryState state, string key) => state.Progress.GetValueOrDefault(key);

    public static bool IsWatched(this LibraryState state, string key) => state.ProgressFor(key)?.IsFinished == true;

    /// <summary>
    /// Saves a resume point. Rejected (state unchanged) unless the duration is positive, finite and under
    /// 7 days and the position is finite. Pruned to 2000 entries, dropping unfinished ones before watched marks.
    /// The item must already be credential-free.
    /// </summary>
    public static LibraryState RecordProgress(this LibraryState state, string key, PlaybackItem item, double position, double duration, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!double.IsFinite(position) || !double.IsFinite(duration) || duration <= 0 || duration >= LibraryState.MaxPlausibleDuration)
        {
            return state;
        }

        return Prune(state with { Progress = state.Progress.SetItem(key, new WatchProgress(item, position, duration, at)) });
    }

    /// <summary>Stores the item as finished (position = duration = max(duration ?? 1, 1)).</summary>
    public static LibraryState MarkWatched(this LibraryState state, string key, PlaybackItem item, double? duration, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(item);
        var length = Math.Max(duration is { } d && double.IsFinite(d) ? d : 1, 1);
        return Prune(state with { Progress = state.Progress.SetItem(key, new WatchProgress(item, length, length, at)) });
    }

    /// <summary>Removes the entry completely, resume point included.</summary>
    public static LibraryState MarkUnwatched(this LibraryState state, string key) =>
        state with { Progress = state.Progress.Remove(key) };

    // ---- continue watching ---------------------------------------------------------------------

    /// <summary>Resumable entries of a playlist, newest first, with episodes collapsed to one per series.</summary>
    public static IReadOnlyList<KeyedProgress> ContinueWatching(this LibraryState state, Guid playlistId)
    {
        var prefix = LibraryKeys.PlaylistPrefix(playlistId);
        var seenSeries = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<KeyedProgress>();
        foreach (var (key, progress) in state.Progress
                     .Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal) && p.Value.IsResumable)
                     .OrderByDescending(p => p.Value.UpdatedAt))
        {
            if (progress.Item is { Kind: ContentKind.Episode, ParentId: { } series } && !seenSeries.Add(series))
            {
                continue;
            }

            result.Add(new KeyedProgress(key, progress));
        }

        return result;
    }

    /// <summary>
    /// Removes a Continue Watching row. For an episode, every resumable episode of that series goes too
    /// (otherwise the row falls back to an older one); watched marks are kept.
    /// </summary>
    public static LibraryState RemoveFromContinueWatching(this LibraryState state, string key)
    {
        if (state.ProgressFor(key) is not { Item: { Kind: ContentKind.Episode, ParentId: { } series } })
        {
            return state.MarkUnwatched(key);
        }

        var playlistPrefix = key[..(key.IndexOf('/', StringComparison.Ordinal) + 1)];
        var doomed = state.Progress
            .Where(p => p.Key.StartsWith(playlistPrefix, StringComparison.Ordinal)
                && p.Value.Item.Kind == ContentKind.Episode
                && p.Value.Item.ParentId == series
                && p.Value.IsResumable)
            .Select(p => p.Key);
        return state with { Progress = state.Progress.RemoveRange(doomed) };
    }

    /// <summary>Removes the playlist's resumable entries; watched marks and barely-started ones stay.</summary>
    public static LibraryState ClearContinueWatching(this LibraryState state, Guid playlistId)
    {
        var prefix = LibraryKeys.PlaylistPrefix(playlistId);
        var doomed = state.Progress.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal) && p.Value.IsResumable).Select(p => p.Key);
        return state with { Progress = state.Progress.RemoveRange(doomed) };
    }

    // ---- categories ----------------------------------------------------------------------------

    public static LibraryState ToggleHidden(this LibraryState state, string categoryKey) =>
        state with { HiddenCategories = Toggle(state.HiddenCategories, categoryKey) };

    /// <summary>Pinning appends to the pin order; unpinning removes.</summary>
    public static LibraryState TogglePinned(this LibraryState state, string categoryKey) =>
        state with
        {
            PinnedCategories = state.PinnedCategories.Contains(categoryKey)
                ? state.PinnedCategories.Remove(categoryKey)
                : state.PinnedCategories.Add(categoryKey),
        };

    public static LibraryState SetLocked(this LibraryState state, string categoryKey, bool locked) =>
        state with { LockedCategories = locked ? state.LockedCategories.Add(categoryKey) : state.LockedCategories.Remove(categoryKey) };

    // ---- PIN -----------------------------------------------------------------------------------

    public static LibraryState WithPinHash(this LibraryState state, string hash) => state with { PinHash = hash };

    /// <summary>Removing the PIN also removes every category lock, in every playlist.</summary>
    public static LibraryState RemovePin(this LibraryState state) => state with { PinHash = null, LockedCategories = [] };

    // ---- playlist lifecycle and maintenance ----------------------------------------------------

    /// <summary>Forgets everything stored for one playlist. The PIN is global and stays.</summary>
    public static LibraryState RemoveAll(this LibraryState state, Guid playlistId)
    {
        var prefix = LibraryKeys.PlaylistPrefix(playlistId);
        bool Mine(string key) => key.StartsWith(prefix, StringComparison.Ordinal);
        return state with
        {
            Favorites = state.Favorites.Except(state.Favorites.Where(Mine)),
            Progress = state.Progress.RemoveRange(state.Progress.Keys.Where(Mine)),
            RecentChannels = state.RecentChannels.RemoveAll(Mine),
            HiddenCategories = state.HiddenCategories.Except(state.HiddenCategories.Where(Mine)),
            LockedCategories = state.LockedCategories.Except(state.LockedCategories.Where(Mine)),
            PinnedCategories = state.PinnedCategories.RemoveAll(Mine),
        };
    }

    /// <summary>
    /// Rewrites any stored raw Xtream stream URL as a credential-free locator. Idempotent.
    /// Returns whether anything changed (the caller saves at once if so).
    /// </summary>
    public static (LibraryState State, bool Changed) SanitizeStreamUrls(this LibraryState state)
    {
        var builder = state.Progress.ToBuilder();
        var changed = false;
        foreach (var (key, progress) in state.Progress)
        {
            if (!Locators.IsLocator(progress.Item.Url) && Locators.FromXtreamStream(progress.Item.Url) is { } locator)
            {
                builder[key] = progress with { Item = progress.Item with { Url = locator } };
                changed = true;
            }
        }

        return changed ? (state with { Progress = builder.ToImmutable() }, true) : (state, false);
    }

    private static LibraryState Prune(LibraryState state)
    {
        var excess = state.Progress.Count - LibraryState.ProgressLimit;
        if (excess <= 0)
        {
            return state;
        }

        var doomed = state.Progress
            .OrderBy(p => p.Value.IsFinished)
            .ThenBy(p => p.Value.UpdatedAt)
            .Take(excess)
            .Select(p => p.Key);
        return state with { Progress = state.Progress.RemoveRange(doomed) };
    }

    /// <summary>Immutable Remove returns the same instance when the key was absent.</summary>
    private static ImmutableHashSet<string> Toggle(ImmutableHashSet<string> set, string key)
    {
        var without = set.Remove(key);
        return ReferenceEquals(without, set) ? set.Add(key) : without;
    }

    private static IEnumerable<string> IdsWithPrefix(IEnumerable<string> keys, string prefix) =>
        keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => k[prefix.Length..]);
}

/// <summary>
/// Limits how often in-progress reports reach the library: at most once per 30 s per key.
/// Final reports (stop, switch, end of file) always go through. In-memory only.
/// </summary>
public sealed class ProgressThrottle(TimeProvider time)
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, DateTimeOffset> _lastCommit = new(StringComparer.Ordinal);

    public bool ShouldCommit(string key, bool isFinal)
    {
        var now = time.GetUtcNow();
        if (!isFinal && _lastCommit.TryGetValue(key, out var last) && now - last < MinInterval)
        {
            return false;
        }

        _lastCommit[key] = now;
        return true;
    }
}
