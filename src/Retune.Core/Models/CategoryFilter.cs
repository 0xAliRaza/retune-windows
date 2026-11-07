namespace Retune.Core.Models;

/// <summary>The selection in the category column.</summary>
public abstract record CategoryFilter
{
    public static CategoryFilter All { get; } = new AllFilter();

    public static CategoryFilter Favorites { get; } = new FavoritesFilter();

    public static CategoryFilter ContinueWatching { get; } = new ContinueWatchingFilter();

    public static CategoryFilter Recent { get; } = new RecentFilter();

    public static CategoryFilter Category(string id) => new CategoryIdFilter(id);
}

public sealed record AllFilter : CategoryFilter;

public sealed record FavoritesFilter : CategoryFilter;

public sealed record ContinueWatchingFilter : CategoryFilter;

public sealed record RecentFilter : CategoryFilter;

public sealed record CategoryIdFilter(string Id) : CategoryFilter;

/// <summary>Where loading the active playlist stands.</summary>
public abstract record LoadStatus
{
    public static LoadStatus Idle { get; } = new IdleStatus();

    public static LoadStatus Ready { get; } = new ReadyStatus();

    public static LoadStatus Loading(string message) => new LoadingStatus(message);

    public static LoadStatus Failed(string message) => new FailedStatus(message);
}

public sealed record IdleStatus : LoadStatus;

public sealed record ReadyStatus : LoadStatus;

public sealed record LoadingStatus(string Message) : LoadStatus;

public sealed record FailedStatus(string Message) : LoadStatus;
