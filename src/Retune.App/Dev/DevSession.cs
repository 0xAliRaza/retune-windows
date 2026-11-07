using Retune.App.Composition;
using Retune.Core.Models;

namespace Retune.App.Dev;

/// <summary>Dev-only: <c>--demo &lt;url&gt;</c> adds the fake provider's account; <c>--autoplay</c> starts the first channel.</summary>
internal static class DevSession
{
    public static async Task RunAsync(AppContainer app, DevOptions options)
    {
        if (options.DemoProvider is not { } provider)
        {
            return;
        }

        if (app.Playlists.Playlists.Count == 0)
        {
            await app.Playlists.AddAsync(new Playlist(Guid.NewGuid(), "Demo TV", new XtreamSource(provider, "demo"), DateTimeOffset.UtcNow), "demo").ConfigureAwait(true);
        }

        if (options.AutoPlay && app.Shell.Content.Items.Count > 0)
        {
            app.Shell.Open(app.Shell.Content.Items[0]);
        }
    }
}
