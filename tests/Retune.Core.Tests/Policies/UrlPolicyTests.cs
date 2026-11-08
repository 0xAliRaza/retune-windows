using Retune.Core.Models;
using Retune.Core.Policies;
using Retune.Core.Xtream;

namespace Retune.Core.Tests.Policies;

public sealed class UrlPolicyTests
{
    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("file://server/share/x.ts")]
    [InlineData("av://dshow:video=cam")]
    [InlineData("edl://a.mkv,10,20")]
    [InlineData("memory://abc")]
    [InlineData("fd://3")]
    [InlineData("lavf://http://x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/etc/passwd")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData("httpx://host.example/x.ts")]
    [InlineData("http:host.example/x.ts")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_local_and_special_protocols(string? url)
    {
        Assert.False(UrlPolicy.IsAllowedStream(url));
    }

    [Theory]
    [InlineData("http://h.example/live/1.ts")]
    [InlineData("HTTPS://h.example/x.m3u8")]
    [InlineData("rtmp://h.example/app/stream")]
    [InlineData("rtsps://cam.example:322/stream")]
    [InlineData("rtp://239.0.0.1:5004")]
    [InlineData("udp://@239.0.0.1:1234")]
    public void Accepts_network_streams(string url)
    {
        Assert.True(UrlPolicy.IsAllowedStream(url));
    }

    [Theory]
    [InlineData("rtmp://h.example/logo.png")]
    [InlineData("file:///C:/logo.png")]
    public void Artwork_is_http_only(string url)
    {
        Assert.False(UrlPolicy.IsAllowedArtwork(new Uri(url)));
    }

    [Theory]
    [InlineData("http:\\/\\/img.example\\/logo.png", "http://img.example/logo.png")]
    [InlineData("  https://img.example/a.jpg  ", "https://img.example/a.jpg")]
    [InlineData("http://img.example/my poster.jpg", "http://img.example/my%20poster.jpg")]
    public void Lenient_urls_tolerate_escaped_slashes_spaces_and_padding(string raw, string expected)
    {
        Assert.Equal(expected, LenientUrl.Parse(raw)?.AbsoluteUri);
    }

    [Theory]
    [InlineData("httpx://img.example/a.jpg")]
    [InlineData("ftp://img.example/a.jpg")]
    [InlineData("img.example/a.jpg")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Lenient_urls_reject_lookalikes_and_blanks(string? raw)
    {
        Assert.Null(LenientUrl.Parse(raw));
    }
}

public sealed class LocatorTests
{
    private static readonly XtreamUrls Urls = new(new XtreamCredentials("http://h:8080", "u", "secret"));

    private static PlaybackItem Item(string url, string id = "42") => new(id, ContentKind.Movie, "Film", null, url);

    [Fact]
    public void Locators_strip_credentials_and_resolve_back()
    {
        const string url = "http://h:8080/movie/u/secret/9.mkv";

        var locator = Locators.FromXtreamStream(url);

        Assert.Equal("retune://movie/9.mkv", locator);
        Assert.DoesNotContain("secret", locator, StringComparison.Ordinal);
        Assert.Equal(url, Locators.Resolve(locator!, Urls));
    }

    [Fact]
    public void A_non_xtream_url_has_no_locator_and_resolves_unchanged()
    {
        const string url = "http://cdn.example/film.mp4";

        Assert.Null(Locators.FromXtreamStream(url));
        Assert.Equal(url, Locators.Resolve(url, Urls));
    }

    [Fact]
    public void Slash_in_password_still_produces_a_credential_free_locator()
    {
        var urls = new XtreamUrls(new XtreamCredentials("http://h:8080", "u", "se/cret"));

        Assert.Equal("retune://movie/9.mkv", Locators.FromXtreamStream(urls.Movie(new Movie("9", "M", null, null, null, null, "mkv", null))));
    }

    [Fact]
    public void Credential_free_items_use_xtream_locators_with_an_account()
    {
        var stored = Locators.CredentialFree(Item("http://h:8080/series/u/secret/11.mp4"), hasAccount: true);

        Assert.Equal("retune://series/11.mp4", stored!.Url);
    }

    [Fact]
    public void Credential_free_items_use_opaque_ids_without_an_account()
    {
        var stored = Locators.CredentialFree(Item("http://cdn.example/x.mkv?token=abc"), hasAccount: false);

        Assert.Equal("retune://item/42", stored!.Url);
        Assert.Equal("42", Locators.OpaqueId(stored.Url));
    }

    [Fact]
    public void An_unrecognised_account_url_is_not_stored_at_all()
    {
        Assert.Null(Locators.CredentialFree(Item("http://h:8080/get.php?username=u&password=secret"), hasAccount: true));
    }

    [Fact]
    public void An_already_stored_item_is_unchanged()
    {
        var item = Item("retune://movie/9.mkv");

        Assert.Same(item, Locators.CredentialFree(item, hasAccount: true));
    }

    [Theory]
    [InlineData("retune://item/42")]
    [InlineData("retune://bogus/1.ts")]
    [InlineData("retune://movie/")]
    public void Unresolvable_locators_give_null(string locator)
    {
        Assert.Null(Locators.Resolve(locator, Urls));
    }

    [Theory]
    [InlineData("http://tv.example:8080", "http://TV.example:8080/", true)]
    [InlineData("http://tv.example", "http://tv.example:80", true)]
    [InlineData("http://tv.example:8080", "http://tv.example:8081", false)]
    [InlineData("http://tv.example", "https://tv.example", false)]
    [InlineData("http://tv.example", "http://evil.example", false)]
    [InlineData("http://tv.example", "not a url", false)]
    [InlineData(null, "http://tv.example", false)]
    public void SameOrigin(string? a, string? b, bool expected) => Assert.Equal(expected, Retune.Core.Policies.UrlPolicy.SameOrigin(a, b));
}