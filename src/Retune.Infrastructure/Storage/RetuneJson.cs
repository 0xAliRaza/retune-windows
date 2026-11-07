using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Retune.Core.Models;
using Retune.Core.Playback;

namespace Retune.Infrastructure.Storage;

/// <summary>Source-generated serializers for every persisted type (fast, trimming- and AOT-safe).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(PlaylistsFile))]
[JsonSerializable(typeof(XtreamSource))]
[JsonSerializable(typeof(M3uSource))]
[JsonSerializable(typeof(CatalogFile))]
[JsonSerializable(typeof(LibraryFile))]
[JsonSerializable(typeof(WatchProgress))]
[JsonSerializable(typeof(SubtitleStyle))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class RetuneJsonContext : JsonSerializerContext;

/// <summary>
/// Options over <see cref="RetuneJsonContext"/> that keep the domain models attribute-free:
/// computed properties are not written, and playlist sources carry a <c>$type</c> discriminator.
/// </summary>
internal static class RetuneJson
{
    /// <summary>
    /// Copies the context's options (camelCase names, string enums, nullability checks): source-generated
    /// metadata takes its naming policy from the options instance, not from the attribute.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new(RetuneJsonContext.Default.Options)
    {
        TypeInfoResolver = RetuneJsonContext.Default.WithAddedModifier(StripComputedProperties).WithAddedModifier(TagPlaylistSources),
    };

    public static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    /// <summary>Drops get-only properties that no constructor parameter feeds (IsResumable, KindLabel, …).</summary>
    private static void StripComputedProperties(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        for (var i = info.Properties.Count - 1; i >= 0; i--)
        {
            var property = info.Properties[i];
            if (property.Set is null && property.AssociatedParameter is null)
            {
                info.Properties.RemoveAt(i);
            }
        }
    }

    private static void TagPlaylistSources(JsonTypeInfo info)
    {
        if (info.Type == typeof(PlaylistSource))
        {
            info.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type",
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
                DerivedTypes = { new JsonDerivedType(typeof(XtreamSource), "xtream"), new JsonDerivedType(typeof(M3uSource), "m3u") },
            };
        }
    }
}

/// <summary>playlists.json.</summary>
internal sealed record PlaylistsFile(int Version, IReadOnlyList<Playlist> Playlists)
{
    public const int CurrentVersion = 1;
}

/// <summary>Catalogs/&lt;uuid&gt;.json.</summary>
internal sealed record CatalogFile(int Version, Catalog Catalog)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// library.json. Progress entries stay raw so each can be decoded on its own: one bad entry is skipped,
/// while a type error anywhere else fails the whole file (it is then moved aside).
/// </summary>
internal sealed record LibraryFile(
    int Version = LibraryFile.CurrentVersion,
    IReadOnlyList<string>? Favorites = null,
    IReadOnlyDictionary<string, JsonElement>? Progress = null,
    IReadOnlyList<string>? RecentChannels = null,
    IReadOnlyList<string>? HiddenCategories = null,
    IReadOnlyList<string>? LockedCategories = null,
    IReadOnlyList<string>? PinnedCategories = null,
    string? PinHash = null)
{
    public const int CurrentVersion = 1;
}
