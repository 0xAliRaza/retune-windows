using Retune.App.Composition;
using Retune.Core.Library;
using Retune.Core.Models;

namespace Retune.App.Dev;

/// <summary>Dev-only: favourites, recent channels and progress on the demo playlist, so Home has rows to show.</summary>
internal static class DemoHistory
{
    public static void Seed(AppContainer app, Guid playlistId)
    {
        var catalog = app.Playlists.Index?.Catalog ?? throw new InvalidOperationException("The demo catalog isn't loaded.");
        string Key(ContentKind kind, string id) => LibraryKeys.Content(playlistId, kind, id);
        var now = app.Time.GetUtcNow();
        app.Library.Update(state =>
        {
            foreach (var (movie, i) in catalog.Movies.Skip(20).Take(3).Select((m, i) => (m, i)))
            {
                var item = new PlaybackItem(movie.Id, ContentKind.Movie, movie.Name, null, $"http://127.0.0.1/movie/{movie.Id}.mp4", movie.Poster);
                state = state.RecordProgress(Key(ContentKind.Movie, movie.Id), item, 900 + (i * 1300), 6000, now.AddMinutes(-i));
            }

            foreach (var channel in catalog.Channels.Skip(4).Take(6).Reverse())
            {
                state = state.RecordRecentChannel(Key(ContentKind.Live, channel.Id));
            }

            foreach (var id in catalog.Channels.Take(5).Select(c => Key(ContentKind.Live, c.Id))
                .Concat(catalog.Movies.Skip(5).Take(9).Select(m => Key(ContentKind.Movie, m.Id)))
                .Concat(catalog.Series.Take(7).Select(s => Key(ContentKind.Series, s.Id))))
            {
                state = state.ToggleFavorite(id);
            }

            return state;
        });
    }
}