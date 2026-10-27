using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;

namespace CawPlus
{
    public static class ImageIO
    {
        /// <summary>
        /// Writes a 0..1 grid as a grayscale PNG, stored as 24-bit RGB with R=G=B (the format CAW's paint import has
        /// taken since v0.1). Assembled here row by row rather than through GDI+, so it needs no full-size bitmap in
        /// memory and also works in CAWPlusHelper.
        /// </summary>
        public static void WriteGrayPng(Grid g, string path)
        {
            int w = g.W;
            WritePng(path, w, g.H, 8, 2, 3, delegate (int y, byte[] row) // 8-bit RGB
            {
                for (int x = 0; x < w; x++)
                {
                    float v = g.D[y * w + x] * 255f + 0.5f;
                    byte c = (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
                    row[1 + x * 3] = c; row[2 + x * 3] = c; row[3 + x * 3] = c;
                }
            });
        }

        /// <summary>
        /// Writes a 16-bit greyscale PNG (the format CAW's height import wants). 'g' holds 0..1, which maps to
        /// 0..65535. GDI+ cannot write 16-bit, so the PNG is assembled here (zlib = header + deflate + Adler-32).
        /// </summary>
        public static void WriteGray16Png(Grid g, string path)
        {
            int w = g.W;
            WritePng(path, w, g.H, 16, 0, 2, delegate (int y, byte[] row) // 16-bit grey, big-endian
            {
                for (int x = 0; x < w; x++)
                {
                    float v = g.D[y * w + x];
                    int q = (int)(Math.Max(0f, Math.Min(1f, v)) * 65535f + 0.5f);
                    row[1 + x * 2] = (byte)(q >> 8); row[2 + x * 2] = (byte)q;
                }
            });
        }

        private delegate void RowFill(int y, byte[] row);

        /// <summary>PNG with one IDAT, rows (filter none) filled by 'fill' and deflated as they're made.</summary>
        private static void WritePng(string path, int w, int h, byte depth, byte colourType, int bytesPerPixel, RowFill fill)
        {
            using (FileStream fs = File.Create(path))
            {
                fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                byte[] ihdr = new byte[13];
                PutBE32(ihdr, 0, w); PutBE32(ihdr, 4, h);
                ihdr[8] = depth; ihdr[9] = colourType;
                Chunk(fs, "IHDR", ihdr);
                MemoryStream z = new MemoryStream();
                z.WriteByte(0x78); z.WriteByte(0x01);
                uint a = 1, b2 = 0;
                byte[] row = new byte[1 + w * bytesPerPixel];
                using (DeflateStream ds = new DeflateStream(z, CompressionMode.Compress, true))
                    for (int y = 0; y < h; y++)
                    {
                        row[0] = 0; // filter: none
                        fill(y, row);
                        ds.Write(row, 0, row.Length);
                        for (int i = 0; i < row.Length; i++) { a = (a + row[i]) % 65521; b2 = (b2 + a) % 65521; }
                    }
                uint adler = (b2 << 16) | a;
                z.WriteByte((byte)(adler >> 24)); z.WriteByte((byte)(adler >> 16)); z.WriteByte((byte)(adler >> 8)); z.WriteByte((byte)adler);
                Chunk(fs, "IDAT", z.ToArray());
                Chunk(fs, "IEND", new byte[0]);
            }
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            byte[] len = new byte[4]; PutBE32(len, 0, data.Length); s.Write(len, 0, 4);
            byte[] t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t, 0, 4);
            s.Write(data, 0, data.Length);
            uint crc = Crc(t, 0xFFFFFFFFu); crc = Crc(data, crc) ^ 0xFFFFFFFFu;
            byte[] c = new byte[4]; PutBE32(c, 0, (int)crc); s.Write(c, 0, 4);
        }

        private static uint[] crcTable;
        private static uint Crc(byte[] d, uint c)
        {
            if (crcTable == null)
            {
                crcTable = new uint[256];
                for (uint n = 0; n < 256; n++) { uint v = n; for (int k = 0; k < 8; k++) v = (v & 1) != 0 ? 0xEDB88320u ^ (v >> 1) : v >> 1; crcTable[n] = v; }
            }
            for (int i = 0; i < d.Length; i++) c = crcTable[(c ^ d[i]) & 0xFF] ^ (c >> 8);
            return c;
        }

        private static void PutBE32(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        /// <summary>Reads just the size from a PNG header. Returns false if not a PNG.</summary>
        public static bool PngSize(string path, out int w, out int h)
        {
            w = h = 0;
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] hdr = new byte[24];
                if (fs.Read(hdr, 0, 24) < 24 || hdr[1] != (byte)'P' || hdr[2] != (byte)'N' || hdr[3] != (byte)'G') return false;
                w = BE32(hdr, 16); h = BE32(hdr, 20);
                return true;
            }
        }

        /// <summary>
        /// Reads a PNG's first channel into a 0..1 grid. Handles 8/16-bit grayscale, gray+alpha, RGB, RGBA
        /// (non-interlaced) with our own decoder, because GDI+ cannot open 16-bit grayscale PNGs (which is what
        /// CAW's height export may produce). Falls back to GDI+ for anything else.
        /// </summary>
        public static Grid ReadPngChannel0(string path)
        {
            try { return DecodePng(File.ReadAllBytes(path)); }
            catch (Exception ex)
            {
                Log.Info("Own PNG decoder failed (" + ex.Message + "), trying GDI+");
                using (Bitmap bmp = new Bitmap(path))
                {
                    Grid g = new Grid(bmp.Width, bmp.Height);
                    for (int y = 0; y < bmp.Height; y++)
                        for (int x = 0; x < bmp.Width; x++)
                            g.D[y * g.W + x] = bmp.GetPixel(x, y).R / 255f;
                    return g;
                }
            }
        }

        private static Grid DecodePng(byte[] f)
        {
            if (f.Length < 33 || f[1] != (byte)'P') throw new InvalidDataException("not a PNG");
            int pos = 8, w = 0, h = 0, depth = 0, colorType = 0, interlace = 0;
            MemoryStream idat = new MemoryStream();
            while (pos + 8 <= f.Length)
            {
                int len = BE32(f, pos);
                if (len < 0 || len > f.Length - pos - 12) throw new InvalidDataException("bad PNG chunk length");
                string type = System.Text.Encoding.ASCII.GetString(f, pos + 4, 4);
                int data = pos + 8;
                if (type == "IHDR")
                {
                    w = BE32(f, data); h = BE32(f, data + 4);
                    depth = f[data + 8]; colorType = f[data + 9]; interlace = f[data + 12];
                }
                else if (type == "IDAT") idat.Write(f, data, len);
                else if (type == "IEND") break;
                pos = data + len + 4;
            }
            if (interlace != 0) throw new NotSupportedException("interlaced PNG");
            int channels;
            switch (colorType)
            {
                case 0: channels = 1; break;
                case 2: channels = 3; break;
                case 4: channels = 2; break;
                case 6: channels = 4; break;
                default: throw new NotSupportedException("PNG colour type " + colorType);
            }
            if (depth != 8 && depth != 16) throw new NotSupportedException("PNG bit depth " + depth);
            int bpp = channels * depth / 8;
            if (w <= 0 || h <= 0 || (long)(w * (long)bpp + 1) * h > int.MaxValue) throw new InvalidDataException("bad PNG size");
            int stride = w * bpp;

            byte[] raw = new byte[(stride + 1) * h];
            idat.Position = 2; // skip zlib header
            using (DeflateStream ds = new DeflateStream(idat, CompressionMode.Decompress))
            {
                int read = 0, n;
                while (read < raw.Length && (n = ds.Read(raw, read, raw.Length - read)) > 0) read += n;
                if (read < raw.Length) throw new InvalidDataException("truncated PNG data");
            }

            byte[] prev = new byte[stride], cur = new byte[stride];
            Grid g = new Grid(w, h);
            float maxv = depth == 16 ? 65535f : 255f;
            for (int y = 0; y < h; y++)
            {
                int o = y * (stride + 1);
                int filter = raw[o];
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    int x = raw[o + 1 + i];
                    switch (filter)
                    {
                        case 1: x += a; break;
                        case 2: x += b; break;
                        case 3: x += (a + b) >> 1; break;
                        case 4: x += Paeth(a, b, c); break;
                    }
                    cur[i] = (byte)x;
                }
                for (int px = 0; px < w; px++)
                {
                    int i = px * bpp;
                    float v = depth == 16 ? ((cur[i] << 8) | cur[i + 1]) : cur[i];
                    g.D[y * w + px] = v / maxv;
                }
                byte[] t = prev; prev = cur; cur = t;
            }
            return g;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
        }

        private static int BE32(byte[] b, int o) { return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]; }
    }
}
