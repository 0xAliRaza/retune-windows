using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Retune.FakeProvider;

/// <summary>Tiny dependency-free PNG generator: smooth two-colour gradients, stable per name.</summary>
internal static class PngArt
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Gradient(string seed, int width, int height)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var (r1, g1, b1) = (hash[0], hash[1], hash[2]);
        var (r2, g2, b2) = ((byte)(hash[3] / 3), (byte)(hash[4] / 3), (byte)(hash[5] / 3));

        var raw = new byte[height * (1 + (width * 3))];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + (width * 3));
            raw[row] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                var t = (x + y) / (double)(width + height);
                var p = row + 1 + (x * 3);
                raw[p] = (byte)(r1 + ((r2 - r1) * t));
                raw[p + 1] = (byte)(g1 + ((g2 - g1) * t));
                raw[p + 2] = (byte)(b1 + ((b2 - b1) * t));
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
}
