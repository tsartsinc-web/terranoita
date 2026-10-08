using System;
using System.IO;
using System.IO.Compression;

namespace Terranoita.Noita
{
    /// <summary>
    /// A small PNG reader for Noita's pixel scenes and maps (8-bit grey, RGB, palette, grey+alpha, RGBA; not interlaced):
    /// pixels as ARGB, row by row. Core has no image library and the game's XNA is not here.
    /// </summary>
    public sealed class NoitaPng
    {
        public int Width, Height;
        public uint[] Pixels;   // 0xAARRGGBB

        public uint At(int x, int y) => Pixels[y * Width + x];

        public static NoitaPng Read(byte[] data)
        {
            if (data == null || data.Length < 8 || data[0] != 0x89 || data[1] != (byte)'P' || data[2] != (byte)'N' || data[3] != (byte)'G')
                throw new InvalidDataException("not a PNG");
            int w = 0, h = 0, depth = 0, type = 0, interlace = 0;
            uint[] palette = null;
            byte[] alphas = null;
            var idat = new MemoryStream();
            int p = 8;
            while (p + 8 <= data.Length)
            {
                int len = (data[p] << 24) | (data[p + 1] << 16) | (data[p + 2] << 8) | data[p + 3];
                string kind = System.Text.Encoding.ASCII.GetString(data, p + 4, 4);
                int at = p + 8;
                if (len < 0 || at + len > data.Length)
                    throw new InvalidDataException("broken PNG chunk " + kind);
                switch (kind)
                {
                    case "IHDR":
                        w = Int(data, at); h = Int(data, at + 4); depth = data[at + 8]; type = data[at + 9]; interlace = data[at + 12];
                        break;
                    case "PLTE":
                        palette = new uint[len / 3];
                        for (int i = 0; i < palette.Length; i++)
                            palette[i] = 0xFF000000u | ((uint)data[at + i * 3] << 16) | ((uint)data[at + i * 3 + 1] << 8) | data[at + i * 3 + 2];
                        break;
                    case "tRNS":
                        alphas = new byte[len];
                        Array.Copy(data, at, alphas, 0, len);
                        break;
                    case "IDAT":
                        idat.Write(data, at, len);
                        break;
                }
                if (kind == "IEND")
                    break;
                p = at + len + 4;   // + CRC
            }
            if (depth != 8 || interlace != 0 || (type != 0 && type != 2 && type != 3 && type != 4 && type != 6))
                throw new NotSupportedException("PNG type " + type + " depth " + depth + (interlace != 0 ? " interlaced" : ""));
            int bpp = type == 0 || type == 3 ? 1 : type == 4 ? 2 : type == 2 ? 3 : 4;
            int stride = w * bpp;
            var raw = new byte[(stride + 1) * h];
            idat.Position = 2;   // zlib header; the Adler checksum at the end is not read
            using (var z = new DeflateStream(idat, CompressionMode.Decompress))
            {
                int got = 0, n;
                while (got < raw.Length && (n = z.Read(raw, got, raw.Length - got)) > 0)
                    got += n;
                if (got < raw.Length)
                    throw new InvalidDataException("PNG data too short");
            }
            var px = new uint[w * h];
            var prev = new byte[stride];
            var cur = new byte[stride];
            for (int y = 0; y < h; y++)
            {
                int filter = raw[y * (stride + 1)];
                Array.Copy(raw, y * (stride + 1) + 1, cur, 0, stride);
                Unfilter(filter, cur, prev, bpp);
                for (int x = 0; x < w; x++)
                {
                    int i = x * bpp;
                    uint c;
                    switch (type)
                    {
                        case 0: c = Grey(cur[i], 255); break;
                        case 4: c = Grey(cur[i], cur[i + 1]); break;
                        case 2: c = 0xFF000000u | ((uint)cur[i] << 16) | ((uint)cur[i + 1] << 8) | cur[i + 2]; break;
                        case 3:
                            c = palette != null && cur[i] < palette.Length ? palette[cur[i]] : 0;
                            if (alphas != null && cur[i] < alphas.Length)
                                c = (c & 0x00FFFFFFu) | ((uint)alphas[cur[i]] << 24);
                            break;
                        default: c = ((uint)cur[i + 3] << 24) | ((uint)cur[i] << 16) | ((uint)cur[i + 1] << 8) | cur[i + 2]; break;
                    }
                    px[y * w + x] = c;
                }
                var t = prev; prev = cur; cur = t;
            }
            return new NoitaPng { Width = w, Height = h, Pixels = px };
        }

        static uint Grey(byte v, byte a) => ((uint)a << 24) | ((uint)v << 16) | ((uint)v << 8) | v;

        static int Int(byte[] d, int at) => (d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3];

        static void Unfilter(int filter, byte[] cur, byte[] prev, int bpp)
        {
            for (int i = 0; i < cur.Length; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                switch (filter)
                {
                    case 1: cur[i] = (byte)(cur[i] + a); break;
                    case 2: cur[i] = (byte)(cur[i] + b); break;
                    case 3: cur[i] = (byte)(cur[i] + ((a + b) >> 1)); break;
                    case 4:
                        int pa = Math.Abs(b - c), pb = Math.Abs(a - c), pc = Math.Abs(a + b - 2 * c);
                        cur[i] = (byte)(cur[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
                        break;
                }
            }
        }
    }
}
