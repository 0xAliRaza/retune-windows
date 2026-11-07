using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Retune.Infrastructure.Net;

namespace Retune.App.Services;

/// <summary>
/// Decoded, downsized, frozen bitmaps for the UI. Bytes come from the disk-backed <see cref="IArtworkSource"/>;
/// decoding happens off the UI thread at the size it's shown, and up to 600 images / 300 MiB stay in memory
/// (least recently used evicted). Spec: docs/spec/ui-player.md §5.
/// </summary>
public sealed class ArtworkLoader(IArtworkSource source)
{
    private const int MaxEntries = 600;
    private const long MaxBytes = 300L << 20;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, BitmapSource Image, long Cost)>> _index = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, BitmapSource Image, long Cost)> _recency = new();
    private long _bytes;

    public void Clear()
    {
        lock (_gate)
        {
            _index.Clear();
            _recency.Clear();
            _bytes = 0;
        }

        source.Clear();
    }

    /// <summary>The cached image, if it's already decoded at this size.</summary>
    public BitmapSource? Peek(Uri url, int pixelSize)
    {
        lock (_gate)
        {
            if (!_index.TryGetValue(Key(url, pixelSize), out var node))
            {
                return null;
            }

            _recency.Remove(node);
            _recency.AddFirst(node);
            return node.Value.Image;
        }
    }

    /// <param name="pixelSize">The longest side to decode to, in physical pixels.</param>
    public async Task<BitmapSource?> LoadAsync(Uri url, int pixelSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (Peek(url, pixelSize) is { } cached)
        {
            return cached;
        }

        var bytes = await source.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        var image = await Task.Run(() => Decode(bytes, pixelSize), cancellationToken).ConfigureAwait(false);
        if (image is null)
        {
            source.MarkUndecodable(url);
            return null;
        }

        Remember(Key(url, pixelSize), image);
        return image;
    }

    private static BitmapImage? Decode(byte[] bytes, int pixelSize)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var probe = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            var landscape = probe.PixelWidth >= probe.PixelHeight;
            stream.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            if (landscape)
            {
                image.DecodePixelWidth = Math.Min(pixelSize, probe.PixelWidth);
            }
            else
            {
                image.DecodePixelHeight = Math.Min(pixelSize, probe.PixelHeight);
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException or OverflowException)
        {
            return null;
        }
    }

    private void Remember(string key, BitmapSource image)
    {
        var cost = (long)image.PixelWidth * image.PixelHeight * 4;
        lock (_gate)
        {
            if (_index.ContainsKey(key))
            {
                return;
            }

            _index[key] = _recency.AddFirst((key, image, cost));
            _bytes += cost;
            while (_recency.Count > MaxEntries || _bytes > MaxBytes)
            {
                var last = _recency.Last!;
                _recency.RemoveLast();
                _index.Remove(last.Value.Key);
                _bytes -= last.Value.Cost;
            }
        }
    }

    private static string Key(Uri url, int pixelSize) => $"{url.AbsoluteUri}#{pixelSize}";
}

/// <summary>A gradient brush for items without artwork, stable per title (Core decides the colours).</summary>
public static class PlaceholderBrushes
{
    private static readonly Dictionary<string, LinearGradientBrush> Cache = new(StringComparer.Ordinal);

    public static Brush For(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        lock (Cache)
        {
            if (Cache.TryGetValue(title, out var cached))
            {
                return cached;
            }

            var (from, to) = Core.Formatting.ArtworkPlaceholder.Gradient(title);
            var brush = new LinearGradientBrush(FromHsb(from), FromHsb(to), new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
            brush.Freeze();
            if (Cache.Count > 2000)
            {
                Cache.Clear();
            }

            Cache[title] = brush;
            return brush;
        }
    }

    private static Color FromHsb((double H, double S, double B) hsb)
    {
        var (h, s, v) = (hsb.H * 6, hsb.S, hsb.B);
        var i = (int)Math.Floor(h) % 6;
        var f = h - Math.Floor(h);
        var p = v * (1 - s);
        var q = v * (1 - (f * s));
        var t = v * (1 - ((1 - f) * s));
        var (r, g, b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
