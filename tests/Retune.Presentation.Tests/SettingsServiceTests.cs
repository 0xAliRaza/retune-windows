using Microsoft.Extensions.Time.Testing;
using Retune.Core.Models;
using Retune.Presentation.Services;
using Retune.Presentation.Tests.Support;

namespace Retune.Presentation.Tests;

public sealed class SettingsServiceTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly MemoryStores.Settings _store = new();

    [Fact]
    public void An_immediate_change_is_saved_at_once()
    {
        using var settings = new SettingsService(_store, _time);

        settings.Update(s => s with { AutoplayNext = false });

        Assert.False(_store.Saved.AutoplayNext);
    }

    [Fact]
    public void A_debounced_change_is_saved_after_the_delay_and_only_once()
    {
        var store = new CountingStore();
        using var settings = new SettingsService(store, _time);

        for (var volume = 10; volume <= 50; volume += 10)
        {
            settings.Update(s => s with { Volume = volume }, debounce: true);
        }

        Assert.Equal(0, store.Saves);
        _time.Advance(SettingsService.SaveDelay);
        Assert.Equal(1, store.Saves);
        Assert.Equal(50, store.Saved.Volume);
    }

    [Fact]
    public void A_pending_debounced_change_survives_a_later_immediate_change()
    {
        using var settings = new SettingsService(_store, _time);

        settings.Update(s => s with { Volume = 30 }, debounce: true);
        settings.Update(s => s with { TileSize = TileSize.Small });

        Assert.Equal(30, _store.Saved.Volume);
        Assert.Equal(TileSize.Small, _store.Saved.TileSize);
    }

    [Fact]
    public void Dispose_writes_a_pending_change()
    {
        var settings = new SettingsService(_store, _time);
        settings.Update(s => s with { Volume = 40 }, debounce: true);

        settings.Dispose();

        Assert.Equal(40, _store.Saved.Volume);
    }

    [Fact]
    public void An_unchanged_value_neither_saves_nor_notifies()
    {
        var store = new CountingStore();
        using var settings = new SettingsService(store, _time);
        var notified = 0;
        settings.Changed += (_, _) => notified++;

        settings.Update(s => s);

        Assert.Equal(0, store.Saves);
        Assert.Equal(0, notified);
    }

    private sealed class CountingStore : Core.Persistence.ISettingsStore
    {
        public AppSettings Saved { get; private set; } = AppSettings.Default;

        public int Saves { get; private set; }

        public AppSettings Load() => Saved;

        public void Save(AppSettings settings)
        {
            Saved = settings;
            Saves++;
        }
    }
}
