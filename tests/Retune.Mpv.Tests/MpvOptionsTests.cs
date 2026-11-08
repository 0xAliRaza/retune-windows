namespace Retune.Mpv.Tests;

public sealed class MpvOptionsTests
{
    [Fact]
    public void Security_options_are_always_present()
    {
        var options = MpvOptions.ForEmbeddedPlayer(windowHandle: 42, volume: 80, userAgent: "UA");

        Assert.All(MpvOptions.Security, required => Assert.Contains(required, options));
        Assert.Contains(new KeyValuePair<string, string>("wid", "42"), options);
        Assert.Contains(new KeyValuePair<string, string>("volume-max", "100"), options);
    }

    [Fact]
    public void Silent_players_have_no_audio_device_and_others_keep_theirs()
    {
        Assert.Contains(new KeyValuePair<string, string>("ao", "null"), MpvOptions.ForEmbeddedPlayer(null, 100, "UA", silent: true));
        Assert.DoesNotContain(MpvOptions.ForEmbeddedPlayer(null, 100, "UA"), o => o.Key == "ao");
    }

    [Fact]
    public void Certificates_are_verified_against_the_given_bundle()
    {
        var options = MpvOptions.ForEmbeddedPlayer(null, 100, "UA", trustedCertificates: @"C:\data\roots.pem");

        Assert.Contains(new KeyValuePair<string, string>("tls-verify", "yes"), options);
        Assert.Contains(new KeyValuePair<string, string>("tls-ca-file", @"C:\data\roots.pem"), options);
        Assert.DoesNotContain(MpvOptions.ForEmbeddedPlayer(null, 100, "UA"), o => o.Key == "tls-ca-file");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Volume_outside_0_to_100_is_refused(int volume) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MpvOptions.ForEmbeddedPlayer(null, volume, "UA"));
}
