using Retune.Core.Navigation;

namespace Retune.Core.Tests.Navigation;

public sealed class ShortcutCatalogTests
{
    [Fact]
    public void The_sheet_has_a_player_and_an_app_column()
    {
        Assert.Equal(["Player", "App"], ShortcutCatalog.All.Select(g => g.Title));
        Assert.All(ShortcutCatalog.All, g => Assert.NotEmpty(g.Shortcuts));
    }

    [Fact]
    public void Every_row_has_keys_and_an_action()
    {
        Assert.All(ShortcutCatalog.All.SelectMany(g => g.Shortcuts), s =>
        {
            Assert.NotEmpty(s.Keys);
            Assert.All(s.Keys, k => Assert.False(string.IsNullOrWhiteSpace(k)));
            Assert.False(string.IsNullOrWhiteSpace(s.Action));
        });
    }

    [Fact]
    public void A_key_means_one_thing_within_a_column()
    {
        foreach (var group in ShortcutCatalog.All)
        {
            var keys = group.Shortcuts.SelectMany(s => s.Keys).ToList();
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("Ctrl+/")]
    [InlineData("?")]
    public void Help_is_listed_under_every_help_key(string key) =>
        Assert.Contains(ShortcutCatalog.App.Shortcuts, s => s.Keys.Contains(key) && s.Action == "Keyboard shortcuts");
}
