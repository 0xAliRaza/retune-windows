using Retune.Core.Library;
using Retune.Core.Models;
using Retune.Presentation.Tests.Support;
using Retune.Presentation.ViewModels;

namespace Retune.Presentation.Tests;

public sealed class SettingsTests : IAsyncLifetime
{
    private AppHarness _app = null!;
    private Playlist _playlist = null!;

    private SettingsViewModel Page => _app.Shell.SettingsPage ?? throw new InvalidOperationException("Settings isn't open.");

    public async ValueTask InitializeAsync()
    {
        _app = await AppHarness.StartAsync();
        _playlist = await _app.AddDemoAsync();
        _app.Shell.OpenSettings();
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public void Back_or_choosing_a_section_leaves_settings()
    {
        Assert.True(_app.Shell.CanGoBack);
        _app.Shell.Back();
        Assert.False(_app.Shell.IsSettingsOpen);

        _app.Shell.OpenSettings();
        _app.Shell.SelectSection(LibrarySection.Movies);
        Assert.False(_app.Shell.IsSettingsOpen);
    }

    [Fact]
    public void Playback_options_are_saved()
    {
        Page.SelectedLiveFormat = SettingsViewModel.LiveFormats.Single(o => o.Format == LiveFormat.Hls);
        Page.AutoplayNext = false;

        Assert.Equal(LiveFormat.Hls, _app.Settings.Saved.LiveFormat);
        Assert.False(_app.Settings.Saved.AutoplayNext);
        Assert.Equal(LiveFormat.Hls, Page.SelectedLiveFormat.Format);
    }

    [Fact]
    public void Hidden_categories_are_listed_by_name_and_can_be_shown_again()
    {
        var movies = _app.Controller.Index!.Catalog.CategoriesFor(LibrarySection.Movies);
        var known = LibraryKeys.Category(_playlist.Id, LibrarySection.Movies, movies[0].Id);
        var unknown = LibraryKeys.Category(_playlist.Id, LibrarySection.Live, "gone");
        var otherPlaylist = LibraryKeys.Category(Guid.NewGuid(), LibrarySection.Live, "x");
        _app.Library.Update(s => s.ToggleHidden(known).ToggleHidden(unknown).ToggleHidden(otherPlaylist));

        Assert.Equal(["Category gone", movies[0].Name.Trim()], Page.HiddenCategories.Select(r => r.Name));

        Page.ShowCategory(Page.HiddenCategories[1]);

        Assert.DoesNotContain(known, _app.Library.State.HiddenCategories);
        Assert.Single(Page.HiddenCategories);
    }

    [Fact]
    public async Task Removing_the_pin_needs_the_pin_and_removes_every_lock()
    {
        var key = LibraryKeys.Category(_playlist.Id, LibrarySection.Live, "1");
        _app.Library.Update(s => s.WithPinHash("hash").SetLocked(key, true));
        Assert.True(Page.HasPin);

        _app.Dialogs.PinAnswer = false;
        await Page.RemovePinAsync();
        Assert.True(Page.HasPin);

        _app.Dialogs.PinAnswer = true;
        await Page.RemovePinAsync();
        Assert.False(Page.HasPin);
        Assert.Empty(_app.Library.State.LockedCategories);
    }

    [Fact]
    public async Task Clearing_history_asks_first_and_keeps_favorites()
    {
        var channel = LibraryKeys.Content(_playlist.Id, ContentKind.Live, "1");
        _app.Library.Update(s => s.RecordRecentChannel(channel).ToggleFavorite(channel));

        _app.Dialogs.ConfirmAnswer = false;
        await Page.ClearHistoryAsync();
        Assert.NotEmpty(_app.Library.State.RecentChannelIds(_playlist.Id));

        _app.Dialogs.ConfirmAnswer = true;
        await Page.ClearHistoryAsync();
        Assert.Empty(_app.Library.State.RecentChannelIds(_playlist.Id));
        Assert.True(_app.Library.State.IsFavorite(channel));
        Assert.Contains("Clear watch history?", _app.Dialogs.Confirmations);
    }

    [Fact]
    public void Zoom_keys_step_reset_and_persist_and_the_settings_page_follows()
    {
        var changes = new List<string?>();
        _app.Shell.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _app.Shell.ZoomIn();
        _app.Shell.ZoomIn();
        Assert.Equal(1.25, _app.Shell.Zoom);
        Assert.Equal(1.25, _app.Settings.Saved.Zoom);
        Assert.Equal("125%", Page.SelectedZoom.Label);
        Assert.Contains(nameof(ShellViewModel.Zoom), changes);

        _app.Shell.ZoomOut();
        Assert.Equal(1.1, _app.Shell.Zoom);

        Page.SelectedZoom = SettingsViewModel.ZoomOptions.Single(o => o.Label == "150%");
        Assert.Equal(1.5, _app.Shell.Zoom);

        _app.Shell.ResetZoom();
        Assert.Equal(1, _app.Settings.Saved.Zoom);
    }

    [Fact]
    public void The_sidebar_collapses_and_expands_and_is_remembered()
    {
        Assert.True(_app.Shell.IsSidebarExpanded);
        var changed = 0;
        _app.Shell.PropertyChanged += (_, e) => changed += e.PropertyName == nameof(ShellViewModel.IsSidebarExpanded) ? 1 : 0;

        _app.Shell.ToggleSidebar();
        Assert.False(_app.Shell.IsSidebarExpanded);
        Assert.True(_app.Settings.Saved.SidebarCollapsed);

        _app.Shell.ToggleSidebar();
        Assert.True(_app.Shell.IsSidebarExpanded);
        Assert.False(_app.Settings.Saved.SidebarCollapsed);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Clearing_artwork_runs_once_then_reads_cleared()
    {
        Page.ClearArtworkCommand.Execute(null);

        Assert.Equal(1, _app.ArtworkClears);
        Assert.Equal("Cleared", Page.ArtworkButtonLabel);
        Assert.False(Page.ClearArtworkCommand.CanExecute(null));
    }
}
