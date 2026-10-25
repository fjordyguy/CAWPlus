using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace CawPlus
{
    /// <summary>Colour statistics of a texture, used to judge what kind of ground it shows.</summary>
    public sealed class TextureFeatures
    {
        public float R, G, B;        // mean colour, 0..1
        public float Hue;            // degrees 0..360
        public float Sat, Light;     // 0..1
        public float Detail;         // mean luminance spread inside 4x4 blocks (fine grain: pebbles, cracks)
    }

    /// <summary>
    /// "Smart pick": scores each texture for each paint role from its name, its colours and what a learned style used,
    /// then picks the best texture per role.
    /// </summary>
    public static class TextureAnalyzer
    {
        private static readonly Dictionary<string, TextureFeatures> cache = new Dictionary<string, TextureFeatures>(StringComparer.OrdinalIgnoreCase);

        // Textures that are not natural ground.
        internal static readonly string[] Unnatural = { "road", "asphalt", "concrete", "cement", "cobble", "tile", "wood", "carpet",
            "brick", "paver", "pavement", "paving", "plaza", "boardwalk", "sidewalk", "floor", "stripe", "marble", "metal", "plastic",
            "lino", "flagstone", "parking", "tarmac", "glass" };

        /// <summary>True if a word of the name (split at punctuation, digits and camelCase) starts with a built-surface
        /// word: "05_streetTile_asphalt", "Ground_Pavement", but not "dirtHollywoodHills".</summary>
        public static bool IsUnnatural(string name)
        {
            foreach (string w in Words(name ?? ""))
                foreach (string u in Unnatural) if (w.StartsWith(u)) return true;
            return false;
        }

        private static List<string> Words(string name)
        {
            List<string> words = new List<string>();
            System.Text.StringBuilder cur = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool split = !char.IsLetter(c) || (char.IsUpper(c) && i > 0 && char.IsLower(name[i - 1]));
                if (split && cur.Length > 0) { words.Add(cur.ToString().ToLowerInvariant()); cur.Length = 0; }
                if (char.IsLetter(c)) cur.Append(c);
            }
            if (cur.Length > 0) words.Add(cur.ToString().ToLowerInvariant());
            return words;
        }

        public static TextureFeatures Analyze(string path)
        {
            TextureFeatures f;
            if (cache.TryGetValue(path, out f)) return f;
            f = null;
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".tga") { cache[path] = null; return null; } // GDI+ can't read TGA; judged by name only
                List<float[]> blocks = ext == ".dds" ? Dds.SampleBlocks(path, 4096) : GdiBlocks(path, 64);
                if (blocks != null && blocks.Count > 0) f = Summarize(blocks);
            }
            catch (Exception ex) { Log.Error("Analysing texture " + path + " failed", ex); }
            cache[path] = f;
            return f;
        }

        /// <summary>Each block is 16 RGB texels (48 floats, 0..1).</summary>
        private static TextureFeatures Summarize(List<float[]> blocks)
        {
            double r = 0, g = 0, b = 0, detail = 0;
            foreach (float[] blk in blocks)
            {
                double br = 0, bg = 0, bb = 0, ls = 0, ls2 = 0;
                for (int t = 0; t < 16; t++)
                {
                    float tr = blk[t * 3], tg = blk[t * 3 + 1], tb = blk[t * 3 + 2];
                    br += tr; bg += tg; bb += tb;
                    double lum = 0.299 * tr + 0.587 * tg + 0.114 * tb;
                    ls += lum; ls2 += lum * lum;
                }
                br /= 16; bg /= 16; bb /= 16; ls /= 16; ls2 /= 16;
                r += br; g += bg; b += bb;
                detail += Math.Sqrt(Math.Max(0, ls2 - ls * ls));
            }
            int n = blocks.Count;
            TextureFeatures f = new TextureFeatures();
            f.R = (float)(r / n); f.G = (float)(g / n); f.B = (float)(b / n);
            f.Detail = (float)(detail / n);
            float max = Math.Max(f.R, Math.Max(f.G, f.B)), min = Math.Min(f.R, Math.Min(f.G, f.B));
            f.Light = (max + min) / 2;
            float d = max - min;
            f.Sat = d < 1e-4f ? 0 : d / (1 - Math.Abs(2 * f.Light - 1) + 1e-4f);
            float h;
            if (d < 1e-4f) h = 0;
            else if (max == f.R) h = 60 * (((f.G - f.B) / d) % 6);
            else if (max == f.G) h = 60 * ((f.B - f.R) / d + 2);
            else h = 60 * ((f.R - f.G) / d + 4);
            f.Hue = h < 0 ? h + 360 : h;
            return f;
        }

        private static List<float[]> GdiBlocks(string path, int grid)
        {
            List<float[]> blocks = new List<float[]>();
            using (Bitmap bmp = new Bitmap(path))
            {
                int bw = bmp.Width / 4, bh = bmp.Height / 4;
                if (bw == 0 || bh == 0) return blocks;
                for (int gy = 0; gy < grid; gy++)
                    for (int gx = 0; gx < grid; gx++)
                    {
                        int bx = gx * bw / grid, by = gy * bh / grid;
                        float[] blk = new float[48];
                        for (int t = 0; t < 16; t++)
                        {
                            Color c = bmp.GetPixel(bx * 4 + (t & 3), by * 4 + (t >> 2));
                            blk[t * 3] = c.R / 255f; blk[t * 3 + 1] = c.G / 255f; blk[t * 3 + 2] = c.B / 255f;
                        }
                        blocks.Add(blk);
                    }
            }
            return blocks;
        }

        private static float Band(float v, float lo, float hi, float soft)
        {
            if (v < lo) return Math.Max(0, 1 - (lo - v) / soft);
            if (v > hi) return Math.Max(0, 1 - (v - hi) / soft);
            return 1;
        }

        private static float HueBand(float hue, float lo, float hi, float soft)
        {
            // hue ranges here never wrap past 360
            return Band(hue, lo, hi, soft);
        }

        /// <summary>How well the colours suit a role, 0..1.</summary>
        public static float ColourScore(PaintRole r, TextureFeatures f)
        {
            if (f == null) return 0;
            switch (r)
            {
                case PaintRole.Rock:
                    // grey, and visibly grainy (flat greys are pavement)
                    return Band(f.Sat, 0, 0.1f, 0.08f) * Band(f.Light, 0.25f, 0.7f, 0.15f) * Band(f.Detail, 0.02f, 1, 0.012f);
                case PaintRole.Dirt:
                    return HueBand(f.Hue, 12, 45, 15) * Band(f.Sat, 0.12f, 0.6f, 0.12f) * Band(f.Light, 0.15f, 0.5f, 0.12f);
                case PaintRole.Shore:
                    return HueBand(f.Hue, 25, 55, 15) * Band(f.Sat, 0.1f, 0.38f, 0.1f) * Band(f.Light, 0.4f, 0.85f, 0.12f);
                default:
                    return HueBand(f.Hue, 55, 140, 20) * Band(f.Sat, 0.15f, 1, 0.1f);
            }
        }

        /// <summary>
        /// Total score for using 'path' (or an existing layer named 'name') for role r. Higher is better; below 0 = unsuitable.
        /// 'styleTextures' = textures the learned style used for this role; 'eaTextures' = textures EA's worlds use for
        /// this role (from the built-in EA styles); 'baseF' = the base layer's colours.
        /// </summary>
        public static float Score(PaintRole r, string name, TextureFeatures f, ICollection<string> styleTextures, TextureFeatures baseF,
                                  ICollection<string> eaTextures = null)
        {
            string n = name.ToLowerInvariant();
            if (IsUnnatural(name)) return -1;
            float s = 0;
            if (TextureLibrary.Matches(r, n)) s += 0.5f;
            if (Listed(styleTextures, name)) s += 0.8f;
            else if (Listed(eaTextures, name)) s += 0.4f;
            if (f != null)
            {
                s += ColourScore(r, f);
                if (baseF != null)
                {
                    // Layers should sit close to the base in tone so they blend; meadow grass most of all.
                    float dl = Math.Abs(f.Light - baseF.Light);
                    float dh = Math.Abs(f.Hue - baseF.Hue); if (dh > 180) dh = 360 - dh;
                    if (r == PaintRole.Meadow) s += 0.6f * Band(dl, 0, 0.08f, 0.12f) * Band(dh, 0, 15, 25) - (baseF != null && dl < 0.01f && dh < 2 ? 0.4f : 0);
                    else s += 0.2f * Band(dl, 0, 0.2f, 0.2f);
                }
            }
            return s;
        }

        /// <summary>True if 'name' (a texture file, or an existing layer shown as "name type") is one of 'textures'.</summary>
        private static bool Listed(ICollection<string> textures, string name)
        {
            if (textures == null) return false;
            string n = Path.GetFileNameWithoutExtension(name);
            foreach (string t in textures)
            {
                string b = Path.GetFileNameWithoutExtension(t);
                if (string.Equals(b, n, StringComparison.OrdinalIgnoreCase) || name.StartsWith(b + " ", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static string Describe(TextureFeatures f)
        {
            if (f == null) return "colours unreadable";
            return string.Format("hue {0:F0}, sat {1:F2}, light {2:F2}, detail {3:F3}", f.Hue, f.Sat, f.Light, f.Detail);
        }
    }

    /// <summary>Minimal DDS reader: DXT1/DXT3/DXT5 and 24/32-bit uncompressed, top mip only, sampled blocks.</summary>
    public static class Dds
    {
        public static List<float[]> SampleBlocks(string path, int maxBlocks)
        {
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < 128 || d[0] != 'D' || d[1] != 'D' || d[2] != 'S') return null;
            int h = BitConverter.ToInt32(d, 12), w = BitConverter.ToInt32(d, 16);
            int pfFlags = BitConverter.ToInt32(d, 80);
            string fourCC = System.Text.Encoding.ASCII.GetString(d, 84, 4);
            int bits = BitConverter.ToInt32(d, 88);
            uint rMask = BitConverter.ToUInt32(d, 92), gMask = BitConverter.ToUInt32(d, 96), bMask = BitConverter.ToUInt32(d, 100);
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            int step = Math.Max(1, (int)Math.Sqrt((double)bw * bh / maxBlocks));
            List<float[]> blocks = new List<float[]>();
            bool compressed = (pfFlags & 4) != 0;
            int blockBytes = fourCC == "DXT1" ? 8 : 16;
            for (int by = 0; by < bh; by += step)
                for (int bx = 0; bx < bw; bx += step)
                {
                    float[] blk = new float[48];
                    if (compressed)
                    {
                        if (fourCC != "DXT1" && fourCC != "DXT3" && fourCC != "DXT5") return null;
                        int o = 128 + (by * bw + bx) * blockBytes + (blockBytes == 16 ? 8 : 0);
                        if (o + 8 > d.Length) continue;
                        DecodeColourBlock(d, o, blk, blockBytes == 8);
                    }
                    else
                    {
                        int bpp = bits / 8;
                        if (bpp < 3) return null;
                        for (int t = 0; t < 16; t++)
                        {
                            int x = Math.Min(w - 1, bx * 4 + (t & 3)), y = Math.Min(h - 1, by * 4 + (t >> 2));
                            int o = 128 + (y * w + x) * bpp;
                            if (o + bpp > d.Length) continue;
                            uint px = bpp == 4 ? BitConverter.ToUInt32(d, o) : (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16));
                            blk[t * 3] = Channel(px, rMask == 0 ? 0xFF0000u : rMask);
                            blk[t * 3 + 1] = Channel(px, gMask == 0 ? 0xFF00u : gMask);
                            blk[t * 3 + 2] = Channel(px, bMask == 0 ? 0xFFu : bMask);
                        }
                    }
                    blocks.Add(blk);
                }
            return blocks;
        }

        /// <summary>
        /// Decodes the top mip to 0xRRGGBB pixels (DXT1/3/5 or 24/32-bit). Larger than 'maxSize' is reduced by
        /// taking every Nth texel. Returns null for other formats.
        /// </summary>
        public static int[] Decode(string path, int maxSize, out int outW, out int outH)
        {
            outW = outH = 0;
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < 128 || d[0] != 'D' || d[1] != 'D' || d[2] != 'S') return null;
            int h = BitConverter.ToInt32(d, 12), w = BitConverter.ToInt32(d, 16);
            int pfFlags = BitConverter.ToInt32(d, 80);
            string fourCC = System.Text.Encoding.ASCII.GetString(d, 84, 4);
            int bits = BitConverter.ToInt32(d, 88);
            uint rMask = BitConverter.ToUInt32(d, 92), gMask = BitConverter.ToUInt32(d, 96), bMask = BitConverter.ToUInt32(d, 100);
            int step = 1; while (w / step > maxSize || h / step > maxSize) step *= 2;
            outW = w / step; outH = h / step;
            int[] px = new int[outW * outH];
            bool compressed = (pfFlags & 4) != 0;
            if (compressed && fourCC != "DXT1" && fourCC != "DXT3" && fourCC != "DXT5") return null;
            if (!compressed && bits / 8 < 3) return null;
            int bw = Math.Max(1, (w + 3) / 4), blockBytes = fourCC == "DXT1" ? 8 : 16;
            float[] blk = new float[48];
            int lastBlock = -1;
            for (int y = 0; y < outH; y++)
                for (int x = 0; x < outW; x++)
                {
                    int sx = x * step, sy = y * step;
                    float r, g, b;
                    if (compressed)
                    {
                        int bi = (sy / 4) * bw + sx / 4;
                        if (bi != lastBlock)
                        {
                            int o = 128 + bi * blockBytes + (blockBytes == 16 ? 8 : 0);
                            if (o + 8 > d.Length) return null;
                            DecodeColourBlock(d, o, blk, blockBytes == 8);
                            lastBlock = bi;
                        }
                        int t = (sy & 3) * 4 + (sx & 3);
                        r = blk[t * 3]; g = blk[t * 3 + 1]; b = blk[t * 3 + 2];
                    }
                    else
                    {
                        int bpp = bits / 8, o = 128 + (sy * w + sx) * bpp;
                        if (o + bpp > d.Length) return null;
                        uint p = bpp == 4 ? BitConverter.ToUInt32(d, o) : (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16));
                        r = Channel(p, rMask == 0 ? 0xFF0000u : rMask); g = Channel(p, gMask == 0 ? 0xFF00u : gMask); b = Channel(p, bMask == 0 ? 0xFFu : bMask);
                    }
                    px[y * outW + x] = ((int)(r * 255 + 0.5f) << 16) | ((int)(g * 255 + 0.5f) << 8) | (int)(b * 255 + 0.5f);
                }
            return px;
        }

        private static float Channel(uint px, uint mask)
        {
            int shift = 0; while (shift < 32 && ((mask >> shift) & 1) == 0) shift++;
            uint max = mask >> shift;
            return max == 0 ? 0 : ((px & mask) >> shift) / (float)max;
        }

        private static void DecodeColourBlock(byte[] d, int o, float[] blk, bool dxt1)
        {
            ushort c0 = BitConverter.ToUInt16(d, o), c1 = BitConverter.ToUInt16(d, o + 2);
            uint idx = BitConverter.ToUInt32(d, o + 4);
            float[] pal = new float[12];
            Rgb565(c0, pal, 0); Rgb565(c1, pal, 3);
            bool fourColour = !dxt1 || c0 > c1;
            for (int k = 0; k < 3; k++)
            {
                if (fourColour)
                {
                    pal[6 + k] = (2 * pal[k] + pal[3 + k]) / 3;
                    pal[9 + k] = (pal[k] + 2 * pal[3 + k]) / 3;
                }
                else
                {
                    pal[6 + k] = (pal[k] + pal[3 + k]) / 2;
                    pal[9 + k] = pal[6 + k]; // transparent texel; treat as the mid colour
                }
            }
            for (int t = 0; t < 16; t++)
            {
                int s = (int)((idx >> (2 * t)) & 3);
                blk[t * 3] = pal[s * 3]; blk[t * 3 + 1] = pal[s * 3 + 1]; blk[t * 3 + 2] = pal[s * 3 + 2];
            }
        }

        private static void Rgb565(ushort c, float[] p, int o)
        {
            p[o] = ((c >> 11) & 31) / 31f; p[o + 1] = ((c >> 5) & 63) / 63f; p[o + 2] = (c & 31) / 31f;
        }
    }
}
