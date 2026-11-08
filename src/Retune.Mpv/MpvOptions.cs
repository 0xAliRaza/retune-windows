using System.Globalization;

namespace Retune.Mpv;

/// <summary>
/// Startup options for an embedded Retune player (see docs/spec/ui-player.md §4.3).
/// The app owns all input and on-screen display; mpv only decodes and renders into the given window.
/// </summary>
public static class MpvOptions
{
    /// <summary>Options that are never negotiable: hostile URLs must not run scripts or pull in other content.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Security { get; } =
    [
        new("load-scripts", "no"),
        new("ytdl", "no"),
        new("load-unsafe-playlists", "no"),
        new("config", "no"),

        // HTTPS streams carry the login in their address: never send it to a server whose certificate isn't trusted.
        new("tls-verify", "yes"),
    ];

    /// <summary>Builds the full option list for a player that renders into <paramref name="windowHandle"/>.</summary>
    /// <param name="windowHandle">Child window mpv renders into, or null for audio/headless use (tests).</param>
    /// <param name="silent">No audio device at all (unattended dev runs): nothing can ever be heard.</param>
    /// <param name="trustedCertificates">A PEM bundle of trusted roots (libmpv's TLS library ships without any).</param>
    public static IReadOnlyList<KeyValuePair<string, string>> ForEmbeddedPlayer(nint? windowHandle, int volume, string userAgent, bool silent = false, string? trustedCertificates = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(volume, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volume, 100);

        var options = new List<KeyValuePair<string, string>>(Security)
        {
            // Rendering: mpv draws on its own thread with D3D11, independent of the WPF UI thread.
            new("vo", windowHandle is null ? "null" : "gpu-next"),
            new("gpu-api", "d3d11"),
            new("hwdec", "auto-safe"),
            new("deinterlace", "auto"),

            // Playback lifecycle
            new("keep-open", "yes"),
            new("idle", "yes"),
            new("force-window", "no"),

            // The app handles all input, cursor and OSD itself.
            new("input-default-bindings", "no"),
            new("input-vo-keyboard", "no"),
            new("input-cursor", "no"),
            new("cursor-autohide", "no"),
            new("osd-level", "0"),

            // Audio: never amplify.
            new("volume", volume.ToString(CultureInfo.InvariantCulture)),
            new("volume-max", "100"),

            // Network and cache
            new("cache", "yes"),
            new("demuxer-max-bytes", "512MiB"),
            new("demuxer-readahead-secs", "120"),
            new("network-timeout", "20"),
            new("demuxer-lavf-analyzeduration", "3"),
            new("user-agent", userAgent),
        };

        if (silent)
        {
            options.Add(new("ao", "null"));
        }

        if (trustedCertificates is not null)
        {
            options.Add(new("tls-ca-file", trustedCertificates));
        }

        if (windowHandle is { } hwnd)
        {
            options.Add(new("wid", ((long)hwnd).ToString(CultureInfo.InvariantCulture)));
        }

        return options;
    }
}
