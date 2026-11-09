// CharmCLI (S.I.V.A.): a fast PNG writer for 8-bit BGRA / RGBA textures.
// Windows' WIC PNG encoder (Charm's way) spends most of a texture's save time compressing. This writer gives the SAME PIXELS (8-bit RGBA,
// sRGB chunk like WIC_FLAGS.FORCE_SRGB) with one fixed row filter (Paeth) and the fastest zlib level: files are a bit bigger, much quicker.
// Used by the -siva CLI only (-png wic = Charm's WIC encoder). Formats it does not handle return false and go through WIC as before.
using System.IO.Compression;

namespace Tiger.Exporters;

public static class SivaPng
{
    static readonly uint[] _crc = MakeCrc();

    static uint[] MakeCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    static uint Crc(uint crc, ReadOnlySpan<byte> b)
    {
        foreach (byte x in b) crc = _crc[(crc ^ x) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    static void BE(Span<byte> d, uint v) { d[0] = (byte)(v >> 24); d[1] = (byte)(v >> 16); d[2] = (byte)(v >> 8); d[3] = (byte)v; }

    static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> h = stackalloc byte[8];
        BE(h, (uint)data.Length);
        for (int i = 0; i < 4; i++) h[4 + i] = (byte)type[i];
        s.Write(h);
        s.Write(data);
        uint c = Crc(Crc(0xFFFFFFFFu, h.Slice(4, 4)), data) ^ 0xFFFFFFFFu;
        Span<byte> t = stackalloc byte[4];
        BE(t, c);
        s.Write(t);
    }

    /// <summary>Writes rows of 4-byte pixels (bgra: B,G,R,A order, else R,G,B,A) as an 8-bit RGBA PNG.  rowPitch = bytes from one row to the next.</summary>
    public static unsafe void Write(string path, byte* pixels, int width, int height, long rowPitch, bool bgra, bool srgb = true)
    {
        int stride = width * 4;
        byte[] prev = new byte[stride], cur = new byte[stride], line = new byte[stride + 1];
        using var idat = new MemoryStream(Math.Max(1024, stride * height / 3));
        using (var z = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                byte* src = pixels + y * rowPitch;
                if (bgra)
                    for (int x = 0; x < stride; x += 4) { cur[x] = src[x + 2]; cur[x + 1] = src[x + 1]; cur[x + 2] = src[x]; cur[x + 3] = src[x + 3]; }
                else
                    new ReadOnlySpan<byte>(src, stride).CopyTo(cur);
                line[0] = 4;                                                   // Paeth
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= 4 ? cur[x - 4] : 0, b = prev[x], c = x >= 4 ? prev[x - 4] : 0;
                    int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                    int pr = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                    line[x + 1] = (byte)(cur[x] - pr);
                }
                z.Write(line, 0, line.Length);
                (prev, cur) = (cur, prev);
            }
        }

        using var f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        f.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        Span<byte> ihdr = stackalloc byte[13];
        BE(ihdr, (uint)width); BE(ihdr.Slice(4), (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;   // 8 bit, RGBA, deflate, adaptive filtering, no interlace
        Chunk(f, "IHDR", ihdr);
        if (srgb) Chunk(f, "sRGB", stackalloc byte[] { 0 });                   // perceptual, as WIC writes for FORCE_SRGB
        Chunk(f, "IDAT", new ReadOnlySpan<byte>(idat.GetBuffer(), 0, (int)idat.Length));
        Chunk(f, "IEND", ReadOnlySpan<byte>.Empty);
    }
}
