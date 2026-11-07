using Retune.Core.Errors;
using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Presentation.Tests.Support;
using Retune.Presentation.ViewModels;

namespace Retune.Presentation.Tests;

public sealed class ShellTests
{
    [Fact]
    public async Task Adding_an_xtream_playlist_verifies_stores_the_secret_and_loads_the_catalog()
    {
        await using var app = await AppHarness.StartAsync();

        var playlist = await app.AddDemoAsync();

        Assert.Equal("demo", app.Secrets.Saved[playlist.Id]);
        Assert.Equal([playlist], app.PlaylistStore.Saved);
        Assert.Equal(playlist.Id, app.Settings.Saved.ActivePlaylist);
        Assert.IsType<ReadyStatus>(app.Controller.Status);
        Assert.True(app.Controller.Account?.IsActive);
        Assert.True(app.Catalogs.Saved.ContainsKey(playlist.Id));
        Assert.Equal(ContentState.Items, app.Shell.Content.State);
        Assert.Equal(120, app.Shell.Content.Items.Count);
        Assert.Equal("All Live TV", app.Shell.Content.Title);
        Assert.Equal([120, 400, 80], app.Shell.Sections.Select(s => s.Count));
    }

    [Fact]
    public async Task A_rejected_login_saves_nothing()
    {
        await using var app = await AppHarness.StartAsync();

        var ex = await Assert.ThrowsAsync<RetuneException>(() => app.Controller.AddAsync(app.XtreamPlaylist(), "wrong"));

        Assert.Equal(ProblemKind.InvalidCredentials, ex.Kind);
        Assert.Empty(app.PlaylistStore.Saved);
        Assert.Empty(app.Secrets.Saved);
    }

    [Fact]
    public async Task A_cached_catalog_shows_at_once_without_downloading_again()
    {
        await using var app = await AppHarness.StartAsync();
        var playlist = await app.AddDemoAsync();
        var requests = app.Provider.RequestCount;

        await using var reopened = await AppHarness.StartAsync();
        reopened.PlaylistStore.Save([playlist with { Source = new XtreamSource(reopened.Provider.BaseUrl, "demo") }]);
        reopened.Secrets.Store(playlist.Id, "demo");
        await reopened.Catalogs.SaveAsync(playlist.Id, app.Catalogs.Saved[playlist.Id]);

        await reopened.Controller.InitializeAsync();

        Assert.IsType<ReadyStatus>(reopened.Controller.Status);
        Assert.Equal(120, reopened.Shell.Content.Items.Count);
        Assert.Equal(1, reopened.Provider.RequestCount); // only the account check
        Assert.True(requests > 1);
    }

    [Fact]
    public async Task An_xtream_playlist_without_its_password_shows_the_error_not_the_cache()
    {
        await using var app = await AppHarness.StartAsync();
        var playlist = app.XtreamPlaylist();
        app.PlaylistStore.Save([playlist]);

        await app.Controller.InitializeAsync();

        Assert.IsType<FailedStatus>(app.Controller.Status);
        Assert.Equal(ContentState.Failed, app.Shell.Content.State);
    }

    [Fact]
    public async Task Deleting_the_active_playlist_stops_playback_and_forgets_everything()
    {
        await using var app = await AppHarness.StartAsync();
        var playlist = await app.AddDemoAsync();
        app.Shell.ToggleFavorite(app.Shell.Content.Items[0]);

        await app.Shell.DeletePlaylistAsync(playlist);

        Assert.Equal(["Delete this playlist?"], app.Dialogs.Confirmations);
        Assert.Equal(1, app.Player.Stops);
        Assert.Empty(app.PlaylistStore.Saved);
        Assert.Empty(app.Secrets.Saved);
        Assert.False(app.Catalogs.Saved.ContainsKey(playlist.Id));
        Assert.Empty(app.LibraryStore.Saved.Favorites);
        Assert.False(app.Shell.HasPlaylists);
    }

    [Fact]
    public async Task Declining_the_delete_confirmation_keeps_the_playlist()
    {
        await using var app = await AppHarness.StartAsync();
        var playlist = await app.AddDemoAsync();
        app.Dialogs.ConfirmAnswer = false;

        await app.Shell.DeletePlaylistAsync(playlist);

        Assert.Single(app.PlaylistStore.Saved);
    }

    [Fact]
    public async Task Search_is_debounced_and_spans_every_category()
    {
        await using var app = await AppHarness.StartAsync();
        await app.AddDemoAsync();
        app.Shell.SelectCategory(app.Shell.Categories.Categories[0]);

        app.Shell.SearchText = "nova";
        Assert.NotEqual("Results for “nova”", app.Shell.Content.Title);
        app.Time.Advance(TimeSpan.FromMilliseconds(200));

        Assert.Equal("Results for “nova”", app.Shell.Content.Title);
        Assert.All(app.Shell.Content.Items, i => Assert.Contains("Nova", i.Title, StringComparison.Ordinal));
        Assert.True(app.Shell.Content.Items.Count > 1);
    }

    [Fact]
    public async Task Switching_section_resets_search_and_sort_and_picks_the_default_filter()
    {
        await using var app = await AppHarness.StartAsync();
        await app.AddDemoAsync();
        app.Shell.SearchText = "x";
        app.Time.Advance(TimeSpan.FromSeconds(1));
        app.Shell.SetSort(BrowseSort.Title);

        app.Shell.SelectSection(LibrarySection.Movies);

        Assert.Equal(string.Empty, app.Shell.SearchText);
        Assert.Equal(BrowseSort.Provider, app.Shell.Sort);
        Assert.IsType<CategoryIdFilter>(app.Shell.Filter);
        Assert.Equal("Action", app.Shell.Content.Title);
        Assert.Equal(4, app.Shell.SortOptions.Count);
    }

    [Fact]
    public async Task Toggling_a_favourite_refreshes_tiles_in_place_but_rebuilds_the_favourites_view()
    {
        await using var app = await AppHarness.StartAsync();
        await app.AddDemoAsync();
        var items = app.Shell.Content.Items;
        var first = items[0];

        app.Shell.ToggleFavorite(first);

        Assert.Same(items, app.Shell.Content.Items);
        Assert.True(first.IsFavorite);

        app.Shell.SelectCategory(app.Shell.Categories.Fixed.Single(r => r.Kind == CategoryRowKind.Favorites));
        Assert.Equal([first.Id], app.Shell.Content.Items.Select(i => i.Id));

        app.Shell.ToggleFavorite(app.Shell.Content.Items[0]);
        Assert.Equal(ContentState.Empty, app.Shell.Content.State);
        Assert.Equal("No favorites yet", app.Shell.Content.StatusTitle);
    }

    [Fact]
    public async Task Library_changes_are_saved_after_one_quiet_second()
    {
        await using var app = await AppHarness.StartAsync();
        await app.AddDemoAsync();
        var before = app.LibraryStore.SaveCount;

        app.Shell.ToggleFavorite(app.Shell.Content.Items[0]);
        app.Shell.ToggleFavorite(app.Shell.Content.Items[1]);
        Assert.Equal(before, app.LibraryStore.SaveCount);

        app.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(before + 1, app.LibraryStore.SaveCount);
        Assert.Equal(2, app.LibraryStore.Saved.Favorites.Count);
    }

    [Fact]
    public async Task Locked_categories_ask_for_the_pin_and_stay_hidden_elsewhere()
    {
        await using var app = await AppHarness.StartAsync();
        await app.AddDemoAsync();
        app.Library.Update(s => s.WithPinHash(Core.Parental.PinHash.Create("1234")));
        var news = app.Shell.Categories.Categories[0];
        var total = app.Shell.Content.Items.Count;

        await app.Shell.LockAsync(news);
        Assert.True(app.Shell.Content.Items.Count < total);
        Assert.Null(app.Shell.Categories.Categories[0].CountText);

        app.Shell.SelectCategory(app.Shell.Categories.Categories[0]);
        Assert.Equal(ContentState.Locked, app.Shell.Content.State);
        Assert.False(app.Shell.TryUnlock("0000"));
        Assert.True(app.Shell.TryUnlock("1234"));
        Assert.Equal(ContentState.Items, app.Shell.Content.State);
        Assert.True(app.Shell.HasUnlocks);

        app.Shell.LockAll();
        Assert.Equal(ContentState.Locked, app.Shell.Content.State);
    }

    [Fact]
    public async Task Hiding_the_selected_category_falls_back_to_all()
    {
        await using var app = await AppHarness.StartAsync();
        await app.AddDemoAsync();
        var news = app.Shell.Categories.Categories[0];
        app.Shell.SelectCategory(news);

        app.Shell.Hide(news);

        Assert.Equal(CategoryFilter.All, app.Shell.Filter);
        Assert.DoesNotContain(app.Shell.Categories.Categories, c => c.CategoryId == news.CategoryId);
        Assert.Equal(1, app.Shell.Categories.HiddenCount);
    }

    [Fact]
    public async Task Playing_a_channel_queues_the_visible_list_from_that_channel()
    {
        await using var app = await AppHarness.StartAsync();
        await app.AddDemoAsync();
        var third = app.Shell.Content.Items[2];

        app.Shell.Open(third);

        var (lineup, _) = Assert.Single(app.Player.Played);
        Assert.Equal(120, lineup.Items.Count);
        Assert.Equal(2, lineup.StartIndex);
        Assert.Equal("All Live TV", lineup.Title);
        Assert.StartsWith(app.Provider.BaseUrl + "/live/demo/demo/", lineup.Items[2].Url, StringComparison.Ordinal);
        Assert.True(third.IsPlaying);
    }

    [Fact]
    public async Task Opening_a_movie_shows_its_page_and_play_uses_the_visible_list()
    {
        await using var app = await AppHarness.StartAsync();
        await app.AddDemoAsync();
        app.Shell.SelectSection(LibrarySection.Movies);
        var movie = app.Shell.Content.Items[1];

        app.Shell.Open(movie);
        var page = Assert.IsType<MovieDetailViewModel>(app.Shell.Detail);
        await WaitUntil(() => page.Plot is not null);
        page.PlayCommand.Execute(null);

        Assert.Equal(6163, page.Info.DurationSeconds);
        var (lineup, resume) = Assert.Single(app.Player.Played);
        Assert.Equal(1, lineup.StartIndex);
        Assert.True(resume);
        app.Shell.Back();
        Assert.Null(app.Shell.Detail);
    }

    [Fact]
    public async Task Series_pages_load_seasons_and_play_episodes_as_one_lineup()
    {
        await using var app = await AppHarness.StartAsync();
        await app.AddDemoAsync();
        app.Shell.SelectSection(LibrarySection.Series);
        app.Shell.SelectCategory(app.Shell.Categories.Fixed[0]);

        app.Shell.Open(app.Shell.Content.Items[2]);
        var page = Assert.IsType<SeriesDetailViewModel>(app.Shell.Detail);
        await WaitUntil(() => !page.IsLoading);
        page.PlayPrimaryCommand.Execute(null);

        Assert.Null(page.Error);
        Assert.Equal(3, page.Seasons.Count);
        Assert.Equal("Play S1 E1", page.PrimaryLabel);
        var (lineup, _) = Assert.Single(app.Player.Played);
        Assert.Equal(18, lineup.Items.Count);
        Assert.Equal(0, lineup.StartIndex);
        Assert.All(lineup.Items, i => Assert.Equal(ContentKind.Episode, i.Kind));
    }

    [Fact]
    public async Task M3u_playlists_have_no_series_section()
    {
        await using var app = await AppHarness.StartAsync();
        var m3u = new Playlist(Guid.NewGuid(), "List", new M3uSource($"{app.Provider.BaseUrl}/get.php?username=demo&password=demo"), app.Time.GetUtcNow());

        await app.Controller.AddAsync(m3u, null);
        app.Shell.SelectSection(LibrarySection.Series);

        Assert.Equal(2, app.Shell.Sections.Count);
        Assert.Equal(LibrarySection.Live, app.Shell.Section);
        Assert.Empty(app.Secrets.Saved);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), "Timed out waiting for the condition.");
    }
}

public sealed class EditorTests
{
    [Fact]
    public async Task Pasting_a_provider_link_fills_in_the_login()
    {
        await using var app = await AppHarness.StartAsync();
        var editor = new PlaylistEditorViewModel(app.Controller, app.Time, null)
        {
            Server = "http://h.example:8080/get.php?username=viewer&password=12&type=m3u_plus",
        };

        Assert.Equal("http://h.example:8080", editor.Server);
        Assert.Equal("viewer", editor.Username);
        Assert.Equal("12", editor.Password);
        Assert.True(editor.ShowsPlainHttpWarning);
        Assert.True(editor.IsValid);
    }

    [Fact]
    public async Task An_xtream_link_in_the_m3u_field_suggests_switching()
    {
        await using var app = await AppHarness.StartAsync();
        var editor = new PlaylistEditorViewModel(app.Controller, app.Time, null)
        {
            Kind = PlaylistKind.M3uUrl,
            Url = "https://h.example/get.php?username=a&password=b",
        };

        Assert.True(editor.SuggestsXtream);
        Assert.False(editor.ShowsPlainHttpWarning);
        editor.UseXtreamInsteadCommand.Execute(null);
        Assert.Equal(PlaylistKind.Xtream, editor.Kind);
        Assert.Equal("a", editor.Username);
    }

    [Fact]
    public async Task Save_shows_errors_inline_and_succeeds_with_the_right_login()
    {
        await using var app = await AppHarness.StartAsync();
        var editor = new PlaylistEditorViewModel(app.Controller, app.Time, null) { Server = app.Provider.BaseUrl, Username = "demo", Password = "nope" };
        var saved = false;
        editor.Saved += (_, _) => saved = true;

        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal("The server rejected that username or password.", editor.Error);
        Assert.False(saved);

        editor.Password = "demo";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.True(saved);
        Assert.Null(editor.Error);
        Assert.Equal("127.0.0.1", app.PlaylistStore.Saved.Single().Name);
    }

    [Fact]
    public async Task Editing_keeps_the_stored_password_when_left_blank()
    {
        await using var app = await AppHarness.StartAsync();
        var playlist = await app.AddDemoAsync();
        var editor = new PlaylistEditorViewModel(app.Controller, app.Time, playlist) { Name = "Renamed" };

        Assert.True(editor.IsValid);
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Null(editor.Error);
        Assert.Equal("Renamed", app.PlaylistStore.Saved.Single().Name);
        Assert.Equal("demo", app.Secrets.Saved[playlist.Id]);
    }

    [Fact]
    public void Pin_creation_validates_format_and_match()
    {
        var library = new Services.LibraryService(new MemoryStores.Library(), TimeProvider.System);
        var create = new CreatePinViewModel(library) { Pin = "12", Confirm = "12" };

        Assert.False(create.Submit());
        Assert.Equal("Use 4 to 8 digits.", create.Error);
        create.Pin = "1234";
        Assert.False(create.Submit());
        Assert.Equal("The PINs don't match.", create.Error);
        create.Confirm = "1234";
        Assert.True(create.Submit());
        Assert.NotNull(library.State.PinHash);

        var verify = new VerifyPinViewModel(library, "Enter your PIN.") { Pin = "9999" };
        Assert.False(verify.Submit());
        Assert.Equal("Wrong PIN.", verify.Error);
        Assert.Equal(string.Empty, verify.Pin);
        verify.Pin = "1234";
        Assert.True(verify.Submit());
    }

    [Fact]
    public void Lazy_lists_only_build_what_is_read()
    {
        var built = 0;
        var list = new LazyList<string>(65_000, i =>
        {
            built++;
            return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        });

        Assert.Equal("42", list[42]);
        Assert.Same(list[42], list[42]);
        Assert.Equal(1, built);
        Assert.Single(list.Realized);
    }
}
