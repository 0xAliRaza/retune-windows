using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Retune.FakeProvider;

/// <summary>
/// Tiny dependency-free PNG generator for demo artwork. Posters, backdrops and stills are procedural landscapes
/// (sky, sun or moon, layered ridges); channel logos are emblems. Everything is synthetic and stable per seed,
/// so a movie's poster and backdrop share one scene.
/// </summary>
internal static class PngArt
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static readonly Palette[] Palettes =
    [
        new(Rgb(0x1B1A4A), Rgb(0xF2795A), Rgb(0xFFD9A0), Rgb(0x2A1838), Night: false),
        new(Rgb(0x0E1033), Rgb(0x8A4FB5), Rgb(0xFFC6E8), Rgb(0x1A1030), Night: false),
        new(Rgb(0x031A2B), Rgb(0x1E6F7E), Rgb(0xE8F6FF), Rgb(0x04131E), Night: true),
        new(Rgb(0x3B6FB6), Rgb(0xF5C98A), Rgb(0xFFF3D0), Rgb(0x7A3E2A), Night: false),
        new(Rgb(0x020817), Rgb(0x0F5D4E), Rgb(0xCFFFEF), Rgb(0x020B10), Night: true),
        new(Rgb(0x6FA8D6), Rgb(0xE9F1E0), Rgb(0xFFFFFF), Rgb(0x1F4A3A), Night: false),
        new(Rgb(0x2A0610), Rgb(0xE2364A), Rgb(0xFFE0B0), Rgb(0x1A0408), Night: false),
        new(Rgb(0x4A2A6A), Rgb(0xFFB347), Rgb(0xFFF0C0), Rgb(0x2C1A3A), Night: false),
        new(Rgb(0x0A0A1F), Rgb(0x3A2F7A), Rgb(0xF4F1FF), Rgb(0x0B0A18), Night: true),
        new(Rgb(0x103A5A), Rgb(0x6FD3C8), Rgb(0xFFFBE8), Rgb(0x0B2A33), Night: false),
    ];

    /// <summary>A landscape scene: same <paramref name="seed"/>, same scene at any size.</summary>
    public static byte[] Scene(string seed, int width, int height)
    {
        var random = new Random(Seed(seed));
        var palette = Palettes[random.Next(Palettes.Length)];
        var pixels = new Color[width * height];
        var horizon = height * (0.52 + (random.NextDouble() * 0.12));

        // Sky.
        for (var y = 0; y < height; y++)
        {
            var sky = Mix(palette.SkyTop, palette.Horizon, Math.Pow(Math.Min(1, y / horizon), 1.6));
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = sky;
            }
        }

        // Stars on night scenes.
        if (palette.Night)
        {
            var stars = width * height / 900;
            for (var i = 0; i < stars; i++)
            {
                var (x, y) = (random.Next(width), (int)(random.NextDouble() * horizon * 0.85));
                var brightness = 0.35 + (random.NextDouble() * 0.65);
                ref var p = ref pixels[(y * width) + x];
                p = Mix(p, palette.Sun, brightness);
            }
        }

        // Sun or moon with a soft glow.
        var scale = Math.Min(width, height);
        var (cx, cy) = (width * (0.25 + (random.NextDouble() * 0.5)), horizon * (0.45 + (random.NextDouble() * 0.35)));
        var radius = scale * (0.09 + (random.NextDouble() * 0.07));
        for (var y = 0; y < (int)horizon + (int)radius; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var d = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
                ref var p = ref pixels[(Math.Min(y, height - 1) * width) + x];
                p = d < radius
                    ? Mix(p, palette.Sun, Math.Clamp((radius - d) * 1.5, 0, 1))
                    : Mix(p, palette.Sun, 0.55 * Math.Exp(-(d - radius) / (radius * 1.1)));
            }
        }

        // Ridges, back to front: each nearer layer is darker and less hazy.
        const int Layers = 4;
        for (var layer = 0; layer < Layers; layer++)
        {
            var depth = (layer + 1) / (double)Layers;
            var color = Mix(palette.Horizon, palette.Ridge, 0.35 + (0.65 * depth));
            var baseline = horizon + (height * 0.07 * layer);
            var amplitude = height * (0.13 - (0.02 * layer));
            var (f1, f2, f3) = (Frequency(random, 1.2, width), Frequency(random, 3.1, width), Frequency(random, 7.3, width));
            var (p1, p2, p3) = (random.NextDouble() * Math.Tau, random.NextDouble() * Math.Tau, random.NextDouble() * Math.Tau);
            for (var x = 0; x < width; x++)
            {
                var ridge = baseline - (amplitude * ((0.55 * Math.Sin((f1 * x) + p1)) + (0.3 * Math.Sin((f2 * x) + p2)) + (0.15 * Math.Sin((f3 * x) + p3)) + 0.4));
                for (var y = Math.Max(0, (int)ridge); y < height; y++)
                {
                    var shade = Math.Clamp((y - ridge) / (height - ridge + 1), 0, 1);
                    pixels[(y * width) + x] = Mix(color, palette.Ridge, shade * 0.6);
                }
            }
        }

        // A gentle vignette, then a touch of noise so gradients don't band.
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (u, v) = ((x / (double)width) - 0.5, (y / (double)height) - 0.5);
                var vignette = 1 - (0.35 * Math.Clamp((u * u) + (v * v) - 0.08, 0, 1));
                var noise = (random.NextDouble() - 0.5) * 2.0;
                ref var p = ref pixels[(y * width) + x];
                p = new Color((p.R * vignette) + noise, (p.G * vignette) + noise, (p.B * vignette) + noise);
            }
        }

        return Encode(pixels, width, height);
    }

    /// <summary>A channel logo: a glowing emblem on a dark plate.</summary>
    public static byte[] Logo(string seed, int width, int height)
    {
        var random = new Random(Seed(seed));
        var palette = Palettes[random.Next(Palettes.Length)];
        var pixels = new Color[width * height];
        var plate = Rgb(0x15171C);
        var (cx, cy, r) = (width / 2.0, height / 2.0, height * 0.34);
        var inner = r * (0.42 + (random.NextDouble() * 0.15));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var d = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
                var disc = Mix(palette.Horizon, palette.SkyTop, Math.Clamp((y - (cy - r)) / (2 * r), 0, 1));
                var color = plate;
                if (d < r)
                {
                    color = Mix(plate, disc, Math.Clamp(r - d, 0, 1));
                    if (d < inner)
                    {
                        color = Mix(color, Mix(plate, palette.Sun, 0.92), Math.Clamp(inner - d, 0, 1));
                    }
                }
                else
                {
                    color = Mix(plate, palette.Horizon, 0.35 * Math.Exp(-(d - r) / (r * 0.35)));
                }

                pixels[(y * width) + x] = color;
            }
        }

        return Encode(pixels, width, height);
    }

    private static double Frequency(Random random, double cycles, int width) => cycles * (0.7 + (random.NextDouble() * 0.6)) * Math.Tau / width;

    private static int Seed(string seed) => BinaryPrimitives.ReadInt32LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    private static Color Rgb(int hex) => new((hex >> 16) & 0xFF, (hex >> 8) & 0xFF, hex & 0xFF);

    private static Color Mix(Color a, Color b, double t) =>
        new(a.R + ((b.R - a.R) * t), a.G + ((b.G - a.G) * t), a.B + ((b.B - a.B) * t));

    private static byte[] Encode(Color[] pixels, int width, int height)
    {
        var raw = new byte[height * (1 + (width * 3))];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + (width * 3));
            raw[row] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                var c = pixels[(y * width) + x];
                var p = row + 1 + (x * 3);
                raw[p] = ToByte(c.R);
                raw[p + 1] = ToByte(c.G);
                raw[p + 2] = ToByte(c.B);
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: RGB
        Chunk(png, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            Chunk(png, "IDAT", compressed.ToArray());
        }

        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        png.Write(typeBytes);
        png.Write(data);
        var crc = Crc(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        png.Write(crcBytes);
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type.Concat(data))
        {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        return c ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private readonly record struct Color(double R, double G, double B);

    private sealed record Palette(Color SkyTop, Color Horizon, Color Sun, Color Ridge, bool Night);
}
