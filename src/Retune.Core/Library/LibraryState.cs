using System.Collections.Immutable;
using Retune.Core.Models;

namespace Retune.Core.Library;

/// <summary>
/// The single persisted library document for all playlists. Immutable, so a snapshot can be
/// saved on a background thread while the UI keeps changing the live state.
/// </summary>
public sealed record LibraryState
{
    public const int RecentChannelLimit = 30;
    public const int ProgressLimit = 2000;

    /// <summary>Durations above this (7 days) are treated as bogus and not stored.</summary>
    public const double MaxPlausibleDuration = 604_800;

    public static LibraryState Empty { get; } = new();

    public ImmutableHashSet<string> Favorites { get; init; } = [];

    public ImmutableDictionary<string, WatchProgress> Progress { get; init; } = ImmutableDictionary<string, WatchProgress>.Empty;

    /// <summary>Most recent first.</summary>
    public ImmutableList<string> RecentChannels { get; init; } = [];

    public ImmutableHashSet<string> HiddenCategories { get; init; } = [];

    public ImmutableHashSet<string> LockedCategories { get; init; } = [];

    /// <summary>In the order they were pinned.</summary>
    public ImmutableList<string> PinnedCategories { get; init; } = [];

    /// <summary>Parental PIN hash; null when no PIN is set.</summary>
    public string? PinHash { get; init; }
}
